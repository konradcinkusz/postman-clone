using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.History;

/// <summary>
/// Entries as ExecutionService stores them (ADR-0015): the request is the masked template and the result was masked by the sender.
/// Times are relative to the fake clock, so no test depends on a calendar date.
/// </summary>
internal static class HistoryFixtures
{
    /// <summary>The mask as the service writes it (CONTRACTS §4). Test data only: it is not a secret.</summary>
    public const string Mask = "••••••";

    public static HistoryEntryDto Entry(
        TimeProvider clock,
        int minutesAgo,
        string url = "https://api.example.test/items",
        string method = "GET",
        string body = "{\"ok\":true}",
        bool maskedHeader = false)
    {
        var id = Guid.NewGuid();
        var headers = maskedHeader
            ? new List<KeyValue> { new("Authorization", Mask) }
            : new List<KeyValue> { new("Accept", "application/json") };
        var request = new RequestDefinition(
            method,
            url,
            [new KeyValue("page", "2")],
            headers,
            new RequestBody("none", null, null, null),
            new RequestAuth("bearer", null, null, null, null, null, null));
        var result = new ExecutionResultDto(
            "completed",
            null,
            null,
            new ResponseDto(
                200,
                "OK",
                "HTTP/1.1",
                [new HeaderLine("Content-Type", "application/json")],
                new ResponseBodyDto("utf8", body, body.Length, false, "application/json")),
            new ExecutionTiming(120, 4, 9, 30, 2),
            url,
            [],
            [],
            id);
        return new HistoryEntryDto(id, clock.GetUtcNow().AddMinutes(-minutesAgo), null, null, request, result);
    }

    public static HistorySummaryDto Summary(HistoryEntryDto entry) => new(
        entry.Id,
        entry.ExecutedAt,
        entry.Request.Method,
        entry.Request.Url,
        entry.Result.Response?.StatusCode,
        entry.Result.Outcome,
        entry.Result.Timing.TotalMs,
        entry.Result.Response?.Body.SizeBytes ?? 0);
}

/// <summary>
/// An in-memory history with the service's rules: the caller's entries only, newest first, a filter on the masked URL, paging,
/// <c>not_found</c> for an absent entry, and a clear that removes every entry. Every call is recorded for the test to read.
/// </summary>
internal sealed class FakeHistoryClient : IHistoryClient
{
    private readonly List<HistoryEntryDto> _entries = [];

    public List<string> Calls { get; } = [];

    public List<string> Bearers { get; } = [];

    /// <summary>When set, every call answers that the sender cannot be reached.</summary>
    public bool Unavailable { get; set; }

    public IReadOnlyList<HistoryEntryDto> Entries => _entries;

    public HistoryEntryDto Add(HistoryEntryDto entry)
    {
        _entries.Add(entry);
        return entry;
    }

    public Task<CollectionAnswer<PageResult<HistorySummaryDto>>> ListAsync(string accessToken, int page, int pageSize, string? query, CancellationToken cancellationToken)
    {
        Record($"list page={page} size={pageSize} q={query}", accessToken);
        if (Unavailable)
        {
            return Task.FromResult(new CollectionAnswer<PageResult<HistorySummaryDto>>(CollectionOutcome.Unavailable, null, null));
        }

        var matching = _entries
            .Where(entry => string.IsNullOrEmpty(query) || entry.Request.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.ExecutedAt)
            .ToList();
        var items = matching.Skip((page - 1) * pageSize).Take(pageSize).Select(HistoryFixtures.Summary).ToList();
        var result = new PageResult<HistorySummaryDto>(items, page, pageSize, matching.Count);
        return Task.FromResult(new CollectionAnswer<PageResult<HistorySummaryDto>>(CollectionOutcome.Succeeded, result, null));
    }

    public Task<CollectionAnswer<HistoryEntryDto>> GetAsync(string accessToken, Guid entryId, CancellationToken cancellationToken)
    {
        Record($"get {entryId}", accessToken);
        if (Unavailable)
        {
            return Task.FromResult(new CollectionAnswer<HistoryEntryDto>(CollectionOutcome.Unavailable, null, null));
        }

        var entry = _entries.Find(candidate => candidate.Id == entryId);
        return Task.FromResult(entry is null
            ? new CollectionAnswer<HistoryEntryDto>(CollectionOutcome.NotFound, null, "not_found")
            : new CollectionAnswer<HistoryEntryDto>(CollectionOutcome.Succeeded, entry, null));
    }

    public Task<CollectionAnswer> DeleteAsync(string accessToken, Guid entryId, CancellationToken cancellationToken)
    {
        Record($"delete {entryId}", accessToken);
        if (Unavailable)
        {
            return Task.FromResult(new CollectionAnswer(CollectionOutcome.Unavailable, null));
        }

        var removed = _entries.RemoveAll(entry => entry.Id == entryId);
        return Task.FromResult(removed == 0
            ? new CollectionAnswer(CollectionOutcome.NotFound, "not_found")
            : new CollectionAnswer(CollectionOutcome.Succeeded, null));
    }

    public Task<CollectionAnswer> ClearAsync(string accessToken, CancellationToken cancellationToken)
    {
        Record("clear", accessToken);
        if (Unavailable)
        {
            return Task.FromResult(new CollectionAnswer(CollectionOutcome.Unavailable, null));
        }

        _entries.Clear();
        return Task.FromResult(new CollectionAnswer(CollectionOutcome.Succeeded, null));
    }

    private void Record(string call, string bearer)
    {
        Calls.Add(call);
        Bearers.Add(bearer);
    }
}
