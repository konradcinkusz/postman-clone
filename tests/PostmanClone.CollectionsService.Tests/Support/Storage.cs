using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>
/// The two providers the endpoint suites run on. The InMemory run always executes; the PostgreSQL run executes the same
/// assertions against a real database when <c>TEST_POSTGRES</c> is set, and reports itself as skipped otherwise.
/// </summary>
public static class Storage
{
    public const string InMemory = "InMemory";

    public const string PostgreSql = "PostgreSQL";

    /// <summary>The theory data: every test runs once per provider.</summary>
    public static IEnumerable<object[]> Providers =>
    [
        [InMemory],
        [PostgreSql],
    ];

    /// <summary>
    /// Starts the service on the named provider. A PostgreSQL run without <c>TEST_POSTGRES</c> is skipped here, so the
    /// test reports the reason instead of passing without having run.
    /// </summary>
    public static CollectionsFactory Start(TestTokens tokens, string storage, PostgresFixture postgres, TimeProvider? clock = null, string? environment = null)
    {
        if (storage == PostgreSql && !postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL run needs a server.");
        }

        return new CollectionsFactory(tokens, storage == PostgreSql ? postgres.ConnectionString : null, clock, environment);
    }

    /// <summary>Adds collections directly, without the API, so a test can reach a limit without a hundred requests.</summary>
    public static async Task AddCollectionsAsync(CollectionsFactory factory, Guid workspace, int count, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < count; index++)
        {
            db.Collections.Add(new Collection
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace,
                Name = $"Seeded {index:D4}",
                CreatedBy = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds top-level folders to a collection, without the API, to reach the per-collection folder limit.</summary>
    public static async Task AddFoldersAsync(CollectionsFactory factory, Guid workspace, Guid collection, int count, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < count; index++)
        {
            db.Folders.Add(new Folder
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace,
                CollectionId = collection,
                Name = $"Seeded {index:D4}",
                SortOrder = index,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds a chain of folders, each the child of the one before, and returns their ids from the top down.</summary>
    public static async Task<IReadOnlyList<Guid>> AddFolderChainAsync(
        CollectionsFactory factory,
        Guid workspace,
        Guid collection,
        int length,
        CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>(length);
        Guid? parent = null;
        for (var level = 0; level < length; level++)
        {
            var folder = new Folder
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace,
                CollectionId = collection,
                ParentFolderId = parent,
                Name = $"Level {level + 1}",
                SortOrder = 0,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };
            db.Folders.Add(folder);
            ids.Add(folder.Id);
            parent = folder.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    /// <summary>Adds saved requests to a collection without the API, to reach the per-collection request limit.</summary>
    public static async Task AddRequestsAsync(CollectionsFactory factory, Guid workspace, Guid collection, int count, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < count; index++)
        {
            db.SavedRequests.Add(new SavedRequest
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace,
                CollectionId = collection,
                Name = $"Seeded {index:D4}",
                Method = "GET",
                Definition = System.Text.Json.JsonDocument.Parse("{\"method\":\"GET\",\"url\":\"https://example.invalid/items\"}"),
                SortOrder = index,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds one saved request to a collection (and optionally a folder), in the document shape the endpoints store.</summary>
    public static async Task AddRequestAsync(CollectionsFactory factory, Guid workspace, Guid collection, Guid? folder, string method, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.SavedRequests.Add(new SavedRequest
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace,
            CollectionId = collection,
            FolderId = folder,
            Name = $"{method} request",
            Method = method,
            Definition = System.Text.Json.JsonDocument.Parse($$"""{"method":"{{method}}","url":"https://example.invalid/items"}"""),
            CreatedBy = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
