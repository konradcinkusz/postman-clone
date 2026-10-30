using System.Text.Json;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>Reads the stable <c>code</c> extension of an RFC 7807 answer (CONTRACTS §0).</summary>
public static class ProblemJson
{
    public static async Task<string?> CodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
