using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Data.Configurations;

public sealed class SavedRequestConfiguration : IEntityTypeConfiguration<SavedRequest>
{
    public void Configure(EntityTypeBuilder<SavedRequest> builder)
    {
        builder.ToTable("SavedRequests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Name).HasMaxLength(200).IsRequired();
        builder.Property(request => request.Description).HasMaxLength(500);
        builder.Property(request => request.Method).HasMaxLength(10).IsRequired();

        // The RequestDefinition document lives in a jsonb column (CONTRACTS §1). It is stored as text through a converter,
        // because the InMemory provider (tests) cannot map JsonDocument; PostgreSQL reads and writes the text as jsonb.
        builder.Property(request => request.Definition)
            .HasConversion(document => document.RootElement.GetRawText(), json => JsonDocument.Parse(json, default))
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(request => request.Version).IsConcurrencyToken();

        builder.HasOne<Collection>()
            .WithMany()
            .HasForeignKey(request => request.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(request => request.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(request => new { request.WorkspaceId, request.CollectionId, request.FolderId });
    }
}
