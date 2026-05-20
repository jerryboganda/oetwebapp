using System.Security.Claims;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiAssistant.Permissions;

/// <summary>
/// Single source of truth for AI Assistant access checks.
/// Permission keys are sourced from <see cref="AdminPermissions"/>; the role
/// gate uses <see cref="ApplicationUserRoles.Admin"/>. <c>system_admin</c>
/// implicitly grants every permission (matches the rest of the API).
/// </summary>
public static class AiAssistantAuthorizationService
{
    public const string PermUse = AdminPermissions.UseAiAssistant;
    public const string PermManage = AdminPermissions.ManageAiAssistant;
    public const string PermUnrestricted = AdminPermissions.UseAiAssistantUnrestricted;

    public static bool HasAdminAccess(ClaimsPrincipal? user)
        => user?.IsInRole(ApplicationUserRoles.Admin) == true;

    public static bool HasUse(ClaimsPrincipal? user)
        => HasAdminAccess(user) && HasPerm(user!, PermUse);

    public static bool HasManageAiAssistant(ClaimsPrincipal? user)
        => HasAdminAccess(user) && HasPerm(user!, PermManage);

    public static bool HasUseUnrestricted(ClaimsPrincipal? user)
        => HasAdminAccess(user) && HasPerm(user!, PermUnrestricted);

    private static bool HasPerm(ClaimsPrincipal user, string perm)
    {
        foreach (var c in user.FindAll("perm"))
        {
            if (string.Equals(c.Value, AdminPermissions.SystemAdmin, System.StringComparison.Ordinal))
                return true;
            if (string.Equals(c.Value, perm, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
