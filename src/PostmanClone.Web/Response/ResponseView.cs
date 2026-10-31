using PostmanClone.Web.Api;

namespace PostmanClone.Web.Response;

/// <summary>How a send ended, as the page reads it. <see cref="Unknown"/> covers an outcome this page does not know.</summary>
public enum OutcomeKind
{
    Completed,
    Blocked,
    Timeout,
    NetworkError,
    InvalidRequest,
    TooLarge,
    Unknown,
}

public enum BodyMode
{
    Pretty,
    Raw,
    Preview,
}

/// <summary>
/// A send's result, ready for the page (ADR-0022). It holds what ExecutionService returned and derives the rest: the outcome's
/// words, the status family, the times and sizes as text, and the lines each body view shows. It never holds markup; every string
/// here is text the page encodes. Lines are built lazily and once, so a re-render does not re-format a large body.
/// </summary>
public sealed class ResponseView
{
    private readonly ExecutionResultDto _result;
    private readonly Lazy<IReadOnlyList<IReadOnlyList<ViewToken>>?> _pretty;
    private readonly Lazy<IReadOnlyList<IReadOnlyList<ViewToken>>> _raw;

    private ResponseView(ExecutionResultDto result)
    {
        _result = result;
        Outcome = ParseOutcome(result.Outcome);
        (Heading, Explanation) = ResponseMessages.ForOutcome(Outcome, result.ErrorCode);
        Body = result.Response is { } response ? new BodyView(response.Body) : null;
        _pretty = new Lazy<IReadOnlyList<IReadOnlyList<ViewToken>>?>(() =>
            Body is { LooksJson: true, IsBinary: false } ? JsonPretty.TryFormat(Body.Content) : null);
        _raw = new Lazy<IReadOnlyList<IReadOnlyList<ViewToken>>>(() =>
            Body is { IsBinary: false } ? TextLines.Of(Body.Content) : []);
    }

    public static ResponseView From(ExecutionResultDto result) => new(result);

    public OutcomeKind Outcome { get; }

    /// <summary>The short name of the outcome (for instance "Request blocked").</summary>
    public string Heading { get; }

    /// <summary>What happened and what the user can do. Chosen by this page from the outcome and its code, never from the body.</summary>
    public string Explanation { get; }

    /// <summary>The service's message (already masked, and category-level for a refusal). Shown as text, beside the explanation.</summary>
    public string? ServiceMessage => _result.Message;

    public string? ErrorCode => _result.ErrorCode;

    public string FinalUrl => _result.FinalUrl;

    public IReadOnlyList<RedirectHop> Redirects => _result.Redirects;

    public IReadOnlyList<string> UnresolvedVariables => _result.UnresolvedVariables;

    public ExecutionTiming Timing => _result.Timing;

    public Guid? HistoryId => _result.HistoryId;

    public bool HasResponse => _result.Response is not null;

    public int StatusCode => _result.Response?.StatusCode ?? 0;

    public string StatusText => _result.Response is { } response
        ? ResponseFormat.StatusLine(response.StatusCode, response.ReasonPhrase)
        : string.Empty;

    public StatusFamily Family => ResponseFormat.Family(StatusCode);

    public string BadgeClass => ResponseFormat.BadgeClass(Family);

    public string TimeText => ResponseFormat.Milliseconds(Timing.TotalMs);

    public string SizeText => Body is null ? string.Empty : ResponseFormat.Bytes(Body.SizeBytes);

    /// <summary>The response headers. Sensitive values are already masked by the sender (CONTRACTS §4).</summary>
    public IReadOnlyList<HeaderLine> Headers => _result.Response?.Headers ?? [];

    /// <summary>The body, or null when the send produced no response.</summary>
    public BodyView? Body { get; }

    /// <summary>The lines of the body in a mode. Preview has none: HTML is never rendered (ADR-0022).</summary>
    public IReadOnlyList<IReadOnlyList<ViewToken>> Lines(BodyMode mode) => mode switch
    {
        BodyMode.Pretty => _pretty.Value ?? _raw.Value,
        BodyMode.Raw => _raw.Value,
        _ => [],
    };

    /// <summary>True when Pretty shows JSON. False means it falls back to the raw text, and the page says so.</summary>
    public bool PrettyIsJson => _pretty.Value is not null;

    private static OutcomeKind ParseOutcome(string outcome) => outcome switch
    {
        "completed" => OutcomeKind.Completed,
        "blocked" => OutcomeKind.Blocked,
        "timeout" => OutcomeKind.Timeout,
        "networkError" => OutcomeKind.NetworkError,
        "invalidRequest" => OutcomeKind.InvalidRequest,
        "tooLarge" => OutcomeKind.TooLarge,
        _ => OutcomeKind.Unknown,
    };
}

/// <summary>
/// One response body as the page holds it. Binary (<c>base64</c>) is never shown as text: the page says how many bytes it is.
/// </summary>
public sealed class BodyView
{
    public BodyView(ResponseBodyDto body)
    {
        Encoding = body.Encoding;
        Content = body.Content;
        SizeBytes = body.SizeBytes;
        Truncated = body.Truncated;
        ContentType = body.ContentType;
    }

    public string Encoding { get; }

    /// <summary>The text (for <c>utf8</c>) or the base64 text (for <c>base64</c>, which the page never shows).</summary>
    public string Content { get; }

    public long SizeBytes { get; }

    public bool Truncated { get; }

    public string? ContentType { get; }

    public bool IsBinary => Encoding != "utf8";

    /// <summary>JSON when the type says so, or when no type is declared and the text starts like a JSON value.</summary>
    public bool LooksJson
    {
        get
        {
            if (ContentType is { Length: > 0 } type)
            {
                return type.Contains("json", StringComparison.OrdinalIgnoreCase);
            }

            var trimmed = Content.AsSpan().TrimStart();
            return trimmed.Length > 0 && trimmed[0] is '{' or '[';
        }
    }
}
