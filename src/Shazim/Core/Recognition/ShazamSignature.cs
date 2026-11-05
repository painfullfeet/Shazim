using System.Buffers.Binary;
using Shazim.Core.Audio;

namespace Shazim.Core.Recognition;

/// <summary>
/// Spectral-peak signature in the format understood by the Shazam service.
/// Port of the open-source SongRec algorithm (16 kHz mono, 2048-point FFT,
/// one FFT every 128 samples, peaks grouped in four frequency bands).
/// </summary>
public sealed class ShazamSignature
{
    public const int SampleRate = 16000;
    public const int MaxSeconds = 12;

    private ShazamSignature(int numberSamples, SortedDictionary<int, List<Peak>> bands)
    {
        NumberSamples = numberSamples;
        Bands = bands;
    }

    public int NumberSamples { get; }
    internal SortedDictionary<int, List<Peak>> Bands { get; }
    public int PeakCount => Bands.Values.Sum(b => b.Count);
    public int SampleMilliseconds => (int)(NumberSamples * 1000L / SampleRate);

    internal readonly record struct Peak(int FftPassNumber, ushort Magnitude, ushort CorrectedBin);

    /// <summary>Builds a signature from any clip (downmixed and resampled to 16 kHz).</summary>
    public static ShazamSignature FromClip(AudioClip clip)
    {
        var mono = clip.ToMono(SampleRate);

        // Shazam expects 12 s: keep the most recent part, or pad with silence
        // (as the reference implementation does) so trailing audio is not lost.
        int max = MaxSeconds * SampleRate;
        if (mono.Length > max)
            mono = mono.AsSpan(mono.Length - max).ToArray();
        else if (mono.Length < max)
            Array.Resize(ref mono, max);

        return FromMono16k(mono);
    }

    public static ShazamSignature FromMono16k(float[] samples)
    {
        var pcm = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            pcm[i] = (short)Math.Clamp((int)MathF.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
        return new Generator().Run(pcm);
    }

    /// <summary>Binary encoding (header + peak tables) with CRC32.</summary>
    public byte[] Encode()
    {
        using var content = new MemoryStream();
        Span<byte> u32 = stackalloc byte[4];
        Span<byte> u16 = stackalloc byte[2];

        foreach (var (band, peaks) in Bands)
        {
            using var peakBuf = new MemoryStream();
            int lastPass = 0;
            foreach (var p in peaks)
            {
                if (p.FftPassNumber - lastPass >= 255)
                {
                    peakBuf.WriteByte(0xFF);
                    BinaryPrimitives.WriteInt32LittleEndian(u32, p.FftPassNumber);
                    peakBuf.Write(u32);
                    lastPass = p.FftPassNumber;
                }
                peakBuf.WriteByte((byte)(p.FftPassNumber - lastPass));
                BinaryPrimitives.WriteUInt16LittleEndian(u16, p.Magnitude);
                peakBuf.Write(u16);
                BinaryPrimitives.WriteUInt16LittleEndian(u16, p.CorrectedBin);
                peakBuf.Write(u16);
                lastPass = p.FftPassNumber;
            }

            var bytes = peakBuf.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(u32, 0x60030040u + (uint)band);
            content.Write(u32);
            BinaryPrimitives.WriteInt32LittleEndian(u32, bytes.Length);
            content.Write(u32);
            content.Write(bytes);
            int pad = (4 - bytes.Length % 4) % 4;
            for (int i = 0; i < pad; i++) content.WriteByte(0);
        }

        var contentBytes = content.ToArray();
        int sizeMinusHeader = contentBytes.Length + 8;
        var buf = new byte[48 + 8 + contentBytes.Length];
        var span = buf.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span[0..], 0xCAFE2580);           // magic 1
        // [4..8] CRC32, written last
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], sizeMinusHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0x94119C00);          // magic 2
        // [16..28] void
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], 3u << 27);            // 16 kHz sample-rate id
        // [32..40] void
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)(NumberSamples + SampleRate * 0.24));
        BinaryPrimitives.WriteUInt32LittleEndian(span[44..], (15u << 19) + 0x40000u);
        BinaryPrimitives.WriteUInt32LittleEndian(span[48..], 0x40000000);
        BinaryPrimitives.WriteInt32LittleEndian(span[52..], sizeMinusHeader);
        contentBytes.CopyTo(span[56..]);

        uint crc = Crc32(span[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], crc);
        return buf;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    public string ToDataUri() => "data:audio/vnd.shazam.sig;base64," + Convert.ToBase64String(Encode());

    private sealed class Generator
    {
        private const int FftSize = 2048;
        private const int Bins = FftSize / 2 + 1; // 1025
        private static readonly float[] Hanning = BuildHanning();

        private readonly short[] _samples = new short[FftSize];
        private int _samplePos;
        private readonly float[][] _fftOutputs = NewRing();
        private int _fftPos;
        private readonly float[][] _spreadOutputs = NewRing();
        private int _spreadPos;
        private int _spreadDone;

        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];
        private readonly SortedDictionary<int, List<Peak>> _bands = new();

        private static float[][] NewRing()
        {
            var ring = new float[256][];
            for (int i = 0; i < ring.Length; i++) ring[i] = new float[Bins];
            return ring;
        }

        private static float[] BuildHanning()
        {
            // Equivalent to numpy.hanning(2050)[1:-1]
            var w = new float[FftSize];
            for (int i = 0; i < FftSize; i++)
                w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 1) / (FftSize + 1)));
            return w;
        }

        public ShazamSignature Run(short[] pcm)
        {
            for (int offset = 0; offset + 128 <= pcm.Length; offset += 128)
            {
                DoFft(pcm.AsSpan(offset, 128));
                DoPeakSpreading();
                _spreadDone++;
                if (_spreadDone >= 46) DoPeakRecognition();
            }
            return new ShazamSignature(pcm.Length, _bands);
        }

        private void DoFft(ReadOnlySpan<short> chunk)
        {
            chunk.CopyTo(_samples.AsSpan(_samplePos, 128));
            _samplePos = (_samplePos + 128) & (FftSize - 1);

            for (int i = 0; i < FftSize; i++)
            {
                _re[i] = _samples[(i + _samplePos) & (FftSize - 1)] * Hanning[i];
                _im[i] = 0f;
            }
            AudioDsp.Fft(_re, _im);

            var output = _fftOutputs[_fftPos];
            for (int i = 0; i < Bins; i++)
            {
                float v = (_re[i] * _re[i] + _im[i] * _im[i]) / (1 << 17);
                output[i] = MathF.Max(v, 1e-10f);
            }
            _fftPos = (_fftPos + 1) & 255;
        }

        private void DoPeakSpreading()
        {
            var last = _fftOutputs[(_fftPos - 1) & 255];
            var spread = _spreadOutputs[_spreadPos];
            Array.Copy(last, spread, Bins);

            // Frequency-domain spreading
            for (int p = 0; p <= Bins - 3; p++)
                spread[p] = MathF.Max(spread[p], MathF.Max(spread[p + 1], spread[p + 2]));

            // Time-domain spreading
            foreach (int former in FormerOffsets)
            {
                var f = _spreadOutputs[(_spreadPos - former) & 255];
                for (int p = 0; p < Bins; p++)
                    f[p] = MathF.Max(f[p], spread[p]);
            }
            _spreadPos = (_spreadPos + 1) & 255;
        }

        private static readonly int[] FormerOffsets = { 1, 3, 6 };
        private static readonly int[] NeighborOffsets = { -10, -7, -4, -3, 1, 2, 5, 8 };
        private static readonly int[] OtherOffsets = { -53, -45, 165, 172, 179, 186, 193, 200, 214, 221, 228, 235, 242, 249 };

        private void DoPeakRecognition()
        {
            var fft46 = _fftOutputs[(_fftPos - 46) & 255];
            var spread49 = _spreadOutputs[(_spreadPos - 49) & 255];

            for (int bin = 10; bin <= 1014; bin++)
            {
                float v = fft46[bin];
                if (v < 1f / 64f || v < spread49[bin - 1]) continue;

                float maxNeighbor = 0f;
                foreach (int o in NeighborOffsets)
                    maxNeighbor = MathF.Max(maxNeighbor, spread49[bin + o]);
                if (v <= maxNeighbor) continue;

                float maxOther = maxNeighbor;
                foreach (int o in OtherOffsets)
                    maxOther = MathF.Max(maxOther, _spreadOutputs[(_spreadPos + o) & 255][bin - 1]);
                if (v <= maxOther) continue;

                int pass = _spreadDone - 46;
                float mag = MathF.Max(MathF.Log(v), 1f / 64f) * 1477.3f + 6144f;
                float before = MathF.Max(MathF.Log(fft46[bin - 1]), 1f / 64f) * 1477.3f + 6144f;
                float after = MathF.Max(MathF.Log(fft46[bin + 1]), 1f / 64f) * 1477.3f + 6144f;
                float variation1 = mag * 2f - before - after;
                if (variation1 <= 0f) continue;
                float variation2 = (after - before) * 32f / variation1;
                int corrected = (ushort)(bin * 64 + (int)variation2);
                float hz = corrected * (16000f / 2f / 1024f / 64f);

                int band = (int)hz switch
                {
                    >= 250 and < 520 => 0,
                    >= 520 and < 1450 => 1,
                    >= 1450 and < 3500 => 2,
                    >= 3500 and <= 5500 => 3,
                    _ => -1
                };
                if (band < 0) continue;

                if (!_bands.TryGetValue(band, out var list))
                    _bands[band] = list = new List<Peak>();
                list.Add(new Peak(pass, (ushort)mag, (ushort)corrected));
            }
        }
    }
}
