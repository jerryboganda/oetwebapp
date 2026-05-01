namespace OetLearner.Api.Domain;

/// <summary>
/// Canonical OET mock-type taxonomy per <c>docs/MOCKS-MODULE-PLAN-V2.md</c> Wave 1.
/// All values are stored lower-case in <see cref="MockBundle.MockType"/> and
/// <see cref="MockAttempt.MockType"/>. The legacy values <c>full</c> and <c>sub</c>
/// remain canonical so existing data and admin tooling continues to function;
/// the new variants are flavours that the resolver classifies into a "shape"
/// (<see cref="Shape.FullShape"/> or <see cref="Shape.SubShape"/>) for routing.
/// </summary>
public static class MockTypes
{
    /// <summary>Complete OET simulation across all four sub-tests.</summary>
    public const string Full = "full";

    /// <summary>Listening + Reading + Writing in one sitting (Speaking scheduled separately).</summary>
    public const string Lrw = "lrw";

    /// <summary>Single sub-test (Listening / Reading / Writing / Speaking).</summary>
    public const string Sub = "sub";

    /// <summary>Single part within a sub-test (e.g. Reading Part A only).</summary>
    public const string Part = "part";

    /// <summary>First-attempt diagnostic that establishes baseline + study path.</summary>
    public const string Diagnostic = "diagnostic";

    /// <summary>Final-readiness mock taken before booking the real exam (strict full-mock).</summary>
    public const string FinalReadiness = "final_readiness";

    /// <summary>Targeted remedial mock generated from weak-area analysis.</summary>
    public const string Remedial = "remedial";

    /// <summary>All canonical mock-type tokens.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Full, Lrw, Sub, Part, Diagnostic, FinalReadiness, Remedial,
    };

    /// <summary>Mock types that fan out to multiple sub-tests in canonical order.</summary>
    public static readonly IReadOnlySet<string> FullShapeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Full, Lrw, Diagnostic, FinalReadiness,
    };

    /// <summary>Mock types that target a single sub-test or part.</summary>
    public static readonly IReadOnlySet<string> SubShapeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Sub, Part, Remedial,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>True when the mock type spans multiple sub-tests (Listening → Reading → Writing → Speaking).</summary>
    public static bool IsFullShape(string? value) => value is not null && FullShapeTypes.Contains(value);

    /// <summary>True when the mock type targets a single sub-test or smaller part.</summary>
    public static bool IsSubShape(string? value) => value is not null && SubShapeTypes.Contains(value);

    /// <summary>True when the mock type excludes Speaking from the full sequence (LRW only).</summary>
    public static bool ExcludesSpeaking(string? value)
        => string.Equals(value, Lrw, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the human-readable label for a mock type.</summary>
    public static string Label(string value) => value switch
    {
        Full => "Full Mock",
        Lrw => "LRW Mock",
        Sub => "Sub-test Mock",
        Part => "Part Mock",
        Diagnostic => "Diagnostic Mock",
        FinalReadiness => "Final Readiness Mock",
        Remedial => "Remedial Mock",
        _ => value,
    };

    /// <summary>Default strictness pairing per spec §3 + §13.</summary>
    public static string DefaultStrictness(string mockType) => mockType switch
    {
        Diagnostic => MockStrictness.Learning,
        Remedial => MockStrictness.Learning,
        FinalReadiness => MockStrictness.FinalReadiness,
        _ => MockStrictness.Exam,
    };
}

/// <summary>
/// Delivery model per spec §2 — how the mock is administered to the learner.
/// </summary>
public static class MockDeliveryModes
{
    /// <summary>Default — on-screen test with timers and digital answers.</summary>
    public const string Computer = "computer";

    /// <summary>Printable booklet + answer sheet, marked by teacher.</summary>
    public const string Paper = "paper";

    /// <summary>Remote test simulation matching OET@Home regulations.</summary>
    public const string OetHome = "oet_home";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Computer, Paper, OetHome,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>
/// Strictness preset per spec §3. Drives timer locking, hint suppression, and replay rules
/// at the sub-test grader layer.
/// </summary>
public static class MockStrictness
{
    /// <summary>Pause/replay/hints allowed; useful for first attempts.</summary>
    public const string Learning = "learning";

    /// <summary>Default — strict timers, one-play audio, no hints.</summary>
    public const string Exam = "exam";

    /// <summary>Strictest — exam rules + readiness flags surfaced post-attempt.</summary>
    public const string FinalReadiness = "final_readiness";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Learning, Exam, FinalReadiness,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
