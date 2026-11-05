using System.Windows;
using System.Windows.Media;

namespace Shazim.App.Services;

public sealed record Accent(string Key, string Name, Color Main, Color Deep, Color Light);

/// <summary>
/// Accent colours chosen in the settings. Swapped live: every view uses DynamicResource.
/// </summary>
public static class ThemeService
{
    public static readonly IReadOnlyList<Accent> Accents = new[]
    {
        new Accent("rouge", "Rouge", Rgb(0xFF4F7E), Rgb(0xE0335F), Rgb(0xFF7A9C)),
        new Accent("bleu", "Bleu", Rgb(0x1F8FFF), Rgb(0x0B66E4), Rgb(0x63B3FF)),
        new Accent("violet", "Violet", Rgb(0x8B5CF6), Rgb(0x6D3FE0), Rgb(0xAE8CFA)),
        new Accent("vert", "Vert", Rgb(0x1ED760), Rgb(0x14A84A), Rgb(0x63E691)),
        new Accent("orange", "Orange", Rgb(0xFF8A1F), Rgb(0xE56B00), Rgb(0xFFB066)),
    };

    public static Accent Find(string? key) =>
        Accents.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Accents[0];

    public static void Apply(string? key)
    {
        var a = Find(key);
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

        // Glow behind the button on the home screen.
        res["AccentGlowBrush"] = Frozen(new RadialGradientBrush
        {
            Center = new Point(0.5, 0.42), GradientOrigin = new Point(0.5, 0.42),
            RadiusX = 0.6, RadiusY = 0.7,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x55, a.Deep.R, a.Deep.G, a.Deep.B), 0),
                new GradientStop(Color.FromArgb(0x18, a.Deep.R, a.Deep.G, a.Deep.B), 0.55),
                new GradientStop(Colors.Transparent, 1)
            }
        });
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
