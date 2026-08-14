using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace rptr.Services.KeepWarm;

public sealed class GitHubWorkflowDispatchService : BackgroundService
{
    public const string HttpClientName = "GitHubWorkflowDispatch";

    private const int MaximumAttempts = 4;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KeepWarmDispatchSettings _settings;
    private readonly ILogger<GitHubWorkflowDispatchService> _logger;

    public GitHubWorkflowDispatchService(
        IHttpClientFactory httpClientFactory,
        IOptions<KeepWarmDispatchSettings> settings,
        ILogger<GitHubWorkflowDispatchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("GitHub keep-warm dispatch is disabled.");
            return;
        }

        _logger.LogInformation(
            "GitHub keep-warm dispatch will run every {IntervalMinutes} minutes.",
            _settings.IntervalMinutes);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(_settings.IntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "GitHub keep-warm workflow dispatch failed.");
            }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var endpoint = BuildDispatchEndpoint();

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _settings.Token);
            request.Content = JsonContent.Create(new { @ref = _settings.Ref });

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaximumAttempts - 1)
            {
                var delay = GetFallbackDelay(attempt);
                _logger.LogWarning(
                    "GitHub workflow dispatch encountered a network error and will retry. Attempt: {Attempt}. Delay: {Delay}.",
                    attempt + 1,
                    delay);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "GitHub keep-warm workflow dispatch was accepted. StatusCode: {StatusCode}.",
                        (int)response.StatusCode);
                    return;
                }

                var canRetry = response.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                if (!canRetry || attempt == MaximumAttempts - 1)
                {
                    throw new HttpRequestException(
                        $"GitHub workflow dispatch returned HTTP {(int)response.StatusCode}.",
                        inner: null,
                        response.StatusCode);
                }

                var retryDelay = GetRetryDelay(response, attempt);
                _logger.LogWarning(
                    "GitHub workflow dispatch will retry. StatusCode: {StatusCode}. Attempt: {Attempt}. Delay: {Delay}.",
                    (int)response.StatusCode,
                    attempt + 1,
                    retryDelay);
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    private string BuildDispatchEndpoint() =>
        $"repos/{Uri.EscapeDataString(_settings.Owner)}/" +
        $"{Uri.EscapeDataString(_settings.Repository)}/actions/workflows/" +
        $"{Uri.EscapeDataString(_settings.WorkflowFile)}/dispatches";

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
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

        return GetFallbackDelay(attempt);
    }

    private static TimeSpan GetFallbackDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
}
