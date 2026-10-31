using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PostmanClone.Web;

namespace PostmanClone.Web.Tests;

/// <summary>Hosts the Web manifest in memory (TestServer): the real pipeline, no socket, no backend.</summary>
public sealed class WebHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}
