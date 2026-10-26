using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PostmanClone.ServiceDefaults.Database;

/// <summary>
/// Applies the schema with <c>MigrateAsync</c> after the server is listening (P4). InMemory runs
/// <c>EnsureCreated</c> instead, because it has no migrations. A failure is logged and recorded on the
/// <see cref="MigrationCompletionSignal"/>; the process stays up, and <c>/health</c> reports it as unhealthy.
/// </summary>
public sealed class MigrationHostedService<TContext>(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    MigrationCompletionSignal signal,
    ILogger<MigrationHostedService<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    private readonly CancellationTokenSource _stopping = new();
    private CancellationTokenRegistration _applicationStarted;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // ApplicationStarted fires after the server is listening, so the schema is applied after Kestrel starts.
        _applicationStarted = lifetime.ApplicationStarted.Register(() => _ = MigrateAsync(_stopping.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _applicationStarted.Dispose();
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            if (context.Database.IsInMemory())
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
            }
            else
            {
                await context.Database.MigrateAsync(cancellationToken);
            }

            signal.MarkSucceeded();
            logger.LogInformation("Database schema is applied for {Context}.", typeof(TContext).Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: the attempt is abandoned, not failed.
        }
        catch (Exception exception)
        {
            signal.MarkFailed();
            logger.LogError(exception, "Database migration failed for {Context}.", typeof(TContext).Name);
        }
    }
}
