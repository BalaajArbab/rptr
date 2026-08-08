using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rptr.Services;
using rptr.Services.Authorization;
using rptr.Services.Playlists;
using rptr.Services.PlaylistSongs;

namespace rptr.Controllers;

[ApiController]
[Authorize]
[Route("api/playlist")]
public sealed class PlaylistController : ControllerBase
{
    private readonly IPlaylistService _playlistService;
    private readonly IPlaylistSongService _playlistSongService;

    public PlaylistController(
        IPlaylistService playlistService,
        IPlaylistSongService playlistSongService)
    {
        _playlistService = playlistService;
        _playlistSongService = playlistSongService;
    }

    [HttpPost("{spotifyPlaylistId}/access", Name = "AccessPlaylist")]
    [EndpointSummary("Register or access a playlist")]
    [EndpointDescription(
        "Updates the last-accessed time for a registered playlist. " +
        "When the playlist is unknown, Spotify verifies that it is public and owned by the authenticated user before it is registered.")]
    [ProducesResponseType<PlaylistAccessResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<ActionResult<PlaylistAccessResponse>> Access(
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _playlistService.AccessAsync(
            userId,
            spotifyPlaylistId,
            cancellationToken);

        return result.Status == ServiceResultStatus.Success
            ? Ok(result.Value)
            : MapFailure(result);
    }

    [HttpGet("{spotifyPlaylistId}/tags", Name = "GetPlaylistTags")]
    [EndpointSummary("Get a playlist's tags")]
    [EndpointDescription("Returns the locally stored structured tags for a Spotify playlist.")]
    [ProducesResponseType<PlaylistTagDto[]>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<PlaylistTagDto>>> GetTags(
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        var tags = await _playlistService.GetTagsAsync(spotifyPlaylistId, cancellationToken);
        return Ok(tags);
    }

    [HttpPut("{spotifyPlaylistId}/tags", Name = "UpdatePlaylistTags")]
    [EndpointSummary("Replace a playlist's tags")]
    [EndpointDescription(
        "Replaces all locally stored tags for a registered playlist. " +
        "Only the Spotify playlist owner may update tags.")]
    [ProducesResponseType<PlaylistTagDto[]>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<PlaylistTagDto>>> UpdateTags(
        string spotifyPlaylistId,
        [FromBody] UpdatePlaylistTagsRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _playlistService.UpdateTagsAsync(
            userId,
            spotifyPlaylistId,
            request.Tags,
            cancellationToken);

        return result.Status == ServiceResultStatus.Success
            ? Ok(result.Value)
            : MapFailure(result);
    }

    [HttpGet("{spotifyPlaylistId}/songs", Name = "GetPlaylistSongs")]
    [EndpointSummary("Get all locally annotated songs for a playlist")]
    [EndpointDescription(
        "Returns each song having a snippet, tags, or an exclusion, together with mutation capabilities for the authenticated user.")]
    [ProducesResponseType<PlaylistSongsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<PlaylistSongsResponse>> GetSongs(
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _playlistSongService.GetForPlaylistAsync(
            userId,
            spotifyPlaylistId,
            cancellationToken);

        return result.Status == ServiceResultStatus.Success
            ? Ok(result.Value)
            : MapFailure(result);
    }

    [HttpGet("{spotifyPlaylistId}/songs/tags", Name = "GetPlaylistSongTags")]
    [EndpointSummary("Get all song tags for a playlist (deprecated)")]
    [EndpointDescription(
        "Deprecated: use GET /api/playlist/{spotifyPlaylistId}/songs. " +
        "Returns the legacy song-tag response shape for songs with tags.")]
    [ProducesResponseType<SongTagsResponse[]>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<SongTagsResponse>>> GetSongTags(
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        var result = await _playlistSongService.GetTagsForPlaylistAsync(
            spotifyPlaylistId,
            cancellationToken);

        return result.Status == ServiceResultStatus.Success
            ? Ok(result.Value)
            : MapFailure(result);
    }

    [HttpPut("{spotifyPlaylistId}/songs/{songId}/tags", Name = "PutSongTags")]
    [EndpointSummary("Replace a song's tags")]
    [EndpointDescription(
        "Creates or replaces the locally stored tags and metadata for a playlist song. " +
        "An empty tag array clears tags and removes the row only when no other annotation remains. " +
        "Only the playlist owner may perform this operation.")]
    [ProducesResponseType<SongTagsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<SongTagsResponse>> PutSongTags(
        string spotifyPlaylistId,
        string songId,
        [FromBody] PutSongTagsRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _playlistSongService.PutTagsAsync(
            userId,
            spotifyPlaylistId,
            songId,
            request,
            cancellationToken);

        if (result.Status != ServiceResultStatus.Success)
        {
            return MapSongMutationFailure(result);
        }

        return result.Value is null
            ? NoContent()
            : Ok(result.Value);
    }

    [HttpPut("{spotifyPlaylistId}/songs/{songId}/excluded", Name = "PutSongExcluded")]
    [EndpointSummary("Set whether a playlist song is excluded")]
    [EndpointDescription(
        "Creates or updates a playlist song's exclusion and metadata. " +
        "Clearing the final annotation removes the local song row. Only the playlist owner may perform this operation.")]
    [ProducesResponseType<SongExcludedResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<SongExcludedResponse>> PutSongExcluded(
        string spotifyPlaylistId,
        string songId,
        [FromBody] PutSongExcludedRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _playlistSongService.PutExcludedAsync(
            userId,
            spotifyPlaylistId,
            songId,
            request,
            cancellationToken);

        return result.Status == ServiceResultStatus.Success
            ? Ok(result.Value)
            : MapSongMutationFailure(result);
    }

    private ActionResult MapFailure<T>(ServiceResult<T> result) => result.Status switch
    {
        ServiceResultStatus.ValidationFailed => ValidationProblem(
            new ValidationProblemDetails(
                result.Errors!.ToDictionary(pair => pair.Key, pair => pair.Value))),
        ServiceResultStatus.Unauthorized => AuthenticationProblem(),
        ServiceResultStatus.Forbidden => Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Playlist owner access required."),
        ServiceResultStatus.NotFound => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "The Spotify playlist was not found."),
        ServiceResultStatus.Conflict => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The playlist was registered concurrently. Retry the request."),
        ServiceResultStatus.UpstreamFailure => Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Spotify could not verify the playlist."),
        _ => Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.")
    };

    private ActionResult MapSongMutationFailure<T>(ServiceResult<T> result) =>
        result.Status switch
        {
            ServiceResultStatus.ValidationFailed => ValidationProblem(
                new ValidationProblemDetails(
                    result.Errors!.ToDictionary(pair => pair.Key, pair => pair.Value))),
            ServiceResultStatus.Unauthorized => AuthenticationProblem(),
            ServiceResultStatus.Forbidden => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Playlist owner access required."),
            ServiceResultStatus.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The registered playlist was not found."),
            ServiceResultStatus.Conflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Song data was updated concurrently. Retry the request."),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred.")
        };

    private ObjectResult AuthenticationProblem() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "A valid backend user identity is required.");
}
