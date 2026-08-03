namespace rptr.Services.Authorization;

public sealed record SpotifyLoginRedirect(
    string AuthorizationUrl,
    string StateCookieName,
    string StateCookieValue,
    DateTimeOffset ExpiresAt);

public sealed record SpotifyCallbackRequest(
    string? Code,
    string? State,
    string? Error,
    string? StateCookieValue);

public sealed record SpotifyCallbackResult(string RedirectUri);

public sealed record AuthSessionRequest(string Code);

public sealed record AuthSessionResponse(
    string BackendAccessToken,
    DateTime BackendAccessTokenExpiresAt,
    string TokenType,
    string SpotifyAccessToken,
    DateTime SpotifyAccessTokenExpiresAt,
    string SpotifyScopes,
    AuthUserResponse User);

public sealed record SpotifyAccessTokenResponse(
    string SpotifyAccessToken,
    DateTime SpotifyAccessTokenExpiresAt,
    string SpotifyScopes);

public sealed record AuthUserResponse(
    int Id,
    string SpotifyAccountId,
    string AccountName,
    string DisplayName,
    string Country,
    string SpotifyProfileUrl,
    string SpotifyImageUrl,
    string SubscriptionLevel);
