using System.Security.Cryptography;
using System.Text;
using PostmanClone.EnvironmentsService.Secrets;
using Xunit;

namespace PostmanClone.EnvironmentsService.Tests.Secrets;

/// <summary>The secret envelope is AES-256-GCM with a per-value nonce, bound to its place (ADR-0006, CONTRACTS §3).</summary>
public sealed class AesGcmSecretProtectorTests
{
    private const string Place = "workspace|environment|API_TOKEN";
    private const string Secret = "not-a-real-secret-value";

    private static AesGcmSecretProtector NewProtector() =>
        new(RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeySizeInBytes));

    [Fact]
    public void A_protected_value_decrypts_to_the_same_text()
    {
        var protector = NewProtector();

        var envelope = protector.Protect("Ünïcödé ✓ value", Place);

        Assert.Equal("Ünïcödé ✓ value", protector.Unprotect(envelope, Place));
    }

    [Fact]
    public void The_envelope_has_the_v1_prefix_and_never_contains_the_plaintext()
    {
        var envelope = NewProtector().Protect(Secret, Place);

        Assert.StartsWith("v1:", envelope, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, envelope, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Secret)), envelope, StringComparison.Ordinal);
    }

    [Fact]
    public void A_different_associated_data_fails_to_decrypt()
    {
        var protector = NewProtector();
        var envelope = protector.Protect(Secret, Place);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(envelope, "workspace|environment|OTHER_KEY"));
    }

    [Fact]
    public void A_ciphertext_moved_to_another_key_fails_to_decrypt()
    {
        var protector = NewProtector();
        var envelope = protector.Protect(Secret, "workspace|environment|API_TOKEN");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(envelope, "workspace|environment|API_TOKEN_COPY"));
    }

    [Fact]
    public void Flipping_any_single_byte_of_the_envelope_makes_decryption_fail()
    {
        var protector = NewProtector();
        var envelope = protector.Protect(Secret, Place);
        var payload = Convert.FromBase64String(envelope["v1:".Length..]);

        for (var index = 0; index < payload.Length; index++)
        {
            var tampered = (byte[])payload.Clone();
            tampered[index] ^= 0x01;
            var tamperedEnvelope = "v1:" + Convert.ToBase64String(tampered);

            Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tamperedEnvelope, Place));
        }
    }

    [Fact]
    public void Each_encryption_uses_a_fresh_nonce_across_1000_values()
    {
        var protector = NewProtector();

        var nonces = new HashSet<string>();
        var envelopes = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            var envelope = protector.Protect(Secret, Place);
            var payload = Convert.FromBase64String(envelope["v1:".Length..]);
            nonces.Add(Convert.ToBase64String(payload.AsSpan(0, AesGcmSecretProtector.NonceSizeInBytes)));
            envelopes.Add(envelope);
        }

        Assert.Equal(1000, nonces.Count);
        Assert.Equal(1000, envelopes.Count);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void A_key_that_is_not_32_bytes_is_rejected_without_echoing_it(int length)
    {
        var key = RandomNumberGenerator.GetBytes(length);

        var error = Assert.Throws<ArgumentException>(() => new AesGcmSecretProtector(key));

        Assert.DoesNotContain(Convert.ToBase64String(key), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_envelope_without_the_v1_prefix_is_rejected()
    {
        var protector = NewProtector();

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(new byte[64]), Place));
    }

    [Fact]
    public void An_envelope_shorter_than_nonce_plus_tag_is_rejected()
    {
        var protector = NewProtector();

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("v1:" + Convert.ToBase64String(new byte[10]), Place));
    }
}
