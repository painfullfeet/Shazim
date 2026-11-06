using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;
using Shazim.App.Infrastructure;
using Shazim.Core.Models;

namespace Shazim.App.Services;

/// <summary>
/// Plays the 30-second preview of a track (streamed with Media Foundation, which accepts
/// Apple's "audio/x-m4p" previews that the WPF MediaPlayer rejects). One player for the
/// whole app: starting a preview anywhere stops the previous one. Members are used on the UI thread.
/// </summary>
public sealed class PreviewPlayer : ObservableObject, IDisposable
{
    private static readonly Core.Logging.ILogger<PreviewPlayer> Log = new Core.Services.FileLogger<PreviewPlayer>();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private MediaFoundationReader? _reader;
    private WasapiOut? _output;
    private int _generation;
    private string? _trackId;
    private bool _isPlaying;
    private bool _isLoading;
    private double _progress;
    private string? _errorTrackId;
    private readonly SettingsService _settings;
    private VolumeSampleProvider? _volumeStage;
    private double _volume;

    public PreviewPlayer(SettingsService settings)
    {
        _settings = settings;
        _volume = settings.GetSettings().General.PreviewVolume;
        _timer.Tick += (_, _) =>
        {
            var reader = _reader;
            if (reader == null) return;
            var total = reader.TotalTime.TotalSeconds;
            Progress = total > 0 ? Math.Clamp(reader.CurrentTime.TotalSeconds / total, 0, 1) : 0;
        };
    }

    /// <summary>Id of the track being played, or null.</summary>
    public string? TrackId { get => _trackId; private set => Set(ref _trackId, value); }
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    /// <summary>Preview volume, 0..1 (applied live, saved in the settings).</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (!Set(ref _volume, value)) return;
            var stage = _volumeStage;
            if (stage != null) stage.Volume = (float)(value * value); // perceptual curve: fine control at low levels
            _settings.GetSettings().General.PreviewVolume = value;
        }
    }

    /// <summary>Saves the volume (called when the user lets go of the slider).</summary>
    public void SaveVolume() => _settings.Save();

    /// <summary>Track whose preview could not be played last.</summary>
    public string? ErrorTrackId { get => _errorTrackId; private set => Set(ref _errorTrackId, value); }

    public bool IsPlayingTrack(string? id) => IsPlaying && id != null && id == TrackId;

    /// <summary>Plays the track's preview, or stops it if it is the one playing.</summary>
    public void Toggle(Track track)
    {
        if (IsPlayingTrack(track.Id)) { Stop(); return; }
        Stop();
        if (!track.HasPreview) { ErrorTrackId = track.Id; return; }

        int generation = _generation;
        ErrorTrackId = null;
        TrackId = track.Id;
        IsPlaying = true;
        IsLoading = true;
        // Only web previews: a stored path or another scheme is never opened.
        var url = Core.Security.LinkPolicy.Sanitize(track.PreviewUrl) is { } safe && safe.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? track.PreviewUrl.Trim() : null;
        if (url == null) { IsLoading = false; IsPlaying = false; TrackId = null; ErrorTrackId = track.Id; return; }
        var dispatcher = Application.Current.Dispatcher;

        Task.Run(() =>
        {
            // Network and COM work stay off the UI thread; WASAPI objects live on the thread pool (MTA).
            var reader = new MediaFoundationReader(url);
            var stage = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = (float)(_volume * _volume) };
            _volumeStage = stage;
            var output = new WasapiOut(AudioClientShareMode.Shared, 200);
            output.Init(new SampleToWaveProvider(stage));
            output.PlaybackStopped += (_, e) => dispatcher.BeginInvoke(() =>
            {
                if (e.Exception != null) Log.LogError(e.Exception, "Preview stopped with an error");
                if (generation == _generation) Stop();
            });
            output.Play();
            Log.LogDebug("Preview playing ({Format}, state {State})", reader.WaveFormat.ToString(), output.PlaybackState.ToString());
            return (reader, output);
        }).ContinueWith(t => dispatcher.BeginInvoke(() =>
        {
            if (t.IsFaulted)
            {
                Log.LogError(t.Exception?.GetBaseException(), "Preview failed");
                if (generation == _generation) { Stop(); ErrorTrackId = track.Id; }
                return;
            }
            if (generation != _generation)
            {
                Release(t.Result.output, t.Result.reader); // stopped or replaced while loading
                return;
            }
            (_reader, _output) = t.Result;
            IsLoading = false;
            _timer.Start();
        }));
    }

    private static void Release(WasapiOut? output, MediaFoundationReader? reader)
    {
        if (output == null && reader == null) return;
        // Disposing waits for the audio thread: do it in the background.
        Task.Run(() =>
        {
            try { output?.Stop(); output?.Dispose(); } catch { }
            try { reader?.Dispose(); } catch { }
        });
    }

    public void Stop()
    {
        _generation++;
        _timer.Stop();
        Release(_output, _reader);
        _output = null;
        _reader = null;
        IsPlaying = false;
        IsLoading = false;
        Progress = 0;
        TrackId = null;
    }

    public void Dispose() => Stop();
}
