using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PostmanClone.ExecutionService.Api;
using PostmanClone.Sending.Contracts;
using PostmanClone.ExecutionService.History;
using PostmanClone.Sending.Execution;
using PostmanClone.ExecutionService.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.History;

/// <summary>The history endpoints of CONTRACTS §4 and the link from the sender to the history (ADR-0015).</summary>
public sealed class HistoryEndpointTests
{
    [Fact]
    public async Task Execute_returns_a_history_id_and_the_entry_keeps_the_template_and_the_result()
    {
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("reached"));
        var environmentId = environments.Add(workspace, ("baseUrl", target.Address.AbsoluteUri, false));
        var savedRequestId = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);

        var result = await caller.ExecuteResultAsync(
            new { request = HistoryCaller.Get("{{baseUrl}}items"), environmentId, savedRequestId },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        Assert.NotNull(result.HistoryId);
        var historyId = result.HistoryId!.Value;

        var entry = await caller.EntryAsync(historyId, TestContext.Current.CancellationToken);
        Assert.Equal(historyId, entry.Id);
        Assert.Equal(historyId, entry.Result.HistoryId);
        Assert.Equal(savedRequestId, entry.SavedRequestId);
        Assert.Equal(environmentId, entry.EnvironmentId);
        Assert.Equal("{{baseUrl}}items", entry.Request.Url);
        Assert.Equal(200, entry.Result.Response!.StatusCode);

        var page = await caller.ListPageAsync("", TestContext.Current.CancellationToken);
        var summary = Assert.Single(page.Items);
        Assert.Equal(1, page.Total);
        Assert.Equal(historyId, summary.Id);
        Assert.Equal("{{baseUrl}}items", summary.Url);
        Assert.Equal("completed", summary.Outcome);
        Assert.Equal(200, summary.StatusCode);
    }

    [Fact]
    public async Task A_recorder_failure_does_not_fail_the_send_and_the_caller_gets_no_history_id()
    {
        var logs = new CapturingLogs();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("reached"));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address))
        {
            ConfigureServices = services =>
            {
                services.AddScoped<IHistoryRecorder, FailingRecorder>();
                services.AddSingleton<ILoggerProvider>(logs);
            },
        };
        var caller = await HistoryCaller.ForAsync(factory, Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        var result = await caller.ExecuteResultAsync(
            new { request = HistoryCaller.Get($"{target.Address}items") },
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        Assert.Equal("reached", ReadBody(result));
        Assert.Null(result.HistoryId);
        Assert.Contains(logs.Messages, message => message.Contains("could not be recorded", StringComparison.Ordinal) && message.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_list_is_newest_first_pages_and_clamps_the_page_size()
    {
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new ExecutionServiceFactory { ConfigureServices = services => services.AddSingleton<TimeProvider>(clock) };
        foreach (var name in new[] { "one", "two", "three" })
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await HistorySeed.AddAsync(factory, workspace, user, $"https://a.test/{name}", TestContext.Current.CancellationToken);
        }

        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);
        var first = await caller.ListPageAsync("?page=1&pageSize=2", TestContext.Current.CancellationToken);
        var second = await caller.ListPageAsync("?page=2&pageSize=2", TestContext.Current.CancellationToken);
        var clamped = await caller.ListPageAsync("?page=0&pageSize=1000", TestContext.Current.CancellationToken);

        Assert.Equal(3, first.Total);
        Assert.Equal(new[] { "https://a.test/three", "https://a.test/two" }, first.Items.Select(item => item.Url));
        Assert.Equal(new[] { "https://a.test/one" }, second.Items.Select(item => item.Url));
        Assert.Equal(1, clamped.PageNumber);
        Assert.Equal(100, clamped.PageSize);
        Assert.Equal(3, clamped.Items.Count);
    }

    [Fact]
    public async Task The_query_matches_the_url_case_insensitively()
    {
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory();
        await HistorySeed.AddAsync(factory, workspace, user, "https://a.test/Orders/1", TestContext.Current.CancellationToken);
        await HistorySeed.AddAsync(factory, workspace, user, "https://a.test/users", TestContext.Current.CancellationToken);
        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);

        var page = await caller.ListPageAsync("?q=ORDERS", TestContext.Current.CancellationToken);

        var match = Assert.Single(page.Items);
        Assert.Equal("https://a.test/Orders/1", match.Url);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Delete_removes_one_entry_and_clear_removes_only_the_callers_own_entries()
    {
        var workspace = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory();
        var first = await HistorySeed.AddAsync(factory, workspace, owner, "https://a.test/first", TestContext.Current.CancellationToken);
        await HistorySeed.AddAsync(factory, workspace, owner, "https://a.test/second", TestContext.Current.CancellationToken);
        await HistorySeed.AddAsync(factory, workspace, member, "https://a.test/member", TestContext.Current.CancellationToken);
        var ownerCaller = await HistoryCaller.ForAsync(factory, workspace, owner, TestContext.Current.CancellationToken);
        var memberCaller = await HistoryCaller.ForAsync(factory, workspace, member, TestContext.Current.CancellationToken);

        using (var deleted = await ownerCaller.DeleteAsync(first, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var gone = await ownerCaller.GetAsync(first, TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(gone);
        }

        Assert.Equal(1, (await ownerCaller.ListPageAsync("", TestContext.Current.CancellationToken)).Total);

        using (var cleared = await ownerCaller.ClearAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        }

        Assert.Equal(0, (await ownerCaller.ListPageAsync("", TestContext.Current.CancellationToken)).Total);
        Assert.Equal(1, (await memberCaller.ListPageAsync("", TestContext.Current.CancellationToken)).Total);
    }

    [Fact]
    public async Task An_unknown_id_is_not_found_and_an_anonymous_caller_is_refused()
    {
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory();
        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);

        using (var get = await caller.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(get);
        }

        using (var delete = await caller.DeleteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(delete);
        }

        using var anonymous = await HistoryCaller.AnonymousListAsync(factory, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task A_subject_that_is_not_a_user_id_has_no_history_and_is_refused()
    {
        var workspace = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory();
        var caller = await HistoryCaller.ForAsync(factory, workspace, "not-a-user-id", TestContext.Current.CancellationToken);

        using var list = await caller.ListAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Contains("\"code\":\"forbidden\"", await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static string ReadBody(ExecutionResult result) => result.Response!.Body.Content;

    /// <summary>A recorder whose store is down. The sender must still answer (ADR-0015).</summary>
    private sealed class FailingRecorder : IHistoryRecorder
    {
        public async Task<Guid?> RecordAsync(HistoryDraft draft, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("The history store is down.");
        }
    }
}
