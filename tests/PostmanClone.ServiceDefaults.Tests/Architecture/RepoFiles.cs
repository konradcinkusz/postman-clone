using System.Text.RegularExpressions;
using PostmanClone.TestKit;

namespace PostmanClone.ServiceDefaults.Tests.Architecture;

/// <summary>Reads the repository's own files for the architecture rules. Build output (bin, obj) is never read.</summary>
public static partial class RepoFiles
{
    public static string Root { get; } = RepoRoot.Find();

    public static string Read(params string[] parts) => File.ReadAllText(Path.Combine(Root, Path.Combine(parts)));

    /// <summary>Every C# source file under a folder of the repository, excluding build output.</summary>
    public static IReadOnlyList<(string File, string Text)> CSharpSources(params string[] parts) =>
        Files(parts, "*.cs");

    /// <summary>Every file of a name pattern under a folder of the repository, excluding build output.</summary>
    public static IReadOnlyList<(string File, string Text)> Files(string[] parts, string pattern)
    {
        var folder = Path.Combine(Root, Path.Combine(parts));
        return Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
            .Where(IsSource)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Relative(path), File.ReadAllText(path)))
            .ToList();
    }

    /// <summary>The project names a csproj references (<c>ProjectReference</c>), without the folder or extension.</summary>
    public static IReadOnlyList<string> ProjectReferences(string csprojText) =>
        ProjectReference().Matches(csprojText).Select(match => match.Groups["name"].Value).ToList();

    /// <summary>The assembly names of a loaded assembly's references.</summary>
    public static IReadOnlyList<string> ReferencedAssemblyNames(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToList();

    /// <summary>The namespaces of every type an assembly declares.</summary>
    public static IReadOnlyList<string> TypeNamespaces(System.Reflection.Assembly assembly)
    {
        IEnumerable<Type> types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException exception)
        {
            types = exception.Types.OfType<Type>();
        }

        return types.Select(type => type.Namespace ?? string.Empty).Where(ns => ns.Length > 0).Distinct().ToList();
    }

    private static bool IsSource(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar);
        return !segments.Any(segment => segment is "bin" or "obj");
    }

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    [GeneratedRegex(@"<ProjectReference\s+Include=""[^""]*?(?<name>[^\\/""]+)\.csproj""")]
    private static partial Regex ProjectReference();
}
