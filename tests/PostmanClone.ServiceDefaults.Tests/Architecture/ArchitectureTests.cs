extern alias collections;
extern alias environments;
extern alias execution;
extern alias web;

using System.Text.RegularExpressions;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Architecture;

/// <summary>
/// The architectural and tenancy rules made mechanical (WP25; architecture-standards P2, P3, P4, P6, P9 and the
/// security review's authorization structure). Each rule has two facts: it holds on the repository, and it fails on a
/// deliberately broken in-memory sample, so a rule that can never fail is caught here.
/// </summary>
public sealed class ArchitectureTests
{
    private const string Kernel = "PostmanClone.ServiceDefaults";
    private const string CollectionsService = "PostmanClone.CollectionsService";
    private const string EnvironmentsService = "PostmanClone.EnvironmentsService";
    private const string ExecutionService = "PostmanClone.ExecutionService";
    private const string WebProject = "PostmanClone.Web";

    /// <summary>The three domain services. Each owns one database and reaches the others only over HTTP (P3).</summary>
    private static readonly string[] DomainServices = [CollectionsService, EnvironmentsService, ExecutionService];

    /// <summary>The test project of each service, where its scoping tests live (R6).</summary>
    private static readonly IReadOnlyDictionary<string, string> TestProjects = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CollectionsService"] = "PostmanClone.CollectionsService.Tests",
        ["EnvironmentsService"] = "PostmanClone.EnvironmentsService.Tests",
        ["ExecutionService"] = "PostmanClone.ExecutionService.Tests",
    };

    // ---------------------------------------------------------------- R1

    [Fact]
    public void R1_The_kernel_references_no_domain_type()
    {
        var kernel = typeof(PostmanClone.ServiceDefaults.Authentication.RequireActiveWorkspaceFilter).Assembly;
        var namespaces = RepoFiles.TypeNamespaces(kernel);
        Assert.Contains("PostmanClone.ServiceDefaults.Authentication", namespaces);

        var violations = ArchitectureRules.KernelReferencesNoDomainType(
            RepoFiles.ProjectReferences(RepoFiles.Read("src", Kernel, Kernel + ".csproj")),
            RepoFiles.ReferencedAssemblyNames(kernel),
            namespaces);

        Assert.Empty(violations);
    }

    [Fact]
    public void R1_fails_on_a_kernel_that_references_a_domain_type()
    {
        var violations = ArchitectureRules.KernelReferencesNoDomainType(
            [CollectionsService],
            [CollectionsService, "System.Runtime"],
            ["PostmanClone.ServiceDefaults.Authentication", "PostmanClone.CollectionsService.Domain"]);

        Assert.Equal(3, violations.Count); // the project reference, the assembly, and the domain namespace
        Assert.All(violations, violation => Assert.Equal("R1", violation.Rule));
    }

    // ---------------------------------------------------------------- R2

    [Fact]
    public void R2_No_service_references_or_uses_another_service()
    {
        var violations = new List<Violation>();
        foreach (var service in DomainServices)
        {
            var others = DomainServices.Where(other => other != service).ToArray();
            var projectReferences = RepoFiles.ProjectReferences(RepoFiles.Read("src", service, service + ".csproj"));
            var assemblies = RepoFiles.ReferencedAssemblyNames(AssemblyOf(service));
            var sources = RepoFiles.CSharpSources("src", service).Select(source => source.Text).ToList();

            Assert.NotEmpty(sources);
            violations.AddRange(ArchitectureRules.ServiceIsolated(service, projectReferences.Concat(assemblies), sources, others));
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void R2_fails_when_a_service_uses_another_services_context()
    {
        var violations = ArchitectureRules.ServiceIsolated(
            ExecutionService,
            [EnvironmentsService],
            ["using PostmanClone.EnvironmentsService.Data;\nvar db = new EnvironmentsDbContext();"],
            [EnvironmentsService]);

        Assert.Equal(2, violations.Count); // the reference, and the namespace in code
        Assert.All(violations, violation => Assert.Equal("R2", violation.Rule));
    }

    [Fact]
    public void R2_does_not_count_a_mention_in_a_comment()
    {
        var violations = ArchitectureRules.ServiceIsolated(
            ExecutionService,
            [],
            ["// The contract is documented in PostmanClone.EnvironmentsService, not reached through its code.\n/// <see cref=\"PostmanClone.EnvironmentsService.Api\"/>"],
            [EnvironmentsService]);

        Assert.Empty(violations);
    }

    // ---------------------------------------------------------------- R3

    [Fact]
    public void R3_The_Web_references_only_the_kernel()
    {
        var web = typeof(web::Program).Assembly;
        var violations = ArchitectureRules.WebReferencesOnlyTheKernel(
            RepoFiles.ProjectReferences(RepoFiles.Read("src", WebProject, WebProject + ".csproj")),
            RepoFiles.ReferencedAssemblyNames(web),
            DomainServices);

        Assert.Empty(violations);
        Assert.Contains(Kernel, RepoFiles.ProjectReferences(RepoFiles.Read("src", WebProject, WebProject + ".csproj")));
    }

    [Fact]
    public void R3_fails_when_the_Web_references_a_service()
    {
        var violations = ArchitectureRules.WebReferencesOnlyTheKernel(
            [Kernel, EnvironmentsService],
            [EnvironmentsService],
            DomainServices);

        Assert.Equal(2, violations.Count); // the project reference, and the service assembly at run time
        Assert.All(violations, violation => Assert.Equal("R3", violation.Rule));
    }

    // ---------------------------------------------------------------- R4

    [Fact]
    public void R4_Every_manifest_is_short_and_calls_the_kernel()
    {
        var manifests = new[] { CollectionsService, EnvironmentsService, ExecutionService, WebProject };
        foreach (var manifest in manifests)
        {
            var text = RepoFiles.Read("src", manifest, "Program.cs");
            Assert.Empty(ArchitectureRules.ManifestShape(manifest, text));
        }
    }

    [Fact]
    public void R4_fails_on_a_long_manifest_without_the_kernel_calls()
    {
        var violations = ArchitectureRules.ManifestShape("Fixture", string.Concat(Enumerable.Repeat("builder.Services.AddThing();\n", 200)));

        Assert.Equal(3, violations.Count); // too long, no AddServiceDefaults, no MapDefaultEndpoints
        Assert.All(violations, violation => Assert.Equal("R4", violation.Rule));
    }

    // ---------------------------------------------------------------- R5

    [Fact]
    public void R5_Every_route_is_authorized_or_on_the_anonymous_allow_list()
    {
        var routes = RouteInventory.All;
        foreach (var service in new[] { "CollectionsService", "EnvironmentsService", "ExecutionService", "Web" })
        {
            Assert.NotEmpty(RouteInventory.For(service));
        }

        Assert.Contains(routes, route => route.Route == "/health" && route.AllowsAnonymous);
        Assert.Contains(routes, route => route.Route == "/api/v1/execute");

        Holds(ArchitectureRules.RoutesAuthorized(routes, ArchitectureRules.AnonymousAllowList));
    }

    [Fact]
    public void R5_fails_on_a_route_with_no_authorization_and_on_an_unlisted_anonymous_route()
    {
        IReadOnlyList<RouteFact> routes =
        [
            new("Fixture", "GET", "/api/v1/widgets", AllowsAnonymous: false, HasAuthorizeMetadata: false, FallbackRequiresUser: false),
            new("Fixture", "GET", "/api/v1/open", AllowsAnonymous: true, HasAuthorizeMetadata: false, FallbackRequiresUser: true),
            new("Fixture", "GET", "/health", AllowsAnonymous: false, HasAuthorizeMetadata: true, FallbackRequiresUser: true),
        ];

        var violations = ArchitectureRules.RoutesAuthorized(routes, ArchitectureRules.AnonymousAllowList);

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, violation => violation.Where.EndsWith("GET /api/v1/widgets", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Where.EndsWith("GET /api/v1/open", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Where.EndsWith("GET /health", StringComparison.Ordinal));
    }

    [Fact]
    public void R5_does_not_judge_the_frameworks_own_endpoints_but_still_judges_every_application_route()
    {
        IReadOnlyList<RouteFact> routes =
        [
            new("Fixture", "GET", "app.css", AllowsAnonymous: false, HasAuthorizeMetadata: false, FallbackRequiresUser: false, FrameworkOwned: true),
            new("Fixture", "GET", "/_blazor", AllowsAnonymous: false, HasAuthorizeMetadata: false, FallbackRequiresUser: false, FrameworkOwned: true),
            new("Fixture", "GET", "/api/v1/widgets", AllowsAnonymous: false, HasAuthorizeMetadata: false, FallbackRequiresUser: false, FrameworkOwned: false),
        ];

        var violation = Assert.Single(ArchitectureRules.RoutesAuthorized(routes, ArchitectureRules.AnonymousAllowList));
        Assert.Contains("/api/v1/widgets", violation.Where, StringComparison.Ordinal);
    }

    [Fact]
    public void R5_accepts_a_route_authorized_by_the_fallback_policy_alone()
    {
        IReadOnlyList<RouteFact> routes = [new("Fixture", "GET", "/api/v1/widgets", AllowsAnonymous: false, HasAuthorizeMetadata: false, FallbackRequiresUser: true)];

        Assert.Empty(ArchitectureRules.RoutesAuthorized(routes, ArchitectureRules.AnonymousAllowList));
    }

    // ---------------------------------------------------------------- R6

    [Theory]
    [InlineData("CollectionsService")]
    [InlineData("EnvironmentsService")]
    [InlineData("ExecutionService")]
    public void R6_Every_id_route_has_a_scoping_test_named_by_convention(string service)
    {
        var routes = RouteInventory.For(service);
        Assert.Contains(routes, route => route.Route.Contains('{', StringComparison.Ordinal));

        var declared = ArchitectureRules.DeclaredScopingTests(
            RepoFiles.CSharpSources("tests", TestProjects[service]).Select(source => source.Text));
        Assert.NotEmpty(declared);

        Assert.Empty(ArchitectureRules.ScopingTestsDeclared(routes, declared));
    }

    [Fact]
    public void R6_The_scoping_name_is_the_route_without_the_api_prefix_and_with_parameters_reduced_to_their_names()
    {
        Assert.Equal("Scoping_GET_collections_id", ArchitectureRules.ScopingTestName("GET", "/api/v1/collections/{id:guid}"));
        Assert.Equal("Scoping_GET_requests_id_export_curl", ArchitectureRules.ScopingTestName("GET", "/api/v1/requests/{id:guid}/export/curl"));
        Assert.Equal("Scoping_DELETE_history", ArchitectureRules.ScopingTestName("DELETE", "/api/v1/history"));
        Assert.Equal("Scoping_POST_environments_id_resolve", ArchitectureRules.ScopingTestName("POST", "/api/v1/environments/{id}/resolve"));
    }

    [Fact]
    public void R6_fails_when_an_id_route_has_no_scoping_test()
    {
        IReadOnlyList<RouteFact> routes =
        [
            new("Fixture", "GET", "/api/v1/widgets/{id:guid}", AllowsAnonymous: false, HasAuthorizeMetadata: true, FallbackRequiresUser: true),
            new("Fixture", "GET", "/api/v1/widgets", AllowsAnonymous: false, HasAuthorizeMetadata: true, FallbackRequiresUser: true),
        ];

        var violations = ArchitectureRules.ScopingTestsDeclared(routes, ["Scoping_GET_gadgets_id"]);

        var violation = Assert.Single(violations);
        Assert.Contains("Scoping_GET_widgets_id", violation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void R6_ignores_a_scoping_test_that_is_commented_out()
    {
        var declared = ArchitectureRules.DeclaredScopingTests(
            ["// public async Task Scoping_GET_widgets_id(string storage)\npublic async Task Scoping_GET_gadgets_id(string storage) { }"]);

        Assert.DoesNotContain("Scoping_GET_widgets_id", declared);
        Assert.Contains("Scoping_GET_gadgets_id", declared);
    }

    // ---------------------------------------------------------------- R7

    [Fact]
    public void R7_EnsureCreated_is_called_only_on_the_InMemory_branch()
    {
        var sources = RepoFiles.CSharpSources("src");
        Assert.NotEmpty(sources);

        var violations = sources.SelectMany(source => ArchitectureRules.EnsureCreatedOnlyInMemory(source.File, source.Text)).ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void R7_fails_on_EnsureCreated_outside_the_InMemory_branch_and_passes_inside_it()
    {
        var unguarded = ArchitectureRules.EnsureCreatedOnlyInMemory("Fixture.cs", "await context.Database.EnsureCreatedAsync(ct);");
        var guarded = ArchitectureRules.EnsureCreatedOnlyInMemory(
            "Fixture.cs",
            "if (context.Database.IsInMemory())\n{\n    await context.Database.EnsureCreatedAsync(ct);\n}");

        Assert.Single(unguarded);
        Assert.Empty(guarded);
    }

    // ---------------------------------------------------------------- R8

    [Fact]
    public void R8_Every_Dockerfile_satisfies_P6()
    {
        var targetFramework = TargetFrameworkOf(RepoFiles.Read("Directory.Build.props"));
        var dockerfiles = RepoFiles.Files(["src"], "Dockerfile");
        Assert.True(dockerfiles.Count >= 4, $"expected a Dockerfile per composition root, found {dockerfiles.Count}");

        var violations = dockerfiles.SelectMany(file => ArchitectureRules.DockerfileSatisfiesP6(file.File, file.Text, targetFramework)).ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void R8_fails_on_a_Dockerfile_with_the_wrong_runtime_major_no_port_and_root()
    {
        const string dockerfile = """
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            RUN dotnet publish
            FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
            USER root
            """;

        var violations = ArchitectureRules.DockerfileSatisfiesP6("Fixture/Dockerfile", dockerfile, "net10.0");

        Assert.Equal(3, violations.Count); // the runtime major, no EXPOSE 8080, and the root user
        Assert.All(violations, violation => Assert.Equal("R8", violation.Rule));
    }

    // ---------------------------------------------------------------- R9

    [Fact]
    public void R9_The_kernel_adds_the_one_resilience_handler_and_no_service_adds_another()
    {
        Assert.Empty(ArchitectureRules.SingleResilienceHandler(RepoFiles.CSharpSources("src")));
    }

    [Fact]
    public void R9_fails_on_a_second_handler_outside_the_kernel_and_on_a_kernel_without_one()
    {
        var second = ArchitectureRules.SingleResilienceHandler(
            [("src/PostmanClone.ServiceDefaults/Extensions.cs", "http.AddStandardResilienceHandler();"),
             ("src/PostmanClone.Web/Setup.cs", "http.AddStandardResilienceHandler();")]);
        var none = ArchitectureRules.SingleResilienceHandler([]);

        Assert.Single(second);
        Assert.Equal("R9", second[0].Rule);
        Assert.Single(none);
        Assert.Contains("adds 0", none[0].Detail, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- R10

    [Fact]
    public void R10_Every_package_version_is_exact_and_declared_centrally()
    {
        var props = RepoFiles.Read("Directory.Packages.props");
        Assert.Contains("<PackageVersion", props, StringComparison.Ordinal);

        var projects = RepoFiles.Files(["src"], "*.csproj").Concat(RepoFiles.Files(["tests"], "*.csproj")).ToList();

        Assert.Empty(ArchitectureRules.ExactPackageVersions(props, projects));
    }

    [Fact]
    public void R10_fails_on_a_range_a_wildcard_and_a_version_named_in_a_project()
    {
        const string props = """
            <Project>
              <ItemGroup>
                <PackageVersion Include="A" Version="[1.0,2.0)" />
                <PackageVersion Include="B" Version="1.*" />
                <PackageVersion Include="C" Version="2.1.0" />
              </ItemGroup>
            </Project>
            """;

        var violations = ArchitectureRules.ExactPackageVersions(
            props,
            [("tests/Fixture.csproj", "<ItemGroup><PackageReference Include=\"D\" Version=\"1.0.0\" /></ItemGroup>")]);

        Assert.Equal(3, violations.Count); // two floating versions and one version named in a project
        Assert.All(violations, violation => Assert.Equal("R10", violation.Rule));
    }

    // ---------------------------------------------------------------- helpers

    private static System.Reflection.Assembly AssemblyOf(string service) => service switch
    {
        CollectionsService => typeof(collections::Program).Assembly,
        EnvironmentsService => typeof(environments::Program).Assembly,
        ExecutionService => typeof(execution::Program).Assembly,
        _ => throw new ArgumentOutOfRangeException(nameof(service), service, "Not a domain service."),
    };

    /// <summary>Passes when there are no violations; otherwise fails listing every one (xunit's own collection output truncates).</summary>
    private static void Holds(IReadOnlyList<Violation> violations)
    {
        if (violations.Count > 0)
        {
            Assert.Fail($"{violations.Count} violation(s):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }
    }

    private static string TargetFrameworkOf(string buildProps)
    {
        var match = Regex.Match(buildProps, @"<TargetFramework>(?<tfm>net\d+\.\d+)</TargetFramework>");
        return match.Success ? match.Groups["tfm"].Value : throw new InvalidOperationException("No TargetFramework in Directory.Build.props.");
    }
}
