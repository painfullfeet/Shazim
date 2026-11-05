using System.Windows;
using Shazim.App.Infrastructure;
using Shazim.Core.Logging;
using Shazim.Core.Models;
using Shazim.Core.Recognition;

namespace Shazim.App.Services;

/// <summary>
/// Runs manual identifications for every entry point (big button, mini player,
/// tray menu, hotkey) and exposes their state to the UI. All members are used on the UI thread.
/// </summary>
public sealed class IdentifyController : ObservableObject
{
    private readonly IdentificationService _identification;
    private readonly SettingsService _settings;
    private readonly ILogger<IdentifyController> _logger;
    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private IdentifyPhase _phase;
    private double _progress;
    private float _level;
    private IdentifyOutcome? _lastOutcome;

    public IdentifyController(IdentificationService identification, SettingsService settings, ILogger<IdentifyController> logger)
    {
        _identification = identification;
        _settings = settings;
        _logger = logger;
    }

    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public IdentifyPhase Phase { get => _phase; private set => Set(ref _phase, value); }

    /// <summary>0..1 share of the maximum listening time elapsed.</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public float Level { get => _level; private set => Set(ref _level, value); }
    public IdentifyOutcome? LastOutcome { get => _lastOutcome; private set => Set(ref _lastOutcome, value); }

    /// <summary>Raised when an identification ends (match, no match, error; not on cancel).</summary>
    public event EventHandler<IdentifyOutcome>? Completed;

    public Task ToggleAsync() => IsBusy ? CancelAsync() : StartAsync();

    public Task CancelAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public async Task StartAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Phase = IdentifyPhase.Listening;
        Progress = 0;
        Level = 0;
        _cts = new CancellationTokenSource();

        var cfg = _settings.GetSettings();
        var progress = new Progress<IdentifyProgress>(p =>
        {
            if (!IsBusy) return; // late report after completion
            Phase = p.Phase;
            Progress = Math.Clamp(p.Elapsed.TotalMilliseconds / Math.Max(1, p.MaxDuration.TotalMilliseconds), 0, 1);
            Level = p.Level;
        });

        try
        {
            var outcome = await _identification.IdentifyAsync(
                new IdentifyRequest(cfg.Audio.DeviceIds(), IsAuto: false, cfg.Audio.CaptureDurationMs, cfg.AutoShazam.DuplicateWindowMinutes),
                progress, _cts.Token);

            if (outcome.Kind != IdentifyOutcomeKind.Cancelled)
            {
                LastOutcome = outcome;
                Completed?.Invoke(this, outcome);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual identification failed");
            var outcome = new IdentifyOutcome { Kind = IdentifyOutcomeKind.Error, Message = Infrastructure.Loc.T("identify.unexpected") };
            LastOutcome = outcome;
            Completed?.Invoke(this, outcome);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            Level = 0;
            Progress = 0;
            IsBusy = false;
        }
    }

    /// <summary>Shows a result found by Auto Shazim on the home screen.</summary>
    public void ShowExternal(IdentifyOutcome outcome)
    {
        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            Application.Current.Dispatcher.BeginInvoke(() => ShowExternal(outcome));
            return;
        }
        if (IsBusy) return;
        LastOutcome = outcome;
    }

    public Track? LastTrack => LastOutcome?.Track;
}
