using PostmanClone.ExecutionService.Sending.Assembly;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>CONTRACTS §4 masking: a secret in every form it can take, sensitive names, and short-secret warnings.</summary>
public sealed class SecretMaskerTests
{
    private const string Secret = "pass word";

    [Theory]
    [InlineData("pass word", SecretMasker.Mask)]
    [InlineData("pass%20word", SecretMasker.Mask)]
    [InlineData("pass+word", SecretMasker.Mask)]
    [InlineData("cGFzcyB3b3Jk", SecretMasker.Mask)]
    [InlineData("a pass word b", "a " + SecretMasker.Mask + " b")]
    [InlineData("password", "password")]
    [InlineData("pass word!", SecretMasker.Mask + "!")]
    public void A_secret_is_masked_as_written_percent_encoded_form_encoded_and_base64(string text, string expected)
    {
        var masker = SecretMasker.For([new ResolvedValue(Secret, true)]);

        Assert.Equal(expected, masker.MaskText(text));
    }

    [Fact]
    public void A_non_secret_value_is_not_masked()
    {
        var masker = SecretMasker.For([new ResolvedValue("public value", false)]);

        Assert.Equal("public value", masker.MaskText("public value"));
        Assert.Empty(masker.Warnings);
    }

    [Fact]
    public void An_empty_secret_masks_nothing_and_warns_nothing()
    {
        var masker = SecretMasker.For([new ResolvedValue(string.Empty, true)]);

        Assert.Equal("abc", masker.MaskText("abc"));
        Assert.Empty(masker.Warnings);
    }

    [Fact]
    public void The_longest_form_is_replaced_first_so_a_shorter_secret_inside_it_does_not_break_the_match()
    {
        var masker = SecretMasker.For([new ResolvedValue("ab", true), new ResolvedValue("abcdef", true)]);

        Assert.Equal(SecretMasker.Mask, masker.MaskText("abcdef"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ab")]
    public void A_secret_shorter_than_four_characters_is_masked_and_reported(string secret)
    {
        var masker = SecretMasker.For([new ResolvedValue(secret, true)]);

        Assert.Equal(SecretMasker.Mask, masker.MaskText(secret));
        Assert.Equal(["secret_short"], masker.Warnings.Select(warning => warning.Code));
    }

    [Fact]
    public void A_secret_of_four_characters_is_not_reported()
    {
        var masker = SecretMasker.For([new ResolvedValue("abcd", true)]);

        Assert.Empty(masker.Warnings);
    }

    [Theory]
    [InlineData("Authorization", true)]
    [InlineData("authorization", true)]
    [InlineData("Proxy-Authorization", true)]
    [InlineData("Cookie", true)]
    [InlineData("Set-Cookie", true)]
    [InlineData("X-Api-Key", true)]
    [InlineData("X-Auth-Token", true)]
    [InlineData("access_token", true)]
    [InlineData("client-secret", true)]
    [InlineData("password", true)]
    [InlineData("apikey", true)]
    [InlineData("api_key", true)]
    [InlineData("Content-Type", false)]
    [InlineData("Accept", false)]
    [InlineData("X-Request-Id", false)]
    [InlineData("page", false)]
    public void Sensitive_names_are_recognised_without_regard_to_case(string name, bool sensitive)
    {
        Assert.Equal(sensitive, SecretMasker.IsSensitiveName(name));
    }

    [Fact]
    public void A_sensitive_query_value_is_replaced_whole_and_other_pairs_are_untouched()
    {
        var masker = SecretMasker.For([]);

        Assert.Equal(
            "https://h.test/items?access_token=" + SecretMasker.Mask + "&page=2",
            masker.MaskUrl("https://h.test/items?access_token=abc&page=2"));
    }

    [Fact]
    public void A_sensitive_query_name_is_recognised_after_percent_decoding()
    {
        var masker = SecretMasker.For([]);

        Assert.Equal("https://h.test/?" + "to%6Ben=" + SecretMasker.Mask, masker.MaskUrl("https://h.test/?to%6Ben=abc"));
    }

    [Fact]
    public void A_sensitive_name_without_a_value_has_nothing_to_mask()
    {
        var masker = SecretMasker.For([]);

        Assert.Equal("https://h.test/?token", masker.MaskUrl("https://h.test/?token"));
    }

    [Fact]
    public void A_secret_in_the_url_path_is_masked_in_its_encoded_form_too()
    {
        var masker = SecretMasker.For([new ResolvedValue(Secret, true)]);

        Assert.Equal("https://h.test/" + SecretMasker.Mask + "/x?q=" + SecretMasker.Mask, masker.MaskUrl("https://h.test/pass%20word/x?q=pass+word"));
    }

    [Fact]
    public void Headers_with_a_sensitive_name_have_their_value_replaced_and_names_with_a_secret_are_masked()
    {
        var headers = new[]
        {
            new HeaderLine("Authorization", "Bearer anything"),
            new HeaderLine("Accept", "text/plain"),
            new HeaderLine("X-pass word", "shown"),
            new HeaderLine("X-Echo", "pass word"),
        };

        var masked = SecretMasker.For([new ResolvedValue(Secret, true)]).MaskHeaders(headers);

        Assert.Equal(
        [
            new HeaderLine("Authorization", SecretMasker.Mask),
            new HeaderLine("Accept", "text/plain"),
            new HeaderLine("X-" + SecretMasker.Mask, "shown"),
            new HeaderLine("X-Echo", SecretMasker.Mask),
        ], masked);
    }
}
