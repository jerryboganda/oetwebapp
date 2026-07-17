namespace OetWithDrHesham.Api.Services.Entitlements;

/// <summary>
/// Canonical PascalCase module keys stored in <see cref="Domain.BillingPlan.DashboardModulesJson"/>
/// and surfaced as <see cref="EffectiveEntitlementSnapshot.EnabledModules"/>. These are the
/// admin-togglable "student subscription modules" (Enable/Disable per plan) that gate both the
/// learner navigation/tiles AND real backend access — see
/// <see cref="EffectiveEntitlementSnapshot.IsModuleEnabled"/>.
///
/// Keep in sync with the frontend list in <c>components/admin/billing/plan-catalog-editor.tsx</c>
/// (the four dropdowns) and <c>hooks/use-enabled-modules.ts</c>.
/// </summary>
public static class ModuleKeys
{
    /// <summary>Spaced-repetition Recalls (vocabulary) module.</summary>
    public const string Recalls = "Recalls";

    /// <summary>Downloadable study Materials library.</summary>
    public const string MaterialsLibrary = "MaterialsLibrary";

    /// <summary>Video Library module.</summary>
    public const string VideoLibrary = "VideoLibrary";

    /// <summary>Mock exams module (subscription-granted unlimited mocks).</summary>
    public const string Mocks = "Mocks";
}
