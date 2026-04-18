using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Authoring — Slice R1
//
// Entities that turn a Reading ContentPaper into a machine-gradable tree:
//   ContentPaper  (subtestCode="reading")
//   └── ReadingPart         (A=20, B=6, C=16 items — canonical OET shape)
//         └── ReadingText   (passages within the part)
//         └── ReadingQuestion (scorable items with 5 type variants)
//
// Attempt side:
//   ReadingAttempt (per user per paper)
//     └── ReadingAnswer (one per answered question)
//
// See docs/READING-AUTHORING-PLAN.md and docs/READING-AUTHORING-POLICY.md.
// ═════════════════════════════════════════════════════════════════════════════

public enum ReadingPartCode
{
    A = 1,
    B = 2,
    C = 3,
}

/// <summary>
/// Question-type vocabulary. Changing an existing integer value is a
/// migration-breaking change (values persist in the DB).
/// </summary>
public enum ReadingQuestionType
{
    /// <summary>Part A: match a statement to one of texts 1-4.</summary>
    MatchingTextReference = 0,
    /// <summary>Part A: gap-fill / short-answer graded by string compare.</summary>
    ShortAnswer = 1,
    /// <summary>Part A: sentence completion from a phrase bank.</summary>
    SentenceCompletion = 2,
    /// <summary>Part B: 3-option multiple choice.</summary>
    MultipleChoice3 = 3,
    /// <summary>Part C: 4-option multiple choice.</summary>
    MultipleChoice4 = 4,
}

/// <summary>
/// Lifecycle of a learner attempt.
/// </summary>
public enum ReadingAttemptStatus
{
    InProgress = 0,
    Submitted = 1,
    Expired = 2,
    Abandoned = 3,
}

/// <summary>One of three parts of a Reading paper.</summary>
[Index(nameof(PaperId), nameof(PartCode), IsUnique = true,
    Name = "UX_ReadingPart_Paper_PartCode")]
public class ReadingPart
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PaperId { get; set; } = default!;   // FK → ContentPaper.Id

    public ReadingPartCode PartCode { get; set; }

    public int TimeLimitMinutes { get; set; }         // canonical default: A=15, B+C=45 shared

    /// <summary>Max raw score achievable on this part, e.g. 20 / 6 / 16.</summary>
    public int MaxRawScore { get; set; }

    [MaxLength(1024)]
    public string? Instructions { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ReadingText> Texts { get; set; } = new List<ReadingText>();
    public ICollection<ReadingQuestion> Questions { get; set; } = new List<ReadingQuestion>();
}

/// <summary>
/// One passage within a part. Part A = 4 short texts (single topic),
/// Part B = 6 short workplace texts, Part C = 2 long articles.
/// </summary>
[Index(nameof(ReadingPartId), nameof(DisplayOrder))]
public class ReadingText
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReadingPartId { get; set; } = default!;

    public int DisplayOrder { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    /// <summary>Copyright attribution — required before publish.</summary>
    [MaxLength(256)]
    public string? Source { get; set; }

    /// <summary>Sanitised HTML. Rendered verbatim in the player.</summary>
    public string BodyHtml { get; set; } = string.Empty;

    public int WordCount { get; set; }

    [MaxLength(64)]
    public string? TopicTag { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ReadingPart? Part { get; set; }
}

/// <summary>
/// One scorable item.
///
/// <para>
/// Answer storage: both <see cref="OptionsJson"/> and
/// <see cref="CorrectAnswerJson"/> are strict JSON. Shape depends on
/// question type; grader strategies dispatch on
/// <see cref="QuestionType"/>. Client-facing DTOs NEVER include
/// <see cref="CorrectAnswerJson"/>, <see cref="ExplanationMarkdown"/>, or
/// <see cref="AcceptedSynonymsJson"/>.
/// </para>
/// </summary>
[Index(nameof(ReadingPartId), nameof(DisplayOrder))]
public class ReadingQuestion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReadingPartId { get; set; } = default!;

    /// <summary>Optional — Part A matching items may span all texts.</summary>
    [MaxLength(64)]
    public string? ReadingTextId { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>Points earned for a correct answer. Default 1.</summary>
    public int Points { get; set; } = 1;

    public ReadingQuestionType QuestionType { get; set; }

    [MaxLength(2048)]
    public string Stem { get; set; } = default!;

    /// <summary>JSON array/object holding the selectable options for MCQ /
    /// matching types. Unused for short-answer / sentence-completion.</summary>
    public string OptionsJson { get; set; } = "[]";

    /// <summary>Correct answer(s). Shape depends on type:
    /// MCQ → <c>"A"</c>; matching (multi) → <c>["1","3"]</c>;
    /// short answer → <c>"ORT"</c>.</summary>
    public string CorrectAnswerJson { get; set; } = "\"\"";

    /// <summary>Accepted alternate spellings / synonyms. Array of strings.
    /// Only consulted when <see cref="ReadingQuestionType.ShortAnswer"/>
    /// or <see cref="ReadingQuestionType.SentenceCompletion"/>.</summary>
    public string? AcceptedSynonymsJson { get; set; }

    /// <summary>Case-sensitive exact match? Default false = case-insensitive.</summary>
    public bool CaseSensitive { get; set; }

    [MaxLength(4096)]
    public string? ExplanationMarkdown { get; set; }

    [MaxLength(32)]
    public string? SkillTag { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ReadingPart? Part { get; set; }
    public ReadingText? Text { get; set; }
}

/// <summary>
/// A learner's attempt at a Reading paper. Timer, snapshot of policy, final
/// score — all here. One row per run-through; retries produce new rows.
/// </summary>
[Index(nameof(UserId), nameof(Status))]
[Index(nameof(PaperId), nameof(StartedAt))]
public class ReadingAttempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string PaperId { get; set; } = default!;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }

    public ReadingAttemptStatus Status { get; set; } = ReadingAttemptStatus.InProgress;

    /// <summary>Raw marks. Null until graded. 42 max on real papers.</summary>
    public int? RawScore { get; set; }

    /// <summary>Scaled 0-500 via <c>OetScoring.RawToScaled</c>. Null until graded.</summary>
    public int? ScaledScore { get; set; }

    /// <summary>Max raw achievable at time of attempt start (snapshot).</summary>
    public int MaxRawScore { get; set; }

    /// <summary>Snapshot of the Reading policy that was in effect when the
    /// attempt started. Ensures policy changes don't retroactively alter an
    /// in-flight or past attempt.</summary>
    public string PolicySnapshotJson { get; set; } = "{}";

    /// <summary>Paper revision id at attempt-start. Protects in-flight
    /// attempts from mid-flight content edits.</summary>
    [MaxLength(64)]
    public string? PaperRevisionId { get; set; }

    public ICollection<ReadingAnswer> Answers { get; set; } = new List<ReadingAnswer>();
}

/// <summary>One answer. Written by autosave, graded at submit.</summary>
[Index(nameof(ReadingAttemptId), nameof(ReadingQuestionId), IsUnique = true,
    Name = "UX_ReadingAnswer_Attempt_Question")]
public class ReadingAnswer
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReadingAttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string ReadingQuestionId { get; set; } = default!;

    /// <summary>JSON shaped to match the question type (echoes
    /// <see cref="ReadingQuestion.CorrectAnswerJson"/>).</summary>
    public string UserAnswerJson { get; set; } = "\"\"";

    public bool? IsCorrect { get; set; }
    public int PointsEarned { get; set; }

    public DateTimeOffset AnsweredAt { get; set; }

    public ReadingAttempt? Attempt { get; set; }
    public ReadingQuestion? Question { get; set; }
}

/// <summary>
/// Singleton global policy (id = <c>global</c>). Wraps every option
/// documented in <c>docs/READING-AUTHORING-POLICY.md</c>.
/// </summary>
public class ReadingPolicy
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "global";

    // §1 — Retry policy
    public int AttemptsPerPaperPerUser { get; set; }   // 0 = unlimited
    public int AttemptCooldownMinutes { get; set; }
    [MaxLength(16)]
    public string BestScoreDisplay { get; set; } = "best"; // best|latest|average|first
    public bool ShowPastAttempts { get; set; } = true;
    public bool AllowAttemptOnArchivedPaper { get; set; }

    // §2 — Timer policy
    [MaxLength(16)]
    public string PartATimerStrictness { get; set; } = "hard_lock";
    public int PartATimerMinutes { get; set; } = 15;
    public int PartBCTimerMinutes { get; set; } = 45;
    public int GracePeriodSeconds { get; set; } = 10;
    [MaxLength(32)]
    public string OnExpirySubmitPolicy { get; set; } = "auto_submit_graded";
    public string CountdownWarningsJson { get; set; } = "[300,60,15]";

    // §3 — Grading strategy
    public string EnabledQuestionTypesJson { get; set; } =
        "[\"MatchingTextReference\",\"ShortAnswer\",\"SentenceCompletion\",\"MultipleChoice3\",\"MultipleChoice4\"]";
    [MaxLength(32)]
    public string ShortAnswerNormalisation { get; set; } = "trim_collapse_case_insensitive";
    public bool ShortAnswerAcceptSynonyms { get; set; } = true;
    public bool MatchingAllowPartialCredit { get; set; } = true;
    [MaxLength(32)]
    public string SentenceCompletionStrictness { get; set; } = "exact_from_bank";
    [MaxLength(32)]
    public string UnknownTypeFallbackPolicy { get; set; } = "skip_with_zero";

    // §4 — Explanation + review policy
    public bool ShowExplanationsAfterSubmit { get; set; } = true;
    public bool ShowExplanationsOnlyIfWrong { get; set; }
    public bool ShowCorrectAnswerOnReview { get; set; } = true;
    public bool AllowResultDownload { get; set; } = true;
    public bool AllowResultSharing { get; set; }

    // §5 — AI-assisted extraction
    public bool AiExtractionEnabled { get; set; } = true;
    public bool AiExtractionRequireHumanApproval { get; set; } = true;
    public int AiExtractionMaxRetriesPerPaper { get; set; } = 5;
    [MaxLength(64)]
    public string? AiExtractionModelOverride { get; set; }
    [MaxLength(16)]
    public string AiExtractionStrictSchemaMode { get; set; } = "strict";

    // §6 — Question-bank
    public bool QuestionBankEnabled { get; set; }
    [MaxLength(32)]
    public string AssemblyStrategy { get; set; } = "fixed";
    public bool AllowLearnerRandomisation { get; set; }

    // §7 — Accessibility
    public bool FontScaleUserControl { get; set; } = true;
    public bool HighContrastMode { get; set; } = true;
    public bool ScreenReaderOptimised { get; set; } = true;
    public bool AllowPaperReadingMode { get; set; }
    public bool ExtraTimeApprovalWorkflow { get; set; } = true;

    // §8 — Security + integrity
    public bool RequireFreshAuthForSubmit { get; set; }
    public bool AllowMultipleConcurrentAttempts { get; set; }
    [MaxLength(32)]
    public string AttemptIpPinning { get; set; } = "off"; // off|on|warn
    public int SubmitRateLimitPerMinute { get; set; } = 5;
    public int AutosaveRateLimitPerMinute { get; set; } = 120;
    public bool PreventMultipleTabs { get; set; }

    // §9 — Retention
    public int RetainAnswerRowsDays { get; set; } = 730;
    public int RetainAttemptHeadersDays { get; set; } = 3650;
    public bool AnonymiseOnAccountDelete { get; set; } = true;
    public bool ShareAnonymousAnalytics { get; set; } = true;

    // §10 — Lifecycle
    public bool AllowPausingAttempt { get; set; }
    public bool AutoExpireWorkerEnabled { get; set; } = true;
    public int AutoExpireAfterMinutes { get; set; } = 180;
    public bool AllowResumeAfterExpiry { get; set; }

    [ConcurrencyCheck]
    public int RowVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

/// <summary>
/// Per-user overrides for Reading policy. Currently: extra-time entitlements
/// and forced-disable flag. Honoured when present; falls back to global
/// policy defaults otherwise.
/// </summary>
public class ReadingUserPolicyOverride
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Accessibility-grant extra-time, % bump applied to all
    /// Reading timers. 0 = no entitlement.</summary>
    public int ExtraTimeEntitlementPct { get; set; }

    /// <summary>If true, this user cannot start new Reading attempts until
    /// the flag is cleared. Reason is captured for audit.</summary>
    public bool BlockAttempts { get; set; }

    [MaxLength(512)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string? GrantedByAdminId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
