using System.Security.Claims;
using rptr.Services.Authorization;

namespace rptr.Services.GuestSessions;

public enum GuestSessionStatus
{
    Success,
    InvalidEntryCode,
    Occupied,
    Unauthorized,
    SpotifyFailure,
    Unavailable
}

public sealed record GuestSessionResult(
    GuestSessionStatus Status,
    AuthSessionResponse? Session = null,
    int? RetryAfterSeconds = null);

public sealed record GuestSessionAvailabilityResponse(
    bool IsOccupied,
    int? IdleExpiresInSeconds,
    int? AbsoluteExpiresInSeconds);

public sealed record GuestHeartbeatResponse(
    string BackendAccessToken,
    DateTime BackendAccessTokenExpiresAt);

public interface IGuestSessionService
{
    Task<GuestSessionResult> AcquireAsync(
        string? entryCode,
        CancellationToken cancellationToken = default);

    Task<GuestHeartbeatResponse?> HeartbeatAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);

    Task<SpotifyAccessTokenResponse?> GetSpotifyAccessTokenAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
