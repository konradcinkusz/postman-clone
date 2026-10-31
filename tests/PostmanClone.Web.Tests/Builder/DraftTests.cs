using System.Text.Json;
using PostmanClone.Web.Api;
using PostmanClone.Web.Builder;

namespace PostmanClone.Web.Tests.Builder;

/// <summary>The draft (WP19 acceptance): every field round-trips, canonical forms drop the unused fields, rows and URL stay in step.</summary>
public sealed class DraftTests
{
    private static RequestDefinition Get(string url, List<KeyValue>? parameters = null, List<KeyValue>? headers = null) =>
        new("GET", url, parameters ?? [], headers ?? [], new RequestBody("none", null, null, []), new RequestAuth("none", null, null, null, null, null, null));

    private static void AssertSameDefinition(RequestDefinition expected, RequestDefinition actual)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(expected, options), JsonSerializer.Serialize(actual, options));
    }

    public static TheoryData<RequestDefinition> CanonicalDefinitions => new()
    {
        Get("https://api.example.test/items", [new KeyValue("page", "2"), new KeyValue("debug", "1", Enabled: false)], [new KeyValue("Accept", "application/json")]),
        new RequestDefinition("POST", "https://api.example.test/items", [], [], new RequestBody("raw", "json", "{\"name\":\"ü\"}", []), new RequestAuth("none", null, null, null, null, null, null)),
        new RequestDefinition("PUT", "https://api.example.test/items/1", [], [], new RequestBody("raw", "xml", "<a/>", []), new RequestAuth("none", null, null, null, null, null, null)),
        new RequestDefinition("POST", "https://api.example.test/form", [], [], new RequestBody("formUrlEncoded", null, null, [new KeyValue("a", "1"), new KeyValue("b", "2", Enabled: false)]), new RequestAuth("none", null, null, null, null, null, null)),
        new RequestDefinition("POST", "https://api.example.test/multi", [], [], new RequestBody("formData", null, null, [new KeyValue("title", "{{title}}")]), new RequestAuth("none", null, null, null, null, null, null)),
        new RequestDefinition("GET", "https://api.example.test/me", [], [], new RequestBody("none", null, null, []), new RequestAuth("bearer", "{{token}}", null, null, null, null, null)),
        new RequestDefinition("GET", "https://api.example.test/me", [], [], new RequestBody("none", null, null, []), new RequestAuth("basic", null, "ops", "{{password}}", null, null, null)),
        new RequestDefinition("GET", "https://api.example.test/me", [], [], new RequestBody("none", null, null, []), new RequestAuth("apiKey", null, null, null, "X-Key", "{{apiKey}}", "header")),
        new RequestDefinition("GET", "https://api.example.test/me", [new KeyValue("api_key", "{{apiKey}}")], [], new RequestBody("none", null, null, []), new RequestAuth("apiKey", null, null, null, "api_key", "{{apiKey}}", "query")),
    };

    [Theory]
    [MemberData(nameof(CanonicalDefinitions))]
    public void A_canonical_definition_reads_into_the_draft_and_writes_back_unchanged(RequestDefinition definition)
    {
        var draft = RequestDraft.FromDefinition(definition);

        AssertSameDefinition(definition, draft.ToDefinition());
    }

    [Fact]
    public void Every_field_the_editor_shows_is_read_from_the_definition()
    {
        var draft = RequestDraft.FromDefinition(new RequestDefinition(
            "DELETE",
            "https://api.example.test/x",
            [new KeyValue("a", "1")],
            [new KeyValue("X-Trace", "t", Enabled: false)],
            new RequestBody("raw", "javascript", "run()", []),
            new RequestAuth("basic", null, "ops", "{{pw}}", null, null, null)));

        Assert.Equal("DELETE", draft.Method);
        Assert.Equal("https://api.example.test/x?a=1", draft.Url);
        Assert.Equal("a", draft.Params.Single().Key);
        Assert.False(draft.Headers.Single().Enabled);
        Assert.Equal("raw", draft.BodyType);
        Assert.Equal("javascript", draft.RawLanguage);
        Assert.Equal("run()", draft.Raw);
        Assert.Equal("basic", draft.AuthType);
        Assert.Equal("ops", draft.Username);
        Assert.Equal("{{pw}}", draft.Password);
    }

    [Fact]
    public void A_null_list_or_object_in_the_definition_reads_as_empty()
    {
        var draft = RequestDraft.FromDefinition(new RequestDefinition("GET", "https://api.example.test", null, null, null, null));

        Assert.Empty(draft.Params);
        Assert.Empty(draft.Headers);
        Assert.Equal("none", draft.BodyType);
        Assert.Equal("none", draft.AuthType);
        AssertSameDefinition(
            new RequestDefinition("GET", "https://api.example.test", [], [], new RequestBody("none", null, null, []), new RequestAuth("none", null, null, null, null, null, null)),
            draft.ToDefinition());
    }

    [Fact]
    public void Editing_a_row_in_the_table_rewrites_the_url()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items?page=1"));
        var row = draft.Params.Single();

        draft.EditRow(RowList.Params, row.Id, value: "2");

        Assert.Equal("https://api.example.test/items?page=2", draft.Url);
    }

    [Fact]
    public void Adding_an_empty_row_leaves_the_url_alone()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items"));

        draft.AddRow(RowList.Params);

        Assert.Equal("https://api.example.test/items", draft.Url);
        Assert.Single(draft.Params);
    }

    [Fact]
    public void Disabling_a_row_takes_it_out_of_the_url_and_keeps_it_in_the_table()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items?a=1&b=2"));
        var first = draft.Params[0];

        draft.EditRow(RowList.Params, first.Id, enabled: false);

        Assert.Equal("https://api.example.test/items?b=2", draft.Url);
        Assert.False(draft.Params[0].Enabled);
        Assert.Equal(2, draft.Params.Count);
    }

    [Fact]
    public void Removing_a_row_takes_it_out_of_the_url()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items?a=1&b=2"));

        draft.RemoveRow(RowList.Params, draft.Params[0].Id);

        Assert.Equal("https://api.example.test/items?b=2", draft.Url);
    }

    [Fact]
    public void Typing_in_the_url_rebuilds_the_table_and_keeps_the_disabled_row()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items", [new KeyValue("debug", "1", Enabled: false)]));

        draft.SetUrl("https://api.example.test/items?page=3");

        // The disabled row was first, so it keeps the first place; the pair from the URL follows it.
        Assert.Equal(2, draft.Params.Count);
        Assert.Equal("debug", draft.Params[0].Key);
        Assert.False(draft.Params[0].Enabled);
        Assert.Equal("page", draft.Params[1].Key);
    }

    [Fact]
    public void A_query_the_stored_url_carries_is_moved_into_the_table_and_saved_without_it()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items?q=1&q=2#top"));

        Assert.Equal(new[] { "q", "q" }, draft.Params.Select(row => row.Key).ToArray());
        Assert.Equal("https://api.example.test/items?q=1&q=2#top", draft.Url);
        Assert.Equal("https://api.example.test/items#top", draft.ToDefinition().Url);
        Assert.Equal(2, draft.ToDefinition().Params!.Count);
    }

    [Fact]
    public void A_stored_query_and_its_params_are_not_sent_twice_after_a_save()
    {
        var definition = Get("https://api.example.test/items?page=1", [new KeyValue("sort", "asc")]);
        var draft = RequestDraft.FromDefinition(definition);

        var saved = draft.ToDefinition();

        Assert.Equal("https://api.example.test/items", saved.Url);
        Assert.Equal(new[] { "page", "sort" }, saved.Params!.Select(row => row.Key).ToArray());
        Assert.Equal(saved.Params!.Count, RequestDraft.FromDefinition(saved).ToDefinition().Params!.Count);
    }

    [Fact]
    public void Loading_a_request_whose_query_was_moved_is_clean_until_something_is_edited()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items?page=1"));
        var baseline = draft.Fingerprint();

        Assert.Equal(baseline, RequestDraft.FromDefinition(draft.ToDefinition()).Fingerprint());
    }

    [Fact]
    public void The_url_text_is_kept_as_typed_when_it_is_read_back_into_the_draft()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items"));

        draft.SetUrl("https://api.example.test/items?q=ü&empty=");

        Assert.Equal("https://api.example.test/items?q=ü&empty=", draft.Url);
    }

    [Fact]
    public void Switching_the_body_type_drops_the_form_fields_it_does_not_use()
    {
        var draft = RequestDraft.FromDefinition(new RequestDefinition("POST", "https://api.example.test", [], [], new RequestBody("formUrlEncoded", null, null, [new KeyValue("a", "1")]), null));

        draft.SetBodyType("none");

        Assert.Empty(draft.FormFields);
        AssertSameDefinition(
            new RequestDefinition("POST", "https://api.example.test", [], [], new RequestBody("none", null, null, []), new RequestAuth("none", null, null, null, null, null, null)),
            draft.ToDefinition());
    }

    [Fact]
    public void An_auth_helper_writes_only_the_fields_of_its_own_type()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test"));

        draft.SetAuthType("bearer");
        draft.SetBearerToken("{{token}}");
        draft.SetPassword("{{stale}}");
        draft.SetUsername("left-behind");

        var auth = draft.ToDefinition().Auth!;
        Assert.Equal("{{token}}", auth.BearerToken);
        Assert.Null(auth.Password);
        Assert.Null(auth.Username);
    }

    [Fact]
    public void A_raw_body_writes_its_language_and_text_and_no_fields()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test"));

        draft.SetBodyType("raw");
        draft.SetRawLanguage("xml");
        draft.SetRaw("<x/>");

        var body = draft.ToDefinition().Body!;
        Assert.Equal("raw", body.Type);
        Assert.Equal("xml", body.RawLanguage);
        Assert.Equal("<x/>", body.Raw);
        Assert.Empty(body.Fields!);
    }

    [Fact]
    public void A_new_raw_body_defaults_to_json()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test"));

        draft.SetBodyType("raw");

        Assert.Equal("json", draft.ToDefinition().Body!.RawLanguage);
    }

    [Fact]
    public void The_fingerprint_changes_with_every_field_the_service_would_store()
    {
        var baseline = RequestDraft.FromDefinition(Get("https://api.example.test/items")).Fingerprint();
        var changed = new List<Action<RequestDraft>>
        {
            draft => draft.SetMethod("POST"),
            draft => draft.SetUrl("https://api.example.test/other"),
            draft => draft.AddRow(RowList.Headers),
            draft => draft.SetBodyType("raw"),
            draft => draft.SetAuthType("bearer"),
        };

        foreach (var change in changed)
        {
            var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items"));
            change(draft);
            Assert.NotEqual(baseline, draft.Fingerprint());
        }
    }

    [Fact]
    public void Two_drafts_of_the_same_definition_have_the_same_fingerprint()
    {
        var first = RequestDraft.FromDefinition(Get("https://api.example.test/items?a=1"));
        var second = RequestDraft.FromDefinition(Get("https://api.example.test/items?a=1"));

        Assert.Equal(first.Fingerprint(), second.Fingerprint());
    }

    [Fact]
    public void A_literal_secret_in_the_draft_is_reported_by_the_validation()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test"));

        draft.SetAuthType("bearer");
        draft.SetBearerToken("Bearer abc");

        var issue = Assert.Single(draft.Validate());
        Assert.Equal("request.auth.bearerToken", issue.Field);
        Assert.Equal(DraftValidator.SecretLiteralCode, issue.Code);
    }

    [Fact]
    public void A_secret_typed_under_another_auth_type_is_not_sent_and_so_not_refused()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test"));

        draft.SetBearerToken("Bearer abc");

        Assert.Empty(draft.Validate());
    }

    [Fact]
    public void Rows_and_their_ids_stay_put_when_a_neighbour_is_edited()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items?a=1&b=2&c=3"));
        var ids = draft.Params.Select(row => row.Id).ToArray();

        draft.EditRow(RowList.Params, ids[1], value: "20");

        Assert.Equal(ids, draft.Params.Select(row => row.Id).ToArray());
    }

    [Fact]
    public void A_draft_read_from_a_definition_is_clean_until_it_changes()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items?a=1"));

        Assert.False(draft.IsDirty);

        draft.SetMethod("PUT");
        Assert.True(draft.IsDirty);

        draft.SetMethod("GET");
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Marking_a_draft_saved_makes_its_current_text_the_clean_one()
    {
        var draft = RequestDraft.FromDefinition(Get("https://api.example.test/items"));
        draft.SetUrl("https://api.example.test/other");
        Assert.True(draft.IsDirty);

        draft.MarkSaved();

        Assert.False(draft.IsDirty);
    }
}
