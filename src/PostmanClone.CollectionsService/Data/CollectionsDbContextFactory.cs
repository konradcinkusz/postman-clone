using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PostmanClone.CollectionsService.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build the context for migrations without starting the host or connecting to a server
/// (the model is all it needs). The connection string is never used to reach a database.
/// </summary>
public sealed class CollectionsDbContextFactory : IDesignTimeDbContextFactory<CollectionsDbContext>
{
    public CollectionsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CollectionsDbContext>()
            .UseNpgsql("Host=localhost;Database=collections-design-time")
            .Options);
}
