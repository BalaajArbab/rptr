namespace rptr.Services.GuestSessions;

public sealed class GuestSessionCleanupService : BackgroundService
{
    private readonly IGuestRuntimeStore _runtimeStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GuestSessionCleanupService> _logger;

    public GuestSessionCleanupService(
        IGuestRuntimeStore runtimeStore,
        TimeProvider timeProvider,
        ILogger<GuestSessionCleanupService> logger)
    {
        _runtimeStore = runtimeStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var runtime = await _runtimeStore.GetCurrentAsync(cancellationToken);
        if (runtime is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (runtime.Lease.IdleExpiresAt > now
            && runtime.Lease.AbsoluteExpiresAt > now)
        {
            return;
        }

        await _runtimeStore.RemoveAsync(runtime.Lease.SessionKey, cancellationToken);
        _logger.LogInformation(
            "Cleaned up expired guest session {GuestSessionNumber}.",
            runtime.Lease.SessionNumber);
    }
}
