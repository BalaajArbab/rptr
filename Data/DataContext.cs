
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
    public DbSet<Snippet> Snippets { get; set; }
    public DbSet<SongTag> SongTags { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
                .HasMaxLength(128)
                .UseCollation("Latin1_General_100_BIN2");

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

        modelBuilder.Entity<Snippet>(snippet =>
        {
            snippet.HasIndex(s => new { s.PlaylistId, s.SongId }).IsUnique();
            snippet.Property(s => s.SongId)
                .IsRequired()
                .HasMaxLength(128)
                .UseCollation("Latin1_General_100_BIN2");
            snippet.Property(s => s.Name).IsRequired();
            snippet.Property(s => s.SongName).IsRequired();
            snippet.Property(s => s.PrimaryArtistName).IsRequired();

            snippet
                .HasOne(s => s.Playlist)
                .WithMany(p => p.Snippets)
                .HasForeignKey(s => s.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);

            snippet.OwnsMany(s => s.Segments, segments =>
            {
                segments.ToJson();
                segments.Property(s => s.StartMs).IsRequired();
                segments.Property(s => s.EndMs).IsRequired();
            });

            snippet.ToTable(table => table.HasCheckConstraint(
                "CK_Snippets_OriginalSongLengthMs_Positive",
                "[OriginalSongLengthMs] > 0"));
        });

        modelBuilder.Entity<SongTag>(songTag =>
        {
            songTag.HasIndex(s => new { s.PlaylistId, s.SongId }).IsUnique();
            songTag.Property(s => s.SongId)
                .IsRequired()
                .HasMaxLength(128)
                .UseCollation("Latin1_General_100_BIN2");
            songTag.Property(s => s.SongName).IsRequired();
            songTag.Property(s => s.PrimaryArtistName).IsRequired();
            songTag.Property(s => s.Tags)
                .IsRequired()
                .HasColumnType("nvarchar(max)");

            songTag
                .HasOne(s => s.Playlist)
                .WithMany(p => p.SongTags)
                .HasForeignKey(s => s.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }


    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Automatically converts all standard and nullable enums to strings globally
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }
}
