using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Shazim.App.Infrastructure;
using Shazim.Core.Models;
using Shazim.Core.Services;

namespace Shazim.App.Views;

/// <summary>Statistics: totals, the last 30 days, highlights, top artists and genres, sources.</summary>
public partial class StatsView : UserControl, IRefreshable
{
    private readonly LibraryService _library;
    private readonly ArtworkConverter _art = new();
    private bool _dirty = true;

    public StatsView(IServiceProvider services)
    {
        _library = services.GetRequiredService<LibraryService>();
        InitializeComponent();
        _library.Changed += (_, _) => _dirty = true;
    }

    public async Task RefreshAsync()
    {
        if (!_dirty) return;
        _dirty = false;
        var stats = await _library.GetStatsAsync(DateTime.Now);
        Show(stats);
    }

    private void Show(LibraryStats s)
    {
        var culture = Loc.Instance.Culture;
        EmptyPanel.Visibility = s.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        StatsBody.Visibility = s.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (s.IsEmpty) return;

        TracksValue.Text = s.TrackCount.ToString("N0", culture);
        PlaysValue.Text = s.PlayCount.ToString("N0", culture);
        MonthValue.Text = s.PlaysThisMonth.ToString("N0", culture);
        FavValue.Text = s.FavoriteCount.ToString("N0", culture);

        // Bar chart of the last days.
        int max = Math.Max(1, s.LastDays.Max(d => d.Count));
        DaysChart.ItemsSource = s.LastDays.Select(d => new
        {
            BarHeight = d.Count == 0 ? 2 : 6 + 114.0 * d.Count / max,
            BarOpacity = d.Count == 0 ? 0.25 : d.Day == DateTime.Today ? 1.0 : 0.75,
            Tip = $"{d.Day.ToString("ddd d MMM", culture)} : {Formats.Times(d.Count)}"
        }).ToList();
        DaysStart.Text = s.LastDays.Count > 0 ? s.LastDays[0].Day.ToString("d MMM", culture) : "";
        DaysTotal.Text = Loc.F("stats.days_total", s.LastDays.Sum(d => d.Count));

        // Highlights.
        Highlights.Children.Clear();
        if (s.TrackOfTheMonth is { } month)
            Highlights.Children.Add(Highlight(Loc.T("stats.track_of_month"), month.Track, Loc.F("fmt.shazimed_times", Formats.Times(month.Count))));
        if (s.MostShazimed is { } most)
            Highlights.Children.Add(Highlight(Loc.T("stats.most_shazimed"), most.Track, Loc.F("fmt.shazimed_times", Formats.Times(most.Count))));
        if (s.FirstEver is { Track: { } firstTrack } first)
            Highlights.Children.Add(Highlight(Loc.T("stats.first_ever"), firstTrack, Formats.Full(first.DetectedAt)));

        ArtistList.ItemsSource = Rank(s.TopArtists);
        GenreList.ItemsSource = s.TopGenres.Count > 0 ? Rank(s.TopGenres) : new[] { Row(0, Loc.T("stats.no_genre"), 0, 1, "") };

        int total = Math.Max(1, s.PlayCount);
        string Share(int n) => $"{n} · {Math.Round(100.0 * n / total)} %";
        SourceList.ItemsSource = new[]
        {
            Row(0, Loc.T("home.son_du_pc"), s.PcCount, total, Share(s.PcCount)),
            Row(0, Loc.T("settings.micro"), s.MicCount, total, Share(s.MicCount)),
            Row(0, Loc.T("mainwin.auto_shazim"), s.AutoCount, total, Share(s.AutoCount)),
            Row(0, Loc.T("detail.manual"), s.ManualCount, total, Share(s.ManualCount)),
        };
    }

    private static object[] Rank(IReadOnlyList<NamedCount> items)
    {
        int max = Math.Max(1, items.Count > 0 ? items.Max(i => i.Count) : 1);
        return items.Select((x, i) => Row(i + 1, x.Name, x.Count, max, Formats.Times(x.Count))).ToArray();
    }

    private static object Row(int rank, string name, int count, int max, string countText) => new
    {
        Rank = rank > 0 ? rank.ToString() : "",
        Name = name,
        BarWidth = 300.0 * count / Math.Max(1, max),
        CountText = countText
    };

    /// <summary>A card with the artwork, a label, the track and a detail line. Click opens the track.</summary>
    private UIElement Highlight(string label, Track track, string detail)
    {
        var art = new Grid { Width = 64, Height = 64, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        var placeholder = new Border { CornerRadius = new CornerRadius(8) };
        placeholder.SetResourceReference(Border.BackgroundProperty, "ArtPlaceholderBrush");
        placeholder.Child = new TextBlock
        {
            Text = ArtInitial.Of(track), Style = (Style)FindResource("SectionTitle"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        art.Children.Add(placeholder);
        if (_art.Convert(track.AlbumArtUrl, typeof(ImageSource), "64", null!) is ImageSource img)
            art.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill } });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var labelText = new TextBlock { Text = label, Style = (Style)FindResource("MutedText"), FontSize = 11 };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "SignalBrush");
        texts.Children.Add(labelText);
        texts.Children.Add(new TextBlock { Text = track.DisplayTitle, Style = (Style)FindResource("BodyText"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) });
        texts.Children.Add(new TextBlock { Text = track.DisplayArtist, Style = (Style)FindResource("MutedText"), FontSize = 12 });
        texts.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("MutedText"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });

        var row = new DockPanel();
        DockPanel.SetDock(art, Dock.Left);
        row.Children.Add(art);
        row.Children.Add(texts);

        var button = new Button
        {
            Style = (Style)FindResource("BaseButton"), Content = new Border { Style = (Style)FindResource("Panel"), Child = row },
            Margin = new Thickness(0, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0),
            ToolTip = $"{track.DisplayArtist} – {track.DisplayTitle}"
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{label} : {track.DisplayTitle}, {track.DisplayArtist}");
        button.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.ShowTrack(track);
        return button;
    }
}
