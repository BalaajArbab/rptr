using rptr.Services.Authorization;
using rptr.Services.GuestSessions;
using rptr.Services.Spotify;

namespace rptr.Services.Playlists;

public sealed class PlaylistServiceRouter : IPlaylistService
{
    private readonly PlaylistService _normalService;
    private readonly IGuestRuntimeStore _runtimeStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly ISpotifyHttpClient _spotifyHttpClient;
    private readonly ILogger<PlaylistService> _serviceLogger;

    public PlaylistServiceRouter(
        PlaylistService normalService,
        IGuestRuntimeStore runtimeStore,
        IHttpContextAccessor httpContextAccessor,
        IAuthService authService,
        ISpotifyHttpClient spotifyHttpClient,
        ILogger<PlaylistService> serviceLogger)
    {
        _normalService = normalService;
        _runtimeStore = runtimeStore;
        _httpContextAccessor = httpContextAccessor;
        _authService = authService;
        _spotifyHttpClient = spotifyHttpClient;
        _serviceLogger = serviceLogger;
    }

    public Task<ServiceResult<PlaylistAccessResponse>> AccessAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetGuestKey(out var sessionKey))
        {
            return _normalService.AccessAsync(userId, spotifyPlaylistId, cancellationToken);
        }

        return _runtimeStore.ExecuteAsync(
            sessionKey,
            async context =>
            {
                var exists = await context.Playlists.AnyAsync(
                    value => value.SpotifyPlaylistId == spotifyPlaylistId,
                    cancellationToken);
                if (!exists)
                {
                    return ServiceResult<PlaylistAccessResponse>.Failure(
                        ServiceResultStatus.NotFound);
                }

                return await CreateService(context).AccessAsync(
                    userId,
                    spotifyPlaylistId,
                    cancellationToken);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<PlaylistTagDto>> GetTagsAsync(
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetGuestKey(out var sessionKey))
        {
            return _normalService.GetTagsAsync(spotifyPlaylistId, cancellationToken);
        }

        return _runtimeStore.ExecuteAsync(
            sessionKey,
            context => CreateService(context).GetTagsAsync(
                spotifyPlaylistId,
                cancellationToken),
            cancellationToken);
    }

    public Task<ServiceResult<IReadOnlyList<PlaylistTagDto>>> UpdateTagsAsync(
        int userId,
        string spotifyPlaylistId,
        IReadOnlyList<PlaylistTagDto>? tags,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetGuestKey(out var sessionKey))
        {
            return _normalService.UpdateTagsAsync(
                userId,
                spotifyPlaylistId,
                tags,
                cancellationToken);
        }

        return _runtimeStore.ExecuteAsync(
            sessionKey,
            context => CreateService(context).UpdateTagsAsync(
                userId,
                spotifyPlaylistId,
                tags,
                cancellationToken),
            cancellationToken);
    }

    private PlaylistService CreateService(DataContext context) => new(
        context,
        _authService,
        _spotifyHttpClient,
        _serviceLogger);

    private bool TryGetGuestKey(out Guid sessionKey)
    {
        sessionKey = Guid.Empty;
        return _httpContextAccessor.HttpContext?.User.TryGetGuestSessionKey(out sessionKey) == true;
    }
}
