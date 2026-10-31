using PostmanClone.Web.Api;
using PostmanClone.Web.Builder;

namespace PostmanClone.Web.Tests.Builder;

/// <summary>The bulk-text, JSON beautifier, variable tokenizer and the per-request editor state (WP19): plain logic, no renderer.</summary>
public sealed class BulkRowsTests
{
    [Fact]
    public void Format_writes_one_key_value_line_per_row_and_a_disabled_row_with_a_double_slash()
    {
        var rows = new[]
        {
            new DraftRow(Guid.NewGuid(), "Accept", "application/json", Enabled: true),
            new DraftRow(Guid.NewGuid(), "X-Debug", "1", Enabled: false),
            new DraftRow(Guid.NewGuid(), "Empty", string.Empty, Enabled: true),
        };

        Assert.Equal("Accept: application/json\n//X-Debug: 1\nEmpty:", BulkRows.Format(rows));
    }

    [Fact]
    public void Parse_reads_each_line_and_skips_blank_ones()
    {
        var rows = BulkRows.Parse("Accept: text/html\n\n   \nX-Trace:  abc  \r\n//Off: no", Guid.NewGuid);

        Assert.Equal(new[] { ("Accept", "text/html", true), ("X-Trace", "abc", true), ("Off", "no", false) }, rows.Select(row => (row.Key, row.Value, row.Enabled)).ToArray());
    }

    [Fact]
    public void A_line_without_a_colon_is_a_name_with_an_empty_value()
    {
        var row = Assert.Single(BulkRows.Parse("Flag", Guid.NewGuid));

        Assert.Equal(("Flag", string.Empty), (row.Key, row.Value));
    }

    [Fact]
    public void A_value_may_contain_colons_because_only_the_first_colon_splits()
    {
        var row = Assert.Single(BulkRows.Parse("Link: https://x.test:8080/a", Guid.NewGuid));

        Assert.Equal(("Link", "https://x.test:8080/a"), (row.Key, row.Value));
    }

    [Fact]
    public void Format_then_parse_gives_the_same_rows()
    {
        var rows = new[]
        {
            new DraftRow(Guid.NewGuid(), "Accept", "application/json", Enabled: true),
            new DraftRow(Guid.NewGuid(), "X-Debug", "{{debug}}", Enabled: false),
            new DraftRow(Guid.NewGuid(), "Cache-Control", "no-cache", Enabled: true),
        };

        var parsed = BulkRows.Parse(BulkRows.Format(rows), Guid.NewGuid);

        Assert.Equal(rows.Select(row => (row.Key, row.Value, row.Enabled)), parsed.Select(row => (row.Key, row.Value, row.Enabled)));
    }

    [Fact]
    public void Parsing_gives_each_row_a_new_id()
    {
        var ids = new Queue<Guid>([Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002")]);

        var rows = BulkRows.Parse("a: 1\nb: 2", ids.Dequeue);

        Assert.Empty(ids);
        Assert.Equal(2, rows.Select(row => row.Id).Distinct().Count());
    }
}

public sealed class JsonBeautifierTests
{
    [Fact]
    public void A_valid_body_is_written_indented()
    {
        var result = JsonBeautifier.Beautify("{\"a\":1,\"b\":[true,null]}");

        Assert.True(result.Succeeded);
        Assert.Contains("\n  \"a\": 1,", result.Text);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Non_ascii_text_is_written_as_typed_not_escaped()
    {
        var result = JsonBeautifier.Beautify("{\"city\":\"Kraków\"}");

        Assert.Contains("Kraków", result.Text);
    }

    [Fact]
    public void An_invalid_body_is_refused_with_its_line_and_column_and_the_text_is_kept()
    {
        const string text = "{\n  \"a\": ,\n}";

        var result = JsonBeautifier.Beautify(text);

        Assert.False(result.Succeeded);
        Assert.Equal(text, result.Text);
        Assert.Matches(@"at line \d+, column \d+\.", result.Message);
    }

    [Fact]
    public void A_message_never_repeats_the_body()
    {
        const string secretish = "{\"password\": \"not-a-real-secret-value\",,}"; // gitleaks:allow test fixture, not a credential

        var result = JsonBeautifier.Beautify(secretish);

        Assert.DoesNotContain("not-a-real-secret-value", result.Message);
    }

    [Fact]
    public void An_empty_body_says_so()
    {
        var result = JsonBeautifier.Beautify("   ");

        Assert.False(result.Succeeded);
        Assert.Equal(BuilderMessages.EmptyBody, result.Message);
    }

    [Fact]
    public void A_variable_inside_the_json_is_not_valid_json_and_is_refused()
    {
        Assert.False(JsonBeautifier.Beautify("{\"id\": {{id}}}").Succeeded);
    }
}

public sealed class VariableTokensTests
{
    [Fact]
    public void Segments_join_back_to_the_text()
    {
        const string text = "https://{{host}}/v1/{{ id }}/x?a={{b.c-d}}&e=";

        Assert.Equal(text, string.Concat(VariableTokens.Segment(text).Select(segment => segment.Text)));
    }

    [Fact]
    public void A_reference_is_a_segment_of_its_own_and_plain_text_is_not()
    {
        var segments = VariableTokens.Segment("a{{host}}b");

        Assert.Equal(new[] { ("a", (string?)null), ("{{host}}", "host"), ("b", (string?)null) }, segments.Select(segment => (segment.Text, segment.Variable)).ToArray());
    }

    [Theory]
    [InlineData("{{ spaced }}")]
    [InlineData("{{}}")]
    [InlineData("{{a b}}")]
    public void Text_that_is_not_a_reference_stays_plain_text(string text)
    {
        var variables = VariableTokens.NamesIn(text);

        Assert.DoesNotContain("spaced", variables);
        Assert.DoesNotContain("a b", variables);
    }

    [Fact]
    public void Names_are_listed_once_each_in_order()
    {
        Assert.Equal(new[] { "a", "b" }, VariableTokens.NamesIn("{{a}}/{{b}}/{{a}}"));
    }
}

public sealed class RequestEditorTests
{
    private static SavedRequestDto Saved(string name = "List", int version = 1, string url = "https://api.example.test/items?page=1") =>
        new(
            Guid.Parse("00000000-0000-0000-0000-00000000000a"),
            Guid.Parse("00000000-0000-0000-0000-0000000000c1"),
            null,
            name,
            null,
            new RequestDefinition("GET", url, [new KeyValue("page", "1")], [], new RequestBody("none", null, null, []), new RequestAuth("none", null, null, null, null, null, null)),
            0,
            version,
            Guid.Empty,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void A_freshly_loaded_request_is_clean()
    {
        var editor = new RequestEditor(Saved());

        Assert.False(editor.IsDirty);
        Assert.Equal(1, editor.Version);
        Assert.Equal("List", editor.Name);
    }

    [Fact]
    public void An_edit_makes_it_dirty_and_taking_it_back_makes_it_clean_again()
    {
        var editor = new RequestEditor(Saved());

        editor.Draft.SetMethod("POST");
        Assert.True(editor.IsDirty);

        editor.Draft.SetMethod("GET");
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void A_clean_editor_takes_a_renamed_copy_whole()
    {
        var editor = new RequestEditor(Saved());

        editor.AcceptServerCopy(Saved(name: "Renamed", version: 2, url: "https://api.example.test/other"));

        Assert.Equal("Renamed", editor.Name);
        Assert.Equal(2, editor.Version);
        Assert.Equal("https://api.example.test/other?page=1", editor.Draft.Url);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void A_dirty_editor_keeps_its_draft_and_its_version_so_its_next_save_is_a_conflict_the_user_decides()
    {
        var editor = new RequestEditor(Saved());
        editor.Draft.SetMethod("POST");

        editor.AcceptServerCopy(Saved(name: "Renamed", version: 2));

        Assert.Equal("Renamed", editor.Name);
        Assert.Equal(1, editor.Version);
        Assert.Equal("POST", editor.Draft.Method);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void A_conflict_is_remembered_until_the_editor_is_loaded_again()
    {
        var editor = new RequestEditor(Saved());

        editor.MarkConflicted();
        Assert.True(editor.Conflicted);

        editor.Load(Saved(version: 3));
        Assert.False(editor.Conflicted);
        Assert.Equal(3, editor.Version);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Name", true)]
    public void A_request_name_must_be_present(string name, bool valid)
    {
        Assert.Equal(valid, RequestEditor.IsValidName(name));
    }

    [Fact]
    public void A_name_of_200_characters_is_the_longest_the_service_keeps()
    {
        Assert.True(RequestEditor.IsValidName(new string('n', RequestEditor.MaximumNameLength)));
        Assert.False(RequestEditor.IsValidName(new string('n', RequestEditor.MaximumNameLength + 1)));
    }
}
