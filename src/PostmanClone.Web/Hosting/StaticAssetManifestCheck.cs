using System.Globalization;
using System.Text.Json;

namespace PostmanClone.Web.Hosting;

/// <summary>
/// Checks the publish-time static asset manifest against the files on disk, before the Web answers a request (OI-27).
/// A listed file that is missing, or whose length differs from the <c>Content-Length</c> the manifest declares, is
/// served as an empty 200 and the browser never starts the circuit. The check stops the host instead, with the names of
/// the files at fault. Development (build) manifests are not checked: they point at source folders.
/// </summary>
public static class StaticAssetManifestCheck
{
    public const string ManifestFileName = "PostmanClone.Web.staticwebassets.endpoints.json";
    private const int MaximumNamedProblems = 5;

    /// <summary>Throws when the manifest beside the assembly lists a file that is missing or the wrong size.</summary>
    public static void Verify(string manifestDirectory, string webRoot)
    {
        var manifestPath = Path.Combine(manifestDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        var problems = Problems(File.ReadAllText(manifestPath), webRoot).Distinct(StringComparer.Ordinal).ToList();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The static assets do not match their manifest ({problems.Count} file(s)); the Web will not start. " +
                "Publish the Web again, or run it from its publish folder. First: " +
                string.Join("; ", problems.Take(MaximumNamedProblems)));
        }
    }

    /// <summary>The problems in a publish manifest, one line each. A build manifest, or one with no sizes, has none.</summary>
    public static IReadOnlyList<string> Problems(string manifestJson, string webRoot)
    {
        using var document = JsonDocument.Parse(manifestJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("ManifestType", out var type) || type.GetString() != "Publish"
            || !root.TryGetProperty("Endpoints", out var endpoints))
        {
            return [];
        }

        var problems = new List<string>();
        foreach (var endpoint in endpoints.EnumerateArray())
        {
            if (!endpoint.TryGetProperty("AssetFile", out var assetFileElement))
            {
                continue;
            }

            var assetFile = assetFileElement.GetString() ?? string.Empty;
            var declared = DeclaredLength(endpoint);
            if (declared is null)
            {
                continue;
            }

            var path = Path.Combine(webRoot, assetFile);
            if (!File.Exists(path))
            {
                problems.Add($"{assetFile} is missing");
                continue;
            }

            var actual = new FileInfo(path).Length;
            if (actual != declared.Value)
            {
                problems.Add($"{assetFile} is {actual} bytes, the manifest says {declared.Value}");
            }
        }

        return problems;
    }

    private static long? DeclaredLength(JsonElement endpoint)
    {
        if (!endpoint.TryGetProperty("ResponseHeaders", out var headers))
        {
            return null;
        }

        foreach (var header in headers.EnumerateArray())
        {
            if (header.TryGetProperty("Name", out var name)
                && string.Equals(name.GetString(), "Content-Length", StringComparison.OrdinalIgnoreCase)
                && header.TryGetProperty("Value", out var value)
                && long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            {
                return length;
            }
        }

        return null;
    }
}
