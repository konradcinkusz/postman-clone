using System.Text.RegularExpressions;
using PostmanClone.TestKit;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace PostmanClone.AppHost.Tests;

/// <summary>compose.yaml read as data, with its anchors and merge keys expanded. Values are the text as written (no interpolation).</summary>
internal sealed class ComposeFile
{
    private readonly Dictionary<object, object> _services;

    private ComposeFile(Dictionary<object, object> services) => _services = services;

    public static ComposeFile Load()
    {
        using var reader = new StreamReader(Path.Combine(RepoRoot.Find(), "compose.yaml"));
        var parser = new MergingParser(new Parser(reader));
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(parser);
        return new ComposeFile((Dictionary<object, object>)root["services"]);
    }

    public IEnumerable<string> ServiceNames => _services.Keys.Select(k => (string)k);

    public Dictionary<string, string> Environment(string service)
    {
        var map = (Dictionary<object, object>)Service(service)["environment"];
        return map.ToDictionary(p => (string)p.Key, p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }

    public IReadOnlyList<string> Entrypoint(string service) =>
        ((List<object>)Service(service)["entrypoint"]).Select(o => (string)o).ToList();

    public IReadOnlyList<string> Volumes(string service) =>
        ((List<object>)Service(service)["volumes"]).Select(o => (string)o).ToList();

    /// <summary>The image reference with a <c>${VAR:-default}</c> reduced to its default.</summary>
    public string ImageDefault(string service)
    {
        var image = (string)Service(service)["image"];
        var match = Regex.Match(image, @"^\$\{[A-Z_]+:-(?<default>[^}]+)\}$");
        return match.Success ? match.Groups["default"].Value : image;
    }

    private Dictionary<object, object> Service(string name) => (Dictionary<object, object>)_services[name];
}
