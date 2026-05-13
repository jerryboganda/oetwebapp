using System;
using System.Linq;

namespace OetLearner.Api.Security;

/// <summary>
/// Pure-function evaluator for the comma-separated admin-permissions claim
/// (<c>AuthTokenService.AdminPermissionsClaimType</c>). Centralises the
/// parsing + case-insensitive containment logic that was previously inlined
/// in <c>Program.HasAdminPermission</c>, so that:
///
///   1. The 17 policies registered in <c>Program.cs</c> share one tested
///      implementation.
///   2. Service-layer code that needs to gate an in-process operation on
///      a permission claim can reuse the same logic.
///   3. Billing-hardening I-7 introduces 3 granular billing-write policies
///      that all delegate here; tests can drive the evaluator directly
///      without spinning up an HTTP host.
/// </summary>
public static class AdminPermissionEvaluator
{
    private static readonly char[] Separators = [','];

    /// <summary>
    /// Returns true when the comma-separated <paramref name="permissionsClaim"/>
    /// includes at least one of the values in <paramref name="anyOf"/>.
    /// Whitespace around values is ignored. Comparisons are case-insensitive.
    /// Empty/null claims return false. Empty <paramref name="anyOf"/> returns false.
    /// </summary>
    public static bool HasAny(string? permissionsClaim, params string[] anyOf)
    {
        if (string.IsNullOrEmpty(permissionsClaim)) return false;
        if (anyOf is null || anyOf.Length == 0) return false;

        var granted = permissionsClaim.Split(
            Separators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return anyOf.Any(p => granted.Contains(p, StringComparer.OrdinalIgnoreCase));
    }
}
