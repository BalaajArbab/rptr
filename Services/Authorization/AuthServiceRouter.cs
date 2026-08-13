using System.Security.Claims;
using rptr.Services.GuestSessions;

namespace rptr.Services.Authorization;

public sealed class AuthServiceRouter : IAuthService
{
    private readonly AuthService _normalService;
    private readonly IGuestSessionService _guestSessionService;

    public AuthServiceRouter(
        AuthService normalService,
        IGuestSessionService guestSessionService)
    {
        _normalService = normalService;
        _guestSessionService = guestSessionService;
    }

    public string StateCookieName => _normalService.StateCookieName;

    public SpotifyLoginRedirect CreateLoginRedirect() =>
        _normalService.CreateLoginRedirect();

    public Task<SpotifyCallbackResult> HandleCallbackAsync(
        SpotifyCallbackRequest request,
        CancellationToken cancellationToken = default) =>
        _normalService.HandleCallbackAsync(request, cancellationToken);

    public Task<AuthSessionResponse?> CreateSessionAsync(
        string handoffCode,
        CancellationToken cancellationToken = default) =>
        _normalService.CreateSessionAsync(handoffCode, cancellationToken);

    public Task<SpotifyAccessTokenResponse?> GetSpotifyAccessTokenAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default) =>
        principal.IsGuest()
            ? _guestSessionService.GetSpotifyAccessTokenAsync(principal, cancellationToken)
            : _normalService.GetSpotifyAccessTokenAsync(principal, cancellationToken);

    public Task<SpotifyAccessTokenResponse?> GetSpotifyAccessTokenAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        _normalService.GetSpotifyAccessTokenAsync(userId, cancellationToken);
}
