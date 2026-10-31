using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// A small HTTP target inside the test process, on 127.0.0.1 at a port the OS picks. The sender reaches it only when the
/// stack runs with E2E_SENDER_LOOPBACK=1 (the test-only flag); the strict journeys send to it and expect a refusal.
/// Routes: /json (a JSON body), /echo (what it received), /html (markup, to prove the response is shown as text),
/// /redirect (a 302 to /json).
/// </summary>
public sealed class TargetServer : IAsyncDisposable
{
    private WebApplication? _app;

    public Uri BaseAddress { get; private set; } = new("http://127.0.0.1");

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.MapGet("/json", () => Results.Text("{\"ok\":true,\"items\":[1,2,3]}", "application/json"));
        _app.MapMethods("/echo", ["GET", "POST", "PUT", "PATCH", "DELETE"], async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var headers = string.Join(", ", context.Request.Headers
                .Where(h => h.Key is "Authorization" or "Content-Type" or "X-Trace")
                .Select(h => $"{h.Key}={h.Value}"));
            // The echo is plain text; the journeys check what the sender actually transmitted.
            return Results.Text($"method={context.Request.Method}\nheaders={headers}\nbody={body}", "text/plain");
        });
        _app.MapGet("/html", () => Results.Text(
            "<p id=\"injected\">markup</p><img id=\"injected-img\" src=\"x\" onerror=\"document.title='pwned'\">",
            "text/html"));
        _app.MapGet("/redirect", () => Results.Redirect("/json"));

        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseAddress = new Uri(address);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
