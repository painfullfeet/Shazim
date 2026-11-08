using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shazim.Core.Models;
using Shazim.Core.Services;

namespace Shazim.App;

/// <summary>
/// Small app-styled card shown when a track is found (instead of the Windows notification):
/// slides in above the taskbar, never takes the focus, hides itself after a few seconds
/// (not while the mouse is over it).
/// </summary>
public partial class ToastWindow : Window
{
    private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(6);

    private readonly LibraryService _library;
    private readonly Action _open;
    private readonly DispatcherTimer _timer;
    private Track _track;
    private bool _closing;

    public ToastWindow(LibraryService library, Track track, string badge, Action open)
    {
        _library = library;
        _track = track;
        _open = open;
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = Visible };
        _timer.Tick += (_, _) => Dismiss();

        SourceInitialized += (_, _) =>
        {
            // No taskbar button, no Alt+Tab entry, and clicking it does not steal the focus.
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) => { Place(); Enter(); StartAura(); _timer.Start(); };
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { if (!_closing) { _timer.Stop(); _timer.Start(); } };
        Show(track, badge);
    }

    /// <summary>The halo breathes around the card; in multicolour mode it turns through the rainbow.</summary>
    private void StartAura()
    {
        if (Infrastructure.Shell.ReduceMotion) return;
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        var period = TimeSpan.FromSeconds(1.6);
        AuraScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1.03, period) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        AuraScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1.08, period) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        Aura.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 0.6, period) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine });
        if (Services.ThemeService.IsRainbow)
        {
            double h = Services.ThemeService.RainbowHue;
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Services.ThemeService.FromHue(h), 0),
                    new GradientStop(Services.ThemeService.FromHue((h + 120) % 360), 0.5),
                    new GradientStop(Services.ThemeService.FromHue((h + 240) % 360), 1)
                },
                RelativeTransform = new RotateTransform(0, 0.5, 0.5)
            };
            Aura.Background = brush;
            ((RotateTransform)brush.RelativeTransform).BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3)) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    /// <summary>Shows another track in the same card (several finds in a row).</summary>
    public void Show(Track track, string badge)
    {
        _track = track;
        BadgeText.Text = badge;
        TitleText.Text = track.DisplayTitle;
        ArtistText.Text = track.DisplayArtist;
        ArtInitial.Text = Infrastructure.ArtInitial.Of(track);
        ArtBrush.ImageSource = new Infrastructure.ArtworkConverter().Convert(track.AlbumArtUrl, typeof(ImageSource), "64", null!) as ImageSource;
        FavButton.IsChecked = track.IsFavorite;
        if (IsLoaded) { _timer.Stop(); _timer.Start(); }
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 4;
        Top = area.Bottom - ActualHeight - 4;
    }

    private void Enter()
    {
        if (Infrastructure.Shell.ReduceMotion) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(60, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    public void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        if (Infrastructure.Shell.ReduceMotion) { Close(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, TimeSpan.FromMilliseconds(180)));
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private async void Fav_Click(object sender, RoutedEventArgs e)
    {
        _track.IsFavorite = await _library.ToggleFavoriteAsync(_track.Id);
        FavButton.IsChecked = _track.IsFavorite;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        _open();
        Dismiss();
    }

    private void OpenArea_Click(object sender, MouseButtonEventArgs e) => Open_Click(sender, e);

    private void Close_Click(object sender, RoutedEventArgs e) => Dismiss();
}
