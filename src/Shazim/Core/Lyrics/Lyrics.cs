using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;
using Shazim.Core.Models;

namespace Shazim.Core.Lyrics;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Lyrics of a track: timed lines (karaoke) or plain text.</summary>
public sealed class TrackLyrics
{
    public bool IsSynced { get; init; }
    public bool IsInstrumental { get; init; }
    public IReadOnlyList<LyricLine> Lines { get; init; } = Array.Empty<LyricLine>();

    /// <summary>Length of the track according to the lyrics source (0 if unknown).</summary>
    public double DurationSeconds { get; init; }

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Index of the line being sung at <paramref name="position"/>, -1 before the first one.</summary>
    public int IndexAt(TimeSpan position)
    {
        if (!IsSynced || Lines.Count == 0 || position < Lines[0].Time) return -1;
        int lo = 0, hi = Lines.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Lines[mid].Time <= position) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    public static TrackLyrics FromLrc(string? synced, string? plain, bool instrumental = false, double duration = 0)
    {
        var lines = LrcParser.Parse(synced);
        if (lines.Count > 0) return new TrackLyrics { IsSynced = true, Lines = lines, IsInstrumental = instrumental, DurationSeconds = duration };
        var text = (plain ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
        while (text.Count > 0 && text[^1].Length == 0) text.RemoveAt(text.Count - 1);
        return new TrackLyrics
        {
            IsSynced = false, IsInstrumental = instrumental, DurationSeconds = duration,
            Lines = text.Select(t => new LyricLine(TimeSpan.Zero, t)).ToList()
        };
    }
}

/// <summary>Reads LRC files: "[mm:ss.xx]text", several stamps per line, "[offset:+ms]".</summary>
public static class LrcParser
{
    private static readonly Regex Stamp = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant);
    private static readonly Regex Offset = new(@"^\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<LyricLine> Parse(string? lrc)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return result;
        double offsetMs = 0;
        foreach (var raw in lrc.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            var off = Offset.Match(line);
            if (off.Success) { offsetMs = double.Parse(off.Groups[1].Value, CultureInfo.InvariantCulture); continue; }

            var stamps = new List<TimeSpan>();
            int pos = 0;
            Match m;
            while ((m = Stamp.Match(line, pos)).Success && m.Index == pos)
            {
                int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double frac = 0;
                if (m.Groups[3].Success)
                {
                    var f = m.Groups[3].Value;
                    frac = int.Parse(f, CultureInfo.InvariantCulture) / Math.Pow(10, f.Length);
                }
                stamps.Add(TimeSpan.FromSeconds(min * 60 + sec + frac));
                pos = m.Index + m.Length;
            }
            if (stamps.Count == 0) continue; // metadata ([ar:], [ti:]) or junk
            var text = line[pos..].Trim();
            // Word-level stamps ("<00:12.34>") of the enhanced format are dropped.
            text = Regex.Replace(text, @"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", "").Trim();
            foreach (var t in stamps)
            {
                // A positive offset makes the lyrics come sooner.
                var time = t - TimeSpan.FromMilliseconds(offsetMs);
                result.Add(new LyricLine(time < TimeSpan.Zero ? TimeSpan.Zero : time, text));
            }
        }
        result.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }
}

/// <summary>
/// Synced lyrics from LRCLIB (lrclib.net, free, no key), cached on disk in the data folder.
/// Only the artist, title, album and duration of identified tracks are sent.
/// </summary>
public sealed class LyricsService
{
    private const string Api = "https://lrclib.net/api/";
    private const string UserAgent = "Shazim (https://github.com/painfullfeet/Shazim)";
    private static readonly TimeSpan RetryMissingAfter = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IAppHttpClientFactory _http;
    private readonly ILogger<LyricsService> _logger;
    private readonly string _cacheDir;
    private readonly Dictionary<string, TrackLyrics?> _memory = new();
    private readonly SemaphoreSlim _gate = new(2, 2);

    public LyricsService(IAppHttpClientFactory http, ILogger<LyricsService> logger)
        : this(http, logger, Path.Combine(AppPaths.DataDirectory, "lyrics")) { }

    public LyricsService(IAppHttpClientFactory http, ILogger<LyricsService> logger, string cacheDir)
    {
        _http = http;
        _logger = logger;
        _cacheDir = cacheDir;
    }

    /// <summary>Lyrics of the track, or null when LRCLIB has none (or is unreachable).</summary>
    public async Task<TrackLyrics?> GetAsync(Track track, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist)) return null;
        lock (_memory) if (_memory.TryGetValue(track.Id, out var known)) return known;

        var cacheFile = Path.Combine(_cacheDir, CacheName(track.Id));
        var cached = ReadCache(cacheFile);
        if (cached != null && (!cached.NotFound || DateTime.UtcNow - cached.FetchedAt < RetryMissingAfter))
            return Remember(track.Id, cached.ToLyrics());

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entry = await FetchAsync(track, ct).ConfigureAwait(false);
            if (entry == null) return null; // network trouble: try again next time
            WriteCache(cacheFile, entry);
            return Remember(track.Id, entry.ToLyrics());
        }
        finally
        {
            _gate.Release();
        }
    }

    private TrackLyrics? Remember(string id, TrackLyrics? lyrics)
    {
        if (lyrics is { IsEmpty: true, IsInstrumental: false }) lyrics = null;
        lock (_memory) _memory[id] = lyrics;
        return lyrics;
    }

    private async Task<CacheEntry?> FetchAsync(Track track, CancellationToken ct)
    {
        var client = _http.CreateClient();
        try
        {
            // 1. Exact match (needs the duration).
            if (track.DurationMs > 0)
            {
                var url = Api + "get?" + Query(("artist_name", track.Artist), ("track_name", track.Title),
                    ("album_name", track.Album), ("duration", Math.Round(track.DurationMs / 1000.0).ToString(CultureInfo.InvariantCulture)));
                var (status, body) = await GetAsync(client, url, ct).ConfigureAwait(false);
                if (status == 200 && Parse<LrclibItem>(body) is { } exact) return CacheEntry.From(exact);
                if (status is not (200 or 404)) return null;
            }

            // 2. Search, keep the best candidate (synced lyrics first, then the closest title).
            var searchUrl = Api + "search?" + Query(("track_name", track.Title), ("artist_name", track.Artist));
            var (code, json) = await GetAsync(client, searchUrl, ct).ConfigureAwait(false);
            if (code != 200) return code == 404 ? CacheEntry.Missing() : null;
            var items = Parse<List<LrclibItem>>(json) ?? new();
            var best = Pick(items, track);
            return best != null ? CacheEntry.From(best) : CacheEntry.Missing();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogInformation("Lyrics unavailable: {Message}", ex.Message);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Best search result for a track. Public for tests.</summary>
    public static LrclibItem? Pick(IEnumerable<LrclibItem> items, Track track)
    {
        string Fold(string? s) => Regex.Replace((s ?? "").ToLowerInvariant(), @"\(.*?\)|\[.*?\]|feat\..*$|[^\p{L}\p{N}]", "");
        var title = Fold(track.Title);
        var artist = Fold(track.Artist);
        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.SyncedLyrics) || !string.IsNullOrWhiteSpace(i.PlainLyrics) || i.Instrumental)
            .Select(i => (Item: i,
                Score: (Fold(i.TrackName) == title ? 4 : Fold(i.TrackName).Contains(title) || title.Contains(Fold(i.TrackName)) ? 2 : 0)
                     + (Fold(i.ArtistName) == artist ? 3 : Fold(i.ArtistName).Contains(artist) || artist.Contains(Fold(i.ArtistName)) ? 1 : 0)
                     + (!string.IsNullOrWhiteSpace(i.SyncedLyrics) ? 2 : 0)
                     - (track.DurationMs > 0 && i.Duration > 0 && Math.Abs(i.Duration - track.DurationMs / 1000.0) > 8 ? 2 : 0)))
            .Where(x => x.Score >= 4)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Item)
            .FirstOrDefault();
    }

    private static async Task<(int Status, string Body)> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
    }

    private static string Query(params (string Key, string? Value)[] parts) =>
        string.Join("&", parts.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value!)));

    private static T? Parse<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static string CacheName(string trackId) =>
        Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(trackId))) + ".json";

    private CacheEntry? ReadCache(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(file), Json) : null; }
        catch { return null; }
    }

    private void WriteCache(string file, CacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(entry));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Lyrics cache not written: {Message}", ex.Message);
        }
    }

    public sealed class LrclibItem
    {
        [JsonPropertyName("trackName")] public string? TrackName { get; set; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
        [JsonPropertyName("albumName")] public string? AlbumName { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
    }

    private sealed class CacheEntry
    {
        public string? Synced { get; set; }
        public string? Plain { get; set; }
        public bool Instrumental { get; set; }
        public double Duration { get; set; }
        public bool NotFound { get; set; }
        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

        public static CacheEntry From(LrclibItem i) => new()
        {
            Synced = i.SyncedLyrics, Plain = i.PlainLyrics, Instrumental = i.Instrumental, Duration = i.Duration
        };

        public static CacheEntry Missing() => new() { NotFound = true };

        public TrackLyrics? ToLyrics() => NotFound ? null : TrackLyrics.FromLrc(Synced, Plain, Instrumental, Duration);
    }
}

/// <summary>
/// Where the song is now, from the Shazam match: the sample that started at <see cref="SampleStartUtc"/>
/// was <see cref="OffsetSeconds"/> into the track.
/// </summary>
public readonly record struct PlaybackAnchor(string TrackId, double OffsetSeconds, DateTime SampleStartUtc)
{
    public TimeSpan PositionAt(DateTime utcNow, double nudgeSeconds = 0) =>
        TimeSpan.FromSeconds(Math.Max(0, OffsetSeconds + (utcNow - SampleStartUtc).TotalSeconds + nudgeSeconds));
}
