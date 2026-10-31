using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Requests;

/// <summary>
/// The saved-request routes of CONTRACTS §2 against each provider: create, read, the <c>If-Match</c> precondition,
/// duplicate, move, delete, the definition limits and the 5000-request limit of a collection.
/// </summary>
public sealed class RequestEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Member = "Member";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly HttpStatusCode PayloadTooLarge = (HttpStatusCode)413;

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_stores_the_definition_and_answers_created_with_version_one(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var sent = RequestCalls.Sample();

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/requests", token,
            new { name = "List payments", request = sent }, Ct);
        var created = await response.Content.ReadFromJsonAsync<SavedRequestDto>(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/requests/{created!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
        Assert.Equal(collection, created.CollectionId);
        Assert.Null(created.FolderId);
        Assert.Equal("List payments", created.Name);
        Assert.Equal(1, created.Version);
        Assert.Equal(0, created.SortOrder);
        Assert.Equal(CollectionsCalls.SubjectOf(token), created.CreatedBy);
        Assert.Equal(RequestCalls.Canonical(sent), RequestCalls.Canonical(created.Request));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_appends_each_request_after_the_last_one_of_its_folder(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Refunds", null, Ct);

        await RequestCalls.CreateRequestAsync(client, token, collection, "A", folder, RequestCalls.Sample(), Ct);
        await RequestCalls.CreateRequestAsync(client, token, collection, "B", folder, RequestCalls.Sample(), Ct);
        await RequestCalls.CreateRequestAsync(client, token, collection, "C", null, RequestCalls.Sample(), Ct);
        var tree = await RequestCalls.TreeAsync(client, token, collection, Ct);

        Assert.Equal(new[] { ("A", 0), ("B", 1) }, RequestCalls.Slot(tree, folder));
        Assert.Equal(new[] { ("C", 0) }, RequestCalls.Slot(tree, null));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_a_folder_of_another_collection_with_400_named_by_field(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var payments = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var refunds = await CollectionsCalls.CreateCollectionAsync(client, token, "Refunds", Ct);
        var refundsFolder = await CollectionsCalls.CreateFolderAsync(client, token, refunds, "Old", null, Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{payments}/requests", token,
            new { name = "Misplaced", folderId = refundsFolder, request = RequestCalls.Sample() }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await CollectionsCalls.JsonAsync(response, Ct);
        Assert.Equal("validation_failed", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(new[] { "folderId" }, ErrorFields(problem));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_a_literal_secret_with_secret_literal_not_allowed(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var literal = RequestCalls.Sample() with { Auth = new RequestAuth("bearer", "Bearer abc", null, null, null, null, null) };

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/requests", token,
            new { name = "Secret", request = literal }, Ct);

        using var problem = await CollectionsCalls.JsonAsync(response, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("secret_literal_not_allowed", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(new[] { "request.auth.bearerToken" }, ErrorFields(problem));
        Assert.DoesNotContain("Bearer abc", problem.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_an_invalid_method_with_400_naming_the_field(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/requests", token,
            new { name = "Lower", request = RequestCalls.Sample() with { Method = "get" } }, Ct);

        using var problem = await CollectionsCalls.JsonAsync(response, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(new[] { "request.method" }, ErrorFields(problem));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_a_definition_one_byte_over_1_5_MB_and_accepts_one_exactly_at_it(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);

        using var over = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/requests", token,
            new { name = "Too big", request = RequestSizing.OfSerializedSize(RequestDefinitionLimits.MaxJsonBytes + 1) }, Ct);
        using var atLimit = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/requests", token,
            new { name = "Exactly", request = RequestSizing.OfSerializedSize(RequestDefinitionLimits.MaxJsonBytes) }, Ct);

        Assert.Equal(PayloadTooLarge, over.StatusCode);
        Assert.Equal("payload_too_large", await ProblemJson.CodeAsync(over, Ct));
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Create_refuses_the_request_past_5000_in_a_collection_and_accepts_the_5000th(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        await Storage.AddRequestsAsync(factory, workspace, collection, CollectionLimits.MaxRequestsPerCollection - 1, Ct);

        var fifth = await RequestCalls.CreateRequestAsync(client, token, collection, "The 5000th", null, RequestCalls.Sample(), Ct);
        using var sixth = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/collections/{collection}/requests", token,
            new { name = "The 5001st", request = RequestCalls.Sample() }, Ct);
        using var duplicate = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{fifth}/duplicate", token, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.Conflict, sixth.StatusCode);
        Assert.Equal("conflict", await ProblemJson.CodeAsync(sixth, Ct));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Get_returns_the_definition_unchanged_through_storage_with_unicode_empty_arrays_and_nulls(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var unicode = new RequestDefinition(
            "PATCH",
            "https://example.invalid/zażółć/🚀?q=ß",
            [],
            [new KeyValue("X-Greeting", "Dzień dobry ✓", true, null), new KeyValue("X-Off", string.Empty, false, "off for now")],
            new RequestBody("raw", "json", "{\"name\":\"Zoë 🚀\"}", []),
            new RequestAuth("apiKey", null, null, null, "api_key", "{{api_key}}", "query"));
        var sparse = new RequestDefinition("GET", "https://example.invalid/items", null, [], null, null);

        var unicodeId = await RequestCalls.CreateRequestAsync(client, token, collection, "Unicode", null, unicode, Ct);
        var sparseId = await RequestCalls.CreateRequestAsync(client, token, collection, "Sparse", null, sparse, Ct);
        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/requests/{unicodeId}", token, cancellationToken: Ct);
        var stored = await RequestCalls.ReadAsync(client, token, unicodeId, Ct);
        var storedSparse = await RequestCalls.ReadAsync(client, token, sparseId, Ct);

        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
        Assert.Equal(RequestCalls.Canonical(unicode), RequestCalls.Canonical(stored.Request));
        Assert.Equal(RequestCalls.Canonical(RequestDefinitionValidator.Normalize(sparse)), RequestCalls.Canonical(storedSparse.Request));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Get_answers_not_found_for_an_absent_request(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/requests/{Guid.NewGuid()}", token, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ProblemJson.CodeAsync(response, Ct));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Update_with_if_match_replaces_the_definition_and_refuses_a_stale_version(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var id = await RequestCalls.CreateRequestAsync(client, token, collection, "List", null, RequestCalls.Sample(), Ct);
        var first = RequestCalls.Sample("https://example.invalid/first");
        var second = RequestCalls.Sample("https://example.invalid/second");
        var body = new { name = "Renamed", description = (string?)null, request = first };

        using var current = await RequestCalls.SendWithIfMatchAsync(client, HttpMethod.Put, $"/api/v1/requests/{id}", token, body, "\"1\"", Ct);
        var updated = await current.Content.ReadFromJsonAsync<SavedRequestDto>(Ct);

        using var stale = await RequestCalls.SendWithIfMatchAsync(client, HttpMethod.Put, $"/api/v1/requests/{id}", token,
            new { name = "Stale", description = (string?)null, request = second }, "\"1\"", Ct);
        var afterStale = await RequestCalls.ReadAsync(client, token, id, Ct);

        using var anyVersion = await RequestCalls.SendWithIfMatchAsync(client, HttpMethod.Put, $"/api/v1/requests/{id}", token,
            new { name = "Any", description = (string?)null, request = second }, "*", Ct);
        var afterAny = await RequestCalls.ReadAsync(client, token, id, Ct);

        using var malformed = await RequestCalls.SendWithIfMatchAsync(client, HttpMethod.Put, $"/api/v1/requests/{id}", token,
            new { name = "Typo", description = (string?)null, request = second }, "abc", Ct);

        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal("\"2\"", current.Headers.ETag?.Tag);
        Assert.Equal(2, updated!.Version);
        Assert.Equal(RequestCalls.Canonical(first), RequestCalls.Canonical(updated.Request));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("conflict", await ProblemJson.CodeAsync(stale, Ct));
        Assert.Equal(2, afterStale.Version);
        Assert.Equal("Renamed", afterStale.Name);
        Assert.Equal(RequestCalls.Canonical(first), RequestCalls.Canonical(afterStale.Request));

        Assert.Equal(HttpStatusCode.OK, anyVersion.StatusCode);
        Assert.Equal(3, afterAny.Version);
        Assert.Equal(RequestCalls.Canonical(second), RequestCalls.Canonical(afterAny.Request));

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(new[] { "If-Match" }, ErrorFields(await CollectionsCalls.JsonAsync(malformed, Ct)));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Update_refuses_a_literal_secret_and_keeps_the_stored_definition(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var id = await RequestCalls.CreateRequestAsync(client, token, collection, "List", null, RequestCalls.Sample(), Ct);
        var literal = RequestCalls.Sample() with { Auth = new RequestAuth("basic", null, "user", "not-a-variable", null, null, null) };

        using var response = await RequestCalls.SendWithIfMatchAsync(client, HttpMethod.Put, $"/api/v1/requests/{id}", token,
            new { name = "List", description = (string?)null, request = literal }, null, Ct);
        var stored = await RequestCalls.ReadAsync(client, token, id, Ct);

        using var problem = await CollectionsCalls.JsonAsync(response, Ct);
        Assert.Equal("secret_literal_not_allowed", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(new[] { "request.auth.password" }, ErrorFields(problem));
        Assert.Equal(1, stored.Version);
        Assert.Equal(RequestCalls.Canonical(RequestCalls.Sample()), RequestCalls.Canonical(stored.Request));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Duplicate_copies_the_definition_into_the_same_folder_named_copy(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Refunds", null, Ct);
        var original = await RequestCalls.CreateRequestAsync(client, token, collection, "Get items", folder, RequestCalls.Sample(), Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{original}/duplicate", token, cancellationToken: Ct);
        var copy = await response.Content.ReadFromJsonAsync<SavedRequestDto>(Ct);
        var tree = await RequestCalls.TreeAsync(client, token, collection, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/requests/{copy!.Id}", response.Headers.Location?.OriginalString);
        Assert.NotEqual(original, copy.Id);
        Assert.Equal("Get items copy", copy.Name);
        Assert.Equal(folder, copy.FolderId);
        Assert.Equal(1, copy.Version);
        Assert.Equal(new[] { ("Get items", 0), ("Get items copy", 1) }, RequestCalls.Slot(tree, folder));
        Assert.Equal(RequestCalls.Canonical(RequestCalls.Sample()), RequestCalls.Canonical(copy.Request));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Duplicate_shortens_the_name_so_the_copy_still_fits_200_characters(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var longName = new string('x', 200);
        var original = await RequestCalls.CreateRequestAsync(client, token, collection, longName, null, RequestCalls.Sample(), Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{original}/duplicate", token, cancellationToken: Ct);
        var copy = await response.Content.ReadFromJsonAsync<SavedRequestDto>(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new string('x', 195) + " copy", copy!.Name);
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_within_a_folder_places_the_request_at_the_requested_position(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Refunds", null, Ct);
        await RequestCalls.CreateRequestAsync(client, token, collection, "A", folder, RequestCalls.Sample(), Ct);
        await RequestCalls.CreateRequestAsync(client, token, collection, "B", folder, RequestCalls.Sample(), Ct);
        var third = await RequestCalls.CreateRequestAsync(client, token, collection, "C", folder, RequestCalls.Sample(), Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{third}/move", token,
            new { collectionId = (Guid?)null, folderId = folder, sortOrder = 0 }, Ct);
        var tree = await RequestCalls.TreeAsync(client, token, collection, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { ("C", 0), ("A", 1), ("B", 2) }, RequestCalls.Slot(tree, folder));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_to_another_collection_of_the_workspace_lands_on_top_and_closes_the_gap(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var payments = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var refunds = await CollectionsCalls.CreateCollectionAsync(client, token, "Refunds", Ct);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, payments, "Auth", null, Ct);
        var moved = await RequestCalls.CreateRequestAsync(client, token, payments, "A", folder, RequestCalls.Sample(), Ct);
        await RequestCalls.CreateRequestAsync(client, token, payments, "B", folder, RequestCalls.Sample(), Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{moved}/move", token,
            new { collectionId = refunds, folderId = (Guid?)null, sortOrder = (int?)null }, Ct);
        var moveTo = await RequestCalls.TreeAsync(client, token, refunds, Ct);
        var leftBehind = await RequestCalls.TreeAsync(client, token, payments, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(refunds, (await response.Content.ReadFromJsonAsync<SavedRequestDto>(Ct))!.CollectionId);
        Assert.Equal(new[] { ("A", 0) }, RequestCalls.Slot(moveTo, null));
        Assert.Equal(new[] { ("B", 0) }, RequestCalls.Slot(leftBehind, folder));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_into_a_folder_of_another_collection_is_refused_with_400(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var payments = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var refunds = await CollectionsCalls.CreateCollectionAsync(client, token, "Refunds", Ct);
        var paymentsFolder = await CollectionsCalls.CreateFolderAsync(client, token, payments, "Auth", null, Ct);
        var moved = await RequestCalls.CreateRequestAsync(client, token, payments, "A", null, RequestCalls.Sample(), Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{moved}/move", token,
            new { collectionId = refunds, folderId = paymentsFolder, sortOrder = (int?)null }, Ct);

        using var problem = await CollectionsCalls.JsonAsync(response, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(new[] { "folderId" }, ErrorFields(problem));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Move_into_a_collection_at_the_request_limit_is_refused_with_409(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var workspace = Guid.NewGuid();
        var token = CollectionsCalls.Token(tokens, workspace, Member);
        var payments = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var full = await CollectionsCalls.CreateCollectionAsync(client, token, "Full", Ct);
        var moved = await RequestCalls.CreateRequestAsync(client, token, payments, "A", null, RequestCalls.Sample(), Ct);
        await Storage.AddRequestsAsync(factory, workspace, full, CollectionLimits.MaxRequestsPerCollection, Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Post, $"/api/v1/requests/{moved}/move", token,
            new { collectionId = full, folderId = (Guid?)null, sortOrder = (int?)null }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict", await ProblemJson.CodeAsync(response, Ct));
    }

    [Theory]
    [MemberData(nameof(Storage.Providers), MemberType = typeof(Storage))]
    public async Task Delete_removes_the_request_and_closes_the_gap_in_its_folder(string storage)
    {
        using var tokens = new TestTokens();
        await using var factory = Storage.Start(tokens, storage, postgres);
        await SchemaWait.AppliedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var token = CollectionsCalls.Token(tokens, Guid.NewGuid(), Member);
        var collection = await CollectionsCalls.CreateCollectionAsync(client, token, "Payments", Ct);
        var folder = await CollectionsCalls.CreateFolderAsync(client, token, collection, "Refunds", null, Ct);
        var first = await RequestCalls.CreateRequestAsync(client, token, collection, "A", folder, RequestCalls.Sample(), Ct);
        await RequestCalls.CreateRequestAsync(client, token, collection, "B", folder, RequestCalls.Sample(), Ct);
        await RequestCalls.CreateRequestAsync(client, token, collection, "C", folder, RequestCalls.Sample(), Ct);

        using var response = await CollectionsCalls.SendAsync(client, HttpMethod.Delete, $"/api/v1/requests/{first}", token, cancellationToken: Ct);
        using var gone = await CollectionsCalls.SendAsync(client, HttpMethod.Get, $"/api/v1/requests/{first}", token, cancellationToken: Ct);
        var tree = await RequestCalls.TreeAsync(client, token, collection, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(new[] { ("B", 0), ("C", 1) }, RequestCalls.Slot(tree, folder));
    }

    private static string[] ErrorFields(JsonDocument problem) =>
        problem.RootElement.TryGetProperty("errors", out var errors)
            ? errors.EnumerateObject().Select(property => property.Name).ToArray()
            : [];
}
