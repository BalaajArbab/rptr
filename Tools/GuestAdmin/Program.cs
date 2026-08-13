using dotnetrpg_again.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using rptr.Data.Mongo;
using rptr.Models;
using rptr.Services.Authorization;
using rptr.Services.GuestSessions;

return await GuestAdmin.RunAsync(args);

internal static class GuestAdmin
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 1;
        }

        var projectRoot = FindProjectRoot();
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environments.Development;
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(DataContext).Assembly.GetName().Name,
            ContentRootPath = projectRoot,
            EnvironmentName = environment
        });
        builder.Services.AddDataProtection();
        using var host = builder.Build();

        try
        {
            return args[0] switch
            {
                "export-baseline" => await ExportBaselineAsync(
                    builder.Configuration,
                    host.Services.GetRequiredService<IDataProtectionProvider>(),
                    projectRoot,
                    args[1..]),
                "provision-credential" => await ProvisionCredentialAsync(
                    builder.Configuration,
                    host.Services.GetRequiredService<IDataProtectionProvider>(),
                    args[1..]),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> ExportBaselineAsync(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        string projectRoot,
        string[] args)
    {
        var spotifyAccountId = RequiredOption(args, "--spotify-account-id");
        var output = Option(args, "--output")
            ?? Path.Combine(projectRoot, "GuestData", "guest-baseline.db");
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        await using var source = CreateSqlContext(configuration, dataProtectionProvider);
        var sourceUser = await source.Users
            .AsNoTracking()
            .Include(user => user.OwnedPlaylists)
                .ThenInclude(playlist => playlist.Songs)
                    .ThenInclude(song => song.Snippet)
            .SingleOrDefaultAsync(user => user.SpotifyAccountId == spotifyAccountId)
            ?? throw new InvalidOperationException(
                $"No SQL user exists for Spotify account '{spotifyAccountId}'.");

        var temporary = output + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = CreateSqliteContext(
                temporary,
                dataProtectionProvider))
            {
                await destination.Database.EnsureCreatedAsync();
                destination.Users.Add(CloneUser(sourceUser));
                await destination.SaveChangesAsync();
            }

            File.Move(temporary, output, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        Console.WriteLine(
            $"Exported {sourceUser.OwnedPlaylists.Count} playlists to {output}");
        return 0;
    }

    private static async Task<int> ProvisionCredentialAsync(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        string[] args)
    {
        var spotifyAccountId = RequiredOption(args, "--spotify-account-id");
        await using var source = CreateSqlContext(configuration, dataProtectionProvider);
        var user = await source.Users
            .AsNoTracking()
            .Include(value => value.SpotifyToken)
            .SingleOrDefaultAsync(value => value.SpotifyAccountId == spotifyAccountId)
            ?? throw new InvalidOperationException(
                $"No SQL user exists for Spotify account '{spotifyAccountId}'.");
        var token = user.SpotifyToken
            ?? throw new InvalidOperationException("The selected user has no Spotify token.");

        if (string.IsNullOrWhiteSpace(token.RefreshToken)
            || token.RefreshTokenExpiresAt <= DateTime.UtcNow)
        {
            throw new InvalidOperationException(
                "The selected user's Spotify refresh token is missing or expired.");
        }

        if (!SpotifyScopeRequirements.ContainsAll(token.Scope))
        {
            throw new InvalidOperationException(
                "The selected user's Spotify credential lacks one or more required scopes.");
        }

        var mongoSettings = new MongoDbSettings();
        configuration.GetSection(MongoDbSettings.SectionName).Bind(mongoSettings);
        var guestSettings = new GuestSessionSettings();
        configuration.GetSection(GuestSessionSettings.SectionName).Bind(guestSettings);
        var mongo = new MongoDbContext(Options.Create(mongoSettings));
        var protector = new GuestCredentialProtector(Options.Create(guestSettings));
        var store = new MongoGuestCredentialStore(mongo, protector);

        await store.UpsertAsync(new GuestCredential(
            user.SpotifyAccountId,
            token.Scope,
            token.RefreshTokenExpiresAt,
            token.RefreshToken));

        Console.WriteLine(
            $"Provisioned the encrypted guest credential for Spotify account {user.SpotifyAccountId}.");
        return 0;
    }

    private static DataContext CreateSqlContext(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider)
    {
        var connectionName = configuration["Database:ConnectionName"]
            ?? "LocalConnection";
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionName}' is not configured.");
        var options = new DbContextOptionsBuilder<DataContext>()
            .ReplaceService<IModelCacheKeyFactory, ProviderModelCacheKeyFactory>()
            .UseSqlServer(connectionString)
            .Options;
        return new DataContext(options, dataProtectionProvider);
    }

    private static DataContext CreateSqliteContext(
        string path,
        IDataProtectionProvider dataProtectionProvider)
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .ReplaceService<IModelCacheKeyFactory, ProviderModelCacheKeyFactory>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString())
            .Options;
        return new DataContext(options, dataProtectionProvider);
    }

    private static User CloneUser(User source) => new()
    {
        Id = source.Id,
        SpotifyAccountId = source.SpotifyAccountId,
        AccountName = source.AccountName,
        Country = source.Country,
        DisplayName = source.DisplayName,
        SpotifyProfileUrl = source.SpotifyProfileUrl,
        SpotifyImageUrl = source.SpotifyImageUrl,
        SubscriptionLevel = source.SubscriptionLevel,
        CreatedAt = source.CreatedAt,
        OwnedPlaylists = source.OwnedPlaylists.Select(ClonePlaylist).ToList()
    };

    private static Playlist ClonePlaylist(Playlist source) => new()
    {
        Id = source.Id,
        SpotifyPlaylistId = source.SpotifyPlaylistId,
        OwnerUserId = source.OwnerUserId,
        CreatedAt = source.CreatedAt,
        LastAccessed = source.LastAccessed,
        Tags = source.Tags.Select(tag => new PlaylistTag
        {
            Name = tag.Name,
            Colour = tag.Colour
        }).ToList(),
        Songs = source.Songs.Select(CloneSong).ToList()
    };

    private static PlaylistSong CloneSong(PlaylistSong source) => new()
    {
        Id = source.Id,
        PlaylistId = source.PlaylistId,
        SpotifySongId = source.SpotifySongId,
        SongName = source.SongName,
        PrimaryArtistName = source.PrimaryArtistName,
        OriginalSongLengthMs = source.OriginalSongLengthMs,
        Excluded = source.Excluded,
        Tags = source.Tags?.ToList(),
        Snippet = source.Snippet is null
            ? null
            : new Snippet
            {
                Id = source.Snippet.Id,
                PlaylistSongId = source.Snippet.PlaylistSongId,
                Name = source.Snippet.Name,
                CreatedAt = source.Snippet.CreatedAt,
                Segments = source.Snippet.Segments.Select(segment =>
                    new SnippetSegment
                    {
                        StartMs = segment.StartMs,
                        EndMs = segment.EndMs
                    }).ToList()
            }
    };

    private static string RequiredOption(string[] args, string name) =>
        Option(args, name)
        ?? throw new ArgumentException($"Required option '{name}' was not provided.");

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "rptr.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Run GuestAdmin from the rptr repository or one of its subdirectories.");
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("GuestAdmin commands:");
        Console.WriteLine("  export-baseline --spotify-account-id <id> [--output <path>]");
        Console.WriteLine("  provision-credential --spotify-account-id <id>");
    }
}
