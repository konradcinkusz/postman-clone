using Aspire.Hosting.ApplicationModel;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The authservice container (ADR-0004): the only identity provider, pinned, never copied into this tree.
/// The tag is the single place the version is written; the local no-Docker runner pins the same tag in
/// <c>scripts/run-local-stack.sh</c>.
/// </summary>
internal static class AuthServiceResource
{
    public const string ImageName = "ghcr.io/konradcinkusz/authservice";
    public const string ImageTag = "v0.3.4";

    public const string Name = "authservice";
    public const string Issuer = "PostmanClone";
    public const string Audience = "PostmanClone";
    public const string PrivateKeyParameter = "authservice-jwt-private-key-pem";

    /// <summary>
    /// Adds authservice with its own database, RS256 signing (the private key is a secret parameter), and the
    /// health probe the platform will use (<c>/health/ready</c>, 503 until the schema is in place).
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddAuthService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database)
    {
        // Required: without the key authservice would sign with a symmetric secret, which no other service may verify.
        var privateKey = builder.AddParameter(PrivateKeyParameter, secret: true);

        // Optional integrations (P8): an absent value is an empty string, and authservice runs without the feature.
        var frontendBaseUrl = builder.Configuration["Parameters:frontend-base-url"] ?? "http://localhost:5310";

        var authservice = builder.AddContainer(Name, ImageName, ImageTag)
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithEnvironment("ConnectionStrings__DefaultConnection", database.Resource.ConnectionStringExpression)
            .WithEnvironment("Database__SchemaMode", "Migrate")
            .WithEnvironment("Database__MigrationsAssembly", "AuthService.Migrations.PostgreSQL")
            .WithEnvironment("Jwt__Issuer", Issuer)
            .WithEnvironment("Jwt__Audience", Audience)
            .WithEnvironment("Jwt__PrivateKeyPem", privateKey)
            .WithEnvironment("FrontendBaseUrl", frontendBaseUrl)
            .WaitFor(database)
            .WithHttpHealthCheck("/health/ready");

        // Jwt:PublicBaseUrl is this container's own public origin. It is the issuer of the JWKS URL and must
        // be the URL other processes reach it on, so it is taken from the endpoint once Aspire assigns it.
        authservice.WithEnvironment("Jwt__PublicBaseUrl", authservice.GetEndpoint("http"));

        return authservice;
    }
}
