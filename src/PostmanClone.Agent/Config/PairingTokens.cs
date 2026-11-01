using System.Security.Cryptography;
using System.Text;

namespace PostmanClone.Agent.Config;

/// <summary>
/// The local pairing token (CONTRACTS A2.6): 32 random bytes, base64url. The browser holds it in memory and sends it on every
/// send; the agent keeps only its hash. The comparison is constant-time.
/// </summary>
public static class PairingTokens
{
    public const string HeaderName = "X-Agent-Pairing-Token";

    public static string New() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>True when the presented token is the one whose hash is stored. A missing hash never matches.</summary>
    public static bool Matches(string? presented, string? storedHash)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var actual = Encoding.UTF8.GetBytes(Hash(presented));
        var expected = Encoding.UTF8.GetBytes(storedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
