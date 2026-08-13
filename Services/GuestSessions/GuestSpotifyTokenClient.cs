using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using rptr.Services.Authorization;
using rptr.Services.Spotify;

namespace rptr.Services.GuestSessions;

public sealed record GuestSpotifyToken(
    string AccessToken,
    DateTime ExpiresAt,
    string Scopes,
    string? RotatedRefreshToken);

public interface IGuestSpotifyTokenClient
{
    Task<GuestSpotifyToken> RefreshAsync(
        string refreshToken,
        string existingScopes,
        CancellationToken cancellationToken = default);
}

public sealed class GuestSpotifyTokenClient : IGuestSpotifyTokenClient
{
    private readonly ISpotifyHttpClient _spotifyHttpClient;
    private readonly SpotifySettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GuestSpotifyTokenClient> _logger;

    public GuestSpotifyTokenClient(
        ISpotifyHttpClient spotifyHttpClient,
        IOptions<SpotifySettings> settings,
        TimeProvider timeProvider,
        ILogger<GuestSpotifyTokenClient> logger)
    {
        _spotifyHttpClient = spotifyHttpClient;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GuestSpotifyToken> RefreshAsync(
        string refreshToken,
        string existingScopes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId)
            || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "SpotifySettings ClientId and ClientSecret are required.");
        }

        using var response = await _spotifyHttpClient.SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://accounts.spotify.com/api/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = refreshToken
                    })
                };

                var basicValue = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicValue);
                return request;
            },
            cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<RefreshTokenPayload>(
            stream,
            JsonSerializerOptions.Web,
            cancellationToken);

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.AccessToken)
            || payload.ExpiresIn <= 0)
        {
            throw new InvalidOperationException(
                "Spotify refresh response did not include access_token.");
        }

        var scopes = string.IsNullOrWhiteSpace(payload.Scope)
            ? existingScopes
            : payload.Scope;

        _logger.LogInformation(
            "Refreshed the guest Spotify credential. Access token expires in {ExpiresInSeconds} seconds.",
            payload.ExpiresIn);

        return new GuestSpotifyToken(
            payload.AccessToken,
            _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(payload.ExpiresIn),
            scopes,
            payload.RefreshToken);
    }

    private sealed class RefreshTokenPayload
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }
}
