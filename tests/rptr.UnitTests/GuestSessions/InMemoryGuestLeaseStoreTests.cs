using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using rptr.Services.GuestSessions;

namespace rptr.UnitTests.GuestSessions;

[Trait("Category", "Unit")]
public sealed class InMemoryGuestLeaseStoreTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryAcquireAsync_WhenVacant_CreatesLeaseFromCurrentTime()
    {
        var counter = new StubGuestSessionCounter(41);
        var timeProvider = new ManualTimeProvider(StartTime);
        using var store = CreateStore(counter, timeProvider);

        var result = await store.TryAcquireAsync();

        Assert.True(result.Acquired);
        var lease = Assert.IsType<GuestLease>(result.Lease);
        Assert.Equal(41, lease.SessionNumber);
        Assert.NotEqual(Guid.Empty, lease.SessionKey);
        Assert.Equal(StartTime, lease.IssuedAt);
        Assert.Equal(StartTime, lease.LastActivityAt);
        Assert.Equal(StartTime.AddMinutes(5), lease.IdleExpiresAt);
        Assert.Equal(StartTime.AddMinutes(57), lease.AbsoluteExpiresAt);
        Assert.Equal(1, counter.CallCount);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenLeaseIsActive_ReturnsOccupiedWithoutIncrementingCounter()
    {
        var counter = new StubGuestSessionCounter(10);
        var timeProvider = new ManualTimeProvider(StartTime);
        using var store = CreateStore(counter, timeProvider);
        await store.TryAcquireAsync();

        var result = await store.TryAcquireAsync();

        Assert.False(result.Acquired);
        Assert.Null(result.Lease);
        Assert.Equal(1, counter.CallCount);
    }

    [Fact]
    public async Task RenewAsync_NearAbsoluteExpiry_CapsIdleExpiryAtAbsoluteExpiry()
    {
        var counter = new StubGuestSessionCounter(1);
        var timeProvider = new ManualTimeProvider(StartTime);
        using var store = CreateStore(
            counter,
            timeProvider,
            new GuestSessionSettings
            {
                IdleTimeoutSeconds = 300,
                MaximumDurationMinutes = 5
            });
        var acquired = await store.TryAcquireAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var renewed = await store.RenewAsync(acquired.Lease!.SessionKey);

        Assert.NotNull(renewed);
        Assert.Equal(StartTime.AddMinutes(1), renewed.LastActivityAt);
        Assert.Equal(renewed.AbsoluteExpiresAt, renewed.IdleExpiresAt);
        Assert.Equal(StartTime.AddMinutes(5), renewed.AbsoluteExpiresAt);
    }

    [Fact]
    public async Task TryAcquireAsync_AfterIdleExpiry_ReplacesExpiredLease()
    {
        var counter = new StubGuestSessionCounter(100);
        var timeProvider = new ManualTimeProvider(StartTime);
        using var store = CreateStore(
            counter,
            timeProvider,
            new GuestSessionSettings
            {
                IdleTimeoutSeconds = 30,
                MaximumDurationMinutes = 57
            });
        var first = await store.TryAcquireAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(31));

        var second = await store.TryAcquireAsync();

        Assert.True(second.Acquired);
        Assert.Equal(101, second.Lease!.SessionNumber);
        Assert.NotEqual(first.Lease!.SessionKey, second.Lease.SessionKey);
        Assert.Equal(2, counter.CallCount);
    }

    [Fact]
    public async Task ReleaseAsync_WithDifferentSessionKey_DoesNotReleaseLease()
    {
        var counter = new StubGuestSessionCounter(1);
        var timeProvider = new ManualTimeProvider(StartTime);
        using var store = CreateStore(counter, timeProvider);
        var acquired = await store.TryAcquireAsync();

        var released = await store.ReleaseAsync(Guid.NewGuid());

        Assert.False(released);
        Assert.NotNull(await store.GetActiveAsync(acquired.Lease!.SessionKey));
    }

    private static InMemoryGuestLeaseStore CreateStore(
        IGuestSessionCounter counter,
        TimeProvider timeProvider,
        GuestSessionSettings? settings = null) =>
        new(
            counter,
            Options.Create(settings ?? new GuestSessionSettings()),
            timeProvider,
            NullLogger<InMemoryGuestLeaseStore>.Instance);

    private sealed class StubGuestSessionCounter(long next) : IGuestSessionCounter
    {
        private long _next = next;

        public int CallCount { get; private set; }

        public Task<long> GetNextAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_next++);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
