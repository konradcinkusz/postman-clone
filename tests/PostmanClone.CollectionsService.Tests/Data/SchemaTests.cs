using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.CollectionsService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Data;

/// <summary>The schema is migrated by the hosted service after the server listens (P4); the model matches the migrations.</summary>
public sealed class SchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public void The_model_has_no_pending_changes_against_the_migration_snapshot()
    {
        // The design-time factory builds the context without a server: the check needs the model only.
        using var context = new CollectionsDbContextFactory().CreateDbContext([]);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task InMemory_host_creates_the_schema_and_stores_a_collection()
    {
        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        var workspace = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        context.Collections.Add(NewCollection(workspace));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await context.Collections.CountAsync(collection => collection.WorkspaceId == workspace, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostgreSQL_host_applies_every_migration_after_it_starts()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL schema test needs a server.");
        }

        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens, postgres.ConnectionString);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(applied);
        Assert.Equal(context.Database.GetMigrations().Count(), applied.Count());
    }

    [Fact]
    public async Task PostgreSQL_saved_request_definition_is_stored_as_jsonb_and_reads_back()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the jsonb round trip needs a server.");
        }

        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens, postgres.ConnectionString);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        var workspace = Guid.NewGuid();
        var request = new SavedRequest
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace,
            CollectionId = Guid.NewGuid(),
            Name = "List items",
            Method = "GET",
            Definition = JsonDocument.Parse("""{"method":"GET","url":"https://example.invalid/items"}"""),
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        using (var writer = factory.Services.CreateScope())
        {
            var context = writer.ServiceProvider.GetRequiredService<CollectionsDbContext>();
            context.Collections.Add(NewCollection(workspace, request.CollectionId));
            context.SavedRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var reader = factory.Services.CreateScope();
        var stored = await reader.ServiceProvider.GetRequiredService<CollectionsDbContext>().SavedRequests
            .AsNoTracking()
            .SingleAsync(row => row.Id == request.Id, TestContext.Current.CancellationToken);

        Assert.Equal("https://example.invalid/items", stored.Definition.RootElement.GetProperty("url").GetString());
        Assert.Equal("GET", stored.Method);
    }

    [Fact]
    public async Task PostgreSQL_deleting_a_collection_removes_its_folders_and_requests()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the cascade test needs a server.");
        }

        using var tokens = new TestTokens();
        await using var factory = new CollectionsFactory(tokens, postgres.ConnectionString);
        await SchemaWait.AppliedAsync(factory, TestContext.Current.CancellationToken);
        var workspace = Guid.NewGuid();
        var collection = NewCollection(workspace);
        var folder = new Folder { Id = Guid.NewGuid(), WorkspaceId = workspace, CollectionId = collection.Id, Name = "Auth", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var request = new SavedRequest
        {
            Id = Guid.NewGuid(), WorkspaceId = workspace, CollectionId = collection.Id, FolderId = folder.Id, Name = "Login", Method = "POST",
            Definition = JsonDocument.Parse("{}"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        using (var writer = factory.Services.CreateScope())
        {
            var context = writer.ServiceProvider.GetRequiredService<CollectionsDbContext>();
            context.Collections.Add(collection);
            context.Folders.Add(folder);
            context.SavedRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var deleter = factory.Services.CreateScope())
        {
            await deleter.ServiceProvider.GetRequiredService<CollectionsDbContext>().Collections
                .Where(row => row.Id == collection.Id)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        using var reader = factory.Services.CreateScope();
        var context2 = reader.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        Assert.Equal(0, await context2.Folders.CountAsync(row => row.CollectionId == collection.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, await context2.SavedRequests.CountAsync(row => row.CollectionId == collection.Id, TestContext.Current.CancellationToken));
    }

    private static Collection NewCollection(Guid workspace, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        WorkspaceId = workspace,
        Name = "Payments",
        CreatedBy = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
