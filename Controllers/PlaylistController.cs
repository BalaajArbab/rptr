using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rptr.Services;
using rptr.Services.Authorization;
using rptr.Services.Playlists;
using rptr.Services.SongTags;

namespace rptr.Controllers;

[ApiController]
[Authorize]
[Route("api/playlist")]
public sealed class PlaylistController : ControllerBase
{
    private readonly IPlaylistService _playlistService;
    private readonly ISongTagService _songTagService;

    public PlaylistController(
        IPlaylistService playlistService,
        ISongTagService songTagService)
    {
        _playlistService = playlistService;
        _songTagService = songTagService;
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

    [HttpGet("{spotifyPlaylistId}/songs/tags", Name = "GetPlaylistSongTags")]
    [EndpointSummary("Get all song tags for a playlist")]
    [EndpointDescription(
        "Returns every locally stored song-tag entry for a Spotify playlist. " +
        "Returns an empty array when the playlist or its song tags are not stored.")]
    [ProducesResponseType<SongTagsResponse[]>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<SongTagsResponse>>> GetSongTags(
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        var result = await _songTagService.GetForPlaylistAsync(
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
        "An empty tag array deletes the stored row. Only the playlist owner may perform this operation.")]
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

        var result = await _songTagService.PutAsync(
            userId,
            spotifyPlaylistId,
            songId,
            request,
            cancellationToken);

        if (result.Status != ServiceResultStatus.Success)
        {
            return MapSongTagFailure(result);
        }

        return result.Value is null
            ? NoContent()
            : Ok(result.Value);
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

    private ActionResult MapSongTagFailure(ServiceResult<SongTagsResponse?> result) =>
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
                title: "Song tags were created concurrently. Retry the request."),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred.")
        };

    private ObjectResult AuthenticationProblem() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "A valid backend user identity is required.");
}
