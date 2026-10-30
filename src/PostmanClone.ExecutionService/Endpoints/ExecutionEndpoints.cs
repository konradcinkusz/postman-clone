using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Clients;
using PostmanClone.ExecutionService.Sending.Execution;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.Problems;
using PostmanClone.ServiceDefaults.RateLimiting;

namespace PostmanClone.ExecutionService.Endpoints;

/// <summary>
/// The sender's endpoint (CONTRACTS §4). A signed-in user acting in one workspace, metered by the <c>execute</c> policy.
/// The workspace comes from the token; the request body names no workspace.
/// </summary>
public static class ExecutionEndpoints
{
    /// <summary>The largest JSON body read. The raw body inside it is capped separately at 1 MB (CONTRACTS §4).</summary>
    public const long MaxRequestBytes = 8 * 1024 * 1024;

    public static WebApplication MapExecutionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .RequireActiveWorkspace();

        api.MapPost("/execute", ExecuteAsync).RequireRateLimiting(RateLimitPolicies.Execute);
        return app;
    }

    private static async Task<IResult> ExecuteAsync(
        ExecuteRequest request,
        ClaimsPrincipal user,
        HttpContext http,
        RequestExecutor executor,
        CancellationToken cancellationToken)
    {
        if (http.Request.ContentLength is > MaxRequestBytes)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "The request is larger than the sender accepts.");
        }

        var workspace = WorkspaceClaims.Resolve(user);
        var userId = user.FindFirst("sub")?.Value;
        if (!workspace.IsActive || userId is null)
        {
            return Problem(StatusCodes.Status403Forbidden, "forbidden", "Your sign-in does not name a user in a workspace.");
        }

        var caller = new ExecutionCaller(workspace.WorkspaceId!.Value, userId, BearerToken(http));
        try
        {
            return Results.Ok(await executor.ExecuteAsync(request, caller, cancellationToken));
        }
        catch (ExecuteFailure failure)
        {
            return Problem(failure.StatusCode, failure.Code, failure.Message);
        }
        catch (UpstreamUnavailableException)
        {
            return Problem(StatusCodes.Status502BadGateway, "upstream_unavailable", "The environments service could not be reached.");
        }
    }

    /// <summary>The caller's own bearer, forwarded to EnvironmentsService so it applies the same workspace check (CONTRACTS §3).</summary>
    private static string BearerToken(HttpContext http)
    {
        const string Prefix = "Bearer ";
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith(Prefix, StringComparison.Ordinal) ? header[Prefix.Length..] : string.Empty;
    }

    private static IResult Problem(int statusCode, string code, string title) =>
        Results.Problem(
            title: title,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { [ProblemDetailsExtensions.CodeExtension] = code });
}
