namespace rptr.Services.GuestSessions;

public sealed class LazyGuestSessionCounter : IGuestSessionCounter
{
    private readonly IServiceProvider _services;

    public LazyGuestSessionCounter(IServiceProvider services)
    {
        _services = services;
    }

    public Task<long> GetNextAsync(CancellationToken cancellationToken = default) =>
        _services
            .GetRequiredService<MongoGuestSessionCounter>()
            .GetNextAsync(cancellationToken);
}
