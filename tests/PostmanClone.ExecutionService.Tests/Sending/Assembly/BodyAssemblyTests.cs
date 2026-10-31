using System.Text;
using System.Text.RegularExpressions;
using PostmanClone.ExecutionService.Sending.Assembly;
using Xunit;
using static PostmanClone.ExecutionService.Tests.Sending.Assembly.AssemblyFixtures;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>Body encoding: default content types, form encoding, the multipart boundary and the size limit.</summary>
public sealed class BodyAssemblyTests
{
    private const string Url = "https://api.example.test/upload";
    private const int OneMebibyte = 1_048_576;

    private static readonly IReadOnlyDictionary<string, ResolvedValue> Vars = Variables(("t", "v", false));

    [Theory]
    [InlineData(RawLanguage.Json, "application/json")]
    [InlineData(RawLanguage.Text, "text/plain")]
    [InlineData(RawLanguage.Xml, "application/xml")]
    [InlineData(RawLanguage.Html, "text/html")]
    [InlineData(RawLanguage.Javascript, "application/javascript")]
    public void A_raw_body_gets_the_default_content_type_of_its_language(RawLanguage language, string contentType)
    {
        var request = Assemble(Post(new RequestBody(BodyType.Raw, language, "{}", [])));

        Assert.Equal(new HeaderLine("Content-Type", contentType), Assert.Single(request.Headers));
    }

    [Fact]
    public void A_raw_body_without_a_language_sends_no_default_content_type()
    {
        var request = Assemble(Post(new RequestBody(BodyType.Raw, null, "x", [])));

        Assert.Empty(request.Headers);
        Assert.Equal("x", Encoding.UTF8.GetString(request.Body!));
    }

    [Fact]
    public void A_content_type_the_user_wrote_is_kept_and_not_duplicated()
    {
        var request = Assemble(Post(new RequestBody(BodyType.Raw, RawLanguage.Json, "{}", [])) with
        {
            Headers = [Row("content-type", "application/vnd.api+json")],
        });

        Assert.Equal(new HeaderLine("content-type", "application/vnd.api+json"), Assert.Single(request.Headers));
        Assert.Empty(request.Warnings);
    }

    [Fact]
    public void A_raw_body_expands_its_references_and_is_utf8_encoded()
    {
        var request = Assemble(Post(new RequestBody(BodyType.Raw, RawLanguage.Text, "héllo {{t}} 🙂", [])));

        Assert.Equal("héllo v 🙂", Encoding.UTF8.GetString(request.Body!));
        Assert.Equal("héllo v 🙂"u8.ToArray(), request.Body);
    }

    [Fact]
    public void A_raw_body_of_exactly_one_mebibyte_is_accepted()
    {
        var request = Assemble(Post(new RequestBody(BodyType.Raw, RawLanguage.Text, new string('a', OneMebibyte), [])));

        Assert.Equal(OneMebibyte, request.Body!.Length);
    }

    [Fact]
    public void A_raw_body_one_byte_over_the_limit_is_refused_with_body_too_large()
    {
        var refused = Refused(() => Assemble(Post(new RequestBody(BodyType.Raw, RawLanguage.Text, new string('a', OneMebibyte + 1), []))));

        Assert.Equal(RequestAssemblyException.BodyTooLarge, refused.Code);
    }

    [Fact]
    public void The_limit_counts_bytes_not_characters()
    {
        var accepted = Assemble(Post(new RequestBody(BodyType.Raw, RawLanguage.Text, new string('é', OneMebibyte / 2), [])));
        var refused = Refused(() => Assemble(Post(new RequestBody(BodyType.Raw, RawLanguage.Text, new string('é', OneMebibyte / 2 + 1), []))));

        Assert.Equal(OneMebibyte, accepted.Body!.Length);
        Assert.Equal(RequestAssemblyException.BodyTooLarge, refused.Code);
    }

    [Fact]
    public void Form_url_encoding_writes_space_as_plus_and_encodes_reserved_characters()
    {
        var fields = new[]
        {
            Row("a", "1"),
            Row("b", "x y"),
            Row("c", "&=+%"),
            Row("off", "skipped", enabled: false),
            Row("", "no key"),
        };

        var request = Assemble(Post(new RequestBody(BodyType.FormUrlEncoded, null, null, fields)));

        Assert.Equal("a=1&b=x+y&c=%26%3D%2B%25", Encoding.UTF8.GetString(request.Body!));
        Assert.Equal(new HeaderLine("Content-Type", "application/x-www-form-urlencoded"), Assert.Single(request.Headers));
    }

    [Fact]
    public void Form_url_encoding_expands_references_in_names_and_values()
    {
        var request = Assemble(Post(new RequestBody(BodyType.FormUrlEncoded, null, null, [Row("{{t}}", "{{t}}!")])));

        Assert.Equal("v=v%21", Encoding.UTF8.GetString(request.Body!));
    }

    [Fact]
    public void Multipart_body_has_the_exact_layout_with_the_boundary_from_the_content_type()
    {
        var fields = new[] { Row("a", "1"), Row("b", "two words") };

        var request = Assemble(Post(new RequestBody(BodyType.FormData, null, null, fields)), new SeededRandomBytes(7));

        var boundary = BoundaryOf(request);
        var expected = $"--{boundary}\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\n1\r\n"
            + $"--{boundary}\r\nContent-Disposition: form-data; name=\"b\"\r\n\r\ntwo words\r\n"
            + $"--{boundary}--\r\n";
        Assert.Equal(expected, Encoding.UTF8.GetString(request.Body!));
    }

    [Fact]
    public void Multipart_boundaries_are_repeatable_for_the_same_injected_source_and_differ_for_another()
    {
        var body = new RequestBody(BodyType.FormData, null, null, [Row("a", "1")]);

        var first = Assemble(Post(body), new SeededRandomBytes(7));
        var again = Assemble(Post(body), new SeededRandomBytes(7));
        var other = Assemble(Post(body), new SeededRandomBytes(8));

        Assert.Equal(first.Body, again.Body);
        Assert.NotEqual(BoundaryOf(first), BoundaryOf(other));
    }

    [Fact]
    public void The_default_boundary_comes_from_the_cryptographic_source_and_has_128_bits()
    {
        var first = Assemble(Post(new RequestBody(BodyType.FormData, null, null, [])), CryptographicRandomBytes.Instance);
        var second = Assemble(Post(new RequestBody(BodyType.FormData, null, null, [])), CryptographicRandomBytes.Instance);

        Assert.Matches("^----PostmanClone[0-9a-f]{32}$", BoundaryOf(first));
        Assert.NotEqual(BoundaryOf(first), BoundaryOf(second));
    }

    [Fact]
    public void A_multipart_body_replaces_a_content_type_row_and_says_so()
    {
        var request = Assemble(Post(new RequestBody(BodyType.FormData, null, null, [Row("a", "1")])) with
        {
            Headers = [Row("Content-Type", "text/plain")],
        });

        var contentType = Assert.Single(request.Headers, header => HeaderBuilder.SameName(header.Name, "Content-Type"));
        Assert.StartsWith("multipart/form-data; boundary=", contentType.Value, StringComparison.Ordinal);
        Assert.Equal(["content_type_overridden"], request.Warnings.Select(warning => warning.Code));
    }

    [Fact]
    public void Quotes_and_line_breaks_in_a_multipart_field_name_are_percent_encoded()
    {
        var request = Assemble(Post(new RequestBody(BodyType.FormData, null, null, [Row("a\"b\r\nc", "v")])), new SeededRandomBytes(3));

        var body = Encoding.UTF8.GetString(request.Body!);
        Assert.Contains("name=\"a%22b%0D%0Ac\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_and_keyless_multipart_rows_are_left_out()
    {
        var fields = new[] { Row("off", "x", enabled: false), Row("", "y"), Row("keep", "k") };

        var request = Assemble(Post(new RequestBody(BodyType.FormData, null, null, fields)), new SeededRandomBytes(3));

        var body = Encoding.UTF8.GetString(request.Body!);
        Assert.Single(Regex.Matches(body, "Content-Disposition"));
        Assert.Contains("name=\"keep\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("off", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_body_of_type_none_sends_no_body_and_no_content_type()
    {
        var request = Assemble(Post(new RequestBody(BodyType.None, RawLanguage.Json, "ignored", [])));

        Assert.Null(request.Body);
        Assert.Empty(request.Headers);
    }

    private static RequestDefinition Post(RequestBody body) => Get("https://api.example.test/upload") with { Method = "POST", Body = body };

    private static AssembledRequest Assemble(RequestDefinition definition, IRandomBytes? random = null) =>
        Assembler(random).Assemble(definition, Vars);

    private static string BoundaryOf(AssembledRequest request)
    {
        var contentType = request.Headers.Single(header => HeaderBuilder.SameName(header.Name, "Content-Type")).Value;
        return contentType["multipart/form-data; boundary=".Length..];
    }
}
