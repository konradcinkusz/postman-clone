using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PostmanClone.EnvironmentsService.Data;

namespace PostmanClone.EnvironmentsService.Agent;

/// <summary>
/// The scheme of the agent-only routes (CONTRACTS A2.4). It accepts an agent token and nothing else: a user's JWT fails here
/// and gets <c>401 agent_token_invalid</c>. The other routes use the JWT scheme, which refuses an agent token as an unknown
/// bearer. Both rules are architecture tests (WP30).
/// </summary>
public static class AgentTokenAuthenticationDefaults
{
    public const string Scheme = "AgentToken";

    /// <summary>The claim that says the principal is an agent, so a handler can tell it from a user.</summary>
    public const string TokenUseClaim = "token_use";
    public const string AgentTokenUse = "agent";
}

public sealed class AgentTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    EnvironmentsDbContext db,
    TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    /// <summary>A last-used time is written at most this often, so a busy agent does not write on every call.</summary>
    private static readonly TimeSpan LastUsedInterval = TimeSpan.FromMinutes(5);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = BearerToken(Request);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        var now = clock.GetUtcNow();
        var hash = AgentCredentials.Hash(token);
        var agent = await db.AgentInstallations.FirstOrDefaultAsync(
            installation => installation.TokenHash == hash && installation.RevokedAt == null && installation.ExpiresAt > now,
            Context.RequestAborted);
        if (agent is null)
        {
            return AuthenticateResult.Fail("The agent token is not valid.");
        }

        if (agent.LastUsedAt is null || now - agent.LastUsedAt > LastUsedInterval)
        {
            agent.LastUsedAt = now;
            await db.SaveChangesAsync(Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("sub", agent.Id.ToString()),
            new Claim("agent_id", agent.Id.ToString()),
            new Claim("organization", agent.WorkspaceId.ToString()),
            new Claim("agent_user", agent.UserId),
            new Claim("agent_expires", agent.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)),
            new Claim(AgentTokenAuthenticationDefaults.TokenUseClaim, AgentTokenAuthenticationDefaults.AgentTokenUse),
        ], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <summary>The refusal names the one code the agent API uses, so a page can say "pair again" (CONTRACTS A2.1).</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        Response.Headers.WWWAuthenticate = "Bearer";
        var body = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = "The agent token is missing, wrong, revoked or expired.",
            status = StatusCodes.Status401Unauthorized,
            code = "agent_token_invalid",
        });
        await Response.WriteAsync(body);
    }

    private static string? BearerToken(HttpRequest request)
    {
        const string Prefix = "Bearer ";
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith(Prefix, StringComparison.Ordinal) && header.Length > Prefix.Length
            ? header[Prefix.Length..]
            : null;
    }
}
