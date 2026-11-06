using Shazim.Core.Models;

namespace Shazim.Core.Services;

public sealed record DayCount(DateTime Day, int Count);
public sealed record NamedCount(string Name, int Count);
public sealed record TrackCount(Track Track, int Count);

/// <summary>Numbers for the statistics page, computed from the listening history.</summary>
public sealed class LibraryStats
{
    public int TrackCount { get; init; }
    public int PlayCount { get; init; }
    public int FavoriteCount { get; init; }
    public int PlaysThisMonth { get; init; }

    /// <summary>One entry per day, oldest first, the last one being today (local time).</summary>
    public IReadOnlyList<DayCount> LastDays { get; init; } = Array.Empty<DayCount>();
    public IReadOnlyList<NamedCount> TopArtists { get; init; } = Array.Empty<NamedCount>();
    public IReadOnlyList<NamedCount> TopGenres { get; init; } = Array.Empty<NamedCount>();

    public TrackCount? MostShazimed { get; init; }

    /// <summary>Track heard most often this calendar month.</summary>
    public TrackCount? TrackOfTheMonth { get; init; }
    public Detection? FirstEver { get; init; }

    public int PcCount { get; init; }
    public int MicCount { get; init; }
    public int AutoCount { get; init; }
    public int ManualCount => PlayCount - AutoCount;

    public bool IsEmpty => PlayCount == 0 && TrackCount == 0;

    /// <param name="now">Local time "now" (a parameter, for tests).</param>
    public static LibraryStats Compute(IReadOnlyList<Detection> detections, IReadOnlyList<Track> tracks, DateTime now, int days = 30)
    {
        var today = now.Date;
        DateTime Local(Detection d) => d.DetectedAt.Kind == DateTimeKind.Local ? d.DetectedAt : d.DetectedAt.ToLocalTime();

        var perDay = detections.GroupBy(d => Local(d).Date).ToDictionary(g => g.Key, g => g.Count());
        var lastDays = Enumerable.Range(0, days)
            .Select(i => today.AddDays(i - days + 1))
            .Select(day => new DayCount(day, perDay.GetValueOrDefault(day)))
            .ToList();

        var byId = tracks.ToDictionary(t => t.Id);
        Track TrackOf(Detection d) => byId.TryGetValue(d.TrackId, out var t) ? t : d.Track ?? new Track { Id = d.TrackId };

        static List<NamedCount> Top(IEnumerable<string> names, int n) => names
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => s.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new NamedCount(g.First().Trim(), g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(n).ToList();

        TrackCount? MostOf(IEnumerable<Detection> ds) => ds
            .GroupBy(d => d.TrackId)
            .Select(g => (Track: TrackOf(g.First()), Count: g.Count(), Last: g.Max(Local)))
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.Last)
            .Select(x => new TrackCount(x.Track, x.Count))
            .FirstOrDefault();

        var month = detections.Where(d => Local(d).Year == now.Year && Local(d).Month == now.Month).ToList();
        int mic = detections.Count(d => SourceKinds.Of(d) == SourceKinds.Mic);

        return new LibraryStats
        {
            TrackCount = tracks.Count,
            PlayCount = detections.Count,
            FavoriteCount = tracks.Count(t => t.IsFavorite),
            PlaysThisMonth = month.Count,
            LastDays = lastDays,
            // An artist counts once per detection: the artists heard the most.
            TopArtists = Top(detections.Select(d => TrackOf(d).Artist), 5),
            TopGenres = Top(detections.Select(d => TrackOf(d).Genre), 5),
            MostShazimed = MostOf(detections),
            TrackOfTheMonth = MostOf(month),
            FirstEver = detections.OrderBy(d => d.DetectedAt).FirstOrDefault() is { } first
                ? new Detection { Id = first.Id, TrackId = first.TrackId, DetectedAt = first.DetectedAt, IsAuto = first.IsAuto, AudioDevice = first.AudioDevice, Track = TrackOf(first) }
                : null,
            MicCount = mic,
            PcCount = detections.Count - mic,
            AutoCount = detections.Count(d => d.IsAuto),
        };
    }
}

/// <summary>Where a detection was heard: what the PC plays, or the microphone.</summary>
public static class SourceKinds
{
    public const string Pc = "pc";
    public const string Mic = "mic";

    /// <summary>From a device id ("in:..." is a microphone).</summary>
    public static string FromDeviceId(string? id) => id != null && id.StartsWith("in:", StringComparison.Ordinal) ? Mic : Pc;

    /// <summary>Stored kind, or a guess from the device name for detections saved before it was stored.</summary>
    public static string Of(Detection d)
    {
        if (d.SourceKind is Pc or Mic) return d.SourceKind;
        var name = d.AudioDevice ?? "";
        return name.Contains("micro", StringComparison.OrdinalIgnoreCase) || name.Contains("mic ", StringComparison.OrdinalIgnoreCase)
               || name.Contains("mikro", StringComparison.OrdinalIgnoreCase) || name.Contains("micrófono", StringComparison.OrdinalIgnoreCase)
            ? Mic : Pc;
    }
}
