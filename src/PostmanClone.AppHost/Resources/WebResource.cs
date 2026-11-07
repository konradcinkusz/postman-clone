using Aspire.Hosting.ApplicationModel;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The Blazor Web App (BLAZOR-WEB-APP section 4), built from its Dockerfile and run in the same container network as the services
/// (ADR-0031). It is the only externally reachable resource, on the fixed port 5310 (README section 3, the launch profile).
/// </summary>
internal static class WebResource
{
    public const string Name = "web";
    public const int Port = 5310;
    public const string DockerfilePath = "src/PostmanClone.Web/Dockerfile";

    public static IResourceBuilder<ContainerResource> AddWeb(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ContainerResource> authservice,
        IResourceBuilder<ContainerResource> collections,
        IResourceBuilder<ContainerResource> environments,
        IResourceBuilder<ContainerResource> execution)
    {
        return builder.AddDockerfile(Name, contextPath: "../..", dockerfilePath: DockerfilePath)
            .WithHttpEndpoint(port: Port, targetPort: 8080, name: "http")
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
            // Shows the demo accounts on the sign-in page while the seed is on (ADR-0030).
            .WithEnvironment("Seed__Enabled", SeedResource.IsEnabled(builder) ? "true" : "false")
            .WaitFor(authservice)
            .WaitFor(collections)
            .WaitFor(environments)
            .WaitFor(execution)
            .WithHttpHealthCheck("/health");
    }
}
