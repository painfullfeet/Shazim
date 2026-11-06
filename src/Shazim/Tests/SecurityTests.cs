using Shazim.Core.Security;
using Xunit;

namespace Shazim.Tests;

public class SecurityTests
{
    [Theory]
    [InlineData("https://open.spotify.com/track/abc")]
    [InlineData("https://music.apple.com/fr/album/x?i=1")]
    [InlineData("http://example.com/page")]
    [InlineData("spotify:track:4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("deezer://www.deezer.com/track/1")]
    public void SafeLinks_AreAllowed(string link) => Assert.True(LinkPolicy.IsSafe(link));

    [Theory]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("\\\\evil-server\\share\\payload.exe")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("search-ms:query=x&crumb=location:\\\\evil\\share")]
    [InlineData("javascript:alert(1)")]
    [InlineData("calc.exe")]
    [InlineData("https://user:pass@evil.example/")]
    [InlineData("https://example.com/\nother")]
    [InlineData("")]
    [InlineData(null)]
    public void DangerousLinks_AreRefused(string? link) => Assert.False(LinkPolicy.IsSafe(link));
}
