using Microsoft.EntityFrameworkCore;

namespace PostmanClone.ExecutionService.Data;

/// <summary>The ExecutionService's database, <c>executiondb</c> (P3). The schema is applied by migrations, never by EnsureCreated.</summary>
public sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options)
{
    public DbSet<HistoryEntryRow> HistoryEntries => Set<HistoryEntryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HistoryEntryRow>(entity =>
        {
            entity.ToTable("history_entries");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Method).HasMaxLength(16);
            entity.Property(row => row.Outcome).HasMaxLength(32);
            // Rows written before the column existed were all sent by the server (CONTRACTS A2.5).
            entity.Property(row => row.Source).HasMaxLength(16).HasDefaultValue("server");
            // A history list is read per user and workspace, newest first.
            entity.HasIndex(row => new { row.WorkspaceId, row.UserId, row.ExecutedAt });
        });
    }
}
