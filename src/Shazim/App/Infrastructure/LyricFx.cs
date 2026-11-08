using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Shazim.App.Infrastructure;

/// <summary>
/// Animations for synced lyrics: the line being sung rises in (fade, slide, scale, un-blur),
/// fills like karaoke from left to right, and the list glides to it.
/// </summary>
public static class LyricFx
{
    // Close to CSS "cubic-bezier(.2,.8,.2,1)": quick start, long soft landing.
    private static readonly IEasingFunction Smooth = new PowerEase { Power = 4, EasingMode = EasingMode.EaseOut };

    private static (TranslateTransform Move, ScaleTransform Scale) Transforms(FrameworkElement el)
    {
        if (el.RenderTransform is TransformGroup g && g.Children.Count == 2 &&
            g.Children[0] is ScaleTransform s && g.Children[1] is TranslateTransform t) return (t, s);
        var scale = new ScaleTransform();
        var move = new TranslateTransform();
        el.RenderTransform = new TransformGroup { Children = { scale, move } };
        el.RenderTransformOrigin = new Point(0, 0.5);
        return (move, scale);
    }

    /// <summary>The new current line: from smaller, lower, blurred and faint to crisp.</summary>
    public static void Enter(FrameworkElement el, double fromScale = 0.82, double fromY = 16)
    {
        if (Shell.ReduceMotion) return;
        var (move, scale) = Transforms(el);
        var t = TimeSpan.FromMilliseconds(520);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, 1, t) { EasingFunction = Smooth });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, 1, t) { EasingFunction = Smooth });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, t) { EasingFunction = Smooth });
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = Smooth });
        var blur = new BlurEffect { Radius = 9, KernelType = KernelType.Gaussian };
        el.Effect = blur;
        var unblur = new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = Smooth };
        unblur.Completed += (_, _) => { if (ReferenceEquals(el.Effect, blur)) el.Effect = null; };
        blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);
    }

    /// <summary>The line that was just sung: back to normal size, fading a little.</summary>
    public static void Leave(FrameworkElement el, double fromScale, double toOpacity)
    {
        if (Shell.ReduceMotion) { el.Opacity = toOpacity; return; }
        var (move, scale) = Transforms(el);
        var t = TimeSpan.FromMilliseconds(420);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, 1, t) { EasingFunction = Smooth });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, 1, t) { EasingFunction = Smooth });
        move.BeginAnimation(TranslateTransform.YProperty, null);
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(toOpacity, t) { EasingFunction = Smooth });
        el.Effect = null;
    }

    /// <summary>Resets a line to a still state (no animation).</summary>
    public static void Still(FrameworkElement el, double opacity)
    {
        var (move, scale) = Transforms(el);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        move.BeginAnimation(TranslateTransform.YProperty, null);
        el.BeginAnimation(UIElement.OpacityProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
        move.Y = 0;
        el.Opacity = opacity;
        el.Effect = null;
    }

    /// <summary>Karaoke: the accent colour sweeps across the line over <paramref name="duration"/>.</summary>
    public static void Fill(TextBlock text, Color sung, Color rest, TimeSpan duration)
    {
        var edge1 = new GradientStop(sung, 0);
        var edge2 = new GradientStop(rest, 0.001);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops = { new GradientStop(sung, 0), edge1, edge2, new GradientStop(rest, 1) }
        };
        text.Foreground = brush;
        if (Shell.ReduceMotion || duration <= TimeSpan.Zero) { edge1.Offset = edge2.Offset = 1; return; }
        // A soft edge: the sung colour leads by a few percent.
        edge1.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0, 1, duration));
        edge2.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0.06, 1.0001, duration));
    }

    // ===== Défilement fluide =====

    private sealed class Glide { public double From, To; public long Start; public bool Hooked; }
    private static readonly ConditionalWeakTable<ScrollViewer, Glide> Glides = new();

    /// <summary>Scrolls smoothly to <paramref name="offset"/> (ScrollViewer offsets cannot be animated directly).</summary>
    public static void GlideTo(ScrollViewer sv, double offset)
    {
        offset = Math.Clamp(offset, 0, sv.ScrollableHeight);
        if (Shell.ReduceMotion) { sv.ScrollToVerticalOffset(offset); return; }
        var g = Glides.GetValue(sv, _ => new Glide());
        g.From = sv.VerticalOffset;
        g.To = offset;
        g.Start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (g.Hooked) return;
        g.Hooked = true;
        void Step(object? s, EventArgs e)
        {
            double t = Math.Min(1, (System.Diagnostics.Stopwatch.GetTimestamp() - g.Start) / (double)System.Diagnostics.Stopwatch.Frequency / 0.55);
            double k = 1 - Math.Pow(1 - t, 4);
            sv.ScrollToVerticalOffset(g.From + (g.To - g.From) * k);
            if (t >= 1 || !sv.IsLoaded) { CompositionTarget.Rendering -= Step; g.Hooked = false; }
        }
        CompositionTarget.Rendering += Step;
    }
}
