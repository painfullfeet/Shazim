using Shazim.Core.Models;

namespace Shazim.Core.Interfaces;

/// <summary>
/// Persistent storage for the local library (tracks, detections, favorites).
/// </summary>
public interface ITrackRepository
{
    Task<Track?> GetTrackByIdAsync(string id);

    /// <summary>All tracks, most recently detected first, with favorite flag and detection count.</summary>
    Task<List<Track>> GetAllTracksAsync();
    Task<List<Track>> GetFavoritesAsync();
    Task<int> GetTrackCountAsync();

    /// <summary>Inserts the track, or refreshes its metadata if it already exists.</summary>
    Task<string> UpsertTrackAsync(Track track);
    Task DeleteTrackAsync(string trackId);

    Task<int> AddDetectionAsync(Detection detection);

    /// <summary>Most recent detections with their track joined.</summary>
    Task<List<Detection>> GetRecentDetectionsAsync(int count);
    Task<List<Detection>> GetAllDetectionsAsync();
    Task<List<Detection>> GetDetectionsForTrackAsync(string trackId);
    Task<int> GetDetectionCountAsync();
    Task ClearDetectionsAsync();

    Task<bool> IsFavoriteAsync(string trackId);
    Task SetFavoriteAsync(string trackId, bool favorite);
}
