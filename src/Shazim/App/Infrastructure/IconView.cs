using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shazim.App.Infrastructure;

/// <summary>
/// Vector icon (Phosphor, 256-unit grid) drawn with the current Foreground.
/// Crisp at any DPI, unlike font glyphs or bitmaps.
/// </summary>
public class IconView : Control
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(IconView), new PropertyMetadata(null));

    static IconView()
    {
        FocusableProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(false));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Looks up an icon geometry by resource key ("IconHeart"...).</summary>
    public static Geometry? Get(string key) => Application.Current?.TryFindResource(key) as Geometry;
}
