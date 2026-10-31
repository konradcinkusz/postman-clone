using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Tests.Support;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Execution;

/// <summary>What a request looks like on the wire, and what comes back: status, body, headers, size and cap (CONTRACTS §4).</summary>
public sealed class ExecuteResponseTests
{
    private const int FiveMegabytes = 5 * 1024 * 1024;

    [Fact]
    public async Task A_200_JSON_answer_is_completed_with_its_body_and_the_target_sees_the_request()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("{\"ok\":true}");
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}items?x=1"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        // WP15 stores the entry, so the send now names it (ADR-0014: the null recorder returned no id until then).
        Assert.NotNull(result.HistoryId);
        Assert.Empty(result.UnresolvedVariables);
        Assert.Equal($"{target.Address}items?x=1", result.FinalUrl);
        Assert.NotNull(result.Response);
        Assert.Equal(200, result.Response.StatusCode);
        Assert.Equal("utf8", result.Response.Body.Encoding);
        Assert.Equal("{\"ok\":true}", result.Response.Body.Content);
        Assert.Equal(11, result.Response.Body.SizeBytes);
        Assert.False(result.Response.Body.Truncated);
        Assert.Equal("application/json", result.Response.Body.ContentType);
        Assert.True(result.Timing.TotalMs >= result.Timing.TtfbMs);
        var received = Assert.Single(target.Requests);
        Assert.Equal("GET", received.Method);
        Assert.Equal("/items?x=1", received.PathAndQuery);
    }

    [Fact]
    public async Task A_404_answer_is_still_a_completed_send_with_status_404()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.StatusCode = 404;
            return context.Response.WriteAsync("missing");
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}gone"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        Assert.Equal(404, result.Response!.StatusCode);
        Assert.Equal("missing", result.Response.Body.Content);
    }

    [Fact]
    public async Task A_HEAD_request_is_sent_as_HEAD_and_its_answer_has_no_body()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.ContentLength = 100;
            return Task.CompletedTask;
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("HEAD", $"{target.Address}"), TestContext.Current.CancellationToken);

        Assert.Equal("HEAD", Assert.Single(target.Requests).Method);
        Assert.Equal(200, result.Response!.StatusCode);
        Assert.Equal(0, result.Response.Body.SizeBytes);
        Assert.Equal(string.Empty, result.Response.Body.Content);
    }

    [Fact]
    public async Task Binary_answers_are_sent_as_base64()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.ContentType = "application/octet-stream";
            return context.Response.Body.WriteAsync(new byte[] { 0x00, 0x01, 0x02, 0xFF }, 0, 4);
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}blob"), TestContext.Current.CancellationToken);

        Assert.Equal("base64", result.Response!.Body.Encoding);
        Assert.Equal("AAEC/w==", result.Response.Body.Content);
        Assert.Equal(4, result.Response.Body.SizeBytes);
    }

    [Fact]
    public async Task A_body_past_the_five_megabyte_cap_is_truncated_and_kept_at_the_cap()
    {
        const int Generated = 50 * 1024 * 1024;
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(async context =>
        {
            context.Response.ContentType = "text/plain";
            var chunk = Encoding.ASCII.GetBytes(new string('a', 64 * 1024));
            try
            {
                for (var sent = 0; sent < Generated; sent += chunk.Length)
                {
                    await context.Response.Body.WriteAsync(chunk, context.RequestAborted);
                }
            }
            catch (Exception failure) when (failure is OperationCanceledException or IOException)
            {
                // The client stopped reading at the cap and closed the connection; that is the behaviour under test.
            }
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}big"), TestContext.Current.CancellationToken);

        Assert.Equal(FiveMegabytes, result.Response!.Body.SizeBytes);
        Assert.Equal(FiveMegabytes, result.Response.Body.Content.Length);
        Assert.True(result.Response.Body.Truncated);
    }

    [Fact]
    public async Task A_declared_length_past_the_refusal_limit_is_not_read_and_is_too_large()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.ContentLength = 2048;
            return context.Response.Body.WriteAsync(new byte[2048], 0, 2048);
        });
        var settings = SenderSettings.Loopback(environments.Server.Address);
        settings["Execution:RefuseDeclaredLengthBytes"] = "1024";
        await using var factory = new ExecutionServiceFactory(overrides: settings);
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}declared"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.TooLarge, result.Outcome);
        Assert.Equal(0, result.Response!.Body.SizeBytes);
        Assert.True(result.Response.Body.Truncated);
    }

    [Fact]
    public async Task A_gzip_bomb_of_one_gigabyte_is_stopped_at_the_cap_on_the_decoded_bytes()
    {
        const long DecodedBytes = 1024L * 1024 * 1024;
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(async context =>
        {
            context.Response.ContentType = "text/plain";
            context.Response.Headers["Content-Encoding"] = "gzip";
            var chunk = Encoding.ASCII.GetBytes(new string('a', 1024 * 1024));
            try
            {
                await using var gzip = new System.IO.Compression.GZipStream(context.Response.Body, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true);
                for (long sent = 0; sent < DecodedBytes; sent += chunk.Length)
                {
                    await gzip.WriteAsync(chunk, context.RequestAborted);
                }
            }
            catch (Exception failure) when (failure is OperationCanceledException or IOException)
            {
                // The client stopped at the cap.
            }
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}bomb"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        Assert.Equal(FiveMegabytes, result.Response!.Body.SizeBytes);
        Assert.True(result.Response.Body.Truncated);
    }

    [Fact]
    public async Task A_Set_Cookie_is_masked_and_no_cookie_is_sent_on_the_next_call()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.Headers["Set-Cookie"] = "session=abc123";
            return context.Response.WriteAsync("ok");
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var first = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}one"), TestContext.Current.CancellationToken);
        var second = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}two"), TestContext.Current.CancellationToken);

        var setCookie = first.Response!.Headers.Single(header => header.Name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("••••••", setCookie.Value);
        Assert.Null(target.Requests[1].Header("Cookie"));
        Assert.Equal(ExecutionOutcome.Completed, second.Outcome);
    }

    [Fact]
    public async Task Assembled_headers_arrive_at_the_target_and_a_disabled_row_does_not()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("ok"));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);
        // A row with no "enabled" field is enabled (CONTRACTS §1); an explicit false is not sent.
        var headers = new object[]
        {
            Requests.Row("X-Trace", "abc-123"),
            new { key = "X-Off", value = "1", enabled = false },
        };

        await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}headers", headers: headers), TestContext.Current.CancellationToken);

        var received = Assert.Single(target.Requests);
        Assert.Equal("abc-123", received.Header("X-Trace"));
        Assert.Null(received.Header("X-Off"));
    }

    [Fact]
    public async Task A_header_the_sender_owns_is_refused_before_anything_is_sent()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("ok"));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        using var response = await sender.PostAsync(
            Requests.Execute("GET", $"{target.Address}forbidden", headers: new[] { Requests.Row("Host", "evil.example") }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"code\":\"validation_failed\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task A_raw_body_over_one_megabyte_is_refused_with_413()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("ok"));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);
        var raw = new string('a', 1_048_577);

        using var response = await sender.PostAsync(
            Requests.Execute("POST", $"{target.Address}upload", body: Requests.RawBody(raw)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("\"code\":\"payload_too_large\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task Unresolved_variables_are_reported_and_the_request_is_still_sent()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("ok"));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var sender = await Sender.ForAsync(factory, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await sender.ExecuteAsync(Requests.Execute("GET", $"{target.Address}{{{{missing}}}}"), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        Assert.Equal(new[] { "missing" }, result.UnresolvedVariables);
        Assert.Single(target.Requests);
    }

    [Fact]
    public async Task A_request_without_a_bearer_token_is_unauthorized()
    {
        await using var environments = await FakeEnvironments.StartAsync();
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var http = await ServiceClient.ReadyAsync(factory, TestContext.Current.CancellationToken);
        var anonymous = new SenderClient(http, token: null);

        using var response = await anonymous.PostAsync(Requests.Execute("GET", "http://127.0.0.1/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
