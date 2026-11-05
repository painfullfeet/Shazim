using Shazim.Core.Audio;

namespace Shazim.Core.Interfaces;

/// <summary>An audio endpoint the user can listen to.</summary>
/// <param name="Id">"default", "out:{endpoint id}" (system audio) or "in:{endpoint id}" (microphone).</param>
public sealed record AudioDevice(string Id, string Name, bool IsInput)
{
    public const string DefaultId = "default";
    public const string DefaultMicId = "in:default";
    public string Kind => Id == DefaultId ? "Sortie par défaut" : Id == DefaultMicId ? "Micro par défaut" : IsInput ? "Microphone" : "Sortie audio";
    public override string ToString() => Name;
}

/// <summary>
/// Captures what the PC is playing (WASAPI loopback) or a microphone.
/// </summary>
public interface IAudioCaptureService
{
    Task<IReadOnlyList<AudioDevice>> GetDevicesAsync();

    /// <summary>
    /// Starts recording from a device. Audio accumulates in a ring buffer until the
    /// session is disposed. Throws <see cref="AudioCaptureException"/> when the device cannot be opened.
    /// </summary>
    ICaptureSession StartSession(string deviceId);
}

public interface ICaptureSession : IDisposable
{
    /// <summary>Wall-clock time since the session started.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>Amount of audio actually received.</summary>
    TimeSpan Captured { get; }

    /// <summary>Recent RMS level, 0..1.</summary>
    float Level { get; }

    /// <summary>Friendly name of the device actually used.</summary>
    string DeviceName { get; }

    /// <summary>Copy of the most recent audio, at most <paramref name="maxLength"/> long.</summary>
    AudioClip Snapshot(TimeSpan maxLength);
}

public sealed class AudioCaptureException : Exception
{
    public AudioCaptureException(string message, Exception? inner = null) : base(message, inner) { }
}
