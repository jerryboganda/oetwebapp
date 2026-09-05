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

    public bool HasEligibleSubscription { get; init; }

    /// <summary>Candidate-facing AI Credit balance, for the "exact charge" contract.</summary>
    public int AiCreditsRemaining { get; init; }

    /// <summary>Preferred locale, e.g. `en` or `ar`. Drives language and RTL.</summary>
    public string Locale { get; init; } = "en";

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
