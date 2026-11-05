namespace Shazim.Core.Audio;

/// <summary>
/// A short piece of captured audio: interleaved 32-bit float samples.
/// </summary>
public sealed class AudioClip
{
    public static readonly AudioClip Empty = new(Array.Empty<float>(), 16000, 1);

    public AudioClip(float[] samples, int sampleRate, int channels)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>Interleaved samples in the [-1, 1] range.</summary>
    public float[] Samples { get; }
    public int SampleRate { get; }
    public int Channels { get; }

    public int FrameCount => Samples.Length / Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);
    public bool IsEmpty => Samples.Length == 0;

    /// <summary>Returns a mono copy at the requested sample rate.</summary>
    public float[] ToMono(int targetSampleRate)
    {
        var mono = AudioDsp.ToMono(Samples, Channels);
        return SampleRate == targetSampleRate ? mono : AudioDsp.Resample(mono, SampleRate, targetSampleRate);
    }
}
