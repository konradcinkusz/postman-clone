using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.Tests.Support;
using PostmanClone.TestKit;
using PostmanClone.ServiceDefaults.Database;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Data;

/// <summary>The schema is applied by migrations after the host listens (P4). InMemory uses EnsureCreated; PostgreSQL uses MigrateAsync.</summary>
public sealed class SchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task InMemory_schema_is_created_and_a_history_row_can_be_written()
    {
        using var factory = new ExecutionServiceFactory();
        using var client = await ServiceClient.ReadyAsync(factory, TestContext.Current.CancellationToken);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();

        // The InMemory database is named after the connection, so it is shared by every factory in this process. The
        // count is therefore of this test's own workspace, not of the whole table.
        var row = Entry();
        context.HistoryEntries.Add(row);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await context.HistoryEntries.CountAsync(entry => entry.WorkspaceId == row.WorkspaceId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostgreSQL_schema_is_applied_by_MigrateAsync_after_the_host_starts()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL migration test needs a server.");
        }

        using var factory = new ExecutionServiceFactory("Development", new Dictionary<string, string>
        {
            ["DATABASE_PROVIDER"] = "PostgreSQL",
            ["ConnectionStrings:executiondb"] = postgres.ConnectionString,
        });
        using var client = await ServiceClient.ReadyAsync(factory, TestContext.Current.CancellationToken);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();

        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(context.Database.GetMigrations().Count(), applied.Count());
        Assert.Equal(0, await context.HistoryEntries.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_model_has_no_pending_changes_against_the_migration_snapshot()
    {
        using var context = new ExecutionDbContext(
            new DbContextOptionsBuilder<ExecutionDbContext>().UseNpgsql("Host=localhost;Database=snapshot-check").Options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static HistoryEntryRow Entry() => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        ExecutedAt = DateTimeOffset.UtcNow,
        Method = "GET",
        Url = "https://api.example.test/items",
        StatusCode = 200,
        Outcome = "completed",
        TotalMs = 12,
        SizeBytes = 34,
        RequestJson = "{}",
        ResultJson = "{}",
    };
}
