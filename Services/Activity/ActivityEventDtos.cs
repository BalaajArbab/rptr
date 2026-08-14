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
[JsonDerivedType(
    typeof(SnippetChangedActivityEventRequest),
    SnippetChangedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(TagSelectionChangedActivityEventRequest),
    TagSelectionChangedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(PlaylistTagChangedActivityEventRequest),
    PlaylistTagChangedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(SongTagsChangedActivityEventRequest),
    SongTagsChangedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(PlaylistOpenedActivityEventRequest),
    PlaylistOpenedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(WebPlayerInteractionActivityEventRequest),
    WebPlayerInteractionActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(SearchUsedActivityEventRequest),
    SearchUsedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(QueueChangedActivityEventRequest),
    QueueChangedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(SongExclusionChangedActivityEventRequest),
    SongExclusionChangedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(PlaybackFailedActivityEventRequest),
    PlaybackFailedActivityEventRequest.EventType)]
[JsonDerivedType(
    typeof(GuestSessionStartedActivityEventRequest),
    GuestSessionStartedActivityEventRequest.EventType)]
public abstract class ActivityEventRequest;

public sealed class SpotifyTrackPlayedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "spotify_track_played";

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(500)]
    public required string PlaylistName { get; init; }

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
    [MaxLength(500)]
    public required string PlaylistName { get; init; }

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

public sealed record ActivitySnippetSegmentDto(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int StartMs,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int EndMs);

public sealed class SnippetChangedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "snippet_changed";

    [Required]
    [AllowedValues("created", "edited", "deleted")]
    public required string Action { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(500)]
    public required string PlaylistName { get; init; }

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

    [Range(1, 4)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? SegmentCount { get; init; }

    public IReadOnlyList<ActivitySnippetSegmentDto>? Segments { get; init; }
}

public sealed class TagSelectionChangedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "tag_selection_changed";

    [Required]
    [AllowedValues("selected", "unselected")]
    public required string Action { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(100)]
    public required string TagName { get; init; }
}

public sealed class PlaylistTagChangedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "playlist_tag_changed";

    [Required]
    [AllowedValues("created", "deleted")]
    public required string Action { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(100)]
    public required string TagName { get; init; }

    [RegularExpression("^#[0-9A-Fa-f]{6}$")]
    public string? TagColour { get; init; }
}

public sealed class SongTagsChangedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "song_tags_changed";

    [Required]
    [AllowedValues("added", "deleted")]
    public required string Action { get; init; }

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

    [Required]
    public required IReadOnlyList<string> Tags { get; init; }
}

public sealed class PlaylistOpenedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "playlist_opened";

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(500)]
    public required string PlaylistName { get; init; }

    [Range(0, int.MaxValue)]
    [JsonRequired]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int TrackCount { get; init; }
}

public sealed class WebPlayerInteractionActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "web_player_interaction";

    [Required]
    [AllowedValues("next", "previous", "replay", "seek", "pause", "resume")]
    public required string Action { get; init; }

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

    [Range(0, int.MaxValue)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? PlaybackPositionMs { get; init; }
}

public sealed class SearchUsedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "search_used";

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(200)]
    public required string SearchText { get; init; }

    [Range(0, int.MaxValue)]
    [JsonRequired]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int ResultCount { get; init; }
}

public sealed class QueueChangedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "queue_changed";

    [Required]
    [AllowedValues("added", "removed", "cleared")]
    public required string Action { get; init; }

    [MaxLength(128)]
    public string? SpotifyPlaylistId { get; init; }

    [MaxLength(128)]
    public string? SpotifyTrackId { get; init; }

    [MaxLength(500)]
    public string? SongName { get; init; }

    [MaxLength(500)]
    public string? PrimaryArtistName { get; init; }

    [Range(0, int.MaxValue)]
    [JsonRequired]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int QueueLength { get; init; }
}

public sealed class SongExclusionChangedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "song_exclusion_changed";

    [Required]
    [AllowedValues("excluded", "included")]
    public required string Action { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(500)]
    public required string PlaylistName { get; init; }

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

public sealed class PlaybackFailedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "playback_failed";

    [Required]
    [AllowedValues("spotify_track", "snippet")]
    public required string PlaybackType { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SpotifyPlaylistId { get; init; }

    [Required]
    [MaxLength(500)]
    public required string PlaylistName { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SpotifyTrackId { get; init; }

    [Required]
    [MaxLength(500)]
    public required string SongName { get; init; }

    [Required]
    [MaxLength(500)]
    public required string PrimaryArtistName { get; init; }

    [Range(1, int.MaxValue)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? SnippetId { get; init; }

    [MaxLength(45)]
    public string? SnippetName { get; init; }

    [MaxLength(100)]
    public string? ErrorCode { get; init; }

    [Required]
    [MaxLength(1000)]
    public required string ErrorMessage { get; init; }
}

public sealed class GuestSessionStartedActivityEventRequest : ActivityEventRequest
{
    public const string EventType = "guest_session_started";
}

public sealed class GuestSessionEndedActivityEventRequest
{
    public const string EventType = "guest_session_ended";

    [Required]
    [AllowedValues(
        "logout",
        "idle_timeout",
        "absolute_timeout",
        "unauthorized",
        "page_closed",
        "unknown")]
    public required string EndReason { get; init; }
}

public sealed class GuestSessionCheckActivityEventRequest
{
    [Required]
    [AllowedValues("vacant", "occupied")]
    public required string Availability { get; init; }
}
