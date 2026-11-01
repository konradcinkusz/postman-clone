extern alias execution;
extern alias sending;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PostmanClone.Web.Api;
using Service = execution::PostmanClone.ExecutionService.Api;
using ServiceContracts = sending::PostmanClone.Sending.Contracts;
using ServiceAssembly = sending::PostmanClone.Sending.Assembly;

namespace PostmanClone.Web.Tests.Api;

/// <summary>
/// The Web's copies of ExecutionService's send types still match the service's own JSON (CONTRACTS §4, §5). The result is written by
/// the service's types, read through the copies and written back: the two trees must be equal. The request the Web sends is read by
/// the service's own request type, field by field.
/// </summary>
public sealed class ExecutionRoundTripTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The service serializes enums as camelCase strings (ExecutionServiceExtensions); the copies carry them as strings.</summary>
    private static readonly JsonSerializerOptions ServiceOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static ServiceContracts.ExecutionResult ServiceResult() => new(
        ServiceContracts.ExecutionOutcome.Completed,
        null,
        null,
        new ServiceContracts.ResponseDto(
            200,
            "OK",
            "HTTP/1.1",
            [new ServiceAssembly.HeaderLine("Content-Type", "application/json")],
            new ServiceContracts.ResponseBodyDto("utf8", "{\"ok\":true}", 11, false, "application/json")),
        new ServiceContracts.ExecutionTiming(42, 3, 5, 20, 14),
        "https://api.example.test/items?page=2",
        [new ServiceContracts.RedirectHop(302, "https://api.example.test/next")],
        ["token"],
        Guid.NewGuid());

    [Fact]
    public void A_service_result_reads_into_the_copies_and_writes_back_unchanged()
    {
        var original = JsonNode.Parse(JsonSerializer.Serialize(ServiceResult(), ServiceOptions));

        var copy = JsonSerializer.Deserialize<ExecutionResultDto>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(copy, WebOptions));

        Assert.True(JsonNode.DeepEquals(original, again), $"The result changed on the way through the copies:\n{original}\n{again}");
        Assert.Equal("completed", copy!.Outcome);
        Assert.Equal(200, copy.Response!.StatusCode);
        Assert.Equal("{\"ok\":true}", copy.Response.Body.Content);
    }

    [Fact]
    public void The_web_request_is_read_by_the_service_request_type_with_every_field_intact()
    {
        var body = new ExecuteBody(
            new RequestDefinition(
                "POST",
                "https://api.example.test/items",
                [new KeyValue("page", "2", Enabled: false)],
                [new KeyValue("X-Trace", "abc")],
                new RequestBody("raw", "json", "{\"a\":1}", []),
                new RequestAuth("bearer", "{{api.token}}", null, null, null, null, null)),
            Guid.NewGuid(),
            Guid.NewGuid(),
            7,
            false);

        var sent = JsonSerializer.Deserialize<ServiceContracts.ExecuteRequest>(JsonSerializer.Serialize(body, WebOptions), ServiceOptions);

        Assert.NotNull(sent);
        Assert.Equal(body.EnvironmentId, sent.EnvironmentId);
        Assert.Equal(body.SavedRequestId, sent.SavedRequestId);
        Assert.Equal(7, sent.TimeoutSeconds);
        Assert.False(sent.FollowRedirects);
        Assert.Equal("POST", sent.Request.Method);
        Assert.Equal("https://api.example.test/items", sent.Request.Url);
        Assert.False(sent.Request.Params[0].Enabled);
        Assert.Equal("X-Trace", sent.Request.Headers[0].Key);
        Assert.Equal("{\"a\":1}", sent.Request.Body.Raw);
        Assert.Equal("{{api.token}}", sent.Request.Auth!.BearerToken);
    }
}
