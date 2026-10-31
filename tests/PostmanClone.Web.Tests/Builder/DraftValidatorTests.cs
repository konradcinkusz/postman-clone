extern alias collections;

using System.Text.Json;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;
using PostmanClone.Web.Builder;
using Server = collections::PostmanClone.CollectionsService.Contracts;
using ServerValidator = collections::PostmanClone.CollectionsService.Domain.RequestDefinitionValidator;

namespace PostmanClone.Web.Tests.Builder;

/// <summary>
/// The client validator (WP19 acceptance): the shared cases file that CollectionsService's tests read too, and a parity test that
/// judges generated definitions with both validators and requires the same fields and codes (ADR-0017).
/// </summary>
public sealed class DraftValidatorTests
{
    private sealed record SharedCase(RequestDefinition Input, string? ExpectedCode);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Every_shared_case_gives_exactly_its_expected_code()
    {
        var path = Path.Combine(RepoRoot.Find(), "tests", "Shared", "validation-cases.json");
        var cases = JsonSerializer.Deserialize<List<SharedCase>>(File.ReadAllText(path), Options)!;

        Assert.NotEmpty(cases);
        foreach (var testCase in cases)
        {
            var codes = DraftValidator.Validate(testCase.Input).Select(issue => issue.Code).Distinct().ToArray();
            Assert.Equal(testCase.ExpectedCode is null ? Array.Empty<string>() : new[] { testCase.ExpectedCode }, codes);
        }
    }

    [Theory]
    [InlineData("{{a}}", true)]
    [InlineData("  {{a.b-c_1}}  ", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("Bearer abc", false)]
    [InlineData("{{a}} x", false)]
    [InlineData("{{a}}{{b}}", false)]
    [InlineData("{{}}", false)]
    [InlineData("{{a b}}", false)]
    public void A_secret_field_takes_only_a_reference(string? value, bool accepted)
    {
        Assert.Equal(accepted, DraftValidator.IsSecretReference(value));
    }

    [Fact]
    public void Each_refused_secret_is_named_by_its_field_and_no_message_repeats_the_value()
    {
        const string literal = "literal-value-that-must-not-echo"; // gitleaks:allow test fixture, not a credential
        var definition = new RequestDefinition(
            "GET",
            "https://api.example.test",
            [],
            [],
            new RequestBody("none", null, null, []),
            new RequestAuth("apiKey", literal, "u", literal, "X-Key", literal, "header"));

        var issues = DraftValidator.Validate(definition);

        Assert.Equal(
            new[] { "request.auth.bearerToken", "request.auth.password", "request.auth.apiKeyValue" },
            issues.Where(issue => issue.Code == DraftValidator.SecretLiteralCode).Select(issue => issue.Field).ToArray());
        Assert.All(issues, issue => Assert.DoesNotContain(literal, issue.Message));
    }

    [Fact]
    public void A_header_name_with_a_line_break_is_refused_and_a_value_with_one_is_not()
    {
        var definition = Definition(headers: [new KeyValue("X-A\r\nX-Evil", "1"), new KeyValue("X-B", "line\r\nbreak")]);

        var issues = DraftValidator.Validate(definition);

        var issue = Assert.Single(issues);
        Assert.Equal("request.headers[0].key", issue.Field);
    }

    [Fact]
    public void A_query_row_may_not_have_an_empty_name_but_may_have_an_empty_value()
    {
        var empty = DraftValidator.Validate(Definition(parameters: [new KeyValue(string.Empty, "x")]));
        var emptyValue = DraftValidator.Validate(Definition(parameters: [new KeyValue("k", string.Empty)]));

        Assert.Equal("request.params[0].key", Assert.Single(empty).Field);
        Assert.Empty(emptyValue);
    }

    [Fact]
    public void The_limits_of_the_service_are_the_limits_here()
    {
        Assert.Equal(8192, DraftValidator.MaximumUrlLength);
        Assert.Equal(100, DraftValidator.MaximumRows);
        Assert.Equal(256, DraftValidator.MaximumKeyLength);
        Assert.Equal(8192, DraftValidator.MaximumValueLength);
        Assert.Equal(500, DraftValidator.MaximumDescriptionLength);
        Assert.Equal(1_048_576, DraftValidator.MaximumRawLength);
        Assert.Equal(1_572_864, DraftValidator.MaximumJsonBytes);
    }

    [Fact]
    public void A_url_over_the_limit_is_refused_at_the_limit_plus_one()
    {
        var atLimit = Definition(url: "https://" + new string('a', DraftValidator.MaximumUrlLength - 8));
        var overLimit = Definition(url: "https://" + new string('a', DraftValidator.MaximumUrlLength - 7));

        Assert.Empty(DraftValidator.Validate(atLimit));
        Assert.Equal("request.url", Assert.Single(DraftValidator.Validate(overLimit)).Field);
    }

    [Fact]
    public void Parity_with_the_service_validator_holds_for_generated_definitions()
    {
        var random = new Random(170);
        for (var round = 0; round < 1500; round++)
        {
            var definition = Generate(random);
            var json = JsonSerializer.Serialize(definition, Options);

            var client = DraftValidator.Validate(definition).Select(issue => (issue.Field, issue.Code)).ToHashSet();
            var server = ServerIssues(json);

            Assert.True(
                client.SetEquals(server),
                $"Validators disagree on {json}\nclient: {string.Join(", ", client)}\nserver: {string.Join(", ", server)}");
        }
    }

    private static HashSet<(string Field, string Code)> ServerIssues(string json)
    {
        var definition = JsonSerializer.Deserialize<Server.RequestDefinition>(json, Options)!;
        return ServerValidator.Validate(definition).Issues.Select(issue => (issue.Field, issue.Code)).ToHashSet();
    }

    private static RequestDefinition Definition(
        string url = "https://api.example.test/items",
        List<KeyValue>? parameters = null,
        List<KeyValue>? headers = null) =>
        new("GET", url, parameters ?? [], headers ?? [], new RequestBody("none", null, null, []), new RequestAuth("none", null, null, null, null, null, null));

    /// <summary>A definition with a mix of valid and invalid parts, drawn from the seeded generator, so both validators see the same input.</summary>
    private static RequestDefinition Generate(Random random)
    {
        string[] methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "get", "TRACE"];
        string[] bodyTypes = ["none", "raw", "formUrlEncoded", "formData", "binary"];
        string[] languages = ["json", "text", "xml", "html", "javascript", "yaml"];
        string[] authTypes = ["none", "bearer", "basic", "apiKey", "oauth"];
        string[] locations = ["header", "query", "cookie"];
        string[] values = ["", "a", "{{token}}", "{{a.b-c}}", "Bearer x", "{{a}}{{b}}", "line\r\nbreak", "ü"];

        KeyValue Row() => new(
            random.Next(10) == 0 ? string.Empty : random.Next(8) == 0 ? "X-A\r\nB" : $"k{random.Next(5)}",
            values[random.Next(values.Length)],
            random.Next(2) == 0,
            random.Next(12) == 0 ? new string('d', random.Next(600)) : null);

        List<KeyValue> Rows(int max) => Enumerable.Range(0, random.Next(0, max)).Select(_ => Row()).ToList();

        RequestBody? body = random.Next(10) == 0 ? null : new RequestBody(
            bodyTypes[random.Next(bodyTypes.Length)],
            random.Next(3) == 0 ? null : languages[random.Next(languages.Length)],
            random.Next(3) == 0 ? null : values[random.Next(values.Length)],
            random.Next(3) == 0 ? null : Rows(4));
        RequestAuth? auth = random.Next(10) == 0 ? null : new RequestAuth(
            authTypes[random.Next(authTypes.Length)],
            random.Next(2) == 0 ? null : values[random.Next(values.Length)],
            "user",
            random.Next(2) == 0 ? null : values[random.Next(values.Length)],
            "X-Key",
            random.Next(2) == 0 ? null : values[random.Next(values.Length)],
            random.Next(4) == 0 ? null : locations[random.Next(locations.Length)]);
        var url = random.Next(12) == 0 ? string.Empty : "https://api.example.test/" + new string('p', random.Next(0, 40));

        return new RequestDefinition(
            methods[random.Next(methods.Length)],
            url,
            Rows(6),
            Rows(6),
            body,
            auth);
    }
}
