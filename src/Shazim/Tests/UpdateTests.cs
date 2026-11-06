using System.Net;
using Shazim.Core.Updates;
using Xunit;

namespace Shazim.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("v2.1.0", 2, 1, 0)]
    [InlineData("2.1", 2, 1, 0)]
    [InlineData("V3", 3, 0, 0)]
    [InlineData("v2.10.4-beta", 2, 10, 4)]
    public void ParseVersion_ReadsTags(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), UpdateParser.ParseVersion(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData(null)]
    public void ParseVersion_RejectsOtherTags(string? tag) => Assert.Null(UpdateParser.ParseVersion(tag));

    [Theory]
    [InlineData("2.1.0", "2.0.1", true)]
    [InlineData("2.0.10", "2.0.9", true)]   // numeric, not alphabetical
    [InlineData("2.0.1", "2.0.1", false)]
    [InlineData("2.0.0", "2.0.1", false)]
    [InlineData("3.0", "2.9.9", true)]
    public void IsNewer_ComparesNumerically(string remote, string current, bool expected)
    {
        Assert.Equal(expected, UpdateParser.IsNewer(UpdateParser.ParseVersion(remote)!, UpdateParser.ParseVersion(current)!));
    }

    [Fact]
    public void IsNewer_IgnoresTheRevisionOfTheAssemblyVersion()
    {
        Assert.False(UpdateParser.IsNewer(new Version(2, 1, 0), new Version(2, 1, 0, 0)));
    }

    private const string Sums =
        "﻿3f786850e387550fdab836ed7e6dc881de23001b8b6b0b4b7b0c7b5a0e1c2d3e  Shazim-Setup-2.1.0.exe\r\n" +
        "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899 *Other.zip\n";

    [Fact]
    public void FindChecksum_ReadsSha256SumsFiles()
    {
        Assert.Equal("3f786850e387550fdab836ed7e6dc881de23001b8b6b0b4b7b0c7b5a0e1c2d3e", UpdateParser.FindChecksum(Sums, "Shazim-Setup-2.1.0.exe"));
        Assert.Equal("aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899", UpdateParser.FindChecksum(Sums, "other.zip"));
        Assert.Null(UpdateParser.FindChecksum(Sums, "Shazim-Setup-2.0.0.exe"));
        Assert.Null(UpdateParser.FindChecksum("not a checksum  Shazim-Setup-2.1.0.exe", "Shazim-Setup-2.1.0.exe"));
    }

    private const string ReleaseJson = """
    {
      "tag_name": "v2.1.0", "draft": false, "prerelease": false,
      "html_url": "https://github.com/painfullfeet/Shazim/releases/tag/v2.1.0",
      "body": "## Nouveautés\n- **Mises à jour** depuis l'app\n- Thèmes\n\n## English\n- Updates",
      "assets": [
        { "name": "Shazim-Setup-2.1.0.exe", "size": 1000, "browser_download_url": "https://github.com/x/Shazim-Setup-2.1.0.exe" },
        { "name": "SHA256SUMS.txt", "size": 90, "browser_download_url": "https://github.com/x/SHA256SUMS.txt" },
        { "name": "evil.exe", "size": 1, "browser_download_url": "http://insecure/evil.exe" }
      ]
    }
    """;

    [Fact]
    public void ParseRelease_FindsVersionAndAssets()
    {
        var r = UpdateParser.ParseRelease(ReleaseJson)!;
        Assert.Equal(new Version(2, 1, 0), r.Version);
        Assert.Equal("2.1.0", r.DisplayVersion);
        Assert.Equal("Shazim-Setup-2.1.0.exe", r.SetupAsset!.Name);
        Assert.Equal("SHA256SUMS.txt", r.ChecksumAsset!.Name);
        Assert.DoesNotContain(r.Assets, a => a.Name == "evil.exe"); // only https downloads
    }

    [Fact]
    public void ParseRelease_IgnoresDraftsAndPrereleases()
    {
        Assert.Null(UpdateParser.ParseRelease(ReleaseJson.Replace("\"prerelease\": false", "\"prerelease\": true")));
        Assert.Null(UpdateParser.ParseRelease("{\"message\":\"API rate limit exceeded\"}"));
        Assert.Null(UpdateParser.ParseRelease("<html>"));
    }

    [Fact]
    public void Summary_KeepsTheFirstLanguageOnly()
    {
        var s = UpdateParser.Summary(UpdateParser.ParseRelease(ReleaseJson)!.Notes);
        Assert.Equal("• Mises à jour depuis l'app\n• Thèmes", s);
    }

    [Fact]
    public async Task Download_RefusesAFileWhoseChecksumDoesNotMatch()
    {
        var payload = "fake installer";
        var handler = new FakeHandler(req => req.RequestUri!.AbsolutePath.EndsWith(".txt")
            ? (HttpStatusCode.OK, new string('0', 64) + "  Shazim-Setup-2.1.0.exe")
            : (HttpStatusCode.OK, payload));
        var service = NewService(handler);
        var release = UpdateParser.ParseRelease(ReleaseJson)!;

        await Assert.ThrowsAsync<Shazim.App.Services.UpdateException>(() => service.DownloadAsync(release, null));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "Shazim-Update", "Shazim-Setup-2.1.0.exe.part")));
    }

    [Fact]
    public async Task Download_AcceptsTheRightChecksum()
    {
        var payload = "fake installer bytes";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var handler = new FakeHandler(req => req.RequestUri!.AbsolutePath.EndsWith(".txt")
            ? (HttpStatusCode.OK, $"{hash}  Shazim-Setup-2.1.0.exe")
            : (HttpStatusCode.OK, payload));
        var service = NewService(handler);

        var path = await service.DownloadAsync(UpdateParser.ParseRelease(ReleaseJson)!, null);
        Assert.Equal(payload, File.ReadAllText(path));
        Assert.All(handler.Requests, r => Assert.Equal("Shazim-Updater", r.Headers.UserAgent.ToString()));
        File.Delete(path);
    }

    [Fact]
    public async Task Check_IsQuietWhenRateLimited()
    {
        var service = NewService(new FakeHandler(_ => (HttpStatusCode.Forbidden, "{\"message\":\"API rate limit exceeded\"}")));
        var result = await service.CheckAsync(manual: false);
        Assert.Equal(Shazim.App.Services.UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Check_HonoursTheSkippedVersion()
    {
        var json = ReleaseJson.Replace("v2.1.0", "v99.0.0");
        var (service, settings) = NewServiceWithSettings(new FakeHandler(_ => (HttpStatusCode.OK, json)));
        Assert.Equal(Shazim.App.Services.UpdateCheckStatus.Available, (await service.CheckAsync(false)).Status);
        settings.GetSettings().Updates.SkippedVersion = "99.0.0";
        Assert.Equal(Shazim.App.Services.UpdateCheckStatus.Skipped, (await service.CheckAsync(false)).Status);
        Assert.Equal(Shazim.App.Services.UpdateCheckStatus.Available, (await service.CheckAsync(manual: true)).Status);
        Assert.False(service.AutoCheckDue()); // just checked
    }

    private static Shazim.App.Services.UpdateService NewService(FakeHandler handler) => NewServiceWithSettings(handler).Service;

    private static (Shazim.App.Services.UpdateService Service, Shazim.App.SettingsService Settings) NewServiceWithSettings(FakeHandler handler)
    {
        var settings = new Shazim.App.SettingsService(Path.Combine(Path.GetTempPath(), $"shazim_settings_{Guid.NewGuid():N}.json"));
        return (new Shazim.App.Services.UpdateService(new FakeHttpFactory(handler), settings,
            new Shazim.Core.Services.NullLogger<Shazim.App.Services.UpdateService>()), settings);
    }
}
