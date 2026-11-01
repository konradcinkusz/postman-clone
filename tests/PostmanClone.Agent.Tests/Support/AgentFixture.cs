using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PostmanClone.Agent.Config;
using PostmanClone.Agent.Local;
using PostmanClone.Agent.Remote;

namespace PostmanClone.Agent.Tests.Support;

/// <summary>One sentinel secret the tests look for in every response and every log line. It is a fixture, not a credential.</summary>
internal static class Sentinels
{
    public const string SecretValue = "sentinel-secret-7f3a9c2e-do-not-leak"; // gitleaks:allow

    public const string Origin = "https://app.example";

    public const string ServerBase = "https://estate.example";
}

/// <summary>Captures every log line the agent writes, so a test can prove a value never reached a log.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            provider.Lines.Enqueue(formatter(state, exception));
            if (exception is not null)
            {
                provider.Lines.Enqueue(exception.ToString());
            }
        }
    }
}

/// <summary>An EnvironmentsService stand-in for the agent's calls. Each test sets what the server answers.</summary>
internal sealed class FakeServer : IAgentServer
{
    public Func<string, string, string, string, string, ServerReply<AgentPairingReply>> OnPair { get; set; } =
        (_, _, _, _, _) => new ServerReply<AgentPairingReply>(201, new AgentPairingReply(
            Guid.NewGuid(), "agent-token-for-tests-0000000000000000000", DateTimeOffset.UtcNow.AddDays(90), Guid.NewGuid(), [Sentinels.Origin]), null);

    public Func<string, Guid, ServerReply<ResolvedEnvironmentReply>> OnResolve { get; set; } =
        (_, environmentId) => new ServerReply<ResolvedEnvironmentReply>(200, new ResolvedEnvironmentReply(
            environmentId,
            [new ResolvedVariableReply("token", Sentinels.SecretValue, IsSecret: true), new ResolvedVariableReply("host", "plain", IsSecret: false)]), null);

    /// <summary>The environment ids the agent asked the server to resolve, in order.</summary>
    public List<Guid> ResolvedEnvironments { get; } = [];

    /// <summary>The agent tokens the agent presented, in order. The tests check that the right one was used.</summary>
    public List<string> ResolveTokens { get; } = [];

    public Task<ServerReply<AgentPairingReply>> PairAsync(string baseUrl, string code, string agentName, string platform, string version, CancellationToken cancellationToken) =>
        Task.FromResult(OnPair(baseUrl, code, agentName, platform, version));

    public Task<ServerReply<ResolvedEnvironmentReply>> ResolveAsync(string baseUrl, string agentToken, Guid environmentId, CancellationToken cancellationToken)
    {
        ResolveTokens.Add(agentToken);
        ResolvedEnvironments.Add(environmentId);
        return Task.FromResult(OnResolve(agentToken, environmentId));
    }

    public Task<ServerReply<AgentIdentityReply>> MeAsync(string baseUrl, string agentToken, CancellationToken cancellationToken) =>
        Task.FromResult(new ServerReply<AgentIdentityReply>(200, new AgentIdentityReply(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(90)), null));
}

/// <summary>A running agent on a free loopback port, with its own configuration folder and captured logs.</summary>
internal sealed class RunningAgent : IAsyncDisposable
{
    private RunningAgent(WebApplication app, int port, AgentConfigStore store, FakeServer server, CapturingLoggerProvider logs)
    {
        App = app;
        Port = port;
        Store = store;
        Server = server;
        Logs = logs;
    }

    public WebApplication App { get; }

    public int Port { get; }

    public AgentConfigStore Store { get; }

    public FakeServer Server { get; }

    public CapturingLoggerProvider Logs { get; }

    public static async Task<RunningAgent> StartAsync(string directory, FakeServer? server = null, bool paired = false, bool allowPrivate = true)
    {
        var port = FreePort();
        var store = new AgentConfigStore(directory);
        var config = new AgentConfig { ServerBaseUrl = Sentinels.ServerBase, AllowedWebOrigins = [Sentinels.Origin], AllowPrivate = allowPrivate };
        if (paired)
        {
            config.AgentId = Guid.NewGuid();
            config.AgentToken = "agent-token-for-tests-0000000000000000000";
            config.AgentTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(90);
            config.PairingTokenHash = PairingTokens.Hash(PairingToken);
        }

        store.Save(config);
        var fake = server ?? new FakeServer();
        var logs = new CapturingLoggerProvider();
        var app = AgentHost.Build(new AgentHostOptions(port, store, fake, [logs]));
        await app.StartAsync();
        return new RunningAgent(app, port, store, fake, logs);
    }

    /// <summary>The pairing token the tests use when the agent is started paired. The agent keeps only its hash.</summary>
    public const string PairingToken = "pairing-token-for-tests-00000000000000000";

    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    /// <summary>A client that sends the Host it is told to, so the tests can name a host that is not the loopback one.</summary>
    public HttpClient Client() => new(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{Port}") };

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>A real HTTP target on loopback that records what it received. The send tests point the agent at it.</summary>
internal sealed class LoopbackTarget : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoopbackTarget(WebApplication app, int port, List<(string Header, string Value)> received)
    {
        _app = app;
        Port = port;
        Received = received;
    }

    public int Port { get; }

    /// <summary>Every header the target was sent, name and value. A test reads it to prove a value reached the target.</summary>
    public List<(string Header, string Value)> Received { get; }

    public static async Task<LoopbackTarget> StartAsync()
    {
        var received = new List<(string Header, string Value)>();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.Run(context =>
        {
            lock (received)
            {
                foreach (var header in context.Request.Headers)
                {
                    received.Add((header.Key, header.Value.ToString()));
                }
            }

            return context.Response.WriteAsync("reached");
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new LoopbackTarget(app, new Uri(address).Port, received);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
