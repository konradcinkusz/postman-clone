using System.Security.Cryptography;

namespace PostmanClone.Sending.Assembly;

/// <summary>
/// The source of the multipart boundary. The production source is cryptographic; tests inject a seeded one so the
/// bytes of a multipart body are repeatable.
/// </summary>
public interface IRandomBytes
{
    void Fill(Span<byte> destination);
}

public sealed class CryptographicRandomBytes : IRandomBytes
{
    public static readonly CryptographicRandomBytes Instance = new();

    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}
