using PostmanClone.ServiceDefaults.Authentication;

namespace PostmanClone.EnvironmentsService.Environments;

/// <summary>Who may delete an environment (CONTRACTS §3): its creator, or an Admin or Owner of the workspace.</summary>
public static class EnvironmentAccess
{
    public static bool CanDelete(string role, string userId, string createdBy) =>
        role is WorkspaceRoles.Owner or WorkspaceRoles.Admin
        || (userId.Length > 0 && string.Equals(createdBy, userId, StringComparison.Ordinal));
}
