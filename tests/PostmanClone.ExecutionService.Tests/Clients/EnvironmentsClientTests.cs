using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PostmanClone.ExecutionService.Clients;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Clients;

/// <summary>The EnvironmentsService client: the caller's bearer goes out, every failure mode maps to one answer.</summary>
public sealed class EnvironmentsClientTests
{
    private const string Token = "caller-access-token";

    private static readonly Guid EnvironmentId = Guid.NewGuid();

    [Fact]
    public async Task A_200_answer_is_read_and_the_callers_bearer_is_forwarded()
    {
        var body = $$"""
            {"id":"{{EnvironmentId}}","variables":[{"key":"baseUrl","value":"https://api.example.test","isSecret":false}]}
            """;
        var handler = new FakeHttpMessageHandler(_ => Json(HttpStatusCode.OK, body));
        using var http = Client(handler);
        var client = new EnvironmentsClient(http, NullLogger<EnvironmentsClient>.Instance);

        var resolved = await client.ResolveAsync(EnvironmentId, Token, TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(EnvironmentId, resolved.Id);
        var variable = Assert.Single(resolved.Variables);
        Assert.Equal(("baseUrl", "https://api.example.test", false), (variable.Key, variable.Value, variable.IsSecret));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/api/v1/environments/{EnvironmentId}/resolve", request.RequestUri!.AbsolutePath);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", Token), request.Headers.Authorization);
    }

    [Fact]
    public async Task A_404_answer_is_null_so_the_caller_can_say_not_found()
    {
        var handler = new FakeHttpMessageHandler(_ => Json(HttpStatusCode.NotFound, """{"code":"not_found"}"""));
        using var http = Client(handler);
        var client = new EnvironmentsClient(http, NullLogger<EnvironmentsClient>.Instance);

        var resolved = await client.ResolveAsync(EnvironmentId, Token, TestContext.Current.CancellationToken);

        Assert.Null(resolved);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Found)]
    public async Task Any_other_status_is_an_upstream_failure(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(status));
        using var http = Client(handler);
        var client = new EnvironmentsClient(http, NullLogger<EnvironmentsClient>.Instance);

        var exception = await Assert.ThrowsAsync<UpstreamUnavailableException>(
            () => client.ResolveAsync(EnvironmentId, Token, TestContext.Current.CancellationToken));

        Assert.Contains(((int)status).ToString(), exception.Message);
    }

    [Fact]
    public async Task A_body_that_does_not_match_the_contract_is_an_upstream_failure()
    {
        var handler = new FakeHttpMessageHandler(_ => Json(HttpStatusCode.OK, "{ not json"));
        using var http = Client(handler);
        var client = new EnvironmentsClient(http, NullLogger<EnvironmentsClient>.Instance);

        await Assert.ThrowsAsync<UpstreamUnavailableException>(
            () => client.ResolveAsync(EnvironmentId, Token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_timeout_is_an_upstream_failure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("Simulated HttpClient timeout."));
        using var http = Client(handler);
        var client = new EnvironmentsClient(http, NullLogger<EnvironmentsClient>.Instance);

        var exception = await Assert.ThrowsAsync<UpstreamUnavailableException>(
            () => client.ResolveAsync(EnvironmentId, Token, TestContext.Current.CancellationToken));

        Assert.Contains("in time", exception.Message);
    }

    [Fact]
    public async Task A_connection_failure_is_an_upstream_failure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Simulated connection refusal."));
        using var http = Client(handler);
        var client = new EnvironmentsClient(http, NullLogger<EnvironmentsClient>.Instance);

        await Assert.ThrowsAsync<UpstreamUnavailableException>(
            () => client.ResolveAsync(EnvironmentId, Token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_mistaken_for_an_upstream_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("Cancelled by the caller."));
        using var http = Client(handler);
        var client = new EnvironmentsClient(http, NullLogger<EnvironmentsClient>.Instance);

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.ResolveAsync(EnvironmentId, Token, cancelled.Token));
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_is_an_upstream_failure()
    {
        // A real loopback server: the redirect target counts the requests that reach it.
        var reachedTarget = 0;
        await using var upstream = await StartAsync(app =>
        {
            app.MapPost("/api/v1/environments/{id}/resolve", () => Results.Redirect("/elsewhere"));
            app.MapGet("/elsewhere", () =>
            {
                Interlocked.Increment(ref reachedTarget);
                return Results.Ok();
            });
        });

        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Environments:BaseAddress"] = upstream.Urls.First();
        builder.Services.AddEnvironmentsClient(builder.Configuration);
        await using var app = builder.Build();
        var client = app.Services.GetRequiredService<EnvironmentsClient>();

        await Assert.ThrowsAsync<UpstreamUnavailableException>(
            () => client.ResolveAsync(EnvironmentId, Token, TestContext.Current.CancellationToken));
        Assert.Equal(0, Volatile.Read(ref reachedTarget));
    }

    private static HttpClient Client(FakeHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://environments-service") };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static async Task<WebApplication> StartAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
