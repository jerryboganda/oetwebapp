using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Module — Phase 2 of LISTENING-MODULE-PLAN.md.
//
// Mirrors the Reading entity shape but tuned for OET Listening:
//   ContentPaper  (subtestCode="listening")
//   └── ListeningPart            (5 codes: A1, A2, B, C1, C2)
//         └── ListeningExtract   (consultation / workplace / presentation
//                                 with accent + speakers metadata + audio
//                                 timing window inside the section MP3)
//               └── ListeningQuestion
//                     └── ListeningQuestionOption (Part B / C only)
//
// Attempt side:
//   ListeningAttempt (per user per paper, mode = Exam | Learning | …)
//     └── ListeningAnswer (one per answered question)
//
// ListeningPolicy holds the Listening-specific options (one-play exam mode,
// auto-expire window, etc.). ListeningUserPolicyOverride mirrors the Reading
// override pattern so accessibility extra-time grants port across.
//
// Authored ContentPaper listening now prefers these relational source and
// attempt tables at runtime. JSON authoring/backfill remains supported as a
// migration fallback for legacy and not-yet-backfilled papers.
// ═════════════════════════════════════════════════════════════════════════════

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningPartCode
{
    A1 = 1,
    A2 = 2,
    B = 3,
    C1 = 4,
    C2 = 5,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningQuestionType
{
    /// <summary>Part A only. Note-completion / fill-in-the-blank, graded by
    /// canonical-answer + accepted-variants string compare.</summary>
    ShortAnswer = 0,
    /// <summary>Part B and Part C. 3-option MCQ.</summary>
    MultipleChoice3 = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningExtractKind
{
    /// <summary>Part A consultation extract (~5 minutes).</summary>
    Consultation = 0,
    /// <summary>Part B workplace extract (~30-60 seconds each).</summary>
    Workplace = 1,
    /// <summary>Part C presentation / interview extract (~6 minutes each).</summary>
    Presentation = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningAttemptStatus
{
    InProgress = 0,
    Submitted = 1,
    Expired = 2,
    Abandoned = 3,
}

/// <summary>
/// Listening attempt mode. Mirrors <see cref="ReadingAttemptMode"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningAttemptMode
{
    Exam = 0,
    Learning = 1,
    Drill = 2,
    MiniTest = 3,
    ErrorBank = 4,
    Home = 5,
    Paper = 6,
    /// <summary>Listening V2 — fixed-form placement test that recommends a
    /// pathway stage. Routes through <c>ListeningPathwayProgressService</c>
    /// after submit. CAT/IRT adaptivity deferred to v2.1.</summary>
    Diagnostic = 7,
}

/// <summary>
/// Distractor categories for Part B / Part C MCQ options. Authors tag each
/// wrong option so analytics can drive the "which trap caught most learners"
/// dashboard. The same vocabulary appears in
/// <c>rulebooks/listening/&lt;profession&gt;/rulebook.v1.json</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningDistractorCategory
{
    TooStrong = 0,
    TooWeak = 1,
    WrongSpeaker = 2,
    OppositeMeaning = 3,
    ReusedKeyword = 4,
    /// <summary>Listening V2 — distractor that is plausible but explicitly
    /// outside the scope of what the audio answers. Added per critic finding
    /// HIGH #2 to widen the analytics taxonomy.</summary>
    OutOfScope = 5,
}

/// <summary>
/// Part C speaker-attitude tag. Surfaced on review and on the admin Part C
/// editor so learners can build a feel for the implicit-meaning trap. Allowed
/// values match the listening rulebook tables.speakerAttitudes whitelist.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningSpeakerAttitude
{
    Concerned = 0,
    Optimistic = 1,
    Doubtful = 2,
    Critical = 3,
    Neutral = 4,
    Other = 5,
}

[Index(nameof(PaperId), nameof(PartCode), IsUnique = true,
    Name = "UX_ListeningPart_Paper_PartCode")]
public class ListeningPart
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PaperId { get; set; } = default!;   // FK → ContentPaper.Id

    public ListeningPartCode PartCode { get; set; }

    /// <summary>Max raw points achievable on this part. Canonical: A1=12,
    /// A2=12, B=6, C1=6, C2=6. Total across the paper = 42.</summary>
    public int MaxRawScore { get; set; }

    [MaxLength(1024)]
    public string? Instructions { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ListeningExtract> Extracts { get; set; } = new List<ListeningExtract>();
    public ICollection<ListeningQuestion> Questions { get; set; } = new List<ListeningQuestion>();
}

[Index(nameof(ListeningPartId), nameof(DisplayOrder))]
public class ListeningExtract
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ListeningPartId { get; set; } = default!;

    public int DisplayOrder { get; set; }

    public ListeningExtractKind Kind { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    /// <summary>Free-form accent code surfaced to learners (e.g. <c>en-GB</c>,
    /// <c>en-AU</c>, <c>en-IE</c>). Optional — Part A may omit.</summary>
    [MaxLength(32)]
    public string? AccentCode { get; set; }

    /// <summary>JSON array of speaker descriptors, e.g.
    /// <c>[{"id":"s1","role":"GP","gender":"f","accent":"en-GB"}]</c>.</summary>
    public string SpeakersJson { get; set; } = "[]";

    /// <summary>Start offset (ms) of this extract within the section audio.
    /// 0 when the extract starts at the beginning of the audio file.</summary>
    public int? AudioStartMs { get; set; }

    /// <summary>End offset (ms) of this extract within the section audio.</summary>
    public int? AudioEndMs { get; set; }

    /// <summary>If true, the player allows replaying this extract during
    /// learning mode. Exam mode always enforces one-play regardless of
    /// this flag — cf. <see cref="ListeningPolicy.ExamReplayAllowed"/>.</summary>
    public bool ReplayInLearningOnly { get; set; } = true;

    /// <summary>JSON array of sentence-level transcript segments:
    /// <c>[{"startMs":0,"endMs":3500,"speakerId":"s1","text":"…"}]</c>.
    /// Drives "jump to evidence" playback in learning mode.</summary>
    public string TranscriptSegmentsJson { get; set; } = "[]";

    /// <summary>Listening V2 — comma-separated topic hints surfaced on
    /// admin browse and used by the per-skill analytics breakdown. Example:
    /// <c>"renal,medication-history"</c>. Authoring is optional.</summary>
    [MaxLength(256)]
    public string? TopicCsv { get; set; }

    /// <summary>Listening V2 — author-rated extract difficulty 1–5, optional.
    /// Used by drill targeting and pathway recommendation.</summary>
    public int? DifficultyRating { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ListeningPart? Part { get; set; }
}

[Index(nameof(ListeningPartId), nameof(DisplayOrder))]
[Index(nameof(PaperId), nameof(QuestionNumber), IsUnique = true,
    Name = "UX_ListeningQuestion_Paper_Number")]
public class ListeningQuestion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PaperId { get; set; } = default!;

    [MaxLength(64)]
    public string ListeningPartId { get; set; } = default!;

    [MaxLength(64)]
    public string? ListeningExtractId { get; set; }

    /// <summary>Canonical 1..42 question number across the whole paper.</summary>
    public int QuestionNumber { get; set; }

    public int DisplayOrder { get; set; }

    public int Points { get; set; } = 1;

    public ListeningQuestionType QuestionType { get; set; }

    [MaxLength(2048)]
    public string Stem { get; set; } = default!;

    /// <summary>For ShortAnswer: canonical answer string. For MCQ: the
    /// correct option key (e.g. <c>"A"</c>). NEVER serialised to learner
    /// DTOs.</summary>
    public string CorrectAnswerJson { get; set; } = "\"\"";

    /// <summary>Accepted alternates (UK/US spelling, abbreviations) for
    /// short-answer items. NEVER serialised to learner DTOs.</summary>
    public string? AcceptedSynonymsJson { get; set; }

    public bool CaseSensitive { get; set; }

    /// <summary>Authoring explanation surfaced after submit. NEVER serialised
    /// to learner DTOs pre-submit.</summary>
    [MaxLength(4096)]
    public string? ExplanationMarkdown { get; set; }

    /// <summary>Free-form skill tag (e.g. <c>numbers_units</c>,
    /// <c>cause_effect</c>, <c>speaker_attitude</c>). Drives drill targeting.</summary>
    [MaxLength(64)]
    public string? SkillTag { get; set; }

    /// <summary>Verbatim audio-script excerpt that supports the answer.
    /// NEVER serialised to learner DTOs pre-submit.</summary>
    [MaxLength(2048)]
    public string? TranscriptEvidenceText { get; set; }

    /// <summary>Optional time-coded evidence (ms within section audio).</summary>
    public int? TranscriptEvidenceStartMs { get; set; }
    public int? TranscriptEvidenceEndMs { get; set; }

    /// <summary>Part C only — speaker attitude tag, when authored.</summary>
    public ListeningSpeakerAttitude? SpeakerAttitude { get; set; }

    /// <summary>Listening V2 — author-rated question difficulty 1–5, optional.
    /// Used by drill targeting and CAT/IRT placement (v2.1 deferred).</summary>
    public int? DifficultyLevel { get; set; }

    /// <summary>Listening V2 — version-pin counter incremented by
    /// <c>ListeningAuthoringService</c> on every meaningful edit (stem,
    /// correct answer, options, accepted synonyms). In-flight attempts
    /// snapshot this value into <c>ListeningAnswer.QuestionVersionSnapshot</c>
    /// so admin edits never silently invalidate a candidate's grading.</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ListeningPart? Part { get; set; }
    public ListeningExtract? Extract { get; set; }
    public ICollection<ListeningQuestionOption> Options { get; set; } = new List<ListeningQuestionOption>();
}

/// <summary>
/// MCQ option (Part B / Part C only). Carries per-option distractor
/// metadata so admins can author rule-cited "why wrong" copy and analytics
/// can attribute every wrong selection to a category.
/// </summary>
[Index(nameof(ListeningQuestionId), nameof(OptionKey), IsUnique = true,
    Name = "UX_ListeningQuestionOption_Question_Key")]
public class ListeningQuestionOption
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ListeningQuestionId { get; set; } = default!;

    /// <summary><c>A</c>, <c>B</c>, or <c>C</c>.</summary>
    [MaxLength(2)]
    public string OptionKey { get; set; } = default!;

    public int DisplayOrder { get; set; }

    [MaxLength(1024)]
    public string Text { get; set; } = default!;

    public bool IsCorrect { get; set; }

    /// <summary>Distractor category for wrong options. Null for the correct
    /// option and for un-tagged options.</summary>
    public ListeningDistractorCategory? DistractorCategory { get; set; }

    /// <summary>Author-written "why this distractor is wrong" copy. Surfaced
    /// after submit only.</summary>
    [MaxLength(1024)]
    public string? WhyWrongMarkdown { get; set; }

    /// <summary>Listening V2 — version-pin counter incremented when option
    /// text or correctness changes. Snapshotted onto
    /// <c>ListeningAnswer.OptionVersionSnapshot</c> at grade time.</summary>
    public int Version { get; set; } = 1;

    public ListeningQuestion? Question { get; set; }
}

[Index(nameof(UserId), nameof(Status))]
[Index(nameof(PaperId), nameof(StartedAt))]
public class ListeningAttempt
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

    public ListeningAttemptStatus Status { get; set; } = ListeningAttemptStatus.InProgress;
    public ListeningAttemptMode Mode { get; set; } = ListeningAttemptMode.Exam;

    public int? RawScore { get; set; }
    public int? ScaledScore { get; set; }
    public int MaxRawScore { get; set; }

    /// <summary>Snapshot of the listening policy in effect at attempt start
    /// — protects in-flight attempts from policy edits.</summary>
    public string PolicySnapshotJson { get; set; } = "{}";

    [MaxLength(64)]
    public string? PaperRevisionId { get; set; }

    /// <summary>Optional drill / mini-test scope JSON (matches the Reading
    /// shape: <c>{"kind":"drill","questionIds":[…],"label":"…","minutes":N}</c>).</summary>
    public string? ScopeJson { get; set; }

    // ─────────────────────────────────────────────────────────────────────
    // Listening V2 additive columns. All nullable / opt-in. None are
    // LINQ-queried (per repo memory ef-core-sqlite-translation.md). Mapped
    // as jsonb on Postgres, plain TEXT on SQLite via conditional config in
    // LearnerDbContext.OnModelCreating.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Listening V2 FSM state snapshot:
    /// <c>{"state":"a1_preview","locks":["a1_preview"],"flags":{...}}</c>.
    /// Server-authoritative — frontend reducer mirrors but never overrides.</summary>
    public string? NavigationStateJson { get; set; }

    /// <summary>Listening V2 — wall-clock anchor for the currently-active
    /// FSM window (preview / playback / review). Used to compute remaining
    /// time on resume and to detect server-side window expiry.</summary>
    public DateTimeOffset? WindowStartedAt { get; set; }

    /// <summary>Listening V2 — duration in ms of the currently-active FSM
    /// window, sourced from ListeningPolicyDefaults or per-policy override.</summary>
    public int? WindowDurationMs { get; set; }

    /// <summary>Listening V2 — per-section audio cue timeline replay log
    /// (server-authoritative): <c>[{"cue":"a1.preview","atMs":0},...]</c>.</summary>
    public string? AudioCueTimelineJson { get; set; }

    /// <summary>Listening V2 — R10 tech-readiness probe results captured at
    /// attempt start: <c>{"checkedAt":...,"resolution":"ok",...}</c>.</summary>
    public string? TechReadinessJson { get; set; }

    /// <summary>Listening V2 — R08 highlights + strikethroughs persisted by
    /// extract+question. Survives section advance per
    /// <c>ListeningPolicy.AnnotationsPersistOnAdvance</c>.</summary>
    public string? AnnotationsJson { get; set; }

    /// <summary>Listening V2 — expert/admin manual score override audit:
    /// <c>[{"questionId":"q-1","override":1,"by":"exp-7","reason":"…"}]</c>.</summary>
    public string? HumanScoreOverridesJson { get; set; }

    /// <summary>Listening V2 — version-pinning map at attempt start
    /// <c>{"q-1":3,"q-2":1,...}</c>. Grading reads this map to fetch the
    /// exact authored version that was shown to the candidate, so admin
    /// question edits never silently invalidate the in-flight attempt.</summary>
    public string? LastQuestionVersionMapJson { get; set; }

    public ICollection<ListeningAnswer> Answers { get; set; } = new List<ListeningAnswer>();
}

[Index(nameof(ListeningAttemptId), nameof(ListeningQuestionId), IsUnique = true,
    Name = "UX_ListeningAnswer_Attempt_Question")]
public class ListeningAnswer
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ListeningAttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string ListeningQuestionId { get; set; } = default!;

    /// <summary>Echo of the question's answer shape — the candidate's
    /// submission. JSON-encoded.</summary>
    public string UserAnswerJson { get; set; } = "\"\"";

    public bool? IsCorrect { get; set; }
    public int PointsEarned { get; set; }

    /// <summary>Cached at grade time when the candidate selected an MCQ
    /// option carrying a <see cref="ListeningQuestionOption.DistractorCategory"/>.
    /// Null for correct answers, short-answer items, or un-tagged distractors.</summary>
    public ListeningDistractorCategory? SelectedDistractorCategory { get; set; }

    /// <summary>Listening V2 — snapshot of <c>ListeningQuestion.Version</c>
    /// at the moment the answer was submitted. Grading reads this column to
    /// fetch the exact authored version that was shown to the candidate.
    /// Null on legacy rows; backfill seeds from current Question.Version.</summary>
    public int? QuestionVersionSnapshot { get; set; }

    /// <summary>Listening V2 — snapshot of the selected option's Version, if
    /// any. Carries forward analytics integrity even when option text is later
    /// rewritten.</summary>
    public int? OptionVersionSnapshot { get; set; }

    public DateTimeOffset AnsweredAt { get; set; }

    public ListeningAttempt? Attempt { get; set; }
    public ListeningQuestion? Question { get; set; }
}

/// <summary>
/// Singleton (id = <c>global</c>) Listening policy. Stays additive to
/// <see cref="ReadingPolicy"/> — Listening keeps a separate row so admins
/// can tune one-play / replay / accent-mix without touching Reading.
/// </summary>
public class ListeningPolicy
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "global";

    // §1 — Retry
    public int AttemptsPerPaperPerUser { get; set; }   // 0 = unlimited
    public int AttemptCooldownMinutes { get; set; }
    [MaxLength(16)]
    public string BestScoreDisplay { get; set; } = "best";
    public bool ShowPastAttempts { get; set; } = true;

    // §2 — Timer
    /// <summary>Whole-paper timer (Listening is graded as a single ~40 min
    /// run with no Part-A hard lock — same as the real exam).</summary>
    public int FullPaperTimerMinutes { get; set; } = 45;
    public int GracePeriodSeconds { get; set; } = 10;
    [MaxLength(32)]
    public string OnExpirySubmitPolicy { get; set; } = "auto_submit_graded";
    public string CountdownWarningsJson { get; set; } = "[300,60,15]";

    // §3 — Audio replay
    /// <summary>Real OET Listening is one-play. Default <c>false</c>.</summary>
    public bool ExamReplayAllowed { get; set; } = false;
    /// <summary>Learning mode allows free replay by default.</summary>
    public bool LearningReplayAllowed { get; set; } = true;
    /// <summary>Per-question evidence loop in learning mode (jump-to-segment).</summary>
    public bool LearningEvidenceLoopEnabled { get; set; } = true;

    // §4 — Grading
    [MaxLength(32)]
    public string ShortAnswerNormalisation { get; set; } = "trim_collapse_case_insensitive";
    /// <summary>NON-STANDARD MODE — defaults <c>false</c>; OET grades on
    /// authored canonical + accepted variants only.</summary>
    public bool ShortAnswerAcceptSynonyms { get; set; } = false;

    // §5 — AI extraction
    public bool AiExtractionEnabled { get; set; } = true;
    public bool AiExtractionRequireHumanApproval { get; set; } = true;
    public int AiExtractionMaxRetriesPerPaper { get; set; } = 5;

    // §6 — Review
    public bool ShowExplanationsAfterSubmit { get; set; } = true;
    public bool ShowExplanationsOnlyIfWrong { get; set; }
    public bool ShowCorrectAnswerOnReview { get; set; } = true;

    // §7 — Accessibility
    public int DefaultExtraTimePct { get; set; } = 0;
    public bool ScreenReaderOptimised { get; set; } = true;

    // §8 — Lifecycle
    public bool AutoExpireWorkerEnabled { get; set; } = true;
    public int AutoExpireAfterMinutes { get; set; } = 180;
    public bool AllowResumeAfterExpiry { get; set; }

    // §9 — Retention
    public int RetainAnswerRowsDays { get; set; } = 730;
    public int RetainAttemptHeadersDays { get; set; } = 3650;
    public bool AnonymiseOnAccountDelete { get; set; } = true;

    // ─────────────────────────────────────────────────────────────────────
    // Listening V2 — R05/R06/R07/R08/R10 policy columns. All nullable; the
    // FSM in ListeningSessionService falls back to ListeningPolicyDefaults
    // when null (per planner Wave 2 §1(a)). All times in ms.
    // ─────────────────────────────────────────────────────────────────────

    // R05 — preview windows (silence period before audio starts).
    public int? PreviewWindowMsA1 { get; set; }
    public int? PreviewWindowMsA2 { get; set; }
    public int? PreviewWindowMsC1 { get; set; }
    public int? PreviewWindowMsC2 { get; set; }

    // R05 — review windows (final answer-edit period inside the section).
    public int? ReviewWindowMsA1 { get; set; }
    public int? ReviewWindowMsA2 { get; set; }
    public int? ReviewWindowMsC1 { get; set; }
    public int? ReviewWindowMsC2FinalCbt { get; set; }
    public int? ReviewWindowMsC2FinalPaper { get; set; }

    // R06 — between-section transition + Part B per-question pause window.
    public int? BetweenSectionTransitionMs { get; set; }
    public int? PartBQuestionWindowMs { get; set; }

    // R06 — strict CBT navigation flags (defaults true at runtime).
    public bool? OneWayLocksEnabled { get; set; }
    public bool? ConfirmDialogRequired { get; set; }
    public bool? UnansweredWarningRequired { get; set; }

    // R06.10 — confirm-token TTL for the two-step section advance protocol.
    public int? ConfirmTokenTtlMs { get; set; }

    // R08 — annotation tools.
    public bool? HighlightingEnabledPartA { get; set; }
    public bool? HighlightingEnabledPartBC { get; set; }
    public bool? OptionStrikethroughEnabled { get; set; }
    public bool? InAppZoomEnabled { get; set; }
    public bool? CtrlZoomBlocked { get; set; }
    public bool? AnnotationsPersistOnAdvance { get; set; }

    // R10 — tech-readiness probe.
    public bool? TechReadinessRequired { get; set; }
    public int? TechReadinessTtlMs { get; set; }

    // R07.3 — paper-mode all-parts final review banner threshold.
    public int? FinalReviewAllPartsMsPaper { get; set; }

    [ConcurrencyCheck]
    public int RowVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

/// <summary>
/// Per-user override for Listening policy. Mirrors
/// <see cref="ReadingUserPolicyOverride"/>. Currently surfaces extra-time
/// entitlements and a forced-disable flag.
/// </summary>
public class ListeningUserPolicyOverride
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int ExtraTimeEntitlementPct { get; set; }

    public bool BlockAttempts { get; set; }

    /// <summary>Listening V2 — opt-in accessibility mode badge surfaced in
    /// the player. Does not change scoring, only affects visual emphasis
    /// (high-contrast palette, larger focus rings, tab-only nav hints).</summary>
    public bool AccessibilityModeEnabled { get; set; }

    [MaxLength(512)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string? GrantedByAdminId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// Lifecycle status of an AI-proposed Listening structure draft. Mirrors
/// <see cref="ReadingExtractionStatus"/> but kept separate so the two
/// modules can evolve independently.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningExtractionDraftStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

/// <summary>
/// Persisted AI extraction proposal for a Listening paper. The
/// <c>POST .../listening/extract</c> endpoint persists the AI-gateway
/// result here as <see cref="ListeningExtractionDraftStatus.Pending"/>;
/// admins then review and approve/reject. Approval re-uses
/// <c>ListeningAuthoringService.ReplaceStructureAsync</c> so the same
/// validation + audit trail applies as for a manual edit.
///
/// String <c>Id</c> / <c>PaperId</c> match the project-wide convention
/// (see <see cref="ContentPaper"/> / <see cref="ReadingExtractionDraft"/>);
/// a Guid PaperId would not satisfy the FK to <c>ContentPapers.Id</c>.
/// </summary>
[Index(nameof(PaperId), nameof(Status))]
[Index(nameof(ProposedAt))]
public class ListeningExtractionDraft
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PaperId { get; set; } = default!;   // FK → ContentPaper.Id (cascade)

    public ListeningExtractionDraftStatus Status { get; set; } = ListeningExtractionDraftStatus.Pending;

    public DateTimeOffset ProposedAt { get; set; }

    [MaxLength(64)]
    public string? ProposedByUserId { get; set; }

    public bool IsStub { get; set; }

    [MaxLength(512)]
    public string? StubReason { get; set; }

    [MaxLength(2048)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>JSON-serialised <c>IReadOnlyList&lt;ListeningAuthoredQuestion&gt;</c> —
    /// the candidate 42-item structure the AI proposed.</summary>
    public string ProposedQuestionsJson { get; set; } = "[]";

    /// <summary>Raw AI gateway response for audit / debugging. Capped at 64 KB.</summary>
    [MaxLength(65536)]
    public string? RawAiResponseJson { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    [MaxLength(64)]
    public string? DecidedByUserId { get; set; }

    [MaxLength(512)]
    public string? DecisionReason { get; set; }

    public ContentPaper? Paper { get; set; }
}


// ═════════════════════════════════════════════════════════════════════════════
// Listening V2 — Pathway + Teacher Class + Attempt Notes (PRD §5.2 new tables).
// TeacherClass is intentionally cross-skill (no Listening prefix) so Reading,
// Writing, and Speaking can attach without a second class table.
// ═════════════════════════════════════════════════════════════════════════════

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListeningPathwayStageStatus
{
    Locked = 0,
    Unlocked = 1,
    InProgress = 2,
    Completed = 3,
}

/// <summary>
/// Listening V2 — per-user progress through the 12-stage pathway. Stage
/// codes are authored constants (e.g. <c>"foundation_partA"</c>); unlock
/// predicates live in <c>ListeningPathwayPolicy</c> and are recomputed
/// after every submit by <c>ListeningPathwayProgressService</c>.
/// </summary>
[Index(nameof(UserId), nameof(StageCode), IsUnique = true,
    Name = "UX_ListeningPathwayProgress_User_Stage")]
public class ListeningPathwayProgress
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string StageCode { get; set; } = default!;

    public ListeningPathwayStageStatus Status { get; set; } = ListeningPathwayStageStatus.Locked;

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Best scaled score achieved on the qualifying attempt for
    /// this stage (when applicable). Anchored to <c>OetScoring.OetRawToScaled</c>.</summary>
    public int? ScaledScore { get; set; }

    [MaxLength(64)]
    public string? AttemptId { get; set; }

    /// <summary>Admin / teacher who manually unlocked this stage (overrides
    /// the predicate). Audit-only — does not change predicate evaluation.</summary>
    [MaxLength(64)]
    public string? UnlockOverrideBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Cross-skill teacher class. Owned by a single user (the teacher / sponsor
/// admin). Members are listed in <see cref="TeacherClassMember"/>.
/// Aggregations use <c>TeacherClassService</c> with mandatory
/// <c>OwnerUserId == currentUserId</c> filtering (OWASP A01 — see PRD §6).
/// </summary>
[Index(nameof(OwnerUserId))]
public class TeacherClass
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string OwnerUserId { get; set; } = default!;

    [MaxLength(200)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<TeacherClassMember> Members { get; set; } = new List<TeacherClassMember>();
}

[Index(nameof(TeacherClassId), nameof(UserId), IsUnique = true,
    Name = "UX_TeacherClassMember_Class_User")]
public class TeacherClassMember
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string TeacherClassId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateTimeOffset AddedAt { get; set; }

    public TeacherClass? Class { get; set; }
}

/// <summary>
/// Listening V2 — Learning-mode learner notes per attempt. Stored relationally
/// (rather than in <c>ListeningAttempt.AnnotationsJson</c>) because notes are
/// queried per-extract and referenced by review tooling.
/// </summary>
[Index(nameof(ListeningAttemptId))]
public class ListeningAttemptNote
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ListeningAttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string ListeningExtractId { get; set; } = default!;

    /// <summary>Optional ms offset within the extract audio that the note
    /// targets. Null for extract-level notes.</summary>
    public int? TranscriptMs { get; set; }

    [MaxLength(4096)]
    public string Text { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
