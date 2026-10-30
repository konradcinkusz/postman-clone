using PostmanClone.ExecutionService.Options;

namespace PostmanClone.ExecutionService.Sending.Assembly;

/// <summary>
/// The concrete request, ready for the sender, plus what is needed to show or log it safely. <see cref="Url"/>,
/// <see cref="Headers"/> and <see cref="Body"/> hold the real values: they are for the wire only and must go through
/// <see cref="Masker"/> before anything else sees them.
/// </summary>
public sealed record AssembledRequest(
    string Method,
    string Url,
    IReadOnlyList<HeaderLine> Headers,
    byte[]? Body,
    IReadOnlyList<string> UnresolvedVariables,
    IReadOnlyList<AssemblyWarning> Warnings,
    SecretMasker Masker);

/// <summary>
/// Turns a <see cref="RequestDefinition"/> and the variables of one environment into an <see cref="AssembledRequest"/>.
/// Order: method, expansion of every field, URL query, headers, auth helper, body, then the limits and the masker.
/// A failure throws <see cref="RequestAssemblyException"/> with a stable code and a message that names no value.
/// </summary>
public sealed class RequestAssembler(ExecutionOptions options, IRandomBytes random)
{
    private static readonly HashSet<string> Methods = new(StringComparer.Ordinal)
    {
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "HEAD",
        "OPTIONS",
    };

    public AssembledRequest Assemble(RequestDefinition definition, IReadOnlyDictionary<string, ResolvedValue> variables)
    {
        if (!Methods.Contains(definition.Method))
        {
            throw new RequestAssemblyException(RequestAssemblyException.InvalidMethod, "The method is not one of GET, POST, PUT, PATCH, DELETE, HEAD or OPTIONS.");
        }

        var interpolator = new VariableInterpolator(variables);
        var url = AssemblyText.RequireWellFormed(interpolator.Expand(definition.Url), "URL");
        var query = Pairs(definition.Params, interpolator);
        var headers = HeaderBuilder.Build(definition.Headers, interpolator);

        var auth = AuthApplier.Apply(definition.Auth, interpolator, headers);
        query.AddRange(auth.Query);
        var warnings = new List<AssemblyWarning>(auth.Warnings);

        var body = BodyBuilder.Build(definition.Body, interpolator, random, options.RequestBodyMaxBytes);
        var allHeaders = auth.Headers.ToList();
        if (body.ContentType is { } contentType)
        {
            var hasContentType = allHeaders.Any(header => HeaderBuilder.SameName(header.Name, "Content-Type"));
            if (hasContentType && body.ReplacesContentType)
            {
                allHeaders.RemoveAll(header => HeaderBuilder.SameName(header.Name, "Content-Type"));
                warnings.Add(new AssemblyWarning("content_type_overridden", "The multipart body sets its own Content-Type, so a Content-Type header row was replaced."));
                hasContentType = false;
            }

            if (!hasContentType)
            {
                allHeaders.Add(new HeaderLine("Content-Type", contentType));
            }
        }

        if (allHeaders.Count > options.MaxHeaders)
        {
            throw new RequestAssemblyException(RequestAssemblyException.TooManyHeaders, "The request has more headers than the sender allows.");
        }

        var finalUrl = UrlBuilder.Build(url, query, options.UrlMaxLength);
        var masker = SecretMasker.For(interpolator.Used);
        warnings.AddRange(masker.Warnings);

        return new AssembledRequest(
            definition.Method,
            finalUrl,
            allHeaders,
            body.Content,
            interpolator.Unresolved,
            warnings,
            masker);
    }

    /// <summary>Enabled rows with a non-empty key, expanded. Duplicate keys are kept, in order.</summary>
    private static List<KeyValuePair<string, string>> Pairs(IEnumerable<KeyValueRow> rows, VariableInterpolator variables) =>
        rows
            .Where(row => row.Enabled && row.Key.Length > 0)
            .Select(row => new KeyValuePair<string, string>(
                AssemblyText.RequireWellFormed(variables.Expand(row.Key), "parameter name"),
                AssemblyText.RequireWellFormed(variables.Expand(row.Value), "parameter value")))
            .ToList();
}
