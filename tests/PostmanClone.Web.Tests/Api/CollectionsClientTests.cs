using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Api;

/// <summary>
/// The collections client against a recording handler (BLAZOR-WEB-APP §13): the route, the method, the bearer, the
/// <c>If-Match</c> precondition, the status-to-outcome mapping and the problem code. A failure is an outcome, not an exception.
/// </summary>
public sealed class CollectionsClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Seen(string Method, string Path, string? Bearer, string? IfMatch, string Body);

    private static (CollectionsClient Client, List<Seen> Requests) Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var requests = new List<Seen>();
        var services = new ServiceCollection();
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient(BackendServices.CollectionsName, client => client.BaseAddress = new Uri("http://collections-service"))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(request =>
            {
                var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                requests.Add(new Seen(
                    request.Method.Method,
                    request.RequestUri!.PathAndQuery,
                    request.Headers.Authorization?.Parameter,
                    request.Headers.TryGetValues("If-Match", out var values) ? values.Single() : null,
                    body));
                return respond(request);
            }));
        return (new CollectionsClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>()), requests);
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string? code) =>
        new(status)
        {
            Content = new StringContent(
                code is null ? "{\"title\":\"Something went wrong.\"}" : $"{{\"status\":{(int)status},\"title\":\"x\",\"code\":\"{code}\"}}",
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        };

    private static CollectionDto Dto(string name) => new(Guid.NewGuid(), name, null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Listing_asks_for_the_page_with_the_bearer_and_reads_the_items()
    {
        var page = new PageResult<CollectionSummary>([new CollectionSummary(Guid.NewGuid(), "Billing", null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2)], 1, 100, 1);
        var (client, requests) = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page, options: Json) });

        var answer = await client.ListCollectionsAsync("bearer-value", 1, 100, Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        Assert.Equal("Billing", answer.Value!.Items[0].Name);
        var seen = Assert.Single(requests);
        Assert.Equal("GET", seen.Method);
        Assert.Equal("/api/v1/collections?page=1&pageSize=100", seen.Path);
        Assert.Equal("bearer-value", seen.Bearer);
    }

    [Fact]
    public async Task A_created_collection_is_sent_as_json_and_read_from_the_201_body()
    {
        var created = Dto("Payments");
        var (client, requests) = Client(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(created, options: Json) });

        var answer = await client.CreateCollectionAsync("token", new CreateCollectionBody("Payments", null), Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        Assert.Equal(created.Id, answer.Value!.Id);
        var seen = Assert.Single(requests);
        Assert.Equal("POST", seen.Method);
        Assert.Contains("\"name\":\"Payments\"", seen.Body);
    }

    [Fact]
    public async Task A_missing_or_foreign_item_is_not_found_with_its_code()
    {
        var (client, _) = Client(_ => Problem(HttpStatusCode.NotFound, "not_found"));

        var answer = await client.GetCollectionTreeAsync("token", Guid.NewGuid(), Ct);

        Assert.Equal(CollectionOutcome.NotFound, answer.Outcome);
        Assert.Equal("not_found", answer.Code);
        Assert.Null(answer.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", CollectionOutcome.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, "conflict", CollectionOutcome.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, "depth_exceeded", CollectionOutcome.Invalid)]
    [InlineData(HttpStatusCode.BadRequest, "validation_failed", CollectionOutcome.Invalid)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "payload_too_large", CollectionOutcome.TooLarge)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", CollectionOutcome.RateLimited)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "other", CollectionOutcome.Refused)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "upstream_unavailable", CollectionOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, null, CollectionOutcome.Unavailable)]
    public async Task Each_status_maps_to_one_outcome_and_keeps_the_code(HttpStatusCode status, string? code, CollectionOutcome expected)
    {
        var (client, _) = Client(_ => Problem(status, code));

        var answer = await client.DeleteCollectionAsync("token", Guid.NewGuid(), Ct);

        Assert.Equal(expected, answer.Outcome);
        Assert.Equal(code, answer.Code);
    }

    [Fact]
    public async Task A_transport_failure_is_unavailable_not_an_exception()
    {
        var (client, _) = Client(_ => throw new HttpRequestException("connection refused"));

        var answer = await client.ListCollectionsAsync("token", 1, 100, Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
        Assert.Null(answer.Value);
    }

    [Fact]
    public async Task A_success_body_the_copies_cannot_read_is_unavailable()
    {
        var (client, _) = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json at all", System.Text.Encoding.UTF8, "application/json") });

        var answer = await client.GetCollectionTreeAsync("token", Guid.NewGuid(), Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
    }

    [Fact]
    public async Task The_caller_cancelling_is_not_swallowed()
    {
        var (client, _) = Client(_ => throw new OperationCanceledException());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListCollectionsAsync("token", 1, 100, cancelled.Token));
    }

    [Fact]
    public async Task A_rename_with_a_version_sends_if_match_and_one_without_sends_none()
    {
        var saved = new SavedRequestDto(
            Guid.NewGuid(), Guid.NewGuid(), null, "Refund", null,
            new RequestDefinition("GET", "https://api.example.test", null, null, null, null), 0, 4, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var (client, requests) = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(saved, options: Json) });
        var body = new UpdateSavedRequestBody("Refund v2", null, saved.Request);

        await client.UpdateRequestAsync("token", saved.Id, body, expectedVersion: 4, Ct);
        await client.UpdateRequestAsync("token", saved.Id, body, expectedVersion: null, Ct);

        Assert.Equal("\"4\"", requests[0].IfMatch);
        Assert.Null(requests[1].IfMatch);
        Assert.Equal("PUT", requests[0].Method);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_for_the_page_to_say()
    {
        var (client, _) = Client(_ => Problem(HttpStatusCode.Conflict, "conflict"));

        var answer = await client.UpdateRequestAsync("token", Guid.NewGuid(), new UpdateSavedRequestBody("x", null, new RequestDefinition("GET", "u", null, null, null, null)), 1, Ct);

        Assert.Equal(CollectionOutcome.Conflict, answer.Outcome);
    }

    [Fact]
    public async Task A_delete_that_succeeds_answers_succeeded_with_no_body()
    {
        var (client, requests) = Client(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var answer = await client.DeleteFolderAsync("token", Guid.NewGuid(), Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        Assert.Equal("DELETE", Assert.Single(requests).Method);
    }

    [Fact]
    public async Task A_move_posts_the_destination_to_the_requests_move_route()
    {
        var saved = new SavedRequestDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Refund", null,
            new RequestDefinition("GET", "u", null, null, null, null), 0, 1, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var (client, requests) = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(saved, options: Json) });
        var folder = Guid.NewGuid();

        await client.MoveRequestAsync("token", saved.Id, new MoveRequestBody(CollectionId: null, FolderId: folder, SortOrder: null), Ct);

        var seen = Assert.Single(requests);
        Assert.Equal($"/api/v1/requests/{saved.Id}/move", seen.Path);
        Assert.Contains(folder.ToString(), seen.Body);
    }
}
