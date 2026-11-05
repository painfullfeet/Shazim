using System.Text.Json.Serialization;

namespace Shazim.Core.Models;

/// <summary>
/// A unique recording identified by a recognition service.
/// Stored once in the library and referenced by many detections.
/// </summary>
public class Track
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("artist")] public string Artist { get; set; } = string.Empty;
    [JsonPropertyName("album")] public string Album { get; set; } = string.Empty;
    [JsonPropertyName("albumArtUrl")] public string AlbumArtUrl { get; set; } = string.Empty;
    [JsonPropertyName("genre")] public string Genre { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("releaseYear")] public string ReleaseYear { get; set; } = string.Empty;
    [JsonPropertyName("previewUrl")] public string PreviewUrl { get; set; } = string.Empty;
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("externalLinks")] public List<ExternalLink> ExternalLinks { get; set; } = new();
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Filled by the repository; not a stored column.</summary>
    [JsonPropertyName("isFavorite")] public bool IsFavorite { get; set; }

    /// <summary>Number of times the track was detected (filled by the repository).</summary>
    [JsonIgnore] public int DetectionCount { get; set; }

    [JsonIgnore] public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? CoreText.L("core.unknown_title", "Titre inconnu") : Title;
    [JsonIgnore] public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? CoreText.L("core.unknown_artist", "Artiste inconnu") : Artist;
    [JsonIgnore] public string DisplayAlbum => string.IsNullOrWhiteSpace(Album) ? "" : Album;
    [JsonIgnore] public bool HasArtwork => !string.IsNullOrWhiteSpace(AlbumArtUrl);

    /// <summary>One-line metadata summary: "Album · 2019 · Pop".</summary>
    [JsonIgnore]
    public string MetaLine => string.Join("  ·  ", new[] { Album, ReleaseYear, Genre }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Streaming services listed for this track ("Disponible sur").</summary>
    [JsonIgnore]
    public IReadOnlyList<ExternalLink> Platforms => ExternalLinks
        .Where(l => Array.IndexOf(Recognition.LinkBuilder.StreamingPlatforms, l.Platform) >= 0)
        .OrderBy(l => Array.IndexOf(Recognition.LinkBuilder.StreamingPlatforms, l.Platform))
        .ToList();

    [JsonIgnore] public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewUrl);

    /// <summary>First and last time the track was heard (filled by the repository).</summary>
    [JsonIgnore] public DateTime? FirstDetectedAt { get; set; }
    [JsonIgnore] public DateTime? LastDetectedAt { get; set; }

    public string? FindLink(string platform) =>
        ExternalLinks.FirstOrDefault(l => string.Equals(l.Platform, platform, StringComparison.OrdinalIgnoreCase))?.Url;
}
