using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using rptr.Services.GuestSessions;

namespace rptr.UnitTests.GuestSessions;

[Trait("Category", "Unit")]
public sealed class GuestCredentialProtectorTests
{
    [Fact]
    public void EncryptAndDecrypt_WithValidKey_RoundTripsUnicodeValue()
    {
        var protector = CreateProtector(CreateKey());
        const string plaintext = "Spotify refresh token - 日本語 - پاکستان";

        var encrypted = protector.Encrypt(plaintext);
        var decrypted = protector.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(plaintext, encrypted.Ciphertext);
    }

    [Fact]
    public void Encrypt_WithSamePlaintextTwice_UsesDifferentNonces()
    {
        var protector = CreateProtector(CreateKey());

        var first = protector.Encrypt("refresh-token");
        var second = protector.Encrypt("refresh-token");

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
    }

    [Fact]
    public void Constructor_WithInvalidBase64Key_ThrowsConfigurationError()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateProtector("not-base64"));

        Assert.Contains("base64 encoded", exception.Message);
    }

    [Fact]
    public void Constructor_WithWrongKeyLength_ThrowsConfigurationError()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateProtector(shortKey));

        Assert.Contains("exactly 32 bytes", exception.Message);
    }

    [Fact]
    public void Decrypt_WithModifiedCiphertext_FailsAuthentication()
    {
        var protector = CreateProtector(CreateKey());
        var encrypted = protector.Encrypt("refresh-token");
        var ciphertext = Convert.FromBase64String(encrypted.Ciphertext);
        ciphertext[0] ^= 0x01;
        var modified = encrypted with
        {
            Ciphertext = Convert.ToBase64String(ciphertext)
        };

        Assert.ThrowsAny<CryptographicException>(() => protector.Decrypt(modified));
    }

    private static GuestCredentialProtector CreateProtector(string key) =>
        new(Options.Create(new GuestSessionSettings
        {
            CredentialEncryptionKey = key
        }));

    private static string CreateKey() => Convert.ToBase64String(
        Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
}
