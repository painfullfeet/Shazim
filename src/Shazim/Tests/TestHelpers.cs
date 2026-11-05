using System.Net;
using Shazim.Core.Audio;
using Shazim.Core.Interfaces;
using Shazim.Core.Models;
using Shazim.Core.Recognition;
using Shazim.Core.Services;
using Shazim.Data.Repositories;

namespace Shazim.Tests;

internal static class Signals
{
    /// <summary>A melody-like signal: a new chord every 250 ms, with decaying notes.</summary>
    public static float[] Melody(int sampleRate, double seconds, int channels = 1, int seed = 7)
    {
        var rng = new Random(seed);
        int frames = (int)(sampleRate * seconds);
        var data = new float[frames * channels];
        double[] freqs = new double[3];
        int noteLen = sampleRate / 4;
        for (int f = 0; f < frames; f++)
        {
            if (f % noteLen == 0)
                for (int k = 0; k < freqs.Length; k++) freqs[k] = 300 + rng.NextDouble() * 3000;
            double env = Math.Exp(-3.0 * (f % noteLen) / noteLen);
            double v = 0;
            foreach (var fr in freqs) v += Math.Sin(2 * Math.PI * fr * f / sampleRate);
            float s = (float)(0.25 * env * v);
            for (int c = 0; c < channels; c++) data[f * channels + c] = s;
        }
        return data;
    }

    public static float[] Sine(int sampleRate, double freq, double seconds, float amp = 0.5f)
    {
        var data = new float[(int)(sampleRate * seconds)];
        for (int i = 0; i < data.Length; i++) data[i] = amp * (float)Math.Sin(2 * Math.PI * freq * i / sampleRate);
        return data;
    }
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _respond;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();
    public FakeHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
        var (code, body) = _respond(request);
        return new HttpResponseMessage(code) { Content = new StringContent(body) };
    }
}

internal sealed class FakeHttpFactory : IAppHttpClientFactory
{
    private readonly HttpClient _client;
    public FakeHttpFactory(HttpMessageHandler handler) => _client = new HttpClient(handler);
    public HttpClient CreateClient() => _client;
}


/// <summary>Recognition backend with scripted answers.</summary>
internal sealed class FakeProvider : IMusicRecognitionService
{
    private readonly Func<AudioClip, int, RecognitionResult> _answer;
    public FakeProvider(string name, Func<AudioClip, int, RecognitionResult> answer, bool configured = true)
    {
        ServiceName = name;
        _answer = answer;
        IsConfigured = configured;
    }
    public string ServiceName { get; }
    public bool IsConfigured { get; }
    public int Calls { get; private set; }
    public List<TimeSpan> ClipDurations { get; } = new();

    public Task<RecognitionResult> IdentifyAsync(AudioClip clip, CancellationToken ct = default)
    {
        Calls++;
        ClipDurations.Add(clip.Duration);
        return Task.FromResult(_answer(clip, Calls));
    }
}

/// <summary>Capture service that "records" a fixed signal per device, in accelerated time.</summary>
internal sealed class FakeCapture : IAudioCaptureService
{
    private readonly Func<string, float[]?> _signalFor;
    private readonly int _rate;
    public FakeCapture(float[] mono, int rate) : this(_ => mono, rate) { }
    public FakeCapture(Func<string, float[]?> signalFor, int rate) { _signalFor = signalFor; _rate = rate; }
    public List<string> Opened { get; } = new();

    public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync() =>
        Task.FromResult<IReadOnlyList<AudioDevice>>(new[] { new AudioDevice(AudioDevice.DefaultId, "Fake", false) });

    public ICaptureSession StartSession(string deviceId)
    {
        var signal = _signalFor(deviceId) ?? throw new AudioCaptureException("Micro introuvable.");
        Opened.Add(deviceId);
        return new Session(signal, _rate, deviceId);
    }

    private sealed class Session : ICaptureSession
    {
        private readonly float[] _mono;
        private readonly int _rate;
        private readonly DateTime _start = DateTime.UtcNow;
        public Session(float[] mono, int rate, string name) { _mono = mono; _rate = rate; DeviceName = name; }

        // 1 real millisecond = 40 simulated milliseconds, so tests stay fast.
        public TimeSpan Elapsed => TimeSpan.FromTicks((DateTime.UtcNow - _start).Ticks * 40);
        public TimeSpan Captured => Elapsed;
        public float Level => 0.2f;
        public string DeviceName { get; }

        public AudioClip Snapshot(TimeSpan maxLength)
        {
            int frames = (int)Math.Min(_mono.Length, Math.Min(Elapsed.TotalSeconds, maxLength.TotalSeconds) * _rate);
            return new AudioClip(_mono.AsSpan(0, frames).ToArray(), _rate, 1);
        }

        public void Dispose() { }
    }
}

internal static class TestDb
{
    public static (LibraryService Library, SqliteTrackRepository Repo, string Path) Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shazamdesktop_test_{Guid.NewGuid():N}.db");
        var repo = SqliteTrackRepository.ForFile(path);
        return (new LibraryService(repo, new NullLogger<LibraryService>()), repo, path);
    }

    public static Track Track(string id, string title = "Titre", string artist = "Artiste") => new()
    {
        Id = id, Title = title, Artist = artist, Album = "Album", Source = "Test"
    };
}
