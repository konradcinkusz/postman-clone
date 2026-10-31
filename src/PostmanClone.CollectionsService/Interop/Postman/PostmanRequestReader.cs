using System.Text;
using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// Reads one Postman request into a <see cref="RequestDefinition"/>: the method, the URL with its query rows, the headers,
/// the body and the auth helper. A literal secret is replaced by a generated variable and never kept (ADR-0016). Nothing
/// here runs, fetches or evaluates anything in the file.
/// </summary>
internal sealed class PostmanRequestReader(ImportSession session)
{
    private static readonly RequestBody NoBody = new("none", null, null, []);

    private static readonly RequestAuth NoAuth = new("none", null, null, null, null, null, null);

    private static readonly HashSet<string> RawLanguages = new(StringComparer.Ordinal) { "json", "text", "xml", "html", "javascript" };

    public RequestDefinition Read(JsonElement request, string path, string requestName)
    {
        if (request.ValueKind == JsonValueKind.String)
        {
            return new RequestDefinition("GET", RedactQuery(request.GetString() ?? string.Empty, path, requestName), [], [], NoBody, NoAuth);
        }

        if (request.ValueKind != JsonValueKind.Object)
        {
            throw ImportRefusal.Invalid(path, "The request must be an object or a URL.");
        }

        var method = (PostmanJson.Text(request, "method", path) ?? "GET").Trim().ToUpperInvariant();
        var headers = Police(ReadRows(PostmanJson.Array(request, "header", path), $"{path}.header", "header", null), "header", requestName, $"{path}.header");
        var (url, query) = ReadUrl(request, path, requestName);
        var body = ReadBody(request, path, requestName);
        var auth = ReadAuth(request, path, requestName);
        return new RequestDefinition(method, url, query, headers, body, auth);
    }

    /// <summary>
    /// The URL and its query rows. Postman writes the query into <c>raw</c> as well as the <c>query</c> array, so the
    /// query text is taken off the URL when the rows account for it, and the rows carry it instead (the sender appends them).
    /// </summary>
    private (string Url, IReadOnlyList<KeyValue> Query) ReadUrl(JsonElement request, string path, string requestName)
    {
        var urlPath = $"{path}.url";
        if (!PostmanJson.TryGet(request, "url", out var url))
        {
            return (string.Empty, []);
        }

        if (url.ValueKind == JsonValueKind.String)
        {
            return (RedactQuery(url.GetString() ?? string.Empty, urlPath, requestName), []);
        }

        if (url.ValueKind != JsonValueKind.Object)
        {
            throw ImportRefusal.Invalid(urlPath, "The URL must be text or an object.");
        }

        var raw = PostmanJson.Text(url, "raw", urlPath) ?? BuildRaw(url, urlPath);
        var rows = ReadRows(PostmanJson.Array(url, "query", urlPath), $"{urlPath}.query", "query parameter", null);

        // The expected text is the enabled rows as Postman writes them. It is compared with the original values, before any
        // literal is replaced, so the URL and the rows agree on what was there.
        var expected = string.Join("&", rows.Where(row => row.Enabled).Select(row => row.Key + "=" + row.Value));
        var withoutQuery = StripQuery(raw, expected);
        var policed = Police(rows, "query parameter", requestName, $"{urlPath}.query");
        return (RedactQuery(withoutQuery, urlPath, requestName), policed);
    }

    private static string BuildRaw(JsonElement url, string urlPath)
    {
        var protocol = PostmanJson.Text(url, "protocol", urlPath);
        var host = PostmanJson.Parts(url, "host", urlPath, ".");
        var segments = PostmanJson.Parts(url, "path", urlPath, "/");

        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(protocol))
        {
            builder.Append(protocol).Append("://");
        }

        builder.Append(host);
        if (segments.Length > 0)
        {
            builder.Append('/').Append(segments);
        }

        return builder.ToString();
    }

    /// <summary>Removes the rows' query text from the end of the URL, with the separator in front of it, when it is there.</summary>
    private static string StripQuery(string raw, string expected)
    {
        if (expected.Length == 0)
        {
            return raw;
        }

        foreach (var separator in new[] { "?", "&" })
        {
            var suffix = separator + expected;
            if (raw.EndsWith(suffix, StringComparison.Ordinal))
            {
                return raw[..^suffix.Length];
            }
        }

        return raw;
    }

    /// <summary>A query text in the URL itself (when no rows account for it) has its literal values replaced the same way.</summary>
    private string RedactQuery(string text, string path, string requestName)
    {
        var question = text.IndexOf('?', StringComparison.Ordinal);
        if (question < 0)
        {
            return text;
        }

        var fragment = text.IndexOf('#', question);
        var end = fragment < 0 ? text.Length : fragment;
        var segments = text[(question + 1)..end].Split('&');
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var equals = segment.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            var name = segment[..equals];
            var value = segment[(equals + 1)..];
            if (SecretRules.IsSensitive(name) && SecretRules.IsLiteralRowValue(value))
            {
                var variable = session.NewSecretVariable("query");
                Notify(variable, "query parameter", name, requestName, path);
                segments[index] = segment[..(equals + 1)] + SecretRules.Reference(variable);
            }
        }

        var rest = fragment < 0 ? string.Empty : text[fragment..];
        return text[..(question + 1)] + string.Join("&", segments) + rest;
    }

    private RequestBody ReadBody(JsonElement request, string path, string requestName)
    {
        var bodyPath = $"{path}.body";
        var body = PostmanJson.Object(request, "body", path);
        if (body is not { } value)
        {
            return NoBody;
        }

        var mode = PostmanJson.Text(value, "mode", bodyPath) ?? "none";
        switch (mode)
        {
            case "none":
                return NoBody;

            case "raw":
                return new RequestBody(
                    "raw",
                    RawLanguage(value, bodyPath),
                    PostmanJson.Text(value, "raw", bodyPath) ?? string.Empty,
                    []);

            case "urlencoded":
                var fields = ReadRows(PostmanJson.Array(value, "urlencoded", bodyPath), $"{bodyPath}.urlencoded", "form field", null);
                return new RequestBody("formUrlEncoded", null, null, Police(fields, "form field", requestName, $"{bodyPath}.urlencoded"));

            case "formdata":
                // Only text parts are imported. A file part has no text to keep, and a file is never read from the import.
                var parts = ReadRows(
                    PostmanJson.Array(value, "formdata", bodyPath),
                    $"{bodyPath}.formdata",
                    "form field",
                    row => (PostmanJson.Text(row, "type", bodyPath) ?? "text") == "text");
                return new RequestBody("formData", null, null, Police(parts, "form field", requestName, $"{bodyPath}.formdata"));

            default:
                session.Warn("unsupported_body", "This body mode is not imported, so the request is saved without a body.", bodyPath);
                return NoBody;
        }
    }

    private string RawLanguage(JsonElement body, string bodyPath)
    {
        var options = PostmanJson.Object(body, "options", bodyPath);
        var rawOptions = options is { } outer ? PostmanJson.Object(outer, "raw", $"{bodyPath}.options") : null;
        var language = rawOptions is { } inner ? PostmanJson.Text(inner, "language", $"{bodyPath}.options.raw") : null;
        if (language is null)
        {
            return "text";
        }

        var lower = language.Trim().ToLowerInvariant();
        if (RawLanguages.Contains(lower))
        {
            return lower;
        }

        session.Warn("unsupported_body", "This raw language is not known, so the body is imported as text.", bodyPath);
        return "text";
    }

    private RequestAuth ReadAuth(JsonElement request, string path, string requestName)
    {
        var authPath = $"{path}.auth";
        var auth = PostmanJson.Object(request, "auth", path);
        if (auth is not { } value)
        {
            return NoAuth;
        }

        var type = PostmanJson.Text(value, "type", authPath) ?? "noauth";
        switch (type)
        {
            case "noauth":
                return NoAuth;

            case "bearer":
                var token = AuthValue(value, "bearer", "token", authPath, requestName, "bearer_token", secret: true);
                return new RequestAuth("bearer", token ?? string.Empty, null, null, null, null, null);

            case "basic":
                var username = AuthValue(value, "basic", "username", authPath, requestName, "basic_username", secret: false);
                var password = AuthValue(value, "basic", "password", authPath, requestName, "basic_password", secret: true);
                return new RequestAuth("basic", null, username ?? string.Empty, password ?? string.Empty, null, null, null);

            case "apikey":
                var keyName = AuthValue(value, "apikey", "key", authPath, requestName, "api_key_name", secret: false);
                var keyValue = AuthValue(value, "apikey", "value", authPath, requestName, "api_key", secret: true);
                var location = AuthValue(value, "apikey", "in", authPath, requestName, "api_key_location", secret: false) ?? "header";
                if (location is not ("header" or "query"))
                {
                    session.Warn("unsupported_auth", "The API key location is not header or query, so it is sent in the header.", authPath);
                    location = "header";
                }

                return new RequestAuth("apiKey", null, null, null, keyName ?? string.Empty, keyValue ?? string.Empty, location);

            default:
                session.Warn("unsupported_auth", "This auth type is not imported, so the request is saved without auth.", authPath);
                return NoAuth;
        }
    }

    /// <summary>One named value of an auth helper's array (Postman keeps <c>bearer</c>, <c>basic</c> and <c>apikey</c> as lists). A secret one is replaced when it is literal.</summary>
    private string? AuthValue(JsonElement auth, string container, string key, string authPath, string requestName, string label, bool secret)
    {
        var entries = PostmanJson.Array(auth, container, authPath);
        if (entries is not { } list)
        {
            return null;
        }

        var containerPath = $"{authPath}.{container}";
        var index = 0;
        foreach (var entry in list.EnumerateArray())
        {
            var entryPath = $"{containerPath}[{index}]";
            index++;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw ImportRefusal.Invalid(entryPath, "Each auth value must be an object.");
            }

            if (PostmanJson.Text(entry, "key", entryPath) != key)
            {
                continue;
            }

            var value = PostmanJson.Text(entry, "value", entryPath) ?? string.Empty;
            if (secret && SecretRules.IsLiteralAuthValue(value))
            {
                var variable = session.NewSecretVariable(label);
                Notify(variable, "auth value", key, requestName, containerPath);
                return SecretRules.Reference(variable);
            }

            return value;
        }

        return null;
    }

    /// <summary>
    /// The rows of a list, as the file holds them. A row with no name is skipped with a warning, and a row the
    /// <paramref name="include"/> rule drops is skipped with one too. The list is capped at the row limit (CONTRACTS §1).
    /// </summary>
    private List<KeyValue> ReadRows(JsonElement? array, string arrayPath, string label, Func<JsonElement, bool>? include)
    {
        var rows = new List<KeyValue>();
        if (array is not { } list)
        {
            return rows;
        }

        if (list.GetArrayLength() > RequestDefinitionLimits.MaxRows)
        {
            throw ImportRefusal.Invalid(arrayPath, $"At most {RequestDefinitionLimits.MaxRows} rows are allowed.");
        }

        var index = 0;
        foreach (var row in list.EnumerateArray())
        {
            var rowPath = $"{arrayPath}[{index}]";
            index++;
            if (row.ValueKind != JsonValueKind.Object)
            {
                throw ImportRefusal.Invalid(rowPath, "Each row must be an object.");
            }

            if (include is not null && !include(row))
            {
                session.Warn("unsupported_body", "A file part of a form-data body is not imported.", rowPath);
                continue;
            }

            var key = PostmanJson.Text(row, "key", rowPath) ?? string.Empty;
            if (key.Trim().Length == 0)
            {
                session.Warn("row_skipped", $"A {label} with no name was skipped.", rowPath);
                continue;
            }

            var value = PostmanJson.Text(row, "value", rowPath) ?? string.Empty;
            var enabled = !PostmanJson.Flag(row, "disabled", rowPath);
            var description = session.Description(PostmanJson.Description(row, "description"), $"{rowPath}.description");
            rows.Add(new KeyValue(key, value, enabled, description));
        }

        return rows;
    }

    /// <summary>Replaces each literal value of a sensitive row with a generated variable. The row keeps its name and flags.</summary>
    private List<KeyValue> Police(List<KeyValue> rows, string label, string requestName, string arrayPath)
    {
        var result = new List<KeyValue>(rows.Count);
        foreach (var row in rows)
        {
            if (SecretRules.IsSensitive(row.Key) && SecretRules.IsLiteralRowValue(row.Value))
            {
                var variable = session.NewSecretVariable(label.Replace(' ', '_'));
                Notify(variable, label, row.Key, requestName, arrayPath);
                result.Add(row with { Value = SecretRules.Reference(variable) });
            }
            else
            {
                result.Add(row);
            }
        }

        return result;
    }

    /// <summary>The warning for a replaced literal. It names the field and the variable to set, and never the value.</summary>
    private void Notify(string variable, string label, string name, string requestName, string path)
    {
        session.Warn(
            "secret_not_imported",
            "The " + label + " '" + name + "' of '" + requestName + "' held a literal value, which was not stored. Set the variable "
                + SecretRules.Reference(variable) + " in an environment instead.",
            path);
    }
}
