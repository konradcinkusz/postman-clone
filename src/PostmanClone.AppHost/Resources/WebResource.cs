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
        IResourceBuilder<ContainerResource> authservice)
    {
        return builder.AddProject(Name, ProjectPath)
            .WithHttpEndpoint(port: Port, name: "http")
            .WithExternalHttpEndpoints()
            .WithReference(authservice.GetEndpoint("http"))
            .WaitFor(authservice);
    }
}
