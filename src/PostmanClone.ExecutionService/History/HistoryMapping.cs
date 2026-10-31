using System.Text.Json;
using System.Text.Json.Serialization;
using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.Sending.Assembly;
using PostmanClone.ExecutionService.Sending.Execution;

namespace PostmanClone.ExecutionService.History;

/// <summary>Turns a draft into a row and a row back into the contract's shapes. The stored JSON uses the HTTP format.</summary>
public static class HistoryMapping
{
    /// <summary>The <c>method</c> column's width (migration InitialCreate).</summary>
    private const int MethodColumnLength = 16;

    private static readonly JsonSerializerOptions Json = CreateJson();

    public static HistoryEntryRow ToRow(Guid id, DateTimeOffset executedAt, Guid userId, HistoryDraft draft, int bodyMaxBytes)
    {
        var template = HistoryRedaction.Template(draft.Request, draft.Masker);
        var response = draft.Result.Response;
        var stored = HistoryBodyLimit.Apply(draft.Result with { HistoryId = null }, bodyMaxBytes);

        return new HistoryEntryRow
        {
            Id = id,
            WorkspaceId = draft.WorkspaceId,
            UserId = userId,
            ExecutedAt = executedAt,
            Method = Clip(template.Method, MethodColumnLength),
            Url = template.Url,
            StatusCode = response?.StatusCode,
            Outcome = OutcomeName(draft.Result.Outcome),
            TotalMs = draft.Result.Timing.TotalMs,
            SizeBytes = response?.Body.SizeBytes ?? 0,
            SavedRequestId = draft.SavedRequestId,
            EnvironmentId = draft.EnvironmentId,
            RequestJson = JsonSerializer.Serialize(template, Json),
            ResultJson = JsonSerializer.Serialize(stored, Json),
        };
    }

    public static HistoryEntry ToEntry(HistoryEntryRow row)
    {
        var request = JsonSerializer.Deserialize<RequestDefinition>(row.RequestJson, Json)
            ?? throw new InvalidOperationException("A stored history request is empty.");
        var result = JsonSerializer.Deserialize<ExecutionResult>(row.ResultJson, Json)
            ?? throw new InvalidOperationException("A stored history result is empty.");
        return new HistoryEntry(row.Id, row.ExecutedAt, row.SavedRequestId, row.EnvironmentId, request, result with { HistoryId = row.Id });
    }

    public static string OutcomeName(ExecutionOutcome outcome) => JsonNamingPolicy.CamelCase.ConvertName(outcome.ToString());

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
