using PostmanClone.ExecutionService.Sending.Assembly;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>CONTRACTS §1 variable syntax: one pass, no nesting, case-sensitive, unknown names kept and reported.</summary>
public sealed class VariableInterpolatorTests
{
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("{{a}}", "x")]
    [InlineData("{{a}}{{b}}", "xy")]
    [InlineData("pre-{{a}}-post", "pre-x-post")]
    [InlineData("{{ a }}", "{{ a }}")]
    [InlineData("{{a }}", "{{a }}")]
    [InlineData("{{}}", "{{}}")]
    [InlineData("{{a}", "{{a}")]
    [InlineData("{a}}", "{a}}")]
    [InlineData("{{é}}", "{{é}}")]
    [InlineData("{{A}}", "{{A}}")]
    [InlineData("{{a}}}", "x}")]
    [InlineData("{{{a}}}", "{x}")]
    [InlineData("{{a{{b}}}}", "{{ay}}")]
    [InlineData("$1 {{a}} $& {{a}}", "$1 x $& x")]
    public void Expands_references_in_one_pass_and_leaves_everything_else_as_written(string template, string expected)
    {
        var variables = Variables(("a", "x", false), ("b", "y", false));
        var interpolator = new VariableInterpolator(variables);
        var expanded = interpolator.Expand(template);

        Assert.Equal(expected, expanded);
    }

    [Theory]
    [InlineData("{{a}}", "{{b}}", "X", "{{b}}")]
    [InlineData("{{a}}", "{{a}}", "X", "{{a}}")]
    [InlineData("{{a}}-{{b}}", "{{b}}", "X", "{{b}}-X")]
    public void A_value_that_contains_a_reference_is_inserted_as_text(string template, string valueOfA, string valueOfB, string expected)
    {
        var interpolator = new VariableInterpolator(Variables(("a", valueOfA, false), ("b", valueOfB, false)));

        Assert.Equal(expected, interpolator.Expand(template));
    }

    [Theory]
    [InlineData("héllo 🙂 中文", "héllo 🙂 中文")]
    [InlineData("line one\ttab", "line one\ttab")]
    [InlineData("ümlaut ß ﷺ", "ümlaut ß ﷺ")]
    public void Unicode_values_are_inserted_unchanged(string value, string expected)
    {
        var interpolator = new VariableInterpolator(Variables(("a", value, false)));

        Assert.Equal(expected, interpolator.Expand("{{a}}"));
    }

    [Fact]
    public void A_name_of_exactly_100_characters_is_a_reference_and_101_is_text()
    {
        var name100 = new string('n', 100);
        var name101 = new string('n', 101);
        var interpolator = new VariableInterpolator(Variables((name100, "ok", false)));

        Assert.Equal("ok", interpolator.Expand("{{" + name100 + "}}"));
        Assert.Equal("{{" + name101 + "}}", interpolator.Expand("{{" + name101 + "}}"));
    }

    [Fact]
    public void Unresolved_names_are_kept_as_written_reported_once_and_in_first_seen_order()
    {
        var interpolator = new VariableInterpolator(Variables(("a", "x", false)));

        var expanded = interpolator.Expand("{{missing}}/{{a}}/{{other}}/{{missing}}");

        Assert.Equal("{{missing}}/x/{{other}}/{{missing}}", expanded);
        Assert.Equal(new[] { "missing", "other" }, interpolator.Unresolved);
    }

    [Fact]
    public void Used_records_every_inserted_value_with_its_secret_flag()
    {
        var interpolator = new VariableInterpolator(Variables(("plain", "p", false), ("token", "s3cret-value", true)));

        interpolator.Expand("{{plain}} and {{token}} and {{plain}}");

        Assert.Equal(
            new[] { new ResolvedValue("p", false), new ResolvedValue("s3cret-value", true), new ResolvedValue("p", false) },
            interpolator.Used);
    }

    [Fact]
    public void Names_are_case_sensitive_even_when_the_caller_used_another_comparer()
    {
        var caseInsensitive = new Dictionary<string, ResolvedValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Token"] = new("v", true),
        };
        var interpolator = new VariableInterpolator(caseInsensitive);

        Assert.Equal("{{token}}", interpolator.Expand("{{token}}"));
        Assert.Equal("v", interpolator.Expand("{{Token}}"));
    }

    private static IReadOnlyDictionary<string, ResolvedValue> Variables(params (string Name, string Value, bool Secret)[] variables) =>
        AssemblyFixtures.Variables(variables);
}
