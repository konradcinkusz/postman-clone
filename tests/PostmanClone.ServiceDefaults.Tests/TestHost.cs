using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using PostmanClone.ServiceDefaults.Problems;

namespace PostmanClone.ServiceDefaults.Tests;

/// <summary>Starts a real Kestrel server with the kernel wired the way a service wires it, on a free loopback port.</summary>
internal static class TestHost
{
    public static async Task<WebApplication> StartAsync(Action<WebApplicationBuilder>? configure = null, Action<WebApplication>? map = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();
        configure?.Invoke(builder);

        var app = builder.Build();
        app.UseProblemDetailsDefaults();
        app.MapDefaultEndpoints();
        map?.Invoke(app);
        await app.StartAsync();
        return app;
    }

    public static HttpClient ClientFor(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.First()) };
}
