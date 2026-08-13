using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using rptr.Services.Authorization;

namespace rptr.Services.GuestSessions;

public sealed class InMemoryGuestRuntimeStore : IGuestRuntimeStore, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IWebHostEnvironment _environment;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly GuestSessionSettings _settings;
    private readonly ILogger<InMemoryGuestRuntimeStore> _logger;
    private GuestSessionRuntime? _runtime;

    public InMemoryGuestRuntimeStore(
        IWebHostEnvironment environment,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<GuestSessionSettings> settings,
        ILogger<InMemoryGuestRuntimeStore> logger)
    {
        _environment = environment;
        _dataProtectionProvider = dataProtectionProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<GuestSessionRuntime> CreateAsync(
        GuestLease lease,
        string spotifyAccountId,
        GuestSpotifyToken spotifyToken,
        CancellationToken cancellationToken = default)
    {
        var baselinePath = Path.IsPathRooted(_settings.BaselinePath)
            ? _settings.BaselinePath
            : Path.Combine(_environment.ContentRootPath, _settings.BaselinePath);

        if (!File.Exists(baselinePath))
        {
            throw new FileNotFoundException(
                "The packaged guest baseline database was not found.",
                baselinePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"guest-{lease.SessionKey:N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        var keeper = new SqliteConnection(connectionString);

        try
        {
            await keeper.OpenAsync(cancellationToken);
            await using (var source = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = baselinePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString()))
            {
                await source.OpenAsync(cancellationToken);
                source.BackupDatabase(keeper);
            }

            await using var context = CreateContext(connectionString);
            var user = await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.SpotifyAccountId == spotifyAccountId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The guest baseline does not contain the provisioned Spotify account.");

            var runtime = new GuestSessionRuntime(
                lease,
                MapUser(user),
                spotifyToken,
                connectionString,
                keeper);

            GuestSessionRuntime? previous;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                previous = _runtime;
                _runtime = runtime;
            }
            finally
            {
                _gate.Release();
            }

            if (previous is not null)
            {
                await previous.DisposeAsync();
            }

            _logger.LogInformation(
                "Created isolated SQLite runtime for guest session {GuestSessionNumber}.",
                lease.SessionNumber);
            return runtime;
        }
        catch
        {
            await keeper.DisposeAsync();
            throw;
        }
    }

    public async Task<GuestSessionRuntime?> GetAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _runtime?.Lease.SessionKey == sessionKey ? _runtime : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuestSessionRuntime?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _runtime;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateLeaseAsync(
        GuestLease lease,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_runtime?.Lease.SessionKey == lease.SessionKey)
            {
                _runtime.Lease = lease;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> ExecuteAsync<T>(
        Guid sessionKey,
        Func<DataContext, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var runtime = await GetAsync(sessionKey, cancellationToken)
            ?? throw new GuestSessionUnavailableException();

        await runtime.OperationGate.WaitAsync(cancellationToken);
        try
        {
            if (await GetAsync(sessionKey, cancellationToken) != runtime)
            {
                throw new GuestSessionUnavailableException();
            }

            await using var context = CreateContext(runtime.SqliteConnectionString);
            return await operation(context);
        }
        finally
        {
            runtime.OperationGate.Release();
        }
    }

    public async Task RemoveAsync(
        Guid sessionKey,
        CancellationToken cancellationToken = default)
    {
        GuestSessionRuntime? removed = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_runtime?.Lease.SessionKey == sessionKey)
            {
                removed = _runtime;
                _runtime = null;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (removed is not null)
        {
            await removed.DisposeAsync();
            _logger.LogInformation(
                "Disposed SQLite runtime for guest session {GuestSessionNumber}.",
                removed.Lease.SessionNumber);
        }
    }

    public async ValueTask DisposeAsync()
    {
        GuestSessionRuntime? runtime;
        await _gate.WaitAsync();
        try
        {
            runtime = _runtime;
            _runtime = null;
        }
        finally
        {
            _gate.Release();
        }

        if (runtime is not null)
        {
            await runtime.DisposeAsync();
        }

        _gate.Dispose();
    }

    private DataContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseSqlite(connectionString)
            .ReplaceService<IModelCacheKeyFactory, ProviderModelCacheKeyFactory>()
            .Options;

        return new DataContext(options, _dataProtectionProvider);
    }

    private static AuthUserResponse MapUser(User user) => new(
        user.Id,
        user.SpotifyAccountId,
        user.AccountName,
        user.DisplayName,
        user.Country,
        user.SpotifyProfileUrl,
        user.SpotifyImageUrl,
        user.SubscriptionLevel);
}
