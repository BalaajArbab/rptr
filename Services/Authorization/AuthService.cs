using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using rptr.Services.Spotify;

namespace rptr.Services.Authorization;

public class AuthService : IAuthService
{
    private const string HandoffCacheKeyPrefix = "spotify-auth-handoff:";
    private static readonly string[] SpotifyScopes =
    [
        "streaming",
        "user-read-private",
        "user-read-playback-state",
        "user-modify-playback-state"
    ];

    private readonly ILogger<AuthService> _logger;
    private readonly DataContext _dbContext;
    private readonly ISpotifyHttpClient _spotifyHttpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly SpotifySettings _spotifySettings;
    private readonly AuthRedirectSettings _authRedirectSettings;
    private readonly IConfiguration _configuration;

    public string StateCookieName => "__Host-rptr-spotify-state";

    public AuthService(
        ILogger<AuthService> logger,
        DataContext dbContext,
        ISpotifyHttpClient spotifyHttpClient,
        IMemoryCache memoryCache,
        IOptions<SpotifySettings> spotifySettings,
        IOptions<AuthRedirectSettings> authRedirectSettings,
        IConfiguration configuration)
    {
        _logger = logger;
        _dbContext = dbContext;
        _spotifyHttpClient = spotifyHttpClient;
        _memoryCache = memoryCache;
        _spotifySettings = spotifySettings.Value;
        _authRedirectSettings = authRedirectSettings.Value;
        _configuration = configuration;
    }

    public SpotifyLoginRedirect CreateLoginRedirect()
    {
        EnsureSpotifySettings();

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var state = GenerateToken();
        var authorizationUrl = QueryHelpers.AddQueryString(
            "https://accounts.spotify.com/authorize",
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = _spotifySettings.ClientId,
                ["scope"] = string.Join(' ', SpotifyScopes),
                ["redirect_uri"] = _spotifySettings.RedirectUri,
                ["state"] = state
            });

        _logger.LogInformation(
            "Created Spotify authorization redirect. RedirectUri: {RedirectUri}. Scopes: {Scopes}. StateExpiresAt: {StateExpiresAt}.",
            _spotifySettings.RedirectUri,
            string.Join(' ', SpotifyScopes),
            expiresAt);

        return new SpotifyLoginRedirect(authorizationUrl, StateCookieName, state, expiresAt);
    }

    public async Task<SpotifyCallbackResult> HandleCallbackAsync(
        SpotifyCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.Error))
        {
            _logger.LogWarning("Spotify authorization callback returned an error: {SpotifyError}.", request.Error);
            return FailureRedirect(request.Error);
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            _logger.LogWarning("Spotify authorization callback did not include an authorization code.");
            return FailureRedirect("missing_code");
        }

        if (!IsValidState(request.State, request.StateCookieValue))
        {
            _logger.LogWarning(
                "Spotify authorization callback failed state validation. HasState: {HasState}. HasStateCookie: {HasStateCookie}.",
                !string.IsNullOrWhiteSpace(request.State),
                !string.IsNullOrWhiteSpace(request.StateCookieValue));
            return FailureRedirect("invalid_state");
        }

        try
        {
            _logger.LogInformation("Spotify authorization callback state validated. Exchanging authorization code.");

            var tokenResponse = await ExchangeCodeAsync(request.Code, cancellationToken);
            var profile = await GetCurrentUserProfileAsync(tokenResponse.AccessToken, cancellationToken);
            var user = await UpsertUserAsync(profile, tokenResponse, cancellationToken);
            var handoffCode = GenerateToken();

            _memoryCache.Set(
                GetHandoffCacheKey(handoffCode),
                new HandoffCacheEntry(user.Id),
                TimeSpan.FromMinutes(2));

            _logger.LogInformation(
                "Spotify authorization completed for user {UserId}. Handoff code cached for frontend exchange.",
                user.Id);

            return new SpotifyCallbackResult(QueryHelpers.AddQueryString(
                _authRedirectSettings.SuccessUri,
                "code",
                handoffCode));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify authorization callback failed.");
            return FailureRedirect("spotify_authorization_failed");
        }
    }

    public async Task<AuthSessionResponse?> CreateSessionAsync(
        string handoffCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handoffCode))
        {
            _logger.LogWarning("Auth session exchange failed because the handoff code was missing.");
            return null;
        }

        var cacheKey = GetHandoffCacheKey(handoffCode);
        if (!_memoryCache.TryGetValue(cacheKey, out HandoffCacheEntry? handoff) || handoff is null)
        {
            _logger.LogWarning("Auth session exchange failed because the handoff code was invalid or expired.");
            return null;
        }

        _memoryCache.Remove(cacheKey);
        _logger.LogInformation("Auth handoff code consumed for user {UserId}.", handoff.UserId);

        var user = await _dbContext.Users
            .Include(u => u.SpotifyToken)
            .SingleOrDefaultAsync(u => u.Id == handoff.UserId, cancellationToken);

        if (user?.SpotifyToken is null)
        {
            _logger.LogWarning(
                "Auth session exchange failed because user {UserId} or their Spotify token was not found.",
                handoff.UserId);
            return null;
        }

        await RefreshSpotifyTokenIfNeededAsync(user.SpotifyToken, cancellationToken);

        var jwt = CreateAppJwt(user);
        _logger.LogInformation("Created backend auth session for user {UserId}.", user.Id);

        return new AuthSessionResponse(
            jwt.AccessToken,
            jwt.ExpiresAt,
            "Bearer",
            user.SpotifyToken.AccessToken,
            user.SpotifyToken.ExpiresAt,
            user.SpotifyToken.Scope,
            MapUser(user));
    }

    public async Task<SpotifyAccessTokenResponse?> GetSpotifyAccessTokenAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(userIdValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
        {
            _logger.LogWarning("Spotify access token request failed because the authenticated principal had no valid user id claim.");
            return null;
        }

        var token = await _dbContext.SpotifyTokens
            .SingleOrDefaultAsync(t => t.UserId == userId, cancellationToken);

        if (token is null)
        {
            _logger.LogWarning("Spotify access token request failed because no token row exists for user {UserId}.", userId);
            return null;
        }

        await RefreshSpotifyTokenIfNeededAsync(token, cancellationToken);
        _logger.LogInformation("Returned Spotify access token for user {UserId}.", userId);

        return new SpotifyAccessTokenResponse(
            token.AccessToken,
            token.ExpiresAt,
            token.Scope);
    }

    private async Task<SpotifyTokenResponse> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        EnsureSpotifySettings();

        _logger.LogInformation("Exchanging Spotify authorization code for tokens.");

        using var response = await _spotifyHttpClient.SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["code"] = code,
                        ["redirect_uri"] = _spotifySettings.RedirectUri
                    })
                };

                request.Headers.Authorization = CreateSpotifyBasicAuthHeader();
                return request;
            },
            cancellationToken);

        var tokenResponse = await ReadSpotifyTokenResponseAsync(response, cancellationToken);
        _logger.LogInformation(
            "Spotify authorization code exchange succeeded. Access token expires in {ExpiresInSeconds} seconds. Scope: {Scope}.",
            tokenResponse.ExpiresIn,
            tokenResponse.Scope);

        return tokenResponse;
    }

    private async Task RefreshSpotifyTokenIfNeededAsync(
        SpotifyToken token,
        CancellationToken cancellationToken)
    {
        if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(2))
        {
            _logger.LogDebug(
                "Spotify access token for user {UserId} is still valid until {ExpiresAt}.",
                token.UserId,
                token.ExpiresAt);
            return;
        }

        EnsureSpotifySettings();

        _logger.LogInformation("Refreshing Spotify access token for user {UserId}.", token.UserId);

        using var response = await _spotifyHttpClient.SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = token.RefreshToken
                    })
                };

                request.Headers.Authorization = CreateSpotifyBasicAuthHeader();
                return request;
            },
            cancellationToken);

        var refreshedToken = await ReadSpotifyTokenResponseAsync(response, cancellationToken);
        token.AccessToken = refreshedToken.AccessToken;
        token.ExpiresAt = DateTime.UtcNow.AddSeconds(refreshedToken.ExpiresIn);

        if (!string.IsNullOrWhiteSpace(refreshedToken.RefreshToken))
        {
            token.RefreshToken = refreshedToken.RefreshToken;
        }

        if (!string.IsNullOrWhiteSpace(refreshedToken.Scope))
        {
            token.Scope = refreshedToken.Scope;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Spotify access token refreshed for user {UserId}. New expiry: {ExpiresAt}.",
            token.UserId,
            token.ExpiresAt);
    }

    private async Task<SpotifyCurrentUserProfile> GetCurrentUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching current Spotify user profile.");

        using var response = await _spotifyHttpClient.SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return request;
            },
            cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var profile = await JsonSerializer.DeserializeAsync<SpotifyCurrentUserProfile>(
            stream,
            JsonSerializerOptions.Web,
            cancellationToken);

        if (profile is null || string.IsNullOrWhiteSpace(profile.AccountId))
        {
            throw new InvalidOperationException("Spotify profile response did not include account_id.");
        }

        _logger.LogInformation("Fetched Spotify user profile for account {SpotifyAccountId}.", profile.AccountId);

        return profile;
    }

    private async Task<User> UpsertUserAsync(
        SpotifyCurrentUserProfile profile,
        SpotifyTokenResponse tokenResponse,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .Include(u => u.SpotifyToken)
            .SingleOrDefaultAsync(u => u.SpotifyAccountId == profile.AccountId, cancellationToken);

        var isNewUser = user is null;
        var isNewToken = user?.SpotifyToken is null;

        if (user is null)
        {
            user = new User
            {
                SpotifyAccountId = profile.AccountId,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Users.Add(user);
        }

        user.Country = profile.Country ?? string.Empty;
        user.DisplayName = profile.DisplayName ?? string.Empty;
        user.SpotifyProfileUrl = profile.ExternalUrls?.Spotify ?? string.Empty;
        user.SpotifyImageUrl = profile.Images?.FirstOrDefault()?.Url ?? string.Empty;
        user.SubscriptionLevel = profile.Product ?? string.Empty;

        var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        if (user.SpotifyToken is null)
        {
            user.SpotifyToken = new SpotifyToken
            {
                User = user,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? string.Empty,
                ExpiresAt = expiresAt,
                Scope = tokenResponse.Scope ?? string.Join(' ', SpotifyScopes)
            };
        }
        else
        {
            user.SpotifyToken.AccessToken = tokenResponse.AccessToken;
            user.SpotifyToken.ExpiresAt = expiresAt;
            user.SpotifyToken.Scope = tokenResponse.Scope ?? user.SpotifyToken.Scope;

            if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            {
                user.SpotifyToken.RefreshToken = tokenResponse.RefreshToken;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Persisted Spotify auth data for user {UserId}. SpotifyAccountId: {SpotifyAccountId}. CreatedUser: {CreatedUser}. CreatedToken: {CreatedToken}.",
            user.Id,
            user.SpotifyAccountId,
            isNewUser,
            isNewToken);

        return user;
    }

    private async Task<SpotifyTokenResponse> ReadSpotifyTokenResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var tokenResponse = await JsonSerializer.DeserializeAsync<SpotifyTokenResponse>(
            stream,
            JsonSerializerOptions.Web,
            cancellationToken);

        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("Spotify token response did not include access_token.");
        }

        return tokenResponse;
    }

    private AuthenticationHeaderValue CreateSpotifyBasicAuthHeader()
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_spotifySettings.ClientId}:{_spotifySettings.ClientSecret}"));

        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private bool IsValidState(string? state, string? cookieState)
    {
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(cookieState))
        {
            return false;
        }

        return string.Equals(state, cookieState, StringComparison.Ordinal);
    }

    private SpotifyCallbackResult FailureRedirect(string error)
    {
        return new SpotifyCallbackResult(QueryHelpers.AddQueryString(
            _authRedirectSettings.FailureUri,
            "error",
            error));
    }

    private JwtResult CreateAppJwt(User user)
    {
        var signingToken = _configuration.GetSection("AppSettings:Token").Value;
        if (string.IsNullOrWhiteSpace(signingToken))
        {
            throw new InvalidOperationException("AppSettings:Token is not configured.");
        }

        var expiresAt = DateTime.UtcNow.AddMonths(1);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new("spotify_account_id", user.SpotifyAccountId)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingToken));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private void EnsureSpotifySettings()
    {
        if (string.IsNullOrWhiteSpace(_spotifySettings.ClientId)
            || string.IsNullOrWhiteSpace(_spotifySettings.ClientSecret)
            || string.IsNullOrWhiteSpace(_spotifySettings.RedirectUri))
        {
            throw new InvalidOperationException("SpotifySettings ClientId, ClientSecret, and RedirectUri are required.");
        }
    }

    private static AuthUserResponse MapUser(User user)
    {
        return new AuthUserResponse(
            user.Id,
            user.SpotifyAccountId,
            user.DisplayName,
            user.Country,
            user.SpotifyProfileUrl,
            user.SpotifyImageUrl,
            user.SubscriptionLevel);
    }

    private static string GetHandoffCacheKey(string code) => $"{HandoffCacheKeyPrefix}{code}";

    private static string GenerateToken() => Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(16));

    private sealed record HandoffCacheEntry(int UserId);

    private sealed record JwtResult(string AccessToken, DateTime ExpiresAt);

    private sealed class SpotifyTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class SpotifyCurrentUserProfile
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; } = string.Empty;

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("external_urls")]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        [JsonPropertyName("images")]
        public List<SpotifyImage>? Images { get; set; }

        [JsonPropertyName("product")]
        public string? Product { get; set; }
    }

    private sealed class SpotifyExternalUrls
    {
        [JsonPropertyName("spotify")]
        public string? Spotify { get; set; }
    }

    private sealed class SpotifyImage
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
