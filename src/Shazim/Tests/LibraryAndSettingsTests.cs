using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shazim.App;
using Shazim.App.Services;
using Shazim.Core.Models;
using Shazim.Core.Services;
using Shazim.Data.Repositories;
using Xunit;

namespace Shazim.Tests;

public class LibraryServiceTests
{
    [Fact]
    public async Task Upsert_KeepsOneRowAndRefreshesMetadata()
    {
        var (lib, _, _) = TestDb.Create();
        await lib.AddTrackAsync(TestDb.Track("t1"));
        var updated = TestDb.Track("t1");
        updated.AlbumArtUrl = "https://art";
        updated.Album = "";
        await lib.AddTrackAsync(updated);

        var all = await lib.GetAllAsync();
        var t = Assert.Single(all);
        Assert.Equal("https://art", t.AlbumArtUrl);
        Assert.Equal("Album", t.Album); // empty values never erase known metadata
    }

    [Fact]
    public async Task Search_IgnoresCaseAndAccents()
    {
        var (lib, _, _) = TestDb.Create();
        await lib.AddTrackAsync(TestDb.Track("1", "Déjà vu", "Beyoncé"));
        await lib.AddTrackAsync(TestDb.Track("2", "Other", "Someone"));

        Assert.Single(await lib.SearchAsync("deja"));
        Assert.Single(await lib.SearchAsync("BEYONCE"));
        Assert.Equal(2, (await lib.SearchAsync("  ")).Count);
        Assert.Empty(await lib.SearchAsync("zzz"));
    }

    [Fact]
    public async Task Sort_ByArtistAndTitle()
    {
        var (lib, _, _) = TestDb.Create();
        await lib.AddTrackAsync(TestDb.Track("1", "b", "Zed"));
        await lib.AddTrackAsync(TestDb.Track("2", "a", "Amy"));
        Assert.Equal("Amy", (await lib.SearchAsync(null, LibrarySort.Artist))[0].Artist);
        Assert.Equal("a", (await lib.SearchAsync(null, LibrarySort.Title))[0].Title);
    }

    [Fact]
    public async Task Favorites_ToggleAndFlag()
    {
        var (lib, _, _) = TestDb.Create();
        await lib.AddTrackAsync(TestDb.Track("f"));

        Assert.True(await lib.ToggleFavoriteAsync("f"));
        Assert.True((await lib.GetTrackAsync("f"))!.IsFavorite);
        Assert.Single(await lib.GetFavoritesAsync());

        Assert.False(await lib.ToggleFavoriteAsync("f"));
        Assert.Empty(await lib.GetFavoritesAsync());
    }

    [Fact]
    public async Task History_JoinsTrackAndCounts()
    {
        var (lib, _, _) = TestDb.Create();
        await lib.AddTrackAsync(TestDb.Track("h", "Song"));
        await lib.RecordDetectionAsync("h", 1, "Speakers", isAuto: true);
        await lib.RecordDetectionAsync("h", 1, "Speakers", isAuto: false);

        var history = await lib.GetHistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("Song", history[0].Track!.Title);
        Assert.Equal(DateTimeKind.Utc, history[0].DetectedAt.Kind);
        Assert.Equal(2, (await lib.GetTrackAsync("h"))!.DetectionCount);

        var t = (await lib.GetTrackAsync("h"))!;
        Assert.NotNull(t.FirstDetectedAt);
        Assert.True(t.LastDetectedAt >= t.FirstDetectedAt);
        Assert.Equal(2, (await lib.GetTrackHistoryAsync("h")).Count);
        Assert.Equal(2, (await lib.SearchAsync(null, LibrarySort.MostPlayed))[0].DetectionCount);

        await lib.ClearHistoryAsync();
        Assert.Equal(0, await lib.GetDetectionCountAsync());
        Assert.Equal(1, await lib.GetTrackCountAsync());
    }

    [Fact]
    public async Task Delete_CascadesDetectionsAndFavorites()
    {
        var (lib, _, path) = TestDb.Create();
        await lib.AddTrackAsync(TestDb.Track("d"));
        await lib.RecordDetectionAsync("d", 1, "", false);
        await lib.SetFavoriteAsync("d", true);

        await lib.DeleteTrackAsync("d");

        Assert.Equal(0, await lib.GetDetectionCountAsync());
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Favorites";
        Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public async Task Changed_IsRaisedOnWrites()
    {
        var (lib, _, _) = TestDb.Create();
        int count = 0;
        lib.Changed += (_, _) => count++;
        await lib.AddTrackAsync(TestDb.Track("c"));
        await lib.RecordDetectionAsync("c", 1, "", false);
        await lib.ToggleFavoriteAsync("c");
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Export_WritesCsvAndJson()
    {
        var (lib, _, _) = TestDb.Create();
        await lib.AddTrackAsync(TestDb.Track("e", "Ti\"tle", "Art;ist"));
        var dir = Path.Combine(Path.GetTempPath(), "shazamdesktop_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var csv = await lib.ExportAsync(Path.Combine(dir, "lib.csv"));
        var lines = File.ReadAllLines(csv);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"Ti\"\"tle\"", lines[1]);

        var json = await lib.ExportAsync(Path.Combine(dir, "lib.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(json));
        Assert.Equal(1, doc.RootElement.GetProperty("tracks").GetArrayLength());
    }

    [Fact]
    public async Task LegacyDatabase_IsMigrated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shazamdesktop_legacy_{Guid.NewGuid():N}.db");
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE Tracks (Id TEXT PRIMARY KEY, Title TEXT NOT NULL, Artist TEXT NOT NULL, Album TEXT NOT NULL DEFAULT '',
                    AlbumArtUrl TEXT NOT NULL DEFAULT '', DurationMs INTEGER NOT NULL DEFAULT 0, Source TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, ExternalLinksJson TEXT NOT NULL DEFAULT '[]');
                CREATE TABLE Detections (Id INTEGER PRIMARY KEY AUTOINCREMENT, TrackId TEXT NOT NULL, DetectedAt TEXT NOT NULL,
                    Confidence REAL NOT NULL DEFAULT 0, AudioDevice TEXT NOT NULL DEFAULT '', IsAuto INTEGER NOT NULL DEFAULT 0,
                    DurationMs INTEGER NOT NULL DEFAULT 0);
                INSERT INTO Tracks VALUES ('old', 'Old song', 'Old band', '', '', 0, 'AcoustID', '2025-01-01T10:00:00.0000000Z', '2025-01-01T10:00:00.0000000Z', '[]');
                INSERT INTO Detections (TrackId, DetectedAt) VALUES ('old', '2025-01-01T10:00:00.0000000Z');
                INSERT INTO Detections (TrackId, DetectedAt) VALUES ('orphan', '2025-01-01T10:00:00.0000000Z');";
            cmd.ExecuteNonQuery();
        }

        var lib = new LibraryService(SqliteTrackRepository.ForFile(path), new NullLogger<LibraryService>());
        var t = Assert.Single(await lib.GetAllAsync());
        Assert.Equal("Old song", t.Title);
        Assert.Equal("", t.Genre);
        Assert.Equal(1, await lib.GetDetectionCountAsync()); // orphan removed
    }
}

public class DeduplicationServiceTests
{
    [Fact]
    public void SameTrackWithinWindow_IsDuplicate()
    {
        var d = new DeduplicationService(new NullLogger<DeduplicationService>());
        Assert.False(d.IsDuplicate("a"));
        Assert.True(d.IsDuplicate("a"));
        Assert.False(d.IsDuplicate("b"));
        Assert.False(d.IsDuplicate(""));
    }

    [Fact]
    public void Clear_ForgetsTracks()
    {
        var d = new DeduplicationService(new NullLogger<DeduplicationService>());
        d.IsDuplicate("a");
        d.Clear();
        Assert.False(d.IsDuplicate("a"));
    }

    [Theory]
    [InlineData("Beyoncé", "Halo (Live)", "beyonce|halo live")]
    [InlineData("  AC/DC ", "T.N.T.", "acdc|tnt")]
    [InlineData("", "", "")]
    public void NormalizeKey_IsStable(string artist, string title, string expected)
    {
        Assert.Equal(expected, DeduplicationService.NormalizeKey(artist, title));
    }

    [Fact]
    public void SetWindow_IsClamped()
    {
        var d = new DeduplicationService(new NullLogger<DeduplicationService>());
        d.SetWindow(0);
        Assert.Equal(1, d.WindowMinutes);
        d.SetWindow(99999);
        Assert.Equal(1440, d.WindowMinutes);
    }
}

public class SettingsServiceTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "ShazimTest", $"settings_{Guid.NewGuid():N}.json");

    [Fact]
    public void Defaults_AreSensible()
    {
        var s = new SettingsService(TempPath()).GetSettings();
        Assert.Equal(AudioSource.Pc, s.Audio.Source);
        Assert.Equal(new[] { "default" }, s.Audio.DeviceIds());
        Assert.Equal(12000, s.Audio.CaptureDurationMs);
        Assert.Equal("rouge", s.General.Accent);
        Assert.False(s.AutoShazam.Enabled);
        Assert.True(s.Notifications.Enabled);
        Assert.True(s.General.CloseToTray);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var path = TempPath();
        var svc = new SettingsService(path);
        svc.GetSettings().Audio.Source = AudioSource.Both;
        svc.GetSettings().Audio.MicDeviceId = "in:headset";
        svc.GetSettings().General.Accent = "bleu";
        svc.GetSettings().AutoShazam.IntervalMs = 60000;
        svc.GetSettings().Hotkeys.Identify = "Ctrl+Alt+I";
        svc.Save();

        var reloaded = new SettingsService(path).GetSettings();
        Assert.Equal(new[] { "default", "in:headset" }, reloaded.Audio.DeviceIds());
        Assert.Equal("bleu", reloaded.General.Accent);
        Assert.Equal(60000, reloaded.AutoShazam.IntervalMs);
        Assert.Equal("Ctrl+Alt+I", reloaded.Hotkeys.Identify);
    }

    [Fact]
    public void LegacyFile_IsMigratedAndKeysAreErased()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        { "audio": { "deviceId": "{0.0.0.00000000}.{abc}", "captureDurationMs": 8000, "sampleRate": 44100 },
          "autoShazam": { "enabled": true, "intervalMs": 30000, "captureDurationMs": 8000, "minSignalDb": -40, "duplicateWindowMinutes": 15 },
          "recognition": { "provider": "AcoustID", "acoustIDApiKey": "secret-acoustid", "audDApiKey": "secret-audd", "useFallback": true } }
        """);

        var s = new SettingsService(path).GetSettings();
        Assert.Equal(AudioSource.Pc, s.Audio.Source);
        Assert.Equal("out:{0.0.0.00000000}.{abc}", s.Audio.OutputDeviceId);
        Assert.Equal(12000, s.Audio.CaptureDurationMs);
        Assert.False(s.AutoShazam.Enabled);
        Assert.Equal(AppSettings.CurrentVersion, s.Version);

        // The file is rewritten straight away: no key, no other service, no old device field.
        var onDisk = File.ReadAllText(path);
        Assert.DoesNotContain("secret", onDisk);
        Assert.DoesNotContain("recognition", onDisk, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"deviceId\"", onDisk);
    }

    [Fact]
    public void LegacyMicrophoneDevice_BecomesMicSource()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "version": 3, "audio": { "deviceId": "in:abc" } }""");
        var s = new SettingsService(path).GetSettings();
        Assert.Equal(AudioSource.Mic, s.Audio.Source);
        Assert.Equal(new[] { "in:abc" }, s.Audio.DeviceIds());
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        var s = new SettingsService(path).GetSettings();
        Assert.Equal(AudioSource.Pc, s.Audio.Source);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void OutOfRangeValues_AreClamped()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "version": 4, "autoShazam": { "intervalMs": 1, "duplicateWindowMinutes": 0 }, "audio": { "source": "nope", "outputDeviceId": "" } }""");
        var s = new SettingsService(path).GetSettings();
        Assert.Equal(5000, s.AutoShazam.IntervalMs);
        Assert.Equal(1, s.AutoShazam.DuplicateWindowMinutes);
        Assert.Equal(AudioSource.Pc, s.Audio.Source);
        Assert.Equal("default", s.Audio.OutputDeviceId);
    }
}

public class HotkeyParsingTests
{
    [Theory]
    [InlineData("Ctrl+Shift+S", true)]
    [InlineData("Alt+F9", true)]
    [InlineData("Ctrl+1", true)]
    [InlineData("S", false)]
    [InlineData("Ctrl+Shift", false)]
    [InlineData("Ctrl+A+B", false)]
    [InlineData("", false)]
    public void TryParse(string gesture, bool valid)
    {
        Assert.Equal(valid, HotkeyService.TryParse(gesture, out _, out _));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var text = HotkeyService.FormatGesture(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.D5);
        Assert.Equal("Ctrl+Shift+5", text);
        Assert.True(HotkeyService.TryParse(text, out _, out var key));
        Assert.Equal(System.Windows.Input.Key.D5, key);
        Assert.Null(HotkeyService.FormatGesture(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.LeftShift));
    }
}

public class FileLoggerTests
{
    [Fact]
    public void Format_ReplacesNamedPlaceholdersInOrder()
    {
        Assert.Equal("a=1 b=x", FileLogger<object>.Format("a={A} b={B}", new object[] { 1, "x" }));
        Assert.Equal("v=1.50", FileLogger<object>.Format("v={V:F2}", new object[] { 1.5 }));
        Assert.Equal("only {One}", FileLogger<object>.Format("only {One}", Array.Empty<object>()));
    }
}

public class ModelTests
{
    [Fact]
    public void Track_MetaLineSkipsEmptyParts()
    {
        var t = new Track { Album = "LP", Genre = "Pop" };
        Assert.Equal("LP  ·  Pop", t.MetaLine);
        Assert.Equal("Titre inconnu", t.DisplayTitle);
    }

    [Fact]
    public void Detection_DayLabel()
    {
        Assert.Equal("Aujourd'hui", new Detection { DetectedAt = DateTime.UtcNow }.DayLabel);
        Assert.Equal("Hier", new Detection { DetectedAt = DateTime.Now.AddDays(-1) }.DayLabel);
    }
}
