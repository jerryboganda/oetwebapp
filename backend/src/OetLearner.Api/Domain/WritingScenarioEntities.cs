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

    public string CaseNotesMarkdown { get; set; } = default!;

    public string? CaseNotesStructuredJson { get; set; }

    /// <summary>
    /// Optional stimulus PDF (the exam "question paper") shown to learners during the
    /// forced reading window and the writing view. When null, learner UIs fall back to
    /// the case-notes text viewer. Independent of <see cref="CaseNotesMarkdown"/>, which
    /// the grading pipeline still reads. References a <c>MediaAsset.Id</c>.
    /// </summary>
    [MaxLength(64)]
    public string? StimulusPdfMediaAssetId { get; set; }

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

    /// <summary>Recipient details JSON: { name, role, organisation, address }.</summary>
    public string? RecipientJson { get; set; }

    /// <summary>Internal-only expected purpose (marking aid, spec §5.1).</summary>
    public string? ExpectedPurpose { get; set; }

    /// <summary>Internal-only expected action/request (marking aid, spec §5.1).</summary>
    public string? ExpectedAction { get; set; }

    /// <summary>Authored case-note sections JSON: [{ heading, items[] }] (spec §4.1).</summary>
    public string? CaseNoteSectionsJson { get; set; }

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

    /// <summary>Linked model answer exemplar (spec §6.1). Hidden during attempts.</summary>
    public Guid? ModelAnswerExemplarId { get; set; }

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
