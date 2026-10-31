extern alias execution;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PostmanClone.Web.Api;
using Service = execution::PostmanClone.ExecutionService.Api;
using ServiceAssembly = execution::PostmanClone.ExecutionService.Sending.Assembly;

namespace PostmanClone.Web.Tests.Api;

/// <summary>
/// The Web's copies of ExecutionService's history types still match the service's own JSON (CONTRACTS §4, ADR-0015). The service
/// writes an entry and a page; the copies read them and write them back, and the two trees must be equal. A renamed or retyped field
/// fails here, not on a live page. A null the copy writes for a property the service does not send (a row's description, which a
/// history row does not carry) is not drift, and is ignored; a missing or changed value is.
/// </summary>
public sealed class HistoryRoundTripTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The service serializes enums as camelCase strings (ExecutionServiceExtensions); the copies carry them as strings.</summary>
    private static readonly JsonSerializerOptions ServiceOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Equal trees, where a null on either side matches an absent property on the other.</summary>
    private static bool SameIgnoringNulls(JsonNode? original, JsonNode? again)
    {
        if (IsNull(original) || IsNull(again))
        {
            return IsNull(original) && IsNull(again);
        }

        if (original is JsonObject left && again is JsonObject right)
        {
            return left.Select(pair => pair.Key).Union(right.Select(pair => pair.Key))
                .All(key => SameIgnoringNulls(left[key], right[key]));
        }

        if (original is JsonArray leftItems && again is JsonArray rightItems)
        {
            return leftItems.Count == rightItems.Count
                && leftItems.Zip(rightItems).All(pair => SameIgnoringNulls(pair.First, pair.Second));
        }

        return JsonNode.DeepEquals(original, again);
    }

    private static bool IsNull(JsonNode? node) => node is null || node.GetValueKind() == JsonValueKind.Null;

    private static Service.HistoryEntry ServiceEntry(Guid id)
    {
        var request = new ServiceAssembly.RequestDefinition(
            "POST",
            "https://api.example.test/items/{{itemId}}",
            [new ServiceAssembly.KeyValueRow("page", "2", Enabled: false)],
            [new ServiceAssembly.KeyValueRow("Authorization", "••••••")],
            new ServiceAssembly.RequestBody(ServiceAssembly.BodyType.Raw, ServiceAssembly.RawLanguage.Json, "{\"a\":1}", []),
            new ServiceAssembly.RequestAuth(ServiceAssembly.AuthType.Bearer, null, null, null, null, null, null));
        var result = new Service.ExecutionResult(
            Service.ExecutionOutcome.Completed,
            null,
            null,
            new Service.ResponseDto(
                200,
                "OK",
                "HTTP/1.1",
                [new ServiceAssembly.HeaderLine("Content-Type", "application/json")],
                new Service.ResponseBodyDto("utf8", "{\"ok\":true}", 11, true, "application/json")),
            new Service.ExecutionTiming(42, 3, 5, 20, 14),
            "https://api.example.test/items/7",
            [new Service.RedirectHop(302, "https://api.example.test/next")],
            ["itemId"],
            id);
        return new Service.HistoryEntry(id, DateTimeOffset.UnixEpoch.AddDays(1), null, null, request, result);
    }

    [Fact]
    public void A_service_entry_reads_into_the_copies_and_writes_back_unchanged()
    {
        var id = Guid.NewGuid();
        var original = JsonNode.Parse(JsonSerializer.Serialize(ServiceEntry(id), ServiceOptions));

        var copy = JsonSerializer.Deserialize<HistoryEntryDto>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(copy, WebOptions));

        Assert.True(SameIgnoringNulls(original, again), $"The entry changed on the way through the copies:\n{original}\n{again}");
        Assert.Equal(id, copy!.Id);
        Assert.Equal("POST", copy.Request.Method);
        Assert.Equal("••••••", copy.Request.Headers![0].Value);
        Assert.Equal("completed", copy.Result.Outcome);
        Assert.True(copy.Result.Response!.Body.Truncated);
    }

    [Fact]
    public void A_service_page_of_summaries_reads_into_the_page_copy()
    {
        var id = Guid.NewGuid();
        var page = new Service.Page<Service.HistorySummary>(
            [new Service.HistorySummary(id, DateTimeOffset.UnixEpoch.AddDays(1), "GET", "https://api.example.test/{{x}}", 200, "completed", 42, 11)],
            2,
            20,
            21);
        var original = JsonNode.Parse(JsonSerializer.Serialize(page, ServiceOptions));

        var copy = JsonSerializer.Deserialize<PageResult<HistorySummaryDto>>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(copy, WebOptions));

        Assert.True(SameIgnoringNulls(original, again), $"The page changed on the way through the copies:\n{original}\n{again}");
        Assert.Equal(2, copy!.Page);
        Assert.Equal(21, copy.Total);
        Assert.Equal(id, copy.Items[0].Id);
    }
}
