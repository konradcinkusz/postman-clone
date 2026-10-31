using System.Text;
using PostmanClone.Web.Response;

namespace PostmanClone.Web.Tests.Response;

/// <summary>
/// The JSON scanner and the pretty printer (WP20 acceptance): valid text is laid out with one token per piece, invalid text is not
/// formatted at all, a deep body is read without recursion, and no single token exceeds the split size.
/// </summary>
public sealed class JsonPrettyTests
{
    private static string Flatten(IReadOnlyList<IReadOnlyList<ViewToken>> lines) =>
        string.Join("\n", lines.Select(line => string.Concat(line.Select(token => token.Text))));

    [Fact]
    public void An_object_is_laid_out_one_member_per_line_with_two_space_indentation()
    {
        var lines = JsonPretty.TryFormat("{\"a\":1,\"b\":[true,null],\"c\":{}}");

        Assert.NotNull(lines);
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": [\n    true,\n    null\n  ],\n  \"c\": {}\n}", Flatten(lines));
    }

    [Fact]
    public void Keys_and_values_carry_their_own_token_kinds()
    {
        var lines = JsonPretty.TryFormat("{\"name\":\"x\",\"count\":42}")!;

        var kinds = lines.SelectMany(line => line).Select(token => (token.Kind, token.Text)).ToList();
        Assert.Contains((TokenKind.Key, "\"name\""), kinds);
        Assert.Contains((TokenKind.String, "\"x\""), kinds);
        Assert.Contains((TokenKind.Key, "\"count\""), kinds);
        Assert.Contains((TokenKind.Number, "42"), kinds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1,]")]
    [InlineData("{'a':1}")]
    [InlineData("{a:1}")]
    [InlineData("[1 2]")]
    [InlineData("1 2")]
    [InlineData("{\"a\" 1}")]
    [InlineData("\"unterminated")]
    [InlineData("\"bad \\x escape\"")]
    [InlineData("\"bad \\u12\"")]
    [InlineData("\"tab\there\"")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData("-")]
    [InlineData("1e")]
    [InlineData("tru")]
    [InlineData("nul1")]
    [InlineData("<script>alert(1)</script>")]
    public void Text_that_is_not_one_json_value_is_not_formatted(string text)
    {
        Assert.Null(JsonPretty.TryFormat(text));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("-0.5e+10")]
    [InlineData("\"a \\u0041 \\\" \\n\"")]
    [InlineData("null")]
    [InlineData("  [ ]  ")]
    public void A_single_valid_value_is_formatted(string text)
    {
        Assert.NotNull(JsonPretty.TryFormat(text));
    }

    [Fact]
    public void A_body_nested_ten_thousand_levels_deep_is_read_without_a_stack_overflow()
    {
        const int depth = 10_000;
        var text = new string('[', depth) + "1" + new string(']', depth);

        var lines = JsonPretty.TryFormat(text);

        Assert.NotNull(lines);
        // The indentation stops growing past the cap, so no line is wider than the cap allows.
        var widest = lines.Max(line => line.Sum(token => token.Text.Length));
        Assert.True(widest <= JsonPretty.MaxIndentLevels * 2 + 4, $"A line was {widest} characters wide.");
    }

    [Fact]
    public void A_very_deep_unbalanced_body_is_refused_not_thrown()
    {
        var text = new string('{', 10_000) + "\"a\":1";

        Assert.Null(JsonPretty.TryFormat(text));
    }

    [Fact]
    public void A_long_string_is_split_into_tokens_no_longer_than_the_split_size()
    {
        var value = new string('x', JsonPretty.MaxTokenChars * 3 + 17);
        var lines = JsonPretty.TryFormat($"\"{value}\"")!;

        var tokens = lines.SelectMany(line => line).Where(token => token.Kind == TokenKind.String).ToList();
        Assert.True(tokens.Count >= 3);
        Assert.All(tokens, token => Assert.True(token.Text.Length <= JsonPretty.MaxTokenChars));
        Assert.Equal($"\"{value}\"", string.Concat(tokens.Select(token => token.Text)));
    }

    [Fact]
    public void A_large_array_is_formatted_in_linear_time_within_a_generous_budget()
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < 100_000; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"id\":").Append(i).Append(",\"name\":\"item\"}");
        }

        builder.Append(']');
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        var lines = JsonPretty.TryFormat(builder.ToString());

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        Assert.NotNull(lines);
        Assert.True(elapsed < TimeSpan.FromSeconds(20), $"Formatting took {elapsed}.");
    }

    [Fact]
    public void Text_lines_split_on_line_feeds_and_drop_carriage_returns()
    {
        var lines = TextLines.Of("one\r\ntwo\nthree");

        Assert.Equal(["one", "two", "three"], lines.Select(line => string.Concat(line.Select(token => token.Text))));
    }

    [Fact]
    public void A_single_long_text_line_is_split_so_no_one_node_holds_the_whole_body()
    {
        var text = new string('a', JsonPretty.MaxTokenChars * 2 + 5);

        var lines = TextLines.Of(text);

        Assert.Single(lines);
        Assert.Equal(3, lines[0].Count);
        Assert.All(lines[0], token => Assert.True(token.Text.Length <= JsonPretty.MaxTokenChars));
    }

    [Fact]
    public void Empty_text_is_one_empty_line()
    {
        var lines = TextLines.Of(string.Empty);

        Assert.Single(lines);
        Assert.Equal(string.Empty, lines[0][0].Text);
    }
}
