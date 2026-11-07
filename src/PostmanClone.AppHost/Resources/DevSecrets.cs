using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The development secrets the AppHost needs and nobody should have to type: the RS256 signing key for authservice and the
/// AES key for secret variables. A value given in configuration (<c>Parameters:&lt;name&gt;</c>, user secrets, environment) wins.
/// Otherwise one is generated on first run and kept in this project's user secrets file, outside the repository, so the next start
/// signs the same tokens and decrypts the same variables. Nothing generated here is ever printed or written into the tree.
/// </summary>
internal static class DevSecrets
{
    public const string UserSecretsId = "postmanclone-apphost";

    /// <summary>The signing key: an RSA 2048 private key as PKCS#8 PEM, which is what authservice's <c>Jwt:PrivateKeyPem</c> reads.</summary>
    public static string GenerateSigningKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>A 32-byte key, base64, as <c>Secrets:EncryptionKey</c> expects (ADR-0006).</summary>
    public static string GenerateEncryptionKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The value of <paramref name="name"/>: the configured one, else the one kept in <paramref name="secretsFile"/>, else a new one
    /// from <paramref name="generate"/>, which is written to <paramref name="secretsFile"/> (owner-only permissions) before it is returned.
    /// </summary>
    public static string GetOrCreate(Func<string, string?> configured, string secretsFile, string name, Func<string> generate)
    {
        var key = "Parameters:" + name;
        if (configured(key) is { Length: > 0 } fromConfiguration)
        {
            return fromConfiguration;
        }

        var document = File.Exists(secretsFile) ? JsonNode.Parse(File.ReadAllText(secretsFile)) as JsonObject ?? new JsonObject() : new JsonObject();
        if (document[key]?.GetValue<string>() is { Length: > 0 } stored)
        {
            return stored;
        }

        var created = generate();
        document[key] = created;
        Directory.CreateDirectory(Path.GetDirectoryName(secretsFile)!);
        File.WriteAllText(secretsFile, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(secretsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return created;
    }

    /// <summary>The user secrets file of this project, where <c>dotnet user-secrets</c> keeps it.</summary>
    public static string DefaultSecretsFile()
    {
        var root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets");
        return Path.Combine(root, UserSecretsId, "secrets.json");
    }
}
