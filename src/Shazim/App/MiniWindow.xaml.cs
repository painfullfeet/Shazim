using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Shazim.App.Services;
using Shazim.Core.Models;
using Shazim.Core.Recognition;

namespace Shazim.App;

/// <summary>
/// Compact always-on-top widget: artwork, title, artist, Auto Shazim, identify,
/// and the current line of the synced lyrics. Click opens the main window, drag moves it.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly IdentifyController _controller;
    private readonly AutoShazamService _auto;
    private readonly NowPlayingService _now;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _lyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Point _downAt;
    private bool _dragged;

    public MiniWindow(IServiceProvider services)
    {
        _controller = services.GetRequiredService<IdentifyController>();
        _auto = services.GetRequiredService<AutoShazamService>();
        _now = services.GetRequiredService<NowPlayingService>();
        _settings = services.GetRequiredService<SettingsService>();
        InitializeComponent();
        Logo.Source = Infrastructure.Brand.Logo;

        var w = _settings.GetSettings().Widget;
        Opacity = w.Opacity;
        Loaded += (_, _) => PlaceWindow();

        _controller.PropertyChanged += OnControllerChanged;
        _auto.StatusChanged += OnAutoChanged;
        _now.Changed += OnNowChanged;
        _settings.Saved += OnSettingsSaved;
        _lyricsTimer.Tick += (_, _) => UpdateLyrics();
        Closed += (_, _) =>
        {
            _controller.PropertyChanged -= OnControllerChanged;
            _auto.StatusChanged -= OnAutoChanged;
            _now.Changed -= OnNowChanged;
            _settings.Saved -= OnSettingsSaved;
            _lyricsTimer.Stop();
        };
        MouseEnter += (_, _) => FadeClose(1);
        MouseLeave += (_, _) => FadeClose(0);
        LocationChanged += (_, _) => _positionDirty = true;
        Deactivated += (_, _) => SavePosition();
        Closing += (_, _) => SavePosition();

        Update();
        UpdateAuto();
        OnNowChanged(null, EventArgs.Empty);
    }

    private bool _positionDirty;

    /// <summary>Last position if it is still on a screen, else bottom-right.</summary>
    private void PlaceWindow()
    {
        var w = _settings.GetSettings().Widget;
        var area = SystemParameters.WorkArea;
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
        if (w.Left is { } l && w.Top is { } t && l >= vl - 20 && t >= vt - 20 && l + 80 <= vr && t + 40 <= vb)
        {
            Left = l;
            Top = t;
        }
        else
        {
            Left = area.Right - ActualWidth - 20;
            Top = area.Bottom - ActualHeight - 20;
        }
        _positionDirty = false;
    }

    private void SavePosition()
    {
        if (!_positionDirty) return;
        _positionDirty = false;
        var w = _settings.GetSettings().Widget;
        w.Left = Math.Round(Left);
        w.Top = Math.Round(Top);
        _settings.Save();
    }

    private void OnSettingsSaved(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        Opacity = _settings.GetSettings().Widget.Opacity;
        UpdateLyrics();
    });

    private void FadeClose(double to)
    {
        if (Infrastructure.Shell.ReduceMotion) { CloseButton.Opacity = to; return; }
        CloseButton.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)));
    }

    // ===== État =====

    private void OnControllerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IdentifyController.IsBusy) or nameof(IdentifyController.Phase) or nameof(IdentifyController.LastOutcome))
            Update();
    }

    private void OnAutoChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateAuto);

    private void UpdateAuto()
    {
        AutoButton.IsChecked = _auto.IsEnabled;
        AutoText.Text = _auto.IsEnabled ? Loc.T("mainwin.auto_shazim") + " · " + _auto.StatusText : Loc.T("widget.auto_off");
        AutoDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, _auto.State switch
        {
            AutoShazamState.Listening or AutoShazamState.Searching => "WaveBrush",
            AutoShazamState.Waiting => "SignalBrush",
            _ => "MistBrush"
        });
    }

    private void Update()
    {
        bool busy = _controller.IsBusy;
        ListenButton.Tag = busy ? "busy" : null;
        ListenGlyph.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        StopGlyph.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            TitleText.Text = Loc.T(_controller.Phase == IdentifyPhase.Searching ? "home.searching" : "home.listening");
            SubText.Text = Loc.T("mini.keep_sound");
            return;
        }

        var o = _controller.LastOutcome;
        if (o == null) return;
        if (o.Kind == IdentifyOutcomeKind.Match && o.Track != null)
        {
            ShowTrack(o.Track);
        }
        else
        {
            TitleText.Text = o.Kind switch
            {
                IdentifyOutcomeKind.Silence => Loc.T("home.no_sound"),
                IdentifyOutcomeKind.NoMatch => Loc.T("home.no_result"),
                _ => Loc.T("home.cannot_recognise")
            };
            SubText.Text = o.Kind == IdentifyOutcomeKind.Error ? o.Message : Loc.T("mini.try_again");
        }
    }

    private string? _shownTrack;

    private void ShowTrack(Track track)
    {
        TitleText.Text = track.DisplayTitle;
        SubText.Text = track.DisplayArtist;
        ToolTip = $"{track.DisplayArtist} – {track.DisplayTitle}";
        if (_shownTrack == track.Id) return;
        _shownTrack = track.Id;
        ArtInitial.Text = Infrastructure.ArtInitial.Of(track);
        ArtBrush.ImageSource = new Infrastructure.ArtworkConverter().Convert(track.AlbumArtUrl, typeof(ImageSource), "64", null!) as ImageSource;
        IdleArt.Visibility = Visibility.Collapsed;
    }

    // ===== Paroles =====

    private void OnNowChanged(object? sender, EventArgs e)
    {
        if (_now.Track is { } t && !_controller.IsBusy) ShowTrack(t);
        UpdateLyrics();
    }

    private int _shownLine = -2;

    private void UpdateLyrics()
    {
        var lyrics = _now.Lyrics;
        bool live = _settings.GetSettings().Widget.ShowLyrics && lyrics is { IsSynced: true } && _now.Track != null && _now.IsLive(_now.Track.Id, DateTime.UtcNow);
        if (!live)
        {
            _lyricsTimer.Stop();
            LyricsPanel.Visibility = Visibility.Collapsed;
            _shownLine = -2;
            return;
        }
        if (!_lyricsTimer.IsEnabled) _lyricsTimer.Start();
        LyricsPanel.Visibility = Visibility.Visible;

        int i = _now.CurrentLine(DateTime.UtcNow);
        if (i == _shownLine) return;
        _shownLine = i;
        var lines = lyrics!.Lines;
        string Line(int k) => k >= 0 && k < lines.Count ? (lines[k].Text.Length > 0 ? lines[k].Text : "♪") : "";
        LyricNow.Text = i < 0 ? "♪" : Line(i);
        LyricNext.Text = Line(i + 1);
        // Same motion as the track page: the line rises in, un-blurs and fills like karaoke.
        var sung = (Color)FindResource("SignalColor");
        var rest = (Color)FindResource("PaperColor");
        var dur = i >= 0 && i + 1 < lines.Count ? lines[i + 1].Time - lines[i].Time : TimeSpan.Zero;
        Infrastructure.LyricFx.Fill(LyricNow, sung, rest, dur > TimeSpan.FromSeconds(12) ? TimeSpan.FromSeconds(12) : dur);
        Infrastructure.LyricFx.Enter(LyricNow, 0.9, 10);
        Infrastructure.LyricFx.Enter(LyricNext, 0.96, 8);
        LyricNext.Opacity = 0.75;
    }

    // ===== Actions =====

    private async void Listen_Click(object sender, RoutedEventArgs e) => await _controller.ToggleAsync();

    private void Auto_Click(object sender, RoutedEventArgs e) => _auto.SetEnabled(AutoButton.IsChecked == true);

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        _downAt = PointToScreen(e.GetPosition(this));
        var before = new Point(Left, Top);
        try { DragMove(); } catch { }
        // DragMove returns when the button is released: a click if the window did not move.
        _dragged = Math.Abs(Left - before.X) > 3 || Math.Abs(Top - before.Y) > 3;
        if (!_dragged) OpenMain();
        else SavePosition();
    }

    private void Card_MouseUp(object sender, MouseButtonEventArgs e) { }

    private void OpenMain()
    {
        App.Current.ShowMainWindow();
        if (_now.Track is { } t) App.Current.ShowTrackInMain(t);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
