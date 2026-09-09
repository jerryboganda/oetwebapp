using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// Bounded, client-supplied hint about what the learner is looking at.
///
/// <para>
/// The source specification is emphatic that this carries <b>identifiers, not
/// content</b>: the server resolves what those identifiers mean and whether the
/// learner may see them. Screen context can bias retrieval toward the right
/// source; it can never widen entitlement
/// (docs/ai-learning-companion/UX_SURFACES_PERSONA_ACTIONS.md §6).
/// </para>
/// </summary>
public sealed record CompanionContextEnvelope(
    string? Surface = null,
    string? RouteId = null,
    string? ResourceId = null,
    string? AttemptId = null,
    string? QuestionId = null,
    int? VideoTimeSeconds = null,
    string? SubtestCode = null,
    /// <summary>Set by the server from the attempt, never trusted from the client.</summary>
    bool ExamMode = false);

/// <summary>
/// Everything the companion is allowed to know about this turn, resolved
/// server-side from the authenticated principal.
///
/// <para>
/// Client-supplied tier, profession or entitlement fields are hints only. Every
/// field here comes from the database on this request.
/// </para>
/// </summary>
public sealed record CompanionTurnContext
{
    public required string UserId { get; init; }

    /// <summary>Profession drives retrieval scope, examples and rule precedence.</summary>
    public ExamProfession Profession { get; init; } = ExamProfession.Medicine;

    /// <summary>Raw profession id as stored, for diagnostics and citations.</summary>
    public string? ProfessionId { get; init; }

    public string? ExamTypeCode { get; init; }

    public DateOnly? ExamDate { get; init; }

    public int? DaysUntilExam { get; init; }

    public string? TargetGrade { get; init; }

    public string? TargetCountry { get; init; }

    /// <summary>`anonymous` | `free` | `trial` | `paid`.</summary>
    public string Tier { get; init; } = "free";

    /// <summary>Content packages / scopes this learner may have retrieved for them.</summary>
    public IReadOnlyList<string> EntitlementScopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Package isolation scopes the learner's packages resolve (FULL_MEDICINE,
    /// FULL_NURSING, FULL_PHARMACY, CRASH). Empty means shared-only access.
    ///
    /// <para>
    /// Kept separate from <see cref="EntitlementScopes"/>, which is a flat bag
    /// used for single-value <c>RequiredEntitlementScope</c> matching. Package
    /// isolation is a set-membership question — a learner may hold both Full
    /// Medicine and Crash — and flattening it would make "either" inexpressible.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> PackageScopes { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool HasEligibleSubscription { get; init; }

    /// <summary>Candidate-facing AI Credit balance, for the "exact charge" contract.</summary>
    public int AiCreditsRemaining { get; init; }

    /// <summary>Preferred locale, e.g. `en` or `ar`. Drives language and RTL.</summary>
    public string Locale { get; init; } = "en";

    /// <summary>
    /// How this learner wants to be taught (F-011, F-050, F-052, F-055).
    /// Never null — an absent row means every default.
    /// </summary>
    public CompanionPreference Preferences { get; init; } = new() { UserId = string.Empty };

    /// <summary>Bounded surface context. Never expands entitlement.</summary>
    public CompanionContextEnvelope Envelope { get; init; } = new();

    /// <summary>
    /// True while the learner is inside a protected attempt. The companion must
    /// refuse hints, answers and coaching until submission (F-155).
    /// </summary>
    public bool ExamMode { get; init; }

    // ── kill switches, resolved once per turn ────────────────────────────────
    public bool RetrievalEnabled { get; init; }
    public bool ActionsEnabled { get; init; }
    public bool CreditConsumptionEnabled { get; init; }

    /// <summary>
    /// TV-006 / TV-007. While false the companion gives criterion feedback and
    /// must not state a numeric Writing/Speaking band.
    /// </summary>
    public bool ScoreDisplayEnabled { get; init; }
}
