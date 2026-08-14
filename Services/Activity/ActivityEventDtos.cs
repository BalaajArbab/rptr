using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace rptr.Services.Activity;

public sealed class EncodedActivityRequest
{
    public const int MaximumPayloadLength = 8192;

    [Required]
    [MinLength(4)]
    [MaxLength(MaximumPayloadLength)]
    public required string Payload { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(
    typeof(SnippetPlayedActivityEventRequest),
    SnippetPlayedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(SpotifyTrackPlayedActivityEventRequest),
    SpotifyTrackPlayedActivityEventRequest.EventType)]
public abstract class ActivityEventRequest;

public sealed class SpotifyTrackPlayedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "spotify_track_played";

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SpotifyTrackId { get; init; }

    [Required]
    [MaxLength(500)]
    public required string SongName { get; init; }

    [Required]
    [MaxLength(500)]
    public required string PrimaryArtistName { get; init; }
}

public sealed class SnippetPlayedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "snippet_played";

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SpotifyTrackId { get; init; }

    [Range(1, int.MaxValue)]
    [JsonRequired]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int SnippetId { get; init; }

    [Required]
    [MaxLength(45)]
    public required string SnippetName { get; init; }

    [Required]
    [MaxLength(500)]
    public required string SongName { get; init; }

    [Required]
    [MaxLength(500)]
    public required string PrimaryArtistName { get; init; }
}
