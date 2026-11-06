using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Shazim.App.Services;
using Shazim.Core.Updates;

namespace Shazim.App.Views;

/// <summary>"Mise à jour disponible": notes, download with progress, then hand-over to the installer.</summary>
public partial class UpdateDialog : UserControl
{
    private readonly UpdateService _updates;
    private readonly ReleaseInfo _release;
    private CancellationTokenSource? _cts;

    public UpdateDialog(UpdateService updates, ReleaseInfo release)
    {
        _updates = updates;
        _release = release;
        InitializeComponent();

        TitleText.Text = Loc.F("update.available", release.DisplayVersion);
        VersionText.Text = Loc.F("update.current", UpdateService.CurrentVersion.ToString(3));
        var notes = UpdateParser.Summary(release.Notes);
        NotesText.Text = notes;
        NotesText.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Raised when the card should go away.</summary>
    public event EventHandler? Dismissed;

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = SkipButton.IsEnabled = false;
        LaterButton.Content = Loc.T("update.cancel");
        ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("MistBrush");
        StatusText.Text = Loc.T("update.downloading");
        _cts = new CancellationTokenSource();

        var progress = new Progress<double>(p =>
        {
            ProgressFill.Width = ProgressTrack.ActualWidth * p;
            StatusText.Text = Loc.F("update.downloading_percent", Math.Round(p * 100));
        });

        try
        {
            var path = await _updates.DownloadAsync(_release, progress, _cts.Token);
            StatusText.Text = Loc.T("update.installing");
            await Task.Delay(400);
            _updates.Install(path);
            App.Current.ExitApp();
        }
        catch (OperationCanceledException)
        {
            Reset("");
        }
        catch (UpdateException ex)
        {
            Reset(ex.Message);
        }
        catch (Exception ex)
        {
            Reset(Loc.T("update.download_failed") + " " + ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Reset(string error)
    {
        UpdateButton.IsEnabled = SkipButton.IsEnabled = true;
        LaterButton.Content = Loc.T("update.later");
        ProgressPanel.Visibility = Visibility.Collapsed;
        ProgressFill.Width = 0;
        StatusText.Text = error;
        StatusText.Visibility = error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _updates.Skip(_release);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    public void PlayEntrance()
    {
        if (Shell.ReduceMotion) return;
        var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
        var scale = new System.Windows.Media.ScaleTransform(0.94, 0.94);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }
}
