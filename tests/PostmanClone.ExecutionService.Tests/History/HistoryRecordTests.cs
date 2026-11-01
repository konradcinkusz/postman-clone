using System.Net;
using System.Text;
using PostmanClone.ExecutionService.Tests.Support;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.History;

/// <summary>
/// Recording a run an agent sent (CONTRACTS A2.5, ADR-0026 Decision 7). Nothing is sent here: the template is checked with the
/// send's rules, the stored result is masked by name, and the body is cut to the history limit.
/// </summary>
public sealed class HistoryRecordTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recorded_agent_run_is_stored_with_source_agent_and_listed_as_such()
    {
        await using var factory = new ExecutionServiceFactory();
        var caller = await HistoryCaller.ForAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Ct);

        using var response = await caller.RecordAsync(Record(source: "agent"), Ct);
        var entry = await HistoryCaller.ReadAsync<PostmanClone.ExecutionService.Api.HistoryEntry>(response);
        var page = await caller.ListPageAsync("", Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("agent", entry.Source);
        Assert.Equal("agent", Assert.Single(page.Items).Source);
    }

    [Fact]
    public async Task A_recorded_run_with_any_other_source_is_refused()
    {
        await using var factory = new ExecutionServiceFactory();
        var caller = await HistoryCaller.ForAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Ct);

        using var response = await caller.RecordAsync(Record(source: "server"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"validation_failed\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_template_with_a_literal_secret_in_an_auth_field_is_refused_as_on_a_send()
    {
        // The literal is a fixture, not a credential.
        await using var factory = new ExecutionServiceFactory();
        var caller = await HistoryCaller.ForAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Ct);
        var body = Record(source: "agent", auth: new { type = "bearer", bearerToken = "literal-secret-value-do-not-store" }); // gitleaks:allow

        using var response = await caller.RecordAsync(body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("secret_literal_not_allowed", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_that_carries_a_credential_is_masked_in_the_stored_headers()
    {
        await using var factory = new ExecutionServiceFactory();
        var caller = await HistoryCaller.ForAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Ct);

        using var response = await caller.RecordAsync(Record(source: "agent", responseHeaders: [new { name = "Set-Cookie", value = "session=abc123" }]), Ct);
        var entry = await HistoryCaller.ReadAsync<PostmanClone.ExecutionService.Api.HistoryEntry>(response);

        var header = Assert.Single(entry.Result.Response!.Headers);
        Assert.Equal("••••••", header.Value);
    }

    [Fact]
    public async Task A_body_over_the_history_limit_is_cut_and_marked_truncated()
    {
        await using var factory = new ExecutionServiceFactory();
        var caller = await HistoryCaller.ForAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Ct);
        var large = new string('a', 100 * 1024);

        using var response = await caller.RecordAsync(Record(source: "agent", bodyText: large), Ct);
        var entry = await HistoryCaller.ReadAsync<PostmanClone.ExecutionService.Api.HistoryEntry>(response);

        // The stored content is cut; SizeBytes keeps the response size, as the history body test pins it.
        Assert.True(entry.Result.Response!.Body.Truncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(entry.Result.Response.Body.Content) <= 64 * 1024);
    }

    [Fact]
    public async Task Recording_needs_a_signed_in_user()
    {
        await using var factory = new ExecutionServiceFactory();
        using var anonymous = await factory.CreateClient().PostAsync("/api/v1/history/records", new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static object Record(string source, object? auth = null, IEnumerable<object>? responseHeaders = null, string bodyText = "reached") => new
    {
        source,
        request = new
        {
            method = "GET",
            url = "https://api.example.test/items",
            @params = Array.Empty<object>(),
            headers = Array.Empty<object>(),
            body = new { type = "none", raw = (string?)null, rawLanguage = (string?)null, fields = Array.Empty<object>() },
            auth = auth ?? new { type = "none" },
        },
        environmentId = (Guid?)null,
        savedRequestId = (Guid?)null,
        result = new
        {
            outcome = "completed",
            errorCode = (string?)null,
            message = (string?)null,
            response = new
            {
                statusCode = 200,
                reasonPhrase = "OK",
                httpVersion = "1.1",
                headers = responseHeaders ?? [],
                body = new { encoding = "utf8", content = bodyText, sizeBytes = Encoding.UTF8.GetByteCount(bodyText), truncated = false, contentType = "text/plain" },
            },
            timing = new { totalMs = 1, dnsMs = 0, connectMs = 0, ttfbMs = 0, downloadMs = 0 },
            finalUrl = "https://api.example.test/items",
            redirects = Array.Empty<object>(),
            unresolvedVariables = Array.Empty<string>(),
            historyId = (Guid?)null,
        },
    };
}
