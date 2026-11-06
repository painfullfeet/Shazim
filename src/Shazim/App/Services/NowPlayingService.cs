using Shazim.Core.Lyrics;
using Shazim.Core.Models;
using Shazim.Core.Recognition;

namespace Shazim.App.Services;

/// <summary>
/// The track playing right now (last match with a Shazam offset), where it is in the song,
/// and its lyrics. Shared by the track page and the widget. UI thread only.
/// </summary>
public sealed class NowPlayingService
{
    /// <summary>When the length of the song is unknown, lyrics stop following after this.</summary>
    private static readonly TimeSpan DefaultLength = TimeSpan.FromMinutes(7);

    private readonly LyricsService _lyrics;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _cts;
    private int _version;

    public NowPlayingService(LyricsService lyrics, SettingsService settings, IdentifyController controller, AutoShazamService auto)
    {
        _lyrics = lyrics;
        _settings = settings;
        controller.Completed += (_, o) => OnMatch(o);
        auto.Matched += (_, o) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => OnMatch(o));
    }

    public Track? Track { get; private set; }
    public PlaybackAnchor? Anchor { get; private set; }
    public TrackLyrics? Lyrics { get; private set; }

    /// <summary>Correction chosen with the "±" buttons, in seconds.</summary>
    public double Nudge { get; private set; }

    /// <summary>Raised when the track, its lyrics or the nudge changed.</summary>
    public event EventHandler? Changed;

    private void OnMatch(IdentifyOutcome o)
    {
        if (o.Kind != IdentifyOutcomeKind.Match || o.Track == null) return;
        bool sameTrack = Track?.Id == o.Track.Id;
        Track = o.Track;
        if (o.Anchor is { } a) Anchor = a;
        else if (!sameTrack) Anchor = null;
        if (!sameTrack)
        {
            Nudge = 0;
            Lyrics = null;
            _ = LoadLyricsAsync(o.Track);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadLyricsAsync(Track track)
    {
        _cts?.Cancel();
        if (!_settings.GetSettings().Lyrics.Enabled) return;
        var cts = _cts = new CancellationTokenSource();
        int version = ++_version;
        try
        {
            var lyrics = await _lyrics.GetAsync(track, cts.Token);
            if (version != _version || Track?.Id != track.Id) return;
            Lyrics = lyrics;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Lyrics of any track (cached), for the track page of older tracks.</summary>
    public Task<TrackLyrics?> GetLyricsAsync(Track track, CancellationToken ct = default) =>
        _settings.GetSettings().Lyrics.Enabled ? _lyrics.GetAsync(track, ct) : Task.FromResult<TrackLyrics?>(null);

    /// <summary>True when <paramref name="trackId"/> is the song playing now (found recently, not over yet).</summary>
    public bool IsLive(string? trackId, DateTime utcNow) =>
        trackId != null && Track?.Id == trackId && Anchor is { } a && Position(utcNow) is { } p && p <= Length(a) ;

    private TimeSpan Length(PlaybackAnchor a)
    {
        double seconds = Lyrics?.DurationSeconds > 0 ? Lyrics.DurationSeconds : (Track?.DurationMs ?? 0) / 1000.0;
        return seconds > 0 ? TimeSpan.FromSeconds(seconds + 3) : DefaultLength;
    }

    /// <summary>Position in the song now (with the nudge), or null without a Shazam offset.</summary>
    public TimeSpan? Position(DateTime utcNow) => Anchor?.PositionAt(utcNow, Nudge);

    /// <summary>Line being sung now, or -1.</summary>
    public int CurrentLine(DateTime utcNow) =>
        Lyrics is { IsSynced: true } l && Track != null && IsLive(Track.Id, utcNow) && Position(utcNow) is { } p ? l.IndexAt(p) : -1;

    /// <summary>Developer snapshots: pretend a track is playing with these lyrics.</summary>
    public void Simulate(Track track, PlaybackAnchor anchor, TrackLyrics? lyrics)
    {
        Track = track;
        Anchor = anchor;
        Lyrics = lyrics;
        Nudge = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddNudge(double seconds)
    {
        Nudge = Math.Clamp(Nudge + seconds, -30, 30);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
