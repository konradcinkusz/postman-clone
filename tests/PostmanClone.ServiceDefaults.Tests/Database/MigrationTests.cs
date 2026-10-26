using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Database;

/// <summary>The schema is applied by the hosted migration service after the server listens (P4), never by EnsureCreated outside InMemory.</summary>
public sealed class MigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task InMemory_schema_is_created_after_the_host_starts_and_the_database_is_usable()
    {
        await using var app = await TestHost.StartAsync(builder =>
        {
            builder.Configuration[DatabaseExtensions.ProviderSetting] = DatabaseExtensions.InMemoryProvider;
            builder.AddDatabaseContext<ProbeContext>("probe");
        });

        var signal = app.Services.GetRequiredService<MigrationCompletionSignal>();
        using var timeout = new CancellationTokenSource(WaitLimit);
        await signal.WaitAsync(timeout.Token);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ProbeContext>();
        context.Probes.Add(new ProbeRow { Id = Guid.NewGuid(), Label = "written" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(signal.Succeeded);
        Assert.Equal(1, await context.Probes.CountAsync(TestContext.Current.CancellationToken));
        using var client = TestHost.ClientFor(app);
        using var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task PostgreSQL_schema_is_applied_by_MigrateAsync_after_the_host_starts()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL migration test needs a server.");
        }

        await using var app = await TestHost.StartAsync(builder =>
        {
            builder.Configuration[DatabaseExtensions.ProviderSetting] = DatabaseExtensions.PostgreSqlProvider;
            builder.Configuration["ConnectionStrings:probe"] = postgres.ConnectionString;
            builder.AddDatabaseContext<ProbeContext>("probe");
        });
        var signal = app.Services.GetRequiredService<MigrationCompletionSignal>();
        using var timeout = new CancellationTokenSource(WaitLimit);
        await signal.WaitAsync(timeout.Token);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ProbeContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.True(signal.Succeeded);
        Assert.NotEmpty(applied);
        Assert.Equal(context.Database.GetMigrations().Count(), applied.Count());
        Assert.Equal(0, await context.Probes.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_model_has_no_pending_changes_against_the_migration_snapshot()
    {
        using var context = new ProbeContext(new DbContextOptionsBuilder<ProbeContext>().UseNpgsql("Host=localhost;Database=snapshot-check").Options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task A_failed_migration_is_recorded_on_the_signal_and_background_work_does_not_start()
    {
        var signal = new MigrationCompletionSignal();
        signal.MarkFailed();

        await Assert.ThrowsAsync<InvalidOperationException>(() => signal.WaitAsync(TestContext.Current.CancellationToken));
        Assert.True(signal.Finished);
        Assert.False(signal.Succeeded);
    }

    [Fact]
    public async Task A_completed_migration_releases_the_background_work_that_waits_for_it()
    {
        var signal = new MigrationCompletionSignal();
        signal.MarkSucceeded();

        await signal.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(signal.Succeeded);
    }

    [Fact]
    public async Task Readiness_is_unhealthy_until_the_migration_finishes_and_after_it_fails()
    {
        var signal = new MigrationCompletionSignal();
        var check = new MigrationReadinessHealthCheck(signal);
        var context = new HealthCheckContext();

        var pending = await check.CheckHealthAsync(context, TestContext.Current.CancellationToken);
        signal.MarkFailed();
        var failed = await check.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, pending.Status);
        Assert.Contains("not finished", pending.Description, StringComparison.Ordinal);
        Assert.Equal(HealthStatus.Unhealthy, failed.Status);
        Assert.Contains("failed", failed.Description, StringComparison.Ordinal);
    }
}
