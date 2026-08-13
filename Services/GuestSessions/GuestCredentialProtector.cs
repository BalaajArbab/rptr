using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace rptr.Services.GuestSessions;

public sealed record GuestEncryptedValue(
    string Nonce,
    string Tag,
    string Ciphertext);

public sealed class GuestCredentialProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[]? _key;

    public GuestCredentialProtector(IOptions<GuestSessionSettings> settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Value.CredentialEncryptionKey))
        {
            return;
        }

        try
        {
            _key = Convert.FromBase64String(settings.Value.CredentialEncryptionKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "GuestSessions:CredentialEncryptionKey must be base64 encoded.",
                exception);
        }

        if (_key.Length != 32)
        {
            throw new InvalidOperationException(
                "GuestSessions:CredentialEncryptionKey must decode to exactly 32 bytes.");
        }
    }

    public GuestEncryptedValue Encrypt(string plaintext)
    {
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return new GuestEncryptedValue(
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(ciphertext));
    }

    public string Decrypt(GuestEncryptedValue value)
    {
        var key = GetKey();
        var nonce = Convert.FromBase64String(value.Nonce);
        var tag = Convert.FromBase64String(value.Tag);
        var ciphertext = Convert.FromBase64String(value.Ciphertext);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] GetKey() => _key ?? throw new InvalidOperationException(
        "GuestSessions:CredentialEncryptionKey is not configured.");
}
