using System.Net;
using System.Text.Json;

namespace PostmanClone.Web.Api;

/// <summary>
/// How a backend's refusal is read: the status becomes a <see cref="CollectionOutcome"/> and the RFC 7807 <c>code</c> is taken
/// from the body (CONTRACTS §0). Shared by every typed client, so every backend answers the same way.
/// </summary>
public static class ProblemResponses
{
    public static CollectionOutcome FromStatus(HttpStatusCode status) => (int)status switch
    {
        404 => CollectionOutcome.NotFound,
        403 => CollectionOutcome.Forbidden,
        409 => CollectionOutcome.Conflict,
        400 => CollectionOutcome.Invalid,
        413 => CollectionOutcome.TooLarge,
        429 => CollectionOutcome.RateLimited,
        >= 400 and < 500 => CollectionOutcome.Refused,
        _ => CollectionOutcome.Unavailable,
    };

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                    ? code.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
