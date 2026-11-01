using PostmanClone.Sending.Assembly;
using PostmanClone.Sending.Contracts;

namespace PostmanClone.Sending.Execution;

/// <summary>A request whose shape has been checked and whose defaults are filled. Nothing has been read from an environment yet.</summary>
/// <param name="Definition">The request as written, with its defaults applied; <c>{{name}}</c> references are unexpanded.</param>
/// <param name="Timeout">The send deadline, from the request or the default.</param>
/// <param name="FollowRedirects">Whether redirects are followed (at most the configured maximum).</param>
/// <param name="Started">The stopwatch timestamp the total time is measured from.</param>
public sealed record PreparedSend(RequestDefinition Definition, TimeSpan Timeout, bool FollowRedirects, long Started);

/// <summary>
/// The outcome of one send: the masked result, the masker built from the request's secret values (so the caller can mask
/// the same request again, for example when it is recorded), and the request as it was shaped.
/// </summary>
public sealed record SendResult(ExecutionResult Result, SecretMasker Masker, RequestDefinition Definition);
