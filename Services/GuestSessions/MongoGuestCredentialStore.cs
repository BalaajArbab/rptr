using MongoDB.Driver;
using rptr.Data.Mongo;

namespace rptr.Services.GuestSessions;

public sealed class MongoGuestCredentialStore : IGuestCredentialStore
{
    private const string CredentialId = "spotify";
    private readonly IMongoCollection<GuestCredentialDocument> _credentials;
    private readonly GuestCredentialProtector _protector;

    public MongoGuestCredentialStore(
        MongoDbContext mongo,
        GuestCredentialProtector protector)
    {
        _credentials = mongo.Database.GetCollection<GuestCredentialDocument>(
            "guest_credentials");
        _protector = protector;
    }

    public async Task<GuestCredential?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var document = await _credentials
            .Find(value => value.Id == CredentialId)
            .SingleOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        var refreshToken = _protector.Decrypt(new GuestEncryptedValue(
            document.Nonce,
            document.Tag,
            document.Ciphertext));

        return new GuestCredential(
            document.SpotifyAccountId,
            document.Scopes,
            document.RefreshTokenExpiresAt,
            refreshToken);
    }

    public async Task UpsertAsync(
        GuestCredential credential,
        CancellationToken cancellationToken = default)
    {
        var encrypted = _protector.Encrypt(credential.RefreshToken);
        var document = new GuestCredentialDocument
        {
            Id = CredentialId,
            SchemaVersion = 1,
            SpotifyAccountId = credential.SpotifyAccountId,
            Scopes = credential.Scopes,
            RefreshTokenExpiresAt = credential.RefreshTokenExpiresAt,
            Nonce = encrypted.Nonce,
            Tag = encrypted.Tag,
            Ciphertext = encrypted.Ciphertext
        };

        await _credentials.ReplaceOneAsync(
            value => value.Id == CredentialId,
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }
}
