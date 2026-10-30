using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PostmanClone.EnvironmentsService.Api;
using PostmanClone.EnvironmentsService.Data;
using PostmanClone.EnvironmentsService.Environments;
using PostmanClone.EnvironmentsService.Secrets;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.Paging;
using PostmanClone.ServiceDefaults.Problems;
using PostmanClone.ServiceDefaults.RateLimiting;
using PostmanClone.ServiceDefaults.Validation;
using Environment = PostmanClone.EnvironmentsService.Domain.Environment;

namespace PostmanClone.EnvironmentsService.Endpoints;

/// <summary>
/// The <c>/api/v1/environments</c> endpoints (CONTRACTS §3). Every endpoint acts in the one workspace of the token, and
/// every id endpoint answers <c>404</c> for a row that is absent or belongs to another workspace (CONTRACTS §0).
/// </summary>
public static class EnvironmentEndpoints
{
    private const string Route = "/api/v1/environments";
    private const string SubjectClaim = "sub";

    public static WebApplication MapEnvironmentEndpoints(this WebApplication app)
    {
        // One trust level for the group: a signed-in user acting in exactly one workspace, metered by the api policy.
        var environments = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireActiveWorkspace()
            .RequireRateLimiting(RateLimitPolicies.Api);

        environments.MapGet("", ListAsync);
        environments.MapPost("", CreateAsync).ValidateDataAnnotations();
        environments.MapGet("/{id:guid}", GetAsync);
        environments.MapPut("/{id:guid}", UpdateAsync).ValidateDataAnnotations();
        environments.MapDelete("/{id:guid}", DeleteAsync);
        environments.MapPost("/{id:guid}/resolve", ResolveAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user, EnvironmentsDbContext db, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var actor = Actor.From(user);
        var paging = PageRequest.Clamp(page, pageSize);
        var environments = db.Environments.AsNoTracking().Where(environment => environment.WorkspaceId == actor.WorkspaceId);

        var total = await environments.CountAsync(cancellationToken);
        var items = await environments
            .OrderBy(environment => environment.Name)
            .ThenBy(environment => environment.Id)
            .Skip((int)Math.Min(paging.Skip, int.MaxValue))
            .Take(paging.PageSize)
            .Select(environment => new EnvironmentSummary(
                environment.Id, environment.Name, environment.Variables.Count, environment.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new Page<EnvironmentSummary>(items, paging.Page, paging.PageSize, total));
    }

    private static async Task<IResult> CreateAsync(
        CreateEnvironmentRequest request, ClaimsPrincipal user, EnvironmentsDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var actor = Actor.From(user);
        var count = await db.Environments.CountAsync(environment => environment.WorkspaceId == actor.WorkspaceId, cancellationToken);
        if (count >= EnvironmentLimits.EnvironmentsPerWorkspace)
        {
            return Conflict("This workspace already has the maximum number of environments.");
        }

        var now = clock.GetUtcNow();
        var environment = new Environment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = actor.WorkspaceId,
            Name = request.Name,
            CreatedBy = actor.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Environments.Add(environment);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"{Route}/{environment.Id}", EnvironmentMapping.ToEnvironmentDto(environment));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, EnvironmentsDbContext db, CancellationToken cancellationToken)
    {
        var environment = await LoadAsync(db, Actor.From(user).WorkspaceId, id, cancellationToken);
        return environment is null
            ? NotFound()
            : Results.Ok(EnvironmentMapping.ToEnvironmentDto(environment));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateEnvironmentRequest request,
        ClaimsPrincipal user,
        EnvironmentsDbContext db,
        ISecretProtector protector,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var environment = await LoadAsync(db, Actor.From(user).WorkspaceId, id, cancellationToken);
        if (environment is null)
        {
            return NotFound();
        }

        if (request.Variables.GroupBy(variable => variable.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return Conflict("Variable keys must be unique within an environment.");
        }

        // A write that stores a new secret needs the key. Keeping a stored secret does not decrypt it, so it works without one.
        if (request.Variables.Any(variable => variable.IsSecret && variable.Value is not null) && !protector.IsAvailable)
        {
            return SecretsUnavailable();
        }

        var violations = new VariableWriter(protector).Replace(db, environment, request.Variables);
        if (violations.Count > 0)
        {
            return Results.ValidationProblem(violations);
        }

        environment.Name = request.Name;
        environment.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(EnvironmentMapping.ToEnvironmentDto(environment));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal user, EnvironmentsDbContext db, CancellationToken cancellationToken)
    {
        var actor = Actor.From(user);
        var environment = await LoadAsync(db, actor.WorkspaceId, id, cancellationToken);
        if (environment is null)
        {
            return NotFound();
        }

        // The 404 check comes first: a foreign environment is never reported as forbidden.
        if (!EnvironmentAccess.CanDelete(actor.Role, actor.UserId, environment.CreatedBy))
        {
            return Forbidden();
        }

        db.Environments.Remove(environment);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>The only response that carries plaintext secrets (ADR-0006). ExecutionService calls it; it is metered by <c>api</c>.</summary>
    private static async Task<IResult> ResolveAsync(
        Guid id,
        ClaimsPrincipal user,
        EnvironmentsDbContext db,
        ISecretProtector protector,
        ILogger<EnvironmentAudit> audit,
        CancellationToken cancellationToken)
    {
        var actor = Actor.From(user);
        var environment = await LoadAsync(db, actor.WorkspaceId, id, cancellationToken);
        if (environment is null)
        {
            return NotFound();
        }

        var enabled = environment.Variables
            .Where(variable => variable.Enabled)
            .OrderBy(variable => variable.Key, StringComparer.Ordinal)
            .ToList();
        if (enabled.Any(variable => variable.IsSecret) && !protector.IsAvailable)
        {
            return SecretsUnavailable();
        }

        var resolved = new List<ResolvedVariable>(enabled.Count);
        foreach (var variable in enabled)
        {
            if (!variable.IsSecret)
            {
                resolved.Add(new ResolvedVariable(variable.Key, variable.Value ?? string.Empty, IsSecret: false));
                continue;
            }

            try
            {
                var place = SecretPlace.For(environment.WorkspaceId, environment.Id, variable.Key);
                resolved.Add(new ResolvedVariable(variable.Key, protector.Unprotect(variable.Ciphertext ?? string.Empty, place), IsSecret: true));
            }
            catch (CryptographicException)
            {
                EnvironmentAudit.SecretUndecryptable(audit, environment.Id);
                return SecretsUnavailable();
            }
        }

        EnvironmentAudit.Resolved(audit, environment.Id, actor.UserId, resolved.Count);
        return Results.Ok(new ResolvedEnvironment(environment.Id, resolved));
    }

    /// <summary>Loads the environment with its variables, but only from the given workspace: absent and foreign are the same answer.</summary>
    private static Task<Environment?> LoadAsync(EnvironmentsDbContext db, Guid workspaceId, Guid id, CancellationToken cancellationToken) =>
        db.Environments
            .Include(environment => environment.Variables)
            .FirstOrDefaultAsync(environment => environment.Id == id && environment.WorkspaceId == workspaceId, cancellationToken);

    private static IResult NotFound() =>
        Results.Problem(title: "The environment was not found.", statusCode: StatusCodes.Status404NotFound);

    private static IResult Forbidden() =>
        Results.Problem(title: "Only the creator, an Admin or an Owner can delete this environment.", statusCode: StatusCodes.Status403Forbidden);

    private static IResult Conflict(string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status409Conflict);

    private static IResult SecretsUnavailable() =>
        Results.Problem(
            title: "Secret variables are unavailable: the encryption key is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { [ProblemDetailsExtensions.CodeExtension] = "secrets_unavailable" });

    /// <summary>Who is calling: the one workspace of the token, the caller's role there, and the caller's subject.</summary>
    private sealed record Actor(Guid WorkspaceId, string Role, string UserId)
    {
        public static Actor From(ClaimsPrincipal user)
        {
            var workspace = WorkspaceClaims.Resolve(user);
            // RequireActiveWorkspace admits only a request with exactly one workspace, so these values are present.
            return new Actor(
                workspace.WorkspaceId ?? throw new InvalidOperationException("No active workspace reached an environment endpoint."),
                workspace.Role ?? string.Empty,
                user.FindFirst(SubjectClaim)?.Value ?? string.Empty);
        }
    }
}
