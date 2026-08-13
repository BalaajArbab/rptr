using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using rptr.Services.Authorization;

namespace rptr.Services.GuestSessions;

public sealed class GuestSessionService : IGuestSessionService
{
    private readonly IGuestLeaseStore _leaseStore;
    private readonly IGuestRuntimeStore _runtimeStore;
    private readonly IGuestCredentialStore _credentialStore;
    private readonly IGuestSpotifyTokenClient _spotifyTokenClient;
    private readonly IGuestFeedbackTokenService _guestFeedbackTokenService;
    private readonly GuestSessionSettings _settings;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GuestSessionService> _logger;

    public GuestSessionService(
        IGuestLeaseStore leaseStore,
        IGuestRuntimeStore runtimeStore,
        IGuestCredentialStore credentialStore,
        IGuestSpotifyTokenClient spotifyTokenClient,
        IGuestFeedbackTokenService guestFeedbackTokenService,
        IOptions<GuestSessionSettings> settings,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<GuestSessionService> logger)
    {
        _leaseStore = leaseStore;
        _runtimeStore = runtimeStore;
        _credentialStore = credentialStore;
        _spotifyTokenClient = spotifyTokenClient;
        _guestFeedbackTokenService = guestFeedbackTokenService;
        _settings = settings.Value;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GuestSessionResult> AcquireAsync(
        string? entryCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.EntryCode))
        {
            _logger.LogError("Guest session entry code is not configured.");
            return new GuestSessionResult(GuestSessionStatus.Unavailable);
        }

        if (!FixedTimeEquals(entryCode ?? string.Empty, _settings.EntryCode))
        {
            _logger.LogWarning("A guest session request supplied an invalid entry code.");
            return new GuestSessionResult(GuestSessionStatus.InvalidEntryCode);
        }

        GuestLeaseAcquireResult acquireResult;
        try
        {
            acquireResult = await _leaseStore.TryAcquireAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Guest lease acquisition failed.");
            return new GuestSessionResult(GuestSessionStatus.Unavailable);
        }

        if (!acquireResult.Acquired || acquireResult.Lease is null)
        {
            var activeLease = await _leaseStore.GetCurrentAsync(cancellationToken);
            return new GuestSessionResult(
                GuestSessionStatus.Occupied,
                RetryAfterSeconds: activeLease is null
                    ? 1
                    : GetRetryAfterSeconds(activeLease));
        }

        var lease = acquireResult.Lease;
        try
        {
            GuestCredential? credential;
            try
            {
                credential = await _credentialStore.GetAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not load the guest Spotify credential from MongoDB.");
                return await FailInitializationAsync(lease, GuestSessionStatus.Unavailable);
            }

            if (credential is null
                || string.IsNullOrWhiteSpace(credential.RefreshToken)
                || credential.RefreshTokenExpiresAt is not DateTime refreshTokenExpiresAt
                || refreshTokenExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime
                || !SpotifyScopeRequirements.ContainsAll(credential.Scopes))
            {
                _logger.LogError("The provisioned guest Spotify credential is missing, expired, or lacks required scopes.");
                return await FailInitializationAsync(lease, GuestSessionStatus.Unavailable);
            }

            GuestSpotifyToken spotifyToken;
            try
            {
                spotifyToken = await _spotifyTokenClient.RefreshAsync(
                    credential.RefreshToken,
                    credential.Scopes,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Spotify rejected or failed the guest token refresh.");
                return await FailInitializationAsync(lease, GuestSessionStatus.SpotifyFailure);
            }

            if (!string.IsNullOrWhiteSpace(spotifyToken.RotatedRefreshToken))
            {
                try
                {
                    await _credentialStore.UpsertAsync(
                        credential with
                        {
                            Scopes = spotifyToken.Scopes,
                            RefreshToken = spotifyToken.RotatedRefreshToken
                        },
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Could not persist the rotated guest refresh token.");
                    return await FailInitializationAsync(lease, GuestSessionStatus.Unavailable);
                }
            }

            GuestSessionRuntime runtime;
            try
            {
                runtime = await _runtimeStore.CreateAsync(
                    lease,
                    credential.SpotifyAccountId,
                    spotifyToken,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not initialize the guest SQLite baseline.");
                return await FailInitializationAsync(lease, GuestSessionStatus.Unavailable);
            }

            _logger.LogInformation(
                "Guest session {GuestSessionNumber} initialized.",
                lease.SessionNumber);
            return new GuestSessionResult(
                GuestSessionStatus.Success,
                CreateSessionResponse(runtime));
        }
        catch (Exception exception)
        {
            await _runtimeStore.RemoveAsync(lease.SessionKey, CancellationToken.None);
            await _leaseStore.ReleaseAsync(lease.SessionKey, CancellationToken.None);
            _logger.LogError(exception, "Guest session initialization failed unexpectedly.");
            return new GuestSessionResult(GuestSessionStatus.Unavailable);
        }
    }

    public async Task<GuestHeartbeatResponse?> HeartbeatAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!principal.TryGetGuestSessionKey(out var sessionKey))
        {
            return null;
        }

        var lease = await _leaseStore.RenewAsync(sessionKey, cancellationToken);
        var runtime = await _runtimeStore.GetAsync(sessionKey, cancellationToken);
        if (lease is null || runtime is null)
        {
            return null;
        }

        await _runtimeStore.UpdateLeaseAsync(lease, cancellationToken);
        var jwt = CreateJwt(runtime);
        return new GuestHeartbeatResponse(
            jwt.Token,
            jwt.ExpiresAt.UtcDateTime);
    }

    public async Task<bool> ReleaseAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!principal.TryGetGuestSessionKey(out var sessionKey))
        {
            return false;
        }

        var released = await _leaseStore.ReleaseAsync(sessionKey, cancellationToken);
        await _runtimeStore.RemoveAsync(sessionKey, cancellationToken);
        return released;
    }

    public async Task<bool> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!principal.TryGetGuestSessionKey(out var sessionKey)
            || !principal.TryGetGuestSessionNumber(out var sessionNumber))
        {
            return false;
        }

        var lease = await _leaseStore.GetActiveAsync(sessionKey, cancellationToken);
        var runtime = await _runtimeStore.GetAsync(sessionKey, cancellationToken);
        return lease is not null
            && runtime is not null
            && lease.SessionNumber == sessionNumber
            && runtime.Lease.SessionNumber == sessionNumber;
    }

    public async Task<SpotifyAccessTokenResponse?> GetSpotifyAccessTokenAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!principal.TryGetGuestSessionKey(out var sessionKey))
        {
            return null;
        }

        var runtime = await _runtimeStore.GetAsync(sessionKey, cancellationToken);
        return runtime is null
            ? null
            : new SpotifyAccessTokenResponse(
                runtime.SpotifyToken.AccessToken,
                runtime.SpotifyToken.ExpiresAt,
                runtime.SpotifyToken.Scopes);
    }

    private AuthSessionResponse CreateSessionResponse(GuestSessionRuntime runtime)
    {
        var jwt = CreateJwt(runtime);
        var feedbackToken = _guestFeedbackTokenService.Create(runtime.Lease);
        return new AuthSessionResponse(
            jwt.Token,
            jwt.ExpiresAt.UtcDateTime,
            "Bearer",
            runtime.SpotifyToken.AccessToken,
            runtime.SpotifyToken.ExpiresAt,
            runtime.SpotifyToken.Scopes,
            true,
            runtime.User,
            feedbackToken.Token,
            feedbackToken.ExpiresAt.UtcDateTime);
    }

    private (string Token, DateTimeOffset ExpiresAt) CreateJwt(
        GuestSessionRuntime runtime)
    {
        var signingToken = _configuration["AppSettings:Token"];
        if (string.IsNullOrWhiteSpace(signingToken))
        {
            throw new InvalidOperationException("AppSettings:Token is not configured.");
        }

        var expiresAt = runtime.Lease.IdleExpiresAt <= runtime.Lease.AbsoluteExpiresAt
            ? runtime.Lease.IdleExpiresAt
            : runtime.Lease.AbsoluteExpiresAt;
        var userId = runtime.User.Id.ToString(CultureInfo.InvariantCulture);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(JwtRegisteredClaimNames.Sub, userId),
            new("spotify_account_id", runtime.User.SpotifyAccountId),
            new(ClaimsPrincipalExtensions.SessionTypeClaim, ClaimsPrincipalExtensions.GuestSessionType),
            new(ClaimsPrincipalExtensions.GuestSessionKeyClaim, runtime.Lease.SessionKey.ToString("D")),
            new(ClaimsPrincipalExtensions.GuestSessionNumberClaim, runtime.Lease.SessionNumber.ToString(CultureInfo.InvariantCulture))
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingToken)),
            SecurityAlgorithms.HmacSha512Signature);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private async Task<GuestSessionResult> FailInitializationAsync(
        GuestLease lease,
        GuestSessionStatus status)
    {
        await _runtimeStore.RemoveAsync(lease.SessionKey, CancellationToken.None);
        await _leaseStore.ReleaseAsync(lease.SessionKey, CancellationToken.None);
        return new GuestSessionResult(status);
    }

    private int GetRetryAfterSeconds(GuestLease activeLease)
    {
        return Math.Max(
            1,
            (int)Math.Ceiling(
                (activeLease.IdleExpiresAt - _timeProvider.GetUtcNow()).TotalSeconds));
    }

    private static bool FixedTimeEquals(string supplied, string expected)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
}
