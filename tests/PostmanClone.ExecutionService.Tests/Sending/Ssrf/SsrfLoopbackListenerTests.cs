using PostmanClone.ExecutionService.Tests.Support;
using System.Net;
using System.Net.Sockets;
using PostmanClone.ExecutionService.Options;
using PostmanClone.Sending.Ssrf;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Ssrf;

/// <summary>
/// The guard against a real listener on the loopback interface. These are the only tests that open a socket; they never
/// leave the machine. Without the flag the connection is refused before the listener is reached; with it, the request
/// completes.
/// </summary>
public sealed class SsrfLoopbackListenerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_loopback_target_is_refused_without_the_flag_and_the_listener_is_never_reached()
    {
        using var listener = Listen();
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        using var client = Client(new SsrfOptions());

        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync($"http://127.0.0.1:{port}/", TestContext.Current.CancellationToken));

        var rejected = SsrfRejectedException.Find(error);
        Assert.NotNull(rejected);
        Assert.Equal("ssrf_blocked_address", rejected.ErrorCode);
        Assert.Equal("loopback", rejected.Category);
        Assert.False(listener.Poll(0, SelectMode.SelectRead));
    }

    [Fact]
    public async Task A_loopback_target_is_reached_with_the_flag()
    {
        using var listener = Listen();
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        using var client = Client(new SsrfOptions { AllowLoopback = true });

        var serving = ServeOnce(listener);
        using var response = await client.GetAsync($"http://127.0.0.1:{port}/hello", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("GET /hello", await serving);
    }

    [Fact]
    public async Task A_localhost_name_is_refused_without_the_flag_even_though_the_listener_is_up()
    {
        using var listener = Listen();
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        using var client = Client(new SsrfOptions());

        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync($"http://localhost:{port}/", TestContext.Current.CancellationToken));

        Assert.Equal("loopback", SsrfRejectedException.Find(error)?.Category);
        Assert.False(listener.Poll(0, SelectMode.SelectRead));
    }

    [Fact]
    public async Task A_redirect_from_the_loopback_listener_to_a_private_address_is_refused_by_the_planner()
    {
        using var listener = Listen();
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        using var client = Client(new SsrfOptions { AllowLoopback = true });
        var serving = ServeOnce(listener, "HTTP/1.1 302 Found\r\nLocation: http://10.0.0.1/private\r\n");

        using var response = await client.GetAsync($"http://127.0.0.1:{port}/start", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var location = response.Headers.Location?.ToString();
        var step = Senders.Planner(new ExecutionOptions { Ssrf = new SsrfOptions { AllowLoopback = true } })
            .Plan(Senders.Validator(new ExecutionOptions { Ssrf = new SsrfOptions { AllowLoopback = true } })
                .Validate($"http://127.0.0.1:{port}/start").Target!, "GET", (int)response.StatusCode, location, 0);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(RedirectOutcome.Refuse, step.Outcome);
        Assert.Equal("ssrf_blocked_address", step.ErrorCode);
        await serving;
    }

    [Fact]
    public void The_sender_handler_has_no_ambient_proxy_no_cookies_no_automatic_redirects_and_no_decompression()
    {
        using var handler = SsrfHandlerFactory.Create(new SsrfOptions(), new SystemHostResolver(), new SocketAddressDialer());

        Assert.False(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
    }

    private static Socket Listen()
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(4);
        return listener;
    }

    private static HttpClient Client(SsrfOptions options)
    {
        var handler = SsrfHandlerFactory.Create(options, new SystemHostResolver(), new SocketAddressDialer());
        return new HttpClient(handler, disposeHandler: true) { Timeout = Patience };
    }

    /// <summary>Accepts one connection, reads the request line, and answers with the given head or a 200 with a short body.</summary>
    private static async Task<string> ServeOnce(Socket listener, string? responseHead = null)
    {
        using var accepted = await listener.AcceptAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
        using var stream = new NetworkStream(accepted, ownsSocket: false);
        using var reader = new StreamReader(stream);

        var requestLine = await reader.ReadLineAsync(); // "GET /path HTTP/1.1"
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }

        var head = responseHead ?? "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n";
        var body = responseHead is null ? "ok" : string.Empty;
        var bytes = System.Text.Encoding.ASCII.GetBytes($"{head}Connection: close\r\n\r\n{body}");
        await stream.WriteAsync(bytes, TestContext.Current.CancellationToken);
        await stream.FlushAsync(TestContext.Current.CancellationToken);

        return string.Join(" ", requestLine!.Split(' ').Take(2));
    }
}
