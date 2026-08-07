using rptr.Services;

namespace rptr.Services.Snippets;

public interface ISnippetService
{
    Task<ServiceResult<SnippetResponse>> CreateAsync(
        int userId,
        CreateSnippetRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SnippetResponse>> UpdateAsync(
        int userId,
        int snippetId,
        UpdateSnippetRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteAsync(
        int userId,
        int snippetId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PlaylistSnippetsResponse>> GetForPlaylistAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default);
}
