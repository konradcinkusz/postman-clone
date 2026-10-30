using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ServiceDefaults.Database;

namespace PostmanClone.CollectionsService.Tests.Support;

public static class SchemaWait
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <summary>Waits until the hosted migration has finished. Readiness and the data routes depend on it.</summary>
    public static async Task AppliedAsync(CollectionsFactory factory, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Limit);
        var signal = factory.Services.GetRequiredService<MigrationCompletionSignal>();
        await signal.WaitAsync(timeout.Token);
    }
}
