using Shazim.Core.Audio;
using Xunit;

namespace Shazim.Tests;

public class BeatDetectorTests
{
    private const double Dt = 512.0 / 48000; // one analysis window of the visualizer

    /// <summary>Bass level over time: a floor plus a kick (fast attack, ~150 ms decay) every period.</summary>
    private static IEnumerable<double> Kicks(double floor, double peak, double bpm, double seconds)
    {
        double period = 60.0 / bpm;
        for (double t = 0; t < seconds; t += Dt)
        {
            double phase = t % period;
            double attack = Math.Min(1, phase / 0.03);
            double env = phase < 0.03 ? attack : Math.Exp(-(phase - 0.03) / 0.08);
            yield return floor + (peak - floor) * env;
        }
    }

    private static int CountBeats(IEnumerable<double> levels)
    {
        var d = new BeatDetector();
        foreach (var l in levels) d.Feed(l, Dt);
        return d.Count;
    }

    [Fact]
    public void FindsKicks_OnQuietBackground()
    {
        // 120 BPM for 10 s = 20 beats.
        Assert.InRange(CountBeats(Kicks(0.05, 0.8, 120, 10)), 19, 21);
    }

    [Fact]
    public void FindsKicks_WhenTheBassNeverDrops()
    {
        // Loud mastered music: the bass stays high, kicks only add a little on top.
        // The old detector (threshold = average × 1.18 + 0.04 > 1) could never fire here.
        Assert.InRange(CountBeats(Kicks(0.84, 0.98, 128, 10)), 20, 22);
    }

    [Fact]
    public void SteadyOrSilentSignal_HasNoBeat()
    {
        var rng = new Random(3);
        Assert.Equal(0, CountBeats(Enumerable.Range(0, 2000).Select(_ => 0.85 + rng.NextDouble() * 0.01)));
        Assert.Equal(0, CountBeats(Enumerable.Repeat(0.0, 2000)));
    }

    [Fact]
    public void NeverFiresTwiceInsideTheMinimumGap()
    {
        // 400 BPM is faster than the 0.23 s gap: at most one beat per gap.
        int beats = CountBeats(Kicks(0.05, 0.9, 400, 6));
        Assert.True(beats <= 6 / 0.23 + 1, $"{beats} beats");
    }
}
