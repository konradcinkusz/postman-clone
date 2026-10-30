using Microsoft.EntityFrameworkCore;
using Environment = PostmanClone.EnvironmentsService.Domain.Environment;
using PostmanClone.EnvironmentsService.Domain;

namespace PostmanClone.EnvironmentsService.Data;

/// <summary>The EnvironmentsService schema in <c>environmentsdb</c> (P4). Migrations live in <c>Migrations/</c>.</summary>
public sealed class EnvironmentsDbContext(DbContextOptions<EnvironmentsDbContext> options) : DbContext(options)
{
    public DbSet<Environment> Environments => Set<Environment>();

    public DbSet<Variable> Variables => Set<Variable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Environment>(entity =>
        {
            entity.HasKey(environment => environment.Id);
            entity.Property(environment => environment.Name).HasMaxLength(100).IsRequired();
            entity.Property(environment => environment.CreatedBy).HasMaxLength(128).IsRequired();
            entity.HasIndex(environment => environment.WorkspaceId);
            entity.HasMany(environment => environment.Variables)
                .WithOne()
                .HasForeignKey(variable => variable.EnvironmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Variable>(entity =>
        {
            entity.HasKey(variable => variable.Id);
            entity.Property(variable => variable.Key).HasMaxLength(100).IsRequired();
            // A plain value is at most 8192 characters (CONTRACTS §3). A secret's envelope has no fixed bound
            // relative to that, so the ciphertext column is unbounded text.
            entity.Property(variable => variable.Value).HasMaxLength(8192);
            entity.Property(variable => variable.Ciphertext).HasColumnType("text");
            entity.HasIndex(variable => variable.WorkspaceId);
            // Keys are unique per environment; the database compares them case-sensitively (CONTRACTS §3).
            entity.HasIndex(variable => new { variable.EnvironmentId, variable.Key }).IsUnique();
        });
    }
}
