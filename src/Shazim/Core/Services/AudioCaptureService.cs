using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Shazim.Core.Audio;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;

namespace Shazim.Core.Services;

/// <summary>
/// WASAPI capture: loopback of an output device (what the PC plays) or a microphone.
/// Each identification opens its own short-lived session, so nothing is recorded
/// while the app is idle.
/// </summary>
public sealed class AudioCaptureService : IAudioCaptureService
{
    private readonly ILogger<AudioCaptureService> _logger;

    public AudioCaptureService(ILogger<AudioCaptureService> logger) => _logger = logger;

    public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync() => Task.Run<IReadOnlyList<AudioDevice>>(() =>
    {
        var list = new List<AudioDevice>
        {
            new(AudioDevice.DefaultId, CoreText.L("core.default_output", "Sortie audio par défaut de Windows"), false),
            new(AudioDevice.DefaultMicId, CoreText.L("core.default_mic", "Micro par défaut de Windows"), true),
        };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (d) list.Add(new AudioDevice("out:" + d.ID, d.FriendlyName, false));
            }
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (d) list.Add(new AudioDevice("in:" + d.ID, d.FriendlyName, true));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio device enumeration failed");
        }
        return list;
    });

    public ICaptureSession StartSession(string deviceId)
    {
        MMDevice? device = null;
        bool isInput = false;
        using var enumerator = new MMDeviceEnumerator();

        try
        {
            device = ResolveDevice(enumerator, deviceId, out isInput);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Device {Id} unavailable ({Message})", deviceId, ex.Message);
            // A missing microphone must not silently turn into PC-audio capture.
            if (deviceId.StartsWith("in:", StringComparison.Ordinal))
                throw new AudioCaptureException(CoreText.L("core.mic_missing", "Micro introuvable. Branchez-le ou choisissez-en un autre dans les réglages."), ex);
        }

        try
        {
            if (device == null)
            {
                isInput = false;
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            return new CaptureSession(device, isInput, _logger);
        }
        catch (Exception ex)
        {
            device?.Dispose();
            _logger.LogError(ex, "Unable to start capture on {Id}", deviceId);
            throw new AudioCaptureException(CoreText.L("core.device_error", "Impossible d'accéder au périphérique audio. Vérifiez qu'une sortie audio est active."), ex);
        }
    }

    private static MMDevice? ResolveDevice(MMDeviceEnumerator enumerator, string deviceId, out bool isInput)
    {
        isInput = false;
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId == AudioDevice.DefaultId)
            return null;
        if (deviceId == AudioDevice.DefaultMicId)
        {
            isInput = true;
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }

        string raw = deviceId;
        if (deviceId.StartsWith("in:", StringComparison.Ordinal)) { isInput = true; raw = deviceId[3..]; }
        else if (deviceId.StartsWith("out:", StringComparison.Ordinal)) raw = deviceId[4..];

        var device = enumerator.GetDevice(raw); // legacy settings stored the raw endpoint id
        if (device.State != DeviceState.Active)
        {
            device.Dispose();
            if (isInput) throw new InvalidOperationException("Microphone is not active");
            return null;
        }
        isInput = device.DataFlow == DataFlow.Capture;
        return device;
    }

    private sealed class CaptureSession : ICaptureSession
    {
        private const int BufferSeconds = 15;

        private readonly ILogger<AudioCaptureService> _logger;
        private readonly MMDevice _device;
        private readonly WasapiCapture _capture;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _lock = new();
        private readonly int _channels;
        private readonly int _sampleRate;
        private readonly float[] _ring;
        private float[] _convert = Array.Empty<float>();
        private int _writePos;
        private long _totalFrames;
        private volatile float _level;
        private bool _disposed;

        public CaptureSession(MMDevice device, bool isInput, ILogger<AudioCaptureService> logger)
        {
            _logger = logger;
            _device = device;
            DeviceName = device.FriendlyName;

            _capture = isInput
                ? new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared }
                : new WasapiLoopbackCapture(device);

            var format = _capture.WaveFormat;
            _channels = Math.Max(1, format.Channels);
            _sampleRate = format.SampleRate;
            _ring = new float[BufferSeconds * _sampleRate * _channels];

            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;
            _capture.StartRecording();
            _logger.LogInformation("Capture started on {Device} ({Format})", DeviceName, format.ToString());
        }

        public string DeviceName { get; }
        public TimeSpan Elapsed => _clock.Elapsed;
        public float Level => _level;

        public TimeSpan Captured
        {
            get { lock (_lock) return TimeSpan.FromSeconds((double)_totalFrames / _sampleRate); }
        }

        private void OnData(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;
            try
            {
                int count = ConvertToFloat(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
                if (count == 0) return;
                _level = _level * 0.6f + AudioDsp.Rms(_convert.AsSpan(0, count)) * 0.4f;

                lock (_lock)
                {
                    int remaining = count, src = 0;
                    while (remaining > 0)
                    {
                        int chunk = Math.Min(remaining, _ring.Length - _writePos);
                        Array.Copy(_convert, src, _ring, _writePos, chunk);
                        _writePos = (_writePos + chunk) % _ring.Length;
                        src += chunk;
                        remaining -= chunk;
                    }
                    _totalFrames += count / _channels;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio buffer conversion failed");
            }
        }

        private int ConvertToFloat(byte[] buffer, int bytes, WaveFormat format)
        {
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                           (format is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
            int bits = format.BitsPerSample;
            int samples = bytes / (bits / 8);
            if (_convert.Length < samples) _convert = new float[samples];

            if (isFloat && bits == 32)
            {
                Buffer.BlockCopy(buffer, 0, _convert, 0, samples * 4);
            }
            else if (bits == 16)
            {
                for (int i = 0; i < samples; i++) _convert[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
            }
            else if (bits == 24)
            {
                for (int i = 0, b = 0; i < samples; i++, b += 3)
                    _convert[i] = ((buffer[b] << 8 | buffer[b + 1] << 16 | buffer[b + 2] << 24) >> 8) / 8388608f;
            }
            else if (bits == 32)
            {
                for (int i = 0; i < samples; i++) _convert[i] = BitConverter.ToInt32(buffer, i * 4) / 2147483648f;
            }
            else
            {
                return 0;
            }
            return samples - samples % _channels;
        }

        private void OnStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                _logger.LogWarning("Capture stopped with error: {Message}", e.Exception.Message);
        }

        public AudioClip Snapshot(TimeSpan maxLength)
        {
            lock (_lock)
            {
                long available = Math.Min(_totalFrames, _ring.Length / _channels);
                long wanted = Math.Min(available, (long)(maxLength.TotalSeconds * _sampleRate));
                int samples = (int)wanted * _channels;
                var result = new float[samples];
                int start = ((_writePos - samples) % _ring.Length + _ring.Length) % _ring.Length;
                int firstPart = Math.Min(samples, _ring.Length - start);
                Array.Copy(_ring, start, result, 0, firstPart);
                if (firstPart < samples) Array.Copy(_ring, 0, result, firstPart, samples - firstPart);
                return new AudioClip(result, _sampleRate, _channels);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _capture.DataAvailable -= OnData;
                _capture.RecordingStopped -= OnStopped;
                _capture.StopRecording();
                _capture.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error while stopping capture: {Message}", ex.Message);
            }
            _device.Dispose();
        }
    }
}
