using System.Text.RegularExpressions;

namespace PostmanClone.Sending.Assembly;

/// <summary>
/// Replaces <c>{{name}}</c> references (CONTRACTS §1) in a single pass. A value that itself contains <c>{{x}}</c> is
/// inserted as text and is not expanded again. A name that is not defined stays as written and is reported in
/// <see cref="Unresolved"/>. Names are case-sensitive.
/// </summary>
/// <remarks>
/// One instance serves one request: it collects the references of every field, so <see cref="Used"/> covers the whole
/// request and <see cref="SecretMasker"/> can hide every secret that went into it (ADR-0012).
/// </remarks>
public sealed partial class VariableInterpolator
{
    private readonly Dictionary<string, ResolvedValue> _variables;
    private readonly List<ResolvedValue> _used = [];
    private readonly List<string> _unresolved = [];

    public VariableInterpolator(IReadOnlyDictionary<string, ResolvedValue> variables)
    {
        _variables = variables.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Every value inserted so far, secret and non-secret, in the order used.</summary>
    public IReadOnlyList<ResolvedValue> Used => _used;

    /// <summary>Names that were referenced but not defined, in the order first seen, without duplicates.</summary>
    public IReadOnlyList<string> Unresolved => _unresolved;

    public string Expand(string template) => Reference().Replace(template, Substitute);

    private string Substitute(Match match)
    {
        var name = match.Groups[1].Value;
        if (_variables.TryGetValue(name, out var resolved))
        {
            _used.Add(resolved);
            return resolved.Value;
        }

        if (!_unresolved.Contains(name, StringComparer.Ordinal))
        {
            _unresolved.Add(name);
        }

        return match.Value;
    }

    [GeneratedRegex(@"\{\{([A-Za-z0-9_.\-]{1,100})\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Reference();
}
