using System.Text;
using Shazim.Core.Logging;

namespace Shazim.Core.Services;

/// <summary>
/// Prevents the same track from being recorded repeatedly within a time window
/// (Auto Shazim would otherwise log a song every cycle while it plays).
/// </summary>
public class DeduplicationService
{
    private readonly ILogger<DeduplicationService> _logger;
    private readonly Dictionary<string, DateTime> _lastSeen = new();
    private readonly object _lock = new();
    private int _windowMinutes = 15;

    public DeduplicationService(ILogger<DeduplicationService> logger) => _logger = logger;

    public int WindowMinutes => _windowMinutes;

    /// <summary>
    /// Returns true if the track was already seen within the window.
    /// Either way, the track is marked as seen now.
    /// </summary>
    public bool IsDuplicate(string trackId)
    {
        if (string.IsNullOrEmpty(trackId)) return false;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            bool duplicate = _lastSeen.TryGetValue(trackId, out var last) && (now - last).TotalMinutes < _windowMinutes;
            _lastSeen[trackId] = now;

            // Keep the dictionary small during long sessions.
            if (_lastSeen.Count > 500)
            {
                foreach (var key in _lastSeen.Where(kv => (now - kv.Value).TotalMinutes >= _windowMinutes).Select(kv => kv.Key).ToList())
                    _lastSeen.Remove(key);
            }

            if (duplicate) _logger.LogDebug("Duplicate detection: {TrackId}", trackId);
            return duplicate;
        }
    }

    /// <summary>Stable key from artist + title: case, accents, punctuation and spacing ignored.</summary>
    public static string NormalizeKey(string artist, string title)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
            return string.Empty;
        return $"{Normalize(artist)}|{Normalize(title)}";
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool lastWasSpace = true;
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(c) && !lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }
        return sb.ToString().Trim();
    }

    public void SetWindow(int minutes)
    {
        _windowMinutes = Math.Clamp(minutes, 1, 1440);
    }

    public void Clear()
    {
        lock (_lock) _lastSeen.Clear();
    }
}
