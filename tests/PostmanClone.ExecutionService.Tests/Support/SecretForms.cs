using System.Text;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>The forms a secret can take in text (ADR-0012): as written, percent-encoded, form-encoded and base64.</summary>
public static class SecretForms
{
    public static IReadOnlyList<string> Of(string secret)
    {
        var percent = Uri.EscapeDataString(secret);
        return [secret, percent, percent.Replace("%20", "+", StringComparison.Ordinal), Convert.ToBase64String(Encoding.UTF8.GetBytes(secret))];
    }

    /// <summary>A secret that is unique to one test and never written into the source, so the scanner has nothing to find.</summary>
    public static string Fresh(string prefix) => $"{prefix}{Guid.NewGuid():N}";

    /// <summary>The first form of <paramref name="secret"/> found in <paramref name="text"/>, or null when none is present.</summary>
    public static string? FindIn(string text, string secret) =>
        Of(secret).FirstOrDefault(form => text.Contains(form, StringComparison.Ordinal));
}
