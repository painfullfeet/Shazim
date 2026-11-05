using System.Net;
using Shazim.Core.Audio;
using Shazim.Core.Models;
using Shazim.Core.Recognition;
using Shazim.Core.Services;
using Xunit;

namespace Shazim.Tests;

public class ShazamParsingTests
{
    private const string MatchJson = """
    {
      "matches": [{ "id": "1", "offset": 12.3 }],
      "track": {
        "key": "562666397", "title": "Well Figure It Out", "subtitle": "Tys",
        "images": { "coverart": "https://img/400.jpg", "coverarthq": "https://img/hq.jpg" },
        "genres": { "primary": "Hip-Hop" },
        "url": "https://www.shazam.com/track/562666397/well-figure-it-out",
        "sections": [{ "type": "SONG", "metadata": [
            { "title": "Album", "text": "Night Tapes" },
            { "title": "Maison de disques", "text": "Indie" },
            { "title": "Sortie", "text": "2021" } ] }],
        "hub": {
          "type": "APPLEMUSIC",
          "actions": [ { "type": "applemusicplay", "id": "1" }, { "type": "uri", "uri": "https://audio-ssl.itunes.apple.com/preview.m4a" } ],
          "options": [ { "actions": [ { "type": "applemusicopen", "uri": "https://music.apple.com/fr/album/x?i=1" } ] } ],
          "providers": [
            { "type": "SPOTIFY", "actions": [ { "type": "uri", "uri": "spotify:search:Well%20Figure%20It%20Out%20Tys" } ] },
            { "type": "YOUTUBEMUSIC", "actions": [ { "type": "uri", "uri": "https://music.youtube.com/search?q=Well+Figure+It+Out+Tys" } ] }
          ]
        }
      }
    }
    """;

    [Fact]
    public void Parse_Match_ExtractsMetadataAndLinks()
    {
        var r = ShazamRecognitionService.Parse(MatchJson);
        Assert.True(r.IsMatch);
        var t = r.Track!;
        Assert.Equal("shazam:562666397", t.Id);
        Assert.Equal("Tys", t.Artist);
        Assert.Equal("Night Tapes", t.Album);
        Assert.Equal("Indie", t.Label);
        Assert.Equal("2021", t.ReleaseYear);
        Assert.Equal("Hip-Hop", t.Genre);
        Assert.Equal("https://img/hq.jpg", t.AlbumArtUrl);
        Assert.Equal("https://audio-ssl.itunes.apple.com/preview.m4a", t.PreviewUrl);
        Assert.Equal("https://music.apple.com/fr/album/x?i=1", t.FindLink("Apple Music"));
        Assert.StartsWith("https://open.spotify.com/search/", t.FindLink("Spotify"));
        Assert.Equal("https://music.youtube.com/search?q=Well+Figure+It+Out+Tys", t.FindLink("YouTube Music"));
        Assert.Null(t.FindLink("Deezer")); // not listed by Shazam, so not shown
        Assert.Equal(new[] { "Spotify", "Apple Music", "YouTube Music" }, t.Platforms.Select(p => p.Platform));
        Assert.NotNull(t.FindLink("YouTube"));
        Assert.True(t.HasPreview);
        Assert.StartsWith("https://www.shazam.com/track/", t.FindLink("Shazam"));
    }

    [Fact]
    public void Parse_NoMatch_ReturnsRetryHint()
    {
        var r = ShazamRecognitionService.Parse("""{ "matches": [], "retryms": 1500, "tagid": "x" }""");
        Assert.Equal(RecognitionStatus.NoMatch, r.Status);
        Assert.Equal(1500, r.RetryAfterMs);
    }

    [Fact]
    public async Task Identify_PostsSignatureAndParsesResponse()
    {
        var handler = new FakeHandler(_ => (HttpStatusCode.OK, MatchJson));
        var svc = new ShazamRecognitionService(new FakeHttpFactory(handler), new NullLogger<ShazamRecognitionService>());
        var r = await svc.IdentifyAsync(new AudioClip(Signals.Melody(48000, 6, 2), 48000, 2));

        Assert.True(r.IsMatch);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Contains("amp.shazam.com/discovery/v5", req.RequestUri!.ToString());
        var body = handler.Bodies[0];
        Assert.Contains("data:audio/vnd.shazam.sig;base64,", body);
        Assert.Contains("\"samplems\":12000", body);
    }

    [Fact]
    public async Task Identify_Silence_DoesNotCallNetwork()
    {
        var handler = new FakeHandler(_ => (HttpStatusCode.OK, MatchJson));
        var svc = new ShazamRecognitionService(new FakeHttpFactory(handler), new NullLogger<ShazamRecognitionService>());
        var r = await svc.IdentifyAsync(new AudioClip(new float[16000 * 6], 16000, 1));
        Assert.Equal(RecognitionStatus.NoMatch, r.Status);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "Trop de requêtes")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task Identify_HttpError_ReturnsReadableError(HttpStatusCode code, string expected)
    {
        var handler = new FakeHandler(_ => (code, ""));
        var svc = new ShazamRecognitionService(new FakeHttpFactory(handler), new NullLogger<ShazamRecognitionService>());
        var r = await svc.IdentifyAsync(new AudioClip(Signals.Melody(16000, 6), 16000, 1));
        Assert.Equal(RecognitionStatus.Error, r.Status);
        Assert.Contains(expected, r.Error);
    }
}

public class IdentificationServiceTests
{
    private static Track T(string id) => new() { Id = id, Title = "Song " + id, Artist = "Band" };

    private static (IdentificationService Svc, LibraryService Library, FakeProvider Provider) Build(
        Func<AudioClip, int, RecognitionResult> answer, float[]? signal = null)
    {
        var (library, _, _) = TestDb.Create();
        var provider = new FakeProvider("Shazam", answer);
        var capture = new FakeCapture(signal ?? Signals.Melody(16000, 20), 16000);
        var svc = new IdentificationService(capture, provider, library, new DeduplicationService(new NullLogger<DeduplicationService>()),
            new NullLogger<IdentificationService>());
        return (svc, library, provider);
    }

    [Fact]
    public void Checkpoints_AreEveryFourSecondsUpToMax()
    {
        Assert.Equal(new[] { 4.0, 8.0, 12.0 }, IdentificationService.BuildCheckpoints(TimeSpan.FromSeconds(12)).Select(t => t.TotalSeconds));
        Assert.Equal(new[] { 4.0, 8.0 }, IdentificationService.BuildCheckpoints(TimeSpan.FromSeconds(8)).Select(t => t.TotalSeconds));
    }

    [Fact]
    public async Task Match_IsSavedWithDetection()
    {
        var (svc, library, provider) = Build((_, _) => RecognitionResult.Matched("Shazam", T("1"), 1));
        IdentifyOutcome? raised = null;
        svc.TrackIdentified += (_, o) => raised = o;

        var outcome = await svc.IdentifyAsync(new IdentifyRequest("default", IsAuto: false));

        Assert.Equal(IdentifyOutcomeKind.Match, outcome.Kind);
        Assert.Equal(1, provider.Calls); // found at the first checkpoint
        Assert.NotNull(raised);
        Assert.Equal(1, await library.GetTrackCountAsync());
        Assert.Equal(1, await library.GetDetectionCountAsync());
    }

    [Fact]
    public async Task RetriesWithMoreAudioUntilFound()
    {
        var (svc, _, provider) = Build((_, call) => call < 3
            ? RecognitionResult.NoMatch("Shazam")
            : RecognitionResult.Matched("Shazam", T("2"), 1));

        var outcome = await svc.IdentifyAsync(new IdentifyRequest("default", false, MaxDurationMs: 12000));

        Assert.Equal(IdentifyOutcomeKind.Match, outcome.Kind);
        Assert.Equal(3, provider.Calls);
        Assert.True(provider.ClipDurations[2] > provider.ClipDurations[0]);
    }

    [Fact]
    public async Task NothingFound_ReturnsNoMatch()
    {
        var (svc, library, provider) = Build((_, _) => RecognitionResult.NoMatch("Shazam"));
        var outcome = await svc.IdentifyAsync(new IdentifyRequest("default", false, MaxDurationMs: 8000));
        Assert.Equal(IdentifyOutcomeKind.NoMatch, outcome.Kind);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(0, await library.GetTrackCountAsync());
    }

    [Fact]
    public async Task Silence_DoesNotQueryProvider()
    {
        var (svc, _, provider) = Build((_, _) => RecognitionResult.Matched("Shazam", T("x"), 1), new float[16000 * 20]);
        var outcome = await svc.IdentifyAsync(new IdentifyRequest("default", false, MaxDurationMs: 8000));
        Assert.Equal(IdentifyOutcomeKind.Silence, outcome.Kind);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task NetworkError_StopsEarly()
    {
        var (svc, _, provider) = Build((_, _) => RecognitionResult.Failed("Shazam", "Pas de connexion Internet.", fatal: true));
        var outcome = await svc.IdentifyAsync(new IdentifyRequest("default", false, MaxDurationMs: 12000));
        Assert.Equal(IdentifyOutcomeKind.Error, outcome.Kind);
        Assert.Equal(1, provider.Calls);
        Assert.Contains("Internet", outcome.Message);
    }

    [Fact]
    public async Task AutoMode_DuplicateIsNotRecordedTwice()
    {
        var (svc, library, _) = Build((_, _) => RecognitionResult.Matched("Shazam", T("dup"), 1));
        var first = await svc.IdentifyAsync(new IdentifyRequest("default", IsAuto: true));
        var second = await svc.IdentifyAsync(new IdentifyRequest("default", IsAuto: true));

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(1, await library.GetDetectionCountAsync());
    }

    [Fact]
    public async Task BothSources_AreSearchedAndTheOneThatHearsMusicWins()
    {
        var (library, _, _) = TestDb.Create();
        var provider = new FakeProvider("Shazam", (clip, _) => RecognitionResult.Matched("Shazam", T("room"), 1));
        // The PC is silent, the microphone hears music.
        var capture = new FakeCapture(id => id == "in:default" ? Signals.Melody(16000, 20) : new float[16000 * 20], 16000);
        var svc = new IdentificationService(capture, provider, library, new DeduplicationService(new NullLogger<DeduplicationService>()),
            new NullLogger<IdentificationService>());

        var outcome = await svc.IdentifyAsync(new IdentifyRequest(new[] { "default", "in:default" }, IsAuto: false));

        Assert.Equal(IdentifyOutcomeKind.Match, outcome.Kind);
        Assert.Equal("in:default", outcome.SourceName);
        Assert.Equal(new[] { "default", "in:default" }, capture.Opened);
        Assert.Equal(1, provider.Calls); // the silent PC source was not sent
        Assert.Equal("in:default", (await library.GetHistoryAsync())[0].AudioDevice);
    }

    [Fact]
    public async Task MissingMicrophone_StillListensToThePc()
    {
        var (library, _, _) = TestDb.Create();
        var provider = new FakeProvider("Shazam", (_, _) => RecognitionResult.Matched("Shazam", T("pc"), 1));
        var capture = new FakeCapture(id => id.StartsWith("in:") ? null : Signals.Melody(16000, 20), 16000);
        var svc = new IdentificationService(capture, provider, library, new DeduplicationService(new NullLogger<DeduplicationService>()),
            new NullLogger<IdentificationService>());

        var outcome = await svc.IdentifyAsync(new IdentifyRequest(new[] { "default", "in:headset" }, IsAuto: false));
        Assert.Equal(IdentifyOutcomeKind.Match, outcome.Kind);

        var onlyMic = await svc.IdentifyAsync(new IdentifyRequest(new[] { "in:headset" }, IsAuto: false));
        Assert.Equal(IdentifyOutcomeKind.Error, onlyMic.Kind);
        Assert.Contains("Micro", onlyMic.Message);
    }

    [Fact]
    public async Task Cancellation_ReturnsCancelled()
    {
        var (svc, _, _) = Build((_, _) => RecognitionResult.NoMatch("Shazam"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var outcome = await svc.IdentifyAsync(new IdentifyRequest("default", false, MaxDurationMs: 20000), null, cts.Token);
        Assert.Equal(IdentifyOutcomeKind.Cancelled, outcome.Kind);
    }
}
