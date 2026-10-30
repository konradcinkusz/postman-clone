using System.Security.Cryptography;
using System.Text;

namespace PostmanClone.EnvironmentsService.Secrets;

/// <summary>
/// AES-256-GCM with a random 96-bit nonce per value (ADR-0006). The stored form is
/// <c>v1:</c> followed by base64 of <c>nonce | tag | ciphertext</c>. The key is copied in and never logged or echoed.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string EnvelopePrefix = "v1:";
    public const int KeySizeInBytes = 32;
    public const int NonceSizeInBytes = 12;
    public const int TagSizeInBytes = 16;

    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        // The message names the expected size only; the key itself never appears in an exception.
        if (key.Length != KeySizeInBytes)
        {
            throw new ArgumentException($"The secret key must be {KeySizeInBytes} bytes.", nameof(key));
        }

        _key = key.ToArray();
    }

    public bool IsAvailable => true;

    public string Protect(string plaintext, string associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[data.Length];
        var tag = new byte[TagSizeInBytes];

        using var aes = new AesGcm(_key, TagSizeInBytes);
        aes.Encrypt(nonce, data, ciphertext, tag, Encoding.UTF8.GetBytes(associatedData));

        var payload = new byte[NonceSizeInBytes + TagSizeInBytes + ciphertext.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSizeInBytes);
        ciphertext.CopyTo(payload, NonceSizeInBytes + TagSizeInBytes);
        return EnvelopePrefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string envelope, string associatedData)
    {
        if (!envelope.StartsWith(EnvelopePrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("The secret envelope is not in a known format.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(envelope[EnvelopePrefix.Length..]);
        }
        catch (FormatException)
        {
            throw new CryptographicException("The secret envelope is not in a known format.");
        }

        if (payload.Length < NonceSizeInBytes + TagSizeInBytes)
        {
            throw new CryptographicException("The secret envelope is too short.");
        }

        var nonce = payload.AsSpan(0, NonceSizeInBytes);
        var tag = payload.AsSpan(NonceSizeInBytes, TagSizeInBytes);
        var ciphertext = payload.AsSpan(NonceSizeInBytes + TagSizeInBytes);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSizeInBytes);
        // Throws CryptographicException when the tag, the ciphertext or the associated data does not match.
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(associatedData));
        return Encoding.UTF8.GetString(plaintext);
    }
}
