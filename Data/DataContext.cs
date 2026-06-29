
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
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);;

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
    }


    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Automatically converts all standard and nullable enums to strings globally
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }
}
