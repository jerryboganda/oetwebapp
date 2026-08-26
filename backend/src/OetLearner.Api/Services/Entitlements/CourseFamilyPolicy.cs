namespace OetLearner.Api.Services.Entitlements;

/// <summary>
/// Mutual Full Course ↔ Crash Course visibility. The assigned billing
/// <c>ProductCategory</c> (catalog source of truth) decides which course
/// family the candidate may see. Content is classified from existing tags,
/// collection/folder labels, and titles — never from display package names.
/// Shared content (no family marker, or both families tagged) stays visible.
/// </summary>
public enum CourseFamily
{
    None = 0,
    Shared = 1,
    FullCourse = 2,
    CrashCourse = 3,
}

/// <summary>
/// Entitled course families for a candidate. <see cref="Unrestricted"/>
/// means the package does not participate in Full/Crash isolation
/// (custom/legacy/unknown categories). Restricted access is Full-only or
/// Crash-only; shared content remains visible on both.
/// </summary>
public readonly record struct CourseFamilyAccess(bool FullCourse, bool CrashCourse)
{
    public static CourseFamilyAccess Unrestricted => new(true, true);
    public static CourseFamilyAccess FullOnly => new(true, false);
    public static CourseFamilyAccess CrashOnly => new(false, true);

    public bool IsRestricted => !(FullCourse && CrashCourse);

    public bool Allows(CourseFamily family) => family switch
    {
        CourseFamily.None => false,
        CourseFamily.Shared => true,
        CourseFamily.FullCourse => FullCourse,
        CourseFamily.CrashCourse => CrashCourse,
        _ => false,
    };
}

public static class CourseFamilyPolicy
{
    // Catalog ProductCategory values from Data/Seeds/oet-2026-catalog.json.
    private static readonly HashSet<string> FullCourseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "full_course",
        "full_course_bundle",
        "combo_double",
        "combo_mega",
    };

    private static readonly HashSet<string> CrashCourseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "crash_course",
        "crash_course_bundle",
        "writing_crash",
        "writing_crash_bundle",
        "speaking_crash",
    };

    /// <summary>Canonical Full Course-only video tag (admin batch picker).</summary>
    public const string FullCourseOnlyTag = "batch:full-course-only";

    /// <summary>Canonical Shared video tag (admin batch picker) — visible to both families.</summary>
    public const string SharedTag = "batch:shared";

    /// <summary>Canonical Crash Course-only video tag (admin batch picker).</summary>
    public const string CrashCourseOnlyTag = "batch:crash-course-only";

    /// <summary>Canonical Crash Course-only video tags (admin batch picker + writing folders).</summary>
    public static readonly IReadOnlySet<string> CrashCourseOnlyTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "batch:crash-course-only",
        "batch:crash-course-arabic-writing",
        "batch:writing-sessions-crash-course-old",
        "batch:fast-track-crash-course",
        "batch:crash-course-workshops",
        "batch:new-medicine-crash-course",
    };

    public static CourseFamilyAccess Resolve(string? productCategory, string? planCode)
    {
        var category = productCategory?.Trim() ?? string.Empty;
        if (FullCourseCategories.Contains(category)) return CourseFamilyAccess.FullOnly;
        if (CrashCourseCategories.Contains(category)) return CourseFamilyAccess.CrashOnly;
        if (category.Length > 0) return CourseFamilyAccess.Unrestricted;

        var code = (planCode ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length == 0) return CourseFamilyAccess.Unrestricted;

        // Plan codes, not display names: catalog uses full-*, crash-*, writing-crash*, speaking-crash*.
        if (code.StartsWith("full-", StringComparison.Ordinal)) return CourseFamilyAccess.FullOnly;
        if (code.StartsWith("crash-", StringComparison.Ordinal)
            || code.StartsWith("writing-crash", StringComparison.Ordinal)
            || code.StartsWith("speaking-crash", StringComparison.Ordinal))
        {
            return CourseFamilyAccess.CrashOnly;
        }

        return CourseFamilyAccess.Unrestricted;
    }

    public static CourseFamilyAccess Resolve(EffectiveEntitlementSnapshot entitlement)
        => Resolve(entitlement.ProductCategory, entitlement.PlanCode);

    public static CourseFamilyAccess Union(IEnumerable<CourseFamilyAccess> parts)
    {
        var full = false;
        var crash = false;
        var any = false;
        foreach (var part in parts)
        {
            any = true;
            full |= part.FullCourse;
            crash |= part.CrashCourse;
        }

        if (!any) return CourseFamilyAccess.Unrestricted;
        if (full && crash) return CourseFamilyAccess.Unrestricted;
        if (full) return CourseFamilyAccess.FullOnly;
        if (crash) return CourseFamilyAccess.CrashOnly;
        return CourseFamilyAccess.Unrestricted;
    }

    public static CourseFamily ClassifyVideo(Domain.LibraryVideo video, IEnumerable<string>? extraLabels = null)
    {
        // Classification order: explicit batch:* tags first, then title/collection
        // labels, then Shared. This keeps admin tagging authoritative while allowing
        // unmarked content to stay visible unless the title/labels clearly indicate
        // a specific family.
        var tags = SplitTags(video.TagsCsv);
        var hasFullTag = tags.Contains(FullCourseOnlyTag);
        var hasCrashTag = false;
        foreach (var tag in tags)
        {
            if (CrashCourseOnlyTags.Contains(tag))
            {
                hasCrashTag = true;
                break;
            }
        }

        if (hasFullTag && hasCrashTag) return CourseFamily.Shared;
        if (hasFullTag) return CourseFamily.FullCourse;
        if (hasCrashTag) return CourseFamily.CrashCourse;
        if (tags.Contains(SharedTag)) return CourseFamily.Shared;

        var labels = new List<string>();
        if (!string.IsNullOrWhiteSpace(video.Title)) labels.Add(video.Title);
        if (extraLabels is not null)
        {
            foreach (var label in extraLabels)
            {
                if (!string.IsNullOrWhiteSpace(label)) labels.Add(label);
            }
        }

        return ClassifyLabels(labels);
    }

    public static CourseFamily ClassifyLabels(IEnumerable<string?> labels)
    {
        var crash = false;
        var full = false;
        foreach (var label in labels)
        {
            var family = ClassifyLabel(label);
            if (family == CourseFamily.CrashCourse) crash = true;
            else if (family == CourseFamily.FullCourse) full = true;
        }

        if (crash && !full) return CourseFamily.CrashCourse;
        if (full && !crash) return CourseFamily.FullCourse;
        return CourseFamily.Shared;
    }

    public static CourseFamily ClassifyLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return CourseFamily.Shared;
        var text = label.Trim().ToLowerInvariant();
        // "Full Crash Course" is crash — check crash markers first.
        if (IsCrashLabel(text)) return CourseFamily.CrashCourse;
        if (IsFullLabel(text)) return CourseFamily.FullCourse;
        return CourseFamily.Shared;
    }

    public static bool Allows(CourseFamilyAccess? access, CourseFamily family)
        => family is not CourseFamily.None
            && (access is not { } families || !families.IsRestricted || families.Allows(family));

    private static bool IsCrashLabel(string text) =>
        text.Contains("crash course", StringComparison.Ordinal)
        || text.Contains("crash-course", StringComparison.Ordinal)
        || text.Contains("fast-track crash", StringComparison.Ordinal)
        || text.Contains("fast track crash", StringComparison.Ordinal)
        || text.Contains("batch:crash-course", StringComparison.Ordinal)
        || text.Contains("batch:fast-track-crash", StringComparison.Ordinal)
        || text.Contains("batch:writing-sessions-crash-course", StringComparison.Ordinal);

    private static bool IsFullLabel(string text) =>
        text.Contains("full course", StringComparison.Ordinal)
        || text.Contains("full-course", StringComparison.Ordinal)
        || text.Contains("batch:full-course", StringComparison.Ordinal);

    private static HashSet<string> SplitTags(string? tagsCsv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tagsCsv)) return set;
        foreach (var raw in tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(raw);
        }

        return set;
    }
}
