using Microsoft.Extensions.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace PostmanClone.AppHost.Resources;

/// <summary>
/// The demo seed (ADR-0030): a one-shot project that waits for authservice and the data services to be healthy, then creates the demo
/// accounts, collection and environment through their APIs. It exists only in Development and only while <c>Seed:Enabled</c> is not
/// <c>false</c> (default on). The seeder repeats both checks itself and refuses otherwise.
/// </summary>
internal static class SeedResource
{
    public const string Name = "seed";

    /// <summary>Where authservice's console log is kept on the host. The seeder reads the invitation token from it (v0.3.4 returns it only by email).</summary>
    public static string AuthLogDirectory(IDistributedApplicationBuilder builder) =>
        Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".local-stack", "aspire-logs"));

    public static bool IsEnabled(IDistributedApplicationBuilder builder) =>
        builder.Environment.IsDevelopment() && !string.Equals(builder.Configuration["Seed:Enabled"], "false", StringComparison.OrdinalIgnoreCase);

    public static IResourceBuilder<ProjectResource> AddSeed(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ContainerResource> authservice,
        IResourceBuilder<ContainerResource> collections,
        IResourceBuilder<ContainerResource> environments)
    {
        return builder.AddProject<Projects.PostmanClone_Seeder>(Name)
            .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
            .WithEnvironment("Seed__Enabled", "true")
            .WithEnvironment("Seed__AuthBaseUrl", authservice.GetEndpoint("http"))
            .WithEnvironment("Seed__CollectionsBaseUrl", collections.GetEndpoint("http"))
            .WithEnvironment("Seed__EnvironmentsBaseUrl", environments.GetEndpoint("http"))
            .WithEnvironment("Seed__AuthLogPath", Path.Combine(AuthLogDirectory(builder), "authservice.log"))
            .WaitFor(authservice)
            .WaitFor(collections)
            .WaitFor(environments);
    }
}
