using PostmanClone.Web.Api;
using PostmanClone.Web.Collections;

namespace PostmanClone.Web.Tests.Collections;

/// <summary>
/// The import rules of WP23, xUnit with no rendering: the client-side limits at their boundaries, the file name that is sent,
/// the grouping of warnings by code, and the messages for each refusal. The service keeps the same limits (ADR-0016).
/// </summary>
public sealed class ImportRulesTests
{
    [Fact]
    public void A_file_at_the_five_megabyte_limit_may_be_sent()
    {
        Assert.Null(ImportRules.FileProblem("billing.json", ImportRules.MaximumFileBytes, "application/json"));
    }

    [Fact]
    public void A_file_one_byte_over_the_limit_is_refused_before_it_is_sent()
    {
        var problem = ImportRules.FileProblem("billing.json", ImportRules.MaximumFileBytes + 1, "application/json");

        Assert.NotNull(problem);
        Assert.Contains("5 MB", problem);
    }

    [Fact]
    public void An_empty_file_is_refused()
    {
        Assert.Contains("empty", ImportRules.FileProblem("billing.json", 0, "application/json"));
    }

    [Fact]
    public void A_json_extension_is_accepted_in_any_case()
    {
        Assert.Null(ImportRules.FileProblem("Billing.JSON", 10, "text/plain"));
    }

    [Fact]
    public void An_application_json_content_type_is_accepted_without_an_extension()
    {
        Assert.Null(ImportRules.FileProblem("collection", 10, "application/json"));
    }

    [Fact]
    public void A_file_that_is_neither_json_by_extension_nor_by_type_is_refused()
    {
        var problem = ImportRules.FileProblem("notes.txt", 10, "text/plain");

        Assert.NotNull(problem);
        Assert.Contains(".json", problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_command_is_refused(string? command)
    {
        Assert.NotNull(ImportRules.CommandProblem(command));
    }

    [Fact]
    public void A_command_of_exactly_the_limit_may_be_sent_and_one_more_character_is_refused()
    {
        Assert.Null(ImportRules.CommandProblem(new string('a', ImportRules.MaximumCommandLength)));

        var problem = ImportRules.CommandProblem(new string('a', ImportRules.MaximumCommandLength + 1));

        Assert.NotNull(problem);
        Assert.Contains("20000", problem);
    }

    [Fact]
    public void A_file_name_keeps_its_leaf_and_loses_quotes_and_control_characters()
    {
        Assert.Equal("myfile.json", ImportRules.SafeFileName("../../my\"file\n.json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("/")]
    public void A_file_name_with_nothing_usable_gets_a_default(string? name)
    {
        Assert.Equal("collection.json", ImportRules.SafeFileName(name));
    }

    [Fact]
    public void A_long_file_name_is_cut_to_its_limit()
    {
        var name = ImportRules.SafeFileName(new string('a', 400) + ".json");

        Assert.Equal(ImportRules.MaximumFileNameLength, name.Length);
    }

    [Fact]
    public void A_postman_import_says_what_it_created()
    {
        var result = new ImportResult(Guid.NewGuid(), "Billing", FoldersCreated: 2, RequestsCreated: 1, []);

        Assert.Equal("Created \"Billing\" with 2 folders and 1 request.", ImportRules.Summary(ImportKind.Postman, result));
    }

    [Fact]
    public void A_curl_import_says_what_it_added_to_which_collection()
    {
        var result = new ImportResult(Guid.NewGuid(), "Billing", FoldersCreated: 0, RequestsCreated: 1, []);

        Assert.Equal("Added 1 request to \"Billing\".", ImportRules.Summary(ImportKind.Curl, result));
    }

    [Fact]
    public void Warnings_are_grouped_by_code_in_a_fixed_order_with_the_known_codes_first()
    {
        var groups = ImportWarnings.Group(
        [
            new ImportWarning("name_adjusted", "Trimmed.", "$.item[0]"),
            new ImportWarning("made_up_code", "Something new.", "$.item[1]"),
            new ImportWarning("scripts_ignored", "Not run.", "$.item[2]"),
            new ImportWarning("secret_not_imported", "Replaced.", "$.item[3]"),
        ]);

        Assert.Equal(
            ["secret_not_imported", "scripts_ignored", "name_adjusted", "made_up_code"],
            groups.Select(group => group.Code));
    }

    [Fact]
    public void Each_group_counts_its_items_and_keeps_the_order_the_service_gave()
    {
        var groups = ImportWarnings.Group(
        [
            new ImportWarning("unsupported_body", "First.", "$.item[4]"),
            new ImportWarning("secret_not_imported", "A secret.", "$.item[0]"),
            new ImportWarning("unsupported_body", "Second.", "$.item[1]"),
        ]);

        var bodies = Assert.Single(groups, group => group.Code == "unsupported_body");
        Assert.Equal(2, bodies.Items.Count);
        Assert.Equal(["$.item[4]", "$.item[1]"], bodies.Items.Select(item => item.Path));
        Assert.Equal(["First.", "Second."], bodies.Items.Select(item => item.Message));
    }

    [Fact]
    public void A_code_the_page_does_not_know_still_gets_a_group_with_a_generic_title()
    {
        var description = ImportWarnings.Describe("something_new");

        Assert.Equal("Other warnings", description.Title);
    }

    [Fact]
    public void No_warnings_make_no_groups()
    {
        Assert.Empty(ImportWarnings.Group([]));
    }

    [Fact]
    public void The_secret_group_is_the_one_the_page_links_to_the_environments_from()
    {
        Assert.Equal("secret_not_imported", ImportWarnings.SecretCode);
        Assert.Contains("environment", ImportWarnings.Describe(ImportWarnings.SecretCode).Explanation);
    }

    [Theory]
    [InlineData(ImportKind.Postman, CollectionOutcome.Invalid, "import_invalid", "valid Postman v2.1")]
    [InlineData(ImportKind.Curl, CollectionOutcome.Invalid, "import_invalid", "cURL command")]
    [InlineData(ImportKind.Postman, CollectionOutcome.TooLarge, "import_too_large", "5 MB")]
    [InlineData(ImportKind.Curl, CollectionOutcome.TooLarge, "validation_failed", "too large")]
    [InlineData(ImportKind.Postman, CollectionOutcome.Conflict, "conflict", "200")]
    [InlineData(ImportKind.Curl, CollectionOutcome.NotFound, "not_found", "no longer exists")]
    [InlineData(ImportKind.Curl, CollectionOutcome.Forbidden, "forbidden", "permission")]
    [InlineData(ImportKind.Postman, CollectionOutcome.Refused, "validation_failed", "refused")]
    public void Each_refusal_is_said_in_plain_words_for_its_kind(ImportKind kind, CollectionOutcome outcome, string code, string expected)
    {
        Assert.Contains(expected, ImportMessages.For(kind, outcome, code));
    }

    [Fact]
    public void A_refusal_that_is_not_about_the_import_uses_the_shared_message()
    {
        Assert.Equal(
            CollectionMessages.For(CollectionOutcome.Unavailable, null),
            ImportMessages.For(ImportKind.Postman, CollectionOutcome.Unavailable, null));
    }

    [Fact]
    public void A_refused_import_never_reads_as_a_success()
    {
        Assert.DoesNotContain("Created", ImportMessages.For(ImportKind.Postman, CollectionOutcome.Invalid, "import_invalid"));
    }
}
