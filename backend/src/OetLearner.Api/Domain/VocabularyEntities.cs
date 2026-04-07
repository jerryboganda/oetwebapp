using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

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
    public string? ContextNotes { get; set; }              // Usage notes (formal, clinical, etc.)

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? ProfessionId { get; set; }              // null = general

    [MaxLength(64)]
    public string Category { get; set; } = default!;      // "medical", "academic", "general", "clinical_communication"

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    [MaxLength(256)]
    public string? AudioUrl { get; set; }                  // Pronunciation audio

    [MaxLength(256)]
    public string? ImageUrl { get; set; }

    public string SynonymsJson { get; set; } = "[]";
    public string CollocationsJson { get; set; } = "[]";   // Common word pairings
    public string RelatedTermsJson { get; set; } = "[]";

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class LearnerVocabulary
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string TermId { get; set; } = default!;

    [MaxLength(16)]
    public string Mastery { get; set; } = "new";           // "new", "learning", "reviewing", "mastered"

    public double EaseFactor { get; set; } = 2.5;
    public int IntervalDays { get; set; } = 1;
    public int ReviewCount { get; set; }
    public int CorrectCount { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

public class VocabularyQuizResult
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int TermsQuizzed { get; set; }
    public int CorrectCount { get; set; }
    public int DurationSeconds { get; set; }
    public string ResultsJson { get; set; } = "[]";
    public DateTimeOffset CompletedAt { get; set; }
}
