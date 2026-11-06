using System.Net;
using Shazim.Core.Lyrics;
using Shazim.Core.Models;
using Shazim.Core.Recognition;
using Shazim.Core.Services;
using Xunit;

namespace Shazim.Tests;

public class LibraryStatsTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Local);

    private static Detection D(string track, DateTime local, bool auto = false, string kind = "pc", string device = "Haut-parleurs") => new()
    {
        TrackId = track, DetectedAt = local.ToUniversalTime(), IsAuto = auto, SourceKind = kind, AudioDevice = device
    };

    private static Track T(string id, string artist, string genre, bool fav = false) =>
        new() { Id = id, Title = "Titre " + id, Artist = artist, Genre = genre, IsFavorite = fav };

    [Fact]
    public void Compute_CountsEverything()
    {
        var tracks = new List<Track> { T("a", "Daft Punk", "Électro", fav: true), T("b", "Stromae", "Pop"), T("c", "daft punk", "Électro") };
        var detections = new List<Detection>
        {
            D("a", Now.AddDays(-40)),                     // first ever, last month
            D("b", Now.AddDays(-3), auto: true),
            D("b", Now.AddDays(-2), auto: true, kind: "mic", device: "Micro"),
            D("b", Now.AddHours(-1)),
            D("c", Now.AddHours(-2), kind: "", device: "Microphone (USB)"), // older row: guessed from the name
        };

        var s = LibraryStats.Compute(detections, tracks, Now);

        Assert.Equal(3, s.TrackCount);
        Assert.Equal(5, s.PlayCount);
        Assert.Equal(1, s.FavoriteCount);
        Assert.Equal(4, s.PlaysThisMonth);
        Assert.Equal(30, s.LastDays.Count);
        Assert.Equal(Now.Date, s.LastDays[^1].Day);
        Assert.Equal(2, s.LastDays[^1].Count);
        Assert.Equal(4, s.LastDays.Sum(d => d.Count)); // the 40-day-old one is outside
        Assert.Equal("b", s.MostShazimed!.Track.Id);
        Assert.Equal(3, s.MostShazimed.Count);
        Assert.Equal("b", s.TrackOfTheMonth!.Track.Id);
        Assert.Equal("a", s.FirstEver!.TrackId);
        Assert.Equal("Stromae", s.TopArtists[0].Name);
        Assert.Equal(2, s.TopArtists[1].Count); // "Daft Punk" and "daft punk" are one artist
        Assert.Equal("Pop", s.TopGenres[0].Name);
        Assert.Equal(2, s.MicCount);
        Assert.Equal(3, s.PcCount);
        Assert.Equal(2, s.AutoCount);
        Assert.Equal(3, s.ManualCount);
    }

    [Fact]
    public void Compute_EmptyLibrary()
    {
        var s = LibraryStats.Compute(new List<Detection>(), new List<Track>(), Now);
        Assert.True(s.IsEmpty);
        Assert.Null(s.MostShazimed);
        Assert.Null(s.FirstEver);
        Assert.All(s.LastDays, d => Assert.Equal(0, d.Count));
    }

    [Fact]
    public async Task Repository_StoresTheSourceKind()
    {
        var (library, _, path) = TestDb.Create();
        try
        {
            await library.AddTrackAsync(TestDb.Track("t1"));
            await library.RecordDetectionAsync("t1", 1, "Micro", isAuto: false, SourceKinds.Mic);
            await library.RecordDetectionAsync("t1", 1, "PC", isAuto: true, SourceKinds.Pc);
            var history = await library.GetTrackHistoryAsync("t1");
            Assert.Equal(new[] { "pc", "mic" }, history.Select(h => h.SourceKind));
            var stats = await library.GetStatsAsync(DateTime.Now);
            Assert.Equal(1, stats.MicCount);
            Assert.Equal(1, stats.AutoCount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("in:default", "mic")]
    [InlineData("in:{abc}", "mic")]
    [InlineData("default", "pc")]
    [InlineData("out:{abc}", "pc")]
    public void SourceKind_FromDeviceId(string id, string kind) => Assert.Equal(kind, SourceKinds.FromDeviceId(id));
}

public class LyricsTests
{
    [Fact]
    public void Lrc_ParsesStampsOffsetsAndRepeats()
    {
        var lrc = "[ar:Artiste]\n[offset:+500]\n[00:01.50]Bonjour\n[00:03.00][00:10.25]Refrain\n[00:05.123]<00:05.20>Mot <00:05.60>à mot\n\n[01:02]Fin";
        var lines = LrcParser.Parse(lrc);
        Assert.Equal(5, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(1.0), lines[0].Time);   // 1.5 s - 0.5 s offset
        Assert.Equal("Bonjour", lines[0].Text);
        Assert.Equal("Refrain", lines[1].Text);
        Assert.Equal("Mot à mot", lines[2].Text);
        Assert.Equal(TimeSpan.FromSeconds(4.623), lines[2].Time);
        Assert.Equal("Refrain", lines[3].Text);
        Assert.Equal(TimeSpan.FromSeconds(61.5), lines[4].Time);
    }

    [Fact]
    public void IndexAt_FindsTheLineBeingSung()
    {
        var l = TrackLyrics.FromLrc("[00:02.00]Un\n[00:04.00]Deux\n[00:08.00]Trois", null);
        Assert.True(l.IsSynced);
        Assert.Equal(-1, l.IndexAt(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, l.IndexAt(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, l.IndexAt(TimeSpan.FromSeconds(7.9)));
        Assert.Equal(2, l.IndexAt(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void PlainLyrics_AreStatic()
    {
        var l = TrackLyrics.FromLrc(null, "Ligne 1\r\nLigne 2\n\n");
        Assert.False(l.IsSynced);
        Assert.Equal(2, l.Lines.Count);
        Assert.Equal(-1, l.IndexAt(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Anchor_GivesThePositionInTheSong()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var a = new PlaybackAnchor("t", 42.5, start);
        Assert.Equal(TimeSpan.FromSeconds(42.5), a.PositionAt(start));
        Assert.Equal(TimeSpan.FromSeconds(52.5), a.PositionAt(start.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(53), a.PositionAt(start.AddSeconds(10), nudgeSeconds: 0.5));
    }

    [Fact]
    public void Shazam_OffsetIsRead()
    {
        var json = """{ "matches": [{ "id": "1", "offset": 83.25 }], "track": { "key": "9", "title": "T", "subtitle": "A" } }""";
        Assert.Equal(83.25, ShazamRecognitionService.Parse(json).MatchOffsetSeconds);
        var noOffset = """{ "matches": [{ "id": "1" }], "track": { "key": "9", "title": "T", "subtitle": "A" } }""";
        Assert.Null(ShazamRecognitionService.Parse(noOffset).MatchOffsetSeconds);
    }

    [Fact]
    public void Pick_PrefersSyncedLyricsOfTheSameSong()
    {
        var track = new Track { Id = "x", Title = "Alors on danse", Artist = "Stromae", DurationMs = 206_000 };
        var items = new[]
        {
            new LyricsService.LrclibItem { TrackName = "Alors on danse (Remix)", ArtistName = "Someone", SyncedLyrics = "[00:01.00]x", Duration = 300 },
            new LyricsService.LrclibItem { TrackName = "Alors on danse", ArtistName = "Stromae", PlainLyrics = "plain", Duration = 206 },
            new LyricsService.LrclibItem { TrackName = "Alors On Danse", ArtistName = "Stromae", SyncedLyrics = "[00:01.00]synced", Duration = 207 },
        };
        Assert.Equal("[00:01.00]synced", LyricsService.Pick(items, track)!.SyncedLyrics);
        Assert.Null(LyricsService.Pick(new[] { new LyricsService.LrclibItem { TrackName = "Autre chose", ArtistName = "Personne", SyncedLyrics = "x" } }, track));
    }

    [Fact]
    public async Task Service_SearchesThenCachesOnDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "shazim_lyrics_" + Guid.NewGuid().ToString("N"));
        var handler = new FakeHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/search")
            ? (HttpStatusCode.OK, """[{ "trackName": "Titre", "artistName": "Artiste", "duration": 200, "syncedLyrics": "[00:01.00]Une\n[00:03.00]Deux" }]""")
            : (HttpStatusCode.NotFound, "{}"));
        var track = new Track { Id = "shazam:1", Title = "Titre", Artist = "Artiste" };
        try
        {
            var service = new LyricsService(new FakeHttpFactory(handler), new NullLogger<LyricsService>(), dir);
            var lyrics = await service.GetAsync(track);
            Assert.NotNull(lyrics);
            Assert.True(lyrics!.IsSynced);
            Assert.Equal(200, lyrics.DurationSeconds);
            Assert.Contains("track_name=Titre", handler.Requests[0].RequestUri!.Query);
            Assert.All(handler.Requests, r => Assert.StartsWith("Shazim", r.Headers.UserAgent.ToString()));

            // A new service (next start) reads the disk cache: no request.
            int before = handler.Requests.Count;
            var again = await new LyricsService(new FakeHttpFactory(handler), new NullLogger<LyricsService>(), dir).GetAsync(track);
            Assert.Equal(2, again!.Lines.Count);
            Assert.Equal(before, handler.Requests.Count);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Service_OfflineIsNotCachedAsMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "shazim_lyrics_" + Guid.NewGuid().ToString("N"));
        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        var service = new LyricsService(new FakeHttpFactory(handler), new NullLogger<LyricsService>(), dir);
        Assert.Null(await service.GetAsync(new Track { Id = "1", Title = "T", Artist = "A" }));
        Assert.False(Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any());
    }
}

public class NewSettingsTests
{
    [Fact]
    public void Version4File_MigratesNotificationsOffToNone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shazim_settings_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "version": 4, "notifications": { "enabled": false }, "general": { "theme": "bizarre", "customAccent": "nope" } }""");
        var s = new Shazim.App.SettingsService(path).GetSettings();
        Assert.Equal(Shazim.App.NotificationStyle.None, s.Notifications.Style);
        Assert.False(s.Notifications.Enabled);
        Assert.Equal(Shazim.App.ThemeMode.Night, s.General.Theme);
        Assert.Equal("#FF4F7E", s.General.CustomAccent);
        Assert.True(s.Updates.AutoCheck);
        Assert.Equal(5, s.Version);
        File.Delete(path);
    }

    [Fact]
    public void Defaults_UseThePopupWithSound()
    {
        var s = new Shazim.App.AppSettings();
        Assert.Equal(Shazim.App.NotificationStyle.Popup, s.Notifications.Style);
        Assert.True(s.Notifications.Sound);
        Assert.Equal(1, s.Widget.Opacity);
    }

    [Fact]
    public void SilentNotification_FindsTheIconWindow()
    {
        // WinForms keeps the icon's window and id in private fields: make sure they are still there.
        using var icon = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = false };
        var t = typeof(System.Windows.Forms.NotifyIcon);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Assert.NotNull(t.GetField("_window", flags) ?? t.GetField("window", flags));
        Assert.NotNull(t.GetField("_id", flags) ?? t.GetField("id", flags));
    }
}
