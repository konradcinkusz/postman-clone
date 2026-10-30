using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.EnvironmentsService.Data;
using PostmanClone.EnvironmentsService.Secrets;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.TestKit;
using Xunit;
using Environment = PostmanClone.EnvironmentsService.Domain.Environment;
using Variable = PostmanClone.EnvironmentsService.Domain.Variable;

namespace PostmanClone.EnvironmentsService.Tests.Data;

/// <summary>The schema, the migrations, and the rule that a stored secret is ciphertext only (ADR-0006).</summary>
public sealed class EnvironmentsSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public void The_model_has_no_pending_changes_against_the_migration_snapshot()
    {
        using var context = new EnvironmentsDbContext(new DbContextOptionsBuilder<EnvironmentsDbContext>()
            .UseNpgsql("Host=localhost;Database=snapshot-check")
            .Options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task PostgreSQL_schema_is_applied_by_MigrateAsync_after_the_host_starts()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL migration test needs a server.");
        }

        await using var factory = new EnvironmentsServiceFactory(new TestTokens(), new Dictionary<string, string?>
        {
            [DatabaseExtensions.ProviderSetting] = DatabaseExtensions.PostgreSqlProvider,
            ["ConnectionStrings:environmentsdb"] = postgres.ConnectionString,
        });
        await factory.WaitForSchemaAsync(TestContext.Current.CancellationToken);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EnvironmentsDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(applied);
        Assert.Equal(context.Database.GetMigrations().Count(), applied.Count());
    }

    [Fact]
    public async Task A_stored_secret_is_a_ciphertext_and_its_plaintext_never_reaches_the_row()
    {
        // The plaintext is an obviously fake fixture, not a credential.
        const string plaintext = "not-a-real-secret-value";
        var protector = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeySizeInBytes));
        var workspace = Guid.NewGuid();
        var environment = Guid.NewGuid();
        var variable = Guid.NewGuid();
        var place = $"{workspace}|{environment}|API_TOKEN";

        var options = new DbContextOptionsBuilder<EnvironmentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using (var writer = new EnvironmentsDbContext(options))
        {
            writer.Environments.Add(new Environment
            {
                Id = environment,
                WorkspaceId = workspace,
                Name = "dev",
                CreatedBy = "user-1",
                Variables =
                [
                    new Variable
                    {
                        Id = variable,
                        WorkspaceId = workspace,
                        EnvironmentId = environment,
                        Key = "API_TOKEN",
                        Value = null,
                        Ciphertext = protector.Protect(plaintext, place),
                        IsSecret = true,
                    },
                ],
            });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = new EnvironmentsDbContext(options);
        var stored = await reader.Variables.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Null(stored.Value);
        Assert.NotNull(stored.Ciphertext);
        Assert.DoesNotContain(plaintext, stored.Ciphertext, StringComparison.Ordinal);
        Assert.Equal(plaintext, protector.Unprotect(stored.Ciphertext, place));
    }
}
