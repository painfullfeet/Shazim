using System.Windows;
using System.Windows.Media;

namespace Shazim.App.Infrastructure;

/// <summary>
/// Music-reactive effects drawn around the listen button (Ondes, Particules, Néon),
/// redrawn every frame by HomeView. Centred on the element; draws beyond the button.
/// </summary>
public sealed class FxCanvas : FrameworkElement
{
    public const string Waves = "waves", Particles = "particles", Neon = "neon";

    private sealed class Particle { public double X, Y, Vx, Vy, Life, Size, Hue; }

    private readonly List<Particle> _particles = new();
    private readonly Random _rng = new();
    private double[] _bands = Array.Empty<double>();
    private double _beat, _bass, _spin, _time;
    private string _effect = "";
    private Color _accent = Colors.HotPink;
    private bool _rainbow;
    private double _hue;
    private double _boost = 1;

    public FxCanvas() => IsHitTestVisible = false;

    /// <summary>Feeds one frame. <paramref name="newBeat"/> is true on the frame a beat was detected.</summary>
    public void Update(string effect, double[] bands, double bass, double beat, bool newBeat, double dt,
                       Color accent, bool rainbow, double hue, double boost)
    {
        _effect = effect; _bands = bands; _bass = bass; _beat = beat; _accent = accent;
        _rainbow = rainbow; _hue = hue; _boost = boost; _time += dt;

        if (effect == Particles)
        {
            if (newBeat) Burst(rainbow ? 34 : 22);
            else if (bass > 0.35 && _rng.NextDouble() < bass * 0.5) Burst(1);
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.X += p.Vx * dt; p.Y += p.Vy * dt;
                p.Vx *= 0.985; p.Vy *= 0.985;
                p.Life -= dt * 0.9;
                if (p.Life <= 0) _particles.RemoveAt(i);
            }
        }
        else _particles.Clear();

        _spin += dt * (0.6 + 4 * bass) * (rainbow ? 1.6 : 1);
        InvalidateVisual();
    }

    public void Clear()
    {
        _effect = "";
        _particles.Clear();
        InvalidateVisual();
    }

    private void Burst(int count)
    {
        for (int i = 0; i < count; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            double speed = (120 + _rng.NextDouble() * 220) * _boost;
            _particles.Add(new Particle
            {
                X = Math.Cos(a) * 98, Y = Math.Sin(a) * 98,
                Vx = Math.Cos(a) * speed, Vy = Math.Sin(a) * speed,
                Life = 0.7 + _rng.NextDouble() * 0.6, Size = 2 + _rng.NextDouble() * 3.5 * _boost,
                Hue = (_hue + a * 180 / Math.PI) % 360
            });
        }
        if (_particles.Count > 600) _particles.RemoveRange(0, _particles.Count - 600);
    }

    private Color ColorAt(double angleDeg, byte alpha)
    {
        var c = _rainbow ? Services.ThemeService.FromHue((_hue + angleDeg) % 360) : _accent;
        return Color.FromArgb(alpha, c.R, c.G, c.B);
    }

    private double Band(double t)
    {
        if (_bands.Length == 0) return 0;
        // Mirrored: bass at the top, like the aura.
        t = t < 0.5 ? t * 2 : (1 - t) * 2;
        return _bands[Math.Clamp((int)(t * (_bands.Length - 1)), 0, _bands.Length - 1)];
    }

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        switch (_effect)
        {
            case Waves: DrawWaves(dc, c); break;
            case Particles: DrawParticles(dc, c); break;
            case Neon: DrawNeon(dc, c); break;
        }
    }

    /// <summary>Ondes: concentric rings rippling with the spectrum, kicked by the beat.</summary>
    private void DrawWaves(DrawingContext dc, Point c)
    {
        const int rings = 4, points = 120;
        for (int r = 0; r < rings; r++)
        {
            double baseR = 104 + r * 20 + (_bass * 26 + _beat * 18) * (r + 1) * 0.5 * _boost;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int k = 0; k <= points; k++)
                {
                    double t = (double)k / points, a = -Math.PI / 2 + t * Math.PI * 2;
                    double wobble = Band(t) * 22 * _boost * Math.Sin(a * (3 + r) + _time * (2 + r) + r);
                    double rad = baseR + wobble;
                    var p = new Point(c.X + Math.Cos(a) * rad, c.Y + Math.Sin(a) * rad);
                    if (k == 0) ctx.BeginFigure(p, false, true); else ctx.LineTo(p, true, true);
                }
            }
            geo.Freeze();
            byte alpha = (byte)Math.Clamp(200 - r * 42 + _beat * 40, 30, 255);
            var brush = _rainbow
                ? (Brush)new LinearGradientBrush(ColorAt(r * 40, alpha), ColorAt(r * 40 + 160, alpha), 45)
                : new SolidColorBrush(ColorAt(0, alpha));
            dc.DrawGeometry(null, new Pen(brush, 3.2 - r * 0.5), geo);
        }
    }

    /// <summary>Particules: sparks thrown out from the button on every beat.</summary>
    private void DrawParticles(DrawingContext dc, Point c)
    {
        foreach (var p in _particles)
        {
            byte alpha = (byte)Math.Clamp(p.Life * 255, 0, 255);
            var col = _rainbow ? Services.ThemeService.FromHue(p.Hue) : _accent;
            var brush = new SolidColorBrush(Color.FromArgb(alpha, col.R, col.G, col.B));
            dc.DrawEllipse(brush, null, new Point(c.X + p.X, c.Y + p.Y), p.Size, p.Size);
        }
    }

    /// <summary>Néon: a glowing tube around the button and two arcs spinning faster with the bass.</summary>
    private void DrawNeon(DrawingContext dc, Point c)
    {
        double r = 106 + (_bass * 14 + _beat * 10) * _boost;
        for (int i = 4; i >= 0; i--)
        {
            byte alpha = (byte)(i == 0 ? 255 : 26 + (4 - i) * 12 + _beat * 30);
            var pen = new Pen(new SolidColorBrush(ColorAt(i * 12, alpha)), i == 0 ? 3 : 3 + i * 5 * (0.7 + _bass));
            dc.DrawEllipse(null, pen, c, r, r);
        }
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1.2), c, r, r);

        for (int arc = 0; arc < 2; arc++)
        {
            double start = _spin + arc * Math.PI, sweep = 0.9 + _bass * 1.4;
            double ar = r + 18 + arc * 8;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(c.X + Math.Cos(start) * ar, c.Y + Math.Sin(start) * ar), false, false);
                ctx.ArcTo(new Point(c.X + Math.Cos(start + sweep) * ar, c.Y + Math.Sin(start + sweep) * ar),
                    new Size(ar, ar), 0, sweep > Math.PI, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();
            var pen = new Pen(new SolidColorBrush(ColorAt(arc * 180, 230)), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, geo);
        }
    }
}
