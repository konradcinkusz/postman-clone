using Aspire.Hosting.ApplicationModel;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The Blazor Web App (BLAZOR-WEB-APP §4). It is the only externally reachable service in the local composition, and
/// it reads the authservice health through service discovery. The port is fixed (README §3, the launch profile).
/// </summary>
internal static class WebResource
{
    public const string Name = "web";
    public const int Port = 5310;

    /// <summary>The Web is referenced by its project path, not by a project reference, so the AppHost does not build the Web's code.</summary>
    private const string ProjectPath = "../PostmanClone.Web/PostmanClone.Web.csproj";

    public static IResourceBuilder<ProjectResource> AddWeb(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ContainerResource> authservice,
        IResourceBuilder<ContainerResource> collections,
        IResourceBuilder<ContainerResource> environments,
        IResourceBuilder<ProjectResource> execution)
    {
        return builder.AddProject(Name, ProjectPath)
            .WithHttpEndpoint(port: Port, name: "http")
            .WithExternalHttpEndpoints()
            // The Web reaches its backends by service-discovery names (docs/OPERATIONS.md 4.3): the resource names.
            .WithReference(authservice.GetEndpoint("http"))
            .WithReference(collections.GetEndpoint("http"))
            .WithReference(environments.GetEndpoint("http"))
            .WithReference(execution.GetEndpoint("http"))
            // It verifies the session's access token against authservice's published keys, like every service does. Without the
            // metadata address a sign-in answers error=unavailable (found by running the AppHost on a runner, ADR-0031).
            .WithEnvironment("Auth__MetadataAddress", ReferenceExpression.Create(
                $"{authservice.GetEndpoint("http")}/.well-known/openid-configuration"))
            .WithEnvironment("Auth__Issuer", AuthServiceResource.Issuer)
            .WithEnvironment("Auth__Audience", AuthServiceResource.Audience)
            .WithEnvironment("Auth__RequireHttpsMetadata", "false")
            // The session cookie is not Secure-only in Development, because this composition serves plain HTTP on localhost.
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WaitFor(authservice)
            .WaitFor(collections)
            .WaitFor(environments)
            .WaitFor(execution)
            .WithHttpHealthCheck("/health");
    }
}
