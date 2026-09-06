using OetLearner.Api.Services.Entitlements;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Owner directive: the AI Learning Companion is reached <b>through the packages
/// created for it</b>, using the platform's own authentication — not granted to
/// every account, and not behind a separate identity provider.
///
/// <para>
/// That makes <see cref="ModuleKeys.AiCompanion"/> the access gate, and it makes
/// its <i>opt-in</i> behaviour the property that matters. Module access in this
/// repository fails OPEN for legacy plans that never configured a module list —
/// correct for Materials and Videos, and completely wrong here: it would hand the
/// companion to every account on a plan that predates it. These tests pin the
/// exemption.
/// </para>
/// </summary>
public sealed class CompanionPackageAccessTests
{
    [Fact]
    public void LegacyPlanWithNoModuleConfig_DoesNotGrantTheCompanion()
    {
        // The fail-open path: an old plan that never configured modules at all.
        // Materials and Videos open; the companion must not.
        var snapshot = Snapshot(hasNoExplicitModuleConfig: true, modules: []);

        Assert.False(snapshot.IsModuleEnabled(ModuleKeys.AiCompanion));
        Assert.True(snapshot.IsModuleEnabled(ModuleKeys.MaterialsLibrary));
    }

    [Fact]
    public void NonSubscriber_DoesNotGetTheCompanion()
    {
        var snapshot = Snapshot(hasNoExplicitModuleConfig: true, modules: []);
        Assert.False(snapshot.IsModuleEnabled(ModuleKeys.AiCompanion));
    }

    [Fact]
    public void PackageThatGrantsTheModule_OpensTheCompanion()
    {
        var snapshot = Snapshot(
            hasNoExplicitModuleConfig: false,
            modules: [ModuleKeys.AiCompanion, ModuleKeys.MaterialsLibrary]);

        Assert.True(snapshot.IsModuleEnabled(ModuleKeys.AiCompanion));
    }

    [Fact]
    public void PackageThatOmitsTheModule_KeepsTheCompanionShut()
    {
        // A learner with a real, current package that simply does not include
        // the companion. This is the common case and must read as "no".
        var snapshot = Snapshot(
            hasNoExplicitModuleConfig: false,
            modules: [ModuleKeys.MaterialsLibrary, ModuleKeys.VideoLibrary]);

        Assert.False(snapshot.IsModuleEnabled(ModuleKeys.AiCompanion));
        Assert.True(snapshot.IsModuleEnabled(ModuleKeys.VideoLibrary));
    }

    [Fact]
    public void PerUserDisable_RevokesTheCompanionEvenWhenThePackageGrantsIt()
    {
        // Support needs to be able to switch one learner off without touching
        // the package everyone else is on.
        var snapshot = Snapshot(
            hasNoExplicitModuleConfig: false,
            modules: [ModuleKeys.AiCompanion],
            disabled: [ModuleKeys.AiCompanion]);

        Assert.False(snapshot.IsModuleEnabled(ModuleKeys.AiCompanion));
    }

    [Fact]
    public void ModuleKeyIsCaseInsensitive_SoAnAdminTypoDoesNotSilentlyDenyAccess()
    {
        var snapshot = Snapshot(hasNoExplicitModuleConfig: false, modules: ["aicompanion"]);
        Assert.True(snapshot.IsModuleEnabled(ModuleKeys.AiCompanion));
    }

    private static EffectiveEntitlementSnapshot Snapshot(
        bool hasNoExplicitModuleConfig,
        string[] modules,
        string[]? disabled = null) =>
        new(
            UserId: "learner-1",
            HasEligibleSubscription: !hasNoExplicitModuleConfig,
            IsTrial: false,
            Tier: hasNoExplicitModuleConfig ? "free" : "paid",
            SubscriptionId: null,
            SubscriptionStatus: null,
            PlanId: null,
            PlanVersionId: null,
            PlanCode: "test-plan",
            AiQuotaPlanCode: null,
            AiQuotaPlanCodeSource: null,
            ActiveAddOnCodes: [],
            IsFrozen: false,
            Trace: [])
        {
            EnabledModules = modules,
            DisabledModules = disabled ?? [],
            HasNoExplicitModuleConfig = hasNoExplicitModuleConfig,
        };
}
