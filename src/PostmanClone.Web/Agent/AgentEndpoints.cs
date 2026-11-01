using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.WebUtilities;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.RateLimiting;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;
using PostmanClone.Web.Membership;

namespace PostmanClone.Web.Agent;

/// <summary>
/// The agent's Web routes (CONTRACTS A2.7, ADR-0028). Two form posts take the pairing code and revoke an installation; both check
/// antiforgery and redirect with a code, never a token. <c>POST /agent/runs</c> is the one JSON route: the page posts the masked
/// result of a send the agent made, and the route records it through ExecutionService with the session's bearer. The browser never
/// gets that bearer; the route is the only place it is used for a run.
/// </summary>
public static class AgentEndpoints
{
    /// <summary>The largest run record the route reads: the 1 MB body cap, the framing, and the 64 KB result cut (A2.5).</summary>
    public const int MaximumRunBytes = 2 * 1024 * 1024;

    /// <summary>The header the page sends the antiforgery token in (the framework's default name).</summary>
    public const string AntiforgeryHeader = "X-CSRF-TOKEN";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/agent/pairing-codes", CreatePairingCodeAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Api);
        app.MapPost("/agent/installations/{id:guid}/revoke", RevokeAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Api);
        // AllowAnonymous, not RequireAuthorization: the handler checks the session itself and answers 401. A challenge would
        // redirect a JSON caller to the sign-in page, which the page cannot read as a refusal.
        app.MapPost("/agent/runs", RecordRunAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Api);
        return app;
    }

    private static async Task<IResult> CreatePairingCodeAsync(
        HttpContext http,
        IAntiforgery antiforgery,
        IAgentClient agent,
        AgentPairingTickets tickets,
        TokenRefresher refresher,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryValidAsync(http, antiforgery))
        {
            return Results.BadRequest();
        }

        var sessionId = http.User.FindFirst(ServerSession.SessionIdClaim)?.Value;
        if (sessionId is null)
        {
            return SignInEnded(http);
        }

        if (!WorkspaceClaims.Resolve(http.User).IsActive)
        {
            return Redirect(http, WorkspaceRouting.NoWorkspacePath);
        }

        var accessToken = await refresher.AccessTokenAsync(sessionId, touch: true, cancellationToken);
        if (accessToken is null)
        {
            return SignInEnded(http);
        }

        var answer = await agent.CreatePairingCodeAsync(accessToken, cancellationToken);
        if (answer.Value is not { } pairing)
        {
            return Redirect(http, $"/agent?error={AgentCodes.ForOutcome(answer.Outcome)}");
        }

        // The code goes to the page by a one-time ticket, so it never reaches a URL, a log or the browser history.
        var ticket = tickets.Issue(sessionId, pairing.Code, pairing.ExpiresAt);
        return Redirect(http, $"/agent?ticket={Uri.EscapeDataString(ticket)}");
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        HttpContext http,
        IAntiforgery antiforgery,
        IAgentClient agent,
        TokenRefresher refresher,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryValidAsync(http, antiforgery))
        {
            return Results.BadRequest();
        }

        var sessionId = http.User.FindFirst(ServerSession.SessionIdClaim)?.Value;
        if (sessionId is null)
        {
            return SignInEnded(http);
        }

        var accessToken = await refresher.AccessTokenAsync(sessionId, touch: true, cancellationToken);
        if (accessToken is null)
        {
            return SignInEnded(http);
        }

        // Another user's agent, or another workspace's, is "not found" from EnvironmentsService (CONTRACTS §0): the same answer here.
        var answer = await agent.RevokeInstallationAsync(accessToken, id, cancellationToken);
        return answer.Outcome == CollectionOutcome.Succeeded
            ? Redirect(http, "/agent?notice=revoked")
            : Redirect(http, $"/agent?error={AgentCodes.ForOutcome(answer.Outcome)}");
    }

    /// <summary>
    /// The page's run record. Anonymous is 401; a missing or wrong antiforgery token is 400; a body over the limit is 413; a record
    /// that is not from the agent, or has no request or result, is 400. The name-based mask runs before anything is forwarded.
    /// </summary>
    private static async Task<IResult> RecordRunAsync(
        HttpContext http,
        IAntiforgery antiforgery,
        IAgentClient agent,
        TokenRefresher refresher,
        CancellationToken cancellationToken)
    {
        var sessionId = http.User.FindFirst(ServerSession.SessionIdClaim)?.Value;
        if (sessionId is null)
        {
            return Results.Unauthorized();
        }

        if (!await AntiforgeryValidAsync(http, antiforgery))
        {
            return Results.BadRequest();
        }

        if (http.Request.ContentLength is > MaximumRunBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        HistoryRecordWriteDto? record;
        try
        {
            record = await http.Request.ReadFromJsonAsync<HistoryRecordWriteDto>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        catch (BadHttpRequestException failure) when (failure.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (record is null || record.Source != "agent" || record.Request is null || record.Result is null)
        {
            return Results.BadRequest();
        }

        var accessToken = await refresher.AccessTokenAsync(sessionId, touch: true, cancellationToken);
        if (accessToken is null)
        {
            return Results.Unauthorized();
        }

        var answer = await agent.RecordRunAsync(accessToken, AgentRunMasking.MaskRecord(record), cancellationToken);
        return answer.Value is { } recorded
            ? Results.Json(new { id = recorded.Id }, Json, statusCode: StatusCodes.Status201Created)
            : answer.Outcome == CollectionOutcome.Unavailable
                ? Results.StatusCode(StatusCodes.Status502BadGateway)
                : Results.StatusCode(ProblemStatus(answer.Outcome));
    }

    /// <summary>The antiforgery check of the form posts and the run record. A failed check is an answer, not an exception.</summary>
    private static async Task<bool> AntiforgeryValidAsync(HttpContext http, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static int ProblemStatus(CollectionOutcome outcome) => outcome switch
    {
        CollectionOutcome.NotFound => StatusCodes.Status404NotFound,
        CollectionOutcome.Forbidden => StatusCodes.Status403Forbidden,
        CollectionOutcome.Invalid => StatusCodes.Status400BadRequest,
        CollectionOutcome.RateLimited => StatusCodes.Status429TooManyRequests,
        CollectionOutcome.TooLarge => StatusCodes.Status413PayloadTooLarge,
        CollectionOutcome.SessionEnded => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult SignInEnded(HttpContext http) =>
        Redirect(http, ReturnUrlPolicy.SignInPath(ReturnUrlPolicy.Fallback, MembershipCodes.Ended));

    private static IResult Redirect(HttpContext http, string location)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        return Results.Redirect(location);
    }
}

/// <summary>The short codes the agent page reads from its query string. A code names a cause, never a value.</summary>
public static class AgentCodes
{
    public const string Failed = "failed";
    public const string NotFound = "not_found";
    public const string Unavailable = "unavailable";
    public const string Forbidden = "forbidden";

    public static string ForOutcome(CollectionOutcome outcome) => outcome switch
    {
        CollectionOutcome.NotFound => NotFound,
        CollectionOutcome.Forbidden => Forbidden,
        CollectionOutcome.Unavailable => Unavailable,
        _ => Failed,
    };
}
