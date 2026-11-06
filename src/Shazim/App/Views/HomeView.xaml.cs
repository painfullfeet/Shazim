using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Shazim.App.Infrastructure;
using Shazim.App.Services;
using Shazim.Core.Interfaces;
using Shazim.Core.Models;
using Shazim.Core.Recognition;
using Shazim.Core.Services;

namespace Shazim.App.Views;

public partial class HomeView : UserControl, IRefreshable
{
    private readonly IdentifyController _controller;
    private readonly LibraryService _library;
    private readonly SettingsService _settings;
    private readonly IAudioCaptureService _capture;
    private readonly PreviewPlayer _player;
    private readonly TrackDetailView _detail;
    private readonly MusicVisualizer _visualizer;
    private readonly float[] _bandsRaw = new float[MusicVisualizer.BandCount];
    private readonly double[] _bands = new double[MusicVisualizer.BandCount];
    private double _bass;
    private bool _reactive;
    private string _effect = ButtonEffect.Aura;
    private double _smoothLevel;
    private bool _renderHooked;
    private bool _refreshQueued;
    private Storyboard? _ripples;

    public HomeView(IServiceProvider services)
    {
        _controller = services.GetRequiredService<IdentifyController>();
        _library = services.GetRequiredService<LibraryService>();
        _settings = services.GetRequiredService<SettingsService>();
        _capture = services.GetRequiredService<IAudioCaptureService>();
        _player = services.GetRequiredService<PreviewPlayer>();
        _visualizer = services.GetRequiredService<MusicVisualizer>();
        InitializeComponent();

        _detail = new TrackDetailView(services, showHistory: false, allowDelete: false);
        _detail.SetTopInset(52);
        DetailHost.Content = _detail;

        if (Brand.Logo is { } logo)
        {
            LogoImage.Source = logo;
            LogoImage.Visibility = Visibility.Visible;
            CoreGlyph.Visibility = Visibility.Collapsed;
        }

        _controller.PropertyChanged += OnControllerChanged;
        _library.Changed += (_, _) => QueueRecentRefresh();
        _settings.Saved += (_, _) => Dispatcher.BeginInvoke(async () => await UpdateSourceAsync());

        Loaded += (_, _) => { Focus(); StartIdle(); UpdateReactive(); };
        Unloaded += (_, _) => { StopIdle(); UpdateReactive(); };
        IsVisibleChanged += (_, _) => UpdateReactive();
        _settings.Saved += (_, _) => Dispatcher.BeginInvoke(UpdateReactive);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && ResultPanel.Visibility == Visibility.Visible) { ShowListen(); e.Handled = true; }
        };
        ApplyListeningState();
    }

    public async Task RefreshAsync()
    {
        var hk = _settings.GetSettings().Hotkeys;
        HotkeyHint.Text = hk.Enabled ? Loc.F("home.hotkey_hint", hk.Identify) : "";
        await UpdateSourceAsync();
        await LoadRecentAsync();
    }

    // ===== Écoute =====

    private async void Listen_Click(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        await _controller.ToggleAsync();
    }

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IdentifyController.IsBusy):
            case nameof(IdentifyController.Phase):
                ApplyListeningState();
                break;
            case nameof(IdentifyController.Progress):
                UpdateProgressArc(_controller.Progress);
                break;
            case nameof(IdentifyController.LastOutcome):
                if (_controller.LastOutcome is { } outcome) _ = ShowOutcomeAsync(outcome);
                break;
        }
    }

    private void ApplyListeningState()
    {
        bool busy = _controller.IsBusy;
        ProgressArc.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ListenButton.ToolTip = Loc.T(busy ? "home.stop_listening" : "home.identify_space");

        if (busy)
        {
            ShowListen();
            StatusTitle.Text = Loc.T(_controller.Phase == IdentifyPhase.Searching ? "home.searching" : "home.listening");
            StatusDetail.Text = Loc.T("home.keep_sound_stop");
            HookRendering(true);
            StartRipples();
            if (IsLoaded) StartIdle(listening: true);
        }
        else
        {
            HookRendering(false);
            StopRipples();
            if (IsLoaded) StartIdle();
            UpdateProgressArc(0);
            if (StatusTitle.Text == Loc.T("home.searching") || StatusTitle.Text == Loc.T("home.listening"))
            {
                // Stopped by the user: back to the invitation.
                StatusTitle.Text = Loc.T("home.touchez_pour_shazimer");
                _ = UpdateSourceAsync();
            }
        }
    }

    // ===== Effets =====

    private Storyboard? _idle;

    /// <summary>At rest the button breathes and its glow pulses, inviting a tap (like Shazam).</summary>
    private void StartIdle(bool listening = false)
    {
        StopIdle();
        if (Shell.ReduceMotion) return;
        var period = TimeSpan.FromSeconds(listening ? 0.9 : 2.6);
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        _idle = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };

        void Add(DependencyObject target, string path, double from, double to)
        {
            var a = new DoubleAnimation(from, to, period) { EasingFunction = sine };
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, new PropertyPath(path));
            _idle!.Children.Add(a);
        }
        if (!listening)
        {
            Add(CoreBreath, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1, 1.035);
            Add(CoreBreath, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1, 1.035);
        }
        Add(Halo, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.92, listening ? 1.18 : 1.08);
        Add(Halo, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.92, listening ? 1.18 : 1.08);
        Add(Halo, "Opacity", listening ? 0.45 : 0.3, listening ? 0.9 : 0.6);
        _idle.Begin(this, true);
    }

    private void StopIdle()
    {
        if (_idle == null) return;
        _idle.Stop(this);
        _idle = null;
    }

    /// <summary>Tactile press: the button sinks, then springs back on release.</summary>
    private void Listen_Press(object sender, MouseButtonEventArgs e) => AnimatePress(0.92, 90, null);

    private void Listen_Release(object sender, MouseEventArgs e)
    {
        if (PressScale.ScaleX < 1) AnimatePress(1, 420, new ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = EasingMode.EaseOut });
    }

    private void AnimatePress(double to, int ms, IEasingFunction? ease)
    {
        if (Shell.ReduceMotion) return;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        PressScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        PressScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Rings expanding from the button while listening, like the Shazam app.</summary>
    private void StartRipples()
    {
        if (_ripples != null) return;
        if (Shell.ReduceMotion) return;

        _ripples = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var ellipses = new[] { Ripple1, Ripple2, Ripple3 };
        var cycle = TimeSpan.FromSeconds(2.4);
        for (int i = 0; i < ellipses.Length; i++)
        {
            var begin = TimeSpan.FromSeconds(0.8 * i);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            foreach (var prop in new[] { "ScaleX", "ScaleY" })
            {
                var scale = new DoubleAnimation(1.0, 1.55, cycle) { BeginTime = begin, EasingFunction = ease };
                Storyboard.SetTarget(scale, ellipses[i]);
                Storyboard.SetTargetProperty(scale, new PropertyPath($"(UIElement.RenderTransform).(ScaleTransform.{prop})"));
                _ripples.Children.Add(scale);
            }
            var fade = new DoubleAnimation(0.9, 0, cycle) { BeginTime = begin };
            Storyboard.SetTarget(fade, ellipses[i]);
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            _ripples.Children.Add(fade);
        }
        _ripples.Duration = cycle + TimeSpan.FromSeconds(1.6);
        _ripples.Begin(this, true);
    }

    private void StopRipples()
    {
        if (_ripples == null) return;
        _ripples.Stop(this);
        _ripples = null;
        foreach (var r in new[] { Ripple1, Ripple2, Ripple3 }) r.Opacity = 0;
    }

    /// <summary>Starts the music-reactive effect while the home screen is on display.</summary>
    private void UpdateReactive()
    {
        var s = _settings.GetSettings();
        _effect = s.General.ButtonEffect;
        bool on = IsLoaded && IsVisible && _effect != ButtonEffect.Off && !Shell.ReduceMotion;
        if (_effect != ButtonEffect.Aura) DrawAura(false);
        if (on == _reactive) return;
        _reactive = on;
        if (on) _visualizer.Start(s.Audio.OutputDeviceId);
        else
        {
            _visualizer.Stop();
            DrawAura(false);
            Array.Clear(_bands);
            _bass = 0;
            _beat = 0;
        }
        HookRendering(_controller.IsBusy || _reactive);
    }

    private void HookRendering(bool on)
    {
        on |= _reactive;
        if (on == _renderHooked) return;
        _renderHooked = on;
        if (on) CompositionTarget.Rendering += OnRendering;
        else
        {
            CompositionTarget.Rendering -= OnRendering;
            _smoothLevel = 0;
            CoreScale.ScaleX = CoreScale.ScaleY = 1;
        }
    }

    /// <summary>
    /// Every frame: the button swells with the loudness it listens to and kicks on the bass,
    /// while a ring of bars around it follows the spectrum of the music playing on the PC.
    /// </summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this)?.WindowState == WindowState.Minimized) return;

        double target = Shell.ReduceMotion || !_controller.IsBusy ? 0 : Math.Min(1.0, Math.Sqrt(Math.Max(0, _controller.Level)) * 1.8);
        _smoothLevel += (target - _smoothLevel) * (target > _smoothLevel ? 0.35 : 0.08);

        double kick = 0;
        if (_reactive)
        {
            float bass = _visualizer.Read(_bandsRaw);
            if (!_visualizer.HasSignal) { Array.Clear(_bandsRaw); bass = 0; }
            // Fast attack, slow release: punchy but never jittery.
            _bass += (bass - _bass) * (bass > _bass ? 0.55 : 0.10);
            for (int i = 0; i < _bands.Length; i++)
                _bands[i] += (_bandsRaw[i] - _bands[i]) * (_bandsRaw[i] > _bands[i] ? 0.5 : 0.12);
            kick = UpdateBeat();
            DrawAura(_effect == ButtonEffect.Aura);
        }

        // The logo swells on every beat (tempo) and a little with the overall bass.
        // "Pulsation" has no aura, so its kick is stronger and each beat sends a ring outwards.
        double kickAmount = _effect == ButtonEffect.Pulse ? 0.17 : 0.11;
        CoreScale.ScaleX = CoreScale.ScaleY = 1 + 0.06 * _smoothLevel + kickAmount * kick + 0.03 * _bass;
    }

    // ===== Aura (spectre lissé) et pulsation au rythme =====

    private const int AuraPoints = 96;
    private readonly double[] _ring = new double[AuraPoints];
    private readonly double[] _tmp = new double[AuraPoints];
    private double _beat;
    private int _beatsSeen;
    private bool _ringToggle;

    /// <summary>
    /// The visualizer finds beats at its own analysis rate (see BeatDetector); every new one
    /// starts a pulse that decays over ~150 ms, so the logo swells on the tempo.
    /// </summary>
    private double UpdateBeat()
    {
        int count = _visualizer.BeatCount;
        if (count != _beatsSeen)
        {
            bool fresh = count > _beatsSeen && _visualizer.HasSignal;
            _beatsSeen = count;
            if (fresh)
            {
                _beat = 1;
                if (_effect == ButtonEffect.Pulse) FireBeatRing();
                return _beat;
            }
        }
        _beat *= 0.86;
        return _beat;
    }

    /// <summary>A ring leaves the button on the beat (two rings alternate for fast tempos).</summary>
    private void FireBeatRing()
    {
        _ringToggle = !_ringToggle;
        var (ring, scale) = _ringToggle ? (BeatRing1, BeatRing1Scale) : (BeatRing2, BeatRing2Scale);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(1.0, 1.42, TimeSpan.FromMilliseconds(560)) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(560)) { EasingFunction = ease });
    }

    private void DrawAura(bool show)
    {
        if (!show)
        {
            AuraOuter.Data = AuraMiddle.Data = AuraInner.Data = null;
            return;
        }

        // Spectrum around the circle, mirrored left/right, bass on top (big "ears" like Specterr).
        int n = _bands.Length;
        double total = 0;
        for (int k = 0; k < AuraPoints; k++)
        {
            int half = AuraPoints / 2;
            int idx = k < half ? k : AuraPoints - 1 - k;
            double v = _bands[Math.Min(n - 1, idx * n / half)];
            _ring[k] = v;
            total += v;
        }
        // Blur around the ring so the outline stays organic, not spiky.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < AuraPoints; k++)
                _tmp[k] = (_ring[(k - 1 + AuraPoints) % AuraPoints] + 2 * _ring[k] + _ring[(k + 1) % AuraPoints]) / 4;
            Array.Copy(_tmp, _ring, AuraPoints);
        }

        if (total / AuraPoints < 0.02)
        {
            AuraOuter.Data = AuraMiddle.Data = AuraInner.Data = null;
            return;
        }

        double swell = 1 + 0.06 * _beat;
        AuraOuter.Data = Blob(101, 62 * swell);
        AuraMiddle.Data = Blob(100, 44 * swell);
        AuraInner.Data = Blob(99, 24 * swell);
    }

    /// <summary>Closed smooth curve (Catmull-Rom through the ring) around the button.</summary>
    private Geometry Blob(double radius, double amplitude)
    {
        const double cx = 150, cy = 150;
        var pts = new Point[AuraPoints];
        for (int k = 0; k < AuraPoints; k++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * k / AuraPoints;
            double r = radius + Math.Pow(_ring[k], 1.25) * amplitude;
            pts[k] = new Point(cx + Math.Cos(angle) * r, cy + Math.Sin(angle) * r);
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            for (int k = 0; k < AuraPoints; k++)
            {
                Point p0 = pts[(k - 1 + AuraPoints) % AuraPoints], p1 = pts[k], p2 = pts[(k + 1) % AuraPoints], p3 = pts[(k + 2) % AuraPoints];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                ctx.BezierTo(c1, c2, p2, true, true);
            }
        }
        geo.Freeze();
        return geo;
    }

    private void UpdateProgressArc(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        const double r = 108, c = 110;
        double angle = fraction * 2 * Math.PI;
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle)),
            new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        ProgressArc.Stretch = Stretch.None;
        ProgressArc.Data = new PathGeometry(new[] { fig });
    }

    // ===== Résultat =====

    private async Task ShowOutcomeAsync(IdentifyOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case IdentifyOutcomeKind.Match when outcome.Track != null:
                var badge = outcome.IsAuto
                    ? Loc.T("mainwin.auto_shazim") + " · " + DateTime.Now.ToString("t", Loc.Instance.Culture)
                    : Loc.F("home.badge", DateTime.Now.ToString("t", Loc.Instance.Culture), Math.Round(outcome.Duration.TotalSeconds)) +
                      (string.IsNullOrEmpty(outcome.SourceName) ? "" : $" · {outcome.SourceName}");
                await ShowTrackAsync(outcome.Track, badge);
                break;
            case IdentifyOutcomeKind.NoMatch:
                StatusTitle.Text = Loc.T("home.no_result");
                StatusDetail.Text = outcome.Message;
                break;
            case IdentifyOutcomeKind.Silence:
                StatusTitle.Text = Loc.T("home.no_sound");
                StatusDetail.Text = outcome.Message;
                break;
            case IdentifyOutcomeKind.Error:
                StatusTitle.Text = Loc.T("home.cannot_recognise");
                StatusDetail.Text = outcome.Message;
                break;
        }
    }

    private async Task ShowTrackAsync(Track track, string? badge)
    {
        await _detail.ShowAsync(track, badge);
        ListenPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        _detail.PlayReveal();

        if (!Shell.ReduceMotion)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ResultPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }
    }

    private void ShowListen()
    {
        if (ResultPanel.Visibility != Visibility.Visible) return;
        ResultPanel.Visibility = Visibility.Collapsed;
        ListenPanel.Visibility = Visibility.Visible;
        StatusTitle.Text = Loc.T("home.touchez_pour_shazimer");
        _ = UpdateSourceAsync(); // text matches the chosen source (PC, micro or both)
        Focus();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowListen();

    // ===== Source & récents =====

    private async Task UpdateSourceAsync()
    {
        var audio = _settings.GetSettings().Audio;
        var devices = await _capture.GetDevicesAsync();
        string Name(string id) => devices.FirstOrDefault(d => d.Id == id)?.Name ?? Loc.T("home.default_device");

        string icon;
        (icon, SourceText.Text) = audio.Source switch
        {
            AudioSource.Mic => ("IconMicrophone", Loc.T("settings.micro") + " : " + Name(audio.MicDeviceId)),
            AudioSource.Both => ("IconHeadphones", Loc.T("home.pc_and_mic")),
            _ => ("IconSpeakerHigh", audio.OutputDeviceId == AudioDevice.DefaultId ? Loc.T("home.son_du_pc") : Loc.T("home.son_du_pc") + " : " + Name(audio.OutputDeviceId))
        };
        SourceIcon.Data = IconView.Get(icon);
        if (!_controller.IsBusy && StatusTitle.Text == Loc.T("home.touchez_pour_shazimer"))
            StatusDetail.Text = audio.Source switch
            {
                AudioSource.Mic => Loc.T("home.hint_mic"),
                AudioSource.Both => Loc.T("home.hint_both"),
                _ => Loc.T("home.reconnait_la_musique_jouee_sur")
            };
    }

    private void QueueRecentRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(150);
            _refreshQueued = false;
            await LoadRecentAsync();
        });
    }

    private async Task LoadRecentAsync()
    {
        var history = await _library.GetHistoryAsync(30);
        var items = history.Where(d => d.Track != null)
            .GroupBy(d => d.TrackId).Select(g => g.First())
            .Take(6)
            .Select(d => new TrackItem(d.Track!, d))
            .ToList();
        RecentList.ItemsSource = items;
        RecentSection.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TrackItem item)
            (Window.GetWindow(this) as MainWindow)?.ShowTrack(item.Track);
    }

    private void AllHistory_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("library");

    private void Source_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("settings");
}
