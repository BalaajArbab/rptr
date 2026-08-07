using rptr.Services;

namespace rptr.Services.Playlists;

public interface IPlaylistService
{
    Task<ServiceResult<PlaylistAccessResponse>> AccessAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaylistTagDto>> GetTagsAsync(
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PlaylistTagDto>>> UpdateTagsAsync(
        int userId,
        string spotifyPlaylistId,
        IReadOnlyList<PlaylistTagDto>? tags,
        CancellationToken cancellationToken = default);
}
