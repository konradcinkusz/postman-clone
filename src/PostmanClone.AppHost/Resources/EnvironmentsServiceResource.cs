using Aspire.Hosting.ApplicationModel;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The EnvironmentsService container, built from its Dockerfile (P6) and started with its own database (P3).
/// The encryption key is a secret parameter (ADR-0006): it reaches the service as <c>Secrets__EncryptionKey</c>
/// and is never written to the tree.
/// </summary>
internal static class EnvironmentsServiceResource
{
    public const string Name = "environments-service";
    public const string DockerfilePath = "src/PostmanClone.EnvironmentsService/Dockerfile";
    public const string EncryptionKeyParameter = "environments-encryption-key";

    /// <summary>
    /// Adds the service. Its authentication is validated against authservice's discovery document (ADR-0004).
    /// <c>Auth__RequireHttpsMetadata</c> is off because the local AppHost serves authservice over plain HTTP;
    /// a deployed service would keep the default.
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddEnvironmentsService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database,
        IResourceBuilder<ContainerResource> authservice)
    {
        // Optional for the service (P8): without it the service starts and secret variables answer 503.
        var encryptionKey = builder.AddParameter(EncryptionKeyParameter, secret: true);
        var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

        var environments = builder.AddDockerfile(Name, repositoryRoot, DockerfilePath)
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithReference(database)
            .WithEnvironment("Auth__MetadataAddress", ReferenceExpression.Create(
                $"{authservice.GetEndpoint("http")}/.well-known/openid-configuration"))
            .WithEnvironment("Auth__Issuer", AuthServiceResource.Issuer)
            .WithEnvironment("Auth__Audience", AuthServiceResource.Audience)
            .WithEnvironment("Auth__RequireHttpsMetadata", "false")
            .WithEnvironment("Secrets__EncryptionKey", encryptionKey)
            .WaitFor(database)
            .WaitFor(authservice)
            .WithHttpHealthCheck("/health");

        return environments;
    }
}
