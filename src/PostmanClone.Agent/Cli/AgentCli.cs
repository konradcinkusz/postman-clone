using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using PostmanClone.Agent.Config;
using PostmanClone.Agent.Local;
using PostmanClone.Agent.Remote;

namespace PostmanClone.Agent.Cli;

/// <summary>
/// The agent's commands (ADR-0026 Decision 11). <c>run</c> listens; <c>setup</c> records the server and the origins the pages
/// may use; <c>pair</c> exchanges a code from a terminal; <c>status</c> shows what is stored; <c>forget</c> deletes the local
/// credentials. Exit codes: 0 done, 1 failed, 2 misused. Nothing a command prints is a token or a secret value.
/// </summary>
public static class AgentCli
{
    public static async Task<int> RunAsync(string[] args, AgentConfigStore? store = null, IAgentServer? server = null, CancellationToken cancellationToken = default)
    {
        store ??= new AgentConfigStore();
        using var httpServer = server is null ? new AgentServerClient() : null;
        server ??= httpServer!;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var options = Options.Parse(args[1..]);
        try
        {
            return args[0] switch
            {
                "run" => await RunListenerAsync(store, server, ParsePort(options), cancellationToken),
                "setup" => Setup(store, options),
                "pair" => await PairFromTerminalAsync(store, server, options, cancellationToken),
                "status" => Status(store),
                "forget" => Forget(store),
                _ => Misuse($"Unknown command '{args[0]}'."),
            };
        }
        catch (ArgumentException failure)
        {
            return Misuse(failure.Message);
        }
    }

    private const string Usage = """
        postmanclone-agent: sends requests from this computer for the PostmanClone Web.

          setup --server <url> [--web-origin <origin>]... [--allow-private true|false]
              Records the server and the page origins the agent accepts. Run once, before pairing.
          run [--port <n>]
              Listens on 127.0.0.1 only (default 47631). Stop with Ctrl+C.
          pair <code> [--server <url>]
              Exchanges a pairing code from the Web's Agent page in this terminal.
          status
              Shows what is stored locally. Never prints a token.
          forget
              Deletes the local credentials. Revoke the agent on the Web's Agent page as well.
        """;

    private static async Task<int> RunListenerAsync(AgentConfigStore store, IAgentServer server, int port, CancellationToken cancellationToken)
    {
        await using var app = AgentHost.Build(new AgentHostOptions(port, store, server));
        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or InvalidOperationException)
        {
            // Kestrel reports a taken port as an exception at start. Nothing is served, so the browser gets no answer from this port.
            await Console.Error.WriteLineAsync($"The port {port} on 127.0.0.1 is in use. Is another agent running?");
            return 1;
        }

        Console.WriteLine($"postmanclone-agent {AgentInfo.Version} listening on 127.0.0.1:{port}. Press Ctrl+C to stop.");
        await app.WaitForShutdownAsync(cancellationToken);
        return 0;
    }

    private static int Setup(AgentConfigStore store, Options options)
    {
        var server = options.Single("--server") ?? throw new ArgumentException("setup needs --server <url>.");
        ValidateUrl(server, "--server");
        var origins = options.All("--web-origin").ToList();
        if (origins.Count == 0)
        {
            throw new ArgumentException("setup needs at least one --web-origin <origin>, such as https://app.example.");
        }

        foreach (var origin in origins)
        {
            ValidateOrigin(origin);
        }

        var config = store.Load();
        config.ServerBaseUrl = server;
        config.AllowedWebOrigins = origins;
        config.AllowPrivate = ParseBool(options.Single("--allow-private") ?? "true", "--allow-private");
        store.Save(config);
        Console.WriteLine("Setup saved. Pair the agent from the Web's Agent page, or run: postmanclone-agent pair <code>");
        return 0;
    }

    private static async Task<int> PairFromTerminalAsync(AgentConfigStore store, IAgentServer server, Options options, CancellationToken cancellationToken)
    {
        var code = options.Positional.FirstOrDefault() ?? throw new ArgumentException("pair needs the code from the Web's Agent page.");
        var config = store.Load();
        var serverUrl = options.Single("--server") ?? config.ServerBaseUrl ?? throw new ArgumentException("pair needs --server <url> or a setup first.");
        ValidateUrl(serverUrl, "--server");

        var reply = await server.PairAsync(serverUrl, code, AgentInfo.Name, AgentInfo.Platform, AgentInfo.Version, cancellationToken);
        if (reply.Status == 400)
        {
            await Console.Error.WriteLineAsync("The pairing code is not valid, has been used, or has expired.");
            return 1;
        }

        if (!reply.Ok)
        {
            await Console.Error.WriteLineAsync("The server could not complete the pairing.");
            return 1;
        }

        // A terminal pairing has no browser to hold the local token, so the token is printed once for the operator (OPEN-ITEMS).
        var paired = reply.Body!;
        var pairingToken = PairingTokens.New();
        config.ServerBaseUrl = serverUrl;
        config.AgentId = paired.AgentId;
        config.WorkspaceId = paired.WorkspaceId;
        config.AgentToken = paired.AgentToken;
        config.AgentTokenExpiresAt = paired.ExpiresAt;
        config.PairingTokenHash = PairingTokens.Hash(pairingToken);
        if (paired.AllowedWebOrigins.Count > 0)
        {
            config.AllowedWebOrigins = paired.AllowedWebOrigins.ToList();
        }

        store.Save(config);
        Console.WriteLine($"Paired agent {paired.AgentId} until {paired.ExpiresAt:yyyy-MM-dd}.");
        Console.WriteLine($"Local pairing token (shown once, for a page that you link by hand): {pairingToken}");
        return 0;
    }

    private static int Status(AgentConfigStore store)
    {
        var config = store.Load();
        Console.WriteLine($"Server: {config.ServerBaseUrl ?? "not set"}");
        Console.WriteLine($"Allowed page origins: {(config.AllowedWebOrigins.Count == 0 ? "none" : string.Join(", ", config.AllowedWebOrigins))}");
        Console.WriteLine($"Private targets allowed: {(config.AllowPrivate ? "yes" : "no")}");
        Console.WriteLine(config.IsPaired
            ? $"Paired: agent {config.AgentId}, until {config.AgentTokenExpiresAt:yyyy-MM-dd}."
            : "Paired: no.");
        return 0;
    }

    private static int Forget(AgentConfigStore store)
    {
        store.Delete();
        Console.WriteLine("Local credentials deleted. Revoke this agent on the Web's Agent page as well.");
        return 0;
    }

    private static int ParsePort(Options options)
    {
        var text = options.Single("--port");
        if (text is null)
        {
            return LocalProtocol.DefaultPort;
        }

        return int.TryParse(text, out var port) && port is > 0 and < 65536
            ? port
            : throw new ArgumentException("--port must be a port number.");
    }

    private static void ValidateUrl(string text, string option)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException($"{option} must be an absolute http or https URL with no user information.");
        }
    }

    private static void ValidateOrigin(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || uri.AbsolutePath != "/"
            || text.Contains('*', StringComparison.Ordinal)
            || text.EndsWith('/')
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException($"--web-origin '{text}' is not an exact origin such as https://app.example.");
        }
    }

    private static bool ParseBool(string text, string option) =>
        text switch
        {
            "true" => true,
            "false" => false,
            _ => throw new ArgumentException($"{option} must be true or false."),
        };

    private static int Misuse(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
        return 2;
    }

    /// <summary>A minimal option parser: <c>--name value</c> pairs and positional words. Unknown flags are refused.</summary>
    private sealed class Options
    {
        private readonly List<(string Name, string Value)> _pairs = [];

        public List<string> Positional { get; } = [];

        public static Options Parse(string[] words)
        {
            var options = new Options();
            for (var index = 0; index < words.Length; index++)
            {
                var word = words[index];
                if (!word.StartsWith("--", StringComparison.Ordinal))
                {
                    options.Positional.Add(word);
                    continue;
                }

                if (index + 1 >= words.Length || words[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{word} needs a value.");
                }

                options._pairs.Add((word, words[index + 1]));
                index++;
            }

            return options;
        }

        public string? Single(string name)
        {
            var values = All(name).ToList();
            if (values.Count > 1)
            {
                throw new ArgumentException($"{name} may be given once.");
            }

            return values.FirstOrDefault();
        }

        public IEnumerable<string> All(string name) => _pairs.Where(pair => pair.Name == name).Select(pair => pair.Value);
    }
}
