using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace PostmanClone.ServiceDefaults.Database;

/// <summary>
/// Registers a service's <see cref="DbContext"/> (P4). The provider comes from <c>DATABASE_PROVIDER</c>:
/// <c>PostgreSQL</c> (the default) or <c>InMemory</c> for tests. Any other value fails at startup.
/// The schema is applied by <see cref="MigrationHostedService{TContext}"/>, never by <c>EnsureCreated</c>
/// outside the InMemory path.
/// </summary>
public static class DatabaseExtensions
{
    public const string ProviderSetting = "DATABASE_PROVIDER";
    public const string PostgreSqlProvider = "PostgreSQL";
    public const string InMemoryProvider = "InMemory";

    public static IHostApplicationBuilder AddDatabaseContext<TContext>(this IHostApplicationBuilder builder, string connectionName)
        where TContext : DbContext
    {
        var provider = builder.Configuration[ProviderSetting] ?? PostgreSqlProvider;

        if (string.Equals(provider, InMemoryProvider, StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddDbContext<TContext>(options => options.UseInMemoryDatabase(connectionName));
        }
        else if (string.Equals(provider, PostgreSqlProvider, StringComparison.OrdinalIgnoreCase))
        {
            var rawConnectionString = builder.Configuration.GetConnectionString(connectionName)
                ?? throw new InvalidOperationException($"Connection string '{connectionName}' is not configured.");
            var connectionString = ConnectionStringNormalizer.Normalize(rawConnectionString);

            builder.Services.AddDbContext<TContext>(options => options.UseNpgsql(connectionString,
                npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5)));
        }
        else
        {
            throw new InvalidOperationException(
                $"{ProviderSetting} must be '{PostgreSqlProvider}' or '{InMemoryProvider}'.");
        }

        builder.Services.TryAddSingleton<MigrationCompletionSignal>();
        builder.Services.AddHostedService<MigrationHostedService<TContext>>();
        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck<TContext>>("database")
            .AddCheck<MigrationReadinessHealthCheck>("migrations");

        return builder;
    }
}
