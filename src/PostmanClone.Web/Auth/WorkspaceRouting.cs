using PostmanClone.ServiceDefaults.Authentication;

namespace PostmanClone.Web.Auth;

public enum WorkspaceDecision
{
    /// <summary>Show the page.</summary>
    Continue,

    /// <summary>The user has no single workspace: send them to the page that explains it.</summary>
    RedirectToNoWorkspace,
}

/// <summary>
/// Which pages need exactly one workspace (CONTRACTS §0, ADR-0004). Zero or several organizations send a signed-in
/// user to <c>/no-workspace</c>, which explains either case. Anonymous users are the authorization attribute's concern,
/// not this decision's, and the pages that work without a workspace are exempt.
/// </summary>
public static class WorkspaceRouting
{
    public const string NoWorkspacePath = "/no-workspace";

    private static readonly string[] Exempt =
    [
        NoWorkspacePath,
        "/sign-in",
        "/register",
        "/legal/terms",
        "/legal/privacy",
        // Someone with no workspace yet accepts an invitation here: that acceptance is how they get one (WP24).
        "/accept-invitation",
    ];

    public static WorkspaceDecision Decide(string path, bool authenticated, WorkspaceResolution resolution)
    {
        if (!authenticated || IsExempt(path) || resolution.IsActive)
        {
            return WorkspaceDecision.Continue;
        }

        return WorkspaceDecision.RedirectToNoWorkspace;
    }

    private static bool IsExempt(string path)
    {
        var normalized = path.TrimEnd('/');
        return Array.Exists(Exempt, page => string.Equals(page, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
