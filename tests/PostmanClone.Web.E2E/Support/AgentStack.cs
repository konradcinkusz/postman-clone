using System.Diagnostics;

namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// The local agent the agent journeys drive (WP32, ADR-0026, ADR-0028). It runs from `scripts/run-local-stack.sh up agent` on
/// 127.0.0.1 at AGENT_PORT (default 47631). Its configuration is in .local-stack/agent, and its stdout and stderr go to
/// .local-stack/logs/agent.log: a journey reads that log to show that a secret reached neither.
/// </summary>
public static class AgentStack
{
    public const int DefaultPort = 47631;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>The loopback address the agent listens on. Never a name: the agent's guard checks the Host header (CONTRACTS A2.6).</summary>
    public static Uri BaseAddress { get; } = new($"http://127.0.0.1:{Port}");

    public static int Port => int.TryParse(Environment.GetEnvironmentVariable("AGENT_PORT"), out var port) ? port : DefaultPort;

    /// <summary>The page origin the agent accepts: the Web's origin as the browser sends it (scheme, host and port, no path).</summary>
    public static string WebOrigin => E2ESettings.WebBaseUrl.GetLeftPart(UriPartial.Authority);

    /// <summary>The agent's tool, built by the stack script into .local-stack/agent-bin (a single-file executable).</summary>
    public static string ExecutablePath => Path.Combine(StackLogs.Directory, "agent-bin", "postmanclone-agent");

    /// <summary>The agent's configuration folder that the stack script set up.</summary>
    public static string ConfigDirectory => Path.Combine(StackLogs.Directory, "agent");

    /// <summary>The agent's combined output, written by the stack script.</summary>
    public static string LogPath => Path.Combine(StackLogs.Directory, "logs", "agent.log");

    /// <summary>Stops the journey with the reason when the agent does not answer for the Web's origin.</summary>
    public static async Task RequireAsync()
    {
        if (!await AnswersAsync())
        {
            Prerequisites.Fail($"The local agent is not answering on {BaseAddress}. Start it with `scripts/run-local-stack.sh up agent` (WP32).");
        }
    }

    /// <summary>True when /v1/status answers 200 to the Web's origin, the same question the Web's probe asks.</summary>
    public static async Task<bool> AnswersAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, "/v1/status"));
            request.Headers.TryAddWithoutValidation("Origin", WebOrigin);
            using var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>The agent's output so far. Empty when the log is missing, which the journeys report as a failure, not a pass.</summary>
    public static string LogText() => StackLogs.ReadText("agent.log");

    /// <summary>
    /// A second agent started on the same port. It must refuse to start (CONTRACTS A2.6). Returns its exit code and its error output.
    /// </summary>
    public static async Task<(int ExitCode, string Error)> StartSecondListenerAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Environment["POSTMANCLONE_AGENT_HOME"] = ConfigDirectory;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The second agent did not start as a process.");
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, error);
    }
}
