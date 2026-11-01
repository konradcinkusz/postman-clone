using System.Text;
using PostmanClone.ExecutionService.History;
using PostmanClone.Sending.Assembly;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.History;

/// <summary>The pure parts of the history: the body cut (ADR-0015) and the stored template's redaction (CONTRACTS §4).</summary>
public sealed class HistoryBodyAndRedactionTests
{
    [Fact]
    public void A_text_body_is_cut_to_the_byte_limit_on_a_character_boundary()
    {
        // Two-byte characters: 40,000 of them are 80,000 bytes, so a 65,536-byte cut keeps exactly 32,768 characters.
        var text = new string('é', 40_000);

        var cut = HistoryBodyLimit.CutText(text, 65_536);

        Assert.Equal(32_768, cut.Length);
        Assert.Equal(65_536, Encoding.UTF8.GetByteCount(cut));
        Assert.Equal(cut, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(cut)));
    }

    [Fact]
    public void A_four_byte_character_is_kept_whole_or_not_at_all()
    {
        // Each emoji is four bytes and two UTF-16 units. Ten bytes keep two of them (eight bytes); a third would be twelve.
        var text = string.Concat(Enumerable.Repeat("😀", 5));

        var cut = HistoryBodyLimit.CutText(text, 10);

        Assert.Equal(2, cut.EnumerateRunes().Count());
        Assert.Equal(8, Encoding.UTF8.GetByteCount(cut));
    }

    [Fact]
    public void A_base64_body_is_cut_on_a_four_character_group()
    {
        Assert.Equal("abcd", HistoryBodyLimit.CutBase64("abcdefghij", 7));
    }

    [Fact]
    public void The_stored_template_masks_sensitive_form_fields_and_drops_every_auth_value()
    {
        var masker = SecretMasker.For([new ResolvedValue("hidden-value-1", IsSecret: true)]);
        var request = new RequestDefinition(
            "POST",
            "https://a.test/login?password=literal-pw&next=/home",
            [new KeyValueRow("Token", "literal-token")],
            [new KeyValueRow("X-Trace", "carries hidden-value-1 inside")],
            new RequestBody(
                BodyType.FormUrlEncoded,
                null,
                null,
                [new KeyValueRow("password", "literal-form-pw"), new KeyValueRow("user", "maria")]),
            new RequestAuth(AuthType.Basic, null, "maria-login", "literal-basic-pw", null, null, null));

        var template = HistoryRedaction.Template(request, masker);

        Assert.Equal("https://a.test/login?password=••••••&next=/home", template.Url);
        Assert.Equal(SecretMasker.Mask, Assert.Single(template.Params).Value);
        Assert.Equal("carries •••••• inside", template.Headers[0].Value);
        Assert.Equal(SecretMasker.Mask, template.Body.Fields[0].Value);
        Assert.Equal("maria", template.Body.Fields[1].Value);

        Assert.Equal(AuthType.Basic, template.Auth.Type);
        Assert.Null(template.Auth.Username);
        Assert.Null(template.Auth.Password);
    }
}
