using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Shazim.App.Infrastructure;
using Shazim.App.Services;
using Shazim.Core.Models;
using Shazim.Core.Services;

namespace Shazim.App.Views;

/// <summary>
/// Full track sheet: artwork, preview, platforms, details and listening history.
/// Used for the home result and for the library pop-up.
/// </summary>
public partial class TrackDetailView : UserControl
{
    private readonly LibraryService _library;
    private readonly PreviewPlayer _player;
    private readonly ArtworkConverter _art = new();
    private readonly bool _showHistory;
    private Track? _track;

    public TrackDetailView(IServiceProvider services, bool showHistory, bool allowDelete)
    {
        _library = services.GetRequiredService<LibraryService>();
        _player = services.GetRequiredService<PreviewPlayer>();
        _showHistory = showHistory;
        InitializeComponent();

        DeleteButton.Visibility = allowDelete ? Visibility.Visible : Visibility.Collapsed;
        _player.PropertyChanged += OnPlayerChanged;
        Unloaded += (_, _) => _player.PropertyChanged -= OnPlayerChanged;
        Loaded += (_, _) => { _player.PropertyChanged -= OnPlayerChanged; _player.PropertyChanged += OnPlayerChanged; UpdatePlayer(); };
    }

    public Track? Track => _track;

    /// <summary>
    /// Shazam-style reveal: the artwork springs in, then each section rises in turn.
    /// </summary>
    public void PlayReveal()
    {
        if (Shell.ReduceMotion) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var artScale = new ScaleTransform(1, 1);
        ArtBox.RenderTransformOrigin = new Point(0.5, 0.5);
        ArtBox.RenderTransform = artScale;
        var spring = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
        artScale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
        artScale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
        ArtBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));

        int i = 0;
        foreach (UIElement child in Body.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            var delay = TimeSpan.FromMilliseconds(70 + 55 * i++);
            var shift = new TranslateTransform();
            child.RenderTransform = shift;
            child.Opacity = 0;
            child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(340)) { BeginTime = delay, EasingFunction = ease });
        }
    }

    /// <summary>Extra room at the top (for a back button laid over the sheet).</summary>
    public void SetTopInset(double top) => Body.Margin = new Thickness(36, top, 36, 28);

    /// <summary>Raised after the user deleted the track.</summary>
    public event EventHandler? Deleted;

    public async Task ShowAsync(Track track, string? badge = null)
    {
        _track = track;
        Scroller.ScrollToTop();

        Badge.Text = badge ?? "";
        Badge.Visibility = string.IsNullOrEmpty(badge) ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = track.DisplayTitle;
        ArtistText.Text = track.DisplayArtist;
        MetaText.Text = track.MetaLine;
        MetaText.Visibility = string.IsNullOrEmpty(track.MetaLine) ? Visibility.Collapsed : Visibility.Visible;
        ArtInitial.Text = Infrastructure.ArtInitial.Of(track);
        ArtBrush.ImageSource = _art.Convert(track.AlbumArtUrl, typeof(ImageSource), "200", null!) as ImageSource;
        Backdrop.Source = _art.Convert(track.AlbumArtUrl, typeof(ImageSource), "120", null!) as ImageSource;
        FavButton.IsChecked = track.IsFavorite;
        ShazamButton.Visibility = track.FindLink("Shazam") != null ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.IsEnabled = track.HasPreview;
        PlayButton.ToolTip = track.HasPreview ? null : Loc.T("detail.no_preview");
        StatusText.Text = "";

        BuildPlatforms(track);
        BuildInfo(track);
        UpdatePlayer();
        await LoadHistoryAsync(track);
    }

    // ===== Plateformes =====

    private void BuildPlatforms(Track track)
    {
        PlatformsPanel.Children.Clear();
        var links = track.Platforms.ToList();
        var youtube = track.FindLink("YouTube");

        foreach (var link in links)
            PlatformsPanel.Children.Add(PlatformButton(link.Platform, link.Url, primary: true));
        if (youtube != null)
            PlatformsPanel.Children.Add(PlatformButton("YouTube", youtube, primary: false));

        PlatformsSection.Visibility = PlatformsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button PlatformButton(string platform, string url, bool primary)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10, Margin = new Thickness(0, 0, 10, 0),
            Fill = new SolidColorBrush(PlatformColors.For(platform)), VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock { Text = primary ? platform : Loc.T("detail.search_youtube"), VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, label } };
        var b = new Button
        {
            Content = content,
            Style = (Style)FindResource("LinkPill"),
            Padding = new Thickness(16, 10, 18, 10),
            Tag = url,
            ToolTip = primary ? Loc.F("detail.listen_on", platform) : Loc.T("detail.search_youtube_tip")
        };
        b.Click += (s, _) => Shell.Open((string)((Button)s).Tag);
        return b;
    }

    // ===== Informations =====

    private void BuildInfo(Track track)
    {
        InfoGrid.Children.Clear();
        AddInfo(Loc.T("detail.released"), track.ReleaseYear);
        AddInfo(Loc.T("detail.album"), track.Album);
        AddInfo(Loc.T("detail.genre"), track.Genre);
        AddInfo(Loc.T("detail.label"), track.Label);
        AddInfo(Loc.T("detail.shazimed"), Formats.Times(track.DetectionCount));
        if (track.FirstDetectedAt is { } first) AddInfo(Loc.T("detail.first_time"), Formats.Relative(first), Formats.Full(first));
        if (track.LastDetectedAt is { } last && track.DetectionCount > 1) AddInfo(Loc.T("detail.last_time"), Formats.Relative(last), Formats.Full(last));
    }

    private void AddInfo(string label, string? value, string? tooltip = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var panel = new StackPanel { Margin = new Thickness(0, 6, 16, 10), ToolTip = tooltip };
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)Resources["InfoLabel"] });
        panel.Children.Add(new TextBlock { Text = value, Style = (Style)Resources["InfoValue"] });
        InfoGrid.Children.Add(panel);
    }

    private async Task LoadHistoryAsync(Track track)
    {
        if (!_showHistory) { HistorySection.Visibility = Visibility.Collapsed; return; }

        var history = await _library.GetTrackHistoryAsync(track.Id);
        if (_track?.Id != track.Id) return;
        HistoryList.ItemsSource = history.Select(d => new
        {
            When = Formats.Full(d.DetectedAt),
            How = (d.IsAuto ? Loc.T("mainwin.auto_shazim") : Loc.T("detail.manual")) + (string.IsNullOrEmpty(d.AudioDevice) ? "" : " · " + d.AudioDevice)
        }).ToList();
        HistoryTitle.Text = history.Count > 1 ? Loc.F("detail.history_count", history.Count) : Loc.T("trackdetail.historique");
        HistorySection.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Extrait =====

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e) => UpdatePlayer();

    private void UpdatePlayer()
    {
        bool mine = _track != null && _player.TrackId == _track.Id;
        bool playing = mine && _player.IsPlaying;
        PlayIcon.Data = IconView.Get(playing ? "IconStopFill" : "IconPlayFill");
        PlayText.Text = playing ? Loc.T(_player.IsLoading ? "detail.loading" : "detail.stop_preview") : Loc.T("library.ecouter_l_extrait");
        PreviewTrack.Visibility = playing ? Visibility.Visible : Visibility.Hidden;
        PreviewBar.Width = PreviewTrack.Width * (playing ? _player.Progress : 0);
        if (_track != null && _player.ErrorTrackId == _track.Id) StatusText.Text = Loc.T("detail.preview_unavailable");
        else if (StatusText.Text == Loc.T("detail.preview_unavailable")) StatusText.Text = "";
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_track != null) _player.Toggle(_track);
    }

    // ===== Actions =====

    private async void Fav_Click(object sender, RoutedEventArgs e)
    {
        if (_track == null) return;
        _track.IsFavorite = await _library.ToggleFavoriteAsync(_track.Id);
        FavButton.IsChecked = _track.IsFavorite;
        StatusText.Text = Loc.T(_track.IsFavorite ? "detail.fav_added" : "detail.fav_removed");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_track == null) return;
        try
        {
            Clipboard.SetText($"{_track.DisplayArtist} – {_track.DisplayTitle}");
            StatusText.Text = Loc.T("detail.copied");
        }
        catch
        {
            StatusText.Text = Loc.T("detail.clipboard_busy");
        }
    }

    private void Shazam_Click(object sender, RoutedEventArgs e) => Shell.Open(_track?.FindLink("Shazam"));

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_track == null) return;
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            Loc.F("detail.delete_confirm", _track.DisplayTitle),
            Loc.T("detail.delete_title"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        if (_player.TrackId == _track.Id) _player.Stop();
        await _library.DeleteTrackAsync(_track.Id);
        Deleted?.Invoke(this, EventArgs.Empty);
    }
}
