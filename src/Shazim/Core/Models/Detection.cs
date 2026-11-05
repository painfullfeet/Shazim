using System.Text.Json.Serialization;

namespace Shazim.Core.Models;

/// <summary>One identification event. Links to the Track that was found.</summary>
public class Detection
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("trackId")] public string TrackId { get; set; } = string.Empty;
    [JsonPropertyName("detectedAt")] public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("confidence")] public float Confidence { get; set; }
    [JsonPropertyName("audioDevice")] public string AudioDevice { get; set; } = string.Empty;
    [JsonPropertyName("isAuto")] public bool IsAuto { get; set; }
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }

    /// <summary>Joined track (filled by history queries).</summary>
    [JsonIgnore] public Track? Track { get; set; }

    [JsonIgnore] public DateTime LocalTime => DetectedAt.Kind == DateTimeKind.Local ? DetectedAt : DetectedAt.ToLocalTime();
    [JsonIgnore] public string FormattedTime => LocalTime.ToString("HH:mm");
    [JsonIgnore] public string ModeLabel => IsAuto ? "Auto" : "Manuel";

    [JsonIgnore]
    public string DayLabel
    {
        get
        {
            var day = LocalTime.Date;
            if (day == DateTime.Today) return "Aujourd'hui";
            if (day == DateTime.Today.AddDays(-1)) return "Hier";
            return day.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        }
    }
}
