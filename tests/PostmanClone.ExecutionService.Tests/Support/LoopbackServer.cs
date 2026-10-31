using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>
/// A real Kestrel listener on the loopback interface, started in-process by a test. It is the only target the sender
/// reaches in these tests (ADR-0011): nothing leaves the machine. Every request is recorded before the handler runs.
/// </summary>
public sealed class LoopbackServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<CapturedRequest> _requests;

    private LoopbackServer(WebApplication app, Uri address, ConcurrentQueue<CapturedRequest> requests)
    {
        _app = app;
        Address = address;
        _requests = requests;
    }

    /// <summary>The origin, for example <c>http://127.0.0.1:50123</c>.</summary>
    public Uri Address { get; }

    public int Port => Address.Port;

    /// <summary>What the server received, in arrival order.</summary>
    public IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();

    /// <summary>Starts a listener on a free loopback port that answers with <paramref name="handle"/>.</summary>
    public static async Task<LoopbackServer> StartAsync(Func<HttpContext, Task> handle)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();

        var requests = new ConcurrentQueue<CapturedRequest>();
        app.Run(async context =>
        {
            requests.Enqueue(CapturedRequest.From(context));
            await handle(context);
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new LoopbackServer(app, new Uri(address), requests);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>A request as the loopback target received it: the method, the path with its query, and the headers joined by commas.</summary>
public sealed record CapturedRequest(string Method, string PathAndQuery, IReadOnlyDictionary<string, string> Headers)
{
    public static CapturedRequest From(HttpContext context)
    {
        var headers = context.Request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(",", header.Value.ToArray()),
            StringComparer.OrdinalIgnoreCase);
        return new CapturedRequest(context.Request.Method, context.Request.Path + context.Request.QueryString, headers);
    }

    public string? Header(string name) => Headers.GetValueOrDefault(name);
}
