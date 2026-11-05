using Microsoft.Extensions.Hosting;
using Shazim.Core.Logging;
using Shazim.Core.Models;
using Shazim.Core.Recognition;

namespace Shazim.App.Services;

public enum AutoShazamState { Off, Listening, Searching, Waiting }

/// <summary>
/// Background loop: while enabled, listens, identifies, records new tracks, then waits.
/// Keeps running while the window is hidden in the notification area.
/// </summary>
public sealed class AutoShazamService : BackgroundService
{
    private readonly IdentificationService _identification;
    private readonly SettingsService _settings;
    private readonly ILogger<AutoShazamService> _logger;
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private CancellationTokenSource? _cycleCts;
    private volatile bool _enabled;
    private int _consecutiveErrors;

    public AutoShazamService(IdentificationService identification, SettingsService settings, ILogger<AutoShazamService> logger)
    {
        _identification = identification;
        _settings = settings;
        _logger = logger;
        _enabled = settings.GetSettings().AutoShazam.AutoStart;
        settings.GetSettings().AutoShazam.Enabled = _enabled;
    }

    public bool IsEnabled => _enabled;
    public AutoShazamState State { get; private set; } = AutoShazamState.Off;
    // Kept as a function so the text follows a language change.
    private Func<string> _status = () => Loc.T("mainwin.desactive");
    public string StatusText => _status();
    public Track? LastTrack { get; private set; }

    /// <summary>Raised (worker thread) when state or status text changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Raised (worker thread) when a new, non-duplicate track is detected.</summary>
    public event EventHandler<IdentifyOutcome>? TrackDetected;

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        _settings.GetSettings().AutoShazam.Enabled = enabled;
        _logger.LogInformation("Auto Shazim {State}", enabled ? "enabled" : "disabled");

        if (!enabled) _cycleCts?.Cancel();
        _wake.Release();
        SetStatus(enabled ? AutoShazamState.Waiting : AutoShazamState.Off, () => Loc.T(enabled ? "auto.starting" : "mainwin.desactive"));
    }

    public void Toggle() => SetEnabled(!_enabled);

    private void SetStatus(AutoShazamState state, Func<string> text)
    {
        State = state;
        _status = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    // Run the loop on the thread pool, not on the UI thread that starts the host.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() => RunAsync(stoppingToken), stoppingToken);

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        if (_enabled) SetStatus(AutoShazamState.Waiting, () => Loc.T("auto.starting"));

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_enabled)
            {
                try { await _wake.WaitAsync(stoppingToken); } catch (OperationCanceledException) { break; }
                continue;
            }

            var cfg = _settings.GetSettings();
            TimeSpan wait = TimeSpan.FromMilliseconds(cfg.AutoShazam.IntervalMs);

            _cycleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            try
            {
                var progress = new Progress<IdentifyProgress>(p =>
                {
                    if (p.Phase == IdentifyPhase.Searching && State != AutoShazamState.Searching)
                        SetStatus(AutoShazamState.Searching, () => Loc.T("home.searching"));
                });
                SetStatus(AutoShazamState.Listening, () => Loc.T("home.listening"));

                var outcome = await _identification.IdentifyAsync(
                    new IdentifyRequest(cfg.Audio.DeviceIds(), IsAuto: true, cfg.AutoShazam.CaptureDurationMs, cfg.AutoShazam.DuplicateWindowMinutes),
                    progress, _cycleCts.Token);

                switch (outcome.Kind)
                {
                    case IdentifyOutcomeKind.Match:
                        _consecutiveErrors = 0;
                        var track = outcome.Track!;
                        LastTrack = track;
                        if (!outcome.IsDuplicate) TrackDetected?.Invoke(this, outcome);
                        SetStatus(AutoShazamState.Waiting, () => Loc.F("auto.last", track.DisplayArtist, track.DisplayTitle));
                        break;
                    case IdentifyOutcomeKind.Silence:
                        _consecutiveErrors = 0;
                        wait = TimeSpan.FromSeconds(Math.Min(10, wait.TotalSeconds)); // check again soon
                        SetStatus(AutoShazamState.Waiting, () => Loc.T("auto.waiting_music"));
                        break;
                    case IdentifyOutcomeKind.NoMatch:
                        _consecutiveErrors = 0;
                        SetStatus(AutoShazamState.Waiting, () => Loc.T("auto.nothing"));
                        break;
                    case IdentifyOutcomeKind.Error:
                        _consecutiveErrors++;
                        // Back off progressively on repeated errors (network down, rate limit...).
                        wait = TimeSpan.FromSeconds(Math.Min(300, wait.TotalSeconds * Math.Pow(2, Math.Min(_consecutiveErrors, 4))));
                        var message = outcome.Message;
                        SetStatus(AutoShazamState.Waiting, () => message);
                        break;
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                continue; // disabled during a cycle
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto Shazim cycle failed");
                SetStatus(AutoShazamState.Waiting, () => Loc.T("auto.error"));
            }
            finally
            {
                _cycleCts.Dispose();
                _cycleCts = null;
            }

            if (!_enabled) continue;
            try
            {
                // Waiting can be interrupted by disabling/enabling.
                await _wake.WaitAsync(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        SetStatus(AutoShazamState.Off, () => Loc.T("mainwin.desactive"));
    }
}
