using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Data.Configurations;

public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folders");
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Name).HasMaxLength(100).IsRequired();
        builder.Property(folder => folder.Version).IsConcurrencyToken();

        // Deleting a collection or a folder removes what it holds (CONTRACTS §2: DELETE /folders cascades).
        builder.HasOne<Collection>()
            .WithMany()
            .HasForeignKey(folder => folder.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(folder => folder.ParentFolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(folder => new { folder.WorkspaceId, folder.CollectionId, folder.ParentFolderId });
    }
}
