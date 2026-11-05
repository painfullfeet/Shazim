using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;
using Shazim.Core.Models;

namespace Shazim.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ITrackRepository"/>.
/// Connections are pooled by Microsoft.Data.Sqlite; the schema is created once.
/// </summary>
public class SqliteTrackRepository : ITrackRepository
{
    private const int SchemaVersion = 2;

    private readonly string _connectionString;
    private readonly ILogger<SqliteTrackRepository>? _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteTrackRepository(string connectionString, ILogger<SqliteTrackRepository>? logger = null)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = true };
        _connectionString = builder.ToString();
        _logger = logger;
    }

    /// <summary>Convenience constructor from a file path.</summary>
    public static SqliteTrackRepository ForFile(string path, ILogger<SqliteTrackRepository>? logger = null)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return new SqliteTrackRepository(new SqliteConnectionStringBuilder { DataSource = path }.ToString(), logger);
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        if (!_initialized) await InitializeAsync(conn).ConfigureAwait(false);
        return conn;
    }

    private async Task InitializeAsync(SqliteConnection conn)
    {
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            Execute(conn, @"
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS Tracks (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Artist TEXT NOT NULL,
                    Album TEXT NOT NULL DEFAULT '',
                    AlbumArtUrl TEXT NOT NULL DEFAULT '',
                    DurationMs INTEGER NOT NULL DEFAULT 0,
                    Source TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    ExternalLinksJson TEXT NOT NULL DEFAULT '[]'
                );
                CREATE TABLE IF NOT EXISTS Detections (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TrackId TEXT NOT NULL,
                    DetectedAt TEXT NOT NULL,
                    Confidence REAL NOT NULL DEFAULT 0,
                    AudioDevice TEXT NOT NULL DEFAULT '',
                    IsAuto INTEGER NOT NULL DEFAULT 0,
                    DurationMs INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS Favorites (
                    TrackId TEXT PRIMARY KEY,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_detections_track ON Detections(TrackId);
                CREATE INDEX IF NOT EXISTS idx_detections_date ON Detections(DetectedAt);
                CREATE INDEX IF NOT EXISTS idx_favorites_date ON Favorites(CreatedAt);");

            // v2: richer metadata columns.
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Tracks)";
                using var r = cmd.ExecuteReader();
                while (r.Read()) columns.Add(r.GetString(1));
            }
            foreach (var col in new[] { "Genre", "Label", "ReleaseYear", "PreviewUrl" })
            {
                if (!columns.Contains(col))
                    Execute(conn, $"ALTER TABLE Tracks ADD COLUMN {col} TEXT NOT NULL DEFAULT ''");
            }

            // Older versions did not enable foreign keys, which left orphan rows behind.
            Execute(conn, @"
                DELETE FROM Detections WHERE TrackId NOT IN (SELECT Id FROM Tracks);
                DELETE FROM Favorites WHERE TrackId NOT IN (SELECT Id FROM Tracks);");
            Execute(conn, $"PRAGMA user_version = {SchemaVersion}");

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private const string TrackColumns = @"
        t.Id, t.Title, t.Artist, t.Album, t.AlbumArtUrl, t.DurationMs, t.Source, t.CreatedAt, t.UpdatedAt,
        t.ExternalLinksJson, t.Genre, t.Label, t.ReleaseYear, t.PreviewUrl,
        (SELECT 1 FROM Favorites f WHERE f.TrackId = t.Id) AS IsFav,
        (SELECT COUNT(*) FROM Detections d WHERE d.TrackId = t.Id) AS DetCount,
        (SELECT MIN(d.DetectedAt) FROM Detections d WHERE d.TrackId = t.Id) AS FirstSeen,
        (SELECT MAX(d.DetectedAt) FROM Detections d WHERE d.TrackId = t.Id) AS LastSeen";

    // ===== Tracks =====

    public async Task<Track?> GetTrackByIdAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {TrackColumns} FROM Tracks t WHERE t.Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        return await reader.ReadAsync().ConfigureAwait(false) ? ReadTrack(reader) : null;
    }

    public Task<List<Track>> GetAllTracksAsync() =>
        QueryTracksAsync($"SELECT {TrackColumns} FROM Tracks t ORDER BY COALESCE(LastSeen, t.UpdatedAt) DESC");

    public Task<List<Track>> GetFavoritesAsync() =>
        QueryTracksAsync($@"SELECT {TrackColumns} FROM Tracks t
            INNER JOIN Favorites fav ON fav.TrackId = t.Id ORDER BY fav.CreatedAt DESC");

    private async Task<List<Track>> QueryTracksAsync(string sql)
    {
        var list = new List<Track>();
        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false)) list.Add(ReadTrack(reader));
        return list;
    }

    public async Task<int> GetTrackCountAsync() => await ScalarIntAsync("SELECT COUNT(*) FROM Tracks").ConfigureAwait(false);

    public async Task<string> UpsertTrackAsync(Track track)
    {
        if (string.IsNullOrEmpty(track.Id)) track.Id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        track.UpdatedAt = now;

        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Tracks (Id, Title, Artist, Album, AlbumArtUrl, DurationMs, Source, CreatedAt, UpdatedAt,
                                ExternalLinksJson, Genre, Label, ReleaseYear, PreviewUrl)
            VALUES (@id, @title, @artist, @album, @art, @dur, @source, @created, @updated, @links, @genre, @label, @year, @preview)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title, Artist = excluded.Artist,
                Album = CASE WHEN excluded.Album <> '' THEN excluded.Album ELSE Tracks.Album END,
                AlbumArtUrl = CASE WHEN excluded.AlbumArtUrl <> '' THEN excluded.AlbumArtUrl ELSE Tracks.AlbumArtUrl END,
                DurationMs = CASE WHEN excluded.DurationMs > 0 THEN excluded.DurationMs ELSE Tracks.DurationMs END,
                Source = excluded.Source, UpdatedAt = excluded.UpdatedAt, ExternalLinksJson = excluded.ExternalLinksJson,
                Genre = CASE WHEN excluded.Genre <> '' THEN excluded.Genre ELSE Tracks.Genre END,
                Label = CASE WHEN excluded.Label <> '' THEN excluded.Label ELSE Tracks.Label END,
                ReleaseYear = CASE WHEN excluded.ReleaseYear <> '' THEN excluded.ReleaseYear ELSE Tracks.ReleaseYear END,
                PreviewUrl = CASE WHEN excluded.PreviewUrl <> '' THEN excluded.PreviewUrl ELSE Tracks.PreviewUrl END;";
        cmd.Parameters.AddWithValue("@id", track.Id);
        cmd.Parameters.AddWithValue("@title", track.Title ?? "");
        cmd.Parameters.AddWithValue("@artist", track.Artist ?? "");
        cmd.Parameters.AddWithValue("@album", track.Album ?? "");
        cmd.Parameters.AddWithValue("@art", track.AlbumArtUrl ?? "");
        cmd.Parameters.AddWithValue("@dur", track.DurationMs);
        cmd.Parameters.AddWithValue("@source", track.Source ?? "");
        cmd.Parameters.AddWithValue("@created", (track.CreatedAt == default ? now : track.CreatedAt).ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@updated", now.ToString("O"));
        cmd.Parameters.AddWithValue("@links", JsonSerializer.Serialize(track.ExternalLinks ?? new()));
        cmd.Parameters.AddWithValue("@genre", track.Genre ?? "");
        cmd.Parameters.AddWithValue("@label", track.Label ?? "");
        cmd.Parameters.AddWithValue("@year", track.ReleaseYear ?? "");
        cmd.Parameters.AddWithValue("@preview", track.PreviewUrl ?? "");
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        _logger?.LogDebug("Upserted track {Id}", track.Id);
        return track.Id;
    }

    public async Task DeleteTrackAsync(string trackId)
    {
        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Tracks WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", trackId);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ===== Detections =====

    public async Task<int> AddDetectionAsync(Detection detection)
    {
        if (detection.DetectedAt == default) detection.DetectedAt = DateTime.UtcNow;

        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Detections (TrackId, DetectedAt, Confidence, AudioDevice, IsAuto, DurationMs)
            VALUES (@trackId, @at, @conf, @device, @auto, @dur);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@trackId", detection.TrackId);
        cmd.Parameters.AddWithValue("@at", detection.DetectedAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@conf", detection.Confidence);
        cmd.Parameters.AddWithValue("@device", detection.AudioDevice ?? "");
        cmd.Parameters.AddWithValue("@auto", detection.IsAuto ? 1 : 0);
        cmd.Parameters.AddWithValue("@dur", detection.DurationMs);
        detection.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
        return detection.Id;
    }

    public Task<List<Detection>> GetRecentDetectionsAsync(int count) => QueryDetectionsAsync(count, null);
    public Task<List<Detection>> GetAllDetectionsAsync() => QueryDetectionsAsync(null, null);
    public Task<List<Detection>> GetDetectionsForTrackAsync(string trackId) => QueryDetectionsAsync(null, trackId);

    private async Task<List<Detection>> QueryDetectionsAsync(int? limit, string? trackId)
    {
        var list = new List<Detection>();
        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT {TrackColumns}, d.Id, d.TrackId, d.DetectedAt, d.Confidence, d.AudioDevice, d.IsAuto, d.DurationMs
            FROM Detections d INNER JOIN Tracks t ON t.Id = d.TrackId
            {(trackId != null ? "WHERE d.TrackId = @track" : "")}
            ORDER BY d.DetectedAt DESC {(limit.HasValue ? "LIMIT @limit" : "")}";
        if (limit.HasValue) cmd.Parameters.AddWithValue("@limit", limit.Value);
        if (trackId != null) cmd.Parameters.AddWithValue("@track", trackId);

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var track = ReadTrack(reader);
            const int o = 18;
            list.Add(new Detection
            {
                Id = reader.GetInt32(o),
                TrackId = reader.GetString(o + 1),
                DetectedAt = ParseDate(reader.GetString(o + 2)),
                Confidence = reader.GetFloat(o + 3),
                AudioDevice = reader.GetString(o + 4),
                IsAuto = reader.GetInt32(o + 5) == 1,
                DurationMs = reader.GetInt64(o + 6),
                Track = track
            });
        }
        return list;
    }

    public async Task<int> GetDetectionCountAsync() => await ScalarIntAsync("SELECT COUNT(*) FROM Detections").ConfigureAwait(false);

    public async Task ClearDetectionsAsync()
    {
        await using var conn = await OpenAsync().ConfigureAwait(false);
        Execute(conn, "DELETE FROM Detections");
    }

    // ===== Favorites =====

    public async Task<bool> IsFavoriteAsync(string trackId)
    {
        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Favorites WHERE TrackId = @id";
        cmd.Parameters.AddWithValue("@id", trackId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false)) > 0;
    }

    public async Task SetFavoriteAsync(string trackId, bool favorite)
    {
        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = favorite
            ? "INSERT OR IGNORE INTO Favorites (TrackId, CreatedAt) SELECT Id, @at FROM Tracks WHERE Id = @id"
            : "DELETE FROM Favorites WHERE TrackId = @id";
        cmd.Parameters.AddWithValue("@id", trackId);
        cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // ===== Helpers =====

    private async Task<int> ScalarIntAsync(string sql)
    {
        await using var conn = await OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
    }

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? (d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime())
            : DateTime.UtcNow;

    private static Track ReadTrack(SqliteDataReader r)
    {
        var track = new Track
        {
            Id = r.GetString(0),
            Title = r.GetString(1),
            Artist = r.GetString(2),
            Album = r.GetString(3),
            AlbumArtUrl = r.GetString(4),
            DurationMs = r.GetInt64(5),
            Source = r.GetString(6),
            CreatedAt = ParseDate(r.GetString(7)),
            UpdatedAt = ParseDate(r.GetString(8)),
            Genre = r.GetString(10),
            Label = r.GetString(11),
            ReleaseYear = r.GetString(12),
            PreviewUrl = r.GetString(13),
            IsFavorite = !r.IsDBNull(14),
            DetectionCount = r.GetInt32(15),
            FirstDetectedAt = r.IsDBNull(16) ? null : ParseDate(r.GetString(16)),
            LastDetectedAt = r.IsDBNull(17) ? null : ParseDate(r.GetString(17)),
        };
        try
        {
            track.ExternalLinks = JsonSerializer.Deserialize<List<ExternalLink>>(r.GetString(9)) ?? new();
        }
        catch (JsonException)
        {
            track.ExternalLinks = new();
        }
        return track;
    }
}
