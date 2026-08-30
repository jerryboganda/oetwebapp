using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Entitlements;

/// <summary>
/// Derives a candidate's Video Library visibility scope(s) from STRUCTURED package fields
/// (BillingPlan ProductCategory + Code + Profession — the runtime-authoritative fields the
/// resolver already uses for CourseFamilies), never from display titles. Crash is detected
/// FIRST across the category set + plan code (a writing/speaking crash bundle still resolves
/// to CRASH even if some marketing PackageType says "standalone"), then Full Course maps to
/// the profession-isolated scope, everything else is SHARED. Mirrors CourseFamilyPolicy's
/// category sets, adding the profession axis.
/// </summary>
public static class PackageScopePolicy
{
    private static readonly HashSet<string> FullCourseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "full_course", "full_course_bundle", "combo_double", "combo_mega",
    };

    private static readonly HashSet<string> CrashCourseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "crash_course", "crash_course_bundle", "writing_crash", "writing_crash_bundle", "speaking_crash",
    };

    /// <summary>Resolve the single scope a package grants. SHARED = no Full/Crash isolation
    /// (foundation/standalone-session/book/recall/unknown) → only shared L/R.</summary>
    public static string Resolve(string? productCategory, string? planCode, string? profession)
    {
        var category = productCategory?.Trim() ?? string.Empty;
        var code = (planCode ?? string.Empty).Trim().ToLowerInvariant();

        // 1) CRASH first — across BOTH the category set and the plan code.
        if (CrashCourseCategories.Contains(category)
            || code.StartsWith("crash-", StringComparison.Ordinal)
            || code.StartsWith("writing-crash", StringComparison.Ordinal)
            || code.StartsWith("speaking-crash", StringComparison.Ordinal))
        {
            return VideoVisibilityScopes.Crash;
        }

        // 2) Full Course → profession-isolated scope.
        if (FullCourseCategories.Contains(category)
            || code.StartsWith("full-", StringComparison.Ordinal))
        {
            return ResolveFullProfessionScope(profession);
        }

        // 3) Everything else → SHARED.
        return VideoVisibilityScopes.Shared;
    }

    private static string ResolveFullProfessionScope(string? profession)
    {
        var p = (profession ?? string.Empty).Trim().ToLowerInvariant();
        return p switch
        {
            "nursing" => VideoVisibilityScopes.FullNursing,
            "pharmacy" => VideoVisibilityScopes.FullPharmacy,
            _ => VideoVisibilityScopes.FullMedicine, // medicine/physio/allied/all/empty/unknown (OQ-3)
        };
    }

    /// <summary>The scope set a single package grants: empty for SHARED (shared L/R is implicit),
    /// otherwise the one specific FULL_*/CRASH scope.</summary>
    public static IReadOnlySet<string> ResolveSet(string? productCategory, string? planCode, string? profession)
        => UnionScopes(new[] { Resolve(productCategory, planCode, profession) });

    /// <summary>Set-union across a learner's effective packages (OQ-1): SHARED stays implicit,
    /// every distinct FULL_*/CRASH any package grants accumulates.</summary>
    public static IReadOnlySet<string> UnionScopes(IEnumerable<string> scopes)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in scopes)
        {
            if (!string.IsNullOrWhiteSpace(scope)
                && !string.Equals(scope, VideoVisibilityScopes.Shared, StringComparison.OrdinalIgnoreCase))
            {
                set.Add(scope.Trim());
            }
        }
        return set;
    }
}
