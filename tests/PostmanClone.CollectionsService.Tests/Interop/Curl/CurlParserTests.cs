using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Interop.Curl;
using PostmanClone.CollectionsService.Tests.Support;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Interop.Curl;

/// <summary>
/// The cURL importer (WP17): one case per real-world command shape in <see cref="CurlCases"/>, the commands that must be
/// refused, the language of a raw body, and the password rules. The parser is pure logic, so these tests call it directly.
/// </summary>
public sealed class CurlParserTests
{
    public static IEnumerable<object[]> CaseNames() => CurlCases.All.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Imports_the_command_as_its_case_describes(string name)
    {
        var expected = CurlCases.All[name];

        var result = CurlParser.Parse(expected.Command);

        Assert.True(result.Import is not null, $"The command was refused: {result.Error}");
        var import = result.Import!;
        var request = import.Request;
        Assert.Equal(expected.Method, request.Method);
        Assert.Equal(expected.Url, request.Url);
        Assert.Equal(expected.Body, request.Body?.Type);
        if (expected.Raw is not null)
        {
            Assert.Equal(expected.Raw, request.Body?.Raw);
        }

        Assert.Equal(expected.Headers ?? [], (request.Headers ?? []).Select(row => $"{row.Key}: {row.Value}"));
        Assert.Equal(expected.Fields ?? [], (request.Body?.Fields ?? []).Select(row => $"{row.Key}={row.Value}"));
        Assert.Equal(expected.Auth, AuthText(request.Auth));
        Assert.Equal(expected.Codes ?? [], import.Warnings.Select(warning => warning.Code));
    }

    [Theory]
    [InlineData("wget https://api.example.invalid/", "must start with curl")]
    [InlineData("   ", "empty")]
    [InlineData("curl -X POST", "no URL")]
    [InlineData("curl -H 'X-A: 1 https://api.example.invalid/", "single quote")]
    [InlineData("curl -d \"abc https://api.example.invalid/", "double quote")]
    [InlineData("curl -d $'abc https://api.example.invalid/", "$' quote")]
    [InlineData("curl https://api.example.invalid/\0", "NUL")]
    public void Refuses_text_that_is_not_a_readable_command(string command, string reason)
    {
        var result = CurlParser.Parse(command);

        Assert.Null(result.Import);
        Assert.NotNull(result.Error);
        Assert.Contains(reason, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("application/json", "json")]
    [InlineData("application/vnd.api+json", "json")]
    [InlineData("text/xml", "xml")]
    [InlineData("application/xml; charset=utf-8", "xml")]
    [InlineData("text/html", "html")]
    [InlineData("text/javascript", "javascript")]
    [InlineData("text/plain", "text")]
    [InlineData("application/x-www-form-urlencoded", "text")]
    public void A_raw_body_takes_its_language_from_the_content_type(string contentType, string language)
    {
        var result = CurlParser.Parse($"curl https://api.example.invalid/x -H \"Content-Type: {contentType}\" -d 'body'");

        Assert.NotNull(result.Import);
        Assert.Equal("raw", result.Import.Request.Body?.Type);
        Assert.Equal(language, result.Import.Request.Body?.RawLanguage);
    }

    [Fact]
    public void A_raw_body_without_a_content_type_is_text()
    {
        var result = CurlParser.Parse("curl https://api.example.invalid/x -d 'body'");

        Assert.Equal("text", result.Import!.Request.Body?.RawLanguage);
    }

    [Fact]
    public void A_literal_password_is_never_stored_and_the_warning_names_the_option()
    {
        const string literal = "hunter2"; // gitleaks:allow

        var result = CurlParser.Parse($"curl -u alice:{literal} https://api.example.invalid/me"); // gitleaks:allow

        var import = result.Import!;
        var stored = JsonSerializer.Serialize(import.Request, JsonSerializerOptions.Web);
        Assert.DoesNotContain(literal, stored, StringComparison.Ordinal);
        var warning = Assert.Single(import.Warnings);
        Assert.Equal("secret_not_imported", warning.Code);
        Assert.Equal("command[1]", warning.Path);
        Assert.DoesNotContain(literal, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_in_the_url_is_never_stored()
    {
        const string literal = "hunter2"; // gitleaks:allow

        var result = CurlParser.Parse($"curl https://alice:{literal}@api.example.invalid/a"); // gitleaks:allow

        var import = result.Import!;
        Assert.Equal("https://alice:{{password}}@api.example.invalid/a", import.Request.Url);
        Assert.DoesNotContain(literal, JsonSerializer.Serialize(import.Request, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_warning_points_at_the_word_it_is_about()
    {
        var result = CurlParser.Parse("curl -k https://api.example.invalid/a -H 'X-Broken' -A 'agent'");

        var paths = result.Import!.Warnings.Select(warning => (warning.Code, warning.Path)).ToArray();
        Assert.Equal(
            new[]
            {
                ("option_ignored", "command[1]"),
                ("invalid_argument", "command[3]"),
                ("option_ignored", "command[5]"),
            },
            paths);
    }

    [Fact]
    public void The_name_is_the_method_and_the_url_without_its_query()
    {
        var result = CurlParser.Parse("curl -X POST 'https://api.example.invalid/items?page=2#top'");

        Assert.Equal("POST https://api.example.invalid/items", result.Import!.Name);
    }

    [Fact]
    public void A_name_with_no_url_text_falls_back_to_the_method()
    {
        var result = CurlParser.Parse("curl -X PUT '?only=query'");

        Assert.Equal("PUT", result.Import!.Name);
    }

    [Fact]
    public void A_bearer_header_with_a_literal_is_kept_as_a_plain_row_and_reported()
    {
        var result = CurlParser.Parse("curl -H 'Authorization: Bearer ghp_example_literal' https://api.example.invalid/"); // gitleaks:allow

        var import = result.Import!;
        Assert.Equal("none", import.Request.Auth!.Type);
        Assert.Equal("Authorization", Assert.Single(import.Request.Headers!).Key);
        Assert.Equal("sensitive_value_kept", Assert.Single(import.Warnings).Code);
    }

    [Fact]
    public void The_last_method_option_wins_and_its_case_is_normalised()
    {
        var result = CurlParser.Parse("curl -X POST -X PATCH -X put https://api.example.invalid/a");

        Assert.Equal("PUT", result.Import!.Request.Method);
    }

    [Fact]
    public void The_validator_is_left_to_the_request_service_so_an_odd_method_is_still_judged()
    {
        var result = CurlParser.Parse("curl -X 'GE T' https://api.example.invalid/a");
        var request = result.Import!.Request;

        Assert.Equal("GE T", request.Method);
        Assert.Contains(RequestDefinitionValidator.Validate(request).Issues, issue => issue.Field == "request.method");
    }

    private static string AuthText(RequestAuth? auth) =>
        auth is { Type: "basic" } basic ? $"basic|{basic.Username}|{basic.Password ?? "<none>"}" : auth?.Type ?? "none";
}
