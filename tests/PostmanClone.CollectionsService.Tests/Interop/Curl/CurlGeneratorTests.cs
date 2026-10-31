using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Interop.Curl;
using PostmanClone.CollectionsService.Tests.Support;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Interop.Curl;

/// <summary>
/// The exporter (WP17, CONTRACTS §2). The proof that matters: a command it writes reads back as the same request, and a value
/// full of shell syntax stays inert text. No shell is started here. The tests check the quoting by reading the output back
/// through <see cref="CurlParser"/>, never by running it.
/// </summary>
public sealed class CurlGeneratorTests
{
    /// <summary>The payload the brief names: quoted, it is one header value, and a shell would read it as text.</summary>
    private const string Payload = "'; rm -rf / #";

    public static IEnumerable<object[]> CaseNames() => CurlCases.All.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Every_imported_command_exports_to_a_command_that_reads_back_as_the_same_request(string name)
    {
        var first = CurlParser.Parse(CurlCases.All[name].Command).Import!.Request;

        var exported = CurlGenerator.Generate(first);
        var second = CurlParser.Parse(exported);

        Assert.True(second.Import is not null, $"The export was refused: {second.Error}\n{exported}");
        Assert.Equal(Canonical(first), Canonical(second.Import!.Request));
    }

    [Fact]
    public void A_header_value_with_shell_syntax_stays_one_inert_value_and_reads_back_unchanged()
    {
        var definition = Simple(headers: [new KeyValue("X-Probe", Payload)]);

        var exported = CurlGenerator.Generate(definition);

        Assert.Contains("--header 'X-Probe: '\\''; rm -rf / #'", exported);
        var read = CurlParser.Parse(exported).Import!;
        Assert.Equal(Payload, Assert.Single(read.Request.Headers!).Value);
        Assert.Empty(read.Warnings);
    }

    [Theory]
    [InlineData("it's")]
    [InlineData("$(whoami)")]
    [InlineData("`id`")]
    [InlineData("a\"b\\c")]
    [InlineData("tab\there")]
    [InlineData("'''")]
    [InlineData("\\'")]
    [InlineData("é\U0001F600")]
    public void A_body_or_header_value_with_quotes_and_expansions_reads_back_exactly(string value)
    {
        var definition = Simple(
            headers: [new KeyValue("X-Value", value)],
            body: new RequestBody("raw", "text", value, []));

        var read = CurlParser.Parse(CurlGenerator.Generate(definition)).Import!;

        Assert.Equal(value, Assert.Single(read.Request.Headers!).Value);
        Assert.Equal(value, read.Request.Body!.Raw);
        Assert.Empty(read.Warnings);
    }

    [Fact]
    public void A_raw_body_with_line_breaks_reads_back_unchanged()
    {
        const string body = "{\n  \"a\": 1,\n  \"b\": \"it's\"\n}";
        var definition = Simple(
            headers: [new KeyValue("Content-Type", "application/json")],
            body: new RequestBody("raw", "json", body, []));

        var read = CurlParser.Parse(CurlGenerator.Generate(definition)).Import!;

        Assert.Equal(body, read.Request.Body!.Raw);
        Assert.Equal("json", read.Request.Body!.RawLanguage);
    }

    [Fact]
    public void A_hand_built_definition_with_every_part_reads_back_as_the_same_definition()
    {
        var definition = new RequestDefinition(
            "PUT",
            "https://api.example.invalid/{{base}}/items/1?page=2",
            [],
            [
                new KeyValue("Accept", "application/json"),
                new KeyValue("X-Empty", string.Empty),
                new KeyValue("X-Probe", Payload),
                new KeyValue("Authorization", "Bearer {{token}}"),
                new KeyValue("Content-Type", "application/json"),
            ],
            new RequestBody("raw", "json", "{\"text\":\"it's $(id)\"}", []),
            new RequestAuth("none", null, null, null, null, null, null));

        var read = CurlParser.Parse(CurlGenerator.Generate(definition)).Import!;

        Assert.Equal(Canonical(RequestDefinitionValidator.Normalize(definition)), Canonical(read.Request));
    }

    [Fact]
    public void Form_fields_go_out_as_literal_text_so_a_value_starting_with_at_is_not_a_file()
    {
        var definition = Simple(
            body: new RequestBody("formData", null, null, [new KeyValue("avatar", "@/etc/passwd"), new KeyValue("title", "x=y")]));

        var exported = CurlGenerator.Generate(definition);

        Assert.Contains("--form-string 'avatar=@/etc/passwd'", exported);
        var read = CurlParser.Parse(exported).Import!;
        Assert.Equal(new[] { "avatar=@/etc/passwd", "title=x=y" }, read.Request.Body!.Fields!.Select(row => $"{row.Key}={row.Value}"));
        Assert.Empty(read.Warnings);
    }

    [Fact]
    public void A_form_urlencoded_body_exports_as_its_encoded_text_and_reads_back_as_a_raw_body_with_the_same_bytes()
    {
        var definition = Simple(
            body: new RequestBody("formUrlEncoded", null, null, [new KeyValue("a", "1"), new KeyValue("b", "hello world&more")]));

        var exported = CurlGenerator.Generate(definition);

        Assert.Contains("--data-raw 'a=1&b=hello%20world%26more'", exported);
        var read = CurlParser.Parse(exported).Import!;
        Assert.Equal("raw", read.Request.Body!.Type);
        Assert.Equal("a=1&b=hello%20world%26more", read.Request.Body!.Raw);
    }

    [Fact]
    public void Disabled_rows_are_not_exported_and_enabled_query_parameters_join_the_url()
    {
        var definition = Simple(
            url: "https://api.example.invalid/search",
            @params: [new KeyValue("q", "a b"), new KeyValue("off", "1", Enabled: false), new KeyValue("tag", "{{tag}}")],
            headers: [new KeyValue("X-On", "1"), new KeyValue("X-Off", "1", Enabled: false)]);

        var exported = CurlGenerator.Generate(definition);

        Assert.Contains("--url 'https://api.example.invalid/search?q=a%20b&tag={{tag}}'", exported);
        Assert.DoesNotContain("off", exported);
        Assert.DoesNotContain("X-Off", exported);
        Assert.Contains("--header 'X-On: 1'", exported);
    }

    [Fact]
    public void Bearer_and_api_key_auth_export_as_headers_and_a_query_parameter_keeping_their_references()
    {
        var definition = Simple(
            url: "https://api.example.invalid/x",
            auth: new RequestAuth("bearer", "{{token}}", null, null, null, null, null));
        var apiKey = Simple(
            url: "https://api.example.invalid/x",
            auth: new RequestAuth("apiKey", null, null, null, "X-Key", "{{api_key}}", "header"));
        var query = Simple(
            url: "https://api.example.invalid/x",
            auth: new RequestAuth("apiKey", null, null, null, "key", "{{api_key}}", "query"));

        Assert.Contains("--header 'Authorization: Bearer {{token}}'", CurlGenerator.Generate(definition));
        Assert.Contains("--header 'X-Key: {{api_key}}'", CurlGenerator.Generate(apiKey));
        Assert.Contains("--url 'https://api.example.invalid/x?key={{api_key}}'", CurlGenerator.Generate(query));
    }

    [Fact]
    public void Basic_auth_exports_its_password_reference_and_reads_back_without_a_warning()
    {
        var definition = Simple(auth: new RequestAuth("basic", null, "alice", "{{password}}", null, null, null));

        var exported = CurlGenerator.Generate(definition);

        Assert.Contains("--user 'alice:{{password}}'", exported);
        var read = CurlParser.Parse(exported).Import!;
        Assert.Equal("basic", read.Request.Auth!.Type);
        Assert.Equal("{{password}}", read.Request.Auth!.Password);
        Assert.Empty(read.Warnings);
    }

    [Fact]
    public void A_head_request_exports_with_head_and_a_get_without_a_body_exports_with_its_method()
    {
        Assert.Contains("--head", CurlGenerator.Generate(Simple(method: "HEAD")));
        Assert.Contains("--request 'GET'", CurlGenerator.Generate(Simple(method: "GET")));
    }

    [Fact]
    public void A_nul_in_a_stored_value_is_written_as_a_replacement_character_and_never_as_a_nul()
    {
        var exported = CurlGenerator.Generate(Simple(headers: [new KeyValue("X-Nul", "a\0b")]));

        Assert.DoesNotContain('\0', exported);
        Assert.Contains("a�b", exported);
    }

    [Fact]
    public void Quote_wraps_in_single_quotes_and_escapes_each_single_quote()
    {
        Assert.Equal("'plain'", CurlQuoting.Quote("plain"));
        Assert.Equal("'it'\\''s'", CurlQuoting.Quote("it's"));
        Assert.Equal("''", CurlQuoting.Quote(string.Empty));
    }

    [Fact]
    public void Escape_keeps_unreserved_characters_and_braces_and_encodes_the_rest()
    {
        Assert.Equal("a-b_c.d~e{{x}}", CurlQuoting.Escape("a-b_c.d~e{{x}}"));
        Assert.Equal("a%20b%26c%3D%C3%A9", CurlQuoting.Escape("a b&c=é"));
    }

    [Theory]
    [InlineData("https://a.example.invalid/x", "k=v", "https://a.example.invalid/x?k=v")]
    [InlineData("https://a.example.invalid/x?a=1", "k=v", "https://a.example.invalid/x?a=1&k=v")]
    [InlineData("https://a.example.invalid/x?", "k=v", "https://a.example.invalid/x?k=v")]
    [InlineData("https://a.example.invalid/x?a=1&", "k=v", "https://a.example.invalid/x?a=1&k=v")]
    [InlineData("https://a.example.invalid/x#frag", "k=v", "https://a.example.invalid/x?k=v#frag")]
    [InlineData("https://a.example.invalid/x", "", "https://a.example.invalid/x")]
    public void A_query_joins_the_url_before_any_fragment(string url, string query, string expected) =>
        Assert.Equal(expected, CurlQuoting.AppendQuery(url, query));

    /// <summary>The canonical JSON of a definition: what "the same request" means here (the name is not part of it).</summary>
    private static string Canonical(RequestDefinition definition) =>
        JsonSerializer.Serialize(RequestDefinitionValidator.Normalize(definition), JsonSerializerOptions.Web);

    private static RequestDefinition Simple(
        string method = "POST",
        string url = "https://api.example.invalid/items",
        IReadOnlyList<KeyValue>? @params = null,
        IReadOnlyList<KeyValue>? headers = null,
        RequestBody? body = null,
        RequestAuth? auth = null) => new(
        method,
        url,
        @params ?? [],
        headers ?? [],
        body ?? new RequestBody("none", null, null, []),
        auth ?? new RequestAuth("none", null, null, null, null, null, null));
}
