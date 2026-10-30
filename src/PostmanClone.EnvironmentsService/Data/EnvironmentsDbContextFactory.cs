using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PostmanClone.EnvironmentsService.Data;

/// <summary>Lets <c>dotnet ef</c> build the model without a running host or a reachable database.</summary>
public sealed class EnvironmentsDbContextFactory : IDesignTimeDbContextFactory<EnvironmentsDbContext>
{
    public EnvironmentsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<EnvironmentsDbContext>().UseNpgsql("Host=localhost;Database=design-time").Options);
}
