using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Interop.Postman;
using PostmanClone.CollectionsService.Services;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Interop.Postman;

/// <summary>
/// The import and export routes on the real host, on both providers (CONTRACTS §2, ADR-0016): the answers, the limits, the
/// round trip of every realistic fixture, the literal-secret rule over HTTP, and the transaction of a failed import.
/// </summary>
public sealed class PostmanIoEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static IEnumerable<object[]> FixtureAndStorage =>
        from fixture in PostmanFixtures.Realistic
        from storage in new[] { Storage.InMemory, Storage.PostgreSql }
        select new object[] { fixture, storage };

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Import_json_creates_the_collection_and_reports_what_it_made(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        using var response = await PostmanCalls.ImportJsonAsync(client, token, PostmanFixtures.Read("nested-folders.json"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportResult>(JsonSerializerOptions.Web, Ct))!;
        Assert.Equal($"/api/v1/collections/{result.CollectionId}", response.Headers.Location?.OriginalString);
        Assert.Equal(3, result.FoldersCreated);
        Assert.Equal(3, result.RequestsCreated);
        Assert.Equal("Nested folders", result.CollectionName);

        var tree = await RequestCalls.TreeAsync(client, token, result.CollectionId, Ct);
        Assert.Equal(3, tree.Folders.Count);
        Assert.Equal(3, tree.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Import_multipart_reads_the_file_part(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        using var response = await PostmanCalls.ImportMultipartAsync(client, token, PostmanFixtures.Read("minimal.json"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<ImportResult>(JsonSerializerOptions.Web, Ct))!.RequestsCreated);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Import_refuses_a_form_without_a_file_part_and_another_content_type(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        using var form = new MultipartFormDataContent { { new StringContent("x"), "notes" } };
        using var noFile = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/postman") { Content = form };
        noFile.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var missing = await client.SendAsync(noFile, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("import_invalid", await ProblemJson.CodeAsync(missing, Ct));

        using var text = new HttpRequestMessage(HttpMethod.Post, "/api/v1/import/postman") { Content = new StringContent("{}", System.Text.Encoding.UTF8, "text/plain") };
        text.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var wrong = await client.SendAsync(text, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("import_invalid", await ProblemJson.CodeAsync(wrong, Ct));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Hostile_files_are_refused_with_a_stable_code_and_never_a_server_error(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        // Ten megabytes is refused on its declared length, before a byte of it is read.
        await AssertRefusedAsync(client, token, PostmanFixtures.Oversized(10 * 1024 * 1024), HttpStatusCode.RequestEntityTooLarge, "import_too_large");

        // Nesting of 5000 is refused by the parser, which stops at 64 levels.
        await AssertRefusedAsync(client, token, PostmanFixtures.NestedFolders(5000), HttpStatusCode.BadRequest, "import_invalid");

        // 100k items: the walker stops at 5500 folders and requests.
        await AssertRefusedAsync(client, token, PostmanFixtures.ManyRequests(100_000), HttpStatusCode.RequestEntityTooLarge, "import_too_large");

        await AssertRefusedAsync(client, token, PostmanFixtures.Read("hostile-wrong-types.json"), HttpStatusCode.BadRequest, "import_invalid");
        await AssertRefusedAsync(client, token, PostmanFixtures.Read("hostile-nulls.json"), HttpStatusCode.BadRequest, "import_invalid");
        await AssertRefusedAsync(client, token, PostmanFixtures.GiantUrl(9000), HttpStatusCode.BadRequest, "import_invalid");
        await AssertRefusedAsync(client, token, [0x7b, 0xff, 0x7d], HttpStatusCode.BadRequest, "import_invalid");

        // A name of a megabyte is not refused: it is cut and the cut is named (CONTRACTS §2 name limit).
        var named = await PostmanCalls.ImportedAsync(client, token, PostmanFixtures.GiantName(1_000_000), Ct);
        Assert.Contains(named.Warnings, warning => warning.Code == "name_adjusted");
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Import_never_stores_a_literal_secret(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Owner");

        var result = await PostmanCalls.ImportedAsync(client, token, SecretLiterals.Collection(), Ct);

        Assert.Equal(6, result.Warnings.Count(warning => warning.Code == "secret_not_imported"));
        var canonical = string.Join("\n", await PostmanCalls.CanonicalAsync(client, token, result.CollectionId, Ct));
        var exported = await ExportTextAsync(client, token, result.CollectionId);
        var warnings = JsonSerializer.Serialize(result.Warnings, JsonSerializerOptions.Web);
        foreach (var literal in SecretLiterals.All)
        {
            Assert.DoesNotContain(literal, canonical, StringComparison.Ordinal);
            Assert.DoesNotContain(literal, exported, StringComparison.Ordinal);
            Assert.DoesNotContain(literal, warnings, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Import_is_refused_when_the_workspace_holds_the_collection_limit(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        await Storage.AddCollectionsAsync(factory, workspace, CollectionLimits.MaxCollectionsPerWorkspace, Ct);

        using var response = await PostmanCalls.ImportJsonAsync(client, CollectionsCalls.Token(tokens, workspace, "Member"), PostmanFixtures.Read("minimal.json"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict", await ProblemJson.CodeAsync(response, Ct));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Export_is_a_v21_file_named_as_an_attachment(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var result = await PostmanCalls.ImportedAsync(client, token, PostmanFixtures.Read("minimal.json"), Ct);

        using var response = await PostmanCalls.ExportAsync(client, token, result.CollectionId, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Minimal.postman_collection.json", response.Content.Headers.ContentDisposition?.FileName);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var info = document.RootElement.GetProperty("info");
        Assert.Equal(result.CollectionId.ToString(), info.GetProperty("_postman_id").GetString());
        Assert.Equal(PostmanV2Exporter.Schema, info.GetProperty("schema").GetString());
        Assert.Equal("Ping", document.RootElement.GetProperty("item")[0].GetProperty("name").GetString());
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Export_is_deterministic(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");
        var result = await PostmanCalls.ImportedAsync(client, token, PostmanFixtures.Read("body-modes.json"), Ct);

        var first = await ExportTextAsync(client, token, result.CollectionId);
        var second = await ExportTextAsync(client, token, result.CollectionId);

        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(FixtureAndStorage))]
    public async Task Export_then_import_keeps_the_structure_of_every_realistic_fixture(string fixture, string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), "Member");

        var first = await PostmanCalls.ImportedAsync(client, token, PostmanFixtures.Read(fixture), Ct);
        var exported = Encoding.UTF8.GetBytes(await ExportTextAsync(client, token, first.CollectionId));
        var second = await PostmanCalls.ImportedAsync(client, token, exported, Ct);

        Assert.Equal(
            await PostmanCalls.CanonicalAsync(client, token, first.CollectionId, Ct),
            await PostmanCalls.CanonicalAsync(client, token, second.CollectionId, Ct));

        // The export holds no script and no variable, so importing it again says nothing.
        Assert.Empty(second.Warnings);
        Assert.Equal(first.FoldersCreated, second.FoldersCreated);
        Assert.Equal(first.RequestsCreated, second.RequestsCreated);
    }

    [Fact]
    public async Task Import_failure_leaves_nothing_behind_on_postgresql()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the transaction is a PostgreSQL behaviour.");
        }

        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens, postgres.ConnectionString, interceptor: new FailingSavedRequestInsert());
        await SchemaWait.AppliedAsync(factory, Ct);
        var workspace = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPostmanIoService>();
            var file = new ReadOnlyMemory<byte>(PostmanFixtures.Read("nested-folders.json"));
            await Assert.ThrowsAnyAsync<Exception>(() => service.ImportAsync(new Caller(workspace, "Member", Guid.NewGuid()), file, Ct));
        }

        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        Assert.Equal(0, await db.Collections.CountAsync(collection => collection.WorkspaceId == workspace, Ct));
        Assert.Equal(0, await db.Folders.CountAsync(folder => folder.WorkspaceId == workspace, Ct));
        Assert.Equal(0, await db.SavedRequests.CountAsync(request => request.WorkspaceId == workspace, Ct));
    }

    private static async Task AssertRefusedAsync(HttpClient client, string token, byte[] file, HttpStatusCode status, string code)
    {
        using var response = await PostmanCalls.ImportJsonAsync(client, token, file, Ct);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await ProblemJson.CodeAsync(response, Ct));
    }

    private static async Task<string> ExportTextAsync(HttpClient client, string token, Guid collection)
    {
        using var response = await PostmanCalls.ExportAsync(client, token, collection, Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(Ct);
    }
}
