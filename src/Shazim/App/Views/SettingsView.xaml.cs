using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Shazim.App.Infrastructure;
using Shazim.App.Services;
using Shazim.Core.Interfaces;
using Shazim.Core.Services;

namespace Shazim.App.Views;

public partial class SettingsView : UserControl, IRefreshable
{
    private readonly SettingsService _settings;
    private readonly IAudioCaptureService _capture;
    private readonly LibraryService _library;
    private readonly List<RadioButton> _accentButtons = new();
    private bool _loading;
    private bool _testing;

    public SettingsView(IServiceProvider services)
    {
        _settings = services.GetRequiredService<SettingsService>();
        _capture = services.GetRequiredService<IAudioCaptureService>();
        _library = services.GetRequiredService<LibraryService>();
        InitializeComponent();

        string Sec(int n) => Loc.F("unit.seconds", n);
        string Min(int n) => n == 1 ? Loc.T("unit.minute") : Loc.F("unit.minutes", n);
        string Hour(int n) => n == 1 ? Loc.T("unit.hour") : Loc.F("unit.hours", n);
        Fill(ListenCombo, (8000, Sec(8)), (12000, Loc.F("unit.seconds_recommended", 12)), (16000, Sec(16)), (20000, Sec(20)));
        Fill(IntervalCombo, (5000, Sec(5)), (15000, Sec(15)), (30000, Sec(30)), (60000, Min(1)), (120000, Min(2)), (300000, Min(5)));
        Fill(DuplicateCombo, (5, Min(5)), (15, Min(15)), (30, Min(30)), (60, Hour(1)), (180, Hour(3)), (1440, Hour(24)));
        BuildLanguages();
        AboutLogo.Source = Brand.Logo;
        VersionText.Text = Loc.F("about.version", AppVersion());
        BuildAccentButtons();
    }

    private static void Fill(ComboBox combo, params (int Value, string Label)[] items)
    {
        foreach (var (value, label) in items)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
    }

    private static void SelectValue(ComboBox combo, int value)
    {
        var items = combo.Items.Cast<ComboBoxItem>().ToList();
        combo.SelectedItem = items.FirstOrDefault(i => (int)i.Tag == value) ?? items.OrderBy(i => Math.Abs((int)i.Tag - value)).First();
    }

    private static int SelectedValue(ComboBox combo, int fallback) =>
        combo.SelectedItem is ComboBoxItem { Tag: int v } ? v : fallback;

    /// <summary>One round swatch per accent colour.</summary>
    private void BuildAccentButtons()
    {
        foreach (var accent in ThemeService.Accents)
        {
            var swatch = new Grid { Width = 34, Height = 34 };
            var ring = new System.Windows.Shapes.Ellipse { Stroke = Brushes.White, StrokeThickness = 2, Opacity = 0 };
            swatch.Children.Add(ring);
            swatch.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(5), Fill = new SolidColorBrush(accent.Main) });

            var button = new RadioButton
            {
                GroupName = "accent", Tag = accent.Key, ToolTip = Loc.T("accent." + accent.Key), Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand,
                Content = swatch, FocusVisualStyle = (Style)FindResource("FocusRing")
            };
            AutomationPropertiesHelper.SetName(button, Loc.T("settings.couleur") + " " + Loc.T("accent." + accent.Key));
            button.Template = SwatchTemplate();
            button.Checked += (_, _) => ring.Opacity = 1;
            button.Unchecked += (_, _) => ring.Opacity = 0;
            button.Click += (_, _) => SetAccent(accent.Key);
            _accentButtons.Add(button);
            AccentList.Items.Add(button);
        }
    }

    private static ControlTemplate SwatchTemplate()
    {
        var template = new ControlTemplate(typeof(RadioButton));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        template.VisualTree = presenter;
        return template;
    }

    private void SetAccent(string key)
    {
        ThemeService.Apply(key);
        _settings.GetSettings().General.Accent = key;
        AccentName.Text = Loc.T("accent." + ThemeService.Find(key).Key);
        ShowSaved(Loc.T("settings.saved"));
        _settings.Save();
    }

    public async Task RefreshAsync()
    {
        _loading = true;
        try
        {
            var s = _settings.GetSettings();

            var devices = await _capture.GetDevicesAsync();
            var outputs = devices.Where(d => !d.IsInput).ToList();
            var mics = devices.Where(d => d.IsInput).ToList();
            OutputCombo.ItemsSource = outputs;
            MicCombo.ItemsSource = mics;
            OutputCombo.SelectedItem = outputs.FirstOrDefault(d => d.Id == s.Audio.OutputDeviceId) ?? outputs.FirstOrDefault();
            MicCombo.SelectedItem = mics.FirstOrDefault(d => d.Id == s.Audio.MicDeviceId) ?? mics.FirstOrDefault();

            SourcePc.IsChecked = s.Audio.Source == AudioSource.Pc;
            SourceMic.IsChecked = s.Audio.Source == AudioSource.Mic;
            SourceBoth.IsChecked = s.Audio.Source == AudioSource.Both;
            UpdateSourceRows(s.Audio.Source);

            SelectValue(ListenCombo, s.Audio.CaptureDurationMs);
            SelectValue(IntervalCombo, s.AutoShazam.IntervalMs);
            SelectValue(DuplicateCombo, s.AutoShazam.DuplicateWindowMinutes);

            var accent = ThemeService.Find(s.General.Accent);
            foreach (var b in _accentButtons) b.IsChecked = (string)b.Tag == accent.Key;
            AccentName.Text = Loc.T("accent." + accent.Key);

            EffectAura.IsChecked = s.General.ButtonEffect == ButtonEffect.Aura;
            EffectPulse.IsChecked = s.General.ButtonEffect == ButtonEffect.Pulse;
            EffectOff.IsChecked = s.General.ButtonEffect == ButtonEffect.Off;
            AutoStartSwitch.IsChecked = s.AutoShazam.AutoStart;
            NotifySwitch.IsChecked = s.Notifications.Enabled;
            AutoOnlySwitch.IsChecked = s.Notifications.AutoOnly;
            AutoOnlySwitch.IsEnabled = s.Notifications.Enabled;
            HotkeysSwitch.IsChecked = s.Hotkeys.Enabled;
            IdentifyHotkeyBox.Text = s.Hotkeys.Identify;
            AutoHotkeyBox.Text = s.Hotkeys.ToggleAuto;
            IdentifyHotkeyBox.IsEnabled = AutoHotkeyBox.IsEnabled = s.Hotkeys.Enabled;

            StartWithWindowsSwitch.IsChecked = s.General.StartWithWindows;
            StartMinimizedSwitch.IsChecked = s.General.StartMinimized;
            StartMinimizedSwitch.IsEnabled = s.General.StartWithWindows;
            CloseToTraySwitch.IsChecked = s.General.CloseToTray;
        }
        finally
        {
            _loading = false;
        }
    }

    private string CurrentSource =>
        SourceMic.IsChecked == true ? AudioSource.Mic : SourceBoth.IsChecked == true ? AudioSource.Both : AudioSource.Pc;

    private void UpdateSourceRows(string source)
    {
        OutputRow.Visibility = source == AudioSource.Mic ? Visibility.Collapsed : Visibility.Visible;
        MicRow.Visibility = source == AudioSource.Pc ? Visibility.Collapsed : Visibility.Visible;
    }

    // ===== Enregistrement automatique =====

    private void Any_Changed(object sender, SelectionChangedEventArgs e) => SaveAll();
    private void Any_Click(object sender, RoutedEventArgs e) => SaveAll();

    private void Source_Click(object sender, RoutedEventArgs e)
    {
        UpdateSourceRows(CurrentSource);
        SaveAll();
    }

    private void SaveAll()
    {
        if (_loading || !IsLoaded) return;
        var s = _settings.GetSettings();

        s.Audio.Source = CurrentSource;
        if (OutputCombo.SelectedItem is AudioDevice output) s.Audio.OutputDeviceId = output.Id;
        if (MicCombo.SelectedItem is AudioDevice mic) s.Audio.MicDeviceId = mic.Id;
        s.Audio.CaptureDurationMs = SelectedValue(ListenCombo, 12000);
        s.AutoShazam.CaptureDurationMs = s.Audio.CaptureDurationMs;
        s.AutoShazam.IntervalMs = SelectedValue(IntervalCombo, 30000);
        s.AutoShazam.DuplicateWindowMinutes = SelectedValue(DuplicateCombo, 15);
        s.AutoShazam.AutoStart = AutoStartSwitch.IsChecked == true;
        s.General.ButtonEffect = EffectPulse.IsChecked == true ? ButtonEffect.Pulse
            : EffectOff.IsChecked == true ? ButtonEffect.Off : ButtonEffect.Aura;

        s.Notifications.Enabled = NotifySwitch.IsChecked == true;
        s.Notifications.AutoOnly = AutoOnlySwitch.IsChecked == true;
        AutoOnlySwitch.IsEnabled = s.Notifications.Enabled;

        s.Hotkeys.Enabled = HotkeysSwitch.IsChecked == true;
        IdentifyHotkeyBox.IsEnabled = AutoHotkeyBox.IsEnabled = s.Hotkeys.Enabled;

        bool startWithWindows = StartWithWindowsSwitch.IsChecked == true;
        if (startWithWindows != s.General.StartWithWindows || startWithWindows != StartupService.IsEnabled())
        {
            try { StartupService.Apply(startWithWindows); }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this)!, Loc.F("settings.startup_failed", ex.Message)); }
        }
        s.General.StartWithWindows = startWithWindows;
        StartMinimizedSwitch.IsEnabled = startWithWindows;
        s.General.StartMinimized = StartMinimizedSwitch.IsChecked == true;
        s.General.CloseToTray = CloseToTraySwitch.IsChecked == true;

        ShowSaved(Loc.T("settings.saved"));
        _settings.Save();
    }

    private void ShowSaved(string text)
    {
        SavedText.Text = text;
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400))));
        SavedText.BeginAnimation(OpacityProperty, fade);
    }

    // ===== Raccourcis =====

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key is Key.Tab) { e.Handled = false; return; }
        if (key is Key.Escape) { Keyboard.ClearFocus(); return; }

        var gesture = HotkeyService.FormatGesture(Keyboard.Modifiers, key);
        if (gesture == null)
        {
            HotkeyError.Text = Loc.T("settings.hotkey_modifier");
            HotkeyError.Visibility = Visibility.Visible;
            return;
        }

        var other = box == IdentifyHotkeyBox ? AutoHotkeyBox.Text : IdentifyHotkeyBox.Text;
        if (string.Equals(other, gesture, StringComparison.OrdinalIgnoreCase))
        {
            HotkeyError.Text = Loc.T("settings.hotkey_taken");
            HotkeyError.Visibility = Visibility.Visible;
            return;
        }

        HotkeyError.Visibility = Visibility.Collapsed;
        box.Text = gesture;
        var s = _settings.GetSettings();
        if (box == IdentifyHotkeyBox) s.Hotkeys.Identify = gesture; else s.Hotkeys.ToggleAuto = gesture;
        ShowSaved(Loc.T("settings.saved"));
        _settings.Save();
    }

    // ===== Test du son =====

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_testing) return;
        _testing = true;
        TestButton.IsEnabled = false;
        var ids = new List<string>();
        if (CurrentSource != AudioSource.Mic) ids.Add((OutputCombo.SelectedItem as AudioDevice)?.Id ?? AudioDevice.DefaultId);
        if (CurrentSource != AudioSource.Pc) ids.Add((MicCombo.SelectedItem as AudioDevice)?.Id ?? AudioDevice.DefaultMicId);

        var sessions = new List<ICaptureSession>();
        var peaks = new Dictionary<ICaptureSession, float>();
        try
        {
            foreach (var id in ids)
            {
                try { sessions.Add(await Task.Run(() => _capture.StartSession(id))); }
                catch (AudioCaptureException ex) { TestHint.Text = ex.Message; }
            }
            if (sessions.Count == 0) return;

            TestHint.Text = Loc.F("settings.test_listening", string.Join(Loc.T("core.and"), sessions.Select(s => $"« {s.DeviceName} »")));
            var end = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < end)
            {
                foreach (var session in sessions)
                    peaks[session] = Math.Max(peaks.GetValueOrDefault(session), session.Level);
                var level = sessions.Max(x => x.Level);
                LevelBar.Width = 180 * Math.Min(1.0, Math.Sqrt(level) * 1.8);
                await Task.Delay(50);
            }

            var silent = sessions.Where(x => peaks.GetValueOrDefault(x) <= 0.002f).Select(x => x.DeviceName).ToList();
            TestHint.Text = silent.Count == 0
                ? Loc.T("settings.test_ok")
                : Loc.F("settings.test_silent", string.Join(Loc.T("core.and"), silent));
        }
        finally
        {
            foreach (var session in sessions) session.Dispose();
            LevelBar.Width = 0;
            TestButton.IsEnabled = true;
            _testing = false;
        }
    }

    // ===== Langue =====

    private void BuildLanguages()
    {
        foreach (var (code, name) in Loc.Languages)
            LanguageCombo.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        LanguageCombo.SelectedItem = LanguageCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Loc.Instance.Code);
        LanguageCombo.SelectionChanged += (_, _) =>
        {
            if (LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string code } || code == Loc.Instance.Code) return;
            _settings.GetSettings().General.Language = code;
            _settings.Save();
            App.Current.ApplyLanguage(code);
        };
    }

    // ===== À propos =====

    public const string RepositoryUrl = "https://github.com/painfullfeet/Shazim";

    private static string AppVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "2.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) => Infrastructure.Shell.Open(RepositoryUrl);

    private void Licenses_Click(object sender, RoutedEventArgs e) => Infrastructure.Shell.Open(RepositoryUrl + "#licences");

    // ===== Données =====

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_settings.SettingsPath)!;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var log = Path.Combine(FileLogger<SettingsView>.LogDirectory, "shazim.log");
        if (File.Exists(log)) Process.Start(new ProcessStartInfo(log) { UseShellExecute = true });
        else OpenData_Click(sender, e);
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            Loc.T("settings.clear_confirm"),
            Loc.T("settings.effacer_l_historique_d_ecoute"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await _library.ClearHistoryAsync();
        ShowSaved(Loc.T("settings.history_cleared"));
    }
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);
}
