using System.Text.RegularExpressions;

namespace PostmanClone.Web.Environments;

/// <summary>
/// The limits and the name and key rules of CONTRACTS §3, checked in the browser before a save so the page can point at the field.
/// The service is the authority: its attributes and its handler decide, and a parity test runs both on the same inputs, so a
/// change on one side fails the other.
/// </summary>
public static class EnvironmentRules
{
    /// <summary>The name's length (CONTRACTS §3: 1..100).</summary>
    public const int NameMaxLength = 100;

    /// <summary>The key's length (CONTRACTS §3: 1..100).</summary>
    public const int KeyMaxLength = 100;

    /// <summary>A value's length, plain or typed secret (CONTRACTS §3: at most 8192).</summary>
    public const int ValueMaxLength = 8192;

    /// <summary>Variables in one environment (CONTRACTS §3: at most 200).</summary>
    public const int VariablesPerEnvironment = 200;

    /// <summary>Environments in one workspace (CONTRACTS §3: at most 50). The service answers 409 past it.</summary>
    public const int EnvironmentsPerWorkspace = 50;

    /// <summary>The key's pattern, the same one the service's attribute uses.</summary>
    public const string KeyPattern = @"^[A-Za-z0-9_.\-]+$";

    /// <summary>A name must hold at least one non-space character (the service's rule, CONTRACTS §3).</summary>
    private static readonly Regex NameHasText = new(@"\S", RegexOptions.CultureInvariant);

    private static readonly Regex KeyShape = new(KeyPattern, RegexOptions.CultureInvariant);

    /// <summary>The message for a name the service would refuse, or null when it is acceptable.</summary>
    public static string? NameProblem(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return EnvironmentMessages.NameRequired;
        }

        if (name.Length > NameMaxLength)
        {
            return EnvironmentMessages.NameTooLong;
        }

        return NameHasText.IsMatch(name) ? null : EnvironmentMessages.NameRequired;
    }

    /// <summary>The message for a key the service would refuse, or null when it is acceptable.</summary>
    public static string? KeyProblem(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return EnvironmentMessages.KeyRequired;
        }

        if (key.Length > KeyMaxLength)
        {
            return EnvironmentMessages.KeyTooLong;
        }

        return KeyShape.IsMatch(key) ? null : EnvironmentMessages.KeyShape;
    }

    /// <summary>The message for a value longer than the service keeps, or null.</summary>
    public static string? ValueProblem(string? value) =>
        value is not null && value.Length > ValueMaxLength ? EnvironmentMessages.ValueTooLong : null;
}
