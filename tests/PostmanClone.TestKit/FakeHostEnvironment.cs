using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace PostmanClone.TestKit;

/// <summary>An <see cref="IHostEnvironment"/> with a chosen name, for code that branches on Production without a host.</summary>
public sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "PostmanClone.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
