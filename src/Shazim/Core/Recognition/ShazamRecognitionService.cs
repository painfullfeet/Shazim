using System.Net.Http.Json;
using System.Text.Json;
using Shazim.Core.Audio;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;
using Shazim.Core.Models;

namespace Shazim.Core.Recognition;

/// <summary>
/// Recognition through the public Shazam "tag" endpoint, with a locally computed
/// signature (no audio leaves the machine, no API key required).
/// </summary>
public class ShazamRecognitionService : IMusicRecognitionService
{
    public const string Name = "Shazam";
    private const string Endpoint = "https://amp.shazam.com/discovery/v5/fr/FR/android/-/tag/{0}/{1}" +
        "?sync=true&webv3=true&sampling=true&connected=&shazamapiversion=v3&sharehub=true&video=v3";

    private static readonly string[] UserAgents =
    {
        "Dalvik/2.1.0 (Linux; U; Android 13; Pixel 7 Build/TQ3A.230805.001)",
        "Dalvik/2.1.0 (Linux; U; Android 12; SM-G991B Build/SP1A.210812.016)",
        "Dalvik/2.1.0 (Linux; U; Android 11; Mi 10 Build/RKQ1.200826.002)",
    };

    private readonly IAppHttpClientFactory _http;
    private readonly ILogger<ShazamRecognitionService> _logger;

    public ShazamRecognitionService(IAppHttpClientFactory http, ILogger<ShazamRecognitionService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string ServiceName => Name;
    public bool IsConfigured => true;

    public async Task<RecognitionResult> IdentifyAsync(AudioClip clip, CancellationToken cancellationToken = default)
    {
        if (clip.IsEmpty)
            return RecognitionResult.NoMatch(Name);

        ShazamSignature signature;
        try
        {
            signature = await Task.Run(() => ShazamSignature.FromClip(clip), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Signature generation failed");
            return RecognitionResult.Failed(Name, CoreText.L("core.analyse_failed", "Impossible d'analyser l'audio capturé."));
        }

        if (signature.PeakCount < 20)
        {
            _logger.LogInformation("Signature too poor ({Peaks} peaks), skipping request", signature.PeakCount);
            return RecognitionResult.NoMatch(Name);
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = new
        {
            // Fixed, generic position (centre of France) on purpose: the user's real location is never sent.
            geolocation = new { altitude = 300, latitude = 45, longitude = 2 },
            signature = new { samplems = signature.SampleMilliseconds, timestamp = (uint)nowMs, uri = signature.ToDataUri() },
            timestamp = (uint)nowMs,
            timezone = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "Europe/Paris"
        };

        var url = string.Format(Endpoint, Guid.NewGuid().ToString().ToUpperInvariant(), Guid.NewGuid().ToString());
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgents[Random.Shared.Next(UserAgents.Length)]);
        request.Headers.TryAddWithoutValidation("Content-Language", "fr_FR");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            _logger.LogDebug("Shazam request: {Ms} ms of audio, {Peaks} peaks", signature.SampleMilliseconds, signature.PeakCount);
            using var response = await _http.CreateClient().SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Shazam returned HTTP {Status}", (int)response.StatusCode);
                return (int)response.StatusCode == 429
                    ? RecognitionResult.Failed(Name, CoreText.L("core.rate_limited", "Trop de requêtes, réessayez dans quelques instants."), fatal: true)
                    : RecognitionResult.Failed(Name, CoreText.F("core.http_error", "Le service de reconnaissance a répondu {0}.", (int)response.StatusCode));
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Parse(json);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Shazam request timed out");
            return RecognitionResult.Failed(Name, CoreText.L("core.timeout", "Le service de reconnaissance ne répond pas."));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Shazam network error: {Message}", ex.Message);
            return RecognitionResult.Failed(Name, CoreText.L("core.offline", "Pas de connexion Internet."), fatal: true);
        }
    }

    /// <summary>Parses a Shazam tag response. Public for tests.</summary>
    public static RecognitionResult Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        bool hasMatch = root.TryGetProperty("matches", out var matches)
                        && matches.ValueKind == JsonValueKind.Array && matches.GetArrayLength() > 0;
        if (!hasMatch || !root.TryGetProperty("track", out var t) || t.ValueKind != JsonValueKind.Object)
        {
            int? retry = root.TryGetProperty("retryms", out var r) && r.TryGetInt32(out var ms) ? ms : null;
            return RecognitionResult.NoMatch(Name, retry);
        }

        var track = new Track
        {
            Id = "shazam:" + Str(t, "key"),
            Title = Str(t, "title"),
            Artist = Str(t, "subtitle"),
            Source = Name,
            Genre = t.TryGetProperty("genres", out var g) ? Str(g, "primary") : "",
        };

        if (t.TryGetProperty("images", out var images))
        {
            var art = Str(images, "coverarthq");
            track.AlbumArtUrl = HttpsOnly(string.IsNullOrEmpty(art) ? Str(images, "coverart") : art);
        }

        if (t.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
        {
            foreach (var section in sections.EnumerateArray())
            {
                if (!section.TryGetProperty("metadata", out var meta) || meta.ValueKind != JsonValueKind.Array) continue;
                foreach (var m in meta.EnumerateArray())
                {
                    var title = Str(m, "title").ToLowerInvariant();
                    var text = Str(m, "text");
                    if (title is "album") track.Album = text;
                    else if (title is "label" or "maison de disques") track.Label = text;
                    else if (title is "released" or "sortie" or "date de sortie") track.ReleaseYear = text;
                }
            }
        }

        // Links: Shazam page, Apple Music, preview audio, plus search links built from metadata.
        var shazamUrl = Str(t, "url");
        if (string.IsNullOrEmpty(shazamUrl) && t.TryGetProperty("share", out var share)) shazamUrl = Str(share, "href");
        if (HttpsOnly(shazamUrl) != "") track.ExternalLinks.Add(new ExternalLink("Shazam", shazamUrl, "Shazam"));

        // Streaming platforms Shazam lists for this track ("Disponible sur").
        var query = $"{track.Artist} {track.Title}".Trim();
        if (t.TryGetProperty("hub", out var hub) && hub.ValueKind == JsonValueKind.Object)
        {
            string? appleUrl = null;
            foreach (var uri in EnumerateActionUris(hub))
            {
                if (string.IsNullOrEmpty(track.PreviewUrl) && HttpsOnly(uri) != "" &&
                    (uri.Contains(".m4a") || uri.Contains("audio-ssl") || uri.Contains("itunes.apple.com/itunes-assets")))
                    track.PreviewUrl = uri;
                else if (uri.StartsWith("https://music.apple.com/", StringComparison.OrdinalIgnoreCase) && HttpsOnly(uri) != "") appleUrl ??= uri;
            }

            bool apple = appleUrl != null || Str(hub, "type").Equals("APPLEMUSIC", StringComparison.OrdinalIgnoreCase);
            if (apple) LinkBuilder.Add(track, "Apple Music", appleUrl ?? LinkBuilder.SearchUrl("Apple Music", query));

            if (hub.TryGetProperty("providers", out var providers) && providers.ValueKind == JsonValueKind.Array)
            {
                foreach (var provider in providers.EnumerateArray())
                {
                    var platform = Str(provider, "type").ToUpperInvariant() switch
                    {
                        "SPOTIFY" => "Spotify",
                        "DEEZER" => "Deezer",
                        "YOUTUBEMUSIC" => "YouTube Music",
                        _ => null
                    };
                    if (platform == null) continue;
                    var direct = EnumerateActionUris(provider).FirstOrDefault(u => HttpsOnly(u) != "");
                    LinkBuilder.Add(track, platform, direct ?? LinkBuilder.SearchUrl(platform, query));
                }
            }
        }
        else
        {
            // No platform information at all: offer the usual searches.
            foreach (var platform in LinkBuilder.StreamingPlatforms)
                LinkBuilder.Add(track, platform, LinkBuilder.SearchUrl(platform, query));
        }
        LinkBuilder.Add(track, "YouTube", LinkBuilder.SearchUrl("YouTube", query));

        // Position of the sample in the song: lets the lyrics follow the music.
        double? offset = null;
        var first = matches[0];
        if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("offset", out var off) &&
            off.ValueKind == JsonValueKind.Number && off.TryGetDouble(out var seconds) && seconds >= 0 && seconds < 3600)
            offset = seconds;

        return new RecognitionResult
        {
            Status = RecognitionStatus.Match, Provider = Name, Track = track, Confidence = 1f, MatchOffsetSeconds = offset
        };
    }

    private static IEnumerable<string> EnumerateActionUris(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    if (p.NameEquals("uri") && p.Value.ValueKind == JsonValueKind.String)
                        yield return p.Value.GetString() ?? "";
                    else
                        foreach (var u in EnumerateActionUris(p.Value)) yield return u;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var u in EnumerateActionUris(item)) yield return u;
                break;
        }
    }

    /// <summary>Network answers are untrusted: keep only well-formed https URLs (no file:, UNC, other schemes).</summary>
    internal static string HttpsOnly(string? url) =>
        Security.LinkPolicy.Sanitize(url) is { } safe && safe.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url!.Trim() : "";

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}

/// <summary>Builds links to streaming services.</summary>
public static class LinkBuilder
{
    /// <summary>Services where a track can be listened to in full.</summary>
    public static readonly string[] StreamingPlatforms = { "Spotify", "Apple Music", "Deezer", "YouTube Music" };

    public static string SearchUrl(string platform, string query)
    {
        var q = Uri.EscapeDataString(query);
        return platform switch
        {
            "Spotify" => $"https://open.spotify.com/search/{q}",
            "Apple Music" => $"https://music.apple.com/fr/search?term={q}",
            "Deezer" => $"https://www.deezer.com/search/{q}",
            "YouTube Music" => $"https://music.youtube.com/search?q={q}",
            _ => $"https://www.youtube.com/results?search_query={q}"
        };
    }

    public static void Add(Track track, string platform, string url)
    {
        if (track.FindLink(platform) == null && Security.LinkPolicy.IsSafe(url))
            track.ExternalLinks.Add(new ExternalLink(platform, url, platform));
    }
}
