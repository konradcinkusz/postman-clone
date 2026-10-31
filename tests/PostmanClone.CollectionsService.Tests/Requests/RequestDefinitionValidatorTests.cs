using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Tests.Support;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Requests;

/// <summary>
/// Every rule of a <c>RequestDefinition</c> (CONTRACTS §1, WP13), with both sides of every limit. The validator is pure
/// logic, so these tests call it directly and need no host.
/// </summary>
public sealed class RequestDefinitionValidatorTests
{
    private const string Invalid = RequestDefinitionValidator.InvalidCode;

    private const string Secret = RequestDefinitionValidator.SecretLiteralCode;

    public static IEnumerable<object[]> RuleNames() => AllRules.Select(rule => new object[] { rule.Name });

    [Theory]
    [MemberData(nameof(RuleNames))]
    public void Each_rule_accepts_or_names_the_broken_field_with_its_code(string name)
    {
        var rule = AllRules.Single(candidate => candidate.Name == name);
        var issues = RequestDefinitionValidator.Validate(rule.Build()).Issues;

        if (rule.Code is null)
        {
            Assert.Empty(issues);
        }
        else
        {
            Assert.Contains(issues, issue => issue.Code == rule.Code && issue.Field == rule.Field);
        }
    }

    [Fact]
    public void Every_broken_rule_is_reported_not_only_the_first()
    {
        var broken = Valid() with { Method = "get", Url = string.Empty };

        var issues = RequestDefinitionValidator.Validate(broken).Issues;

        Assert.Equal(new[] { "request.method", "request.url" }, issues.Select(issue => issue.Field).Order(StringComparer.Ordinal).ToArray());
        Assert.All(issues, issue => Assert.Equal(Invalid, issue.Code));
    }

    [Fact]
    public void A_secret_message_never_repeats_the_literal_it_refused()
    {
        var issues = RequestDefinitionValidator.Validate(WithBearer("Bearer abc-literal")).Issues;

        var issue = Assert.Single(issues);
        Assert.Equal(Secret, issue.Code);
        Assert.DoesNotContain("abc-literal", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_gives_null_lists_and_objects_their_empty_form_and_a_null_value_an_empty_one()
    {
        var sparse = new RequestDefinition(
            "GET",
            "https://example.invalid",
            null,
            [new KeyValue("X-Null", null!)],
            null,
            null);

        var normalized = RequestDefinitionValidator.Normalize(sparse);

        Assert.Empty(normalized.Params!);
        Assert.Equal(string.Empty, normalized.Headers![0].Value);
        Assert.Equal("none", normalized.Body!.Type);
        Assert.Empty(normalized.Body.Fields!);
        Assert.Equal("none", normalized.Auth!.Type);
        Assert.Empty(RequestDefinitionValidator.Validate(sparse).Issues);
    }

    [Fact]
    public void A_definition_of_exactly_the_size_limit_is_accepted_and_one_byte_more_is_too_large()
    {
        var atLimit = RequestSizing.OfSerializedSize(RequestDefinitionLimits.MaxJsonBytes);

        Assert.Equal(RequestDefinitionLimits.MaxJsonBytes, JsonSerializer.SerializeToUtf8Bytes(atLimit, JsonSerializerOptions.Web).Length);
        Assert.False(RequestDefinitionValidator.ExceedsMaxSize(atLimit));
        Assert.Empty(RequestDefinitionValidator.Validate(atLimit).Issues);

        var overLimit = RequestSizing.OfSerializedSize(RequestDefinitionLimits.MaxJsonBytes + 1);

        Assert.True(RequestDefinitionValidator.ExceedsMaxSize(overLimit));
    }

    private static RequestDefinition Valid() => RequestCalls.Sample();

    private static RequestDefinition WithBody(RequestBody body) => Valid() with { Body = body };

    private static RequestDefinition WithAuth(RequestAuth auth) => Valid() with { Auth = auth };

    private static RequestDefinition WithBearer(string? token) =>
        WithAuth(new RequestAuth("bearer", token, null, null, null, null, null));

    private static RequestDefinition WithHeaders(params KeyValue[] rows) => Valid() with { Headers = rows };

    private static RequestDefinition WithParams(params KeyValue[] rows) => Valid() with { Params = rows };

    private static RequestDefinition WithFormBody(string type, int fieldCount) =>
        WithBody(new RequestBody(type, null, null, Rows(fieldCount)));

    private static IReadOnlyList<KeyValue> Rows(int count) =>
        Enumerable.Range(0, count).Select(index => new KeyValue($"k{index}", "v")).ToList();

    private static string Chars(int count) => new('x', count);

    private static Rule Accepted(string name, Func<RequestDefinition> build) => new(name, build);

    private static Rule Refused(string name, Func<RequestDefinition> build, string field, string code = Invalid) => new(name, build, code, field);

    private static readonly List<Rule> AllRules =
    [
        // Method and URL (CONTRACTS §1)
        Accepted("method GET is accepted", Valid),
        Refused("method is case sensitive", () => Valid() with { Method = "get" }, "request.method"),
        Refused("method outside the seven is refused", () => Valid() with { Method = "CONNECT" }, "request.method"),
        Accepted("url of one character is accepted", () => Valid() with { Url = "x" }),
        Refused("empty url is refused", () => Valid() with { Url = string.Empty }, "request.url"),
        Accepted("url of 8192 characters is accepted", () => Valid() with { Url = Chars(8192) }),
        Refused("url of 8193 characters is refused", () => Valid() with { Url = Chars(8193) }, "request.url"),

        // Key/value rows: key 1..256, value 0..8192, description 0..500, at most 100 rows per list
        Accepted("param key of 256 characters is accepted", () => WithParams(new KeyValue(Chars(256), "1"))),
        Refused("param key of 257 characters is refused", () => WithParams(new KeyValue(Chars(257), "1")), "request.params[0].key"),
        Refused("empty param key is refused", () => WithParams(new KeyValue(string.Empty, "1")), "request.params[0].key"),
        Accepted("empty value is accepted", () => WithHeaders(new KeyValue("X-Empty", string.Empty))),
        Accepted("header value of 8192 characters is accepted", () => WithHeaders(new KeyValue("X-Long", Chars(8192)))),
        Refused("header value of 8193 characters is refused", () => WithHeaders(new KeyValue("X-Long", Chars(8193))), "request.headers[0].value"),
        Accepted("description of 500 characters is accepted", () => WithHeaders(new KeyValue("X-A", "1", true, Chars(500)))),
        Refused("description of 501 characters is refused", () => WithHeaders(new KeyValue("X-A", "1", true, Chars(501))), "request.headers[0].description"),
        Accepted("100 param rows are accepted", () => Valid() with { Params = Rows(100) }),
        Refused("101 param rows are refused", () => Valid() with { Params = Rows(101) }, "request.params"),
        Refused("header name with CRLF is refused", () => WithHeaders(new KeyValue("X-A\r\nX-Evil", "1")), "request.headers[0].key"),
        Refused("header name with LF is refused", () => WithHeaders(new KeyValue("X-A\nX-B", "1")), "request.headers[0].key"),
        Accepted("header name with a hyphen is accepted", () => WithHeaders(new KeyValue("X-Request-Id", "1"))),

        // Body: type, raw only with raw, rawLanguage only with raw, fields only with form bodies (CONTRACTS §1)
        Accepted("none body with no fields is accepted", Valid),
        Refused("body type outside the four is refused", () => WithBody(new RequestBody("binary", null, null, [])), "request.body.type"),
        Accepted("raw json body is accepted", () => WithBody(new RequestBody("raw", "json", "{}", []))),
        Refused("raw body without a language is refused", () => WithBody(new RequestBody("raw", null, "{}", [])), "request.body.rawLanguage"),
        Refused("raw body with an unknown language is refused", () => WithBody(new RequestBody("raw", "markdown", "x", [])), "request.body.rawLanguage"),
        Accepted("raw body of 1048576 characters is accepted", () => WithBody(new RequestBody("raw", "text", Chars(1_048_576), []))),
        Refused("raw body of 1048577 characters is refused", () => WithBody(new RequestBody("raw", "text", Chars(1_048_577), [])), "request.body.raw"),
        Refused("raw text on a none body is refused", () => WithBody(new RequestBody("none", null, "{}", [])), "request.body.raw"),
        Accepted("empty raw text on a none body is accepted", () => WithBody(new RequestBody("none", null, string.Empty, []))),
        Refused("rawLanguage on a none body is refused", () => WithBody(new RequestBody("none", "json", null, [])), "request.body.rawLanguage"),
        Accepted("form-urlencoded fields are accepted", () => WithFormBody("formUrlEncoded", 2)),
        Accepted("form-data text fields are accepted", () => WithFormBody("formData", 2)),
        Refused("fields on a raw body are refused", () => WithBody(new RequestBody("raw", "json", "{}", Rows(1))), "request.body.fields"),
        Refused("fields on a none body are refused", () => WithBody(new RequestBody("none", null, null, Rows(1))), "request.body.fields"),
        Accepted("100 form fields are accepted", () => WithFormBody("formData", 100)),
        Refused("101 form fields are refused", () => WithFormBody("formData", 101), "request.body.fields"),
        Accepted("missing body and auth are the empty ones", () => Valid() with { Body = null, Auth = null }),
        Accepted("null param list is the empty one", () => Valid() with { Params = null }),

        // Auth type and API key location (CONTRACTS §1)
        Refused("auth type outside the four is refused", () => WithAuth(new RequestAuth("oauth", null, null, null, null, null, null)), "request.auth.type"),
        Accepted("api key in query is accepted", () => WithAuth(new RequestAuth("apiKey", null, null, null, "api_key", "{{key}}", "query"))),
        Accepted("api key location null is accepted", () => WithAuth(new RequestAuth("apiKey", null, null, null, "api_key", "{{key}}", null))),
        Refused("api key location cookie is refused", () => WithAuth(new RequestAuth("apiKey", null, null, null, "k", "{{key}}", "cookie")), "request.auth.apiKeyIn"),

        // Secret literal rule (CONTRACTS §1, ADR-0006): empty or exactly one {{name}} reference
        Accepted("empty bearer token is accepted", () => WithBearer(string.Empty)),
        Accepted("null bearer token is accepted", () => WithBearer(null)),
        Accepted("bearer reference is accepted", () => WithBearer("{{token}}")),
        Accepted("reference with dot dash and underscore is accepted", () => WithBearer("{{ok.var-1}}")),
        Accepted("reference of 100 name characters is accepted", () => WithBearer("{{" + Chars(100) + "}}")),
        Refused("reference of 101 name characters is refused", () => WithBearer("{{" + Chars(101) + "}}"), "request.auth.bearerToken", Secret),
        Accepted("reference with surrounding spaces is accepted", () => WithBearer("  {{a}}  ")),
        Refused("bearer literal is refused", () => WithBearer("Bearer abc"), "request.auth.bearerToken", Secret),
        Refused("text beside a reference is refused", () => WithBearer("{{a}} x"), "request.auth.bearerToken", Secret),
        Refused("two references are refused", () => WithBearer("{{a}}{{b}}"), "request.auth.bearerToken", Secret),
        Refused("a reference with a space in the name is refused", () => WithBearer("{{a b}}"), "request.auth.bearerToken", Secret),
        Accepted("password reference is accepted", () => WithAuth(new RequestAuth("basic", null, "user", "{{password}}", null, null, null))),
        Refused("password literal is refused", () => WithAuth(new RequestAuth("basic", null, "user", "not-a-variable", null, null, null)), "request.auth.password", Secret),
        Refused("api key value literal is refused", () => WithAuth(new RequestAuth("apiKey", null, null, null, "k", "literal", "header")), "request.auth.apiKeyValue", Secret),
        Refused("a literal is refused whatever the auth type", () => WithAuth(new RequestAuth("none", "Bearer abc", null, null, null, null, null)), "request.auth.bearerToken", Secret),
    ];

    private sealed record Rule(string Name, Func<RequestDefinition> Build, string? Code = null, string? Field = null);
}
