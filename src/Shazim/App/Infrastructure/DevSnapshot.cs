using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Shazim.App.Infrastructure;

/// <summary>
/// Developer aid for README screenshots: when SHAZIM_SNAPSHOT_DIR is set, writing a file
/// "&lt;name&gt;.request" in that folder renders the app's visible windows to "&lt;name&gt;.png"
/// (offscreen, so other windows on the desktop never end up in the picture).
/// Does nothing unless the variable is set.
/// </summary>
public static class DevSnapshot
{
    public static void Start(Dispatcher dispatcher)
    {
        var dir = Environment.GetEnvironmentVariable("SHAZIM_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            // "<any>.cmd" files drive the UI without touching the real mouse/keyboard:
            // "nav library", "track 3", "scroll end", "lang en", "close".
            foreach (var cmdFile in Directory.GetFiles(dir, "*.cmd"))
            {
                string cmd;
                try { cmd = File.ReadAllText(cmdFile).Trim(); File.Delete(cmdFile); } catch { continue; }
                try { _ = RunAsync(cmd); } catch { }
            }

            foreach (var request in Directory.GetFiles(dir, "*.request"))
            {
                var name = Path.GetFileNameWithoutExtension(request);
                try { File.Delete(request); } catch { continue; }
                int i = 0;
                foreach (Window w in Application.Current.Windows)
                {
                    if (!w.IsVisible || w.ActualWidth < 50) continue;
                    Render(w, Path.Combine(dir, i++ == 0 ? name + ".png" : $"{name}-{i}.png"));
                }
            }
        }, dispatcher);
        timer.Start();
    }

    private static async Task RunAsync(string command)
    {
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var arg = parts.Length > 1 ? parts[1] : "";
        var main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
        switch (parts[0])
        {
            case "nav": main?.CloseTrack(); main?.Navigate(arg); break;
            case "lang": App.Current.ApplyLanguage(arg); break;
            case "close": main?.CloseTrack(); break;
            case "track":
                var library = (Core.Services.LibraryService)App.Current.Services.GetService(typeof(Core.Services.LibraryService))!;
                var tracks = await library.SearchAsync(null);
                if (int.TryParse(arg, out int i) && i < tracks.Count) main?.ShowTrack(tracks[i]);
                break;
            case "scroll":
                if (main != null && FindScroller(main) is { } sv)
                {
                    if (arg == "end") sv.ScrollToEnd();
                    else if (double.TryParse(arg, out var y)) sv.ScrollToVerticalOffset(y);
                }
                break;
        }
    }

    private static System.Windows.Controls.ScrollViewer? FindScroller(DependencyObject root)
    {
        // The biggest visible scroller is the page's own.
        System.Windows.Controls.ScrollViewer? best = null;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is System.Windows.Controls.ScrollViewer { IsVisible: true } sv && sv.ScrollableHeight > 0 &&
                (best == null || sv.ActualHeight > best.ActualHeight)) best = sv;
            for (int k = 0; k < VisualTreeHelper.GetChildrenCount(d); k++) stack.Push(VisualTreeHelper.GetChild(d, k));
        }
        return best;
    }

    private static void Render(Window w, string path)
    {
        if (w.Content is not FrameworkElement root) return;
        var dpi = VisualTreeHelper.GetDpi(w);
        int width = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);

        // Paint the window background first (transparent windows keep their alpha).
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (w.Background is SolidColorBrush { Color.A: > 0 } bg)
                dc.DrawRectangle(bg, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
