using Aspire.Hosting.ApplicationModel;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The ExecutionService, run from source for development (the Dockerfile is for CI's build check and is never
/// deployed, ADR-0007). It owns <c>executiondb</c> and validates tokens against authservice's discovery document.
/// </summary>
internal static class ExecutionServiceResource
{
    public const string Name = "execution-service";

    /// <summary>
    /// Adds the service with its database, its token authority, the EnvironmentsService it resolves variables from
    /// (logical name <c>environments-service</c>, CONTRACTS §5), and the health probe.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddExecutionService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database,
        IResourceBuilder<ContainerResource> authService,
        IResourceBuilder<ContainerResource> environments)
    {
        return builder.AddProject<Projects.PostmanClone_ExecutionService>(Name)
            .WithHttpEndpoint(env: "ASPNETCORE_HTTP_PORTS")
            .WithReference(database)
            .WithReference(environments.GetEndpoint("http"))
            .WaitFor(database)
            .WaitFor(authService)
            .WithEnvironment("Auth__MetadataAddress", ReferenceExpression.Create(
                $"{authService.GetEndpoint("http")}/.well-known/openid-configuration"))
            .WithEnvironment("Auth__Issuer", AuthServiceResource.Issuer)
            .WithEnvironment("Auth__Audience", AuthServiceResource.Audience)
            // authservice listens on plain HTTP on the loopback development stack only.
            .WithEnvironment("Auth__RequireHttpsMetadata", "false")
            .WithHttpHealthCheck("/health");
    }
}
