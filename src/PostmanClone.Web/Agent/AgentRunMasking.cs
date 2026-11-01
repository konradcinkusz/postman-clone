using System.Text.RegularExpressions;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Agent;

/// <summary>
/// The Web's name-based masking of a run the agent sent (CONTRACTS §4, A2.5), applied before the record leaves the Web. The agent has
/// already masked the resolved values; this masks what a name says is secret, in the headers, the query, the URL, the body fields and
/// the auth block, and in the response's headers and redirects. A value that is only a <c>{{variable}}</c> reference is kept: the
/// reference is what the history stores, and it holds no value. The mask is the same <c>••••••</c> the service writes.
/// </summary>
public static class AgentRunMasking
{
    public const string Mask = "••••••";

    private static readonly string[] SensitiveHeaders = ["Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "X-Auth-Token"];

    /// <summary>Names that contain one of these are secret, whatever else they say (CONTRACTS §4).</summary>
    private static readonly string[] SensitiveFragments = ["token", "secret", "password", "apikey", "api_key", "api-key"];

    private static readonly Regex Reference = new(@"^\{\{[^{}]+\}\}$", RegexOptions.CultureInvariant);

    private static readonly Regex QueryPair = new(@"(?<=[?&])([^=&#]+)=([^&#]*)", RegexOptions.CultureInvariant);

    public static bool IsSensitiveName(string? name) =>
        !string.IsNullOrEmpty(name)
        && (Array.Exists(SensitiveHeaders, header => string.Equals(header, name, StringComparison.OrdinalIgnoreCase))
            || Array.Exists(SensitiveFragments, fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

    /// <summary>A value is masked when its name is secret, unless the whole value is one variable reference.</summary>
    public static string MaskValue(string? name, string value) =>
        IsSensitiveName(name) && value.Length > 0 && !Reference.IsMatch(value) ? Mask : value;

    /// <summary>Masks the query of a URL by the names of its parameters. The rest of the URL is unchanged.</summary>
    public static string MaskUrl(string url)
    {
        if (url.IndexOf('?', StringComparison.Ordinal) < 0)
        {
            return url;
        }

        return QueryPair.Replace(url, match => $"{match.Groups[1].Value}={MaskValue(Uri.UnescapeDataString(match.Groups[1].Value), match.Groups[2].Value)}");
    }

    public static HistoryRecordWriteDto MaskRecord(HistoryRecordWriteDto record) =>
        record with
        {
            Request = MaskRequest(record.Request),
            Result = MaskResult(record.Result),
        };

    private static RequestDefinition MaskRequest(RequestDefinition request) =>
        request with
        {
            Url = MaskUrl(request.Url),
            Params = MaskPairs(request.Params),
            Headers = MaskPairs(request.Headers),
            Body = request.Body is null ? null : request.Body with { Fields = MaskPairs(request.Body.Fields) },
            Auth = request.Auth is null
                ? null
                : request.Auth with
                {
                    BearerToken = MaskOptional(request.Auth.BearerToken),
                    Password = MaskOptional(request.Auth.Password),
                    ApiKeyValue = MaskOptional(request.Auth.ApiKeyValue),
                },
        };

    private static ExecutionResultDto MaskResult(ExecutionResultDto result) =>
        result with
        {
            FinalUrl = MaskUrl(result.FinalUrl),
            Response = result.Response is null
                ? null
                : result.Response with
                {
                    Headers = result.Response.Headers.Select(line => line with { Value = MaskValue(line.Name, line.Value) }).ToList(),
                },
            Redirects = result.Redirects.Select(hop => hop with { Location = MaskUrl(hop.Location) }).ToList(),
        };

    private static List<KeyValue>? MaskPairs(List<KeyValue>? pairs) =>
        pairs?.Select(pair => pair with { Value = MaskValue(pair.Key, pair.Value) }).ToList();

    private static string? MaskOptional(string? value) =>
        string.IsNullOrEmpty(value) || Reference.IsMatch(value) ? value : Mask;
}
