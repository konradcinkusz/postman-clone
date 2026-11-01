using PostmanClone.Sending.Assembly;
using Xunit;
using static PostmanClone.ExecutionService.Tests.Sending.Assembly.AssemblyFixtures;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>The whole assembly: fields expanded, limits enforced, unresolved names reported, warnings merged.</summary>
public sealed class RequestAssemblerTests
{
    private static readonly IReadOnlyDictionary<string, ResolvedValue> Vars = Variables(
        ("base", "https://api.example.test", false),
        ("a", "1", false),
        ("b", "2", false),
        ("token", "abc123", true));

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void Each_allowed_method_is_kept_as_written(string method)
    {
        var request = Assemble(Get("https://api.example.test/") with { Method = method });

        Assert.Equal(method, request.Method);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    [InlineData("")]
    public void A_method_outside_the_allowed_set_is_refused_with_invalid_method(string method)
    {
        var refused = Refused(() => Assemble(Get("https://api.example.test/") with { Method = method }));

        Assert.Equal(RequestAssemblyException.InvalidMethod, refused.Code);
    }

    [Theory]
    [InlineData("{{base}}/items", "https://api.example.test/items")]
    [InlineData("{{base}}/{{a}}/{{b}}", "https://api.example.test/1/2")]
    [InlineData("{{base}}/{{a}}{{b}}", "https://api.example.test/12")]
    [InlineData("{{base}}/items?x={{a}}#frag", "https://api.example.test/items?x=1")]
    public void References_in_the_url_are_expanded_before_the_query_is_built(string url, string expected)
    {
        Assert.Equal(expected, Assemble(Get(url)).Url);
    }

    [Fact]
    public void Unresolved_references_stay_in_the_text_and_are_reported_in_order_across_fields()
    {
        var definition = Get("https://api.example.test/{{missing}}") with
        {
            Params = [Row("q", "{{later}}")],
            Headers = [Row("X-One", "{{missing}}")],
        };

        var request = Assemble(definition);

        Assert.Equal("https://api.example.test/{{missing}}?q=%7B%7Blater%7D%7D", request.Url);
        Assert.Equal(["missing", "later"], request.UnresolvedVariables);
    }

    [Fact]
    public void Enabled_params_keep_duplicate_keys_in_order_and_skip_disabled_and_keyless_rows()
    {
        var definition = Get("https://api.example.test/") with
        {
            Params = [Row("a", "1"), Row("off", "x", enabled: false), Row("", "orphan"), Row("a", "2")],
        };

        Assert.Equal("https://api.example.test/?a=1&a=2", Assemble(definition).Url);
    }

    [Fact]
    public void A_hundred_header_rows_are_accepted_and_a_hundred_and_one_are_refused()
    {
        var hundred = Enumerable.Range(0, 100).Select(index => Row($"X-N{index}", "v")).ToList();

        Assert.Equal(100, Assemble(Get("https://api.example.test/") with { Headers = hundred }).Headers.Count);

        var refused = Refused(() => Assemble(Get("https://api.example.test/") with { Headers = [.. hundred, Row("X-N100", "v")] }));
        Assert.Equal(RequestAssemblyException.TooManyHeaders, refused.Code);
    }

    [Fact]
    public void The_sender_added_headers_count_towards_the_limit()
    {
        var hundred = Enumerable.Range(0, 100).Select(index => Row($"X-N{index}", "v")).ToList();
        var definition = Get("https://api.example.test/") with
        {
            Headers = hundred,
            Auth = new RequestAuth(AuthType.Bearer, "{{token}}", null, null, null, null, null),
        };

        var refused = Refused(() => Assemble(definition));

        Assert.Equal(RequestAssemblyException.TooManyHeaders, refused.Code);
    }

    [Fact]
    public void A_configured_header_limit_is_enforced()
    {
        var definition = Get("https://api.example.test/") with { Headers = [Row("X-1", "1"), Row("X-2", "2"), Row("X-3", "3")] };

        var refused = Refused(() => Assembler(configure: options => options.MaxHeaders = 2).Assemble(definition, Vars));

        Assert.Equal(RequestAssemblyException.TooManyHeaders, refused.Code);
    }

    [Fact]
    public void A_configured_url_limit_is_enforced_on_the_assembled_url()
    {
        var definition = Get("https://api.example.test/") with { Params = [Row("q", new string('x', 200))] };

        var refused = Refused(() => Assembler(configure: options => options.UrlMaxLength = 100).Assemble(definition, Vars));

        Assert.Equal(RequestAssemblyException.UrlTooLong, refused.Code);
    }

    [Fact]
    public void A_configured_body_limit_is_enforced()
    {
        var definition = Get("https://api.example.test/") with
        {
            Method = "POST",
            Body = new RequestBody(BodyType.Raw, RawLanguage.Text, new string('x', 11), []),
        };

        var refused = Refused(() => Assembler(configure: options => options.RequestBodyMaxBytes = 10).Assemble(definition, Vars));

        Assert.Equal(RequestAssemblyException.BodyTooLarge, refused.Code);
    }

    [Fact]
    public void A_header_name_built_from_a_secret_with_a_line_break_is_refused_without_echoing_the_secret()
    {
        var variables = Variables(("name", "X-Ok\r\nHost: evil.test", true));
        var definition = Get("https://api.example.test/") with { Headers = [Row("{{name}}", "1")] };

        var refused = Refused(() => Assembler().Assemble(definition, variables));

        Assert.Equal(RequestAssemblyException.InvalidHeaderName, refused.Code);
        Assert.DoesNotContain("evil.test", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Ok", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_in_a_query_value_is_sent_unmasked_and_masked_in_the_masker()
    {
        var definition = Get("https://api.example.test/") with { Params = [Row("t", "{{token}}")] };

        var request = Assemble(definition);

        Assert.Equal("https://api.example.test/?t=abc123", request.Url);
        Assert.Equal("https://api.example.test/?t=" + SecretMasker.Mask, request.Masker.MaskUrl(request.Url));
    }

    [Fact]
    public void A_short_secret_warning_is_merged_into_the_request_warnings()
    {
        var variables = Variables(("pin", "123", true));
        var definition = Get("https://api.example.test/") with { Params = [Row("p", "{{pin}}")] };

        var request = Assembler().Assemble(definition, variables);

        Assert.Equal(["secret_short"], request.Warnings.Select(warning => warning.Code));
    }

    [Fact]
    public void The_same_input_assembles_to_the_same_request()
    {
        var definition = Get("{{base}}/items?x={{a}}") with
        {
            Method = "POST",
            Params = [Row("q", "{{token}} & more")],
            Headers = [Row("X-Trace", "{{a}}")],
            Body = new RequestBody(BodyType.FormData, null, null, [Row("f", "{{b}}")]),
            Auth = new RequestAuth(AuthType.Bearer, "{{token}}", null, null, null, null, null),
        };

        var first = Assembler(new SeededRandomBytes(11)).Assemble(definition, Vars);
        var second = Assembler(new SeededRandomBytes(11)).Assemble(definition, Vars);

        Assert.Equal(first.Url, second.Url);
        Assert.Equal(first.Headers, second.Headers);
        Assert.Equal(first.Body, second.Body);
    }

    private static AssembledRequest Assemble(RequestDefinition definition) => Assembler().Assemble(definition, Vars);
}
