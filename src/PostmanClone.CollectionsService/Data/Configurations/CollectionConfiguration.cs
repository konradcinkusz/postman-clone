using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Data.Configurations;

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");
        builder.HasKey(collection => collection.Id);
        builder.Property(collection => collection.Name).HasMaxLength(100).IsRequired();
        builder.Property(collection => collection.Description).HasMaxLength(500);
        builder.Property(collection => collection.Version).IsConcurrencyToken();

        // Every query filters on the workspace first (CONTRACTS §0); the list order is by name within it.
        builder.HasIndex(collection => new { collection.WorkspaceId, collection.Name });
    }
}
