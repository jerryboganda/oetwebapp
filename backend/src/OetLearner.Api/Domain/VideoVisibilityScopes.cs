namespace OetLearner.Api.Domain;

/// <summary>
/// Video Library visibility scope vocabulary (spec §2). Persisted verbatim in
/// LibraryVideo.VisibilityScope. SHARED = Listening/Reading (visible to every valid
/// Full + Crash learner, all professions). FULL_* = one isolated Full Course profession
/// (Writing/Speaking). CRASH = Crash / Fast-Track only.
/// </summary>
public static class VideoVisibilityScopes
{
    public const string Shared = "SHARED";
    public const string FullMedicine = "FULL_MEDICINE";
    public const string FullNursing = "FULL_NURSING";
    public const string FullPharmacy = "FULL_PHARMACY";
    public const string Crash = "CRASH";

    /// <summary>All valid scope values.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Shared, FullMedicine, FullNursing, FullPharmacy, Crash,
    };

    /// <summary>The four targets an admin MUST choose from for a Writing/Speaking video
    /// (SHARED is intentionally excluded — spec §7.4 / OQ-2).</summary>
    public static readonly IReadOnlySet<string> WritingSpeakingTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FullMedicine, FullNursing, FullPharmacy, Crash,
    };

    /// <summary>Listening/Reading/basic-english (and any non-W/S / unset subtest) are
    /// always SHARED and shown read-only in the admin UI.</summary>
    public static bool IsForcedSharedSubtest(string? subtest)
    {
        var s = subtest?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(s) || s is "listening" or "reading" or "basic-english";
    }

    /// <summary>Writing/Speaking require an explicit isolated target before publish.</summary>
    public static bool RequiresExplicitTarget(string? subtest)
    {
        var s = subtest?.Trim().ToLowerInvariant();
        return s is "writing" or "speaking";
    }
}
