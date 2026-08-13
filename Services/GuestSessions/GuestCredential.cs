using MongoDB.Bson.Serialization.Attributes;

namespace rptr.Services.GuestSessions;

public sealed class GuestCredentialDocument
{
    [BsonId]
    public string Id { get; set; } = "spotify";

    public int SchemaVersion { get; set; } = 1;
    public string SpotifyAccountId { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Ciphertext { get; set; } = string.Empty;
}

public sealed record GuestCredential(
    string SpotifyAccountId,
    string Scopes,
    DateTime? RefreshTokenExpiresAt,
    string RefreshToken);

public interface IGuestCredentialStore
{
    Task<GuestCredential?> GetAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(
        GuestCredential credential,
        CancellationToken cancellationToken = default);
}
