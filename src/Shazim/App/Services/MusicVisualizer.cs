using NAudio.CoreAudioApi;
using NAudio.Wave;
using Shazim.Core.Audio;
using Shazim.Core.Interfaces;

namespace Shazim.App.Services;

/// <summary>
/// Live spectrum of what the PC is playing, for the home-screen button.
/// Listens to the output (loopback) only, never the microphone, and keeps nothing:
/// each 2048-sample window is turned into band levels and dropped.
/// </summary>
public sealed class MusicVisualizer : IDisposable
{
    public const int BandCount = 48;
    private const int FftSize = 2048;
    private const double MinHz = 40, MaxHz = 14000;

    private readonly object _lock = new();
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _hann = BuildHann();
    private int[] _edges = Array.Empty<int>();
    private float _bass;
    private int _filled;
    private int _writePos;
    private int _sinceLast;
    private WasapiCapture? _capture;
    private int _channels = 2;
    private int _sampleRate = 48000;
    private bool _running;
    private string _deviceId = AudioDevice.DefaultId;
    private DateTime _lastData = DateTime.MinValue;

    /// <summary>True when audio arrived recently (music is playing).</summary>
    public bool HasSignal => DevFakeMusic || (DateTime.UtcNow - _lastData) < TimeSpan.FromMilliseconds(300);

    /// <summary>Developer aid (snapshots): synthetic 120 BPM music instead of the PC sound, so effects can be checked silently.</summary>
    public static bool DevFakeMusic { get; set; }

    private static double FakePhase => DateTime.UtcNow.TimeOfDay.TotalSeconds * 2; // beats at 120 BPM

    public void Start(string outputDeviceId)
    {
        lock (_lock)
        {
            if (_running && outputDeviceId == _deviceId) return;
            _running = true;
            _deviceId = outputDeviceId;
        }
        Task.Run(() =>
        {
            StopCapture();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = outputDeviceId.StartsWith("out:", StringComparison.Ordinal)
                    ? enumerator.GetDevice(outputDeviceId[4..])
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var capture = new WasapiLoopbackCapture(device);
                _beats.Reset();
                _channels = Math.Max(1, capture.WaveFormat.Channels);
                _sampleRate = capture.WaveFormat.SampleRate;
                _edges = BuildEdges(_sampleRate);
                capture.DataAvailable += OnData;
                capture.StartRecording();
                lock (_lock)
                {
                    if (!_running) { capture.Dispose(); return; }
                    _capture = capture;
                }
            }
            catch
            {
                // No output device or it disappeared: the button simply does not react.
            }
        });
    }

    public void Stop()
    {
        lock (_lock) _running = false;
        Task.Run(StopCapture);
        Array.Clear(_bands);
        _bass = 0;
    }

    private void StopCapture()
    {
        WasapiCapture? capture;
        lock (_lock) { capture = _capture; _capture = null; }
        if (capture == null) return;
        try
        {
            capture.DataAvailable -= OnData;
            capture.StopRecording();
            capture.Dispose();
        }
        catch { }
    }

    /// <summary>Copies the current band levels (0..1) and the bass level.</summary>
    public float Read(float[] bands)
    {
        if (DevFakeMusic)
        {
            double phase = FakePhase, kick = Math.Exp(-(phase % 1) * 6);
            for (int i = 0; i < bands.Length; i++)
                bands[i] = (float)Math.Clamp(0.25 + 0.55 * kick * Math.Exp(-i / 14.0) + 0.2 * Math.Sin(i * 0.5 + phase * 3), 0, 1);
            return (float)(0.3 + 0.6 * kick);
        }
        lock (_lock)
        {
            Array.Copy(_bands, bands, Math.Min(bands.Length, _bands.Length));
            return _bass;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || sender is not WasapiCapture cap) return;
        var format = cap.WaveFormat;
        int bytesPerSample = format.BitsPerSample / 8;
        int frames = e.BytesRecorded / (bytesPerSample * _channels);
        _lastData = DateTime.UtcNow;

        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < _channels; c++)
            {
                int offset = (f * _channels + c) * bytesPerSample;
                sum += bytesPerSample == 4 && format.Encoding != WaveFormatEncoding.Pcm
                    ? BitConverter.ToSingle(e.Buffer, offset)
                    : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, offset) / 32768f
                    : BitConverter.ToInt32(e.Buffer, offset) / 2147483648f;
            }
            // Circular analysis window (no copying per sample).
            _window[_writePos] = sum / _channels;
            _writePos = (_writePos + 1) % FftSize;
            if (_filled < FftSize) _filled++;
            if (++_sinceLast >= FftSize / 4 && _filled == FftSize)
            {
                _sinceLast = 0;
                Analyse();
            }
        }
    }

    private void Analyse()
    {
        for (int i = 0; i < FftSize; i++) { _re[i] = _window[(_writePos + i) % FftSize] * _hann[i]; _im[i] = 0; }
        AudioDsp.Fft(_re, _im);

        var bands = new float[BandCount];
        for (int b = 0; b < BandCount; b++)
        {
            int from = _edges[b], to = Math.Max(_edges[b] + 1, _edges[b + 1]);
            double peak = 0;
            for (int k = from; k < to && k < FftSize / 2; k++)
                peak = Math.Max(peak, Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]));
            bands[b] = Normalize(peak);
        }

        // Bass: 40-150 Hz, what makes the button "kick".
        double hz = (double)_sampleRate / FftSize, bassPeak = 0;
        for (int k = (int)(40 / hz); k <= (int)(150 / hz); k++)
            bassPeak = Math.Max(bassPeak, Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]));

        float bass = Normalize(bassPeak);
        // Beats are found here, at the analysis rate (~94 per second), not once per screen frame:
        // a short kick is never missed between two frames.
        _beats.Feed(bass, (double)(FftSize / 4) / _sampleRate);
        lock (_lock)
        {
            Array.Copy(bands, _bands, BandCount);
            _bass = bass;
            _beatCount = _beats.Count;
        }
    }

    private readonly BeatDetector _beats = new();
    private int _beatCount;

    /// <summary>Increases by one on every beat of the music playing on the PC.</summary>
    public int BeatCount { get { if (DevFakeMusic) return (int)FakePhase; lock (_lock) return _beatCount; } }

    /// <summary>Magnitude to 0..1 on a 54 dB scale.</summary>
    private static float Normalize(double magnitude)
    {
        double db = 20 * Math.Log10(magnitude / (FftSize / 4.0) + 1e-9);
        return (float)Math.Clamp((db + 54) / 54, 0, 1);
    }

    /// <summary>Log-spaced band edges (FFT bin indexes) from 40 Hz to 14 kHz.</summary>
    private static int[] BuildEdges(int sampleRate)
    {
        var edges = new int[BandCount + 1];
        double hz = (double)sampleRate / FftSize;
        for (int i = 0; i <= BandCount; i++)
        {
            double f = MinHz * Math.Pow(MaxHz / MinHz, (double)i / BandCount);
            edges[i] = Math.Clamp((int)Math.Round(f / hz), 1, FftSize / 2 - 1);
        }
        return edges;
    }

    private static float[] BuildHann()
    {
        var w = new float[FftSize];
        for (int i = 0; i < FftSize; i++) w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        return w;
    }

    public void Dispose() => Stop();
}
