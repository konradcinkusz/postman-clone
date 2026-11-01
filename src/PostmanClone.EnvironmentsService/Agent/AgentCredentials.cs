using System.Security.Cryptography;
using System.Text;

namespace PostmanClone.EnvironmentsService.Agent;

/// <summary>
/// The agent's two credentials (ADR-0026 Decision 3): the one-time pairing code and the agent token. Both are random, both are
/// stored as hashes, and neither is logged. The pairing code uses Crockford base32 so a person can type it.
/// </summary>
public static class AgentCredentials
{
    /// <summary>Crockford base32: no I, L, O or U, so a typed code is not ambiguous.</summary>
    public const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int PairingCodeLength = 10;

    /// <summary>The normalised shape: ten Crockford characters.</summary>
    private static readonly System.Text.RegularExpressions.Regex CodeShape =
        new("^[0-9A-HJKMNP-TV-Z]{10}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A new pairing code: ten characters from 32, about 50 bits (CONTRACTS A2.2).</summary>
    public static string NewPairingCode()
    {
        var code = new char[PairingCodeLength];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = CrockfordAlphabet[RandomNumberGenerator.GetInt32(CrockfordAlphabet.Length)];
        }

        return new string(code);
    }

    /// <summary>A new agent token: 32 random bytes, base64url without padding (43 characters).</summary>
    public static string NewAgentToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The form that is hashed: upper case, no spaces or hyphens, and the look-alike letters folded as Crockford does
    /// (O to 0, I and L to 1). Returns null when the result is not ten Crockford characters.
    /// </summary>
    public static string? NormalizePairingCode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            switch (char.ToUpperInvariant(character))
            {
                case ' ' or '-':
                    continue;
                case 'O':
                    builder.Append('0');
                    break;
                case 'I' or 'L':
                    builder.Append('1');
                    break;
                case var upper:
                    builder.Append(upper);
                    break;
            }
        }

        var normalized = builder.ToString();
        return CodeShape.IsMatch(normalized) ? normalized : null;
    }

    /// <summary>SHA-256 of the text, as lower-case hex. Used for both the pairing code and the agent token.</summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
