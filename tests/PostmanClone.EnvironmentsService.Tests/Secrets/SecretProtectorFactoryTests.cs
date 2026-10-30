using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using PostmanClone.EnvironmentsService.Secrets;
using Xunit;

namespace PostmanClone.EnvironmentsService.Tests.Secrets;

/// <summary>Chooses the protector from <c>Secrets:EncryptionKey</c>; a bad key degrades secrets only (P8).</summary>
public sealed class SecretProtectorFactoryTests
{
    [Fact]
    public void A_valid_32_byte_base64_key_yields_a_working_protector()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeySizeInBytes));

        var protector = SecretProtectorFactory.Create(key, NullLogger.Instance);

        Assert.True(protector.IsAvailable);
        Assert.Equal("value", protector.Unprotect(protector.Protect("value", "place"), "place"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_key_yields_the_unavailable_protector(string? key)
    {
        var protector = SecretProtectorFactory.Create(key, NullLogger.Instance);

        Assert.False(protector.IsAvailable);
        Assert.IsType<UnavailableSecretProtector>(protector);
    }

    [Fact]
    public void Secret_operations_answer_secrets_unavailable_when_no_key_is_configured()
    {
        var protector = SecretProtectorFactory.Create(null, NullLogger.Instance);

        Assert.Throws<SecretsUnavailableException>(() => protector.Protect("value", "place"));
        Assert.Throws<SecretsUnavailableException>(() => protector.Unprotect("v1:AAAA", "place"));
    }

    [Theory]
    [MemberData(nameof(MalformedKeys))]
    public void A_malformed_key_yields_the_unavailable_protector(string key)
    {
        var protector = SecretProtectorFactory.Create(key, NullLogger.Instance);

        Assert.False(protector.IsAvailable);
    }

    public static TheoryData<string> MalformedKeys() =>
    [
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(31)),
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(33)),
        "not base64 at all!",
    ];
}
