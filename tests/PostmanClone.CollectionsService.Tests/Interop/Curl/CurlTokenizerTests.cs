using PostmanClone.CollectionsService.Interop.Curl;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Interop.Curl;

/// <summary>
/// The word splitting of a pasted command (WP17): the quoting rules of a POSIX shell, the <c>$'...'</c> escapes, and what is
/// reported instead of expanded. Nothing is expanded here, so a test string such as <c>$HOME</c> is only ever text.
/// </summary>
public sealed class CurlTokenizerTests
{
    [Theory]
    [InlineData("a b  c", "a", "b", "c")]
    [InlineData("'one two' three", "one two", "three")]
    [InlineData("\"a \\\"quoted\\\" word\"", "a \"quoted\" word")]
    [InlineData("back\\ slash\\\\", "back slash\\")]
    [InlineData("''", "")]
    [InlineData("a''b", "ab")]
    [InlineData("\"it's\"", "it's")]
    [InlineData("$'tab\\there'", "tab\there")]
    [InlineData("$'\\x41\\101\\u0042'", "AAB")]
    [InlineData("$'\\U0001F600'", "\U0001F600")]
    [InlineData("$'\\cA'", "\u0001")]
    [InlineData("$'\\q'", "\\q")]
    [InlineData("$'\\u12'", "\u0012")]
    public void Words_are_split_and_quoted_the_way_a_posix_shell_reads_them(string input, params string[] expected)
    {
        var result = CurlTokenizer.Tokenize(input);

        Assert.Null(result.Error);
        Assert.Equal(expected, result.Words);
    }

    [Fact]
    public void Dollar_expansions_are_kept_as_text_and_reported_once()
    {
        var result = CurlTokenizer.Tokenize("$HOME \"$(id)\" `uname` ${PATH}");

        Assert.Equal(new[] { "$HOME", "$(id)", "`uname`", "${PATH}" }, result.Words);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(CurlTokenizer.ExpansionCode, warning.Code);
        Assert.Equal("command[0]", warning.Path);
    }

    [Fact]
    public void Single_quotes_stop_every_expansion_and_report_nothing()
    {
        var result = CurlTokenizer.Tokenize("'$HOME $(id) `uname` \\n'");

        Assert.Equal(new[] { "$HOME $(id) `uname` \\n" }, result.Words);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_dollar_that_is_not_a_name_is_not_reported()
    {
        var result = CurlTokenizer.Tokenize("price=$5 cost=$ ");

        Assert.Equal(new[] { "price=$5", "cost=$" }, result.Words);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void An_unquoted_caret_is_kept_and_reported()
    {
        var result = CurlTokenizer.Tokenize("curl ^\"x^\"");

        Assert.Equal(CurlTokenizer.CaretCode, Assert.Single(result.Warnings).Code);
        Assert.Equal("curl", result.Words[0]);
    }

    [Fact]
    public void A_line_continuation_joins_the_words_on_either_side()
    {
        var result = CurlTokenizer.Tokenize("ab\\\ncd \\\r\n ef");

        Assert.Equal(new[] { "abcd", "ef" }, result.Words);
    }

    [Fact]
    public void A_trailing_backslash_is_kept_as_text()
    {
        var result = CurlTokenizer.Tokenize("end\\");

        Assert.Equal(new[] { "end\\" }, result.Words);
    }

    [Fact]
    public void A_decoded_nul_is_refused_even_though_the_input_had_none()
    {
        var result = CurlTokenizer.Tokenize("a$'\\x00'b");

        Assert.Equal(CurlTokenizer.NulCharacter, result.Error);
        Assert.Empty(result.Words);
    }

    [Fact]
    public void An_empty_input_has_no_words_and_no_error()
    {
        var result = CurlTokenizer.Tokenize(" \t\r\n ");

        Assert.Empty(result.Words);
        Assert.Null(result.Error);
    }
}
