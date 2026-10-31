using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Interop.Curl;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Interop.Curl;

/// <summary>
/// The cURL routes over HTTP (WP17, CONTRACTS §2): the import answers 201 with its result and stores the request the way the
/// parser read it; the export answers the command as <c>text/plain</c>; the limits and refusals answer their codes.
/// </summary>
public sealed class CurlEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Url = "https://api.example.invalid/items";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Import_stores_the_request_and_answers_created_with_the_result(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Imports", Ct);

        using var response = await ImportAsync(client, token, collection,
            "curl -X POST https://api.example.invalid/items -H 'Accept: application/json' -u alice:hunter2 -d 'a=1'", // gitleaks:allow
            folder: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>(JsonSerializerOptions.Web, Ct);
        Assert.Equal(collection, result!.CollectionId);
        Assert.Equal("Imports", result.CollectionName);
        Assert.Equal(1, result.RequestsCreated);
        Assert.Equal(0, result.FoldersCreated);
        Assert.Equal(new[] { "secret_not_imported" }, result.Warnings.Select(warning => warning.Code));
        Assert.DoesNotContain("hunter2", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var tree = await RequestCalls.TreeAsync(client, token, collection, Ct);
        var summary = Assert.Single(tree.Requests);
        Assert.Equal("POST https://api.example.invalid/items", summary.Name);
        Assert.Equal("POST", summary.Method);
        var stored = await RequestCalls.ReadAsync(client, token, summary.Id, Ct);
        Assert.Equal("POST", stored.Request.Method);
        Assert.Equal("a=1", stored.Request.Body!.Raw);
        Assert.Equal("{{password}}", stored.Request.Auth!.Password);
        Assert.Equal("Accept", Assert.Single(stored.Request.Headers!).Key);
    }

    [Fact]
    public async Task Import_into_a_folder_of_the_collection_places_the_request_in_it()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Imports", Ct);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Auth", null, Ct);

        using var response = await ImportAsync(client, token, collection, $"curl {Url}", folder);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tree = await RequestCalls.TreeAsync(client, token, collection, Ct);
        Assert.Equal(folder, Assert.Single(tree.Requests).FolderId);
    }

    [Fact]
    public async Task A_command_of_exactly_20000_characters_is_accepted_and_one_more_is_refused()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Imports", Ct);
        var atLimit = $"curl {Url}" + new string(' ', 20_000 - $"curl {Url}".Length);
        var overLimit = atLimit + " ";

        using var accepted = await ImportAsync(client, token, collection, atLimit, folder: null);
        using var refused = await ImportAsync(client, token, collection, overLimit, folder: null);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("validation_failed", await ProblemJson.CodeAsync(refused, Ct));
    }

    [Theory]
    [InlineData("wget https://api.example.invalid/")]
    [InlineData("curl -X POST")]
    public async Task A_text_that_is_not_a_command_with_a_url_is_import_invalid(string command)
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Imports", Ct);

        using var response = await ImportAsync(client, token, collection, command, folder: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("import_invalid", await ProblemJson.CodeAsync(response, Ct));
    }

    [Fact]
    public async Task The_shared_validator_judges_an_imported_request_like_any_other()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Imports", Ct);

        using var response = await ImportAsync(client, token, collection, $"curl -X 'GE T' {Url}", folder: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ProblemJson.CodeAsync(response, Ct));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("request.method", out _));
    }

    [Fact]
    public async Task A_folder_of_another_collection_is_refused_as_validation_failed_on_folder_id()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var target = await CollectionsCalls.CreateCollectionAsync(client, token, "Target", Ct);
        var other = await CollectionsCalls.CreateCollectionAsync(client, token, "Other", Ct);
        var folderOfOther = await CollectionsCalls.CreateFolderAsync(client, token, other, "Elsewhere", null, Ct);

        using var response = await ImportAsync(client, token, target, $"curl {Url}", folderOfOther);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ProblemJson.CodeAsync(response, Ct));
    }

    [Fact]
    public async Task Export_answers_the_command_as_text_and_it_reads_back_as_the_stored_request()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Exports", Ct);
        var definition = new RequestDefinition(
            "PATCH",
            "https://api.example.invalid/items/{{itemId}}",
            [],
            [new KeyValue("Authorization", "Bearer {{token}}"), new KeyValue("X-Probe", "'; rm -rf / #")],
            new RequestBody("raw", "json", "{\"name\":\"it's\"}", []),
            new RequestAuth("none", null, null, null, null, null, null));
        var request = await RequestCalls.CreateRequestAsync(client, token, collection, "Patch item", null, definition, Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/requests/{request}/export/curl", token, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        var command = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("{{itemId}}", command);
        Assert.Contains("--header 'Authorization: Bearer {{token}}'", command);
        var read = CurlParser.Parse(command).Import!;
        Assert.Equal("PATCH", read.Request.Method);
        Assert.Equal("https://api.example.invalid/items/{{itemId}}", read.Request.Url);
        Assert.Equal("{\"name\":\"it's\"}", read.Request.Body!.Raw);
        Assert.Contains(read.Request.Headers!, header => header.Key == "X-Probe" && header.Value == "'; rm -rf / #");
    }

    [Fact]
    public async Task Export_of_a_missing_request_is_not_found()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/requests/{Guid.NewGuid()}/export/curl", token, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ProblemJson.CodeAsync(response, Ct));
    }

    private static Task<HttpResponseMessage> ImportAsync(HttpClient client, string token, Guid collection, string command, Guid? folder) =>
        CollectionsCalls.SendAsync(client, HttpMethod.Post, "/api/v1/import/curl", token,
            new { command, collectionId = collection, folderId = folder }, Ct);
}
