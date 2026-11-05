using System.Text;

namespace Shazim.Core.Audio;

/// <summary>
/// Small, allocation-conscious DSP helpers: downmix, resampling, FFT, WAV encoding.
/// </summary>
public static class AudioDsp
{
    public static float[] ToMono(float[] interleaved, int channels)
    {
        if (channels == 1) return (float[])interleaved.Clone();

        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        float scale = 1f / channels;
        for (int f = 0, i = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += interleaved[i++];
            mono[f] = sum * scale;
        }
        return mono;
    }

    private const int KernelZeroCrossings = 16;
    private const int KernelResolution = 512; // table entries per zero crossing
    private static readonly float[] KernelTable = BuildKernelTable();

    /// <summary>Windowed-sinc kernel sampled on a fine grid: k(x) for x in [0, zeroCrossings].</summary>
    private static float[] BuildKernelTable()
    {
        int size = KernelZeroCrossings * KernelResolution + 2;
        var table = new float[size];
        for (int i = 0; i < size; i++)
        {
            double x = (double)i / KernelResolution;
            double t = x / KernelZeroCrossings;
            if (t >= 1) { table[i] = 0; continue; }
            double a = Math.PI * (t + 1);
            double window = 0.42 - 0.5 * Math.Cos(a) + 0.08 * Math.Cos(2 * a); // Blackman
            double sinc = x < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
            table[i] = (float)(sinc * window);
        }
        return table;
    }

    /// <summary>
    /// Band-limited resampling with a Blackman-windowed sinc kernel read from a
    /// precomputed table (no trigonometry in the inner loop).
    /// </summary>
    public static float[] Resample(float[] input, int inRate, int outRate)
    {
        if (inRate == outRate || input.Length == 0) return (float[])input.Clone();

        double ratio = (double)outRate / inRate;
        int outLength = (int)Math.Floor(input.Length * ratio);
        var output = new float[outLength];

        // Cut off slightly below the lower Nyquist frequency to avoid aliasing.
        double cutoff = 0.95 * Math.Min(1.0, ratio);
        double halfWidth = KernelZeroCrossings / cutoff; // in input samples
        int taps = (int)Math.Ceiling(halfWidth);
        double scale = cutoff * KernelResolution;         // input-sample distance -> table index

        for (int n = 0; n < outLength; n++)
        {
            double center = n / ratio;
            int baseIndex = (int)Math.Floor(center);
            int first = Math.Max(0, baseIndex - taps + 1);
            int last = Math.Min(input.Length - 1, baseIndex + taps);
            double acc = 0, norm = 0;

            for (int k = first; k <= last; k++)
            {
                double pos = Math.Abs(k - center) * scale;
                int idx = (int)pos;
                if (idx >= KernelTable.Length - 1) continue;
                double frac = pos - idx;
                double h = KernelTable[idx] + (KernelTable[idx + 1] - KernelTable[idx]) * frac;
                acc += input[k] * h;
                norm += h;
            }
            output[n] = norm != 0 ? (float)(acc / norm) : 0f;
        }
        return output;
    }

    /// <summary>In-place iterative radix-2 complex FFT. Length must be a power of two.</summary>
    public static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        if (n != im.Length || (n & (n - 1)) != 0)
            throw new ArgumentException("FFT length must be a power of two and arrays must match.");

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            float wRe = (float)Math.Cos(angle), wIm = (float)Math.Sin(angle);
            int half = len >> 1;
            for (int start = 0; start < n; start += len)
            {
                float curRe = 1f, curIm = 0f;
                for (int k = 0; k < half; k++)
                {
                    int a = start + k, b = a + half;
                    float tRe = re[b] * curRe - im[b] * curIm;
                    float tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                    re[a] += tRe; im[a] += tIm;
                    float nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }

    /// <summary>Root-mean-square level of a buffer.</summary>
    public static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Encodes samples as a 16-bit PCM WAV file.</summary>
    public static byte[] EncodeWav16(float[] interleaved, int sampleRate, int channels)
    {
        int dataSize = interleaved.Length * 2;
        using var ms = new MemoryStream(44 + dataSize);
        using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataSize);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataSize);
            foreach (var s in interleaved)
                w.Write((short)(Math.Clamp(s, -1f, 1f) * 32767));
        }
        return ms.ToArray();
    }
}
