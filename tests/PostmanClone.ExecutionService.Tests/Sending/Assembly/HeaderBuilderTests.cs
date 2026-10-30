using PostmanClone.ExecutionService.Sending.Assembly;
using Xunit;
using static PostmanClone.ExecutionService.Tests.Sending.Assembly.AssemblyFixtures;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>Header rows: enabled rows only, HTTP token names, no control characters, no sender-owned headers.</summary>
public sealed class HeaderBuilderTests
{
    [Theory]
    [InlineData("Accept", "*/*")]
    [InlineData("X-Custom-1", "v")]
    [InlineData("Content-Type", "text/plain; charset=utf-8")]
    [InlineData("X-Tab", "a\tb")]
    [InlineData("Proxy", "not a Proxy-prefixed name")]
    [InlineData("X-Unicode", "ümlaut 🙂")]
    [InlineData("X-Empty", "")]
    [InlineData("!#$%&'*+-.^_`|~", "token-chars")]
    public void Valid_rows_become_header_lines_in_order(string name, string value)
    {
        var headers = HeaderBuilder.Build([Row(name, value)], new VariableInterpolator(Variables()));

        Assert.Equal(new HeaderLine(name, value), Assert.Single(headers));
    }

    [Theory]
    [InlineData("Bad Name")]
    [InlineData("Bad:Name")]
    [InlineData("Bad\r\nName")]
    [InlineData("Bad\nName")]
    [InlineData("Üml")]
    [InlineData("a(b)")]
    [InlineData("a/b")]
    [InlineData("a\u0000")]
    public void A_name_that_is_not_an_http_token_is_refused(string name)
    {
        var refused = Refused(() => HeaderBuilder.Build([Row(name, "v")], new VariableInterpolator(Variables())));

        Assert.Equal(RequestAssemblyException.InvalidHeaderName, refused.Code);
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("host")]
    [InlineData("Content-Length")]
    [InlineData("Connection")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Upgrade")]
    [InlineData("TE")]
    [InlineData("Expect")]
    [InlineData("Proxy-Authorization")]
    [InlineData("proxy-anything")]
    public void A_header_the_sender_owns_is_refused_with_forbidden_header(string name)
    {
        var refused = Refused(() => HeaderBuilder.Build([Row(name, "v")], new VariableInterpolator(Variables())));

        Assert.Equal(RequestAssemblyException.ForbiddenHeader, refused.Code);
    }

    [Theory]
    [InlineData("a\r\nX-Injected: 1")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\u0000b")]
    [InlineData("a\u007fb")]
    [InlineData("a\u001bb")]
    public void A_value_with_a_line_break_or_control_character_is_refused_with_invalid_header_value(string value)
    {
        var refused = Refused(() => HeaderBuilder.Build([Row("X-Ok", value)], new VariableInterpolator(Variables())));

        Assert.Equal(RequestAssemblyException.InvalidHeaderValue, refused.Code);
    }

    [Fact]
    public void A_disabled_row_is_ignored_even_when_it_would_be_refused()
    {
        var headers = HeaderBuilder.Build([Row("Host", "evil.test", enabled: false), Row("X-Ok", "1")], new VariableInterpolator(Variables()));

        Assert.Equal(new HeaderLine("X-Ok", "1"), Assert.Single(headers));
    }

    [Fact]
    public void A_row_with_an_empty_key_is_skipped()
    {
        var headers = HeaderBuilder.Build([Row("", "orphan value"), Row("X-Ok", "1")], new VariableInterpolator(Variables()));

        Assert.Equal(new HeaderLine("X-Ok", "1"), Assert.Single(headers));
    }

    [Fact]
    public void A_line_break_smuggled_through_a_variable_is_refused_and_the_message_does_not_echo_the_value()
    {
        var variables = Variables(("token", "abc\r\nX-Injected: 1", true));

        var refused = Refused(() => HeaderBuilder.Build([Row("X-Token", "{{token}}")], new VariableInterpolator(variables)));

        Assert.Equal(RequestAssemblyException.InvalidHeaderValue, refused.Code);
        Assert.DoesNotContain("abc", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Injected", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lone_surrogate_in_a_value_is_refused_with_invalid_text()
    {
        var refused = Refused(() => HeaderBuilder.Build([Row("X-Bad", "a\uD800b")], new VariableInterpolator(Variables())));

        Assert.Equal(RequestAssemblyException.InvalidText, refused.Code);
    }

    [Fact]
    public void Names_are_compared_without_regard_to_case()
    {
        Assert.True(HeaderBuilder.SameName("Content-Type", "content-type"));
        Assert.False(HeaderBuilder.SameName("Content-Type", "Content-Length"));
    }

    private static IReadOnlyDictionary<string, ResolvedValue> Variables(params (string Name, string Value, bool Secret)[] variables) =>
        AssemblyFixtures.Variables(variables);
}
