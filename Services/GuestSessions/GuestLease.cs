namespace rptr.Services.GuestSessions;

public sealed record GuestLease(
    long SessionNumber,
    Guid SessionKey,
    DateTimeOffset IssuedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt);

public sealed record GuestLeaseAcquireResult(
    bool Acquired,
    GuestLease? Lease)
{
    public static GuestLeaseAcquireResult Occupied() => new(false, null);

    public static GuestLeaseAcquireResult Success(GuestLease lease) => new(true, lease);
}
