using System.Text.Json.Serialization;

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

public sealed record GuestSessionRequest(string EntryCode);

public sealed record AuthSessionResponse(
    string BackendAccessToken,
    DateTime BackendAccessTokenExpiresAt,
    string TokenType,
    string SpotifyAccessToken,
    DateTime SpotifyAccessTokenExpiresAt,
    string SpotifyScopes,
    bool IsGuest,
    AuthUserResponse User,
    string? GuestFeedbackToken = null,
    DateTime? GuestFeedbackTokenExpiresAt = null);

public sealed record SpotifyAccessTokenResponse(
    string SpotifyAccessToken,
    DateTime SpotifyAccessTokenExpiresAt,
    string SpotifyScopes);

public sealed record AuthUserResponse(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)]
    int Id,
    string SpotifyAccountId,
    string AccountName,
    string DisplayName,
    string Country,
    string SpotifyProfileUrl,
    string SpotifyImageUrl,
    string SubscriptionLevel);
