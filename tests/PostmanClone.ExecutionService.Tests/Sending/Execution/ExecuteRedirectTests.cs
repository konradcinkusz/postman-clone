using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Sending.Ssrf;
using PostmanClone.ExecutionService.Tests.Sending.Ssrf;
using PostmanClone.ExecutionService.Tests.Support;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Execution;

/// <summary>Redirects are followed by the sender, one checked hop at a time (ADR-0011, CONTRACTS §4).</summary>
public sealed class ExecuteRedirectTests
{
    [Fact]
    public async Task A_302_then_200_is_followed_and_the_chain_is_reported()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path == "/start")
            {
                context.Response.StatusCode = 302;
                context.Response.Headers.Location = "/end";
                return Task.CompletedTask;
            }

            return context.Response.WriteAsync("done");
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}start"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        Assert.Equal(200, result.Response!.StatusCode);
        Assert.Equal("done", result.Response.Body.Content);
        Assert.Equal("/end", Assert.Single(result.Redirects).Location);
        Assert.Equal(new[] { "/start", "/end" }, target.Requests.Select(request => request.PathAndQuery));
        Assert.EndsWith("/end", result.FinalUrl);
    }

    [Fact]
    public async Task A_303_turns_a_POST_into_a_GET_and_drops_the_body()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path == "/submit")
            {
                context.Response.StatusCode = 303;
                context.Response.Headers.Location = "/result";
                return Task.CompletedTask;
            }

            return context.Response.WriteAsync("result");
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(
            Requests.Execute("POST", $"{target.Address}submit", body: Requests.RawBody("payload")),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, result.Response!.StatusCode);
        Assert.Equal(new[] { "POST", "GET" }, target.Requests.Select(request => request.Method));
        Assert.Null(target.Requests[1].Header("Content-Type"));
    }

    [Fact]
    public async Task A_307_keeps_the_method_and_the_body_on_the_next_hop()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(async context =>
        {
            if (context.Request.Path == "/first")
            {
                context.Response.StatusCode = 307;
                context.Response.Headers.Location = "/second";
                return;
            }

            using var reader = new StreamReader(context.Request.Body);
            await context.Response.WriteAsync($"{context.Request.Method}:{await reader.ReadToEndAsync()}");
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(
            Requests.Execute("POST", $"{target.Address}first", body: Requests.RawBody("payload")),
            TestContext.Current.CancellationToken);

        Assert.Equal("POST:payload", result.Response!.Body.Content);
    }

    [Fact]
    public async Task A_redirect_loop_stops_at_the_limit_with_too_many_redirects()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.StatusCode = 302;
            context.Response.Headers.Location = "/loop";
            return Task.CompletedTask;
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}loop"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);
        Assert.Equal("too_many_redirects", result.ErrorCode);
        // The first request and the five followed hops; the sixth redirect is refused and not sent.
        Assert.Equal(6, target.Requests.Count);
        Assert.Equal(6, result.Redirects.Count);
    }

    [Fact]
    public async Task With_followRedirects_false_the_redirect_answer_is_the_result()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.StatusCode = 302;
            context.Response.Headers.Location = "/elsewhere";
            return Task.CompletedTask;
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(
            Requests.Execute("GET", $"{target.Address}start", followRedirects: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(302, result.Response!.StatusCode);
        Assert.Empty(result.Redirects);
        Assert.Single(target.Requests);
    }

    [Fact]
    public async Task A_redirect_to_loopback_is_blocked_when_the_flag_is_off()
    {
        // The first hop names a public address, so the guard lets it through: the test's dialer then reaches the loopback
        // target, which is how a name that answers differently on the next lookup is simulated. The redirect to the loopback
        // address is checked when it is planned, with the flag off, and refused without a second connection.
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path == "/start")
            {
                context.Response.StatusCode = 302;
                context.Response.Headers.Location = $"http://127.0.0.1:{context.Connection.LocalPort}/next";
                return Task.CompletedTask;
            }

            return context.Response.WriteAsync("reached");
        });
        var resolver = new ScriptedResolver(ScriptedResolver.Answer("93.184.216.34"));
        var dialer = new DialingLoopback(target.Port);
        await using var factory = new ExecutionServiceFactory
        {
            ConfigureServices = services =>
            {
                services.AddSingleton<IHostResolver>(resolver);
                services.AddSingleton<IAddressDialer>(dialer);
            },
        };
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", "http://public.example.test/start"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);
        Assert.Equal("ssrf_blocked_address", result.ErrorCode);
        // The redirect planner reports the code only, so the message names no category; it must still name no address.
        Assert.StartsWith("The destination address is not allowed", result.Message);
        Assert.DoesNotContain("127.0.0.1", result.Message);
        Assert.Equal(302, Assert.Single(result.Redirects).StatusCode);
        Assert.Equal(new[] { "/start" }, target.Requests.Select(request => request.PathAndQuery));
    }

    /// <summary>Connects to the loopback target whatever endpoint the guard validated. Only the redirect test uses it.</summary>
    private sealed class DialingLoopback(int port) : IAddressDialer
    {
        public Task<Stream> ConnectAsync(System.Net.IPEndPoint endpoint, CancellationToken cancellationToken) =>
            new SocketAddressDialer().ConnectAsync(new System.Net.IPEndPoint(IPAddress.Loopback, port), cancellationToken);
    }
}
