extern alias collections;

using System.Text.Json;
using System.Text.Json.Nodes;
using PostmanClone.Web.Api;
using Service = collections::PostmanClone.CollectionsService.Contracts;

namespace PostmanClone.Web.Tests.Api;

/// <summary>
/// The Web's copies of CollectionsService's types still match the service's own serialization (CONTRACTS §5, BLAZOR-WEB-APP §5).
/// The service's real types are written as JSON, read through the copies, and written again: the two JSON trees must be equal.
/// A renamed or retyped field is then a failing test, not a blank column in the tree.
/// </summary>
public sealed class CollectionsRoundTripTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_service_tree_reads_into_the_copies_and_writes_back_unchanged()
    {
        var service = ServiceTree();
        var original = JsonNode.Parse(JsonSerializer.Serialize(service, WebOptions));

        var copy = JsonSerializer.Deserialize<CollectionTree>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(copy, WebOptions));

        Assert.True(JsonNode.DeepEquals(original, again), $"The tree changed on the way through the copies:\n{original}\n{again}");
        Assert.Equal(service.Folders.Count, copy!.Folders.Count);
        Assert.Equal(service.Requests.Count, copy.Requests.Count);
    }

    [Fact]
    public void The_full_request_definition_keeps_every_field_through_the_copies()
    {
        var saved = ServiceSavedRequest();
        var original = JsonNode.Parse(JsonSerializer.Serialize(saved, WebOptions));

        var copy = JsonSerializer.Deserialize<SavedRequestDto>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(copy, WebOptions));

        Assert.True(JsonNode.DeepEquals(original, again));
        Assert.Equal("{{api.token}}", copy!.Request.Auth!.BearerToken);
        Assert.Equal(7, copy.Version);
    }

    [Fact]
    public void A_page_of_summaries_reads_into_the_copies()
    {
        var page = new Service.PageResult<Service.CollectionSummary>(
            [new Service.CollectionSummary(Guid.NewGuid(), "Billing", null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 3)],
            Page: 1,
            PageSize: 100,
            Total: 1);
        var original = JsonNode.Parse(JsonSerializer.Serialize(page, WebOptions));

        var copy = JsonSerializer.Deserialize<PageResult<CollectionSummary>>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(copy, WebOptions));

        Assert.True(JsonNode.DeepEquals(original, again));
        Assert.Equal(3, copy!.Items[0].RequestCount);
    }

    [Fact]
    public void The_bodies_the_web_sends_are_read_by_the_service_contract()
    {
        var definition = WebDefinition();

        AssertServiceReads<CreateCollectionBody, Service.CreateCollectionRequest>(new CreateCollectionBody("Billing", "Invoices"));
        AssertServiceReads<UpdateCollectionBody, Service.UpdateCollectionRequest>(new UpdateCollectionBody("Billing", null));
        AssertServiceReads<CreateFolderBody, Service.CreateFolderRequest>(new CreateFolderBody("Paid", Guid.NewGuid()));
        AssertServiceReads<RenameFolderBody, Service.RenameFolderRequest>(new RenameFolderBody("Archive"));
        AssertServiceReads<UpdateSavedRequestBody, Service.UpdateRequestBody>(new UpdateSavedRequestBody("Refund", "d", definition));
        AssertServiceReads<MoveRequestBody, Service.MoveRequestBody>(new MoveRequestBody(CollectionId: null, FolderId: Guid.NewGuid(), SortOrder: null));
    }

    /// <summary>Writes the copy as JSON, reads it as the service's type, writes that again: the two JSON trees must match.</summary>
    private static void AssertServiceReads<TWeb, TService>(TWeb body)
    {
        var original = JsonNode.Parse(JsonSerializer.Serialize(body, WebOptions));

        var read = JsonSerializer.Deserialize<TService>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(read, WebOptions));

        Assert.NotNull(read);
        Assert.True(JsonNode.DeepEquals(original, again), $"{typeof(TWeb).Name} does not match {typeof(TService).Name}:\n{original}\n{again}");
    }

    private static Service.CollectionTree ServiceTree()
    {
        var collectionId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new Service.CollectionTree(
            new Service.CollectionDto(collectionId, "Café ✓ billing", "Unicode and a description", Guid.NewGuid(), now, now),
            [new Service.FolderDto(folderId, collectionId, null, "Invoices", 0), new Service.FolderDto(Guid.NewGuid(), collectionId, folderId, "Paid", 1)],
            [new Service.RequestSummary(Guid.NewGuid(), collectionId, folderId, "List", "GET", 0, now)]);
    }

    private static Service.SavedRequestDto ServiceSavedRequest()
    {
        var now = DateTimeOffset.UtcNow;
        var definition = new Service.RequestDefinition(
            "PATCH",
            "{{base.url}}/items/{{item.id}}",
            Params: [],
            Headers: [new Service.KeyValue("X-Trace", "on", Enabled: false, Description: "tracing")],
            Body: new Service.RequestBody("raw", "json", "{\"ok\":true}", Fields: null),
            Auth: new Service.RequestAuth("bearer", "{{api.token}}", null, null, null, null, null));
        return new Service.SavedRequestDto(Guid.NewGuid(), Guid.NewGuid(), null, "Patch item", null, definition, 2, 7, Guid.NewGuid(), now, now);
    }

    private static RequestDefinition WebDefinition() => new(
        "POST",
        "https://api.example.test/items",
        Params: [new KeyValue("page", "1")],
        Headers: [],
        Body: new RequestBody("formUrlEncoded", null, null, [new KeyValue("name", "{{name}}")]),
        Auth: new RequestAuth("apiKey", null, null, null, "X-Key", "{{api.key}}", "header"));
}
