using Shazim.Core.Audio;
using Shazim.Core.Models;

namespace Shazim.Core.Interfaces;

/// <summary>
/// A music recognition backend (Shazam).
/// </summary>
public interface IMusicRecognitionService
{
    /// <summary>Stable identifier used in settings ("Shazam").</summary>
    string ServiceName { get; }

    /// <summary>True when the backend can be used with the current settings (API key present, tool installed...).</summary>
    bool IsConfigured { get; }

    Task<RecognitionResult> IdentifyAsync(AudioClip clip, CancellationToken cancellationToken = default);
}
