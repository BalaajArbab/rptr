using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rptr.Services;
using rptr.Services.Authorization;
using rptr.Services.Snippets;

namespace rptr.Controllers;

[ApiController]
[Authorize]
[Route("api/snippet")]
public sealed class SnippetController : ControllerBase
{
    private readonly ISnippetService _snippetService;

    public SnippetController(ISnippetService snippetService)
    {
        _snippetService = snippetService;
    }

    [HttpPost(Name = "CreateSnippet")]
    [EndpointSummary("Create a snippet for a playlist song")]
    [EndpointDescription(
        "Creates the single shared snippet for a song in a registered playlist. " +
        "Only the playlist owner may create snippets.")]
    [ProducesResponseType<SnippetResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<SnippetResponse>> Create(
        [FromBody] CreateSnippetRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _snippetService.CreateAsync(userId, request, cancellationToken);
        return result.Status == ServiceResultStatus.Success
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : MapFailure(result);
    }

    [HttpPut("{snippetId:int}", Name = "UpdateSnippet")]
    [EndpointSummary("Update a snippet")]
    [EndpointDescription(
        "Replaces the name and timestamp segments of a snippet. Only the Spotify playlist owner may update it.")]
    [ProducesResponseType<SnippetResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<SnippetResponse>> Update(
        int snippetId,
        [FromBody] UpdateSnippetRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _snippetService.UpdateAsync(userId, snippetId, request, cancellationToken);
        return result.Status == ServiceResultStatus.Success
            ? Ok(result.Value)
            : MapFailure(result);
    }

    [HttpDelete("{snippetId:int}", Name = "DeleteSnippet")]
    [EndpointSummary("Delete a snippet")]
    [EndpointDescription("Deletes a snippet. Only the Spotify playlist owner may delete it.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> Delete(int snippetId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _snippetService.DeleteAsync(userId, snippetId, cancellationToken);
        return result.Status == ServiceResultStatus.Success
            ? NoContent()
            : MapFailure(result);
    }

    [HttpGet("playlist/{spotifyPlaylistId}", Name = "GetPlaylistSnippets")]
    [EndpointSummary("Get a playlist's shared snippets")]
    [EndpointDescription(
        "Returns all locally stored snippets for a Spotify playlist. Capability flags are computed for the authenticated user.")]
    [ProducesResponseType<PlaylistSnippetsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<PlaylistSnippetsResponse>> GetForPlaylist(
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return AuthenticationProblem();
        }

        var result = await _snippetService.GetForPlaylistAsync(
            userId,
            spotifyPlaylistId,
            cancellationToken);

        return result.Status == ServiceResultStatus.Success
            ? Ok(result.Value)
            : MapFailure(result);
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
            title: "The requested playlist or snippet was not found."),
        ServiceResultStatus.Conflict => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "A snippet already exists for this playlist song."),
        ServiceResultStatus.UpstreamFailure => Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Spotify could not verify the playlist."),
        _ => Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.")
    };

    private ObjectResult AuthenticationProblem() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "A valid backend user identity is required.");
}
