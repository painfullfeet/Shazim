using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Shazim.App.Services;

public sealed record Accent(string Key, string Name, Color Main, Color Deep, Color Light);

/// <summary>Base colours of a theme (backgrounds, text, lines).</summary>
public sealed record Palette(string Key, bool IsLight, Color Ink, Color Night, Color Dusk, Color Line, Color Paper, Color Mist,
    Color Wave, Color Danger, Color Hover, Color Press, Color ArtFrom, Color ArtTo);

/// <summary>
/// Themes (Nuit, Minuit, Clair, Suivre Windows) and accent colours (5 presets or a custom one).
/// Swapped live: every view uses DynamicResource for these keys.
/// </summary>
public static class ThemeService
{
    public const string CustomKey = "custom";

    public static readonly IReadOnlyList<Accent> Accents = new[]
    {
        new Accent("rouge", "Rouge", Rgb(0xFF4F7E), Rgb(0xE0335F), Rgb(0xFF7A9C)),
        new Accent("bleu", "Bleu", Rgb(0x1F8FFF), Rgb(0x0B66E4), Rgb(0x63B3FF)),
        new Accent("violet", "Violet", Rgb(0x8B5CF6), Rgb(0x6D3FE0), Rgb(0xAE8CFA)),
        new Accent("vert", "Vert", Rgb(0x1ED760), Rgb(0x14A84A), Rgb(0x63E691)),
        new Accent("orange", "Orange", Rgb(0xFF8A1F), Rgb(0xE56B00), Rgb(0xFFB066)),
    };

    public static readonly IReadOnlyList<Palette> Palettes = new[]
    {
        // Nuit: the original "listening room at night".
        new Palette(ThemeMode.Night, false, Rgb(0x17142B), Rgb(0x201C3A), Rgb(0x2C2750), Rgb(0x3A3463), Rgb(0xF3F0FF), Rgb(0xA39EC6),
            Rgb(0x4FD8E8), Rgb(0xFF7A7A), Argb(0x14FFFFFF), Argb(0x22FFFFFF), Rgb(0x3B2F6E), Rgb(0x5A2853)),
        // Minuit: pure black for OLED screens, neutral greys.
        new Palette(ThemeMode.Midnight, false, Rgb(0x000000), Rgb(0x0C0C10), Rgb(0x1A1A21), Rgb(0x2A2A33), Rgb(0xF4F4F8), Rgb(0x9C9CAB),
            Rgb(0x4FD8E8), Rgb(0xFF7A7A), Argb(0x14FFFFFF), Argb(0x24FFFFFF), Rgb(0x1E1E2A), Rgb(0x2E1A2A)),
        // Clair: light, with the same violet hint.
        new Palette(ThemeMode.Light, true, Rgb(0xF5F4FA), Rgb(0xFFFFFF), Rgb(0xE9E7F3), Rgb(0xD9D5E8), Rgb(0x1A1730), Rgb(0x5E5978),
            Rgb(0x0A8C9E), Rgb(0xD23B3B), Argb(0x10000000), Argb(0x1C000000), Rgb(0xD9D2F2), Rgb(0xF0D3E2)),
    };

    /// <summary>The palette in use (after "Suivre Windows" is resolved).</summary>
    public static Palette Current { get; private set; } = Palettes[0];

    public static bool IsLight => Current.IsLight;

    /// <summary>Raised after the theme or the accent changed (code-built visuals can refresh).</summary>
    public static event EventHandler? Changed;

    private static string _mode = ThemeMode.Night;
    private static bool _watchingSystem;

    public static Accent Find(string? key) =>
        Accents.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Accents[0];

    /// <summary>Applies theme and accent from the settings.</summary>
    public static void Apply(GeneralSettings g)
    {
        ApplyTheme(g.Theme);
        ApplyAccent(g.Accent, g.CustomAccent);
    }

    /// <summary>Night theme with a preset accent (installer, tests).</summary>
    public static void ApplyDefault(string? accentKey = null)
    {
        ApplyTheme(ThemeMode.Night);
        ApplyAccent(accentKey, null);
    }

    public static void ApplyTheme(string? mode)
    {
        _mode = mode ?? ThemeMode.Night;
        var p = Resolve(_mode);
        Current = p;
        var res = Application.Current.Resources;

        void Set(string name, Color c)
        {
            res[name + "Color"] = c;
            res[name + "Brush"] = Frozen(new SolidColorBrush(c));
        }
        Set("Ink", p.Ink);
        Set("Night", p.Night);
        Set("Dusk", p.Dusk);
        Set("Line", p.Line);
        Set("Paper", p.Paper);
        Set("Mist", p.Mist);
        Set("Wave", p.Wave);
        Set("Danger", p.Danger);
        res["HoverBrush"] = Frozen(new SolidColorBrush(p.Hover));
        res["PressBrush"] = Frozen(new SolidColorBrush(p.Press));
        res["InkTintColor"] = Color.FromArgb(p.IsLight ? (byte)0x22 : (byte)0x55, p.Ink.R, p.Ink.G, p.Ink.B);
        res["ArtPlaceholderBrush"] = Frozen(new LinearGradientBrush(p.ArtFrom, p.ArtTo, new Point(0, 0), new Point(1, 1)));

        if (_mode == ThemeMode.System && !_watchingSystem)
        {
            _watchingSystem = true;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }
        if (_lastAccent != null) ApplyAccentColors(_lastAccent); // the glow depends on the theme
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void OnSystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != ThemeMode.System || e.Category != UserPreferenceCategory.General) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Resolve(ThemeMode.System) != Current) ApplyTheme(ThemeMode.System);
        });
    }

    /// <summary>"systeme" becomes Clair or Nuit, following "Mode des applications" in Windows.</summary>
    public static Palette Resolve(string mode)
    {
        if (mode == ThemeMode.System) mode = WindowsUsesLightTheme() ? ThemeMode.Light : ThemeMode.Night;
        return Palettes.FirstOrDefault(p => p.Key == mode) ?? Palettes[0];
    }

    public static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    private static Accent? _lastAccent;

    /// <summary>A preset key, or "custom" with a "#RRGGBB" colour.</summary>
    public static void ApplyAccent(string? key, string? customHex)
    {
        var accent = string.Equals(key, CustomKey, StringComparison.OrdinalIgnoreCase) && ParseHex(customHex) is { } c
            ? FromColor(c)
            : Find(key);
        _lastAccent = accent;
        ApplyAccentColors(accent);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyAccentColors(Accent a)
    {
        var res = Application.Current.Resources;
        res["SignalColor"] = a.Main;
        res["SignalDeepColor"] = a.Deep;
        res["SignalBrush"] = Frozen(new SolidColorBrush(a.Main));
        res["SignalDeepBrush"] = Frozen(new SolidColorBrush(a.Deep));
        res["SignalSoftBrush"] = Frozen(new SolidColorBrush(Color.FromArgb(0x33, a.Main.R, a.Main.G, a.Main.B)));

        // The big listening button: lit from the top-left, like a glossy disc.
        res["SignalGradientBrush"] = Frozen(new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.3),
            RadiusX = 0.65, RadiusY = 0.65,
            GradientStops = { new GradientStop(a.Light, 0), new GradientStop(a.Main, 0.55), new GradientStop(a.Deep, 1) }
        });

        // Glow behind the button on the home screen (softer on a light background).
        byte strong = Current.IsLight ? (byte)0x30 : (byte)0x55, weak = Current.IsLight ? (byte)0x10 : (byte)0x18;
        res["AccentGlowBrush"] = Frozen(new RadialGradientBrush
        {
            Center = new Point(0.5, 0.42), GradientOrigin = new Point(0.5, 0.42),
            RadiusX = 0.6, RadiusY = 0.7,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(strong, a.Deep.R, a.Deep.G, a.Deep.B), 0),
                new GradientStop(Color.FromArgb(weak, a.Deep.R, a.Deep.G, a.Deep.B), 0.55),
                new GradientStop(Colors.Transparent, 1)
            }
        });
    }

    /// <summary>Accent built from any colour: a darker and a lighter shade around it.</summary>
    public static Accent FromColor(Color main)
    {
        var (h, s, l) = ToHsl(main);
        return new Accent(CustomKey, "Perso", main, FromHsl(h, s, Math.Max(0, l - 0.1)), FromHsl(h, s, Math.Min(1, l + 0.12)));
    }

    /// <summary>"#RRGGBB" or "RRGGBB" (also "#RGB") → colour, or null.</summary>
    public static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        return Rgb(v);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Vivid colour of the given hue (0..360), for the custom colour slider.</summary>
    public static Color FromHue(double hue) => FromHsl(hue, 0.9, 0.6);

    public static double HueOf(Color c) => ToHsl(c).H;

    /// <summary>WCAG contrast ratio between two colours (1..21).</summary>
    public static double Contrast(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Ch(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max - min > 1e-9)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double C = (1 - Math.Abs(2 * l - 1)) * s, x = C * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - C / 2;
        (double r, double g, double b) = (h % 360) switch
        {
            < 60 => (C, x, 0.0), < 120 => (x, C, 0.0), < 180 => (0.0, C, x),
            < 240 => (0.0, x, C), < 300 => (x, 0.0, C), _ => (C, 0.0, x)
        };
        byte B(double v) => (byte)Math.Round(Math.Clamp((v + m) * 255, 0, 255));
        return Color.FromRgb(B(r), B(g), B(b));
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static Color Argb(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
