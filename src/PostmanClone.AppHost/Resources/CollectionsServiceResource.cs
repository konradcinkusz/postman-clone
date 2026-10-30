using Aspire.Hosting.ApplicationModel;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The CollectionsService container (README §2, P1). It is built from its Dockerfile, so the AppHost runs the same image
/// CI builds. It reads the collectionsdb database and trusts the tokens authservice issues (ADR-0004).
/// </summary>
internal static class CollectionsServiceResource
{
    public const string Name = "collections-service";

    /// <summary>The Dockerfile, relative to the repository root. The build context is the repository root too.</summary>
    public const string DockerfilePath = "src/PostmanClone.CollectionsService/Dockerfile";

    /// <summary>
    /// Adds the service with its database and authservice. The database connection is injected by <c>WithReference</c>
    /// (<c>ConnectionStrings:collectionsdb</c>); the token settings name authservice's discovery document.
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddCollectionsService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database,
        IResourceBuilder<ContainerResource> authservice)
    {
        // The build context is the repository root, two levels above this AppHost project.
        var service = builder.AddDockerfile(Name, contextPath: "../..", dockerfilePath: DockerfilePath)
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithReference(database)
            .WithEnvironment("Auth__MetadataAddress", ReferenceExpression.Create($"{authservice.GetEndpoint("http")}/.well-known/openid-configuration"))
            .WithEnvironment("Auth__Issuer", AuthServiceResource.Issuer)
            .WithEnvironment("Auth__Audience", AuthServiceResource.Audience)
            // authservice answers plain HTTP in this development composition only; production requires HTTPS metadata.
            .WithEnvironment("Auth__RequireHttpsMetadata", "false")
            .WaitFor(database)
            .WaitFor(authservice)
            .WithHttpHealthCheck("/health");

        return service;
    }
}
