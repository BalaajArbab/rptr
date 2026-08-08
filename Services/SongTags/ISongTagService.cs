using rptr.Services;

namespace rptr.Services.SongTags;

public interface ISongTagService
{
    Task<ServiceResult<IReadOnlyList<SongTagsResponse>>> GetForPlaylistAsync(
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SongTagsResponse?>> PutAsync(
        int userId,
        string spotifyPlaylistId,
        string songId,
        PutSongTagsRequest request,
        CancellationToken cancellationToken = default);
}
