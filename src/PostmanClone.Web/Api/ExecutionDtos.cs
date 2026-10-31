namespace PostmanClone.Web.Api;

// The Web's copies of ExecutionService's send types (CONTRACTS §4). ExecutionRoundTripTests reads the service's own types
// through these copies and writes them back; a renamed or retyped field fails there. Outcomes stay strings here: the
// service writes camelCase names, and the view maps them (ResponseView), so an unknown name degrades to a message, not a crash.

/// <summary>The body of <c>POST /api/v1/execute</c>. The request keeps its <c>{{name}}</c> references; the service fills them.</summary>
public sealed record ExecuteBody(
    RequestDefinition Request,
    Guid? EnvironmentId,
    Guid? SavedRequestId,
    int? TimeoutSeconds,
    bool? FollowRedirects);

/// <summary>Milliseconds. An unknown phase is 0.</summary>
public sealed record ExecutionTiming(int TotalMs, int DnsMs, int ConnectMs, int TtfbMs, int DownloadMs);

public sealed record HeaderLine(string Name, string Value);

/// <summary>One followed redirect: the status of the response that redirected, and its Location with secrets masked.</summary>
public sealed record RedirectHop(int StatusCode, string Location);

/// <summary>
/// The response body. <c>Encoding</c> is <c>utf8</c> for text whose bytes are valid UTF-8, otherwise <c>base64</c>.
/// <c>Content</c> is untrusted: the view renders it as encoded text only (ADR-0023).
/// </summary>
public sealed record ResponseBodyDto(string Encoding, string Content, long SizeBytes, bool Truncated, string? ContentType);

public sealed record ResponseDto(
    int StatusCode,
    string ReasonPhrase,
    string HttpVersion,
    List<HeaderLine> Headers,
    ResponseBodyDto Body);

/// <summary>The answer of <c>POST /api/v1/execute</c>. Secrets are masked in every string field (CONTRACTS §4).</summary>
public sealed record ExecutionResultDto(
    string Outcome,
    string? ErrorCode,
    string? Message,
    ResponseDto? Response,
    ExecutionTiming Timing,
    string FinalUrl,
    List<RedirectHop> Redirects,
    List<string> UnresolvedVariables,
    Guid? HistoryId);
