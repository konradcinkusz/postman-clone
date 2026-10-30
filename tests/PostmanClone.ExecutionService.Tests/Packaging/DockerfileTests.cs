using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Packaging;

/// <summary>
/// The image is built in CI (docker-build) but cannot be run in a session. These pins hold the P6 checklist in the
/// file itself: multi-stage, a pinned runtime image, a non-root user, port 8080 and an OCI label.
/// </summary>
public sealed class DockerfileTests
{
    private static readonly string Dockerfile = File.ReadAllText(
        Path.Combine(RepoRoot.Find(), "src", "PostmanClone.ExecutionService", "Dockerfile"));

    [Fact]
    public void The_image_is_multi_stage_on_the_pinned_10_0_runtime()
    {
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build", Dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final", Dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY --from=build", Dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_container_runs_as_the_non_root_app_user_on_port_8080()
    {
        Assert.Contains("USER app", Dockerfile, StringComparison.Ordinal);
        Assert.Contains("EXPOSE 8080", Dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_image_carries_an_OCI_source_label()
    {
        Assert.Contains("org.opencontainers.image.source=", Dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_copies_only_the_kernel_and_this_service()
    {
        Assert.DoesNotContain("COPY . ", Dockerfile, StringComparison.Ordinal);
        Assert.Contains("src/PostmanClone.ServiceDefaults/", Dockerfile, StringComparison.Ordinal);
        Assert.Contains("src/PostmanClone.ExecutionService/", Dockerfile, StringComparison.Ordinal);
    }
}
