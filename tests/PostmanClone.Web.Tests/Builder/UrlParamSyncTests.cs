using PostmanClone.Web.Builder;

namespace PostmanClone.Web.Tests.Builder;

/// <summary>
/// The URL ↔ parameter-table sync (WP19 acceptance: at least 30 cases of fragments, repeated keys, empty values, encoded characters,
/// variables in the query and disabled rows). Pure string logic, so every case is a plain assertion.
/// </summary>
public sealed class UrlParamSyncTests
{
    private const string Base = "https://api.example.test/items";

    private static UrlPair P(string key, string value) => new(key, value);

    [Theory]
    [InlineData("https://api.example.test/a", new string[0], null)]
    [InlineData("https://api.example.test/a?", new string[0], null)]
    [InlineData("https://api.example.test/a?b=1", new[] { "b", "1" }, null)]
    [InlineData("https://api.example.test/a?b=1&c=2", new[] { "b", "1", "c", "2" }, null)]
    [InlineData("https://api.example.test/a?b", new[] { "b", "" }, null)]
    [InlineData("https://api.example.test/a?b=", new[] { "b", "" }, null)]
    [InlineData("https://api.example.test/a?b=1&&c=2", new[] { "b", "1", "c", "2" }, null)]
    [InlineData("https://api.example.test/a?=v", new[] { "", "v" }, null)]
    [InlineData("https://api.example.test/a?a=1&a=2", new[] { "a", "1", "a", "2" }, null)]
    [InlineData("https://api.example.test/a?q=a%20b", new[] { "q", "a b" }, null)]
    [InlineData("https://api.example.test/a?q=a%26b%3Dc", new[] { "q", "a&b=c" }, null)]
    [InlineData("https://api.example.test/a?q=a+b", new[] { "q", "a+b" }, null)]
    [InlineData("https://api.example.test/a?q=%C3%BC", new[] { "q", "ü" }, null)]
    [InlineData("https://api.example.test/a?q=100%", new[] { "q", "100%" }, null)]
    [InlineData("https://api.example.test/a?q=%zz", new[] { "q", "%zz" }, null)]
    [InlineData("https://api.example.test/a?q=%FF", new[] { "q", "%FF" }, null)]
    [InlineData("https://api.example.test/a?q=%2B", new[] { "q", "+" }, null)]
    [InlineData("https://api.example.test/a?id={{userId}}", new[] { "id", "{{userId}}" }, null)]
    [InlineData("https://api.example.test/a?{{key}}=1", new[] { "{{key}}", "1" }, null)]
    [InlineData("https://api.example.test/a?b=x=y", new[] { "b", "x=y" }, null)]
    [InlineData("https://api.example.test/a#frag", new string[0], "frag")]
    [InlineData("https://api.example.test/a?b=1#frag", new[] { "b", "1" }, "frag")]
    [InlineData("https://api.example.test/a#frag?b=2", new string[0], "frag?b=2")]
    [InlineData("https://api.example.test/a?b=1#", new[] { "b", "1" }, "")]
    [InlineData("{{base}}/items?page=2", new[] { "page", "2" }, null)]
    [InlineData("/relative?x=1", new[] { "x", "1" }, null)]
    [InlineData("https://api.example.test/a?b=1&c=%E2%82%AC", new[] { "b", "1", "c", "€" }, null)]
    public void Split_reads_the_base_the_pairs_and_the_fragment(string url, string[] flat, string? fragment)
    {
        var parts = UrlParamSync.Split(url);

        Assert.Equal(flat.Chunk(2).Select(pair => P(pair[0], pair[1])).ToList(), parts.Pairs.ToList());
        Assert.Equal(fragment, parts.Fragment);
        Assert.DoesNotContain('?', parts.Base);
        Assert.DoesNotContain('#', parts.Base);
    }

    [Theory]
    [InlineData("a b", "a%20b")]
    [InlineData("a&b=c", "a%26b%3Dc")]
    [InlineData("100%", "100%25")]
    [InlineData("ü", "%C3%BC")]
    [InlineData("a#b", "a%23b")]
    [InlineData("a+b", "a+b")]
    [InlineData("{{userId}}", "{{userId}}")]
    [InlineData("x/y:z@w", "x/y:z@w")]
    public void Encode_leaves_what_a_query_allows_and_escapes_the_rest(string text, string encoded)
    {
        Assert.Equal(encoded, UrlParamSync.Encode(text));
        Assert.Equal(text, UrlParamSync.Decode(encoded));
    }

    [Fact]
    public void Join_writes_the_pairs_after_a_question_mark_and_an_ampersand_each()
    {
        var url = UrlParamSync.Join(Base, [P("a", "1"), P("b", "2")], fragment: null);

        Assert.Equal($"{Base}?a=1&b=2", url);
    }

    [Fact]
    public void Join_keeps_the_fragment_last()
    {
        Assert.Equal($"{Base}?a=1#top", UrlParamSync.Join(Base, [P("a", "1")], "top"));
    }

    [Fact]
    public void Join_leaves_out_a_pair_with_an_empty_key()
    {
        Assert.Equal($"{Base}?a=1", UrlParamSync.Join(Base, [P("", "v"), P("a", "1")], null));
    }

    [Fact]
    public void Join_with_no_pairs_writes_the_base_alone()
    {
        Assert.Equal(Base, UrlParamSync.Join(Base, [], null));
    }

    [Fact]
    public void A_variable_in_a_query_value_stays_readable()
    {
        Assert.Equal($"{Base}?id={{{{userId}}}}", UrlParamSync.Join(Base, [P("id", "{{userId}}")], null));
    }

    [Fact]
    public void Compose_replaces_only_the_query_and_keeps_the_base_and_the_fragment()
    {
        var url = UrlParamSync.Compose($"{Base}?old=1#section", [P("new", "2")]);

        Assert.Equal($"{Base}?new=2#section", url);
    }

    [Fact]
    public void Compose_with_no_enabled_pairs_drops_the_question_mark()
    {
        Assert.Equal(Base, UrlParamSync.Compose($"{Base}?old=1", []));
    }

    [Theory]
    [InlineData("https://api.example.test/a?b=1")]
    [InlineData("https://api.example.test/a?q=a%20b%26c&r=%C3%BC")]
    [InlineData("https://api.example.test/a?id={{userId}}&x=1#f")]
    [InlineData("https://api.example.test/a?a=1&a=2&a=3")]
    [InlineData("https://api.example.test/a?k=")]
    public void A_canonical_url_survives_a_parse_and_a_join(string url)
    {
        var parts = UrlParamSync.Split(url);

        Assert.Equal(url, UrlParamSync.Join(parts.Base, parts.Pairs, parts.Fragment));
    }

    [Fact]
    public void Decode_and_encode_are_inverse_for_any_text()
    {
        var random = new Random(19);
        const string alphabet = "abcXYZ019 %+&=#?/{}:@!$'()*,;[]\"<>\\^`|-._~ü€\t";
        for (var round = 0; round < 500; round++)
        {
            var text = new string(Enumerable.Range(0, random.Next(0, 40)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

            Assert.Equal(text, UrlParamSync.Decode(UrlParamSync.Encode(text)));
        }
    }

    [Fact]
    public void A_table_edit_then_a_url_read_gives_the_same_pairs()
    {
        var rows = new List<DraftRow>
        {
            new(Guid.NewGuid(), "q", "café & more", Enabled: true),
            new(Guid.NewGuid(), "page", "2", Enabled: true),
            new(Guid.NewGuid(), "x", "{{x}}", Enabled: true),
        };

        var url = UrlParamSync.Compose(Base, UrlParamSync.PairsOf(rows));
        var parsed = UrlParamSync.Split(url).Pairs;

        Assert.Equal(UrlParamSync.PairsOf(rows), parsed);
    }

    [Fact]
    public void A_disabled_row_keeps_its_place_when_the_url_is_edited()
    {
        var disabledId = Guid.NewGuid();
        var previous = new List<DraftRow>
        {
            new(Guid.NewGuid(), "a", "1", Enabled: true),
            new(disabledId, "debug", "true", Enabled: false),
            new(Guid.NewGuid(), "b", "2", Enabled: true),
        };

        var merged = UrlParamSync.MergeFromUrl(previous, [P("a", "9"), P("b", "2")], Guid.NewGuid);

        Assert.Equal(3, merged.Count);
        Assert.Equal(disabledId, merged[1].Id);
        Assert.False(merged[1].Enabled);
        Assert.Equal(new[] { "a", "b" }, new[] { merged[0].Key, merged[2].Key });
        Assert.Equal("9", merged[0].Value);
    }

    [Fact]
    public void An_incomplete_row_with_no_key_survives_a_url_edit()
    {
        var incompleteId = Guid.NewGuid();
        var previous = new List<DraftRow>
        {
            new(Guid.NewGuid(), "a", "1", Enabled: true),
            new(incompleteId, string.Empty, string.Empty, Enabled: true),
        };

        var merged = UrlParamSync.MergeFromUrl(previous, [P("a", "1"), P("c", "3")], Guid.NewGuid);

        Assert.Contains(merged, row => row.Id == incompleteId && row.Key.Length == 0);
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void Rows_that_match_by_place_keep_their_id_when_the_url_changes()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var previous = new List<DraftRow>
        {
            new(first, "a", "1", Enabled: true),
            new(second, "b", "2", Enabled: true),
        };

        var merged = UrlParamSync.MergeFromUrl(previous, [P("a", "1"), P("b", "22")], Guid.NewGuid);

        Assert.Equal(new[] { first, second }, merged.Select(row => row.Id).ToArray());
        Assert.Equal("22", merged[1].Value);
    }

    [Fact]
    public void A_new_pair_from_the_url_gets_a_new_id()
    {
        var fresh = Guid.NewGuid();
        var merged = UrlParamSync.MergeFromUrl([], [P("a", "1")], () => fresh);

        Assert.Equal(fresh, merged.Single().Id);
    }

    [Fact]
    public void The_pairs_a_table_writes_are_the_enabled_rows_with_a_key()
    {
        var rows = new List<DraftRow>
        {
            new(Guid.NewGuid(), "a", "1", Enabled: true),
            new(Guid.NewGuid(), "b", "2", Enabled: false),
            new(Guid.NewGuid(), string.Empty, "x", Enabled: true),
        };

        Assert.Equal([P("a", "1")], UrlParamSync.PairsOf(rows));
    }
}
