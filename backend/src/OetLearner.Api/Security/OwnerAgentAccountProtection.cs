using OetLearner.Api.Configuration;
using OetLearner.Api.Services;

namespace OetLearner.Api.Security;

/// <summary>
/// Owner-account hardening (plan Phase 3): for an account on the env-only
/// <c>OwnerAgent:OwnerAccountIds</c> allow-list, credential/privilege mutations
/// (admin password set/reset, lockout clearing, permission/role edits, MFA reset)
/// are refused unless the acting admin IS that owner. Otherwise any other
/// <c>system_admin</c> could take the owner account over and, with it, the
/// agent console's production authority.
///
/// Applied independently of <c>OwnerAgent:Enabled</c>: the protection is about the
/// account, not about whether the console is currently switched on.
/// </summary>
public static class OwnerAgentAccountProtection
{
    public const string ErrorCode = "owner_account_protected";

    /// <summary>Throws 403 <see cref="ErrorCode"/> when a non-owner actor targets an owner account.</summary>
    public static void EnsureMutationAllowed(
        OwnerAgentOptions? options,
        string? actorAccountId,
        string? targetAccountId,
        string operation)
    {
        if (options is null || string.IsNullOrWhiteSpace(targetAccountId) || !options.IsOwnerAccount(targetAccountId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(actorAccountId)
            && string.Equals(actorAccountId.Trim(), targetAccountId.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        throw ApiException.Forbidden(
            ErrorCode,
            $"This is the protected platform owner account. Only the owner can {operation} on it.");
    }
}
