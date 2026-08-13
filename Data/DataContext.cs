
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace dotnetrpg_again.Data;

public class DataContext : DbContext
{
    private readonly IDataProtector _tokenProtector;

    public DataContext(DbContextOptions<DataContext> options, IDataProtectionProvider dataProtectionProvider) : base(options)
    {
        _tokenProtector = dataProtectionProvider.CreateProtector("SpotifyTokens");
    }
    
    public DbSet<User> Users { get; set; }
    public DbSet<SpotifyToken> SpotifyTokens { get; set; }
    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<PlaylistSong> PlaylistSongs { get; set; }
    public DbSet<Snippet> Snippets { get; set; }
    public DbSet<Feedback> Feedback { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isSqlServer = Database.IsSqlServer();
        var isSqlite = Database.IsSqlite();
        var encryptedStringConverter = new ValueConverter<string, string>(
            plainText => string.IsNullOrEmpty(plainText)
                ? plainText
                : _tokenProtector.Protect(plainText),
            encryptedText => string.IsNullOrEmpty(encryptedText)
                ? encryptedText
                : _tokenProtector.Unprotect(encryptedText));

        modelBuilder.Entity<User>()
            .HasIndex(u => u.SpotifyAccountId)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.SpotifyAccountId)
            .IsRequired()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        modelBuilder.Entity<SpotifyToken>()
            .HasKey(t => t.UserId);

        modelBuilder.Entity<User>()
            .HasOne(u => u.SpotifyToken)
            .WithOne(t => t.User)
            .HasForeignKey<SpotifyToken>(t => t.UserId);

        modelBuilder.Entity<SpotifyToken>()
            .Property(t => t.AccessToken)
            .HasConversion(encryptedStringConverter)
            .HasColumnName("AccessTokenEncrypted");

        modelBuilder.Entity<SpotifyToken>()
            .Property(t => t.RefreshToken)
            .HasConversion(encryptedStringConverter)
            .HasColumnName("RefreshTokenEncrypted");

        modelBuilder.Entity<Playlist>(playlist =>
        {
            playlist.HasIndex(p => p.SpotifyPlaylistId).IsUnique();
            playlist.Property(p => p.SpotifyPlaylistId)
                .IsRequired()
                .HasMaxLength(128);

            if (isSqlServer)
            {
                playlist.Property(p => p.SpotifyPlaylistId)
                    .UseCollation("Latin1_General_100_BIN2");
            }

            playlist
                .HasOne(p => p.OwnerUser)
                .WithMany(u => u.OwnedPlaylists)
                .HasForeignKey(p => p.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);

            playlist.OwnsMany(p => p.Tags, tags =>
            {
                tags.ToJson();
                tags.Property(t => t.Name).IsRequired();
                tags.Property(t => t.Colour).IsRequired();
            });
        });

        modelBuilder.Entity<PlaylistSong>(song =>
        {
            song.HasIndex(s => new { s.PlaylistId, s.SpotifySongId }).IsUnique();
            song.Property(s => s.SpotifySongId)
                .IsRequired()
                .HasMaxLength(128);

            if (isSqlServer)
            {
                song.Property(s => s.SpotifySongId)
                    .UseCollation("Latin1_General_100_BIN2");
            }
            song.Property(s => s.SongName).IsRequired();
            song.Property(s => s.PrimaryArtistName).IsRequired();
            song.Property(s => s.Excluded).HasDefaultValue(false);
            if (isSqlServer)
            {
                song.Property(s => s.Tags).HasColumnType("nvarchar(max)");
            }

            song
                .HasOne(s => s.Playlist)
                .WithMany(p => p.Songs)
                .HasForeignKey(s => s.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);

            song.ToTable(table => table.HasCheckConstraint(
                "CK_PlaylistSongs_OriginalSongLengthMs_Positive",
                isSqlite
                    ? "\"OriginalSongLengthMs\" IS NULL OR \"OriginalSongLengthMs\" > 0"
                    : "[OriginalSongLengthMs] IS NULL OR [OriginalSongLengthMs] > 0"));
        });

        modelBuilder.Entity<Snippet>(snippet =>
        {
            snippet.HasIndex(s => s.PlaylistSongId).IsUnique();
            snippet.Property(s => s.Name).IsRequired();

            snippet
                .HasOne(s => s.PlaylistSong)
                .WithOne(s => s.Snippet)
                .HasForeignKey<Snippet>(s => s.PlaylistSongId)
                .OnDelete(DeleteBehavior.Cascade);

            snippet.OwnsMany(s => s.Segments, segments =>
            {
                segments.ToJson();
                segments.Property(s => s.StartMs).IsRequired();
                segments.Property(s => s.EndMs).IsRequired();
            });
        });

        modelBuilder.Entity<Feedback>(feedback =>
        {
            feedback.Property(f => f.Message)
                .IsRequired()
                .HasMaxLength(2000);
            feedback.Property(f => f.IpAddress)
                .HasMaxLength(45)
                .IsUnicode(false);

            feedback
                .HasOne(f => f.User)
                .WithMany(u => u.FeedbackSubmissions)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }


    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Automatically converts all standard and nullable enums to strings globally
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }
}
