using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PostmanClone.AppHost.Resources;

namespace PostmanClone.AppHost.Tests;

public sealed class DevSecretsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "postmanclone-secrets-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_folder, "secrets.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void The_first_call_generates_and_keeps_the_key_and_the_second_call_returns_the_same_one()
    {
        var first = DevSecrets.GetOrCreate(_ => null, File_, "signing", DevSecrets.GenerateSigningKeyPem);
        var second = DevSecrets.GetOrCreate(_ => null, File_, "signing", () => throw new InvalidOperationException("must not generate twice"));

        Assert.Equal(first, second);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(first); // a malformed key throws
        Assert.Equal(2048, rsa.KeySize);
        Assert.Equal(first, JsonNode.Parse(File.ReadAllText(File_))!["Parameters:signing"]!.GetValue<string>());
    }

    [Fact]
    public void A_configured_value_wins_and_nothing_is_written()
    {
        var value = DevSecrets.GetOrCreate(key => key == "Parameters:signing" ? "from-configuration" : null, File_, "signing", () => throw new InvalidOperationException());

        Assert.Equal("from-configuration", value);
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void The_file_is_readable_by_its_owner_only_and_keeps_other_entries()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(File_, "{ \"Other:Setting\": \"kept\" }");

        DevSecrets.GetOrCreate(_ => null, File_, "encryption", DevSecrets.GenerateEncryptionKey);

        Assert.Equal("kept", JsonNode.Parse(File.ReadAllText(File_))!["Other:Setting"]!.GetValue<string>());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(File_));
        }
    }

    [Fact]
    public void The_encryption_key_is_32_bytes_of_base64()
    {
        Assert.Equal(32, Convert.FromBase64String(DevSecrets.GenerateEncryptionKey()).Length);
    }
}
