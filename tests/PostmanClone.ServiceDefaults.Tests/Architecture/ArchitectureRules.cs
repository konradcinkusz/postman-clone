using System.Text.RegularExpressions;

namespace PostmanClone.ServiceDefaults.Tests.Architecture;

/// <summary>One breach of one rule: which rule, where it was found, and what was wrong.</summary>
public sealed record Violation(string Rule, string Where, string Detail)
{
    public override string ToString() => $"{Rule} {Where}: {Detail}";
}

/// <summary>
/// One endpoint of a service, read from its <c>EndpointDataSource</c>: one record per (route, method).
/// <paramref name="AllowsAnonymous"/> is <c>IAllowAnonymous</c> metadata; <paramref name="HasAuthorizeMetadata"/> is
/// <c>IAuthorizeData</c> metadata (RequireAuthorization, or an [Authorize] on a page); <paramref name="FallbackRequiresUser"/>
/// is true when the host's fallback policy needs a signed-in user, which applies to every endpoint with neither.
/// <paramref name="FrameworkOwned"/> marks the framework's own endpoints (static assets, the static-file fallback, the
/// Blazor runtime); R5 does not judge them, because the framework owns their access (see <see cref="RouteInventory"/>).
/// </summary>
public sealed record RouteFact(
    string Service,
    string Method,
    string Route,
    bool AllowsAnonymous,
    bool HasAuthorizeMetadata,
    bool FallbackRequiresUser,
    bool FrameworkOwned = false);

/// <summary>
/// The architecture rules of WP25, as pure functions over plain data. The tests run them on the real repository and
/// on a deliberately broken in-memory sample, so each rule is shown to fail (the fixtures are never committed as code).
/// Rule numbers match the work package: R1 kernel, R2 services isolated, R3 Web, R4 manifests, R5 authorization,
/// R6 isolation matrix, R7 schema, R8 Dockerfiles, R9 resilience, R10 package versions.
/// </summary>
public static partial class ArchitectureRules
{
    public const string KernelNamespace = "PostmanClone.ServiceDefaults";
    public const string KernelProject = "PostmanClone.ServiceDefaults";

    /// <summary>Maximum lines of a service manifest (P9: "under about 120"; the rule's hard line is 150).</summary>
    public const int MaxManifestLines = 150;

    /// <summary>The unauthenticated routes of the estate. Health probes (kernel) and the Web's anonymous pages and forms.</summary>
    public static readonly IReadOnlySet<string> AnonymousAllowList = new HashSet<string>(StringComparer.Ordinal)
    {
        "/health",
        "/alive",
        // Web: the sign-in, registration and sign-out form posts and their pages (antiforgery, ADR-0005).
        "/sign-in",
        "/register",
        "/sign-out",
        // Web: public legal pages, readable before sign-in.
        "/legal/privacy",
        "/legal/terms",
    };

    private const string Prefix = "PostmanClone.";

    // ---------------------------------------------------------------- R1: the kernel holds no domain

    /// <summary>
    /// R1. The kernel references no other project and no domain assembly, and declares no type outside its own
    /// namespace (no entity, DTO or service-specific type; P2).
    /// </summary>
    public static IReadOnlyList<Violation> KernelReferencesNoDomainType(
        IEnumerable<string> projectReferences,
        IEnumerable<string> referencedAssemblies,
        IEnumerable<string> typeNamespaces)
    {
        var found = new List<Violation>();
        foreach (var reference in projectReferences)
        {
            found.Add(new Violation("R1", KernelProject, $"project reference '{reference}' (the kernel references no project)"));
        }

        foreach (var assembly in referencedAssemblies.Where(IsPostmanCloneName).Where(name => !IsKernel(name)))
        {
            found.Add(new Violation("R1", KernelProject, $"references the domain assembly '{assembly}'"));
        }

        foreach (var ns in typeNamespaces.Where(ns => IsPostmanCloneName(ns) && !IsKernelNamespace(ns)).Distinct())
        {
            found.Add(new Violation("R1", KernelProject, $"declares a type in '{ns}', outside the kernel namespace"));
        }

        return found;
    }

    // ---------------------------------------------------------------- R2: services do not reach into each other

    /// <summary>
    /// R2. A service project references no other service project and uses no other service's namespace, so no service
    /// reaches another's DbContext (P3). <paramref name="otherServices"/> are the assembly or project names of the others.
    /// </summary>
    public static IReadOnlyList<Violation> ServiceIsolated(
        string service,
        IEnumerable<string> referencedNames,
        IEnumerable<string> sourceFileTexts,
        IEnumerable<string> otherServices)
    {
        var others = otherServices.ToHashSet(StringComparer.Ordinal);
        var found = new List<Violation>();
        foreach (var name in referencedNames.Where(others.Contains))
        {
            found.Add(new Violation("R2", service, $"references the service '{name}' (a service reaches only the other's HTTP contract)"));
        }

        foreach (var other in others)
        {
            // "PostmanClone.CollectionsService" → the namespace "PostmanClone.CollectionsService", as a using or a qualified name.
            var pattern = new Regex(@"\b" + Regex.Escape(other) + @"\b");
            if (sourceFileTexts.Any(text => pattern.IsMatch(text) && !OnlyInComments(text, other)))
            {
                found.Add(new Violation("R2", service, $"uses the namespace of the service '{other}' in its code"));
            }
        }

        return found;
    }

    // ---------------------------------------------------------------- R3: the Web references only the kernel

    /// <summary>R3. The Web's project references are exactly the kernel, and it names no service assembly.</summary>
    public static IReadOnlyList<Violation> WebReferencesOnlyTheKernel(
        IEnumerable<string> projectReferences,
        IEnumerable<string> referencedAssemblies,
        IEnumerable<string> serviceAssemblies)
    {
        var found = new List<Violation>();
        foreach (var reference in projectReferences.Where(reference => reference != KernelProject))
        {
            found.Add(new Violation("R3", "PostmanClone.Web", $"project reference '{reference}' (the Web references only ServiceDefaults)"));
        }

        var services = serviceAssemblies.ToHashSet(StringComparer.Ordinal);
        foreach (var assembly in referencedAssemblies.Where(services.Contains))
        {
            found.Add(new Violation("R3", "PostmanClone.Web", $"references the service assembly '{assembly}' at run time"));
        }

        return found;
    }

    // ---------------------------------------------------------------- R4: manifests

    /// <summary>
    /// R4. A service's Program.cs is a manifest: at most <see cref="MaxManifestLines"/> lines, and it calls
    /// <c>AddServiceDefaults</c> and <c>MapDefaultEndpoints</c> itself, so every service shows the kernel in its manifest.
    /// </summary>
    public static IReadOnlyList<Violation> ManifestShape(string service, string programText)
    {
        var found = new List<Violation>();
        var lines = programText.Split('\n').Length;
        if (lines > MaxManifestLines)
        {
            found.Add(new Violation("R4", service, $"Program.cs is {lines} lines (limit {MaxManifestLines})"));
        }

        if (!programText.Contains("AddServiceDefaults(", StringComparison.Ordinal))
        {
            found.Add(new Violation("R4", service, "Program.cs does not call AddServiceDefaults"));
        }

        if (!programText.Contains("MapDefaultEndpoints(", StringComparison.Ordinal))
        {
            found.Add(new Violation("R4", service, "Program.cs does not call MapDefaultEndpoints"));
        }

        return found;
    }

    // ---------------------------------------------------------------- R5: every route is authorized or allow-listed

    /// <summary>
    /// R5. Every application endpoint is either on <paramref name="allowList"/> and anonymous, or authorized: it carries
    /// authorization metadata, or it has no anonymous marker and the fallback policy requires a signed-in user. The
    /// framework's own endpoints are exempt (<see cref="RouteFact.FrameworkOwned"/>).
    /// </summary>
    public static IReadOnlyList<Violation> RoutesAuthorized(IEnumerable<RouteFact> routes, IReadOnlySet<string> allowList)
    {
        var found = new List<Violation>();
        foreach (var route in routes)
        {
            var where = $"{route.Service} {route.Method} {route.Route}";
            if (allowList.Contains(route.Route))
            {
                if (!route.AllowsAnonymous)
                {
                    found.Add(new Violation("R5", where, "is on the anonymous allow-list but requires authorization"));
                }

                continue;
            }

            if (route.AllowsAnonymous)
            {
                found.Add(new Violation("R5", where, "is anonymous and not on the allow-list"));
                continue;
            }

            if (route.FrameworkOwned)
            {
                continue;
            }

            var authorized = route.HasAuthorizeMetadata || route.FallbackRequiresUser;
            if (!authorized)
            {
                found.Add(new Violation("R5", where, "has no authorization (no metadata, and no fallback policy requiring a user)"));
            }
        }

        return found;
    }

    // ---------------------------------------------------------------- R6: the isolation matrix

    /// <summary>
    /// The test name a route with an id parameter must have, <c>Scoping_&lt;METHOD&gt;_&lt;route&gt;</c>: the path without
    /// <c>/api/v1/</c>, each parameter reduced to its name (<c>{id:guid}</c> is <c>id</c>), segments joined with <c>_</c>.
    /// </summary>
    public static string ScopingTestName(string method, string route)
    {
        var path = route.StartsWith("/api/v1/", StringComparison.Ordinal) ? route["/api/v1/".Length..] : route.TrimStart('/');
        var parts = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => ParameterName().Replace(segment, "$1"));
        return $"Scoping_{method}_{string.Join('_', parts)}";
    }

    /// <summary>
    /// R6. Every route with an <c>{id}</c> parameter has a declared scoping test with the matching name. Returns the
    /// missing names as violations. <paramref name="declaredTests"/> are the test method names of the service's tests.
    /// </summary>
    public static IReadOnlyList<Violation> ScopingTestsDeclared(IEnumerable<RouteFact> routes, IEnumerable<string> declaredTests)
    {
        var declared = declaredTests.ToHashSet(StringComparer.Ordinal);
        var found = new List<Violation>();
        foreach (var route in routes.Where(route => route.Route.Contains('{', StringComparison.Ordinal)))
        {
            var expected = ScopingTestName(route.Method, route.Route);
            if (!declared.Contains(expected))
            {
                found.Add(new Violation("R6", route.Service, $"no scoping test '{expected}' for {route.Method} {route.Route}"));
            }
        }

        return found;
    }

    /// <summary>The test method names declared in the sources of a test project (<c>Scoping_*</c> only, for R6).</summary>
    public static IReadOnlySet<string> DeclaredScopingTests(IEnumerable<string> sourceFileTexts) =>
        sourceFileTexts
            .SelectMany(text => ScopingDeclaration().Matches(WithoutCommentLines(text)).Select(match => match.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The source with its comment-only lines removed, so a commented-out test is not a declared one.</summary>
    private static string WithoutCommentLines(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // ---------------------------------------------------------------- R7: the schema is migrated, never ensured

    /// <summary>
    /// R7. <c>EnsureCreated</c> is called only on the InMemory branch: each call is preceded, within four lines, by an
    /// <c>IsInMemory</c> check. Comments are not calls.
    /// </summary>
    public static IReadOnlyList<Violation> EnsureCreatedOnlyInMemory(string fileName, string text)
    {
        var lines = text.Split('\n');
        var found = new List<Violation>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("//", StringComparison.Ordinal) || !line.Contains("EnsureCreated", StringComparison.Ordinal))
            {
                continue;
            }

            var guarded = Enumerable.Range(Math.Max(0, index - 4), Math.Min(4, index) + 1)
                .Any(earlier => lines[earlier].Contains("IsInMemory", StringComparison.Ordinal));
            if (!guarded)
            {
                found.Add(new Violation("R7", $"{fileName}:{index + 1}", "EnsureCreated outside the InMemory branch"));
            }
        }

        return found;
    }

    // ---------------------------------------------------------------- R8: Dockerfiles (P6)

    /// <summary>
    /// R8. A Dockerfile's build stage and its runtime stage use the TargetFramework's major version; the image listens on
    /// 8080; the last stage runs as a named non-root user.
    /// </summary>
    public static IReadOnlyList<Violation> DockerfileSatisfiesP6(string service, string dockerfileText, string targetFramework)
    {
        var found = new List<Violation>();
        var major = TargetFrameworkMajor().Match(targetFramework);
        if (!major.Success)
        {
            return [new Violation("R8", service, $"unrecognised TargetFramework '{targetFramework}'")];
        }

        var expectedMajor = major.Groups["major"].Value;
        var froms = FromLines().Matches(dockerfileText);
        if (froms.Count < 2)
        {
            found.Add(new Violation("R8", service, "is not multi-stage (a build stage and a runtime stage)"));
        }

        foreach (Match fromLine in froms)
        {
            var image = fromLine.Groups["image"].Value;
            if (image.Contains("/dotnet/", StringComparison.Ordinal) && image.Contains(':') && !image.Contains(":" + expectedMajor + ".", StringComparison.Ordinal))
            {
                found.Add(new Violation("R8", service, $"base image '{image}' is not the {expectedMajor} major of {targetFramework}"));
            }
        }

        var runtime = froms.Count == 0 ? string.Empty : dockerfileText[froms[^1].Index..];
        if (!Regex.IsMatch(dockerfileText, @"^\s*EXPOSE\s+8080\b", RegexOptions.Multiline))
        {
            found.Add(new Violation("R8", service, "does not EXPOSE 8080"));
        }

        var users = Regex.Matches(runtime, @"^\s*USER\s+(\S+)", RegexOptions.Multiline);
        if (users.Count == 0)
        {
            found.Add(new Violation("R8", service, "the runtime stage has no USER"));
        }
        else
        {
            var user = users[^1].Groups[1].Value;
            if (user is "root" or "0" or "0:0")
            {
                found.Add(new Violation("R8", service, $"the runtime stage runs as '{user}'"));
            }
        }

        return found;
    }

    // ---------------------------------------------------------------- R9: one resilience handler

    /// <summary>
    /// R9. Resilience handlers are added in the kernel only, exactly once (P2: one HttpClient handler, the kernel's).
    /// <paramref name="sources"/> are (file, text) pairs for every source file under <c>src/</c>.
    /// </summary>
    public static IReadOnlyList<Violation> SingleResilienceHandler(IEnumerable<(string File, string Text)> sources)
    {
        var found = new List<Violation>();
        var kernelHandlers = 0;
        foreach (var (file, text) in sources)
        {
            foreach (Match match in ResilienceCall().Matches(text))
            {
                if (file.Replace('\\', '/').Contains("/" + KernelProject + "/", StringComparison.Ordinal))
                {
                    kernelHandlers++;
                }
                else
                {
                    found.Add(new Violation("R9", file, $"adds a second resilience handler ({match.Value})"));
                }
            }
        }

        if (kernelHandlers != 1)
        {
            found.Add(new Violation("R9", KernelProject, $"adds {kernelHandlers} resilience handlers (exactly one is required)"));
        }

        return found;
    }

    // ---------------------------------------------------------------- R10: no floating versions

    /// <summary>
    /// R10. Every package version is an exact version (no ranges, no wildcards). Central management means the
    /// versions live only in Directory.Packages.props, and projects name no version.
    /// </summary>
    public static IReadOnlyList<Violation> ExactPackageVersions(string propsXml, IEnumerable<(string File, string Text)> projects)
    {
        var found = new List<Violation>();
        foreach (Match package in PackageVersion().Matches(propsXml))
        {
            var version = package.Groups["version"];
            if (!version.Success)
            {
                found.Add(new Violation("R10", package.Groups["id"].Value, "has no Version"));
            }
            else if (!ExactVersion().IsMatch(version.Value))
            {
                found.Add(new Violation("R10", package.Groups["id"].Value, $"floating version '{version.Value}'"));
            }
        }

        foreach (var (file, text) in projects)
        {
            foreach (Match reference in PackageReferenceWithVersion().Matches(text))
            {
                found.Add(new Violation("R10", file, $"PackageReference '{reference.Groups["id"].Value}' names a version (set it in Directory.Packages.props)"));
            }
        }

        return found;
    }

    // ---------------------------------------------------------------- helpers

    private static bool IsPostmanCloneName(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    private static bool IsKernel(string name) => name == KernelProject;

    private static bool IsKernelNamespace(string ns) =>
        ns == KernelNamespace || ns.StartsWith(KernelNamespace + ".", StringComparison.Ordinal);

    /// <summary>True when every occurrence of <paramref name="name"/> in the text is on a comment line.</summary>
    private static bool OnlyInComments(string text, string name)
    {
        var pattern = new Regex(@"\b" + Regex.Escape(name) + @"\b");
        return text.Split('\n')
            .Where(line => pattern.IsMatch(line))
            .All(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) || line.TrimStart().StartsWith("///", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\{(\w+)[^}]*\}")]
    private static partial Regex ParameterName();

    [GeneratedRegex(@"public\s+(?:async\s+)?(?:Task|ValueTask|void)\s+(?<name>Scoping_\w+)\s*\(")]
    private static partial Regex ScopingDeclaration();

    [GeneratedRegex(@"^\s*FROM\s+(?:--\S+\s+)*(?<image>\S+)", RegexOptions.Multiline)]
    private static partial Regex FromLines();

    [GeneratedRegex(@"net(?<major>\d+)\.\d+")]
    private static partial Regex TargetFrameworkMajor();

    [GeneratedRegex(@"\.(AddStandardResilienceHandler|AddResilienceHandler|AddStandardHedgingHandler)\s*\(")]
    private static partial Regex ResilienceCall();

    [GeneratedRegex(@"<PackageVersion\s+Include=""(?<id>[^""]+)""(?:\s+Version=""(?<version>[^""]*)"")?")]
    private static partial Regex PackageVersion();

    [GeneratedRegex(@"^\d+(\.\d+){1,3}(-[0-9A-Za-z][0-9A-Za-z.\-]*)?$")]
    private static partial Regex ExactVersion();

    [GeneratedRegex(@"<PackageReference\s+Include=""(?<id>[^""]+)""[^>]*\bVersion=""")]
    private static partial Regex PackageReferenceWithVersion();
}
