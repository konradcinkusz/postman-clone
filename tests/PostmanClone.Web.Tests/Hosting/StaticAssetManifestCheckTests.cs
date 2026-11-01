using PostmanClone.Web.Hosting;

namespace PostmanClone.Web.Tests.Hosting;

/// <summary>The publish manifest against the files on disk (OI-27): a missing or empty asset stops the Web at startup.</summary>
public sealed class StaticAssetManifestCheckTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pc-static-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Manifest(string type, string assetFile, long declaredLength) =>
        $$"""
        {"Version":1,"ManifestType":"{{type}}","Endpoints":[
          {"Route":"app.css","AssetFile":"{{assetFile}}",
           "ResponseHeaders":[{"Name":"Cache-Control","Value":"no-cache"},{"Name":"Content-Length","Value":"{{declaredLength}}"}]}]}
        """;

    [Fact]
    public void A_publish_asset_of_the_declared_length_passes()
    {
        File.WriteAllText(Path.Combine(_root, "app.css"), "body{}");

        Assert.Empty(StaticAssetManifestCheck.Problems(Manifest("Publish", "app.css", 6), _root));
    }

    [Fact]
    public void An_empty_asset_is_reported_with_its_size_and_the_declared_size()
    {
        File.WriteAllText(Path.Combine(_root, "app.css"), string.Empty);

        var problems = StaticAssetManifestCheck.Problems(Manifest("Publish", "app.css", 3516), _root);

        Assert.Equal(["app.css is 0 bytes, the manifest says 3516"], problems);
    }

    [Fact]
    public void A_missing_asset_is_reported()
    {
        var problems = StaticAssetManifestCheck.Problems(Manifest("Publish", "gone.css", 10), _root);

        Assert.Equal(["gone.css is missing"], problems);
    }

    [Fact]
    public void A_build_manifest_points_at_source_folders_and_is_not_checked()
    {
        Assert.Empty(StaticAssetManifestCheck.Problems(Manifest("Build", "gone.css", 10), _root));
    }

    [Fact]
    public void Verify_stops_the_host_with_the_names_of_the_files_at_fault()
    {
        File.WriteAllText(Path.Combine(_root, "PostmanClone.Web.staticwebassets.endpoints.json"), Manifest("Publish", "app.css", 3516));
        File.WriteAllText(Path.Combine(_root, "app.css"), string.Empty);

        var error = Assert.Throws<InvalidOperationException>(() => StaticAssetManifestCheck.Verify(_root, _root));

        Assert.Contains("app.css is 0 bytes", error.Message, StringComparison.Ordinal);
        Assert.Contains("the Web will not start", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_does_nothing_when_there_is_no_manifest_beside_the_assembly()
    {
        StaticAssetManifestCheck.Verify(_root, _root);
    }
}
