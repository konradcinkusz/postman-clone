using Microsoft.EntityFrameworkCore;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Data;

/// <summary>The collectionsdb schema (P4). Its configuration is in <c>Data/Configurations</c>; the schema is migrated, never created.</summary>
public sealed class CollectionsDbContext(DbContextOptions<CollectionsDbContext> options) : DbContext(options)
{
    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<SavedRequest> SavedRequests => Set<SavedRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CollectionsDbContext).Assembly);
    }
}
