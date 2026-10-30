using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace PostmanClone.ExecutionService.Tests.Support;

/// <summary>
/// A stand-in for EnvironmentsService on the loopback interface, so the real <c>EnvironmentsClient</c> path runs. It
/// answers <c>resolve</c> for an environment only to a caller whose token names the environment's workspace, and 404
/// otherwise, as the real service does (CONTRACTS §0). The token is read, not verified: this stand-in is not the auth boundary.
/// </summary>
public sealed class FakeEnvironments : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, (Guid Workspace, object[] Variables)> _environments = new();

    private FakeEnvironments(LoopbackServer server) => Server = server;

    public LoopbackServer Server { get; }

    public static async Task<FakeEnvironments> StartAsync()
    {
        FakeEnvironments? environments = null;
        var server = await LoopbackServer.StartAsync(context => environments!.AnswerAsync(context));
        environments = new FakeEnvironments(server);
        return environments;
    }

    /// <summary>Adds an environment to a workspace. Each variable is <c>(key, value, isSecret)</c>.</summary>
    public Guid Add(Guid workspace, params (string Key, string Value, bool IsSecret)[] variables)
    {
        var id = Guid.NewGuid();
        var shaped = variables.Select(variable => (object)new { key = variable.Key, value = variable.Value, isSecret = variable.IsSecret }).ToArray();
        _environments[id] = (workspace, shaped);
        return id;
    }

    private async Task AnswerAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (context.Request.Method != HttpMethods.Post || segments.Length != 5 || segments[4] != "resolve" || !Guid.TryParse(segments[3], out var id))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        var workspace = CallerWorkspace(context.Request.Headers.Authorization.ToString());
        if (!_environments.TryGetValue(id, out var environment) || environment.Workspace != workspace)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await context.Response.WriteAsJsonAsync(new { code = "not_found" });
            return;
        }

        await context.Response.WriteAsJsonAsync(new { id, variables = environment.Variables });
    }

    private static Guid? CallerWorkspace(string authorization)
    {
        const string Prefix = "Bearer ";
        if (!authorization.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var token = new JwtSecurityTokenHandler().ReadJwtToken(authorization[Prefix.Length..]);
        var organization = token.Claims.FirstOrDefault(claim => claim.Type == "organization")?.Value;
        return Guid.TryParse(organization, out var workspace) ? workspace : null;
    }

    public ValueTask DisposeAsync() => Server.DisposeAsync();
}
