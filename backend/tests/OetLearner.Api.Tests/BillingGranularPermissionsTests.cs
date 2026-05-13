using OetLearner.Api.Domain;
using OetLearner.Api.Security;

namespace OetLearner.Api.Tests;

/// <summary>
/// Billing-hardening I-7 closure (May 2026). Locks the granular billing-write
/// permission split:
///
///   - billing:refund_write       → AdminBillingRefundWrite policy
///   - billing:catalog_write      → AdminBillingCatalogWrite policy
///   - billing:subscription_write → AdminBillingSubscriptionWrite policy
///
/// Each granular policy also accepts the legacy <c>billing:write</c> superset
/// and the <c>system_admin</c> super-permission, so existing admins continue
/// to function. These tests use <see cref="AdminPermissionEvaluator"/>
/// (the same evaluator the policies are wired to in Program.cs) so they
/// catch any drift between the policy registration and the granted-claim
/// matching logic.
/// </summary>
public class BillingGranularPermissionsTests
{
    // Argument lists that mirror the 3 policy registrations in Program.cs.
    // If a policy registration changes, the corresponding test array must
    // change too — the test acts as a checked contract.
    private static readonly string[] RefundPolicyAllow =
        ["billing:refund_write", "billing:write", "system_admin"];

    private static readonly string[] CatalogPolicyAllow =
        ["billing:catalog_write", "billing:write", "system_admin"];

    private static readonly string[] SubscriptionPolicyAllow =
        ["billing:subscription_write", "billing:write", "system_admin"];

    [Fact]
    public void All_PermissionList_Contains_3_New_Granular_BillingPermissions()
    {
        Assert.Contains(AdminPermissions.BillingRefundWrite, AdminPermissions.All);
        Assert.Contains(AdminPermissions.BillingCatalogWrite, AdminPermissions.All);
        Assert.Contains(AdminPermissions.BillingSubscriptionWrite, AdminPermissions.All);
    }

    [Fact]
    public void Granular_BillingPermissions_Have_Stable_Claim_Strings()
    {
        // Frontend lib/admin-permissions.ts and admin role presets depend on
        // these literal claim strings. Any rename here is a breaking change.
        Assert.Equal("billing:refund_write", AdminPermissions.BillingRefundWrite);
        Assert.Equal("billing:catalog_write", AdminPermissions.BillingCatalogWrite);
        Assert.Equal("billing:subscription_write", AdminPermissions.BillingSubscriptionWrite);
    }

    [Theory]
    [InlineData("billing:refund_write")]
    [InlineData("billing:write")]      // legacy superset
    [InlineData("system_admin")]       // super-permission
    [InlineData("billing:read,billing:refund_write")] // multi-claim with refund
    public void RefundPolicy_Allows(string claim)
    {
        Assert.True(AdminPermissionEvaluator.HasAny(claim, RefundPolicyAllow));
    }

    [Theory]
    [InlineData("billing:catalog_write")]
    [InlineData("billing:subscription_write")]
    [InlineData("billing:read")]
    [InlineData("users:write")]
    [InlineData("")]
    [InlineData(null)]
    public void RefundPolicy_Denies(string? claim)
    {
        Assert.False(AdminPermissionEvaluator.HasAny(claim, RefundPolicyAllow));
    }

    [Theory]
    [InlineData("billing:catalog_write")]
    [InlineData("billing:write")]
    [InlineData("system_admin")]
    public void CatalogPolicy_Allows(string claim)
    {
        Assert.True(AdminPermissionEvaluator.HasAny(claim, CatalogPolicyAllow));
    }

    [Theory]
    [InlineData("billing:refund_write")]
    [InlineData("billing:subscription_write")]
    [InlineData("billing:read")]
    public void CatalogPolicy_Denies(string claim)
    {
        Assert.False(AdminPermissionEvaluator.HasAny(claim, CatalogPolicyAllow));
    }

    [Theory]
    [InlineData("billing:subscription_write")]
    [InlineData("billing:write")]
    [InlineData("system_admin")]
    public void SubscriptionPolicy_Allows(string claim)
    {
        Assert.True(AdminPermissionEvaluator.HasAny(claim, SubscriptionPolicyAllow));
    }

    [Theory]
    [InlineData("billing:refund_write")]
    [InlineData("billing:catalog_write")]
    [InlineData("billing:read")]
    public void SubscriptionPolicy_Denies(string claim)
    {
        Assert.False(AdminPermissionEvaluator.HasAny(claim, SubscriptionPolicyAllow));
    }

    [Fact]
    public void LegacyBillingWrite_Continues_To_Grant_All_Three_GranularPolicies()
    {
        // Backward compatibility: existing admins whose grant is just
        // billing:write must still pass every new granular gate.
        const string legacy = "billing:write";
        Assert.True(AdminPermissionEvaluator.HasAny(legacy, RefundPolicyAllow));
        Assert.True(AdminPermissionEvaluator.HasAny(legacy, CatalogPolicyAllow));
        Assert.True(AdminPermissionEvaluator.HasAny(legacy, SubscriptionPolicyAllow));
    }

    [Fact]
    public void SystemAdmin_Grants_All_Three_GranularPolicies()
    {
        const string sysAdmin = "system_admin";
        Assert.True(AdminPermissionEvaluator.HasAny(sysAdmin, RefundPolicyAllow));
        Assert.True(AdminPermissionEvaluator.HasAny(sysAdmin, CatalogPolicyAllow));
        Assert.True(AdminPermissionEvaluator.HasAny(sysAdmin, SubscriptionPolicyAllow));
    }

    [Fact]
    public void ScopedRefundSpecialist_CannotPerform_Catalog_Or_Subscription_Mutations()
    {
        // Scenario: a support agent is granted billing:read + billing:refund_write
        // (the new "refund_specialist" built-in role). They must be able to
        // process refunds but must not be able to edit catalog or
        // subscriptions — the whole reason for the I-7 split.
        const string refundOnly = "billing:read,billing:refund_write";
        Assert.True(AdminPermissionEvaluator.HasAny(refundOnly, RefundPolicyAllow));
        Assert.False(AdminPermissionEvaluator.HasAny(refundOnly, CatalogPolicyAllow));
        Assert.False(AdminPermissionEvaluator.HasAny(refundOnly, SubscriptionPolicyAllow));
    }

    [Fact]
    public void ClaimParsing_IsCaseInsensitive_AndTolerantToWhitespace()
    {
        Assert.True(AdminPermissionEvaluator.HasAny("BILLING:REFUND_WRITE", RefundPolicyAllow));
        Assert.True(AdminPermissionEvaluator.HasAny("billing:read,  billing:refund_write  ,users:read", RefundPolicyAllow));
    }

    [Fact]
    public void Empty_Or_Null_Claim_Always_Denies()
    {
        Assert.False(AdminPermissionEvaluator.HasAny(null, RefundPolicyAllow));
        Assert.False(AdminPermissionEvaluator.HasAny(string.Empty, RefundPolicyAllow));
        Assert.False(AdminPermissionEvaluator.HasAny("   ", RefundPolicyAllow));
    }

    [Fact]
    public void Empty_AnyOf_Always_Denies()
    {
        Assert.False(AdminPermissionEvaluator.HasAny("system_admin"));
    }
}
