using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PostmanClone.ServiceDefaults;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;
using PostmanClone.Web.Response;
using PostmanClone.Web.Tests.Response;

namespace PostmanClone.Web.Tests.Api;

/// <summary>
/// The execute client against a recording handler (WP20): the route, the method, the bearer, the body it sends, the status-to-outcome
/// mapping, a transport failure, and the send's own limits: a POST is never retried, and a send that outlives its timeout is an
/// unavailable answer, not a hang.
/// </summary>
public sealed class ExecutionClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Seen(string Method, string Path, string? Bearer, string Body);

    private sealed class DelayingHandler(TimeSpan delay) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static IExecutionClient Client(
        Func<HttpRequestMessage, HttpResponseMessage>? respond,
        List<Seen>? requests = null,
        TimeSpan? executionTimeout = null,
        HttpMessageHandler? inner = null)
    {
        // The kernel's defaults are applied as in production: the one resilience handler, read per client (ADR-0022).
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddBackendClients(executionTimeout);
        builder.Services.AddHttpClient(BackendServices.ExecutionName).ConfigurePrimaryHttpMessageHandler(() => inner ?? new FakeHttpMessageHandler(request =>
        {
            var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests?.Add(new Seen(request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter, body));
            return respond!(request);
        }));
        return builder.Build().Services.GetRequiredService<IExecutionClient>();
    }

    private static ExecuteBody Body() => new(
        new RequestDefinition("POST", "https://api.example.test/items", [], [], new RequestBody("none", null, null, []), new RequestAuth("none", null, null, null, null, null, null)),
        null,
        Guid.NewGuid(),
        5,
        true);

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        new(status) { Content = JsonContent.Create(new { code, title = "x", status = (int)status }, options: Json) };

    [Fact]
    public async Task A_send_posts_the_body_to_execute_with_the_bearer_and_reads_the_result()
    {
        var requests = new List<Seen>();
        var result = ResponseViewTests.Result(response: ResponseViewTests.ResponseOf(status: 201));
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: Json) }, requests);

        var answer = await client.ExecuteAsync("access-token", Body(), Ct);

        var seen = Assert.Single(requests);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("/api/v1/execute", seen.Path);
        Assert.Equal("access-token", seen.Bearer);
        using var sent = JsonDocument.Parse(seen.Body);
        Assert.Equal("https://api.example.test/items", sent.RootElement.GetProperty("request").GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, sent.RootElement.GetProperty("environmentId").ValueKind);
        Assert.Equal(5, sent.RootElement.GetProperty("timeoutSeconds").GetInt32());

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        Assert.Equal("completed", answer.Value!.Outcome);
        Assert.Equal(201, answer.Value.Response!.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", CollectionOutcome.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, "upstream_unavailable", CollectionOutcome.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "secrets_unavailable", CollectionOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, "no_active_workspace", CollectionOutcome.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest, "secret_literal_not_allowed", CollectionOutcome.Invalid)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "payload_too_large", CollectionOutcome.TooLarge)]
    [InlineData(HttpStatusCode.NotFound, "not_found", CollectionOutcome.NotFound)]
    public async Task Each_refusal_status_maps_to_its_outcome_and_keeps_the_code(HttpStatusCode status, string code, CollectionOutcome expected)
    {
        var client = Client(_ => Problem(status, code));

        var answer = await client.ExecuteAsync("token", Body(), Ct);

        Assert.Equal(expected, answer.Outcome);
        Assert.Equal(code, answer.Code);
        Assert.Null(answer.Value);
    }

    [Fact]
    public async Task A_transport_failure_is_an_unavailable_answer_not_an_exception()
    {
        var client = Client(null, inner: new ThrowingHandler());

        var answer = await client.ExecuteAsync("token", Body(), Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
    }

    [Fact]
    public async Task An_answer_that_is_not_the_contract_is_unavailable()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });

        var answer = await client.ExecuteAsync("token", Body(), Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
    }

    [Fact]
    public async Task A_cancelled_send_is_an_exception_for_the_caller_to_observe_not_an_outcome()
    {
        var client = Client(null, inner: new DelayingHandler(TimeSpan.FromSeconds(30)));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExecuteAsync("token", Body(), cancelled.Token));
    }

    [Fact]
    public async Task A_send_that_fails_on_the_wire_is_not_retried()
    {
        var calls = 0;
        var client = Client(_ =>
        {
            calls++;
            return Problem(HttpStatusCode.ServiceUnavailable, "upstream_unavailable");
        });

        var answer = await client.ExecuteAsync("token", Body(), Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_send_that_outlives_its_timeout_is_an_unavailable_answer_and_does_not_hang()
    {
        var delaying = new DelayingHandler(TimeSpan.FromSeconds(30));
        var client = Client(null, executionTimeout: TimeSpan.FromMilliseconds(200), inner: delaying);
        var started = Stopwatch.GetTimestamp();

        var answer = await client.ExecuteAsync("token", Body(), Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
        Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(20), "The send was not stopped by its timeout.");
    }

    [Fact]
    public void The_execute_client_waits_longer_than_the_sender_deadline_and_is_the_only_long_one()
    {
        // The sender's deadline is 60 s (CONTRACTS §4); the Web must wait longer than that, or it gives up on a send that is working.
        Assert.True(BackendServices.ExecutionTimeout > TimeSpan.FromSeconds(60));
        Assert.Equal("execution", BackendServices.ExecutionName);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
