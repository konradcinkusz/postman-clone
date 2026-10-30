using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PostmanClone.ExecutionService.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without a running host or a database. The connection
/// string is never opened: migrations only compare the model with the snapshot.
/// </summary>
public sealed class ExecutionDbContextFactory : IDesignTimeDbContextFactory<ExecutionDbContext>
{
    public ExecutionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseNpgsql("Host=localhost;Database=design-time").Options);
}
