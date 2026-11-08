using Shazim.Core.Services;
using Xunit;

namespace Shazim.Tests;

public class TrackExtrasTests
{
    [Theory]
    [InlineData("Daft Punk, Pharrell Williams & Nile Rodgers", "Daft Punk")]
    [InlineData("Dua Lipa feat. DaBaby", "Dua Lipa")]
    [InlineData("Stromae", "Stromae")]
    public void MainArtist_DropsFeaturedArtists(string artist, string expected) =>
        Assert.Equal(expected, TrackExtrasService.MainArtist(artist));

    [Theory]
    [InlineData("Levitating (feat. DaBaby)", "Levitating")]
    [InlineData("Alors on danse - Radio Edit", "Alors on danse")]
    [InlineData("Starboy [Remastered]", "Starboy")]
    public void CleanTitle_DropsVersionsAndFeats(string title, string expected) =>
        Assert.Equal(expected, TrackExtrasService.CleanTitle(title));

    [Fact]
    public void Pick_PrefersSameArtistAndTitle()
    {
        var items = new[]
        {
            new TrackExtrasService.SearchItem { Id = 1, Title = "Get Lucky (Cover)", Artist = new() { Name = "Someone Else" } },
            new TrackExtrasService.SearchItem { Id = 2, Title = "Get Lucky (feat. Pharrell Williams)", Artist = new() { Name = "Daft Punk" } },
        };
        Assert.Equal(2, TrackExtrasService.Pick(items, "Daft Punk", "Get Lucky")!.Id);
        Assert.Null(TrackExtrasService.Pick(items, "Nobody", "Unknown song"));
    }
}
