using PostmanClone.ExecutionService.Sending.Assembly;
using PostmanClone.ServiceDefaults.Telemetry;

namespace PostmanClone.ExecutionService.Sending.Execution;

/// <summary>Builds one outbound <see cref="HttpRequestMessage"/> from the assembled headers and body.</summary>
internal static class WireRequest
{
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        "Content-Encoding",
        "Content-Language",
        "Content-Location",
        "Content-Range",
        "Content-Disposition",
    };

    public static HttpRequestMessage Build(string method, string uri, IReadOnlyList<HeaderLine> headers, byte[]? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri);
        // The target is the user's own choice: it is not traced (ServiceDefaults OutboundTelemetry).
        request.Options.Set(OutboundTelemetry.UserTarget, true);
        var content = body is null ? null : new ByteArrayContent(body);
        foreach (var header in headers)
        {
            // Entity headers belong to the content when there is one; everything else is a request header.
            if (content is not null && IsContentHeader(header.Name) && content.Headers.TryAddWithoutValidation(header.Name, header.Value))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }

        request.Content = content;
        return request;
    }

    public static bool IsContentHeader(string name) => ContentHeaders.Contains(name);
}
