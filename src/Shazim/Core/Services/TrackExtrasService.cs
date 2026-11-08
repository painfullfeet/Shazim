using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;
using Shazim.Core.Models;

namespace Shazim.Core.Services;

/// <summary>Extra facts about a track, from Deezer's public catalogue (no key).</summary>
public sealed class TrackExtras
{
    public string? ReleaseDate { get; set; }      // "2013-04-19"
    public double? Bpm { get; set; }
    public int? DurationSeconds { get; set; }
    public bool Explicit { get; set; }
    public List<string> Contributors { get; set; } = new();
    public string? Isrc { get; set; }
    public DateTime FetchedUtc { get; set; }
    public bool NotFound { get; set; }

    public DateTime? ReleaseDateValue =>
        DateTime.TryParseExact(ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>
/// Looks the track up on api.deezer.com (search, then track details) and caches the answer on
/// disk. Only artist and title are sent.
/// </summary>
public sealed class TrackExtrasService
{
    private const string Api = "https://api.deezer.com/";
    private static readonly TimeSpan RetryMissingAfter = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IAppHttpClientFactory _http;
    private readonly ILogger<TrackExtrasService> _logger;
    private readonly string _cacheDir;
    private readonly Dictionary<string, TrackExtras?> _memory = new();

    public TrackExtrasService(IAppHttpClientFactory http, ILogger<TrackExtrasService> logger)
        : this(http, logger, Path.Combine(AppPaths.DataDirectory, "extras")) { }

    public TrackExtrasService(IAppHttpClientFactory http, ILogger<TrackExtrasService> logger, string cacheDir)
    {
        _http = http;
        _logger = logger;
        _cacheDir = cacheDir;
    }

    public async Task<TrackExtras?> GetAsync(Track track, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist)) return null;
        lock (_memory) if (_memory.TryGetValue(track.Id, out var known)) return known;

        var file = Path.Combine(_cacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(track.Id)))[..24] + ".json");
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<TrackExtras>(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false)) is { } cached &&
                (!cached.NotFound || DateTime.UtcNow - cached.FetchedUtc < RetryMissingAfter))
                return Remember(track.Id, cached.NotFound ? null : cached);
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }

        TrackExtras? result;
        try { result = await FetchAsync(track, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogInformation("Track extras unavailable: {Message}", ex.Message);
            return null; // offline: try again next time
        }

        var toStore = result ?? new TrackExtras { NotFound = true };
        toStore.FetchedUtc = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(_cacheDir);
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(toStore), ct).ConfigureAwait(false);
        }
        catch (IOException) { }
        return Remember(track.Id, result);
    }

    private TrackExtras? Remember(string id, TrackExtras? e)
    {
        lock (_memory) _memory[id] = e;
        return e;
    }

    private async Task<TrackExtras?> FetchAsync(Track track, CancellationToken ct)
    {
        string artist = MainArtist(track.Artist), title = CleanTitle(track.Title);
        var q = Uri.EscapeDataString($"artist:\"{artist}\" track:\"{title}\"");
        var search = await GetJsonAsync<SearchAnswer>($"{Api}search?q={q}&limit=8", ct).ConfigureAwait(false);
        var hit = Pick(search?.Data, artist, title);
        if (hit == null) return null;

        var t = await GetJsonAsync<DeezerTrack>($"{Api}track/{hit.Id}", ct).ConfigureAwait(false);
        if (t == null) return null;
        return new TrackExtras
        {
            ReleaseDate = string.IsNullOrEmpty(t.ReleaseDate) || t.ReleaseDate.StartsWith("0000") ? null : t.ReleaseDate,
            Bpm = t.Bpm > 30 ? Math.Round(t.Bpm) : null,
            DurationSeconds = t.Duration > 0 ? t.Duration : null,
            Explicit = t.ExplicitLyrics,
            Isrc = string.IsNullOrWhiteSpace(t.Isrc) ? null : t.Isrc,
            Contributors = (t.Contributors ?? new()).Select(c => c.Name ?? "").Where(n => n.Length > 0).Distinct().Take(8).ToList(),
        };
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Shazim (https://github.com/painfullfeet/Shazim)");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var r = await _http.CreateClient().SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!r.IsSuccessStatusCode) return null;
        var text = await r.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (text.Length > 2_000_000) return null;
        return JsonSerializer.Deserialize<T>(text, Json);
    }

    internal static SearchItem? Pick(IEnumerable<SearchItem>? items, string artist, string title)
    {
        if (items == null) return null;
        string a = Fold(artist), t = Fold(title);
        if (a.Length == 0 || t.Length == 0) return null;
        bool SameArtist(SearchItem i)
        {
            var n = Fold(i.Artist?.Name);
            return n.Length > 0 && (n.Contains(a) || a.Contains(n));
        }
        var list = items.ToList();
        return list.FirstOrDefault(i => SameArtist(i) && Fold(i.Title).StartsWith(t))
            ?? list.FirstOrDefault(i => SameArtist(i) && Fold(i.Title).Contains(t));
    }

    /// <summary>"Daft Punk, Pharrell Williams &amp; Nile Rodgers" → "Daft Punk".</summary>
    internal static string MainArtist(string artist)
    {
        foreach (var sep in new[] { " feat", " ft.", ",", " & ", " x ", " / " })
        {
            int i = artist.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (i > 0) artist = artist[..i];
        }
        return artist.Trim();
    }

    /// <summary>"Levitating (feat. DaBaby)" → "Levitating".</summary>
    internal static string CleanTitle(string title)
    {
        int i = title.IndexOfAny(new[] { '(', '[' });
        if (i > 0) title = title[..i];
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) title = title[..dash];
        return title.Trim();
    }

    private static string Fold(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    internal sealed class SearchAnswer { [JsonPropertyName("data")] public List<SearchItem>? Data { get; set; } }

    internal sealed class SearchItem
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("artist")] public Named? Artist { get; set; }
    }

    internal sealed class Named { [JsonPropertyName("name")] public string? Name { get; set; } }

    internal sealed class DeezerTrack
    {
        [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
        [JsonPropertyName("bpm")] public double Bpm { get; set; }
        [JsonPropertyName("duration")] public int Duration { get; set; }
        [JsonPropertyName("explicit_lyrics")] public bool ExplicitLyrics { get; set; }
        [JsonPropertyName("isrc")] public string? Isrc { get; set; }
        [JsonPropertyName("contributors")] public List<Named>? Contributors { get; set; }
    }
}
