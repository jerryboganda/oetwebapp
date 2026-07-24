using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Billing;

/// <summary>Rules for paid products that are handed over outside the platform.</summary>
public static class ManualDeliveryPolicy
{
    /// <summary>
    /// True when fulfilment records an external hand-over without activating a web subscription.
    /// The explicit subtest sentinel prevents a legacy empty-array ("all subtests") interpretation.
    /// </summary>
    public static bool IsExternalOnly(BillingPlan? plan)
    {
        return plan is not null && IsExternalOnly(
            plan.DeliveryMethod,
            plan.IncludedSubtestsJson,
            plan.DashboardModulesJson,
            plan.BundledTutorBook,
            plan.BundledBasicEnglish,
            plan.BundledWritingAssessments,
            plan.BundledSpeakingSessions,
            plan.BundledAiCredits);
    }

    public static bool IsExternalOnly(BillingPlanVersion? version)
    {
        return version is not null && IsExternalOnly(
            version.DeliveryMethod,
            version.IncludedSubtestsJson,
            version.DashboardModulesJson,
            version.BundledTutorBook,
            version.BundledBasicEnglish,
            version.BundledWritingAssessments,
            version.BundledSpeakingSessions,
            version.BundledAiCredits);
    }

    private static bool IsExternalOnly(
        string deliveryMethod,
        string includedSubtestsJson,
        string dashboardModulesJson,
        bool bundledTutorBook,
        bool bundledBasicEnglish,
        int bundledWritingAssessments,
        int bundledSpeakingSessions,
        int bundledAiCredits)
    {
        if (!string.Equals(deliveryMethod, DeliveryMethods.ManualMaterial, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var subtests = EffectiveEntitlementResolver.ParseStringArray(includedSubtestsJson);
        return subtests.Count == 1
            && string.Equals(subtests[0], EffectiveEntitlementResolver.NoPlatformAccessSubtest, StringComparison.OrdinalIgnoreCase)
            && EffectiveEntitlementResolver.ParseDashboardModules(dashboardModulesJson).Count == 0
            && !bundledTutorBook
            && !bundledBasicEnglish
            && bundledWritingAssessments == 0
            && bundledSpeakingSessions == 0
            && bundledAiCredits == 0;
    }
}
