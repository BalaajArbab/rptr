using System.Text.Json;
using rptr.Services.Activity;

namespace rptr.UnitTests.Activity;

[Trait("Category", "Unit")]
public sealed class ActivityEventSerializationTests
{
    [Fact]
    public void EventTypes_HaveUniqueRequestTypesAndDiscriminators()
    {
        var eventTypes = ActivityEventMetadata.EventTypes;

        Assert.NotEmpty(eventTypes);
        Assert.Equal(
            eventTypes.Count,
            eventTypes.Select(eventType => eventType.RequestType).Distinct().Count());
        Assert.Equal(
            eventTypes.Count,
            eventTypes.Select(eventType => eventType.Discriminator).Distinct().Count());
    }

    [Fact]
    public void GetDiscriminator_ForRegisteredRequest_ReturnsDeclaredEventType()
    {
        var discriminator = ActivityEventMetadata.GetDiscriminator(
            typeof(SnippetPlayedActivityEventRequest));

        Assert.Equal(SnippetPlayedActivityEventRequest.EventType, discriminator);
    }

    [Fact]
    public void GetDiscriminator_ForUnregisteredRequest_ThrowsConfigurationError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ActivityEventMetadata.GetDiscriminator(
                typeof(GuestSessionEndedActivityEventRequest)));

        Assert.Contains("is not registered", exception.Message);
    }

    [Fact]
    public void Deserialize_PolymorphicRequest_UsesTypeAndTrimsStrings()
    {
        const string json = """
            {
              "type": "spotify_track_played",
              "spotifyPlaylistId": "  playlist-1  ",
              "playlistName": "  Demo Playlist  ",
              "spotifyTrackId": "  track-1  ",
              "songName": "  Song  ",
              "primaryArtistName": "  Artist  "
            }
            """;
        var options = CreateJsonOptions();

        var result = JsonSerializer.Deserialize<ActivityEventRequest>(json, options);

        var trackPlayed = Assert.IsType<SpotifyTrackPlayedActivityEventRequest>(result);
        Assert.Equal("playlist-1", trackPlayed.SpotifyPlaylistId);
        Assert.Equal("Demo Playlist", trackPlayed.PlaylistName);
        Assert.Equal("track-1", trackPlayed.SpotifyTrackId);
        Assert.Equal("Song", trackPlayed.SongName);
        Assert.Equal("Artist", trackPlayed.PrimaryArtistName);
    }

    [Fact]
    public void Serialize_WithTrimStringConverter_PreservesStoredValue()
    {
        var options = CreateJsonOptions();

        var json = JsonSerializer.Serialize("  value  ", options);

        Assert.Equal("\"  value  \"", json);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new TrimStringJsonConverter());
        return options;
    }
}
