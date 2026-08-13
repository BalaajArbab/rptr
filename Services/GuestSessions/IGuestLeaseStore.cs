namespace rptr.Services.GuestSessions;

public interface IGuestLeaseStore
{
    Task<GuestLeaseAcquireResult> TryAcquireAsync(
        CancellationToken cancellationToken = default);

    Task<GuestLease?> RenewAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default);

    Task<GuestLease?> GetActiveAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default);

    Task<GuestLease?> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default);
}
