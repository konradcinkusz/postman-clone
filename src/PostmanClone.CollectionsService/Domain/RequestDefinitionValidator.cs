using System.Text.Json;
using System.Text.RegularExpressions;
using PostmanClone.CollectionsService.Contracts;

namespace PostmanClone.CollectionsService.Domain;

/// <summary>The sizes of a <c>RequestDefinition</c> (CONTRACTS §1, §2, §4). Exceeding one is refused, never truncated.</summary>
public static class RequestDefinitionLimits
{
    public const int MaxUrlLength = 8192;

    /// <summary>Rows per key/value list: parameters, headers and form fields.</summary>
    public const int MaxRows = 100;

    public const int MaxKeyLength = 256;

    public const int MaxValueLength = 8192;

    public const int MaxDescriptionLength = 500;

    public const int MaxRawLength = 1_048_576;

    /// <summary>1.5 MB (CONTRACTS §2): the UTF-8 JSON of the whole definition, in bytes. Larger is <c>413 payload_too_large</c>.</summary>
    public const int MaxJsonBytes = 1_572_864;
}

/// <summary>One rule a definition breaks: the field it names (<c>request.headers[0].key</c>), the stable code and a message.</summary>
public sealed record RequestIssue(string Field, string Code, string Message);

/// <summary>The outcome of validating a definition: every issue found, or none when the definition is valid.</summary>
public sealed record RequestValidation(IReadOnlyList<RequestIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public bool HasSecretLiteral => Issues.Any(issue => issue.Code == RequestDefinitionValidator.SecretLiteralCode);
}

/// <summary>
/// The rules of a <c>RequestDefinition</c> (CONTRACTS §1, ADR-0006). Pure logic with no HTTP: it reports issues by field, and
/// the caller decides the answer. WP16 and WP17 (importers) call this class too, so a definition is judged the same way
/// wherever it comes from. Null lists and objects count as empty; <see cref="Normalize"/> gives the stored form.
/// </summary>
public static class RequestDefinitionValidator
{
    public const string InvalidCode = "validation_failed";

    public const string SecretLiteralCode = "secret_literal_not_allowed";

    private static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    private static readonly string[] BodyTypes = ["none", "raw", "formUrlEncoded", "formData"];

    private static readonly string[] RawLanguages = ["json", "text", "xml", "html", "javascript"];

    private static readonly string[] AuthTypes = ["none", "bearer", "basic", "apiKey"];

    private static readonly string[] ApiKeyLocations = ["header", "query"];

    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>A secret field is empty or exactly one <c>{{name}}</c> reference, with optional surrounding whitespace.</summary>
    private static readonly Regex SecretReference = new(
        @"^\s*\{\{[A-Za-z0-9_.\-]{1,100}\}\}\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>The definition as it is stored: null lists become empty, null objects become <c>none</c>, a null value becomes "".</summary>
    public static RequestDefinition Normalize(RequestDefinition definition) => definition with
    {
        Params = Rows(definition.Params),
        Headers = Rows(definition.Headers),
        Body = NormalizeBody(definition.Body),
        Auth = definition.Auth ?? new RequestAuth("none", null, null, null, null, null, null),
    };

    /// <summary>Every rule the definition breaks. Nothing stops at the first failure, so the caller can name them all.</summary>
    public static RequestValidation Validate(RequestDefinition definition)
    {
        var normalized = Normalize(definition);
        var issues = new List<RequestIssue>();

        if (!Methods.Contains(normalized.Method, StringComparer.Ordinal))
        {
            issues.Add(Issue("request.method", "The method must be GET, POST, PUT, PATCH, DELETE, HEAD or OPTIONS."));
        }

        CheckLength(normalized.Url, 1, RequestDefinitionLimits.MaxUrlLength, "request.url", "URL", issues);
        CheckRows(normalized.Params!, "request.params", headers: false, issues);
        CheckRows(normalized.Headers!, "request.headers", headers: true, issues);
        CheckBody(normalized.Body!, issues);
        CheckAuth(normalized.Auth!, issues);
        return new RequestValidation(issues);
    }

    /// <summary>The UTF-8 JSON size of the stored form of the definition, in bytes.</summary>
    public static int SerializedSize(RequestDefinition definition) =>
        JsonSerializer.SerializeToUtf8Bytes(Normalize(definition), JsonSerializerOptions.Web).Length;

    /// <summary>True when the definition's JSON is larger than <see cref="RequestDefinitionLimits.MaxJsonBytes"/>.</summary>
    public static bool ExceedsMaxSize(RequestDefinition definition) => SerializedSize(definition) > RequestDefinitionLimits.MaxJsonBytes;

    private static IReadOnlyList<KeyValue> Rows(IReadOnlyList<KeyValue>? rows) =>
        (rows ?? Array.Empty<KeyValue>()).Select(row => row with { Key = row.Key ?? string.Empty, Value = row.Value ?? string.Empty }).ToList();

    private static RequestBody NormalizeBody(RequestBody? body) => body is null
        ? new RequestBody("none", null, null, [])
        : body with { Fields = Rows(body.Fields) };

    private static void CheckRows(IReadOnlyList<KeyValue> rows, string field, bool headers, List<RequestIssue> issues)
    {
        if (rows.Count > RequestDefinitionLimits.MaxRows)
        {
            issues.Add(Issue(field, $"At most {RequestDefinitionLimits.MaxRows} rows are allowed."));
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var path = $"{field}[{index}]";
            CheckLength(row.Key, 1, RequestDefinitionLimits.MaxKeyLength, $"{path}.key", "name", issues);
            CheckLength(row.Value, 0, RequestDefinitionLimits.MaxValueLength, $"{path}.value", "value", issues);

            if (row.Description is { } description)
            {
                CheckLength(description, 0, RequestDefinitionLimits.MaxDescriptionLength, $"{path}.description", "description", issues);
            }

            // A header name becomes one line of the outgoing request, so a line break in it would inject a second header.
            if (headers && row.Key.IndexOfAny(LineBreaks) >= 0)
            {
                issues.Add(Issue($"{path}.key", "A header name cannot contain a line break."));
            }
        }
    }

    private static void CheckBody(RequestBody body, List<RequestIssue> issues)
    {
        if (!BodyTypes.Contains(body.Type, StringComparer.Ordinal))
        {
            issues.Add(Issue("request.body.type", "The body type must be none, raw, formUrlEncoded or formData."));
        }

        var isRaw = body.Type == "raw";
        if (isRaw)
        {
            if (body.RawLanguage is null || !RawLanguages.Contains(body.RawLanguage, StringComparer.Ordinal))
            {
                issues.Add(Issue("request.body.rawLanguage", "A raw body needs a language: json, text, xml, html or javascript."));
            }
        }
        else if (body.RawLanguage is not null)
        {
            issues.Add(Issue("request.body.rawLanguage", "A language applies only to a raw body."));
        }

        if (body.Raw is { Length: > 0 } && !isRaw)
        {
            issues.Add(Issue("request.body.raw", "Raw text applies only to a raw body."));
        }

        if (body.Raw is { } raw && raw.Length > RequestDefinitionLimits.MaxRawLength)
        {
            issues.Add(Issue("request.body.raw", $"The raw body is at most {RequestDefinitionLimits.MaxRawLength} characters."));
        }

        if (body.Type is "formUrlEncoded" or "formData")
        {
            CheckRows(body.Fields!, "request.body.fields", headers: false, issues);
        }
        else if (body.Fields!.Count > 0)
        {
            issues.Add(Issue("request.body.fields", "Fields apply only to formUrlEncoded and formData bodies."));
        }
    }

    private static void CheckAuth(RequestAuth auth, List<RequestIssue> issues)
    {
        if (!AuthTypes.Contains(auth.Type, StringComparer.Ordinal))
        {
            issues.Add(Issue("request.auth.type", "The auth type must be none, bearer, basic or apiKey."));
        }

        CheckSecret(auth.BearerToken, "request.auth.bearerToken", issues);
        CheckSecret(auth.Password, "request.auth.password", issues);
        CheckSecret(auth.ApiKeyValue, "request.auth.apiKeyValue", issues);

        if (auth.ApiKeyIn is not null && !ApiKeyLocations.Contains(auth.ApiKeyIn, StringComparer.Ordinal))
        {
            issues.Add(Issue("request.auth.apiKeyIn", "The API key location must be header, query or null."));
        }
    }

    /// <summary>A secret is never stored as a literal (ADR-0006). The message never repeats the value.</summary>
    private static void CheckSecret(string? value, string field, List<RequestIssue> issues)
    {
        if (string.IsNullOrEmpty(value) || SecretReference.IsMatch(value))
        {
            return;
        }

        issues.Add(new RequestIssue(field, SecretLiteralCode, "Enter a {{variable}} reference, not a literal value."));
    }

    private static void CheckLength(string value, int minimum, int maximum, string field, string name, List<RequestIssue> issues)
    {
        if (value.Length < minimum || value.Length > maximum)
        {
            issues.Add(Issue(field, minimum == 0
                ? $"The {name} is at most {maximum} characters."
                : $"The {name} must be {minimum} to {maximum} characters."));
        }
    }

    private static RequestIssue Issue(string field, string message) => new(field, InvalidCode, message);
}
