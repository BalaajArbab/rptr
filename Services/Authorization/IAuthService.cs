using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace rptr.Services.Authorization;

public interface IAuthService
{
    string StateCookieName { get; }
    SpotifyLoginRedirect CreateLoginRedirect();
    Task<SpotifyCallbackResult> HandleCallbackAsync(SpotifyCallbackRequest request, CancellationToken cancellationToken = default);
    Task<AuthSessionResponse?> CreateSessionAsync(string handoffCode, CancellationToken cancellationToken = default);
    Task<SpotifyAccessTokenResponse?> GetSpotifyAccessTokenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<SpotifyAccessTokenResponse?> GetSpotifyAccessTokenAsync(int userId, CancellationToken cancellationToken = default);
}
