using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Shazim.App.Setup;

/// <summary>Installer / uninstaller window, styled like the app.</summary>
public partial class SetupWindow : Window
{
    private enum Stage { Options, Working, Done, Failed }

    private readonly SetupMode _mode;
    private readonly bool _quiet;
    private Stage _stage = Stage.Options;

    public SetupWindow(SetupMode mode, bool quiet = false)
    {
        _mode = mode;
        _quiet = quiet;
        InitializeComponent();

        Logo.Source = Brand.Logo;
        try { using var s = Brand.OpenIcon(); if (s != null) Icon = System.Windows.Media.Imaging.BitmapFrame.Create(s, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad); }
        catch { }

        double mb = Installer.PackageSizeBytes / 1024d / 1024d;
        VersionText.Text = Loc.F("setup.version", Installer.Version, mb.ToString("0", Loc.Instance.Culture));
        FolderText.Text = Installer.InstallDir.Replace(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase);
        FolderText.ToolTip = Installer.InstallDir;

        if (mode == SetupMode.Uninstall)
        {
            Title = Loc.T("setup.title_uninstall");
            TitleText.Text = Loc.T("setup.title_uninstall");
            SubtitleText.Text = Loc.T("setup.uninstall_text");
            DesktopSwitch.Visibility = Visibility.Collapsed;
            LaunchSwitch.Visibility = Visibility.Collapsed;
            RemoveDataSwitch.Visibility = Visibility.Visible;
            HintText.Visibility = Visibility.Collapsed;
            MainButton.Content = Loc.T("setup.uninstall");
            MainButton.SetResourceReference(BackgroundProperty, "DangerBrush");
        }
        else
        {
            Title = Loc.T("setup.title_install");
            if (Installer.IsInstalled)
            {
                TitleText.Text = Loc.T("setup.title_update");
                DesktopSwitch.IsChecked = Installer.DesktopShortcutExists;
            }
            MainButton.Content = Loc.T(Installer.IsInstalled ? "setup.update" : "setup.install");
        }

        Loaded += (_, _) =>
        {
            StartPulse();
            if (_quiet) Main_Click(this, new RoutedEventArgs());
        };
    }

    // ===== Animations =====

    private void StartPulse()
    {
        if (Shell.ReduceMotion) return;
        var rings = new[] { Pulse1, Pulse2, Pulse3 };
        for (int i = 0; i < rings.Length; i++) AnimateRing(rings[i], TimeSpan.FromSeconds(i * 0.8));
        Breathe(1.04, 1.6);
    }

    private static void AnimateRing(Ellipse ring, TimeSpan delay)
    {
        var period = TimeSpan.FromSeconds(2.4);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var scale = new DoubleAnimation(1, 1.65, period) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        var fade = new DoubleAnimation(0.55, 0, period) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        var t = new ScaleTransform();
        ring.RenderTransform = t;
        t.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        ring.BeginAnimation(OpacityProperty, fade);
    }

    private void Breathe(double to, double seconds)
    {
        var a = new DoubleAnimation(1, to, TimeSpan.FromSeconds(seconds / 2))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        DiscScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        DiscScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void Pop()
    {
        DiscScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DiscScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (Shell.ReduceMotion) return;
        var a = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        DiscScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        DiscScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void ShowPage(FrameworkElement page)
    {
        foreach (var p in new FrameworkElement[] { OptionsPage, ProgressPage, DonePage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        if (Shell.ReduceMotion) return;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        var slide = new TranslateTransform(0, 10);
        page.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void SetProgress(double value, string key)
    {
        StepText.Text = Loc.T(key);
        PercentText.Text = $"{Math.Round(value * 100)} %";
        double width = Math.Max(0, Track.ActualWidth * Math.Clamp(value, 0, 1));
        Fill.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ===== Actions =====

    private async void Main_Click(object sender, RoutedEventArgs e)
    {
        switch (_stage)
        {
            case Stage.Options:
                await RunAsync();
                break;
            case Stage.Done when _mode == SetupMode.Install:
                Installer.LaunchInstalled();
                Close();
                break;
            default:
                Close();
                break;
        }
    }

    private async Task RunAsync()
    {
        _stage = Stage.Working;
        bool install = _mode == SetupMode.Install;
        MainButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        MainButton.Content = Loc.T(install ? "setup.installing" : "setup.uninstalling");
        ShowPage(ProgressPage);
        Breathe(1.08, 0.9);

        var progress = new Progress<(double Value, string Key)>(p => SetProgress(p.Value, p.Key));
        try
        {
            if (install) await Installer.InstallAsync(DesktopSwitch.IsChecked == true, progress);
            else await Installer.UninstallAsync(RemoveDataSwitch.IsChecked == true, progress);
        }
        catch (Exception ex)
        {
            _stage = Stage.Failed;
            Pop();
            TitleText.Text = Loc.T("setup.failed_title");
            DoneText.Text = Loc.F("setup.failed", ex.Message);
            MainButton.Content = Loc.T("setup.close");
            MainButton.IsEnabled = CloseButton.IsEnabled = true;
            ShowPage(DonePage);
            return;
        }

        await Task.Delay(300);
        _stage = Stage.Done;
        Pop();
        Logo.Visibility = Visibility.Collapsed;
        DoneIcon.Visibility = Visibility.Visible;
        MainButton.IsEnabled = CloseButton.IsEnabled = true;
        ShowPage(DonePage);

        if (install)
        {
            TitleText.Text = Loc.T("setup.done_title");
            SubtitleText.Text = Loc.T("setup.done_text");
            DoneText.Text = "";
            MainButton.Content = Loc.T("setup.launch");
            if (_quiet || LaunchSwitch.IsChecked == true)
            {
                // Let the success state show for a moment, then hand over to the app.
                await Task.Delay(_quiet ? 0 : 1400);
                Installer.LaunchInstalled();
                Close();
            }
        }
        else
        {
            TitleText.Text = Loc.T("setup.removed_title");
            SubtitleText.Text = Loc.T("setup.removed_text");
            DoneText.Text = "";
            MainButton.Content = Loc.T("setup.close");
            MainButton.SetResourceReference(BackgroundProperty, "SignalBrush");
            if (_quiet) Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_mode == SetupMode.Uninstall && _stage == Stage.Done) Installer.ScheduleFolderRemoval();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == Stage.Working) return;
        Close();
    }

    private void Drag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _stage != Stage.Working) { Close(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
}
