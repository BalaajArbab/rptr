using System.Net;
using System.Text.Json;

namespace rptr.Services.Spotify;

public sealed class SpotifyHttpClient : ISpotifyHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpotifyHttpClient> _logger;

    public SpotifyHttpClient(
        IHttpClientFactory httpClientFactory,
        ILogger<SpotifyHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = requestFactory();
            var endpoint = request.RequestUri?.GetLeftPart(UriPartial.Path) ?? "unknown Spotify endpoint";
            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.TooManyRequests
                && (int)response.StatusCode < 500)
            {
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "Spotify request succeeded. Method: {Method}. Endpoint: {Endpoint}. StatusCode: {StatusCode}.",
                        request.Method,
                        endpoint,
                        (int)response.StatusCode);
                    return response;
                }

                var error = await ReadSpotifyErrorAsync(response, cancellationToken);
                _logger.LogWarning(
                    "Spotify request failed without retry. Method: {Method}. Endpoint: {Endpoint}. StatusCode: {StatusCode}. Error: {Error}.",
                    request.Method,
                    endpoint,
                    (int)response.StatusCode,
                    error);
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new SpotifyApiException(statusCode, error);
            }

            if (attempt == 3)
            {
                var error = await ReadSpotifyErrorAsync(response, cancellationToken);
                _logger.LogWarning(
                    "Spotify request failed after retries. Method: {Method}. Endpoint: {Endpoint}. StatusCode: {StatusCode}. Error: {Error}.",
                    request.Method,
                    endpoint,
                    (int)response.StatusCode,
                    error);
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new SpotifyApiException(statusCode, error);
            }

            var delay = GetRetryDelay(response, attempt);
            _logger.LogWarning(
                "Spotify request will be retried. Method: {Method}. Endpoint: {Endpoint}. StatusCode: {StatusCode}. Attempt: {Attempt}. Delay: {Delay}.",
                request.Method,
                endpoint,
                (int)response.StatusCode,
                attempt + 1,
                delay);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        throw new InvalidOperationException("Spotify request failed.");
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
    }

    private static async Task<string> ReadSpotifyErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Spotify request failed with HTTP {(int)response.StatusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("error_description", out var description))
            {
                return description.GetString() ?? body;
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? body;
                }

                if (error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? body;
                }
            }
        }
        catch (JsonException)
        {
            return body;
        }

        return body;
    }
}
