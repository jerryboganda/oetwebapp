using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingCoachSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int SuggestionsGenerated { get; set; }
    public int SuggestionsAccepted { get; set; }
    public int SuggestionsDismissed { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}

public class WritingCoachSuggestion
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(32)]
    public string SuggestionType { get; set; } = default!; // "grammar", "vocabulary", "structure", "tone", "conciseness", "format"

    public string OriginalText { get; set; } = default!;
    public string SuggestedText { get; set; } = default!;

    [MaxLength(512)]
    public string Explanation { get; set; } = default!;

    public int StartOffset { get; set; }                   // Character offset in document
    public int EndOffset { get; set; }

    [MaxLength(16)]
    public string? Resolution { get; set; }                // "accepted", "dismissed", null (pending)

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Audit P2-2 closure (May 2026). One row per rule-engine / AI finding
/// produced by <c>WritingEvaluationPipeline</c>. The row is the canonical,
/// queryable source for the admin "Writing rule-violation analytics"
/// dashboard. <c>Attempt.AnalysisJson.rulebookFindings</c> remains the
/// learner-facing copy; this table is admin-only.
/// </summary>
public class WritingRuleViolation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Owning writing attempt id (<c>Attempts.Id</c>).</summary>
    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    /// <summary>Owning evaluation id (<c>Evaluations.Id</c>) when the
    /// pipeline reached the persist phase. May be null on rule-engine-only
    /// pre-runs (kill-switch / failure paths) — but in practice the
    /// pipeline always has an <c>Evaluation</c> row by the time it persists
    /// findings.</summary>
    [MaxLength(64)]
    public string? EvaluationId { get; set; }

    /// <summary>Learner who produced the attempt — denormalized for
    /// analytics queries (avoid the join through <c>Attempts</c>).</summary>
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Profession id at grading time (<c>medicine</c>, <c>nursing</c>,
    /// etc.). Denormalized so the analytics endpoint can group by profession
    /// without joining content metadata.</summary>
    [MaxLength(64)]
    public string Profession { get; set; } = default!;

    /// <summary>Letter type at grading time (e.g. <c>routine_referral</c>,
    /// <c>discharge</c>).</summary>
    [MaxLength(64)]
    public string LetterType { get; set; } = default!;

    /// <summary>Rule id from the rulebook (e.g. <c>greeting_correct</c>).</summary>
    [MaxLength(128)]
    public string RuleId { get; set; } = default!;

    /// <summary>Lowercase severity token: <c>critical</c> / <c>major</c> /
    /// <c>minor</c> / <c>info</c>.</summary>
    [MaxLength(16)]
    public string Severity { get; set; } = default!;

    /// <summary>Source: <c>rulebook</c> (deterministic engine) or
    /// <c>ai</c> (gateway response, after rule-engine merge).</summary>
    [MaxLength(16)]
    public string Source { get; set; } = default!;

    /// <summary>Verbatim message surfaced to the learner. Trimmed to the
    /// per-row column limit; full text remains in attempt analysis JSON.</summary>
    [MaxLength(1024)]
    public string Message { get; set; } = default!;

    /// <summary>Optional anchored quote from the candidate's letter that
    /// triggered the finding.</summary>
    [MaxLength(1024)]
    public string? Quote { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
