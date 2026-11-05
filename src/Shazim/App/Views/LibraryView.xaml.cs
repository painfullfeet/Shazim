using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Shazim.App.Infrastructure;
using Shazim.App.Services;
using Shazim.Core.Services;

namespace Shazim.App.Views;

/// <summary>Every track ever Shazimed: search, filter, sort, preview, open the sheet.</summary>
public partial class LibraryView : UserControl, IRefreshable
{
    private readonly LibraryService _library;
    private readonly PreviewPlayer _player;
    private readonly DispatcherTimer _searchDebounce;
    private List<TrackItem> _items = new();
    private bool _dirty = true;
    private int _loadVersion;

    public LibraryView(IServiceProvider services)
    {
        _library = services.GetRequiredService<LibraryService>();
        _player = services.GetRequiredService<PreviewPlayer>();
        InitializeComponent();

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchDebounce.Tick += async (_, _) => { _searchDebounce.Stop(); await LoadAsync(); };

        _library.Changed += (_, _) => Dispatcher.BeginInvoke(async () =>
        {
            _dirty = true;
            if (IsVisible) await LoadAsync();
        });
        _player.PropertyChanged += OnPlayerChanged;
    }

    public Task RefreshAsync() => _dirty ? LoadAsync() : Task.CompletedTask;

    private LibrarySort CurrentSort =>
        (SortCombo.SelectedItem as ComboBoxItem)?.Tag is string tag && Enum.TryParse<LibrarySort>(tag, out var s) ? s : LibrarySort.Recent;

    private bool FavoritesOnly => FilterFav.IsChecked == true;

    private async Task LoadAsync()
    {
        int version = ++_loadVersion;
        var query = SearchBox.Text;
        var tracks = await _library.SearchAsync(query, CurrentSort);
        if (FavoritesOnly) tracks = tracks.Where(t => t.IsFavorite).ToList();
        if (version != _loadVersion) return; // a newer load started meanwhile

        _items = tracks.Select(t => new TrackItem(t) { IsPlaying = _player.IsPlayingTrack(t.Id) }).ToList();
        TrackList.ItemsSource = _items;
        _dirty = false;

        int total = await _library.GetTrackCountAsync();
        int plays = await _library.GetDetectionCountAsync();
        CountText.Text = total switch
        {
            0 => "",
            1 => Loc.F("lib.count_one", Formats.Times(plays)),
            _ => Loc.F("lib.count_many", total, plays)
        };
        UpdateEmptyState(tracks.Count, total, query);
    }

    private void UpdateEmptyState(int shown, int total, string query)
    {
        bool empty = shown == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        TrackList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        Toolbar.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (!empty) return;

        EmptyAction.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(query))
        {
            EmptyIcon.Data = IconView.Get("IconMagnifyingGlass");
            EmptyTitle.Text = Loc.T("home.no_result");
            EmptyText.Text = Loc.F("lib.no_match", query.Trim());
        }
        else if (FavoritesOnly)
        {
            EmptyIcon.Data = IconView.Get("IconHeart");
            EmptyTitle.Text = Loc.T("lib.no_fav_title");
            EmptyText.Text = Loc.T("lib.no_fav_text");
        }
        else
        {
            EmptyIcon.Data = IconView.Get("IconMusicNotes");
            EmptyTitle.Text = Loc.T("lib.empty_title");
            EmptyText.Text = Loc.T("lib.empty_text");
            EmptyAction.Visibility = Visibility.Visible;
        }
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(PreviewPlayer.IsPlaying) or nameof(PreviewPlayer.TrackId))) return;
        foreach (var item in _items) item.IsPlaying = _player.IsPlayingTrack(item.Track.Id);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) await LoadAsync();
    }

    private async void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) await LoadAsync();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is TrackItem item) _player.Toggle(item.Track);
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not TrackItem item) return;
        item.IsFavorite = await _library.ToggleFavoriteAsync(item.Track.Id);
        if (sender is ToggleButton tb) tb.IsChecked = item.IsFavorite;
    }

    /// <summary>A click on a row (not on its buttons) opens the track sheet.</summary>
    private void TrackList_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        for (var d = source; d != null && d != TrackList; d = VisualTreeHelper.GetParent(d))
            if (d is ButtonBase) return;
        if (ItemsControl.ContainerFromElement(TrackList, source) is ListBoxItem { DataContext: TrackItem item })
            Open(item);
    }

    private void TrackList_KeyDown(object sender, KeyEventArgs e)
    {
        if (TrackList.SelectedItem is not TrackItem item) return;
        if (e.Key == Key.Enter) { Open(item); e.Handled = true; }
        else if (e.Key == Key.Space) { _player.Toggle(item.Track); e.Handled = true; }
    }

    private void Open(TrackItem item) => (Window.GetWindow(this) as MainWindow)?.ShowTrack(item.Track);

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("lib.export_title"),
            FileName = $"bibliotheque-shazim-{DateTime.Now:yyyy-MM-dd}",
            Filter = Loc.T("lib.export_filter"),
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            await _library.ExportAsync(dialog.FileName);
            CountText.Text = Loc.F("lib.exported", System.IO.Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this)!, Loc.F("lib.export_failed", ex.Message), Loc.T("lib.export_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EmptyAction_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("home");
}
