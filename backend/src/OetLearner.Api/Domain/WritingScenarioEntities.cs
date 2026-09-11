using System.ComponentModel.DataAnnotations;
using Pgvector;

namespace OetLearner.Api.Domain;

public class WritingScenario
{
    public Guid Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(8)]
    public string LetterType { get; set; } = default!;

    [MaxLength(64)]
    public string Profession { get; set; } = default!;

    [MaxLength(64)]
    public string? SubDiscipline { get; set; }

    public string TopicsJson { get; set; } = "[]";

    public int Difficulty { get; set; }

    /// <summary>
    /// Optional stimulus PDF (the exam "question paper") shown to learners during the
    /// forced reading window and the writing view. When null, learner UIs show the
    /// task prompt and fixed instructions only. References a <c>MediaAsset.Id</c>.
    /// </summary>
    [MaxLength(64)]
    public string? StimulusPdfMediaAssetId { get; set; }

    /// <summary>
    /// Optional answer-sheet / model-answer PDF revealed to the learner on the results page
    /// (post-submission only) so they can tally their letter against the official answer.
    /// Never exposed on the live exam surface. References a <c>MediaAsset.Id</c>.
    /// </summary>
    [MaxLength(64)]
    public string? AnswerSheetPdfMediaAssetId { get; set; }

    public int EstimatedReadingMinutes { get; set; } = 5;

    public bool IsDiagnostic { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    public int Version { get; set; } = 1;

    public Guid? PreviousVersionId { get; set; }

    [MaxLength(64)]
    public string AuthorId { get; set; } = default!;

    [MaxLength(64)]
    public string? ApprovedById { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ── OET exam-faithful authored task fields (spec §4/§5/§6/§9) ──────────────
    // Added by AddWritingExamModuleClosure. All nullable / defaulted so existing
    // seeded scenarios remain valid; the admin Task Builder populates them and the
    // publish gate requires the key ones (recipient, model answer, ≥1 key item).

    /// <summary>Internal task code, e.g. MED-WR-S01 (spec §3.2).</summary>
    [MaxLength(32)]
    public string? InternalCode { get; set; }

    /// <summary>Explicit learner-facing writing task instruction (spec §5.1).</summary>
    public string? TaskPromptMarkdown { get; set; }

    /// <summary>Candidate role, e.g. "You are a doctor at Newtown Medical Clinic".</summary>
    [MaxLength(256)]
    public string? WriterRole { get; set; }

    /// <summary>Today's date assumption shown to the candidate, e.g. "18 June 2018".</summary>
    [MaxLength(64)]
    public string? TodayDate { get; set; }

    /// <summary>Internal-only expected purpose (marking aid, spec §5.1).</summary>
    public string? ExpectedPurpose { get; set; }

    /// <summary>Internal-only expected action/request (marking aid, spec §5.1).</summary>
    public string? ExpectedAction { get; set; }

    /// <summary>Fixed instruction lines JSON array shown on the task screen (spec §5.2).</summary>
    public string FixedInstructionsJson { get; set; } = "[]";

    public int WordGuideMin { get; set; } = 180;

    public int WordGuideMax { get; set; } = 200;

    public int ReadingTimeSeconds { get; set; } = 300;

    public int WritingTimeSeconds { get; set; } = 2400;

    /// <summary>paper | computer | both (spec §1.1 simulation modes).</summary>
    [MaxLength(16)]
    public string SimulationModes { get; set; } = "both";

    /// <summary>tutor | ai_assisted | double (spec §3.2 marking mode).</summary>
    [MaxLength(16)]
    public string MarkingMode { get; set; } = "tutor";

    /// <summary>Retake policy JSON: { maxAttempts, cooldownHours }.</summary>
    public string? RetakePolicyJson { get; set; }

    /// <summary>Source/license provenance for the audit trail (spec §3.2).</summary>
    [MaxLength(512)]
    public string? SourceProvenance { get; set; }

    [MaxLength(64)]
    public string? IntegrityAcknowledgedById { get; set; }

    public DateTimeOffset? IntegrityAcknowledgedAt { get; set; }

    /// <summary>Content owner (spec §3.2). Distinct from AuthorId for transfer.</summary>
    [MaxLength(64)]
    public string? ContentOwnerId { get; set; }

    /// <summary>Bridge: the writing ContentPaper this scenario was projected from.</summary>
    [MaxLength(64)]
    public string? SourceContentPaperId { get; set; }

    /// <summary>
    /// Admin-confirmed recipient wording, as it should appear/be understood in
    /// the letter (e.g. "Dr Helena Vance, Dermatologist"). When set, this
    /// overrides WritingTaskUnderstandingService's heuristic recipient
    /// detection at the publish gate — the recipient is never guessed once an
    /// admin has confirmed or corrected it. See RecipientNormalizedJson for
    /// the machine-readable form.
    /// </summary>
    public string? RecipientRawText { get; set; }

    /// <summary>
    /// Admin-confirmed recipient, normalised: {"name": "...", "role": "...",
    /// "category": "..."} where category matches
    /// WritingTaskUnderstandingResult.RecipientCategory's vocabulary
    /// (nurse, gp, named_or_unnamed_clinician, etc.). Nullable JSON string.
    /// </summary>
    public string? RecipientNormalizedJson { get; set; }

    /// <summary>
    /// Admin-confirmed purpose/clinical request for this task (e.g. "Refer
    /// for assessment of severe acne and possible rosacea"). When set,
    /// overrides WritingTaskUnderstandingService's heuristic diagnosis/plan
    /// extraction at the publish gate — a task is never blocked on
    /// diagnosis_or_request_unresolved once an admin has confirmed the
    /// clinical purpose in their own words.
    /// </summary>
    public string? ConfirmedPurposeText { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public class WritingScenarioStructuredSentence
{
    public Guid Id { get; set; }

    public Guid ScenarioId { get; set; }

    public int Ordinal { get; set; }

    public string SentenceText { get; set; } = default!;

    [MaxLength(16)]
    public string RelevanceLabel { get; set; } = "relevant";

    [MaxLength(512)]
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One pre-generated, reusable Model Answer per Writing task (1:1 with
/// <see cref="WritingScenario"/> via <see cref="ScenarioId"/>). Generated once
/// ahead of time by an admin action, never regenerated on a normal candidate
/// submission — <see cref="WritingSubmissionEvaluationPipeline"/> reuses this
/// row for every candidate grading the same task instead of calling the AI
/// gateway again. Mirrors the HeldForReview/Ready/Rejected +
/// IsCandidateVisible gate already used by the per-submission
/// <c>WritingAssessmentModelAnswer</c>, so the same admin review mental model
/// applies here.
/// </summary>
public class WritingTaskModelAnswer
{
    public Guid Id { get; set; }

    public Guid ScenarioId { get; set; }

    public WritingAssessmentModelAnswerStatus Status { get; set; } = WritingAssessmentModelAnswerStatus.HeldForReview;

    /// <summary>Only candidate-visible once an admin has approved a Ready answer.</summary>
    public bool IsCandidateVisible { get; set; }

    public string? ModelAnswerText { get; set; }

    public string GroundedFactReferencesJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? HoldReason { get; set; }

    /// <summary>Hash of the task prompt + case-note sentences used at generation
    /// time, so a later edit to either can be detected as "answer may be stale"
    /// without forcing an automatic (costly) regeneration.</summary>
    [MaxLength(64)]
    public string? SourceContentHash { get; set; }

    [MaxLength(32)]
    public string? RulebookVersion { get; set; }

    [MaxLength(64)]
    public string? PromptVersion { get; set; }

    [MaxLength(128)]
    public string? ModelUsed { get; set; }

    public DateTimeOffset? GeneratedAt { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>
    /// Deterministic validator version (<c>WritingRuleEngine.ValidatorVersion</c>)
    /// this exact text last passed with zero violations. Addendum Rev8 §14: a
    /// stored VERIFIED/CLEAN flag is invalid after a rule-pack or validator
    /// version change until the saved answer is revalidated — so a row whose
    /// value differs from the running engine's is never shown to candidates.
    /// </summary>
    [MaxLength(64)]
    public string? ValidatorVersion { get; set; }

    /// <summary>Fingerprint of the active profession rule pack at verification
    /// (<c>WritingRuleEngine.RulePackFingerprint</c>).</summary>
    [MaxLength(64)]
    public string? RulePackHash { get; set; }

    /// <summary>When the full gate (word count, grounding, deterministic rules,
    /// semantic validator) last passed for the current text.</summary>
    public DateTimeOffset? ValidatedAt { get; set; }

    /// <summary>Last full validation report (deterministic findings, semantic
    /// validator verdict, word count, versions) — the audit evidence for why
    /// the row is Ready or held.</summary>
    public string ValidationReportJson { get; set; } = "{}";

    /// <summary>Repair iterations the generator needed before this text passed.</summary>
    public int RepairCount { get; set; }

    /// <summary>Body (introduction to closure) word count of the stored text.</summary>
    public int? BodyWordCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public class WritingScenarioEmbedding
{
    public Guid Id { get; set; }

    public Guid ScenarioId { get; set; }

    [MaxLength(64)]
    public string ModelId { get; set; } = "text-embedding-3-small";

    public int Dimensions { get; set; } = 1536;

    /// <summary>
    /// JSON-encoded <c>float[1536]</c> array. Retained as the source of truth
    /// for backward compatibility with the pre-pgvector C# cosine-similarity
    /// path. <see cref="Embedding"/> is the pgvector mirror — populated lazily
    /// at write time, and backfilled by
    /// <c>WritingExemplarEmbeddingService.BackfillFromJsonAsync</c>.
    /// </summary>
    public string EmbeddingJson { get; set; } = "[]";

    /// <summary>
    /// Native pgvector column (<c>vector(1536)</c>). Nullable so legacy rows
    /// can be backfilled without blocking inserts.
    /// </summary>
    public Vector? Embedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
