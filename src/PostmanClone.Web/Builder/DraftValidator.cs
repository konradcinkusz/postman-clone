using System.Text.RegularExpressions;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Builder;

/// <summary>One rule a draft breaks: the field it names (<c>request.params[0].key</c>, the service's paths), the code and a message.</summary>
public sealed record DraftIssue(string Field, string Code, string Message);

/// <summary>
/// The client mirror of the service's <c>RequestDefinitionValidator</c> (CONTRACTS §1, §4; ADR-0006, ADR-0017). It answers the
/// same fields and codes for the same definition, so the page refuses what the service would refuse, and the parity test
/// (<c>ValidatorParityTests</c>) fails when the two disagree. The service stays the boundary; this only saves a round trip.
/// Every rule here is marked "client mirror" in its name so a reader can find its twin in CollectionsService.
/// </summary>
public static class DraftValidator
{
    public const string InvalidCode = "validation_failed";

    public const string SecretLiteralCode = "secret_literal_not_allowed";

    /// <summary>The service's limits (CONTRACTS §1, §2). They are copies, pinned by the parity test.</summary>
    public const int MaximumUrlLength = 8192;

    public const int MaximumRows = 100;

    public const int MaximumKeyLength = 256;

    public const int MaximumValueLength = 8192;

    public const int MaximumDescriptionLength = 500;

    public const int MaximumRawLength = 1_048_576;

    /// <summary>1.5 MB of JSON (CONTRACTS §2). The service answers <c>413 payload_too_large</c>, which the page reports as a size note.</summary>
    public const int MaximumJsonBytes = 1_572_864;

    private static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    private static readonly string[] BodyTypes = ["none", "raw", "formUrlEncoded", "formData"];

    private static readonly string[] RawLanguages = ["json", "text", "xml", "html", "javascript"];

    private static readonly string[] AuthTypes = ["none", "bearer", "basic", "apiKey"];

    private static readonly string[] ApiKeyLocations = ["header", "query"];

    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>A secret field is empty or exactly one <c>{{name}}</c> reference, with optional surrounding whitespace (ADR-0006).</summary>
    private static readonly Regex SecretReference = new(
        @"^\s*\{\{[A-Za-z0-9_.\-]{1,100}\}\}\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Every rule the definition breaks, with the service's field paths. Nothing stops at the first failure.</summary>
    public static IReadOnlyList<DraftIssue> Validate(RequestDefinition definition)
    {
        var issues = new List<DraftIssue>();

        if (!Methods.Contains(definition.Method, StringComparer.Ordinal))
        {
            issues.Add(Issue("request.method", "The method must be GET, POST, PUT, PATCH, DELETE, HEAD or OPTIONS."));
        }

        CheckLength(definition.Url, 1, MaximumUrlLength, "request.url", "URL", issues);
        CheckRows(definition.Params ?? [], "request.params", headers: false, issues);
        CheckRows(definition.Headers ?? [], "request.headers", headers: true, issues);
        CheckBody(definition.Body ?? new RequestBody("none", null, null, []), issues);
        CheckAuth(definition.Auth ?? new RequestAuth("none", null, null, null, null, null, null), issues);
        return issues;
    }

    /// <summary>True when a secret field holds no literal: it is empty, or exactly one reference (client mirror, ADR-0006).</summary>
    public static bool IsSecretReference(string? value) => string.IsNullOrEmpty(value) || SecretReference.IsMatch(value);

    private static void CheckRows(IReadOnlyList<KeyValue> rows, string field, bool headers, List<DraftIssue> issues)
    {
        if (rows.Count > MaximumRows)
        {
            issues.Add(Issue(field, $"At most {MaximumRows} rows are allowed."));
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var path = $"{field}[{index}]";
            CheckLength(row.Key, 1, MaximumKeyLength, $"{path}.key", "name", issues);
            CheckLength(row.Value, 0, MaximumValueLength, $"{path}.value", "value", issues);

            if (row.Description is { } description)
            {
                CheckLength(description, 0, MaximumDescriptionLength, $"{path}.description", "description", issues);
            }

            // A header name becomes one line of the outgoing request, so a line break in it would inject a second header.
            if (headers && row.Key.IndexOfAny(LineBreaks) >= 0)
            {
                issues.Add(Issue($"{path}.key", "A header name cannot contain a line break."));
            }
        }
    }

    private static void CheckBody(RequestBody body, List<DraftIssue> issues)
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

        if (body.Raw is { } raw && raw.Length > MaximumRawLength)
        {
            issues.Add(Issue("request.body.raw", $"The raw body is at most {MaximumRawLength} characters."));
        }

        var fields = body.Fields ?? [];
        if (body.Type is "formUrlEncoded" or "formData")
        {
            CheckRows(fields, "request.body.fields", headers: false, issues);
        }
        else if (fields.Count > 0)
        {
            issues.Add(Issue("request.body.fields", "Fields apply only to formUrlEncoded and formData bodies."));
        }
    }

    private static void CheckAuth(RequestAuth auth, List<DraftIssue> issues)
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
            issues.Add(Issue("request.auth.apiKeyIn", "The API key location must be header, query or none."));
        }
    }

    /// <summary>A secret is never stored as a literal (ADR-0006). The message never repeats the value.</summary>
    private static void CheckSecret(string? value, string field, List<DraftIssue> issues)
    {
        if (!IsSecretReference(value))
        {
            issues.Add(new DraftIssue(field, SecretLiteralCode, BuilderMessages.SecretLiteral));
        }
    }

    private static void CheckLength(string value, int minimum, int maximum, string field, string name, List<DraftIssue> issues)
    {
        if (value.Length < minimum || value.Length > maximum)
        {
            issues.Add(Issue(field, minimum == 0
                ? $"The {name} is at most {maximum} characters."
                : $"The {name} must be {minimum} to {maximum} characters."));
        }
    }

    private static DraftIssue Issue(string field, string message) => new(field, InvalidCode, message);
}
