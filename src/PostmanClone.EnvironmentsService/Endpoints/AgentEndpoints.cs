using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostmanClone.EnvironmentsService.Agent;
using PostmanClone.EnvironmentsService.Api;
using PostmanClone.EnvironmentsService.Data;
using PostmanClone.EnvironmentsService.Domain;
using PostmanClone.EnvironmentsService.Environments;
using PostmanClone.EnvironmentsService.Secrets;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.Paging;
using PostmanClone.ServiceDefaults.Problems;
using PostmanClone.ServiceDefaults.RateLimiting;
using PostmanClone.ServiceDefaults.Validation;

namespace PostmanClone.EnvironmentsService.Endpoints;

/// <summary>
/// The local agent's routes (CONTRACTS A2.2 to A2.4, ADR-0026). Three trust levels, each on its own group: the user's routes
/// (JWT, one workspace), the pairing exchange (no credential, metered per address), and the agent-only routes (an agent token
/// and no other credential). A user's token is refused on the agent-only routes; an agent token is refused everywhere else.
/// </summary>
public static class AgentEndpoints
{
    public const string Route = "/api/v1/agent";

    /// <summary>The pairing code lives five minutes (CONTRACTS A2.2).</summary>
    public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The agent token lives 90 days from pairing (ADR-0026 Decision 3).</summary>
    public static readonly TimeSpan AgentTokenLifetime = TimeSpan.FromDays(90);

    private const string SubjectClaim = "sub";

    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        // The user's routes: a signed-in user acting in exactly one workspace.
        var user = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireActiveWorkspace()
            .RequireRateLimiting(RateLimitPolicies.Api);
        user.MapPost("/pairing-codes", CreatePairingCodeAsync);
        user.MapGet("/installations", ListInstallationsAsync);
        user.MapDelete("/installations/{id:guid}", RevokeInstallationAsync);

        // The pairing exchange: the one-time code is the credential. Metered like the other sign-in forms.
        var pairing = app.MapGroup(Route)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth);
        pairing.MapPost("/pair", PairAsync).ValidateDataAnnotations();

        // The agent-only routes: an agent token, nothing else.
        var agent = app.MapGroup(Route)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AgentTokenAuthenticationDefaults.Scheme })
            .RequireRateLimiting(RateLimitPolicies.Api);
        agent.MapGet("/me", MeAsync);
        agent.MapPost("/environments/{id:guid}/resolve", ResolveAsync).RequireRateLimiting(RateLimitPolicies.Execute);
        return app;
    }

    private static async Task<IResult> CreatePairingCodeAsync(ClaimsPrincipal user, EnvironmentsDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var actor = Actor.From(user);
        var now = clock.GetUtcNow();

        // A new code replaces the user's unused codes, so only the latest one can be typed.
        var unused = await db.AgentPairingCodes
            .Where(code => code.WorkspaceId == actor.WorkspaceId && code.UserId == actor.UserId && code.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        db.AgentPairingCodes.RemoveRange(unused);

        var code = AgentCredentials.NewPairingCode();
        var expiresAt = now + PairingCodeLifetime;
        db.AgentPairingCodes.Add(new AgentPairingCode
        {
            Id = Guid.NewGuid(),
            WorkspaceId = actor.WorkspaceId,
            UserId = actor.UserId,
            CodeHash = AgentCredentials.Hash(code),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        // The code is returned once. It is never logged and only its hash is stored.
        return Results.Created($"{Route}/pairing-codes", new PairingCodeDto(code, expiresAt));
    }

    private static async Task<IResult> PairAsync(
        AgentPairRequest request,
        EnvironmentsDbContext db,
        IOptions<AgentOptions> options,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var normalized = AgentCredentials.NormalizePairingCode(request.Code);
        var code = normalized is null
            ? null
            : await db.AgentPairingCodes.FirstOrDefaultAsync(
                candidate => candidate.CodeHash == AgentCredentials.Hash(normalized) && candidate.ConsumedAt == null && candidate.ExpiresAt > now,
                cancellationToken);

        // Unknown, used and expired give the same answer, so the response does not reveal which one it was (CONTRACTS A2.1).
        if (code is null || !await ConsumeAsync(db, code, now, cancellationToken))
        {
            return InvalidCode();
        }

        var token = AgentCredentials.NewAgentToken();
        var installation = new AgentInstallation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = code.WorkspaceId,
            UserId = code.UserId,
            Name = request.AgentName,
            Platform = request.Platform,
            Version = request.Version,
            TokenHash = AgentCredentials.Hash(token),
            PairedAt = now,
            ExpiresAt = now + AgentTokenLifetime,
        };
        db.AgentInstallations.Add(installation);
        await db.SaveChangesAsync(cancellationToken);

        // The token is returned once. The origins come from the server's configuration, never from the request.
        var pairing = new AgentPairing(
            installation.Id,
            token,
            installation.ExpiresAt,
            installation.WorkspaceId,
            options.Value.AllowedWebOrigins);
        return Results.Created($"{Route}/installations/{installation.Id}", pairing);
    }

    /// <summary>
    /// Marks the code used, once. On a relational provider the check and the write are one statement, so two exchanges of
    /// the same code cannot both win. The in-memory provider (tests only) has no concurrent writers.
    /// </summary>
    private static async Task<bool> ConsumeAsync(EnvironmentsDbContext db, AgentPairingCode code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            code.ConsumedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var affected = await db.AgentPairingCodes
            .Where(candidate => candidate.Id == code.Id && candidate.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ConsumedAt, now), cancellationToken);
        return affected == 1;
    }

    private static async Task<IResult> ListInstallationsAsync(
        ClaimsPrincipal user, EnvironmentsDbContext db, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var actor = Actor.From(user);
        var paging = PageRequest.Clamp(page, pageSize);
        var agents = db.AgentInstallations.AsNoTracking()
            .Where(installation => installation.WorkspaceId == actor.WorkspaceId && installation.UserId == actor.UserId);

        var total = await agents.CountAsync(cancellationToken);
        var items = await agents
            .OrderByDescending(installation => installation.PairedAt)
            .ThenBy(installation => installation.Id)
            .Skip((int)Math.Min(paging.Skip, int.MaxValue))
            .Take(paging.PageSize)
            .Select(installation => new AgentInstallationDto(
                installation.Id,
                installation.Name,
                installation.Platform,
                installation.Version,
                installation.PairedAt,
                installation.LastUsedAt,
                installation.ExpiresAt,
                installation.RevokedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new Page<AgentInstallationDto>(items, paging.Page, paging.PageSize, total));
    }

    private static async Task<IResult> RevokeInstallationAsync(Guid id, ClaimsPrincipal user, EnvironmentsDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var actor = Actor.From(user);
        // Another user's agent and another workspace's are the same answer as an absent one (CONTRACTS §0, A2.2).
        var installation = await db.AgentInstallations.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.WorkspaceId == actor.WorkspaceId && candidate.UserId == actor.UserId,
            cancellationToken);
        if (installation is null)
        {
            return EnvironmentEndpoints.NotFound();
        }

        installation.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Task<IResult> MeAsync(ClaimsPrincipal agent)
    {
        var identity = AgentPrincipal.From(agent);
        return Task.FromResult(Results.Ok(new AgentIdentity(identity.AgentId, identity.WorkspaceId, identity.ExpiresAt)));
    }

    private static async Task<IResult> ResolveAsync(
        Guid id,
        ClaimsPrincipal agent,
        EnvironmentsDbContext db,
        ISecretProtector protector,
        ILogger<EnvironmentAudit> audit,
        CancellationToken cancellationToken)
    {
        var identity = AgentPrincipal.From(agent);
        return await EnvironmentEndpoints.ResolveForAsync(identity.WorkspaceId, id, identity.UserId, db, protector, audit, cancellationToken);
    }

    private static IResult InvalidCode() =>
        Results.Problem(
            title: "The pairing code is not valid, has been used, or has expired.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { [ProblemDetailsExtensions.CodeExtension] = "pairing_code_invalid" });

    /// <summary>Who is calling a user route: the one workspace of the token and the caller's subject.</summary>
    private sealed record Actor(Guid WorkspaceId, string UserId)
    {
        public static Actor From(ClaimsPrincipal user)
        {
            var workspace = WorkspaceClaims.Resolve(user);
            // RequireActiveWorkspace admits only a request with exactly one workspace, so the value is present.
            return new Actor(
                workspace.WorkspaceId ?? throw new InvalidOperationException("No active workspace reached an agent endpoint."),
                user.FindFirst(SubjectClaim)?.Value ?? string.Empty);
        }
    }

    /// <summary>Who is calling an agent-only route: the agent, its workspace and the user who paired it.</summary>
    private sealed record AgentPrincipal(Guid AgentId, Guid WorkspaceId, string UserId, DateTimeOffset ExpiresAt)
    {
        public static AgentPrincipal From(ClaimsPrincipal agent)
        {
            // The scheme admits only an agent token, which sets these claims from the stored row.
            var agentId = Guid.Parse(agent.FindFirst("agent_id")?.Value ?? throw new InvalidOperationException("No agent id on an agent route."));
            var workspaceId = Guid.Parse(agent.FindFirst("organization")?.Value ?? throw new InvalidOperationException("No workspace on an agent route."));
            var expires = DateTimeOffset.Parse(agent.FindFirst("agent_expires")?.Value ?? throw new InvalidOperationException("No expiry on an agent route."), CultureInfo.InvariantCulture);
            return new AgentPrincipal(agentId, workspaceId, agent.FindFirst("agent_user")?.Value ?? string.Empty, expires);
        }
    }
}
