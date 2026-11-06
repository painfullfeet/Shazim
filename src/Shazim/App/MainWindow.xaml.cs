using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Shazim.App.Infrastructure;
using Shazim.App.Services;
using Shazim.App.Views;

namespace Shazim.App;

public partial class MainWindow : Window
{
    private const double RailWide = 240, RailNarrow = 76;

    private readonly IServiceProvider _services;
    private readonly AutoShazamService _auto;
    private readonly SettingsService _settings;
    private readonly Dictionary<string, UserControl> _views = new();
    private readonly Dictionary<RadioButton, string> _navKeys = new();
    private bool _collapsed;
    private Storyboard? _autoPulse;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        _auto = services.GetRequiredService<AutoShazamService>();
        _settings = services.GetRequiredService<SettingsService>();
        InitializeComponent();

        _navKeys[NavHome] = "mainwin.ecouter";
        _navKeys[NavLibrary] = "mainwin.bibliotheque";
        _navKeys[NavSettings] = "mainwin.reglages";

        BrandLogo.Source = Brand.Logo;
        _auto.StatusChanged += (_, _) => Dispatcher.BeginInvoke(UpdateAutoStatus);
        UpdateAutoStatus();
        StateChanged += (_, _) => UpdateMaximizeState();

        SetSidebar(_settings.GetSettings().General.SidebarCollapsed, animate: false);
        Navigate("home", animate: false);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ===== Navigation =====

    public void Navigate(string key) => Navigate(key, animate: true);

    private void Navigate(string key, bool animate)
    {
        if (!_views.TryGetValue(key, out var view))
        {
            view = key switch
            {
                "library" => new LibraryView(_services),
                "settings" => new SettingsView(_services),
                _ => new HomeView(_services)
            };
            _views[key] = view;
        }

        bool changed = Host.Content != view;
        Host.Content = view;
        if (view is IRefreshable r) _ = r.RefreshAsync();

        // Short rise-and-fade so the change of page reads as movement, not a flash.
        if (changed && animate && !Shell.ReduceMotion)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Host.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            HostShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        var nav = key switch
        {
            "library" => NavLibrary,
            "settings" => NavSettings,
            _ => NavHome
        };
        if (nav.IsChecked != true) nav.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        var key = sender == NavLibrary ? "library" : sender == NavSettings ? "settings" : "home";
        if (Host.Content != (_views.TryGetValue(key, out var v) ? v : null))
            Navigate(key);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DialogLayer.Visibility == Visibility.Visible)
        {
            CloseTrack();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F11)
        {
            ToggleFullView();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.B)
        {
            SetSidebar(!_collapsed, animate: true);
            e.Handled = true;
            return;
        }
        string? key = e.Key switch
        {
            Key.D1 or Key.NumPad1 => "home",
            Key.D2 or Key.NumPad2 => "library",
            Key.D3 or Key.NumPad3 => "settings",
            _ => null
        };
        if (key != null)
        {
            CloseTrack();
            Navigate(key);
            e.Handled = true;
        }
    }

    /// <summary>After a language change: rebuild pages whose texts are set from code.</summary>
    public void RebuildViews()
    {
        string current = Host.Content == null ? "home" : _views.FirstOrDefault(kv => kv.Value == Host.Content).Key ?? "home";
        CloseTrack();
        DialogHost.Content = null;
        _dialogView = null;
        Host.Content = null;
        _views.Clear();
        SetSidebar(_collapsed, animate: false);
        UpdateAutoStatus();
        Navigate(current, animate: false);
    }

    // ===== Barre latérale =====

    private void Collapse_Click(object sender, RoutedEventArgs e) => SetSidebar(!_collapsed, animate: true);

    /// <summary>Icons-only rail, so the content gets the whole width.</summary>
    private void SetSidebar(bool collapsed, bool animate)
    {
        _collapsed = collapsed;
        double target = collapsed ? RailNarrow : RailWide;

        if (animate && !Shell.ReduceMotion)
        {
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(collapsed ? 200 : 240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Rail.BeginAnimation(WidthProperty, anim);
        }
        else
        {
            Rail.BeginAnimation(WidthProperty, null);
            Rail.Width = target;
        }

        BrandText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        AutoCard.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        AutoMini.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;

        // Collapsed: only the toggle stays at the top, centred in the narrow rail.
        BrandMark.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(CollapseButton, collapsed ? 0 : 2);
        Grid.SetColumnSpan(CollapseButton, collapsed ? 3 : 1);
        CollapseButton.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        CollapseButton.ToolTip = Loc.T(collapsed ? "mainwin.expand_sidebar_ctrl" : "mainwin.reduire_la_barre_laterale_ctrl");
        System.Windows.Automation.AutomationProperties.SetName(CollapseButton,
            Loc.T(collapsed ? "mainwin.expand_sidebar" : "mainwin.reduire_la_barre_laterale"));

        foreach (var (nav, key) in _navKeys)
        {
            var label = Loc.T(key);
            nav.Content = collapsed ? null : label;
            nav.ToolTip = collapsed ? label : null;
        }

        var s = _settings.GetSettings();
        if (s.General.SidebarCollapsed != collapsed)
        {
            s.General.SidebarCollapsed = collapsed;
            _settings.Save();
        }
    }

    /// <summary>F11: maximised window with the rail tucked away, or back to normal.</summary>
    private void ToggleFullView()
    {
        bool full = WindowState == WindowState.Maximized && _collapsed;
        if (full)
        {
            WindowState = WindowState.Normal;
            SetSidebar(false, animate: true);
        }
        else
        {
            WindowState = WindowState.Maximized;
            SetSidebar(true, animate: true);
        }
    }

    private void Full_Click(object sender, RoutedEventArgs e) => ToggleFullView();

    // ===== Fiche morceau =====

    private TrackDetailView? _dialogView;

    /// <summary>Opens the track sheet over the current page.</summary>
    public async void ShowTrack(Core.Models.Track track)
    {
        if (_dialogView == null)
        {
            _dialogView = new TrackDetailView(_services, showHistory: true, allowDelete: true);
            _dialogView.Deleted += (_, _) => CloseTrack();
            DialogHost.Content = _dialogView;
        }

        // Reload so counts and dates are current.
        var library = _services.GetRequiredService<Core.Services.LibraryService>();
        var fresh = await library.GetTrackAsync(track.Id) ?? track;
        await _dialogView.ShowAsync(fresh);

        DialogLayer.Visibility = Visibility.Visible;
        if (!Shell.ReduceMotion)
        {
            var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
            DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            DialogShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            DialogLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            _dialogView.PlayReveal();
        }
        DialogCard.Focus();
    }

    public void CloseTrack()
    {
        if (DialogLayer.Visibility != Visibility.Visible) return;
        if (Shell.ReduceMotion)
        {
            DialogLayer.Visibility = Visibility.Collapsed;
            return;
        }
        // Exit faster than entry.
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) =>
        {
            DialogLayer.Visibility = Visibility.Collapsed;
            DialogLayer.BeginAnimation(OpacityProperty, null);
        };
        DialogShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, TimeSpan.FromMilliseconds(130)));
        DialogLayer.BeginAnimation(OpacityProperty, fade);
    }

    private void DialogBackdrop_Click(object sender, MouseButtonEventArgs e) => CloseTrack();
    private void DialogClose_Click(object sender, RoutedEventArgs e) => CloseTrack();

    // ===== Mise à jour =====

    private Core.Updates.ReleaseInfo? _pendingUpdate;

    /// <summary>Shows the "update available" card now, or the next time the window is shown.</summary>
    public void ShowUpdate(Core.Updates.ReleaseInfo release)
    {
        if (!IsVisible)
        {
            _pendingUpdate = release;
            IsVisibleChanged -= ShowPendingUpdate;
            IsVisibleChanged += ShowPendingUpdate;
            return;
        }
        var dialog = new UpdateDialog(_services.GetRequiredService<UpdateService>(), release);
        dialog.Dismissed += (_, _) => { UpdateLayer.Visibility = Visibility.Collapsed; UpdateHost.Content = null; };
        UpdateHost.Content = dialog;
        UpdateLayer.Visibility = Visibility.Visible;
        dialog.PlayEntrance();
    }

    private void ShowPendingUpdate(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible || _pendingUpdate == null) return;
        IsVisibleChanged -= ShowPendingUpdate;
        var release = _pendingUpdate;
        _pendingUpdate = null;
        Dispatcher.BeginInvoke(() => ShowUpdate(release));
    }

    // ===== Auto Shazim =====

    private void UpdateAutoStatus()
    {
        AutoToggle.IsChecked = _auto.IsEnabled;
        AutoMini.IsChecked = _auto.IsEnabled;
        AutoStatusText.Text = _auto.StatusText;
        AutoMini.ToolTip = Loc.T("mainwin.auto_shazim") + " : " + _auto.StatusText;
        AutoDot.Fill = (Brush)FindResource(_auto.State switch
        {
            AutoShazamState.Listening or AutoShazamState.Searching => "WaveBrush",
            AutoShazamState.Waiting => "SignalBrush",
            _ => "MistBrush"
        });

        // A soft beacon around the dot while Auto Shazim is on.
        bool on = _auto.IsEnabled && !Shell.ReduceMotion;
        if (on && _autoPulse == null)
        {
            _autoPulse = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var t = TimeSpan.FromSeconds(1.8);
            foreach (var prop in new[] { "ScaleX", "ScaleY" })
            {
                var a = new DoubleAnimation(1, 2.6, t) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(a, AutoPulse);
                Storyboard.SetTargetProperty(a, new PropertyPath($"(UIElement.RenderTransform).(ScaleTransform.{prop})"));
                _autoPulse.Children.Add(a);
            }
            var f = new DoubleAnimation(0.6, 0, t);
            Storyboard.SetTarget(f, AutoPulse);
            Storyboard.SetTargetProperty(f, new PropertyPath(OpacityProperty));
            _autoPulse.Children.Add(f);
            _autoPulse.Begin(this, true);
        }
        else if (!on && _autoPulse != null)
        {
            _autoPulse.Stop(this);
            _autoPulse = null;
            AutoPulse.Opacity = 0;
        }
    }

    private void AutoToggle_Click(object sender, RoutedEventArgs e) => _auto.SetEnabled(AutoToggle.IsChecked == true);
    private void AutoMini_Click(object sender, RoutedEventArgs e) => _auto.SetEnabled(AutoMini.IsChecked == true);

    // ===== Fenêtre =====

    private void UpdateMaximizeState()
    {
        // A maximized chrome-less window overflows the screen by the resize border.
        bool max = WindowState == WindowState.Maximized;
        RootBorder.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxButton.Tag = FindResource(max ? "IconCopySimple" : "IconSquare");
        MaxButton.ToolTip = max ? "Restaurer" : "Agrandir";
    }

    private void Mini_Click(object sender, RoutedEventArgs e)
    {
        App.Current.ShowMiniWindow();
        Hide();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.Current.IsExiting)
        {
            // Keep running in the notification area (or exit, depending on settings).
            e.Cancel = true;
            Hide();
            App.Current.OnMainWindowClosing();
        }
        base.OnClosing(e);
    }
}

/// <summary>Views that reload their data when shown.</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}
