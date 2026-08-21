using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using dotnetrpg_again.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using rptr.Models;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;

namespace rptr.IntegrationTests.Infrastructure;

public sealed class ApiIntegrationFixture : IAsyncLifetime
{
    public const string CollectionName = "API integration tests";
    public const string PlaylistId = "integration-playlist";
    public const string SeedSongId = "seed-song";

    private const string SqlDatabaseName = "rptr_integration";
    private const string JwtSigningKey =
        "rptr-integration-tests-only-signing-key-which-is-long-enough-for-hmac-sha512";

    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder(
        "mongo:7.0").Build();

    private IntegrationApiFactory? _factory;

    public int OwnerUserId { get; private set; }
    public string OwnerSpotifyAccountId { get; } = "integration-owner";
    public string MongoDatabaseName { get; } = $"rptr_integration_{Guid.NewGuid():N}";
    public IServiceProvider Services => Factory.Services;
    public IMongoDatabase MongoDatabase { get; private set; } = null!;

    private IntegrationApiFactory Factory => _factory
        ?? throw new InvalidOperationException("The integration test host has not started.");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _sqlContainer.StartAsync(),
            _mongoContainer.StartAsync());

        var sqlConnectionString = new SqlConnectionStringBuilder(
            _sqlContainer.GetConnectionString())
        {
            InitialCatalog = SqlDatabaseName
        }.ConnectionString;

        _factory = new IntegrationApiFactory(
            sqlConnectionString,
            _mongoContainer.GetConnectionString(),
            MongoDatabaseName,
            JwtSigningKey);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        await context.Database.MigrateAsync();
        await SeedSqlAsync(context);

        MongoDatabase = new MongoClient(_mongoContainer.GetConnectionString())
            .GetDatabase(MongoDatabaseName);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await Task.WhenAll(
            _sqlContainer.DisposeAsync().AsTask(),
            _mongoContainer.DisposeAsync().AsTask());
    }

    public HttpClient CreateAnonymousClient() => Factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    public HttpClient CreateOwnerClient()
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(OwnerUserId, OwnerSpotifyAccountId));
        return client;
    }

    private async Task SeedSqlAsync(DataContext context)
    {
        var now = DateTime.UtcNow;
        var owner = new User
        {
            SpotifyAccountId = OwnerSpotifyAccountId,
            AccountName = OwnerSpotifyAccountId,
            Country = "CA",
            DisplayName = "Integration Owner",
            SpotifyProfileUrl = "https://open.spotify.com/user/integration-owner",
            SpotifyImageUrl = string.Empty,
            SubscriptionLevel = "premium",
            CreatedAt = now
        };
        var playlist = new Playlist
        {
            SpotifyPlaylistId = PlaylistId,
            OwnerUser = owner,
            CreatedAt = now,
            LastAccessed = now,
            Tags =
            [
                new PlaylistTag { Name = "Demo", Colour = "#336699" }
            ]
        };
        var song = new PlaylistSong
        {
            Playlist = playlist,
            SpotifySongId = SeedSongId,
            SongName = "\u96e8\u306e\u30c6\u30b9\u30c8",
            PrimaryArtistName = "Integration Artist",
            OriginalSongLengthMs = 180_000,
            Excluded = false,
            Tags = ["Focus"]
        };
        song.Snippet = new Snippet
        {
            PlaylistSong = song,
            Name = "Seed snippet",
            CreatedAt = now,
            Segments =
            [
                new SnippetSegment { StartMs = 1_000, EndMs = 8_000 }
            ]
        };

        context.Users.Add(owner);
        context.Playlists.Add(playlist);
        context.PlaylistSongs.Add(song);
        await context.SaveChangesAsync();

        OwnerUserId = owner.Id;
    }

    private string CreateJwt(int userId, string spotifyAccountId)
    {
        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                userId.ToString(CultureInfo.InvariantCulture)),
            new Claim(
                JwtRegisteredClaimNames.Sub,
                userId.ToString(CultureInfo.InvariantCulture)),
            new Claim("spotify_account_id", spotifyAccountId)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey)),
            SecurityAlgorithms.HmacSha512Signature);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}
