using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Interop.Postman;
using PostmanClone.CollectionsService.Services;
using PostmanClone.CollectionsService.Tests.Support;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Interop.Postman;

/// <summary>
/// The importer on its own: no HTTP and no database. Each rule of WP16 has a test here, and the hostile files of the work
/// package are refused with a stable code, never a server error (ADR-0016).
/// </summary>
public sealed class PostmanV2ImporterTests
{
    private static ImportPlan Plan(byte[] file)
    {
        var outcome = PostmanV2Importer.Parse(file);
        Assert.True(outcome.Failure is null, $"The import was refused with {outcome.Failure?.Code}.");
        return outcome.Value!;
    }

    private static Failure Refusal(byte[] file)
    {
        var outcome = PostmanV2Importer.Parse(file);
        Assert.NotNull(outcome.Failure);
        return outcome.Failure!;
    }

    private static IEnumerable<string> Codes(ImportPlan plan) => plan.Warnings.Select(warning => warning.Code);

    private static RequestDefinition Definition(ImportPlan plan, string name) =>
        plan.Requests.Single(request => request.Name == name).Definition;

    [Fact]
    public void Minimal_collection_becomes_one_request_at_the_top()
    {
        var plan = Plan(PostmanFixtures.Read("minimal.json"));

        Assert.Equal("Minimal", plan.Name);
        Assert.Empty(plan.Folders);
        var request = Assert.Single(plan.Requests);
        Assert.Null(request.FolderKey);
        Assert.Equal("GET", request.Definition.Method);
        Assert.Equal("https://example.invalid/ping", request.Definition.Url);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Nested_folders_keep_their_places_and_trim_the_collection_name()
    {
        var plan = Plan(PostmanFixtures.Read("nested-folders.json"));

        // The collection name is trimmed, and the trim is said so.
        Assert.Equal("Nested folders", plan.Name);
        Assert.Contains("name_adjusted", Codes(plan));
        Assert.Equal("Folders inside folders, with requests at every level.", plan.Description);

        var users = plan.Folders.Single(folder => folder.Name == "Users");
        var admin = plan.Folders.Single(folder => folder.Name == "Admin");
        var empty = plan.Folders.Single(folder => folder.Name == "Empty folder");
        Assert.Null(users.ParentKey);
        Assert.Equal(users.Key, admin.ParentKey);
        Assert.Null(empty.ParentKey);
        Assert.Equal(new[] { 0, 1 }, new[] { users.SortOrder, empty.SortOrder });

        var listed = Assert.Single(plan.Requests, request => request.Name == "List admins");
        Assert.Equal(admin.Key, listed.FolderKey);

        // The query is carried by the rows, and the URL keeps only what is not a row: the disabled row stays a row.
        Assert.Equal("https://example.invalid/users/admins", listed.Definition.Url);
        Assert.Equal(new[] { ("page", "1", true), ("size", "20", false) }, listed.Definition.Params!.Select(row => (row.Key, row.Value, row.Enabled)));
        Assert.Equal("Liveness probe.", plan.Requests.Single(request => request.Name == "Health").Description);
    }

    [Fact]
    public void Every_body_mode_maps_to_its_contract_type_and_each_refusal_is_named()
    {
        var plan = Plan(PostmanFixtures.Read("body-modes.json"));

        Assert.Equal("json", Definition(plan, "Raw JSON").Body!.RawLanguage);
        Assert.Equal("{\"name\": \"{{itemName}}\"}", Definition(plan, "Raw JSON").Body!.Raw);
        Assert.Equal("xml", Definition(plan, "Raw XML").Body!.RawLanguage);
        Assert.Equal("text", Definition(plan, "Raw without language").Body!.RawLanguage);

        var form = Definition(plan, "Form urlencoded").Body!;
        Assert.Equal("formUrlEncoded", form.Type);
        Assert.Equal(new[] { true, false }, form.Fields!.Select(row => row.Enabled));

        var data = Definition(plan, "Form data").Body!;
        Assert.Equal("formData", data.Type);
        Assert.Equal(new[] { "title" }, data.Fields!.Select(row => row.Key));

        Assert.Equal("none", Definition(plan, "GraphQL").Body!.Type);
        Assert.Equal("none", Definition(plan, "File body").Body!.Type);

        // A file part, a GraphQL body and a file body each leave a warning that names them, and none of them is dropped silently.
        Assert.Equal(3, plan.Warnings.Count(warning => warning.Code == "unsupported_body"));
        Assert.Contains(plan.Warnings, warning => warning.Path.Contains("formdata[1]", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_auth_type_is_mapped_to_a_reference_or_named_in_a_warning()
    {
        var plan = Plan(PostmanFixtures.Read("auth-types.json"));

        Assert.Equal(new RequestAuth("bearer", "{{apiToken}}", null, null, null, null, null), Definition(plan, "Bearer").Auth);
        Assert.Equal(new RequestAuth("basic", null, "service-user", "{{basicPassword}}", null, null, null), Definition(plan, "Basic").Auth);
        Assert.Equal(new RequestAuth("apiKey", null, null, null, "X-Api-Key", "{{apiKey}}", "header"), Definition(plan, "API key in header").Auth);
        Assert.Equal(new RequestAuth("apiKey", null, null, null, "api_key", "{{apiKey}}", "query"), Definition(plan, "API key in query").Auth);
        Assert.Equal(new RequestAuth("none", null, null, null, null, null, null), Definition(plan, "OAuth 2").Auth);
        Assert.Equal(new RequestAuth("none", null, null, null, null, null, null), Definition(plan, "No auth").Auth);
        var warning = Assert.Single(plan.Warnings, warning => warning.Code == "unsupported_auth");
        Assert.Equal("item[4].request.auth", warning.Path);
    }

    [Fact]
    public void Scripts_and_variables_are_ignored_and_each_one_is_named_in_a_warning()
    {
        var plan = Plan(PostmanFixtures.Read("scripts-and-variables.json"));

        Assert.Equal(2, plan.Warnings.Count(warning => warning.Code == "scripts_ignored"));
        Assert.Equal(2, plan.Warnings.Count(warning => warning.Code == "variables_ignored"));
        Assert.Equal("{{baseUrl}}/checked", Definition(plan, "Checked request").Url);

        // Nothing of a script or a variable reaches the plan.
        var stored = JsonSerializer.Serialize(plan.Requests, JsonSerializerOptions.Web);
        Assert.DoesNotContain("pm.test", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("folderVar", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void A_literal_secret_is_never_kept_and_each_one_is_replaced_by_a_named_variable()
    {
        var plan = Plan(SecretLiterals.Collection());

        var stored = JsonSerializer.Serialize(plan.Requests, JsonSerializerOptions.Web)
            + JsonSerializer.Serialize(plan.Warnings, JsonSerializerOptions.Web);
        foreach (var literal in SecretLiterals.All)
        {
            Assert.DoesNotContain(literal, stored, StringComparison.Ordinal);
        }

        // Six literals, six warnings, each naming the variable that now holds the place.
        var warnings = plan.Warnings.Where(warning => warning.Code == "secret_not_imported").ToList();
        Assert.Equal(6, warnings.Count);
        Assert.All(warnings, warning => Assert.Matches(@"\{\{imported_[a-z_]+_\d+\}\}", warning.Message));

        Assert.StartsWith("{{imported_bearer_token_", Definition(plan, "Bearer literal").Auth!.BearerToken, StringComparison.Ordinal);
        Assert.Equal("svc", Definition(plan, "Basic literal").Auth!.Username);
        Assert.StartsWith("{{imported_basic_password_", Definition(plan, "Basic literal").Auth!.Password, StringComparison.Ordinal);
        Assert.StartsWith("{{imported_api_key_", Definition(plan, "Key literal").Auth!.ApiKeyValue, StringComparison.Ordinal);
        Assert.StartsWith("{{imported_header_", Definition(plan, "Header literal").Headers!.Single().Value, StringComparison.Ordinal);
        Assert.Matches(@"^https://example\.invalid/e\?token=\{\{imported_query_\d+\}\}$", Definition(plan, "Query literal").Url);
        Assert.StartsWith("{{imported_form_", Definition(plan, "Form literal").Body!.Fields!.Single().Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_variable_names_are_unique_within_one_import()
    {
        var plan = Plan(SecretLiterals.Collection());
        var names = plan.Warnings
            .Where(warning => warning.Code == "secret_not_imported")
            .Select(warning => System.Text.RegularExpressions.Regex.Match(warning.Message, @"\{\{(imported_[a-z_]+_\d+)\}\}").Groups[1].Value)
            .ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_name_longer_than_its_limit_is_cut_with_a_warning_and_never_refused()
    {
        var plan = Plan(PostmanFixtures.GiantName(1_000_000));

        Assert.Equal(200, plan.Requests.Single().Name.Length);
        Assert.Contains(plan.Warnings, warning => warning.Code == "name_adjusted" && warning.Path == "item[0].name");
    }

    [Fact]
    public void A_folder_nested_deeper_than_the_model_allows_is_flattened_with_a_warning()
    {
        // Ten folder levels, the request in the deepest one. The model allows eight, so the deepest two levels fold into level eight.
        var file = PostmanFixtures.Collection(WrapFolders(10, "[{\"name\":\"Deep request\",\"request\":\"https://example.invalid\"}]"));

        var plan = Plan(file);

        Assert.Equal(CollectionLimits.MaxFolderDepth, plan.Folders.Count);
        Assert.Equal(CollectionLimits.MaxFolderDepth, MaxLevel(plan));
        Assert.Equal(2, plan.Warnings.Count(warning => warning.Code == "depth_flattened"));
        var request = Assert.Single(plan.Requests);
        Assert.Equal(plan.Folders.Single(folder => folder.Name == "Level 8").Key, request.FolderKey);
    }

    [Fact]
    public void A_folder_nested_deeper_than_twenty_levels_is_refused_as_invalid()
    {
        var failure = Refusal(PostmanFixtures.Collection(WrapFolders(21, "[]")));

        Assert.Equal("import_invalid", failure.Code);
        Assert.Equal(400, failure.Status);
    }

    [Fact]
    public void Each_hostile_file_is_refused_with_a_stable_code_and_no_server_error()
    {
        // Depth 5000 is refused by the parser before any walk; it must answer import_invalid, quickly.
        Assert.Equal("import_invalid", Refusal(PostmanFixtures.NestedFolders(5000)).Code);

        // 100k items: the walker stops at the limit, which is an import_too_large.
        Assert.Equal("import_too_large", Refusal(PostmanFixtures.ManyRequests(100_000)).Code);

        // Wrong types and nulls are refused by the walker, naming the path.
        Assert.Equal("import_invalid", Refusal(PostmanFixtures.Read("hostile-wrong-types.json")).Code);
        Assert.Equal("import_invalid", Refusal(PostmanFixtures.Read("hostile-nulls.json")).Code);

        // A URL of 9000 characters is over the validator's limit (CONTRACTS §1), so the whole file is refused.
        var giant = Refusal(PostmanFixtures.GiantUrl(9000));
        Assert.Equal("import_invalid", giant.Code);
        Assert.Contains(giant.Errors!.Keys, key => key == "item[0].request.url");
    }

    [Fact]
    public void Text_that_is_not_utf8_or_not_json_is_refused()
    {
        Assert.Equal("import_invalid", Refusal([0x7b, 0xff, 0x7d]).Code);
        Assert.Equal("import_invalid", Refusal(PostmanFixtures.Utf8("{ not json")).Code);
        Assert.Equal("import_invalid", Refusal([]).Code);
    }

    [Fact]
    public void A_byte_order_mark_is_accepted()
    {
        var file = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(PostmanFixtures.Read("minimal.json")).ToArray();

        Assert.Single(Plan(file).Requests);
    }

    [Fact]
    public void A_file_of_another_schema_is_refused()
    {
        var file = PostmanFixtures.Utf8("{\"info\":{\"name\":\"Old\",\"schema\":\"https://schema.getpostman.com/json/collection/v1.0.0/collection.json\"},\"item\":[]}");

        var failure = Refusal(file);

        Assert.Equal("import_invalid", failure.Code);
        Assert.Contains("info.schema", failure.Errors!.Keys);
    }

    [Fact]
    public void A_header_with_a_line_break_in_its_name_is_refused_by_the_shared_validator()
    {
        var file = PostmanFixtures.Collection(
            "[{\"name\":\"Split\",\"request\":{\"method\":\"GET\",\"url\":\"https://example.invalid\",\"header\":[{\"key\":\"X-A\\r\\nX-Evil\",\"value\":\"1\"}]}}]");

        var failure = Refusal(file);

        Assert.Equal("import_invalid", failure.Code);
        Assert.Contains("item[0].request.headers[0].key", failure.Errors!.Keys);
    }

    [Fact]
    public void A_request_with_more_than_one_hundred_rows_is_refused()
    {
        var rows = string.Join(",", Enumerable.Range(0, 101).Select(index => $"{{\"key\":\"h{index}\",\"value\":\"v\"}}"));
        var file = PostmanFixtures.Collection(
            "[{\"name\":\"Wide\",\"request\":{\"method\":\"GET\",\"url\":\"https://example.invalid\",\"header\":[" + rows + "]}}]");

        Assert.Equal("import_invalid", Refusal(file).Code);
    }

    [Fact]
    public void A_collection_over_the_request_limit_is_refused_as_too_large()
    {
        // 5001 requests: the 5000 of CONTRACTS §2 are the most one collection holds. The items stay under the 5500 file limit.
        Assert.Equal("import_too_large", Refusal(PostmanFixtures.ManyRequests(CollectionLimits.MaxRequestsPerCollection + 1)).Code);
        Assert.Equal(CollectionLimits.MaxRequestsPerCollection, Plan(PostmanFixtures.ManyRequests(CollectionLimits.MaxRequestsPerCollection)).Requests.Count);
    }

    [Fact]
    public void A_collection_over_the_folder_limit_is_refused_as_too_large()
    {
        var folders = string.Join(",", Enumerable.Range(0, CollectionLimits.MaxFoldersPerCollection + 1).Select(index => $"{{\"name\":\"f{index}\",\"item\":[]}}"));

        Assert.Equal("import_too_large", Refusal(PostmanFixtures.Collection("[" + folders + "]")).Code);
    }

    private static string WrapFolders(int depth, string innermost)
    {
        var text = innermost;
        for (var level = depth; level >= 1; level--)
        {
            text = "[{\"name\":\"Level " + level + "\",\"item\":" + text + "}]";
        }

        return text;
    }

    private static int MaxLevel(ImportPlan plan)
    {
        var level = 0;
        foreach (var folder in plan.Folders)
        {
            var depth = 0;
            var parent = folder.ParentKey;
            depth++;
            while (parent is { } key)
            {
                depth++;
                parent = plan.Folders.Single(candidate => candidate.Key == key).ParentKey;
            }

            level = Math.Max(level, depth);
        }

        return level;
    }
}
