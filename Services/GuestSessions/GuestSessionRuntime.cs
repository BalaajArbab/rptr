using Microsoft.Data.Sqlite;
using rptr.Services.Authorization;

namespace rptr.Services.GuestSessions;

public sealed class GuestSessionRuntime : IAsyncDisposable
{
    public GuestSessionRuntime(
        GuestLease lease,
        AuthUserResponse user,
        GuestSpotifyToken spotifyToken,
        string sqliteConnectionString,
        SqliteConnection keeperConnection)
    {
        Lease = lease;
        User = user;
        SpotifyToken = spotifyToken;
        SqliteConnectionString = sqliteConnectionString;
        KeeperConnection = keeperConnection;
    }

    public GuestLease Lease { get; set; }
    public AuthUserResponse User { get; }
    public GuestSpotifyToken SpotifyToken { get; }
    public string SqliteConnectionString { get; }
    public SqliteConnection KeeperConnection { get; }
    public SemaphoreSlim OperationGate { get; } = new(1, 1);

    public async ValueTask DisposeAsync()
    {
        await OperationGate.WaitAsync();
        try
        {
            await KeeperConnection.DisposeAsync();
        }
        finally
        {
            OperationGate.Release();
            OperationGate.Dispose();
        }
    }
}
