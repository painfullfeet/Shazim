namespace Shazim.Core.Audio;

/// <summary>
/// Finds beats (kicks) in a stream of bass levels (0..1), one value per analysis window.
/// A beat is a clear rise above the recent average, measured against the recent spread,
/// so it works on quiet tracks and on loud ones where the bass never drops to zero.
/// </summary>
public sealed class BeatDetector
{
    private readonly double _minGap;
    private double _mean;
    private double _variance;
    private double _previous;
    private double _sinceBeat = double.MaxValue;
    private bool _primed;

    /// <param name="minGapSeconds">Shortest time between two beats (0.23 s ≈ 260 BPM).</param>
    public BeatDetector(double minGapSeconds = 0.23)
    {
        _minGap = minGapSeconds;
    }

    /// <summary>Number of beats found so far.</summary>
    public int Count { get; private set; }

    public void Reset()
    {
        _mean = _variance = _previous = 0;
        _sinceBeat = double.MaxValue;
        _primed = false;
        Count = 0;
    }

    /// <summary>Feeds one bass level measured <paramref name="dt"/> seconds after the previous one. Returns true on a beat.</summary>
    public bool Feed(double level, double dt)
    {
        level = Math.Clamp(level, 0, 1);
        if (_sinceBeat < double.MaxValue) _sinceBeat += dt;

        if (!_primed)
        {
            _mean = _previous = level;
            _primed = true;
            return false;
        }

        // Rising edge clearly above the recent average (about 0.6 s of memory).
        double deviation = Math.Sqrt(Math.Max(0, _variance));
        double threshold = _mean + Math.Max(0.06, 1.3 * deviation);
        bool rising = level - _previous > 0.02;
        bool beat = level > 0.18 && level > threshold && rising && _sinceBeat >= _minGap;

        double alpha = Math.Clamp(dt / 0.6, 0.001, 1);
        double diff = level - _mean;
        _mean += alpha * diff;
        _variance = (1 - alpha) * (_variance + alpha * diff * diff);
        _previous = level;

        if (beat)
        {
            _sinceBeat = 0;
            Count++;
        }
        return beat;
    }
}
