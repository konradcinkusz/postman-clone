namespace PostmanClone.Sending.Assembly;

/// <summary>
/// Builds the request URL from the expanded URL text and the enabled query pairs. The fragment is dropped (a browser
/// never sends it). The query the URL already carries is kept as written; the pairs follow it in their order, duplicates
/// included, each name and value percent-encoded (RFC 3986 unreserved characters pass through).
/// </summary>
public static class UrlBuilder
{
    public static string Build(string expandedUrl, IReadOnlyList<KeyValuePair<string, string>> query, int maxLength)
    {
        if (expandedUrl.Any(IsControl))
        {
            throw new RequestAssemblyException(RequestAssemblyException.InvalidUrl, "The URL contains a control character.");
        }

        var withoutFragment = expandedUrl;
        var fragment = withoutFragment.IndexOf('#');
        if (fragment >= 0)
        {
            withoutFragment = withoutFragment[..fragment];
        }

        var question = withoutFragment.IndexOf('?');
        var path = question >= 0 ? withoutFragment[..question] : withoutFragment;
        var parts = new List<string>();
        if (question >= 0 && question < withoutFragment.Length - 1)
        {
            parts.Add(withoutFragment[(question + 1)..]);
        }

        parts.AddRange(query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var url = parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";

        if (url.Length > maxLength)
        {
            throw new RequestAssemblyException(RequestAssemblyException.UrlTooLong, "The URL is longer than the sender allows.");
        }

        // A host is required: on Linux a leading slash parses as an absolute file URI. The scheme allow-list is the sender's.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.IsFile || uri.IsUnc || uri.Host.Length == 0)
        {
            throw new RequestAssemblyException(RequestAssemblyException.InvalidUrl, "The URL is empty or is not an absolute URL with a host.");
        }

        return url;
    }

    private static bool IsControl(char c) => c < 0x20 || c == 0x7F;
}
