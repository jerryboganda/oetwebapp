using System.Text;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Entitlements;

/// <summary>
/// The four Medicine crash/special package families covered by the
/// "Video Access Hierarchy &amp; Isolation Rules" specification (31 Aug 2026).
/// <see cref="None"/> = the package does not participate in this specification
/// (full courses, foundation, recalls, books, standalone sessions) and keeps the
/// generic Full/Crash visibility engine.
/// </summary>
public enum MedicinePackageFamily
{
    None = 0,

    /// <summary>§3A — Full Crash Course, +3 Letters, +5 Letters.</summary>
    FullCrash = 1,

    /// <summary>§3B — Recorded Writing Crash Course, +2/+3/+5/+7/+10 Letter Assessments.</summary>
    WritingCrash = 2,

    /// <summary>§3C — Recorded Speaking Crash Course (and the non-recorded label).</summary>
    SpeakingCrash = 3,

    /// <summary>§3D — Mega Special Package + Double Special Package.</summary>
    Special = 4,
}

/// <summary>How much of one subtest a package family exposes (§2 package family map).</summary>
public enum VideoSubtestGrant
{
    /// <summary>HIDE — the subtest card/navigation entry must not be rendered at all.</summary>
    Hidden = 0,

    /// <summary>SHOW* — the card is visible but its content is filtered by the §4 whitelist.</summary>
    Whitelisted = 1,

    /// <summary>SHOW — the standard library for that subtest.</summary>
    Full = 2,
}

/// <summary>
/// Resolved Video Library visibility for a learner under the Medicine crash/special
/// specification. <see cref="Applies"/> false = no package of this learner's is covered
/// by the document, so the generic engine (VisibilityScope / CourseFamilyPolicy) decides.
/// </summary>
public readonly record struct MedicineVideoAccess(
    bool Applies,
    VideoSubtestGrant Listening,
    VideoSubtestGrant Reading,
    VideoSubtestGrant Writing,
    VideoSubtestGrant Speaking)
{
    /// <summary>Not covered by the specification — the legacy engine stays authoritative.</summary>
    public static readonly MedicineVideoAccess Unrestricted = new(
        false, VideoSubtestGrant.Full, VideoSubtestGrant.Full, VideoSubtestGrant.Full, VideoSubtestGrant.Full);

    public VideoSubtestGrant For(string? subtest) => (subtest ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "listening" => Listening,
        "reading" => Reading,
        "writing" => Writing,
        "speaking" => Speaking,
        _ => VideoSubtestGrant.Hidden,
    };

    /// <summary>
    /// Set-union across a learner's effective packages: a learner holding several packages
    /// sees the union of what each one includes.
    ///
    /// Three-valued by design:
    ///   • null       — a package that carries no video library of its own (recalls, the Tutor
    ///                  Book, a standalone speaking session, the Basic English foundation
    ///                  course). It is NEUTRAL: it neither grants nor widens Video Library
    ///                  visibility, so buying one alongside a crash package can never
    ///                  un-isolate it.
    ///   • Unrestricted — a video-bearing package outside this specification (a Full Course).
    ///                  It dominates, exactly like <see cref="CourseFamilyPolicy.Union"/>, so a
    ///                  Full Course held alongside a crash package keeps its own full library.
    ///   • a family set — one of the four documented families; these accumulate per subtest.
    ///
    /// All-neutral (or empty) resolves to <see cref="Unrestricted"/>, preserving today's
    /// behaviour for learners who hold only non-course products.
    /// </summary>
    public static MedicineVideoAccess Union(IEnumerable<MedicineVideoAccess?> parts)
    {
        var any = false;
        var listening = VideoSubtestGrant.Hidden;
        var reading = VideoSubtestGrant.Hidden;
        var writing = VideoSubtestGrant.Hidden;
        var speaking = VideoSubtestGrant.Hidden;

        foreach (var part in parts)
        {
            if (part is not { } value) continue;   // neutral — contributes nothing
            if (!value.Applies) return Unrestricted;
            any = true;
            listening = Max(listening, value.Listening);
            reading = Max(reading, value.Reading);
            writing = Max(writing, value.Writing);
            speaking = Max(speaking, value.Speaking);
        }

        return any
            ? new MedicineVideoAccess(true, listening, reading, writing, speaking)
            : Unrestricted;
    }

    private static VideoSubtestGrant Max(VideoSubtestGrant a, VideoSubtestGrant b) => a >= b ? a : b;
}

/// <summary>
/// Source of truth for the Medicine Crash / Writing Crash / Speaking Crash / Mega + Double
/// Special video entitlements ("VIDEO ACCESS HIERARCHY &amp; ISOLATION RULES", 31 Aug 2026).
///
/// §7 precedence, implemented end to end:
///   1. profession (Medicine — enforced here for Writing/Speaking, §1.1 + §5),
///   2. package family (structured ProductCategory + plan code, never display titles),
///   3. the allowed top-level subtest set (§2),
///   4. the collection/video whitelist inside each allowed subtest (§3/§4),
///   5. the same rules server-side for search / saved / continue-watching / deep links,
///   6. counts are computed only from what survives.
///
/// Where this document conflicts with the legacy tag/VisibilityScope engine it wins, for
/// these four families only — every other package keeps the generic engine untouched.
/// </summary>
public static class MedicinePackageVideoPolicy
{
    // ── Catalog ProductCategory values (Data/Seeds/oet-2026-catalog.json) ───────────────

    private static readonly HashSet<string> FullCrashCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "crash_course", "crash_course_bundle",
    };

    private static readonly HashSet<string> WritingCrashCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "writing_crash", "writing_crash_bundle",
    };

    private static readonly HashSet<string> SpeakingCrashCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "speaking_crash", "speaking_crash_bundle",
    };

    private static readonly HashSet<string> SpecialCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "combo_mega", "combo_double",
    };

    /// <summary>The only profession these package families may ever expose (§1.1, §5).</summary>
    public const string MedicineProfessionId = "medicine";

    /// <summary>
    /// Classify a single package from its STRUCTURED fields. Order matters: the more
    /// specific writing/speaking crash codes are tested before the generic "crash-" prefix
    /// so <c>writing-crash-10</c> never resolves to the Full Crash family.
    /// </summary>
    public static MedicinePackageFamily Classify(string? productCategory, string? planCode)
    {
        var category = productCategory?.Trim() ?? string.Empty;
        if (WritingCrashCategories.Contains(category)) return MedicinePackageFamily.WritingCrash;
        if (SpeakingCrashCategories.Contains(category)) return MedicinePackageFamily.SpeakingCrash;
        if (FullCrashCategories.Contains(category)) return MedicinePackageFamily.FullCrash;
        if (SpecialCategories.Contains(category)) return MedicinePackageFamily.Special;

        var code = (planCode ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length == 0) return MedicinePackageFamily.None;
        if (code.StartsWith("writing-crash", StringComparison.Ordinal)) return MedicinePackageFamily.WritingCrash;
        if (code.StartsWith("speaking-crash", StringComparison.Ordinal)) return MedicinePackageFamily.SpeakingCrash;
        if (code.StartsWith("crash-", StringComparison.Ordinal) || code == "crash-course") return MedicinePackageFamily.FullCrash;
        if (code == "mega-special" || code == "double-special") return MedicinePackageFamily.Special;
        return MedicinePackageFamily.None;
    }

    /// <summary>§2 package family map — the allowed top-level subtest set per family.</summary>
    public static MedicineVideoAccess Resolve(MedicinePackageFamily family) => family switch
    {
        // §3A: all four subtests; Writing filtered by the §4 whitelist; standard Medicine Speaking.
        MedicinePackageFamily.FullCrash => new MedicineVideoAccess(
            true, VideoSubtestGrant.Full, VideoSubtestGrant.Full, VideoSubtestGrant.Whitelisted, VideoSubtestGrant.Full),

        // §3B: Writing only, filtered by the §4 whitelist.
        MedicinePackageFamily.WritingCrash => new MedicineVideoAccess(
            true, VideoSubtestGrant.Hidden, VideoSubtestGrant.Hidden, VideoSubtestGrant.Whitelisted, VideoSubtestGrant.Hidden),

        // §3C: Speaking only — the Medicine Speaking sessions/videos in Arabic and English.
        MedicinePackageFamily.SpeakingCrash => new MedicineVideoAccess(
            true, VideoSubtestGrant.Hidden, VideoSubtestGrant.Hidden, VideoSubtestGrant.Hidden, VideoSubtestGrant.Full),

        // §3D: Writing + Speaking only, both filtered by their special-package whitelists.
        MedicinePackageFamily.Special => new MedicineVideoAccess(
            true, VideoSubtestGrant.Hidden, VideoSubtestGrant.Hidden, VideoSubtestGrant.Whitelisted, VideoSubtestGrant.Whitelisted),

        _ => MedicineVideoAccess.Unrestricted,
    };

    /// <summary>
    /// Product categories that carry no Video Library of their own. They are NEUTRAL in
    /// <see cref="MedicineVideoAccess.Union"/>: holding one alongside a crash package must not
    /// re-expose the subtests that package excludes. Basic English Course videos remain visible
    /// to their buyers through the separate Basic English scope, not through this policy.
    /// </summary>
    private static readonly HashSet<string> NonVideoCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "foundation", "recall_package", "book", "speaking_session",
    };

    /// <summary>
    /// Per-package resolution for the union. Returns null for a package that carries no video
    /// library (neutral), <see cref="MedicineVideoAccess.Unrestricted"/> for a video-bearing
    /// package outside this specification (Full Courses, custom/legacy categories — fail-open),
    /// and the family's subtest set for the four documented families.
    /// </summary>
    public static MedicineVideoAccess? ResolveOrNeutral(string? productCategory, string? planCode)
    {
        var family = Classify(productCategory, planCode);
        if (family != MedicinePackageFamily.None) return Resolve(family);
        return NonVideoCategories.Contains(productCategory?.Trim() ?? string.Empty)
            ? null
            : MedicineVideoAccess.Unrestricted;
    }

    public static MedicineVideoAccess Resolve(string? productCategory, string? planCode)
        => ResolveOrNeutral(productCategory, planCode) ?? MedicineVideoAccess.Unrestricted;

    // ── §4 whitelist matching ──────────────────────────────────────────────────────────

    /// <summary>
    /// §4 Writing whitelist: title/path contains "Crash Course" OR "Fast Track", PLUS the two
    /// explicit English collections (Medicine / English / Sessions and Medicine / English /
    /// Workshops). Everything else — Medicine / Arabic / Batch 1 …, Medicine / Arabic / New
    /// Batch …, other professions — is hidden. Matching is case-insensitive and tolerant of
    /// spacing/hyphenation ("Fast Track", "Fast-Track", "FastTrack").
    /// </summary>
    public static bool IsWritingAllowed(IEnumerable<string?> labels)
    {
        foreach (var label in labels)
        {
            var normalized = Normalize(label);
            if (normalized.Length == 0) continue;
            if (IsCrashOrFastTrack(normalized)) return true;
            if (normalized.Contains("medicine / english / sessions", StringComparison.Ordinal)) return true;
            if (normalized.Contains("medicine / english / workshops", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    /// §3D Speaking whitelist for the Mega/Double Special packages: crash-course / fast-track
    /// Speaking content, plus the applicable Medicine English Speaking sessions/workshops.
    /// Unrelated full-course Speaking collections stay hidden. The Medicine constraint on the
    /// English collections comes from the profession gate (<see cref="IsMedicineScoped"/>),
    /// because the live English Speaking folders are named "English / Sessions" without a
    /// Medicine path segment.
    /// </summary>
    public static bool IsSpecialSpeakingAllowed(IEnumerable<string?> labels)
    {
        foreach (var label in labels)
        {
            var normalized = Normalize(label);
            if (normalized.Length == 0) continue;
            if (IsCrashOrFastTrack(normalized)) return true;
            if (normalized.Contains("english / sessions", StringComparison.Ordinal)) return true;
            if (normalized.Contains("english / workshops", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static bool IsCrashOrFastTrack(string normalized)
        => normalized.Contains("crash course", StringComparison.Ordinal)
            || normalized.Contains("fast track", StringComparison.Ordinal)
            || Compact(normalized).Contains("crashcourse", StringComparison.Ordinal)
            || Compact(normalized).Contains("fasttrack", StringComparison.Ordinal);

    /// <summary>
    /// Case-folds a folder/title path and normalises separators so "Fast-Track",
    /// "Fast  Track" and "Medicine/English/Sessions" all compare equal to their canonical
    /// spelling. Hyphens and underscores become spaces; runs of whitespace collapse; every
    /// path separator becomes exactly " / ".
    /// </summary>
    public static string Normalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;

        var builder = new StringBuilder(label.Length + 8);
        var pendingSpace = false;
        foreach (var ch in label.Trim().ToLowerInvariant())
        {
            if (ch == '/' || ch == '\\')
            {
                TrimTrailingSpace(builder);
                builder.Append(" / ");
                pendingSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
            {
                // Never double up on the space a preceding separator already emitted.
                pendingSpace = builder.Length > 0 && builder[^1] != ' ';
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(ch);
        }

        TrimTrailingSpace(builder);
        return builder.ToString();

        static void TrimTrailingSpace(StringBuilder sb)
        {
            while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        }
    }

    /// <summary>Separator-free form so "FastTrack" matches "Fast Track" (§4 note).</summary>
    private static string Compact(string normalized)
    {
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
        }
        return builder.ToString();
    }

    // ── Subtest resolution ─────────────────────────────────────────────────────────────

    private static readonly HashSet<string> KnownSubtests = new(StringComparer.OrdinalIgnoreCase)
    {
        "listening", "reading", "writing", "speaking",
    };

    /// <summary>
    /// The OET subtest a video belongs to: its own SubtestCode when set, otherwise the first
    /// segment of its collection path(s) — the Video Library encodes the full breadcrumb in
    /// each collection title ("Writing / Medicine / English / Sessions"). Returns null when the
    /// subtest cannot be determined, which the caller treats as HIDE (§CORE: anything not
    /// explicitly included must be completely absent).
    /// </summary>
    public static string? ResolveSubtest(LibraryVideo video, IEnumerable<string?> labels)
    {
        var code = video.SubtestCode?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(code) && KnownSubtests.Contains(code)) return code;

        foreach (var label in labels)
        {
            var normalized = Normalize(label);
            if (normalized.Length == 0) continue;
            var first = normalized.Split(" / ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first is not null && KnownSubtests.Contains(first)) return first;
        }

        return null;
    }

    // ── §1.1 / §5 profession isolation ─────────────────────────────────────────────────

    /// <summary>
    /// These package families are Medicine-only, so a Writing/Speaking video may only appear
    /// when it targets Medicine. Listening/Reading are profession-neutral by contract
    /// (<see cref="CourseContentMatrix"/> forbids profession targets on them), so untargeted
    /// content stays shared.
    /// </summary>
    public static bool IsMedicineScoped(string subtest, IReadOnlyCollection<string> professionTargets)
    {
        if (subtest is not ("writing" or "speaking")) return true;
        if (professionTargets.Count == 0) return false; // W/S must declare a profession; unevaluable targeting is a lock.
        return professionTargets.Contains(MedicineProfessionId, StringComparer.OrdinalIgnoreCase);
    }
}
