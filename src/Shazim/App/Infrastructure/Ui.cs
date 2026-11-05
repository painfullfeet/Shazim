using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shazim.Core.Models;

namespace Shazim.App.Infrastructure;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>List row wrapper so favorite changes refresh without reloading the list.</summary>
public sealed class TrackItem : ObservableObject
{
    private bool _isFavorite;

    public TrackItem(Track track, Detection? detection = null)
    {
        Track = track;
        Detection = detection;
        _isFavorite = track.IsFavorite;
    }

    public Track Track { get; }
    public Detection? Detection { get; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (Set(ref _isFavorite, value)) Track.IsFavorite = value; }
    }

    public string Title => Track.DisplayTitle;
    public string Artist => Track.DisplayArtist;
    public string Album => Track.DisplayAlbum;
    public string ArtUrl => Track.AlbumArtUrl;
    public string Initial => ArtInitial.Of(Track);
    public string AccessibleName => $"{Title}, {Artist}";
    public override string ToString() => AccessibleName;

    public string CountLabel => Track.DetectionCount switch
    {
        0 => "",
        var n => Loc.F("fmt.shazimed_times", Formats.Times(n))
    };

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; set { if (Set(ref _isPlaying, value)) OnPropertyChanged(nameof(PlayIcon)); } }
    public Geometry? PlayIcon => IconView.Get(_isPlaying ? "IconStopFill" : "IconPlayFill");
    public bool HasPreview => Track.HasPreview;

    public string LastHeardLabel => Track.LastDetectedAt is { } d ? Formats.Relative(d) : "";

    public string TimeLabel => Detection?.FormattedTime ?? "";
    public string ModeLabel => Detection == null ? "" : Detection.IsAuto ? "Auto" : "Manuel";
    public string DayLabel => Detection?.DayLabel ?? "";
}

public static class Formats
{

    /// <summary>"à l'instant", "il y a 5 min", "hier à 21:04", "3 nov. 2026".</summary>
    public static string Relative(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Local ? utc : utc.ToLocalTime();
        var ago = DateTime.Now - local;
        var culture = Loc.Instance.Culture;
        if (ago < TimeSpan.FromMinutes(1)) return Loc.T("fmt.just_now");
        if (ago < TimeSpan.FromHours(1)) return Loc.F("fmt.minutes_ago", (int)ago.TotalMinutes);
        if (local.Date == DateTime.Today) return Loc.F("fmt.today_at", local.ToString("t", culture));
        if (local.Date == DateTime.Today.AddDays(-1)) return Loc.F("fmt.yesterday_at", local.ToString("t", culture));
        return local.ToString(local.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", culture);
    }

    public static string Full(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Local ? utc : utc.ToLocalTime();
        var culture = Loc.Instance.Culture;
        return Loc.F("fmt.full_date", local.ToString("D", culture), local.ToString("t", culture));
    }

    public static string Times(int n) => n switch { 0 => Loc.T("fmt.never"), 1 => Loc.T("fmt.once"), _ => Loc.F("fmt.times", n) };
}

/// <summary>Brand colours of the streaming platforms, for the "Disponible sur" buttons.</summary>
public static class PlatformColors
{
    public static Color For(string platform) => platform switch
    {
        "Spotify" => Color.FromRgb(0x1E, 0xD7, 0x60),
        "Apple Music" => Color.FromRgb(0xFA, 0x24, 0x3C),
        "Deezer" => Color.FromRgb(0xA2, 0x38, 0xFF),
        "YouTube Music" or "YouTube" => Color.FromRgb(0xFF, 0x00, 0x33),
        _ => Color.FromRgb(0xA3, 0x9E, 0xC6)
    };
}

/// <summary>The Shazim logo and icon, embedded in the executable.</summary>
public static class Brand
{
    private static ImageSource? _logo;
    private static bool _loaded;

    public static ImageSource? Logo
    {
        get
        {
            if (_loaded) return _logo;
            _loaded = true;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/Assets/logo.png");
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _logo = bmp;
            }
            catch { _logo = null; }
            return _logo;
        }
    }

    /// <summary>Opens the embedded .ico (multi-size), or null.</summary>
    public static System.IO.Stream? OpenIcon()
    {
        try { return Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"))?.Stream; }
        catch { return null; }
    }
}

public static class ArtInitial
{
    public static string Of(Track t)
    {
        var s = !string.IsNullOrWhiteSpace(t.Artist) ? t.Artist : t.Title;
        foreach (var c in s ?? "")
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        return "♪";
    }
}

/// <summary>
/// Loads cover art from a URL, decoded at the size it is displayed (saves memory),
/// with a small in-memory cache. ConverterParameter = decode width in pixels.
/// </summary>
public sealed class ArtworkConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> Cache = new();
    private static readonly LinkedList<string> Order = new();
    private const int MaxEntries = 300;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        int width = parameter is string p && int.TryParse(p, out var w) ? w : 300;
        var key = width + "|" + url;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            // Ask the CDN for an image close to the displayed size, and keep it on disk.
            var sized = SizedUrl(url, (int)Math.Ceiling(width * DpiScale()));
            var source = DiskCache.TryGet(sized, out var file) ? new Uri(file) : new Uri(sized);
            if (!source.IsFile) DiskCache.Fetch(sized);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = source;
            bmp.DecodePixelWidth = (int)(width * DpiScale());
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();

            Cache[key] = bmp;
            Order.AddLast(key);
            if (Order.Count > MaxEntries)
            {
                Cache.Remove(Order.First!.Value);
                Order.RemoveFirst();
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex AppleSize =
        new(@"/\d+x\d+(\w*)\.(jpg|jpeg|png|webp)$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>mzstatic.com (Apple) URLs encode the size: ".../800x800cc.jpg".</summary>
    internal static string SizedUrl(string url, int pixels)
    {
        if (!url.Contains("mzstatic.com", StringComparison.OrdinalIgnoreCase)) return url;
        int size = pixels <= 120 ? 120 : pixels <= 200 ? 200 : pixels <= 400 ? 400 : 600;
        return AppleSize.Replace(url, m => $"/{size}x{size}{m.Groups[1].Value}.{m.Groups[2].Value}");
    }

    private static double DpiScale()
    {
        var main = Application.Current?.MainWindow;
        return main != null ? VisualTreeHelper.GetDpi(main).DpiScaleX : 1.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Cover art cached in %LOCALAPPDATA%\Shazim\art (capped at ~60 MB).</summary>
internal static class DiskCache
{
    private static readonly string Dir = Shazim.Core.AppPaths.ArtDirectory;
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly HashSet<string> Pending = new();
    private const long MaxBytes = 60L * 1024 * 1024;

    private static string PathFor(string url)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        return System.IO.Path.Combine(Dir, hash + ".img");
    }

    public static bool TryGet(string url, out string file)
    {
        file = PathFor(url);
        return System.IO.File.Exists(file);
    }

    /// <summary>Downloads in the background for next time.</summary>
    public static void Fetch(string url)
    {
        lock (Pending) if (!Pending.Add(url)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                System.IO.Directory.CreateDirectory(Dir);
                var file = PathFor(url);
                var tmp = file + ".tmp";
                await System.IO.File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
                System.IO.File.Move(tmp, file, overwrite: true);
                Trim();
            }
            catch { /* offline or gone: the network URL is used instead */ }
            finally { lock (Pending) Pending.Remove(url); }
        });
    }

    private static void Trim()
    {
        var files = new System.IO.DirectoryInfo(Dir).GetFiles("*.img").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        long total = 0;
        foreach (var f in files)
        {
            total += f.Length;
            if (total > MaxBytes) { try { f.Delete(); } catch { } }
        }
    }
}

/// <summary>true → Visible. ConverterParameter "invert" reverses. Non-empty strings and non-null objects count as true.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool truthy = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            null => false,
            _ => true
        };
        if (parameter as string == "invert") truthy = !truthy;
        return truthy ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public static class Shell
{
    public static void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no handler registered for this URL */ }
    }

    /// <summary>True when the user asked Windows to reduce animations.</summary>
    public static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;
}
