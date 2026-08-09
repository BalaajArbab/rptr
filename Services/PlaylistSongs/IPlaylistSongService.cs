using rptr.Services;

namespace rptr.Services.PlaylistSongs;

public interface IPlaylistSongService
{
    Task<ServiceResult<PlaylistSongsResponse>> GetForPlaylistAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SongTagsResponse?>> PutTagsAsync(
        int userId,
        string spotifyPlaylistId,
        string spotifySongId,
        PutSongTagsRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SongExcludedResponse>> PutExcludedAsync(
        int userId,
        string spotifyPlaylistId,
        string spotifySongId,
        PutSongExcludedRequest request,
        CancellationToken cancellationToken = default);
}
