using Microsoft.EntityFrameworkCore;
using Environment = PostmanClone.EnvironmentsService.Domain.Environment;
using PostmanClone.EnvironmentsService.Domain;

namespace PostmanClone.EnvironmentsService.Data;

/// <summary>The EnvironmentsService schema in <c>environmentsdb</c> (P4). Migrations live in <c>Migrations/</c>.</summary>
public sealed class EnvironmentsDbContext(DbContextOptions<EnvironmentsDbContext> options) : DbContext(options)
{
    public DbSet<Environment> Environments => Set<Environment>();

    public DbSet<Variable> Variables => Set<Variable>();

    /// <summary>Pairing codes, hashed, single-use (CONTRACTS A2.2).</summary>
    public DbSet<AgentPairingCode> AgentPairingCodes => Set<AgentPairingCode>();

    /// <summary>Paired agents, with the hash of each agent token (CONTRACTS A2.2).</summary>
    public DbSet<AgentInstallation> AgentInstallations => Set<AgentInstallation>();

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

        modelBuilder.Entity<AgentPairingCode>(entity =>
        {
            entity.ToTable("agent_pairing_codes");
            entity.HasKey(code => code.Id);
            entity.Property(code => code.UserId).HasMaxLength(128).IsRequired();
            entity.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
            // A hash is looked up on every exchange; it is unique so one code can name only one row.
            entity.HasIndex(code => code.CodeHash).IsUnique();
            entity.HasIndex(code => new { code.WorkspaceId, code.UserId });
        });

        modelBuilder.Entity<AgentInstallation>(entity =>
        {
            entity.ToTable("agent_tokens");
            entity.HasKey(installation => installation.Id);
            entity.Property(installation => installation.UserId).HasMaxLength(128).IsRequired();
            entity.Property(installation => installation.Name).HasMaxLength(100).IsRequired();
            entity.Property(installation => installation.Platform).HasMaxLength(40).IsRequired();
            entity.Property(installation => installation.Version).HasMaxLength(40).IsRequired();
            entity.Property(installation => installation.TokenHash).HasMaxLength(64).IsRequired();
            // Every agent-only request looks the token up by hash, so the hash is unique and indexed.
            entity.HasIndex(installation => installation.TokenHash).IsUnique();
            entity.HasIndex(installation => new { installation.WorkspaceId, installation.UserId });
        });
    }
}
