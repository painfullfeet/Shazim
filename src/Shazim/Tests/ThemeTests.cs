using System.Windows.Media;
using Shazim.App.Services;
using Xunit;

namespace Shazim.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData("#FF4F7E", 0xFF, 0x4F, 0x7E)]
    [InlineData("1f8fff", 0x1F, 0x8F, 0xFF)]
    [InlineData(" #abc ", 0xAA, 0xBB, 0xCC)]
    public void ParseHex_ReadsColours(string hex, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromRgb(r, g, b), ThemeService.ParseHex(hex));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGHHII")]
    [InlineData(null)]
    public void ParseHex_RejectsOthers(string? hex) => Assert.Null(ThemeService.ParseHex(hex));

    [Fact]
    public void CustomAccent_HasADarkerAndALighterShade()
    {
        var a = ThemeService.FromColor(Color.FromRgb(0x20, 0x90, 0xF0));
        double Sum(Color c) => c.R + c.G + c.B;
        Assert.True(Sum(a.Deep) < Sum(a.Main) && Sum(a.Main) < Sum(a.Light));
        Assert.Equal("#2090F0", ThemeService.ToHex(a.Main));
    }

    [Fact]
    public void HueSlider_RoundTrips()
    {
        foreach (var hue in new[] { 0.0, 45, 120, 210, 300 })
            Assert.InRange(ThemeService.HueOf(ThemeService.FromHue(hue)), hue - 2, hue + 2);
    }

    /// <summary>Text must stay readable in every theme (WCAG AA: 4.5 for normal text).</summary>
    [Fact]
    public void EveryThemeHasReadableText()
    {
        foreach (var p in ThemeService.Palettes)
        {
            foreach (var bg in new[] { p.Ink, p.Night, p.Dusk })
            {
                Assert.True(ThemeService.Contrast(p.Paper, bg) >= 7, $"{p.Key}: main text {ThemeService.Contrast(p.Paper, bg):F1}");
                Assert.True(ThemeService.Contrast(p.Mist, bg) >= 4.5, $"{p.Key}: secondary text {ThemeService.Contrast(p.Mist, bg):F1}");
            }
            Assert.Equal(p.IsLight, ThemeService.Contrast(p.Ink, Colors.White) < ThemeService.Contrast(p.Ink, Colors.Black));
        }
    }

    [Fact]
    public void SystemTheme_ResolvesToLightOrNight()
    {
        var p = ThemeService.Resolve(Shazim.App.ThemeMode.System);
        Assert.Contains(p.Key, new[] { Shazim.App.ThemeMode.Light, Shazim.App.ThemeMode.Night });
        Assert.Equal(Shazim.App.ThemeMode.Midnight, ThemeService.Resolve(Shazim.App.ThemeMode.Midnight).Key);
    }
}
