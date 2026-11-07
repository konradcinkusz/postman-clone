using Aspire.Hosting.ApplicationModel;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The ExecutionService, built from its Dockerfile like CollectionsService and EnvironmentsService (ADR-0031). It was a project process
/// before; a process on the host cannot fetch the keys from the address authservice advertises inside the container network
/// (<c>Jwt:PublicBaseUrl</c>), so every consumer of authservice's tokens runs in that network. It owns <c>executiondb</c>.
/// </summary>
internal static class ExecutionServiceResource
{
    public const string Name = "execution-service";
    public const string DockerfilePath = "src/PostmanClone.ExecutionService/Dockerfile";

    /// <summary>
    /// Adds the service with its database, its token authority, the EnvironmentsService it resolves variables from
    /// (logical name <c>environments-service</c>, CONTRACTS section 5), and the health probe.
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddExecutionService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database,
        IResourceBuilder<ContainerResource> authService,
        IResourceBuilder<ContainerResource> environments)
    {
        return builder.AddDockerfile(Name, contextPath: "../..", dockerfilePath: DockerfilePath)
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithReference(database)
            .WithReference(environments.GetEndpoint("http"))
            .WithEnvironment("Auth__MetadataAddress", ReferenceExpression.Create(
                $"{authService.GetEndpoint("http")}/.well-known/openid-configuration"))
            .WithEnvironment("Auth__Issuer", AuthServiceResource.Issuer)
            .WithEnvironment("Auth__Audience", AuthServiceResource.Audience)
            // authservice listens on plain HTTP in this development composition only.
            .WithEnvironment("Auth__RequireHttpsMetadata", "false")
            .WaitFor(database)
            .WaitFor(authService)
            .WaitFor(environments)
            .WithHttpHealthCheck("/health");
    }
}
