using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;
using PostmanClone.Web.Tests.History;

namespace PostmanClone.Web.Tests.Api;

/// <summary>
/// The history client against a recording handler (WP22): the routes, the bearer, the paging and filter on the wire, the status
/// to outcome mapping (a foreign entry is <c>not_found</c>), and every failure as an outcome rather than an exception.
/// </summary>
public sealed class HistoryClientTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Seen(string Method, string PathAndQuery, string? Bearer);

    private static IHistoryClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond, List<Seen> seen)
    {
        var services = new ServiceCollection();
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient(BackendServices.ExecutionName, client => client.BaseAddress = new Uri("http://execution-service"))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(request =>
            {
                seen.Add(new Seen(
                    request.Method.Method,
                    request.RequestUri!.PathAndQuery,
                    request.Headers.Authorization?.Parameter));
                return respond(request);
            }));
        return new HistoryClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }

    private static HttpResponseMessage JsonBody(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body, options: Json) };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        new(status) { Content = new StringContent($"{{\"title\":\"x\",\"status\":{(int)status},\"code\":\"{code}\"}}", System.Text.Encoding.UTF8, "application/problem+json") };

    [Fact]
    public async Task A_list_asks_for_the_page_the_size_and_the_filter_with_the_bearer()
    {
        var seen = new List<Seen>();
        var client = Client(_ => JsonBody(HttpStatusCode.OK, new PageResult<HistorySummaryDto>([], 2, 20, 0)), seen);

        var answer = await client.ListAsync("token-value", 2, 500, "items?x=1", Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        var call = Assert.Single(seen);
        Assert.Equal("GET", call.Method);
        Assert.Equal("/api/v1/history?page=2&pageSize=100&q=items%3Fx%3D1", call.PathAndQuery);
        Assert.Equal("token-value", call.Bearer);
    }

    [Fact]
    public async Task A_blank_filter_is_not_sent()
    {
        var seen = new List<Seen>();
        var client = Client(_ => JsonBody(HttpStatusCode.OK, new PageResult<HistorySummaryDto>([], 1, 20, 0)), seen);

        await client.ListAsync("t", 1, 20, "   ", Ct);

        Assert.Equal("/api/v1/history?page=1&pageSize=20", Assert.Single(seen).PathAndQuery);
    }

    [Fact]
    public async Task A_missing_entry_is_not_found_with_its_code()
    {
        var seen = new List<Seen>();
        var client = Client(_ => Problem(HttpStatusCode.NotFound, "not_found"), seen);

        var answer = await client.GetAsync("t", Guid.NewGuid(), Ct);

        Assert.Equal(CollectionOutcome.NotFound, answer.Outcome);
        Assert.Equal("not_found", answer.Code);
        Assert.Null(answer.Value);
    }

    [Fact]
    public async Task A_delete_of_one_entry_is_its_route_and_a_no_content_answer()
    {
        var seen = new List<Seen>();
        var id = Guid.NewGuid();
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NoContent), seen);

        var answer = await client.DeleteAsync("t", id, Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        var call = Assert.Single(seen);
        Assert.Equal("DELETE", call.Method);
        Assert.Equal($"/api/v1/history/{id}", call.PathAndQuery);
    }

    [Fact]
    public async Task Clear_is_the_collection_route()
    {
        var seen = new List<Seen>();
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NoContent), seen);

        var answer = await client.ClearAsync("t", Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        Assert.Equal("/api/v1/history", Assert.Single(seen).PathAndQuery);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", CollectionOutcome.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", CollectionOutcome.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, "internal", CollectionOutcome.Unavailable)]
    public async Task A_refused_call_maps_its_status_to_an_outcome(HttpStatusCode status, string code, CollectionOutcome expected)
    {
        var client = Client(_ => Problem(status, code), []);

        var answer = await client.DeleteAsync("t", Guid.NewGuid(), Ct);

        Assert.Equal(expected, answer.Outcome);
        Assert.Equal(code, answer.Code);
    }

    [Fact]
    public async Task A_connection_failure_is_unavailable_and_not_an_exception()
    {
        var client = Client(_ => throw new HttpRequestException("refused"), []);

        var answer = await client.ListAsync("t", 1, 20, null, Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
        Assert.Null(answer.Value);
    }

    [Fact]
    public async Task A_body_that_is_not_the_contract_is_unavailable_not_a_list()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[1,2,3]", System.Text.Encoding.UTF8, "application/json") }, []);

        var answer = await client.ListAsync("t", 1, 20, null, Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
    }

    [Fact]
    public async Task Cancelling_the_call_stops_it_and_is_not_reported_as_an_outcome()
    {
        var client = Client(_ => throw new TaskCanceledException(), []);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListAsync("t", 1, 20, null, cancelled.Token));
    }
}
