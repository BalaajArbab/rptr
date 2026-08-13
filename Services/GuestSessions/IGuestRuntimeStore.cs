namespace rptr.Services.GuestSessions;

public interface IGuestRuntimeStore
{
    Task<GuestSessionRuntime> CreateAsync(
        GuestLease lease,
        string spotifyAccountId,
        GuestSpotifyToken spotifyToken,
        CancellationToken cancellationToken = default);

    Task<GuestSessionRuntime?> GetAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default);

    Task<GuestSessionRuntime?> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task UpdateLeaseAsync(
        GuestLease lease,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteAsync<T>(
        Guid sessionKey,
        Func<DataContext, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default);
}

public sealed class GuestSessionUnavailableException : Exception
{
    public GuestSessionUnavailableException()
        : base("The guest session is no longer active.")
    {
    }
}
