using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace PostmanClone.AppHost.Tests;

/// <summary>
/// The AppHost's resource model, built in memory and never started (no DCP, no containers). Environment values are read in publish mode,
/// where a parameter or an endpoint is a reference such as <c>{authservice-jwt-private-key-pem.value}</c>, so no secret is generated or read.
/// </summary>
internal sealed class AppHostModel
{
    public const string ConfiguredKey = "configured-signing-key-for-tests"; // gitleaks:allow — a test marker, not a key.

    private readonly IDistributedApplicationTestingBuilder _builder;

    private AppHostModel(IDistributedApplicationTestingBuilder builder) => _builder = builder;

    /// <summary>Builds the model. Development unless <paramref name="extraArgs"/> says otherwise (the last <c>--environment</c> wins).</summary>
    public static async Task<AppHostModel> BuildAsync(params string[] extraArgs)
    {
        var args = new List<string>
        {
            "--environment=Development",
            "Parameters:authservice-jwt-private-key-pem=" + ConfiguredKey,
            "Parameters:environments-encryption-key=configured-encryption-key-for-tests",
        };
        args.AddRange(extraArgs);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PostmanClone_AppHost>(args.ToArray(), CancellationToken.None);
        return new AppHostModel(builder);
    }

    public IResource? Find(string name) => _builder.Resources.FirstOrDefault(r => r.Name == name);

    public IResource Get(string name) => Find(name) ?? throw new InvalidOperationException($"The AppHost has no resource '{name}'.");

    public async Task<Dictionary<string, string>> EnvironmentAsync(string name) => (await ConfigurationAsync(name)).EnvironmentVariables
        .ToDictionary(p => p.Key, p => p.Value);

    public async Task<IReadOnlyList<string>> ArgumentsAsync(string name) => (await ConfigurationAsync(name)).Arguments.Select(a => a.Value).ToList();

    private async Task<IExecutionConfigurationResult> ConfigurationAsync(string name)
    {
        var context = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish);
        return await ExecutionConfigurationBuilder.Create(Get(name))
            .WithEnvironmentVariablesConfig()
            .WithArgumentsConfig()
            .BuildAsync(context, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
    }

    public static string ImageReference(IResource resource)
    {
        var image = resource.Annotations.OfType<ContainerImageAnnotation>().Last();
        return (image.Registry is { Length: > 0 } registry ? registry + "/" : string.Empty) + image.Image + ":" + image.Tag;
    }
}
