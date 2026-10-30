using PostmanClone.ExecutionService.Sending.Assembly;
using Xunit;
using static PostmanClone.ExecutionService.Tests.Sending.Assembly.AssemblyFixtures;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>The auth helper: header or query placement, the override warning, and the reference-only secret rule.</summary>
public sealed class AuthAssemblyTests
{
    private const string Url = "https://api.example.test/items";

    private static readonly IReadOnlyDictionary<string, ResolvedValue> Secrets = Variables(
        ("tok", "abc123", true),
        ("pw", "s3cret", true),
        ("k", "key-value-9", true));

    [Fact]
    public void A_bearer_token_reference_becomes_an_authorization_header()
    {
        var request = Assemble(Get(Url) with { Auth = Auth(AuthType.Bearer, bearerToken: "{{tok}}") });

        Assert.Equal(new HeaderLine("Authorization", "Bearer abc123"), Assert.Single(request.Headers));
        Assert.Empty(request.Warnings);
    }

    [Fact]
    public void A_bearer_reference_with_surrounding_whitespace_is_accepted()
    {
        var request = Assemble(Get(Url) with { Auth = Auth(AuthType.Bearer, bearerToken: "  {{tok}}\t") });

        Assert.Equal("Bearer abc123", Assert.Single(request.Headers).Value);
    }

    [Fact]
    public void The_auth_helper_replaces_a_same_name_row_and_says_so_in_a_warning()
    {
        var request = Assemble(Get(Url) with
        {
            Headers = [Row("authorization", "Bearer old")],
            Auth = Auth(AuthType.Bearer, bearerToken: "{{tok}}"),
        });

        Assert.Equal(new HeaderLine("Authorization", "Bearer abc123"), Assert.Single(request.Headers));
        Assert.Equal(["auth_overrides_header"], request.Warnings.Select(warning => warning.Code));
    }

    [Theory]
    [InlineData("literal-token")]
    [InlineData("Bearer {{tok}}")]
    [InlineData("{{tok}}{{tok}}")]
    [InlineData("{{tok}} trailing")]
    [InlineData("{{}}")]
    [InlineData("{{ tok }}")]
    public void A_literal_bearer_token_is_refused_with_secret_literal_not_allowed(string bearerToken)
    {
        var refused = Refused(() => Assemble(Get(Url) with { Auth = Auth(AuthType.Bearer, bearerToken: bearerToken) }));

        Assert.Equal(RequestAssemblyException.SecretLiteralNotAllowed, refused.Code);
    }

    [Fact]
    public void An_empty_bearer_token_applies_nothing()
    {
        var request = Assemble(Get(Url) with { Auth = Auth(AuthType.Bearer, bearerToken: null) });

        Assert.Empty(request.Headers);
    }

    [Fact]
    public void Basic_auth_sends_base64_of_user_colon_password_with_the_password_from_a_reference()
    {
        var request = Assemble(Get(Url) with { Auth = Auth(AuthType.Basic, username: "alice", password: "{{pw}}") });

        Assert.Equal("Basic YWxpY2U6czNjcmV0", Assert.Single(request.Headers).Value);
    }

    [Fact]
    public void Basic_auth_with_only_a_username_sends_the_username_and_an_empty_password()
    {
        var request = Assemble(Get(Url) with { Auth = Auth(AuthType.Basic, username: "alice") });

        Assert.Equal("Basic YWxpY2U6", Assert.Single(request.Headers).Value);
    }

    [Fact]
    public void Basic_auth_with_neither_part_applies_nothing()
    {
        var request = Assemble(Get(Url) with { Auth = Auth(AuthType.Basic, username: null, password: null) });

        Assert.Empty(request.Headers);
    }

    [Theory]
    [InlineData("hunter2")]
    [InlineData("{{pw}}!")]
    public void A_literal_basic_password_is_refused_with_secret_literal_not_allowed(string password)
    {
        var refused = Refused(() => Assemble(Get(Url) with { Auth = Auth(AuthType.Basic, username: "alice", password: password) }));

        Assert.Equal(RequestAssemblyException.SecretLiteralNotAllowed, refused.Code);
    }

    [Fact]
    public void A_basic_username_with_a_colon_is_refused_with_invalid_auth()
    {
        var refused = Refused(() => Assemble(Get(Url) with { Auth = Auth(AuthType.Basic, username: "al:ice", password: "{{pw}}") }));

        Assert.Equal(RequestAssemblyException.InvalidAuth, refused.Code);
    }

    [Fact]
    public void An_api_key_in_a_header_is_sent_under_its_name_and_overrides_a_row_of_that_name()
    {
        var request = Assemble(Get(Url) with
        {
            Headers = [Row("x-api-key", "stale")],
            Auth = Auth(AuthType.ApiKey, apiKeyName: "X-Api-Key", apiKeyValue: "{{k}}", apiKeyIn: ApiKeyLocation.Header),
        });

        Assert.Equal(new HeaderLine("X-Api-Key", "key-value-9"), Assert.Single(request.Headers));
        Assert.Equal(["auth_overrides_header"], request.Warnings.Select(warning => warning.Code));
    }

    [Fact]
    public void An_api_key_without_a_location_goes_in_a_header()
    {
        var request = Assemble(Get(Url) with
        {
            Auth = Auth(AuthType.ApiKey, apiKeyName: "X-Api-Key", apiKeyValue: "{{k}}", apiKeyIn: null),
        });

        Assert.Equal(new HeaderLine("X-Api-Key", "key-value-9"), Assert.Single(request.Headers));
    }

    [Fact]
    public void An_api_key_in_the_query_is_appended_after_the_urls_pairs()
    {
        var request = Assemble(Get("https://api.example.test/items?page=2") with
        {
            Params = [Row("sort", "name")],
            Auth = Auth(AuthType.ApiKey, apiKeyName: "api_token", apiKeyValue: "{{k}}", apiKeyIn: ApiKeyLocation.Query),
        });

        Assert.Equal("https://api.example.test/items?page=2&sort=name&api_token=key-value-9", request.Url);
        Assert.Empty(request.Headers);
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Content-Length")]
    [InlineData("Proxy-Authorization")]
    public void An_api_key_named_like_a_sender_header_is_refused_with_forbidden_header(string apiKeyName)
    {
        var refused = Refused(() => Assemble(Get(Url) with
        {
            Auth = Auth(AuthType.ApiKey, apiKeyName: apiKeyName, apiKeyValue: "{{k}}", apiKeyIn: ApiKeyLocation.Header),
        }));

        Assert.Equal(RequestAssemblyException.ForbiddenHeader, refused.Code);
    }

    [Fact]
    public void An_api_key_with_a_literal_value_is_refused_with_secret_literal_not_allowed()
    {
        var refused = Refused(() => Assemble(Get(Url) with
        {
            Auth = Auth(AuthType.ApiKey, apiKeyName: "X-Api-Key", apiKeyValue: "plain", apiKeyIn: ApiKeyLocation.Header),
        }));

        Assert.Equal(RequestAssemblyException.SecretLiteralNotAllowed, refused.Code);
    }

    [Fact]
    public void An_api_key_without_a_name_applies_nothing()
    {
        var request = Assemble(Get(Url) with { Auth = Auth(AuthType.ApiKey, apiKeyName: null, apiKeyValue: "{{k}}") });

        Assert.Empty(request.Headers);
        Assert.Equal(Url, request.Url);
    }

    [Fact]
    public void Auth_none_adds_nothing()
    {
        var request = Assemble(Get(Url));

        Assert.Empty(request.Headers);
        Assert.Empty(request.Warnings);
    }

    private static AssembledRequest Assemble(RequestDefinition definition) =>
        Assembler().Assemble(definition, Secrets);

    private static RequestAuth Auth(
        AuthType type,
        string? bearerToken = null,
        string? username = null,
        string? password = null,
        string? apiKeyName = null,
        string? apiKeyValue = null,
        ApiKeyLocation? apiKeyIn = null) =>
        new(type, bearerToken, username, password, apiKeyName, apiKeyValue, apiKeyIn);
}
