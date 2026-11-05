using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shazim.App.Services;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;
using Shazim.Core.Recognition;
using Shazim.Core.Services;
using Shazim.Data.Repositories;

namespace Shazim.App;

public partial class App : Application
{
    private const string MutexName = "Shazim.SingleInstance";
    private const string ShowEventName = "Shazim.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private IHost? _host;
    private TrayService? _tray;
    private HotkeyService? _hotkeys;
    private MainWindow? _main;
    private MiniWindow? _mini;
    private ILogger<App> _logger = new FileLogger<App>();
    private bool _exiting;

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not started");
    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Infrastructure.DevSnapshot.Start(Dispatcher);

        // The same exe is also the installer (Shazim-Setup*.exe / --setup) and the uninstaller (--uninstall).
        var setupMode = Setup.Installer.ModeFor(e.Args);
        if (setupMode != Setup.SetupMode.None)
        {
            Infrastructure.Loc.Instance.SetLanguage("");
            ThemeService.Apply(null);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            DispatcherUnhandledException += (_, args) =>
            {
                args.Handled = true;
                MessageBox.Show(args.Exception.Message, "Shazim", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            bool quiet = e.Args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
            MainWindow = new Setup.SetupWindow(setupMode, quiet);
            MainWindow.Show();
            return;
        }

        Core.AppPaths.MigrateLegacyData();

        // Single instance: a second launch just brings the existing window forward.
        // A custom data folder (SHAZIM_DATA_DIR) is a separate profile with its own instance.
        string suffix = Core.AppPaths.IsCustomDataDirectory
            ? "." + (uint)Core.AppPaths.DataDirectory.ToLowerInvariant().GetHashCode() : "";
        _mutex = new Mutex(true, MutexName + suffix, out bool isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName + suffix).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + suffix);
        new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (_exiting) break;
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        }) { IsBackground = true, Name = "SingleInstanceListener" }.Start();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger.LogError(args.ExceptionObject as Exception, "Unhandled exception (terminating: {T})", args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger.LogError(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        try
        {
            _host = BuildHost();
            _logger.LogDebug("Starting host");
            _host.Start();
            _logger.LogDebug("Host started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup failed");
            MessageBox.Show(Loc.T("app.start_failed") + "\n" + ex.Message, "Shazim",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var settings = Services.GetRequiredService<SettingsService>();
        Core.CoreText.Resolver = key => Infrastructure.Loc.Instance.TryGet(key);
        Infrastructure.Loc.Instance.SetLanguage(settings.GetSettings().General.Language);
        ThemeService.Apply(settings.GetSettings().General.Accent);
        var auto = Services.GetRequiredService<AutoShazamService>();
        var controller = Services.GetRequiredService<IdentifyController>();

        _logger.LogDebug("Creating main window");
        _main = new MainWindow(Services);
        _logger.LogDebug("Main window created");

        CreateTray();

        auto.StatusChanged += (_, _) => Dispatcher.BeginInvoke(() => _tray?.SetAutoShazam(auto.IsEnabled, auto.StatusText));
        auto.TrackDetected += (_, outcome) => Dispatcher.BeginInvoke(() =>
        {
            controller.ShowExternal(outcome);
            NotifyTrack(outcome, isAuto: true);
        });
        controller.Completed += (_, outcome) => NotifyTrack(outcome, isAuto: false);

        _hotkeys = new HotkeyService();
        ApplyHotkeys();
        settings.Saved += (_, _) => Dispatcher.BeginInvoke(ApplyHotkeys);

        // Keep the Windows startup entry in sync with the setting.
        if (!Core.AppPaths.IsCustomDataDirectory)
        try { StartupService.Apply(settings.GetSettings().General.StartWithWindows); }
        catch (Exception ex) { _logger.LogWarning("Startup entry not updated: {Message}", ex.Message); }

        bool launchedAtLogin = e.Args.Contains(StartupService.MinimizedArg, StringComparer.OrdinalIgnoreCase);
        if (launchedAtLogin && settings.GetSettings().General.StartMinimized)
            _logger.LogInformation("Started minimized in the notification area");
        else
            ShowMainWindow();

        _logger.LogInformation("Shazim started");
    }

    private void CreateTray()
    {
        var auto = Services.GetRequiredService<AutoShazamService>();
        var controller = Services.GetRequiredService<IdentifyController>();
        _tray?.Dispose();
        _tray = new TrayService(
            open: ShowMainWindow,
            identify: () => _ = controller.StartAsync(),
            toggleAuto: auto.Toggle,
            openMini: ShowMiniWindow,
            exit: ExitApp);
        _tray.SetAutoShazam(auto.IsEnabled, auto.StatusText);
    }

    /// <summary>Switches the UI language live: bindings update, code-built views are rebuilt.</summary>
    public void ApplyLanguage(string code)
    {
        Infrastructure.Loc.Instance.SetLanguage(code);
        _main?.RebuildViews();
        CreateTray();
        _mini?.Close();
    }

    private IHost BuildHost()
    {
        Directory.CreateDirectory(Core.AppPaths.DataDirectory);
        var dbPath = Core.AppPaths.DatabaseFile;

        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(typeof(ILogger<>), typeof(FileLogger<>));

                services.AddSingleton<SettingsService>();
                services.AddSingleton<IAppHttpClientFactory, SharedHttpClientFactory>();

                services.AddSingleton<ITrackRepository>(sp =>
                    SqliteTrackRepository.ForFile(dbPath, sp.GetRequiredService<ILogger<SqliteTrackRepository>>()));
                services.AddSingleton<LibraryService>();
                services.AddSingleton<DeduplicationService>();

                services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
                services.AddSingleton<IMusicRecognitionService, ShazamRecognitionService>();
                services.AddSingleton<IdentificationService>();

                services.AddSingleton<IdentifyController>();
                services.AddSingleton<PreviewPlayer>();
                services.AddSingleton<MusicVisualizer>();
                services.AddSingleton<AutoShazamService>();
                services.AddHostedService(sp => sp.GetRequiredService<AutoShazamService>());
            })
            .Build();
    }

    private void ApplyHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.UnregisterAll();
        var hk = Services.GetRequiredService<SettingsService>().GetSettings().Hotkeys;
        if (!hk.Enabled) return;

        var controller = Services.GetRequiredService<IdentifyController>();
        var auto = Services.GetRequiredService<AutoShazamService>();
        if (!_hotkeys.Register(hk.Identify, () => _ = controller.StartAsync()))
            _logger.LogWarning("Hotkey {Gesture} unavailable", hk.Identify);
        if (!_hotkeys.Register(hk.ToggleAuto, auto.Toggle))
            _logger.LogWarning("Hotkey {Gesture} unavailable", hk.ToggleAuto);
    }

    private void NotifyTrack(Core.Recognition.IdentifyOutcome outcome, bool isAuto)
    {
        var n = Services.GetRequiredService<SettingsService>().GetSettings().Notifications;
        if (!n.Enabled || (n.AutoOnly && !isAuto)) return;
        if (outcome.Kind != Core.Recognition.IdentifyOutcomeKind.Match || outcome.Track == null) return;

        // Manual results are already on screen when the window is in front.
        if (!isAuto && _main is { IsVisible: true, IsActive: true }) return;

        _tray?.Notify(outcome.Track.DisplayTitle, outcome.Track.DisplayArtist + (isAuto ? "  ·  " + Loc.T("mainwin.auto_shazim") : ""), ShowMainWindow);
    }

    public void ShowMainWindow()
    {
        if (_main == null) return;
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        _main.Topmost = true;  // reliably bring to front
        _main.Topmost = false;
        _main.Focus();
    }

    public void ShowMiniWindow()
    {
        if (_mini == null)
        {
            _mini = new MiniWindow(Services);
            _mini.Closed += (_, _) => _mini = null;
        }
        _mini.Show();
        _mini.Activate();
    }

    /// <summary>Called when the main window is closed by the user.</summary>
    public void OnMainWindowClosing()
    {
        var settings = Services.GetRequiredService<SettingsService>().GetSettings();
        if (!settings.General.CloseToTray) Dispatcher.BeginInvoke(ExitApp);
    }

    public bool IsExiting => _exiting;

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _showEvent?.Set();
        _mini?.Close();
        _main?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        try
        {
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _host?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shutdown error");
        }
        _showEvent?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "UI exception");
        e.Handled = true;
        MessageBox.Show(Loc.F("app.ui_error", e.Exception.Message),
            "Shazim", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

/// <summary>One HttpClient for the whole app, with connection recycling.</summary>
public sealed class SharedHttpClientFactory : IAppHttpClientFactory, IDisposable
{
    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(8)
    })
    {
        Timeout = Timeout.InfiniteTimeSpan // per-request timeouts are set by each service
    };

    public HttpClient CreateClient() => _client;
    public void Dispose() => _client.Dispose();
}
