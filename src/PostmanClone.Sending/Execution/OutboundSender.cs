using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using PostmanClone.Sending.Assembly;
using PostmanClone.Sending.Contracts;
using PostmanClone.Sending.Limits;
using PostmanClone.Sending.Ssrf;

namespace PostmanClone.Sending.Execution;

/// <summary>
/// Sends one request (CONTRACTS §4, ADR-0011, ADR-0012, ADR-0014, ADR-0027). It is shared by ExecutionService and the local
/// agent, so both give the same result for the same request. The order is fixed: shape check (<see cref="Prepare"/>), then
/// the caller's variables (supplied by the host), then assembly, the URL checks and the send (<see cref="SendAsync"/>).
/// Redirects are followed one hop at a time, so each hop is checked again. One deadline covers every hop and the body.
/// A refusal or a failure becomes an outcome; only a bad request is thrown.
/// </summary>
public sealed class OutboundSender
{
    /// <summary>Rows per key/value list (CONTRACTS §1).</summary>
    private const int MaxRows = 100;

    private const int MaxKeyLength = 256;

    private const int MaxValueLength = 8192;

    private const int BadRequest = 400;

    private const int PayloadTooLarge = 413;

    /// <summary>How long an idle connection may be pooled. Each send has its own handler, so this is an upper bound only.</summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromSeconds(10);

    private static readonly RequestBody NoBody = new(BodyType.None, null, null, []);

    private static readonly RequestAuth NoAuth = new(AuthType.None, null, null, null, null, null, null);

    private readonly SendingLimits _limits;
    private readonly OutboundRules _rules;
    private readonly IHostResolver _resolver;
    private readonly IAddressDialer _dialer;
    private readonly ILogger<OutboundSender> _logger;
    private readonly RequestAssembler _assembler;
    private readonly TargetValidator _validator;
    private readonly RedirectPlanner _planner;

    public OutboundSender(SendingLimits limits, OutboundRules rules, IHostResolver resolver, IAddressDialer dialer, ILogger<OutboundSender> logger)
    {
        _limits = limits;
        _rules = rules;
        _resolver = resolver;
        _dialer = dialer;
        _logger = logger;
        _assembler = new RequestAssembler(limits, CryptographicRandomBytes.Instance);
        _validator = new TargetValidator(limits.UrlMaxLength, rules);
        _planner = new RedirectPlanner(_validator, limits.MaxRedirects);
    }

    /// <summary>
    /// Checks the request's shape and fills the contract's defaults, before any variable is read. A bad shape is thrown as
    /// <see cref="ExecuteFailure"/>, so the host can answer without touching another service.
    /// </summary>
    public PreparedSend Prepare(ExecuteRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        var definition = Shape(request);
        var timeout = TimeSpan.FromSeconds(request.TimeoutSeconds ?? _limits.DefaultTimeoutSeconds);
        return new PreparedSend(definition, timeout, request.FollowRedirects ?? true, started);
    }

    /// <summary>
    /// Checks a request template the way a send would, but with no variable values and no send: the shape, the caps, and the
    /// rule that a secret field holds a reference and never a literal (CONTRACTS §1). Used when a result is recorded without
    /// being sent here (CONTRACTS A2.5). Throws <see cref="ExecuteFailure"/> with the same codes a send gives.
    /// </summary>
    public void CheckTemplate(ExecuteRequest request)
    {
        var definition = Shape(request);
        Assemble(definition, new Dictionary<string, ResolvedValue>(StringComparer.Ordinal));
    }

    /// <summary>Assembles the prepared request with the given variables and sends it. Never throws for a refused target or a failed send.</summary>
    /// <param name="prepared">The result of <see cref="Prepare"/>.</param>
    /// <param name="variables">The enabled variables of the environment, or none. The host has already checked the caller may see them.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public async Task<SendResult> SendAsync(PreparedSend prepared, IReadOnlyDictionary<string, ResolvedValue> variables, CancellationToken cancellationToken)
    {
        var assembled = Assemble(prepared.Definition, variables);
        var result = await SendAssembledAsync(assembled, prepared.FollowRedirects, prepared.Timeout, prepared.Started, cancellationToken);
        return new SendResult(result, assembled.Masker, prepared.Definition);
    }

    /// <summary>Checks the shape and fills the defaults the contract gives: an absent list is empty, an absent body or auth is none.</summary>
    private RequestDefinition Shape(ExecuteRequest request)
    {
        if (request.Request is null)
        {
            throw Invalid("The request is required.");
        }

        if (request.TimeoutSeconds is { } seconds && (seconds < 1 || seconds > _limits.MaxTimeoutSeconds))
        {
            throw Invalid($"The timeout must be between 1 and {_limits.MaxTimeoutSeconds} seconds.");
        }

        var definition = request.Request;
        var body = definition.Body is null
            ? NoBody
            : definition.Body with { Fields = Rows(definition.Body.Fields, "body fields") };

        return new RequestDefinition(
            definition.Method ?? string.Empty,
            definition.Url ?? string.Empty,
            Rows(definition.Params, "params"),
            Rows(definition.Headers, "headers"),
            body,
            definition.Auth ?? NoAuth);
    }

    private static List<KeyValueRow> Rows(IEnumerable<KeyValueRow>? rows, string field)
    {
        var checkedRows = (rows ?? []).Select(row => row with { Key = row.Key ?? string.Empty, Value = row.Value ?? string.Empty }).ToList();
        if (checkedRows.Count > MaxRows)
        {
            throw Invalid($"The {field} list may hold at most {MaxRows} rows.");
        }

        foreach (var row in checkedRows)
        {
            if (row.Key.Length > MaxKeyLength)
            {
                throw Invalid($"A key in the {field} list is longer than {MaxKeyLength} characters.");
            }

            if (row.Value.Length > MaxValueLength)
            {
                throw Invalid($"A value in the {field} list is longer than {MaxValueLength} characters.");
            }
        }

        return checkedRows;
    }

    private AssembledRequest Assemble(RequestDefinition definition, IReadOnlyDictionary<string, ResolvedValue> variables)
    {
        try
        {
            return _assembler.Assemble(definition, variables);
        }
        catch (RequestAssemblyException failure)
        {
            // The assembler's messages name a field and never a value (ADR-0012), so they are safe to return.
            throw failure.Code switch
            {
                RequestAssemblyException.SecretLiteralNotAllowed => new ExecuteFailure(BadRequest, "secret_literal_not_allowed", failure.Message),
                RequestAssemblyException.BodyTooLarge => new ExecuteFailure(PayloadTooLarge, "payload_too_large", failure.Message),
                _ => Invalid(failure.Message),
            };
        }
    }

    private async Task<ExecutionResult> SendAssembledAsync(AssembledRequest assembled, bool followRedirects, TimeSpan timeout, long started, CancellationToken cancellationToken)
    {
        var masker = assembled.Masker;
        var unresolved = assembled.UnresolvedVariables;
        var timings = new HopTimings();
        var redirects = new List<RedirectHop>();

        var check = _validator.Validate(assembled.Url);
        if (!check.Accepted)
        {
            _logger.LogInformation("Request {Method} was refused before sending: {ErrorCode}.", assembled.Method, check.ErrorCode);
            return Failed(ExecutionOutcome.Blocked, check.ErrorCode!, BlockedMessage(check.ErrorCode!, check.Category), masker.MaskUrl(assembled.Url), redirects, unresolved, TimingSince(started, timings));
        }

        var target = check.Target!;
        var method = assembled.Method;
        var headers = assembled.Headers;
        var body = assembled.Body;
        var ttfbMs = 0;
        HttpResponseMessage? response = null;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var handler = SsrfHandlerFactory.Create(_rules.Options, new TimedHostResolver(_resolver, timings), new TimedDialer(_dialer, timings));
        handler.PooledConnectionLifetime = PooledConnectionLifetime;
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

        try
        {
            while (true)
            {
                using var request = WireRequest.Build(method, target.Uri, headers, body);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                ttfbMs = ElapsedMs(started);
                if (!followRedirects)
                {
                    break;
                }

                var location = FirstLocation(response);
                var step = _planner.Plan(target, method, (int)response.StatusCode, location, redirects.Count);
                if (step.Outcome == RedirectOutcome.NotRedirect)
                {
                    break;
                }

                redirects.Add(new RedirectHop((int)response.StatusCode, masker.MaskUrl(location!)));
                if (step.Outcome == RedirectOutcome.Refuse)
                {
                    return Failed(ExecutionOutcome.Blocked, step.ErrorCode!, BlockedMessage(step.ErrorCode!, null), masker.MaskUrl(target.Uri), redirects, unresolved, TimingSince(started, timings, ttfbMs));
                }

                response.Dispose();
                response = null;
                (method, headers, body) = NextHop(step, method, headers, body);
                target = step.Next!;
            }

            var declaredTooLarge = response!.Content.Headers.ContentLength > _limits.RefuseDeclaredLengthBytes;
            var captured = new CapturedBody([], 0, Truncated: true);
            var downloadMs = 0;
            if (!declaredTooLarge)
            {
                var download = Stopwatch.GetTimestamp();
                captured = await ResponseBody.ReadAsync(response.Content, _limits.ResponseReadCapBytes, deadline.Token);
                downloadMs = ElapsedMs(download);
            }

            var dto = new ResponseDto(
                (int)response.StatusCode,
                response.ReasonPhrase ?? string.Empty,
                $"{response.Version.Major}.{response.Version.Minor}",
                masker.MaskHeaders(HeaderLines(response)),
                ResponseBody.Describe(captured, response.Content.Headers.ContentType?.ToString(), masker));
            var timing = TimingSince(started, timings, ttfbMs, downloadMs);

            _logger.LogInformation(
                "Sent {Method} to {Host}: status {StatusCode}, {ResponseBytes} body bytes, {TotalMs} ms, truncated {Truncated}.",
                method, masker.MaskText(target.Host), dto.StatusCode, captured.SizeBytes, timing.TotalMs, captured.Truncated);

            return new ExecutionResult(
                declaredTooLarge ? ExecutionOutcome.TooLarge : ExecutionOutcome.Completed,
                null,
                null,
                dto,
                timing,
                masker.MaskUrl(target.Uri),
                redirects,
                unresolved,
                null);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            var finalUrl = masker.MaskUrl(target.Uri);
            var timing = TimingSince(started, timings, ttfbMs);

            var rejected = SsrfRejectedException.Find(failure);
            if (rejected is not null)
            {
                _logger.LogInformation("Request to {Host} was refused: {ErrorCode}.", masker.MaskText(target.Host), rejected.ErrorCode);
                return Failed(ExecutionOutcome.Blocked, rejected.ErrorCode, BlockedMessage(rejected.ErrorCode, rejected.Category), finalUrl, redirects, unresolved, timing);
            }

            if (deadline.IsCancellationRequested)
            {
                return Failed(ExecutionOutcome.Timeout, "timeout", $"The request did not finish within {timeout.TotalSeconds:0} seconds.", finalUrl, redirects, unresolved, timing);
            }

            if (failure is InvalidDataException)
            {
                return Failed(ExecutionOutcome.NetworkError, "invalid_body_encoding", "The response body could not be decoded.", finalUrl, redirects, unresolved, timing);
            }

            _logger.LogInformation("Request to {Host} failed with {FailureType}.", masker.MaskText(target.Host), failure.GetType().Name);
            return Failed(ExecutionOutcome.NetworkError, "network_error", "The request could not be completed.", finalUrl, redirects, unresolved, timing);
        }
        finally
        {
            // Cancelling first asks the transport to abort the connection rather than drain an unread body. The memory bound
            // does not depend on it: the reader keeps no more than the cap, whatever the transport does with the rest.
            deadline.Cancel();
            response?.Dispose();
        }
    }

    /// <summary>
    /// 303 becomes GET (HEAD stays HEAD). 301 and 302 turn a POST into GET. 307 and 308 keep the method and the body. A cross-origin hop drops
    /// the credential headers; a hop that changes the method drops the body and its content headers.
    /// </summary>
    private static (string Method, IReadOnlyList<HeaderLine> Headers, byte[]? Body) NextHop(RedirectStep step, string method, IReadOnlyList<HeaderLine> headers, byte[]? body)
    {
        var kept = headers.Where(header => !RedirectPlanner.ShouldDropHeader(header.Name, step)).ToList();
        if (!step.DropBody)
        {
            return (step.Method!, kept, body);
        }

        return (step.Method!, kept.Where(header => !WireRequest.IsContentHeader(header.Name)).ToList(), null);
    }

    private static ExecuteFailure Invalid(string message) => new(BadRequest, "validation_failed", message);

    private static ExecutionResult Failed(
        ExecutionOutcome outcome,
        string errorCode,
        string message,
        string finalUrl,
        IReadOnlyList<RedirectHop> redirects,
        IReadOnlyList<string> unresolved,
        ExecutionTiming timing) =>
        new(outcome, errorCode, message, null, timing, finalUrl, redirects, unresolved, null);

    /// <summary>The messages never name the address that matched; the category is the most they say (ADR-0011).</summary>
    private string BlockedMessage(string code, string? category) => code switch
    {
        SsrfErrorCodes.BlockedAddress => category is null
            ? "The destination address is not allowed."
            : $"The destination address is not allowed ({category}).",
        SsrfErrorCodes.BlockedScheme => "Only http and https URLs can be sent.",
        SsrfErrorCodes.BlockedPort => "The destination port is not allowed.",
        SsrfErrorCodes.DnsFailed => "The host name could not be resolved.",
        SsrfErrorCodes.TooManyRedirects => $"The redirect chain is longer than {_limits.MaxRedirects} redirects.",
        _ => "The URL is not valid.",
    };

    private static ExecutionTiming TimingSince(long started, HopTimings timings, int ttfbMs = 0, int downloadMs = 0) =>
        new(ElapsedMs(started), timings.DnsMs, timings.ConnectMs, ttfbMs, downloadMs);

    private static int ElapsedMs(long started) => (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static string? FirstLocation(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Location", out var values) ? values.FirstOrDefault() : null;

    private static List<HeaderLine> HeaderLines(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .SelectMany(header => header.Value.Select(value => new HeaderLine(header.Key, value)))
            .ToList();
}
