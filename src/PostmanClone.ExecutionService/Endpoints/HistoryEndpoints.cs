using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PostmanClone.ExecutionService.Api;
using PostmanClone.Sending.Contracts;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.History;
using PostmanClone.ExecutionService.Sending;
using PostmanClone.Sending.Assembly;
using PostmanClone.Sending.Execution;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.Paging;
using PostmanClone.ServiceDefaults.Problems;
using PostmanClone.ServiceDefaults.RateLimiting;

namespace PostmanClone.ExecutionService.Endpoints;

/// <summary>
/// The caller's own request history (CONTRACTS §4, ADR-0015). Every endpoint acts in the one workspace of the token and only
/// on the caller's own entries. A row of another user or another workspace is <c>404</c>, never <c>403</c> (CONTRACTS §0).
/// </summary>
public static class HistoryEndpoints
{
    private const string Route = "/api/v1/history";
    private const string SubjectClaim = "sub";
    private const string AgentSource = "agent";

    public static WebApplication MapHistoryEndpoints(this WebApplication app)
    {
        var history = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireActiveWorkspace()
            .RequireRateLimiting(RateLimitPolicies.Api);

        history.MapGet("", ListAsync);
        history.MapPost("/records", RecordAsync).RequireRateLimiting(RateLimitPolicies.Api);
        history.MapGet("/{id:guid}", GetAsync);
        history.MapDelete("/{id:guid}", DeleteOneAsync);
        history.MapDelete("", ClearAsync);
        return app;
    }

    /// <summary>Newest first. <c>q</c> matches the masked URL template, case-insensitively.</summary>
    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user, ExecutionDbContext db, int? page, int? pageSize, string? q, CancellationToken cancellationToken)
    {
        var caller = Caller.From(user);
        if (caller is null)
        {
            return Forbidden();
        }

        var paging = PageRequest.Clamp(page, pageSize);
        var entries = HistoryScope.For(db, caller.WorkspaceId, caller.UserId).AsNoTracking();
        var needle = q?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(needle))
        {
            entries = entries.Where(entry => entry.Url.ToLower().Contains(needle));
        }

        var total = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(entry => entry.ExecutedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((int)Math.Min(paging.Skip, int.MaxValue))
            .Take(paging.PageSize)
            .Select(entry => new HistorySummary(
                entry.Id, entry.ExecutedAt, entry.Method, entry.Url, entry.StatusCode, entry.Outcome, entry.TotalMs, entry.SizeBytes, entry.Source))
            .ToListAsync(cancellationToken);

        return Results.Ok(new Page<HistorySummary>(items, paging.Page, paging.PageSize, total));
    }

    /// <summary>
    /// Records a run that an agent sent (CONTRACTS A2.5, ADR-0026 Decision 7). Nothing is sent or resolved here. The template is
    /// checked with the rules of a send, so a literal secret in an auth field is refused. The name-based masking applies to the
    /// stored headers, query and URL, and the body is cut to the history limit. The entry is owned by the caller's user and
    /// workspace and kept under the same retention as a server send.
    /// </summary>
    private static async Task<IResult> RecordAsync(
        HistoryRecordWrite write,
        ClaimsPrincipal user,
        ExecutionDbContext db,
        OutboundSender sender,
        IHistoryRecorder recorder,
        CancellationToken cancellationToken)
    {
        var caller = Caller.From(user);
        if (caller is null)
        {
            return Forbidden();
        }

        if (write.Source != AgentSource)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", "The source of a recorded run must be agent.");
        }

        if (write.Request is null || write.Result is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", "The request and the result are required.");
        }

        try
        {
            sender.CheckTemplate(new ExecuteRequest(write.Request, write.EnvironmentId, write.SavedRequestId, null, null));
        }
        catch (ExecuteFailure failure)
        {
            return Problem(failure.StatusCode, failure.Code, failure.Message);
        }

        // No secret value is known here, so the masker holds only the name-based rules. The agent masked the values already.
        var masker = SecretMasker.For([]);
        var result = MaskResult(write.Result, masker) with { HistoryId = null };
        var draft = new HistoryDraft(
            caller.WorkspaceId,
            caller.UserId.ToString(),
            write.SavedRequestId,
            write.EnvironmentId,
            write.Request,
            masker,
            result,
            AgentSource);

        var id = await recorder.RecordAsync(draft, cancellationToken);
        var row = id is null ? null : await db.HistoryEntries.AsNoTracking().FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        return row is null
            ? Problem(StatusCodes.Status500InternalServerError, "internal_error", "The run could not be recorded.")
            : Results.Created($"{Route}/{row.Id}", HistoryMapping.ToEntry(row));
    }

    /// <summary>The name-based masking of the stored result: response headers and the URLs in it (CONTRACTS §4).</summary>
    private static ExecutionResult MaskResult(ExecutionResult result, SecretMasker masker)
    {
        var response = result.Response is null
            ? null
            : result.Response with { Headers = masker.MaskHeaders(result.Response.Headers).ToList() };
        return result with
        {
            FinalUrl = masker.MaskUrl(result.FinalUrl),
            Redirects = result.Redirects.Select(hop => hop with { Location = masker.MaskUrl(hop.Location) }).ToList(),
            Response = response,
        };
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ExecutionDbContext db, CancellationToken cancellationToken)
    {
        var caller = Caller.From(user);
        if (caller is null)
        {
            return Forbidden();
        }

        var row = await HistoryScope.For(db, caller.WorkspaceId, caller.UserId)
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        return row is null ? NotFound() : Results.Ok(HistoryMapping.ToEntry(row));
    }

    private static async Task<IResult> DeleteOneAsync(Guid id, ClaimsPrincipal user, ExecutionDbContext db, CancellationToken cancellationToken)
    {
        var caller = Caller.From(user);
        if (caller is null)
        {
            return Forbidden();
        }

        var removed = await HistoryDeletion.DeleteAsync(
            db, HistoryScope.For(db, caller.WorkspaceId, caller.UserId).Where(entry => entry.Id == id), cancellationToken);
        return removed == 0 ? NotFound() : Results.NoContent();
    }

    /// <summary>Clears the caller's entries in this workspace. Another member's entries are not touched.</summary>
    private static async Task<IResult> ClearAsync(ClaimsPrincipal user, ExecutionDbContext db, CancellationToken cancellationToken)
    {
        var caller = Caller.From(user);
        if (caller is null)
        {
            return Forbidden();
        }

        await HistoryDeletion.DeleteAsync(db, HistoryScope.For(db, caller.WorkspaceId, caller.UserId), cancellationToken);
        return Results.NoContent();
    }

    private static IResult NotFound() => Problem(StatusCodes.Status404NotFound, "not_found", "The history entry was not found.");

    private static IResult Forbidden() =>
        Problem(StatusCodes.Status403Forbidden, "forbidden", "Your sign-in does not name a user in a workspace.");

    private static IResult Problem(int statusCode, string code, string title) =>
        Results.Problem(
            title: title,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { [ProblemDetailsExtensions.CodeExtension] = code });

    /// <summary>Who is calling: the caller's user id from <c>sub</c> and the one active workspace. Anything else is not a caller.</summary>
    private sealed record Caller(Guid WorkspaceId, Guid UserId)
    {
        public static Caller? From(ClaimsPrincipal user)
        {
            var workspace = WorkspaceClaims.Resolve(user);
            return workspace.IsActive && Guid.TryParse(user.FindFirst(SubjectClaim)?.Value, out var userId)
                ? new Caller(workspace.WorkspaceId!.Value, userId)
                : null;
        }
    }
}
