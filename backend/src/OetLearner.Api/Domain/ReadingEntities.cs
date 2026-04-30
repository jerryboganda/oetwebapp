using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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

[JsonConverter(typeof(JsonStringEnumConverter))]
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
[JsonConverter(typeof(JsonStringEnumConverter))]
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
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReadingAttemptStatus
{
    InProgress = 0,
    Submitted = 1,
    Expired = 2,
    Abandoned = 3,
}

/// <summary>
/// Reading attempt mode. Phase 3 (practice mode + drills + error bank).
///
/// <list type="bullet">
/// <item><description><c>Exam</c>: strict OET simulation. Part A 15-min hard
/// lock, Parts B+C 45-min shared timer, single concurrent attempt, no
/// mid-attempt feedback. Default for back-compat.</description></item>
/// <item><description><c>Learning</c>: untimed teaching mode against a full
/// paper. No Part A hard-lock, may run alongside an exam attempt, learner
/// receives explanations after submit. Marked NON-STANDARD in the UI.</description></item>
/// <item><description><c>Drill</c>: skill / part-scoped drill (Part A scan,
/// Part B distractor, Part C inference, etc.). <see cref="ReadingAttempt.ScopeJson"/>
/// captures the question subset.</description></item>
/// <item><description><c>MiniTest</c>: 5/10/15-minute timed mini-test against
/// a sampled subset of questions. Same enforcement as Exam but scope is a
/// subset.</description></item>
/// <item><description><c>ErrorBank</c>: learner-driven retest of previously
/// missed questions, sourced from <see cref="ReadingErrorBankEntry"/>.</description></item>
/// </list>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReadingAttemptMode
{
    Exam = 0,
    Learning = 1,
    Drill = 2,
    MiniTest = 3,
    ErrorBank = 4,
}

/// <summary>
/// Phase 4 — distractor categorisation for MCQ-style questions. Authors
/// label each non-correct option with the OET teaching category so we can
/// (a) drive analytics ("which distractor type trips this cohort up most")
/// and (b) surface targeted feedback to learners on review.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReadingDistractorCategory
{
    /// <summary>Option means the opposite of the correct answer.</summary>
    Opposite = 0,
    /// <summary>Option overgeneralises ("everyone", "always").</summary>
    TooBroad = 1,
    /// <summary>Option narrows beyond what the text supports.</summary>
    TooSpecific = 2,
    /// <summary>Option attributes the claim to the wrong speaker / source.</summary>
    WrongSpeaker = 3,
    /// <summary>Option introduces information that isn't in the text.</summary>
    NotInText = 4,
    /// <summary>Option distorts a real detail (number, qualifier, scope).</summary>
    DistortedDetail = 5,
    /// <summary>Option is on a different topic entirely.</summary>
    OutOfScope = 6,
}

/// <summary>
/// Phase 4 — per-question authoring review state. A paper may only be
/// published when every question is in <see cref="Published"/>. The state
/// machine is strictly forward except for the <see cref="Retired"/>
/// terminal and a single emergency rollback (any → <see cref="Draft"/> by
/// admin with audit log).
///
/// <list type="number">
/// <item><description><c>Draft</c> — author is still composing the
/// question.</description></item>
/// <item><description><c>AcademicReview</c> — content reviewed by an OET
/// SME for question fidelity / level / OET style.</description></item>
/// <item><description><c>MedicalReview</c> — clinical reviewer confirms the
/// scenario and terminology are accurate for the profession.</description></item>
/// <item><description><c>LanguageReview</c> — language-and-tone editor
/// confirms register, ambiguity, and B2 readability.</description></item>
/// <item><description><c>Pilot</c> — released to a small cohort to
/// collect facility / discrimination data.</description></item>
/// <item><description><c>Published</c> — eligible for inclusion in
/// learner-facing papers.</description></item>
/// <item><description><c>Retired</c> — withdrawn (e.g. answer compromised).
/// Past attempts referencing this question stay valid; new attempts
/// cannot include it.</description></item>
/// </list>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReadingReviewState
{
    Draft = 0,
    AcademicReview = 1,
    MedicalReview = 2,
    LanguageReview = 3,
    Pilot = 4,
    Published = 5,
    Retired = 6,
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
/// One passage within a part. Part A = 4 medical texts on a single topic
/// (variable length — no cap, Text C may include large tables/graphs);
/// Part B = 6 short extracts from different healthcare contexts
/// (policies, notices, guidelines, clinical communications);
/// Part C = 2 long articles.
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

    /// <summary>Phase 4 — JSON map keyed by option key, value is the
    /// <see cref="ReadingDistractorCategory"/> name. Example for an MCQ4:
    /// <c>{"A":"Opposite","B":"DistortedDetail","D":"NotInText"}</c>.
    /// Authoring-only; NEVER serialised to learner DTOs. Optional —
    /// questions without distractor metadata simply don't contribute to
    /// distractor analytics.</summary>
    public string? OptionDistractorsJson { get; set; }

    /// <summary>Phase 4 — per-question authoring lifecycle. New questions
    /// default to <see cref="ReadingReviewState.Draft"/>; the publish gate
    /// requires every question on the paper to be
    /// <see cref="ReadingReviewState.Published"/>.</summary>
    public ReadingReviewState ReviewState { get; set; } = ReadingReviewState.Draft;

    /// <summary>Phase 4 — most recent reviewer note (rolled forward on each
    /// transition). For full history, see <see cref="ReadingQuestionReviewLog"/>.</summary>
    [MaxLength(2048)]
    public string? LatestReviewNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ReadingPart? Part { get; set; }
    public ReadingText? Text { get; set; }
}

/// <summary>
/// Phase 4 — append-only audit log of every <see cref="ReadingQuestion.ReviewState"/>
/// transition. Drives the question history pane in admin and the audit
/// export. We deliberately keep this denormalised (no FK to user) so the
/// log survives reviewer-account deletion.
/// </summary>
[Index(nameof(ReadingQuestionId), nameof(TransitionedAt))]
public class ReadingQuestionReviewLog
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReadingQuestionId { get; set; } = default!;

    public ReadingReviewState FromState { get; set; }
    public ReadingReviewState ToState { get; set; }

    [MaxLength(64)]
    public string ReviewerUserId { get; set; } = default!;

    /// <summary>Snapshot of the reviewer's display name (so the log stays
    /// readable even if the user is renamed/deleted).</summary>
    [MaxLength(200)]
    public string? ReviewerDisplayName { get; set; }

    [MaxLength(2048)]
    public string? Note { get; set; }

    public DateTimeOffset TransitionedAt { get; set; }

    public ReadingQuestion? Question { get; set; }
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

    /// <summary>
    /// Phase 3: practice mode. Default <c>Exam</c> for back-compat with all
    /// rows pre-dating this column. See <see cref="ReadingAttemptMode"/>.
    /// </summary>
    public ReadingAttemptMode Mode { get; set; } = ReadingAttemptMode.Exam;

    /// <summary>
    /// Phase 3: optional JSON snapshot describing which subset of questions
    /// is in scope for this attempt (Drill / MiniTest / ErrorBank). Shape:
    /// <c>{ "kind":"drill", "questionIds":[...], "label":"Part A scan",
    /// "minutes": 10 }</c>. Null for Exam and Learning modes (full paper).
    /// </summary>
    public string? ScopeJson { get; set; }

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

    /// <summary>Phase 4 — when the learner picked a wrong MCQ option that
    /// was tagged with a distractor category in
    /// <see cref="ReadingQuestion.OptionDistractorsJson"/>, we cache the
    /// category here at grade time so analytics queries don't have to
    /// re-parse the question's JSON. Null when the answer was correct,
    /// when the question isn't an MCQ, or when no distractor metadata was
    /// authored for the picked option.</summary>
    public ReadingDistractorCategory? SelectedDistractorCategory { get; set; }

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
    /// <summary>
    /// NON-STANDARD MODE. Default <c>false</c> to stay OET-faithful:
    /// real OET Reading Part A answers are copied word-for-word from the
    /// text. Enabling synonym acceptance fundamentally changes the
    /// assessment and must be explicitly opted in by the admin.
    /// </summary>
    public bool ShortAnswerAcceptSynonyms { get; set; } = false;
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

/// <summary>
/// Phase 3 — Error Bank. One row per (user, question) capturing a question
/// the learner got wrong on a graded attempt. Refreshed at grading time:
/// upserted on incorrect answers and removed when the learner answers the
/// same question correctly on a later attempt or explicitly clears the
/// entry.
///
/// <para>
/// Used by the practice hub to power "retest the questions you missed"
/// without scanning every historical <see cref="ReadingAnswer"/> row.
/// </para>
/// </summary>
[Index(nameof(UserId), nameof(IsResolved))]
[Index(nameof(UserId), nameof(LastSeenWrongAt))]
[Index(nameof(ReadingQuestionId), nameof(UserId), IsUnique = true,
    Name = "UX_ReadingErrorBankEntry_User_Question")]
public class ReadingErrorBankEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string ReadingQuestionId { get; set; } = default!;

    /// <summary>Cached for cheap filtering / display in the hub.</summary>
    [MaxLength(64)]
    public string PaperId { get; set; } = default!;

    /// <summary>Cached <see cref="ReadingPartCode"/> for hub filters.</summary>
    public ReadingPartCode PartCode { get; set; }

    /// <summary>Most recent attempt where this question was missed.</summary>
    [MaxLength(64)]
    public string LastWrongAttemptId { get; set; } = default!;

    public DateTimeOffset FirstSeenWrongAt { get; set; }
    public DateTimeOffset LastSeenWrongAt { get; set; }

    /// <summary>How many distinct submitted attempts marked this wrong.</summary>
    public int TimesWrong { get; set; }

    /// <summary>Set true when a later submitted attempt graded this question
    /// correct, or when the learner clears the entry from the practice
    /// hub. Resolved rows are kept for analytics but hidden from the
    /// default error-bank view.</summary>
    public bool IsResolved { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    [MaxLength(32)]
    public string? ResolvedReason { get; set; }   // "answered_correctly" | "cleared_by_user" | "question_retired"
}

/// <summary>
/// Phase 6 — staging row for AI-assisted PDF extraction. An admin uploads a
/// PDF (as a <see cref="MediaAsset"/>), kicks off an extraction, and the AI
/// gateway returns a candidate <c>ReadingStructureManifest</c>. The draft
/// sits in <see cref="ReadingExtractionStatus.Pending"/> until an admin
/// reviews and either approves (which writes the structure via the manifest
/// importer) or rejects it.
/// <para>
/// Approval is gated on <c>ReadingPolicy.AiExtractionRequireHumanApproval</c>
/// — if disabled, the service may auto-approve immediately, but in practice
/// we keep the flag on for content fidelity.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReadingExtractionStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Failed = 3,
}

[Index(nameof(PaperId), nameof(Status))]
[Index(nameof(CreatedAt))]
public class ReadingExtractionDraft
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PaperId { get; set; } = default!;

    /// <summary>The MediaAsset that was extracted (the source PDF).</summary>
    [MaxLength(64)]
    public string? MediaAssetId { get; set; }

    public ReadingExtractionStatus Status { get; set; } = ReadingExtractionStatus.Pending;

    /// <summary>The candidate manifest the AI produced, serialised as JSON
    /// in the same shape <c>ReadingStructureManifest</c> uses on the wire.
    /// Admin approval flow re-uses <c>ImportManifestAsync</c> to apply this
    /// to the paper.</summary>
    public string? ExtractedManifestJson { get; set; }

    /// <summary>Raw AI response (for audit + debugging). Capped at 64 KB.</summary>
    [MaxLength(65536)]
    public string? RawAiResponseJson { get; set; }

    /// <summary>Free-text reason logged when an admin rejects a draft, or
    /// the failure reason when <see cref="Status"/> = Failed.</summary>
    [MaxLength(2048)]
    public string? Notes { get; set; }

    /// <summary>True when the AI gateway was unavailable and a deterministic
    /// stub was used instead. Surface this in the admin UI so the reviewer
    /// knows the draft is a placeholder, not a real extraction.</summary>
    public bool IsStub { get; set; }

    [MaxLength(64)]
    public string CreatedByAdminId { get; set; } = default!;

    [MaxLength(64)]
    public string? ResolvedByAdminId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
