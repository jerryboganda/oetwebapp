using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

public sealed class ManualDeliveryPolicyTests
{
    [Fact]
    public void NoneSentinel_ResolvesToNoSubtests_InsteadOfLegacyAllSubtests()
    {
        var plan = ExternalOnlyPlan();

        var (allSubtests, subtests) = EffectiveEntitlementResolver.UnionIncludedSubtests([plan]);

        Assert.False(allSubtests);
        Assert.Empty(subtests);
    }

    [Fact]
    public void ExternalOnly_RequiresManualMaterial_NoModules_NoBundles_AndNoneSentinel()
    {
        var plan = ExternalOnlyPlan();
        Assert.True(ManualDeliveryPolicy.IsExternalOnly(plan));

        plan.DashboardModulesJson = "[\"TutorBook\"]";
        Assert.False(ManualDeliveryPolicy.IsExternalOnly(plan));
    }

    private static BillingPlan ExternalOnlyPlan() => new()
    {
        Id = "plan-tutor-book",
        Code = "tutor-book",
        DeliveryMethod = DeliveryMethods.ManualMaterial,
        IncludedSubtestsJson = "[\"none\"]",
        DashboardModulesJson = "[]",
        BundledTutorBook = false,
        BundledBasicEnglish = false,
        BundledWritingAssessments = 0,
        BundledSpeakingSessions = 0,
        BundledAiCredits = 0,
    };
}
