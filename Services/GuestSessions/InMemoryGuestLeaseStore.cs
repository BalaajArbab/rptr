using Microsoft.Extensions.Options;

namespace rptr.Services.GuestSessions;

public sealed class InMemoryGuestLeaseStore : IGuestLeaseStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IGuestSessionCounter _sessionCounter;
    private readonly GuestSessionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InMemoryGuestLeaseStore> _logger;

    private GuestLease? _activeLease;

    public InMemoryGuestLeaseStore(
        IGuestSessionCounter sessionCounter,
        IOptions<GuestSessionSettings> settings,
        TimeProvider timeProvider,
        ILogger<InMemoryGuestLeaseStore> logger)
    {
        _sessionCounter = sessionCounter;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GuestLeaseAcquireResult> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (IsActive(_activeLease, now))
            {
                return GuestLeaseAcquireResult.Occupied();
            }

            var sessionNumber = await _sessionCounter.GetNextAsync(cancellationToken);
            var absoluteExpiresAt = now.AddMinutes(_settings.MaximumDurationMinutes);
            var idleExpiresAt = Min(
                now.AddSeconds(_settings.IdleTimeoutSeconds),
                absoluteExpiresAt);

            _activeLease = new GuestLease(
                sessionNumber,
                Guid.NewGuid(),
                now,
                now,
                idleExpiresAt,
                absoluteExpiresAt);

            _logger.LogInformation(
                "Acquired guest session {GuestSessionNumber}. IdleExpiresAt: {IdleExpiresAt}. AbsoluteExpiresAt: {AbsoluteExpiresAt}.",
                _activeLease.SessionNumber,
                _activeLease.IdleExpiresAt,
                _activeLease.AbsoluteExpiresAt);

            return GuestLeaseAcquireResult.Success(_activeLease);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuestLease?> RenewAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (!IsOwnedAndActive(_activeLease, sessionKey, now))
            {
                return null;
            }

            _activeLease = _activeLease! with
            {
                LastActivityAt = now,
                IdleExpiresAt = Min(
                    now.AddSeconds(_settings.IdleTimeoutSeconds),
                    _activeLease.AbsoluteExpiresAt)
            };

            _logger.LogDebug(
                "Renewed guest session {GuestSessionNumber} until {IdleExpiresAt}.",
                _activeLease.SessionNumber,
                _activeLease.IdleExpiresAt);

            return _activeLease;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuestLease?> GetActiveAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            return IsOwnedAndActive(_activeLease, sessionKey, now)
                ? _activeLease
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuestLease?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            return IsActive(_activeLease, now) ? _activeLease : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ReleaseAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_activeLease?.SessionKey != sessionKey)
            {
                return false;
            }

            var sessionNumber = _activeLease.SessionNumber;
            _activeLease = null;

            _logger.LogInformation(
                "Released guest session {GuestSessionNumber}.",
                sessionNumber);

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static bool IsOwnedAndActive(
        GuestLease? lease,
        Guid sessionKey,
        DateTimeOffset now) =>
        lease?.SessionKey == sessionKey && IsActive(lease, now);

    private static bool IsActive(GuestLease? lease, DateTimeOffset now) =>
        lease is not null
        && lease.IdleExpiresAt > now
        && lease.AbsoluteExpiresAt > now;

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;
}
