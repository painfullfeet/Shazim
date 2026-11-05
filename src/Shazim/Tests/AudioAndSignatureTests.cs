using System.Buffers.Binary;
using Shazim.Core.Audio;
using Shazim.Core.Recognition;
using Xunit;

namespace Shazim.Tests;

public class AudioDspTests
{
    [Fact]
    public void ToMono_AveragesChannels()
    {
        var mono = AudioDsp.ToMono(new float[] { 1f, 0f, 0.5f, 0.5f }, 2);
        Assert.Equal(new[] { 0.5f, 0.5f }, mono);
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void Resample_KeepsDurationAndFrequency(int inRate)
    {
        var input = Signals.Sine(inRate, 1000, 1.0);
        var output = AudioDsp.Resample(input, inRate, 16000);
        Assert.InRange(output.Length, 15990, 16000);

        // Dominant FFT bin must still be 1 kHz.
        var re = output.AsSpan(4000, 2048).ToArray();
        var im = new float[2048];
        AudioDsp.Fft(re, im);
        int best = 0; double bestMag = 0;
        for (int k = 1; k < 1024; k++)
        {
            double m = re[k] * re[k] + im[k] * im[k];
            if (m > bestMag) { bestMag = m; best = k; }
        }
        Assert.InRange(best * 16000.0 / 2048, 990, 1010);
    }

    [Fact]
    public void Resample_RemovesFrequenciesAboveNyquist()
    {
        var input = Signals.Sine(48000, 12000, 0.5); // above 8 kHz: must vanish at 16 kHz
        var output = AudioDsp.Resample(input, 48000, 16000);
        Assert.True(AudioDsp.Rms(output.AsSpan(200, output.Length - 400)) < 0.02f);
    }

    [Fact]
    public void EncodeWav16_WritesValidHeader()
    {
        var wav = AudioDsp.EncodeWav16(new float[] { 0f, 1f, -1f }, 16000, 1);
        Assert.Equal(44 + 6, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(32767, BitConverter.ToInt16(wav, 46));
    }

    [Fact]
    public void AudioClip_ReportsDuration()
    {
        var clip = new AudioClip(new float[48000 * 2], 48000, 2);
        Assert.Equal(TimeSpan.FromSeconds(1), clip.Duration);
        Assert.Equal(16000, clip.ToMono(16000).Length);
    }
}

public class ShazamSignatureTests
{
    [Fact]
    public void Silence_ProducesNoPeaks()
    {
        var sig = ShazamSignature.FromMono16k(new float[16000 * 5]);
        Assert.Equal(0, sig.PeakCount);
    }

    [Fact]
    public void Melody_ProducesPeaksInSeveralBands()
    {
        var sig = ShazamSignature.FromClip(new AudioClip(Signals.Melody(44100, 8, 2), 44100, 2));
        Assert.True(sig.PeakCount > 50, $"only {sig.PeakCount} peaks");
        Assert.True(sig.Bands.Count >= 2);
    }

    [Fact]
    public void Clip_IsPaddedOrTrimmedTo12Seconds()
    {
        var shortSig = ShazamSignature.FromClip(new AudioClip(Signals.Melody(16000, 4), 16000, 1));
        var longSig = ShazamSignature.FromClip(new AudioClip(Signals.Melody(16000, 20), 16000, 1));
        Assert.Equal(12 * 16000, shortSig.NumberSamples);
        Assert.Equal(12 * 16000, longSig.NumberSamples);
        Assert.Equal(12000, longSig.SampleMilliseconds);
    }

    [Fact]
    public void Encode_HasValidHeaderSizeAndCrc()
    {
        var sig = ShazamSignature.FromClip(new AudioClip(Signals.Melody(16000, 8), 16000, 1));
        var bin = sig.Encode();

        Assert.Equal(0xCAFE2580u, BinaryPrimitives.ReadUInt32LittleEndian(bin));
        Assert.Equal(0x94119C00u, BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(12)));
        Assert.Equal(3u << 27, BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(28)));
        Assert.Equal((uint)(bin.Length - 48), BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(8)));
        Assert.Equal((uint)(bin.Length - 48), BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(52)));
        Assert.Equal(0x40000000u, BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(48)));
        Assert.Equal(ShazamSignature.Crc32(bin.AsSpan(8)), BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(4)));
        Assert.Equal(0, bin.Length % 4);
    }

    [Fact]
    public void Crc32_MatchesStandardCheckValue()
    {
        Assert.Equal(0xCBF43926u, ShazamSignature.Crc32("123456789"u8));
    }

    [Fact]
    public void Signature_IsDeterministic()
    {
        var clip = new AudioClip(Signals.Melody(48000, 6, 2), 48000, 2);
        Assert.Equal(ShazamSignature.FromClip(clip).ToDataUri(), ShazamSignature.FromClip(clip).ToDataUri());
        Assert.StartsWith("data:audio/vnd.shazam.sig;base64,", ShazamSignature.FromClip(clip).ToDataUri());
    }
}
