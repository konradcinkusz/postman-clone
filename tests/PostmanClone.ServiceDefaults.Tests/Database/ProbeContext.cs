using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PostmanClone.ServiceDefaults.Tests.Database;

/// <summary>A test-only model: the kernel has no entities of its own (P2), so the migration tests need one.</summary>
public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    public DbSet<ProbeRow> Probes => Set<ProbeRow>();
}

public sealed class ProbeRow
{
    public Guid Id { get; set; }

    public string Label { get; set; } = string.Empty;
}

/// <summary>Lets <c>dotnet ef</c> build the migration without a running host.</summary>
public sealed class ProbeContextFactory : IDesignTimeDbContextFactory<ProbeContext>
{
    public ProbeContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProbeContext>().UseNpgsql("Host=localhost;Database=design-time").Options);
}
