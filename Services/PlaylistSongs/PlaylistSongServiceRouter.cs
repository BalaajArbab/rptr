using rptr.Services.Authorization;
using rptr.Services.GuestSessions;

namespace rptr.Services.PlaylistSongs;

public sealed class PlaylistSongServiceRouter : IPlaylistSongService
{
    private readonly PlaylistSongService _normalService;
    private readonly IGuestRuntimeStore _runtimeStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PlaylistSongService> _serviceLogger;

    public PlaylistSongServiceRouter(
        PlaylistSongService normalService,
        IGuestRuntimeStore runtimeStore,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PlaylistSongService> serviceLogger)
    {
        _normalService = normalService;
        _runtimeStore = runtimeStore;
        _httpContextAccessor = httpContextAccessor;
        _serviceLogger = serviceLogger;
    }

    public Task<ServiceResult<PlaylistSongsResponse>> GetForPlaylistAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default) =>
        RouteAsync(
            service => service.GetForPlaylistAsync(userId, spotifyPlaylistId, cancellationToken),
            () => _normalService.GetForPlaylistAsync(userId, spotifyPlaylistId, cancellationToken),
            cancellationToken);

    public Task<ServiceResult<SongTagsResponse?>> PutTagsAsync(
        int userId,
        string spotifyPlaylistId,
        string spotifySongId,
        PutSongTagsRequest request,
        CancellationToken cancellationToken = default) =>
        RouteAsync(
            service => service.PutTagsAsync(
                userId,
                spotifyPlaylistId,
                spotifySongId,
                request,
                cancellationToken),
            () => _normalService.PutTagsAsync(
                userId,
                spotifyPlaylistId,
                spotifySongId,
                request,
                cancellationToken),
            cancellationToken);

    public Task<ServiceResult<SongExcludedResponse>> PutExcludedAsync(
        int userId,
        string spotifyPlaylistId,
        string spotifySongId,
        PutSongExcludedRequest request,
        CancellationToken cancellationToken = default) =>
        RouteAsync(
            service => service.PutExcludedAsync(
                userId,
                spotifyPlaylistId,
                spotifySongId,
                request,
                cancellationToken),
            () => _normalService.PutExcludedAsync(
                userId,
                spotifyPlaylistId,
                spotifySongId,
                request,
                cancellationToken),
            cancellationToken);

    private Task<T> RouteAsync<T>(
        Func<PlaylistSongService, Task<T>> guestOperation,
        Func<Task<T>> normalOperation,
        CancellationToken cancellationToken)
    {
        if (_httpContextAccessor.HttpContext?.User.TryGetGuestSessionKey(out var sessionKey) != true)
        {
            return normalOperation();
        }

        return _runtimeStore.ExecuteAsync(
            sessionKey,
            context => guestOperation(new PlaylistSongService(context, _serviceLogger)),
            cancellationToken);
    }
}
