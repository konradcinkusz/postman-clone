using PostmanClone.Sending.Contracts;
using PostmanClone.Sending.Assembly;

namespace PostmanClone.Agent.Send;

/// <summary>The body of the local <c>POST /v1/send</c> (CONTRACTS A2.6): an <c>ExecuteRequest</c> without a saved request.</summary>
public sealed record AgentSendRequest(
    RequestDefinition Request,
    Guid? EnvironmentId,
    int? TimeoutSeconds,
    bool? FollowRedirects);

/// <summary>The body of the local <c>POST /v1/pair</c> (CONTRACTS A2.6).</summary>
public sealed record AgentPairBody(string? Code);

/// <summary>What a local send answers: a result (200), or a refusal with the status and code the protocol names.</summary>
public sealed record SendReply(int Status, ExecutionResult? Result, string? Code, string? Message)
{
    public static SendReply Sent(ExecutionResult result) => new(200, result, null, null);

    public static SendReply Refused(int status, string code, string message) => new(status, null, code, message);
}
