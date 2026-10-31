using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Tests.Support;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Execution;

/// <summary>
/// The sender's refusals and its secret handling (ADR-0006, ADR-0011, ADR-0012). The default-configuration tests use no
/// loopback flag at all: they prove that a loopback target is refused as shipped.
/// </summary>
public sealed class ExecuteSecurityTests
{
    // A fixture that looks like a secret; the comment tells the secret scanner it is deliberate (AGENTS.md).
    private const string SecretValue = "s3cr3t-token-value"; // gitleaks:allow

    [Fact]
    public async Task Loopback_target_is_blocked_with_the_default_configuration_and_never_reached()
    {
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("reached"));
        await using var factory = new ExecutionServiceFactory();
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}probe"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);
        Assert.Equal("ssrf_blocked_address", result.ErrorCode);
        Assert.Null(result.Response);
        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task The_localhost_name_is_refused_by_name_with_the_default_configuration()
    {
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("reached"));
        await using var factory = new ExecutionServiceFactory();
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(
            Requests.Execute("GET", $"http://localhost:{target.Port}/probe"),
            TestContext.Current.CancellationToken);

        Assert.Equal("ssrf_blocked_address", result.ErrorCode);
        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task A_blocked_port_is_refused_before_any_connection()
    {
        await using var factory = new ExecutionServiceFactory();
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", "http://198.51.100.7:5432/"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);
        Assert.Equal("ssrf_blocked_port", result.ErrorCode);
    }

    [Fact]
    public async Task A_scheme_other_than_http_or_https_is_refused()
    {
        await using var factory = new ExecutionServiceFactory();
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", "ftp://198.51.100.7/file"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Blocked, result.Outcome);
        Assert.Equal("ssrf_blocked_scheme", result.ErrorCode);
    }

    [Fact]
    public async Task A_secret_is_sent_to_the_target_but_is_absent_from_finalUrl_the_result_and_the_logs()
    {
        var logs = new CapturingLogs();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
            context.Response.WriteAsync($"got:{context.Request.Query["token"]}"));
        var workspace = Guid.NewGuid();
        var environmentId = environments.Add(
            workspace,
            ("baseUrl", target.Address.AbsoluteUri, false),
            ("apiToken", SecretValue, true));
        var settings = SenderSettings.Loopback(environments.Server.Address);
        await using var factory = new ExecutionServiceFactory(overrides: settings) { ConfigureServices = SenderSettings.WithLogs(logs) };
        var sender = await Sender.ForAsync(factory, workspace, TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(
            Requests.Execute("GET", "{{baseUrl}}items?token={{apiToken}}", environmentId: environmentId),
            TestContext.Current.CancellationToken);

        // The target received the real value, on the wire, as the user asked.
        Assert.Contains($"token={SecretValue}", Assert.Single(target.Requests).PathAndQuery);
        // Everything the caller can see is masked.
        Assert.Contains("token=••••••", result.FinalUrl);
        Assert.Equal("got:••••••", result.Response!.Body.Content);
        Assert.DoesNotContain(SecretValue, JsonSerializer.Serialize(result));
        Assert.DoesNotContain(logs.Messages, message => message.Contains(SecretValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_secret_literal_in_the_auth_helper_is_refused_and_nothing_is_sent()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("reached"));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);
        var auth = new { type = "bearer", bearerToken = SecretValue };

        using var response = await sender.PostAsync(
            Requests.Execute("GET", $"{target.Address}guarded", auth: auth),
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"code\":\"secret_literal_not_allowed\"", text);
        Assert.DoesNotContain(SecretValue, text);
        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task A_target_slower_than_the_timeout_gives_outcome_timeout_at_the_deadline()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(async context =>
        {
            // Never answers; the client's abort ends the wait.
            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);
        var clock = Stopwatch.StartNew();

        var result = await sender.ExecuteAsync(
            Requests.Execute("GET", $"{target.Address}slow", timeoutSeconds: 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Timeout, result.Outcome);
        Assert.Equal("timeout", result.ErrorCode);
        Assert.Null(result.Response);
        // The deadline is one second. The bound is generous so the test does not depend on machine speed.
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.9, 15);
    }

    [Fact]
    public async Task A_closed_port_on_a_permitted_address_is_a_network_error()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        var closed = await LoopbackServer.StartAsync(context => Task.CompletedTask);
        var port = closed.Port;
        await closed.DisposeAsync();
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"http://127.0.0.1:{port}/"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.NetworkError, result.Outcome);
        Assert.Equal("network_error", result.ErrorCode);
        Assert.DoesNotContain(port.ToString(), result.Message ?? string.Empty);
    }

    [Fact]
    public async Task An_unreachable_EnvironmentsService_is_502_upstream_unavailable()
    {
        var down = await LoopbackServer.StartAsync(context => Task.CompletedTask);
        var address = down.Address;
        await down.DisposeAsync();
        var environmentId = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Defaults(address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        using var response = await sender.PostAsync(
            Requests.Execute("GET", "http://198.51.100.7/", environmentId: environmentId),
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("\"code\":\"upstream_unavailable\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_timeout_outside_the_contract_range_is_a_validation_error()
    {
        await using var factory = new ExecutionServiceFactory();
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        using var response = await sender.PostAsync(
            Requests.Execute("GET", "http://198.51.100.7/", timeoutSeconds: 61),
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"code\":\"validation_failed\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
