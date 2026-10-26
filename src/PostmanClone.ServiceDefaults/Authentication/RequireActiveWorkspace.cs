using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PostmanClone.ServiceDefaults.Problems;

namespace PostmanClone.ServiceDefaults.Authentication;

/// <summary>
/// Answers <c>403</c> unless the caller acts in exactly one workspace (CONTRACTS §0): <c>no_active_workspace</c> when
/// the token names none or its role is unknown, <c>multiple_workspaces</c> when it names two or more.
/// </summary>
public sealed class RequireActiveWorkspaceFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var resolution = WorkspaceClaims.Resolve(context.HttpContext.User);
        if (resolution.IsActive)
        {
            return next(context);
        }

        var (code, title) = resolution.Outcome == WorkspaceOutcomes.Multiple
            ? ("multiple_workspaces", "Your sign-in names more than one workspace.")
            : ("no_active_workspace", "Your sign-in does not name a workspace.");

        return ValueTask.FromResult<object?>(Results.Problem(
            title: title,
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { [ProblemDetailsExtensions.CodeExtension] = code }));
    }
}

public static class RequireActiveWorkspaceExtensions
{
    /// <summary>Gates a domain endpoint on exactly one active workspace. Every domain endpoint calls it.</summary>
    public static RouteHandlerBuilder RequireActiveWorkspace(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<RequireActiveWorkspaceFilter>();

    /// <summary>Gates every endpoint of a group on exactly one active workspace.</summary>
    public static RouteGroupBuilder RequireActiveWorkspace(this RouteGroupBuilder builder) =>
        builder.AddEndpointFilter<RequireActiveWorkspaceFilter>();
}
