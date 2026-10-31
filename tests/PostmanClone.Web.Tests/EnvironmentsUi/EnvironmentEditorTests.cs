using PostmanClone.Web.Api;
using PostmanClone.Web.Environments;

namespace PostmanClone.Web.Tests.EnvironmentsUi;

/// <summary>The editing model of one environment (WP21, ADR-0025): the write-only secret rules, keepExistingSecret, and the checks.</summary>
public sealed class EnvironmentEditorTests
{
    private static readonly Guid EnvironmentId = Guid.NewGuid();

    private static EnvironmentDto Stored(params VariableDto[] variables) =>
        new(EnvironmentId, "Staging", "owner", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, variables.ToList());

    private static VariableDto Plain(string key, string value, bool enabled = true) => new(key, value, false, true, enabled);

    private static VariableDto Secret(string key, bool hasValue = true, bool enabled = true) => new(key, null, true, hasValue, enabled);

    [Fact]
    public void A_stored_secret_has_no_value_in_the_row_only_the_fact_that_one_is_stored()
    {
        var editor = EnvironmentEditor.From(Stored(Secret("token")));

        var row = Assert.Single(editor.Rows);
        Assert.True(row.IsSecret);
        Assert.Equal(string.Empty, row.Value);
        Assert.Null(row.TypedSecret);
        Assert.True(row.Stored!.HasValue);
        Assert.Null(row.Stored.PlainValue);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void An_untouched_stored_secret_is_kept_and_its_value_is_not_sent()
    {
        var editor = EnvironmentEditor.From(Stored(Plain("baseUrl", "https://a.example"), Secret("token")));

        var body = editor.ToUpdateBody();

        Assert.True(editor.CanSave());
        var token = body.Variables.Single(variable => variable.Key == "token");
        Assert.True(token.IsSecret);
        Assert.True(token.KeepExistingSecret);
        Assert.Null(token.Value);
    }

    [Fact]
    public void A_typed_secret_is_sent_as_its_new_value_and_is_not_kept()
    {
        var editor = EnvironmentEditor.From(Stored(Secret("token")));
        var row = editor.Rows[0];

        row.TypedSecret = "replacement-value-in-test"; // gitleaks:allow

        var write = Assert.Single(editor.ToUpdateBody().Variables);
        Assert.Equal("replacement-value-in-test", write.Value); // gitleaks:allow
        Assert.False(write.KeepExistingSecret);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void An_empty_typed_value_counts_as_nothing_typed_so_the_secret_is_kept()
    {
        var editor = EnvironmentEditor.From(Stored(Secret("token")));
        editor.Rows[0].TypedSecret = string.Empty;

        Assert.True(editor.CanSave());
        Assert.True(editor.ToUpdateBody().Variables[0].KeepExistingSecret);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void A_renamed_stored_secret_cannot_be_kept_because_its_ciphertext_is_bound_to_the_key()
    {
        var editor = EnvironmentEditor.From(Stored(Secret("token")));
        editor.Rows[0].Key = "apiToken";

        var row = editor.Rows[0];
        Assert.False(row.CanKeepStoredSecret);
        Assert.Equal(EnvironmentMessages.SecretRenamed, row.OwnProblem());
        Assert.False(editor.CanSave());
    }

    [Fact]
    public void A_new_secret_without_a_value_cannot_be_saved()
    {
        var editor = EnvironmentEditor.From(Stored());
        editor.AddRow();
        var row = editor.Rows[0];
        row.Key = "token";
        row.IsSecret = true;

        Assert.Equal(EnvironmentMessages.SecretNeedsValue, row.OwnProblem());
        Assert.False(editor.CanSave());
        Assert.Contains(editor.Issues().Rows[row.Id], message => message == EnvironmentMessages.SecretNeedsValue);
    }

    [Fact]
    public void A_secret_made_plain_needs_a_value_typed_again()
    {
        var editor = EnvironmentEditor.From(Stored(Secret("token")));
        var row = editor.Rows[0];

        row.IsSecret = false;

        Assert.Equal(EnvironmentMessages.PlainNeedsValue, row.OwnProblem());
        Assert.False(editor.CanSave());

        row.Value = "now-plain";
        Assert.Null(row.OwnProblem());
        var write = row.ToWrite();
        Assert.False(write.IsSecret);
        Assert.Equal("now-plain", write.Value);
        Assert.False(write.KeepExistingSecret);
    }

    [Fact]
    public void A_plain_to_secret_switch_needs_a_typed_value_because_the_stored_row_holds_no_secret()
    {
        var editor = EnvironmentEditor.From(Stored(Plain("region", "eu")));
        var row = editor.Rows[0];

        row.IsSecret = true;

        Assert.False(row.CanKeepStoredSecret);
        Assert.Equal(EnvironmentMessages.SecretNeedsValue, row.OwnProblem());
    }

    [Fact]
    public void Duplicate_keys_are_found_on_every_row_that_repeats_one()
    {
        var editor = EnvironmentEditor.From(Stored(Plain("a", "1"), Plain("b", "2")));
        editor.AddRow();
        editor.Rows[2].Key = "a";

        var issues = editor.Issues();

        Assert.Equal(EnvironmentMessages.KeyDuplicate, Assert.Single(issues.Rows[editor.Rows[0].Id]));
        Assert.Equal(EnvironmentMessages.KeyDuplicate, Assert.Single(issues.Rows[editor.Rows[2].Id]));
        Assert.False(issues.Rows.ContainsKey(editor.Rows[1].Id));
        Assert.False(editor.CanSave());
    }

    [Fact]
    public void Keys_differing_only_in_case_are_two_variables()
    {
        var editor = EnvironmentEditor.From(Stored(Plain("Token", "1")));
        editor.AddRow();
        editor.Rows[1].Key = "token";
        editor.Rows[1].Value = "2";

        Assert.True(editor.Issues().IsEmpty);
        Assert.True(editor.CanSave());
    }

    [Fact]
    public void A_key_the_service_would_refuse_is_shown_on_its_row()
    {
        var editor = EnvironmentEditor.From(Stored());
        editor.AddRow();
        editor.Rows[0].Key = "has space";

        var messages = editor.Issues().Rows[editor.Rows[0].Id];

        Assert.Equal(EnvironmentMessages.KeyShape, Assert.Single(messages));
    }

    [Fact]
    public void More_than_200_variables_are_refused_for_the_table()
    {
        var editor = EnvironmentEditor.From(Stored());
        for (var index = 0; index < EnvironmentRules.VariablesPerEnvironment + 1; index++)
        {
            editor.AddRow();
            editor.Rows[index].Key = $"v{index}";
        }

        var issues = editor.Issues();

        Assert.Contains(EnvironmentMessages.VariableLimit, issues.Table);
        Assert.False(editor.CanSave());
    }

    [Fact]
    public void A_name_is_required_and_is_saved_trimmed()
    {
        var editor = EnvironmentEditor.From(Stored());

        editor.Name = "   ";
        Assert.NotNull(editor.Issues().Name);
        Assert.False(editor.CanSave());

        editor.Name = "  Production  ";
        Assert.Null(editor.Issues().Name);
        Assert.Equal("Production", editor.ToUpdateBody().Name);
    }

    [Fact]
    public void The_service_field_messages_go_to_the_row_they_name_and_the_rest_to_the_table()
    {
        var editor = EnvironmentEditor.From(Stored(Secret("token"), Plain("baseUrl", "https://a")));

        editor.ApplyServerErrors(new Dictionary<string, string[]>
        {
            ["variables"] =
            [
                "token: a secret needs a value, or keepExistingSecret when one is already stored.",
                "The key may use only letters, digits, '_', '.' and '-'.",
            ],
            ["name"] = ["The name must contain a non-space character."],
        });

        var issues = editor.Issues();
        Assert.Equal("token: a secret needs a value, or keepExistingSecret when one is already stored.", Assert.Single(issues.Rows[editor.Rows[0].Id]));
        Assert.False(issues.Rows.ContainsKey(editor.Rows[1].Id));
        Assert.Equal("The key may use only letters, digits, '_', '.' and '-'.", Assert.Single(issues.Table));
        Assert.Equal("The name must contain a non-space character.", issues.Name);
    }

    [Fact]
    public void Service_messages_are_cleared_before_the_next_save()
    {
        var editor = EnvironmentEditor.From(Stored(Plain("a", "1")));
        editor.ApplyServerErrors(new Dictionary<string, string[]> { ["variables"] = ["a: something refused."] });

        editor.ClearServerErrors();

        Assert.True(editor.Issues().IsEmpty);
    }

    [Fact]
    public void Marking_saved_takes_the_service_copy_and_clears_what_was_typed()
    {
        var editor = EnvironmentEditor.From(Stored(Secret("token")));
        editor.Rows[0].TypedSecret = "typed-before-save"; // gitleaks:allow
        editor.Name = "Renamed";

        editor.MarkSaved(Stored(Secret("token")));

        Assert.False(editor.IsDirty);
        Assert.False(editor.Rows[0].HasTypedSecret);
        Assert.Equal("Staging", editor.Name);
    }

    [Fact]
    public void Removing_a_row_takes_its_typed_value_with_it()
    {
        var editor = EnvironmentEditor.From(Stored(Secret("first"), Secret("second")));
        var first = editor.Rows[0];
        var second = editor.Rows[1];
        second.TypedSecret = "typed-for-second"; // gitleaks:allow

        editor.RemoveRow(first.Id);

        var remaining = Assert.Single(editor.Rows);
        Assert.Same(second, remaining);
        Assert.True(remaining.HasTypedSecret);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void A_disabled_row_keeps_its_enabled_flag_in_the_write()
    {
        var editor = EnvironmentEditor.From(Stored(Plain("off", "x", enabled: false)));

        Assert.False(editor.ToUpdateBody().Variables[0].Enabled);
        Assert.False(editor.IsDirty);
    }
}
