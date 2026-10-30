using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ServiceDefaults.Database;

namespace PostmanClone.ExecutionService.Tests.Support;

public static class ServiceClient
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A client for a service whose schema is applied. <c>/health</c> is unhealthy until the migration has finished
    /// (MigrateAsync runs after the server listens), so tests wait on the completion signal first.
    /// </summary>
    public static async Task<HttpClient> ReadyAsync(WebApplicationFactory<Program> factory, CancellationToken cancellationToken)
    {
        var signal = factory.Services.GetRequiredService<MigrationCompletionSignal>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WaitLimit);
        await signal.WaitAsync(timeout.Token);
        return factory.CreateClient();
    }
}
