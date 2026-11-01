using Microsoft.Extensions.DependencyInjection;
using PostmanClone.ExecutionService.Api;
using PostmanClone.Sending.Contracts;
using PostmanClone.Sending.Assembly;
using PostmanClone.ExecutionService.History;
using PostmanClone.Sending.Execution;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>
/// Puts entries into the history through the real <see cref="IHistoryRecorder"/>, so retention and scoping run on the
/// production path. Each entry is a completed GET with a 200 response.
/// </summary>
public static class HistorySeed
{
    public static async Task<Guid> AddAsync(
        ExecutionServiceFactory factory, Guid workspace, Guid user, string url, CancellationToken cancellationToken, SecretMasker? masker = null)
    {
        await WaitForSchemaAsync(factory, cancellationToken);
        using var scope = factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IHistoryRecorder>();
        var id = await recorder.RecordAsync(Draft(workspace, user.ToString(), url, masker), cancellationToken);
        return id ?? throw new InvalidOperationException("The recorder stored nothing.");
    }

    public static HistoryDraft Draft(Guid workspace, string subject, string url, SecretMasker? masker = null) => new(
        workspace,
        subject,
        SavedRequestId: null,
        EnvironmentId: null,
        Request: new RequestDefinition(
            "GET",
            url,
            [],
            [],
            new RequestBody(BodyType.None, null, null, []),
            new RequestAuth(AuthType.None, null, null, null, null, null, null)),
        Masker: masker ?? SecretMasker.For([]),
        Result: new ExecutionResult(
            ExecutionOutcome.Completed,
            null,
            null,
            new ResponseDto(200, "OK", "1.1", [], new ResponseBodyDto("utf8", "ok", 2, false, "text/plain")),
            new ExecutionTiming(5, 0, 0, 0, 0),
            url,
            [],
            [],
            null));

    /// <summary>The schema is applied after the host listens; a seed must not race the migration.</summary>
    private static async Task WaitForSchemaAsync(ExecutionServiceFactory factory, CancellationToken cancellationToken)
    {
        using var ready = await ServiceClient.ReadyAsync(factory, cancellationToken);
    }
}
