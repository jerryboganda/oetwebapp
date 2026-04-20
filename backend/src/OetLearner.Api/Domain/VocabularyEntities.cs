using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Canonical vocabulary term. One row per distinct (Term, ExamTypeCode, ProfessionId).
/// See docs/VOCABULARY-MODULE.md §3 for the full contract.
/// </summary>
public class VocabularyTerm
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Term { get; set; } = default!;

    [MaxLength(1024)]
    public string Definition { get; set; } = default!;

    [MaxLength(2048)]
    public string ExampleSentence { get; set; } = default!;

    [MaxLength(1024)]
    public string? ContextNotes { get; set; }

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? ProfessionId { get; set; }              // null = general

    [MaxLength(64)]
    public string Category { get; set; } = default!;

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    // ── Phase V1 additions ──────────────────────────────────────────────
    // IPA pronunciation string. Separate from AudioUrl (which is a playable URL).
    [MaxLength(64)]
    public string? IpaPronunciation { get; set; }

    [MaxLength(256)]
    public string? AudioUrl { get; set; }

    // Link to a MediaAsset row when the audio was uploaded via the content pipeline.
    [MaxLength(64)]
    public string? AudioMediaAssetId { get; set; }

    [MaxLength(256)]
    public string? ImageUrl { get; set; }

    public string SynonymsJson { get; set; } = "[]";
    public string CollocationsJson { get; set; } = "[]";
    public string RelatedTermsJson { get; set; } = "[]";

    [MaxLength(512)]
    public string? SourceProvenance { get; set; }           // Required at publish

    [MaxLength(16)]
    public string Status { get; set; } = "active";          // draft|active|archived

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Per-user SM-2 card state. Unique on (UserId, TermId).
/// </summary>
public class LearnerVocabulary
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string TermId { get; set; } = default!;

    [MaxLength(16)]
    public string Mastery { get; set; } = "new";

    public double EaseFactor { get; set; } = 2.5;
    public int IntervalDays { get; set; } = 1;
    public int ReviewCount { get; set; }
    public int CorrectCount { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>
    /// Optional marker describing where the term was saved from.
    /// Format: "<module>:<id>[:<offset>]" e.g. "reading:cp-042:134", "writing:att-777",
    /// "speaking:session-55:12400" (ms offset), "mock:mock-12:words", "browse".
    /// See docs/VOCABULARY-MODULE.md §10.
    /// </summary>
    [MaxLength(128)]
    public string? SourceRef { get; set; }
}

/// <summary>
/// Aggregate record of one quiz session.
/// </summary>
public class VocabularyQuizResult
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int TermsQuizzed { get; set; }
    public int CorrectCount { get; set; }
    public int DurationSeconds { get; set; }

    /// <summary>Quiz format identifier. One of: definition_match, fill_blank,
    /// synonym_match, context_usage, audio_recognition. Default definition_match
    /// for backward compatibility with pre-V3 sessions.</summary>
    [MaxLength(32)]
    public string Format { get; set; } = "definition_match";

    public string ResultsJson { get; set; } = "[]";
    public DateTimeOffset CompletedAt { get; set; }
}
