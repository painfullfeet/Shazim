using System.Text;
using System.Text.Json;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;
using Shazim.Core.Models;

namespace Shazim.Core.Services;

public enum LibrarySort { Recent, Artist, Title, MostPlayed }

/// <summary>
/// The local music library: tracks, detections history, favorites, export.
/// Works fully offline once tracks are identified.
/// </summary>
public class LibraryService
{
    private readonly ITrackRepository _repository;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(ITrackRepository repository, ILogger<LibraryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Raised after any change (add, delete, favorite, clear).</summary>
    public event EventHandler? Changed;

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public async Task<string> AddTrackAsync(Track track)
    {
        var id = await _repository.UpsertTrackAsync(track).ConfigureAwait(false);
        _logger.LogInformation("Track saved: {Artist} - {Title}", track.Artist, track.Title);
        OnChanged();
        return id;
    }

    public Task<Track?> GetTrackAsync(string id) => _repository.GetTrackByIdAsync(id);

    public async Task<int> RecordDetectionAsync(string trackId, float confidence, string audioDevice, bool isAuto)
    {
        var id = await _repository.AddDetectionAsync(new Detection
        {
            TrackId = trackId,
            Confidence = confidence,
            AudioDevice = audioDevice,
            IsAuto = isAuto,
            DetectedAt = DateTime.UtcNow
        }).ConfigureAwait(false);
        OnChanged();
        return id;
    }

    public Task<List<Track>> GetAllAsync() => _repository.GetAllTracksAsync();

    /// <summary>Searches title, artist and album (accent and case insensitive), then sorts.</summary>
    public async Task<List<Track>> SearchAsync(string? query, LibrarySort sort = LibrarySort.Recent)
    {
        var tracks = await _repository.GetAllTracksAsync().ConfigureAwait(false);
        IEnumerable<Track> result = tracks;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = Fold(query);
            result = result.Where(t => Fold(t.Title).Contains(q) || Fold(t.Artist).Contains(q) || Fold(t.Album).Contains(q));
        }

        result = sort switch
        {
            LibrarySort.Artist => result.OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.Title => result.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.MostPlayed => result.OrderByDescending(t => t.DetectionCount).ThenByDescending(t => t.LastDetectedAt),
            _ => result
        };
        return result.ToList();
    }

    private static string Fold(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    public Task<List<Detection>> GetHistoryAsync(int count = 500) => _repository.GetRecentDetectionsAsync(count);

    /// <summary>Every time a track was identified, newest first.</summary>
    public Task<List<Detection>> GetTrackHistoryAsync(string trackId) => _repository.GetDetectionsForTrackAsync(trackId);

    public async Task ClearHistoryAsync()
    {
        await _repository.ClearDetectionsAsync().ConfigureAwait(false);
        OnChanged();
    }

    public Task<List<Track>> GetFavoritesAsync() => _repository.GetFavoritesAsync();

    /// <summary>Toggles the favorite flag and returns the new state.</summary>
    public async Task<bool> ToggleFavoriteAsync(string trackId)
    {
        bool newState = !await _repository.IsFavoriteAsync(trackId).ConfigureAwait(false);
        await _repository.SetFavoriteAsync(trackId, newState).ConfigureAwait(false);
        OnChanged();
        return newState;
    }

    public async Task SetFavoriteAsync(string trackId, bool favorite)
    {
        await _repository.SetFavoriteAsync(trackId, favorite).ConfigureAwait(false);
        OnChanged();
    }

    public async Task DeleteTrackAsync(string trackId)
    {
        await _repository.DeleteTrackAsync(trackId).ConfigureAwait(false);
        _logger.LogInformation("Track deleted: {TrackId}", trackId);
        OnChanged();
    }

    public Task<int> GetTrackCountAsync() => _repository.GetTrackCountAsync();
    public Task<int> GetDetectionCountAsync() => _repository.GetDetectionCountAsync();

    /// <summary>Exports the library to JSON, or to CSV when the path ends with .csv.</summary>
    public async Task<string> ExportAsync(string outputPath)
    {
        var tracks = await _repository.GetAllTracksAsync().ConfigureAwait(false);

        if (outputPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine(CoreText.L("core.csv_header", "Titre;Artiste;Album;Année;Genre;Favori;Détections;Lien"));
            foreach (var t in tracks)
            {
                string Cell(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
                sb.AppendLine(string.Join(";", Cell(t.Title), Cell(t.Artist), Cell(t.Album), Cell(t.ReleaseYear), Cell(t.Genre),
                    t.IsFavorite ? CoreText.L("core.yes", "oui") : CoreText.L("core.no", "non"), t.DetectionCount.ToString(), Cell(t.FindLink("Shazam") ?? t.FindLink("Spotify") ?? "")));
            }
            await File.WriteAllTextAsync(outputPath, sb.ToString(), new UTF8Encoding(true)).ConfigureAwait(false);
        }
        else
        {
            var detections = await _repository.GetAllDetectionsAsync().ConfigureAwait(false);
            var export = new
            {
                exportDate = DateTime.UtcNow.ToString("O"),
                tracks,
                detections,
                favoriteTrackIds = tracks.Where(t => t.IsFavorite).Select(t => t.Id).ToList()
            };
            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, json).ConfigureAwait(false);
        }

        _logger.LogInformation("Library exported to {Path}", outputPath);
        return outputPath;
    }
}
