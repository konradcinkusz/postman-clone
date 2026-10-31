using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.History;

/// <summary>
/// Retention (CONTRACTS §4): 200 entries per user and workspace, the oldest removed on insert, in the same transaction. The
/// clock is a fake, advanced one second per entry, so "oldest" is decided by the fake time and not by the wall clock.
/// </summary>
public sealed class HistoryRetentionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const int Limit = 200;
    private const int Inserted = Limit + 5;

    [Fact]
    public async Task Retention_keeps_the_newest_200_per_user_and_workspace_after_205_inserts()
    {
        await using var factory = CreateFactory(overrides: null, out var clock);

        await AssertRetentionAsync(factory, clock);
    }

    [Fact]
    public async Task Retention_holds_in_the_same_transaction_on_PostgreSQL()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL retention test needs a server.");
        }

        await using var factory = CreateFactory(
            new Dictionary<string, string>
            {
                ["DATABASE_PROVIDER"] = "PostgreSQL",
                ["ConnectionStrings:executiondb"] = postgres.ConnectionString,
            },
            out var clock);

        await AssertRetentionAsync(factory, clock);
    }

    /// <summary>
    /// Inserts 205 entries for one user, then checks the newest 200 remain and the oldest five are gone. Another user in the
    /// same workspace and the same user in another workspace keep all of theirs.
    /// </summary>
    private static async Task AssertRetentionAsync(ExecutionServiceFactory factory, FakeTimeProvider clock)
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = Guid.NewGuid();
        var otherWorkspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        var neighbour = Guid.NewGuid();

        var ids = new List<Guid>(Inserted);
        for (var index = 0; index < Inserted; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            ids.Add(await HistorySeed.AddAsync(factory, workspace, user, $"https://a.test/{index}", ct));
        }

        for (var index = 0; index < 3; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await HistorySeed.AddAsync(factory, workspace, neighbour, $"https://a.test/neighbour/{index}", ct);
        }

        for (var index = 0; index < 2; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await HistorySeed.AddAsync(factory, otherWorkspace, user, $"https://a.test/elsewhere/{index}", ct);
        }

        var owner = await HistoryCaller.ForAsync(factory, workspace, user, ct);
        var listed = await owner.ListPageAsync("?pageSize=100", ct);
        Assert.Equal(Limit, listed.Total);

        // The five oldest are gone. Each one is a 404 for the owner, and the newest one still reads.
        for (var index = 0; index < Inserted - Limit; index++)
        {
            using var gone = await owner.GetAsync(ids[index], ct);
            await HistoryCaller.AssertNotFoundAsync(gone);
        }

        Assert.Equal(ids[^1], (await owner.EntryAsync(ids[^1], ct)).Id);
        Assert.Equal(ids[Inserted - Limit], (await owner.EntryAsync(ids[Inserted - Limit], ct)).Id);

        var neighbourCaller = await HistoryCaller.ForAsync(factory, workspace, neighbour, ct);
        Assert.Equal(3, (await neighbourCaller.ListPageAsync("", ct)).Total);

        var elsewhere = await HistoryCaller.ForAsync(factory, otherWorkspace, user, ct);
        Assert.Equal(2, (await elsewhere.ListPageAsync("", ct)).Total);

        await AssertStoredCountAsync(factory, workspace, user, Limit, ct);
    }

    /// <summary>The count is read from the table, not from the API, so a trim that only hides rows would fail.</summary>
    private static async Task AssertStoredCountAsync(ExecutionServiceFactory factory, Guid workspace, Guid user, int expected, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        Assert.Equal(expected, await db.HistoryEntries.CountAsync(entry => entry.WorkspaceId == workspace && entry.UserId == user, ct));
    }

    private static ExecutionServiceFactory CreateFactory(IReadOnlyDictionary<string, string>? overrides, out FakeTimeProvider clock)
    {
        var fake = new FakeTimeProvider(DateTimeOffset.UtcNow);
        clock = fake;
        return new ExecutionServiceFactory(overrides: overrides) { ConfigureServices = services => services.AddSingleton<TimeProvider>(fake) };
    }
}
