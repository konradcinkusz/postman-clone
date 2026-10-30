using System.Text.Json;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>Reads the parts of a problem response the tests assert on: the stable <c>code</c> and the validation members.</summary>
public static class ResponseReading
{
    /// <summary>The <c>code</c> extension of a problem response (CONTRACTS §0), or null when the body has none.</summary>
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The member names under <c>errors</c> of a validation problem (CONTRACTS §0).</summary>
    public static async Task<string[]> ErrorMembersAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("errors").EnumerateObject().Select(member => member.Name).ToArray();
    }
}
