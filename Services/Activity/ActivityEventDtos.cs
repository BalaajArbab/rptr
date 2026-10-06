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

public sealed class DemoVideoInteractionActivityEventRequest : IValidatableObject
{
    public const string EventName = "demo_video_interaction";

    [JsonRequired]
    public Guid PlaybackSessionId { get; init; }

    [Required]
    [AllowedValues(
        "started",
        "resumed",
        "paused",
        "seeked",
        "milestone",
        "completed",
        "replayed",
        "failed")]
    public required string Action { get; init; }

    [Range(0, int.MaxValue)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? PositionMs { get; init; }

    [Range(1, int.MaxValue)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? DurationMs { get; init; }

    [Range(0, 100)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? Percentage { get; init; }

    [Range(0, int.MaxValue)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? FromPositionMs { get; init; }

    [Range(0, int.MaxValue)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? ToPositionMs { get; init; }

    [Range(1, 4)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? ErrorCode { get; init; }

    [MaxLength(500)]
    public string? ErrorMessage { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PlaybackSessionId == Guid.Empty)
        {
            yield return new ValidationResult(
                "The playback session ID must not be empty.",
                [nameof(PlaybackSessionId)]);
        }

        if (Action != "failed")
        {
            if (PositionMs is null)
            {
                yield return new ValidationResult(
                    "The playback position is required for this action.",
                    [nameof(PositionMs)]);
            }

            if (DurationMs is null)
            {
                yield return new ValidationResult(
                    "The video duration is required for this action.",
                    [nameof(DurationMs)]);
            }
        }

        if (PositionMs is { } position
            && DurationMs is { } duration
            && position > duration)
        {
            yield return new ValidationResult(
                "The playback position cannot exceed the video duration.",
                [nameof(PositionMs)]);
        }

        if (Action == "seeked")
        {
            if (FromPositionMs is null)
            {
                yield return new ValidationResult(
                    "The previous playback position is required for a seek event.",
                    [nameof(FromPositionMs)]);
            }

            if (ToPositionMs is null)
            {
                yield return new ValidationResult(
                    "The new playback position is required for a seek event.",
                    [nameof(ToPositionMs)]);
            }
        }

        if (DurationMs is { } videoDuration)
        {
            if (FromPositionMs > videoDuration)
            {
                yield return new ValidationResult(
                    "The previous playback position cannot exceed the video duration.",
                    [nameof(FromPositionMs)]);
            }

            if (ToPositionMs > videoDuration)
            {
                yield return new ValidationResult(
                    "The new playback position cannot exceed the video duration.",
                    [nameof(ToPositionMs)]);
            }
        }

        if (Action == "milestone"
            && Percentage is not (25 or 50 or 75 or 90))
        {
            yield return new ValidationResult(
                "A milestone percentage must be 25, 50, 75, or 90.",
                [nameof(Percentage)]);
        }

        if (Action == "failed"
            && ErrorCode is null
            && string.IsNullOrWhiteSpace(ErrorMessage))
        {
            yield return new ValidationResult(
                "An error code or error message is required for a failed event.",
                [nameof(ErrorCode), nameof(ErrorMessage)]);
        }
    }
}
