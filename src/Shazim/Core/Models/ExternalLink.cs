namespace Shazim.Core.Models;

public record ExternalLink(string Platform, string Url, string DisplayName);

public enum RecognitionStatus
{
    Match,
    NoMatch,
    Error
}

/// <summary>Result of one recognition request against one provider.</summary>
public class RecognitionResult
{
    public RecognitionStatus Status { get; init; }
    public Track? Track { get; init; }
    public float Confidence { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? Error { get; init; }

    /// <summary>Shazam may ask the client to retry after this delay with more audio.</summary>
    public int? RetryAfterMs { get; init; }

    public bool IsMatch => Status == RecognitionStatus.Match && Track != null;

    public static RecognitionResult NoMatch(string provider, int? retryMs = null) =>
        new() { Status = RecognitionStatus.NoMatch, Provider = provider, RetryAfterMs = retryMs };

    /// <summary>The error will not go away by retrying a few seconds later (offline, rate limited).</summary>
    public bool IsFatal { get; init; }

    public static RecognitionResult Failed(string provider, string error, bool fatal = false) =>
        new() { Status = RecognitionStatus.Error, Provider = provider, Error = error, IsFatal = fatal };

    public static RecognitionResult Matched(string provider, Track track, float confidence) =>
        new() { Status = RecognitionStatus.Match, Provider = provider, Track = track, Confidence = confidence };
}
