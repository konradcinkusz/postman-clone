using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Interop.Curl;

/// <summary>
/// Writes a saved request as a cURL command for a POSIX shell (WP17, CONTRACTS §2). Every value is single-quoted, so the
/// command is text a shell reads literally, and <see cref="CurlParser"/> reads it back to the same request. Disabled rows
/// are not written, because cURL has no disabled rows. Variable references (<c>{{name}}</c>) are written as they are.
/// </summary>
public static class CurlGenerator
{
    public static string Generate(RequestDefinition definition)
    {
        var request = RequestDefinitionValidator.Normalize(definition);
        var auth = request.Auth;
        var options = new List<string>
        {
            request.Method == "HEAD" ? "--head" : $"--request {CurlQuoting.Quote(request.Method)}",
            $"--url {CurlQuoting.Quote(UrlWithQuery(request))}",
        };

        var headers = (request.Headers ?? []).Where(row => row.Enabled).Select(row => HeaderLine(row.Key, row.Value)).ToList();
        headers.AddRange(AuthHeaders(auth));
        options.AddRange(headers.Select(line => $"--header {CurlQuoting.Quote(line)}"));

        if (auth is { Type: "basic" } && (auth.Username is not null || auth.Password is not null))
        {
            var userinfo = (auth.Username ?? string.Empty) + (auth.Password is null ? string.Empty : ":" + auth.Password);
            options.Add($"--user {CurlQuoting.Quote(userinfo)}");
        }

        options.AddRange(BodyOptions(request.Body));
        return "curl " + string.Join(" \\\n  ", options);
    }

    /// <summary>The URL with the enabled query parameters (and an API key in the query) appended as they would be typed.</summary>
    private static string UrlWithQuery(RequestDefinition request)
    {
        var pairs = (request.Params ?? [])
            .Where(row => row.Enabled)
            .Select(row => CurlQuoting.Escape(row.Key) + "=" + CurlQuoting.Escape(row.Value))
            .ToList();

        if (request.Auth is { Type: "apiKey", ApiKeyIn: "query" } auth && !string.IsNullOrEmpty(auth.ApiKeyName))
        {
            pairs.Add(CurlQuoting.Escape(auth.ApiKeyName) + "=" + CurlQuoting.Escape(auth.ApiKeyValue ?? string.Empty));
        }

        return pairs.Count == 0 ? request.Url : CurlQuoting.AppendQuery(request.Url, string.Join('&', pairs));
    }

    /// <summary>The header the auth helper implies: a bearer token, or an API key in a header. Basic auth is written as <c>--user</c>.</summary>
    private static List<string> AuthHeaders(RequestAuth? auth)
    {
        var lines = new List<string>();
        if (auth is { Type: "bearer" } && !string.IsNullOrWhiteSpace(auth.BearerToken))
        {
            lines.Add("Authorization: Bearer " + auth.BearerToken.Trim());
        }

        if (auth is { Type: "apiKey", ApiKeyIn: "header" } && !string.IsNullOrEmpty(auth.ApiKeyName))
        {
            lines.Add(HeaderLine(auth.ApiKeyName, auth.ApiKeyValue ?? string.Empty));
        }

        return lines;
    }

    /// <summary>
    /// The options of the body. A raw body is sent as written, and a form-urlencoded body as its encoded text (so it comes back
    /// as a raw body with the same bytes). Form fields are sent as literal text fields, so a value starting with <c>@</c> stays text.
    /// </summary>
    private static IEnumerable<string> BodyOptions(RequestBody? body)
    {
        switch (body?.Type)
        {
            case "raw":
                yield return $"--data-raw {CurlQuoting.Quote(body.Raw ?? string.Empty)}";
                break;
            case "formUrlEncoded":
                var pairs = (body.Fields ?? [])
                    .Where(row => row.Enabled)
                    .Select(row => CurlQuoting.Escape(row.Key) + "=" + CurlQuoting.Escape(row.Value))
                    .ToList();
                if (pairs.Count > 0)
                {
                    yield return $"--data-raw {CurlQuoting.Quote(string.Join('&', pairs))}";
                }

                break;
            case "formData":
                foreach (var field in (body.Fields ?? []).Where(row => row.Enabled))
                {
                    yield return $"--form-string {CurlQuoting.Quote(field.Key + "=" + field.Value)}";
                }

                break;
        }
    }

    /// <summary>
    /// <c>Name: value</c>, or <c>Name;</c> for an empty value: cURL reads <c>Name:</c> as "remove this header", so the empty
    /// value is written in the form that sends it.
    /// </summary>
    private static string HeaderLine(string name, string value) =>
        value.Length == 0 ? name + ";" : name + ": " + value;
}
