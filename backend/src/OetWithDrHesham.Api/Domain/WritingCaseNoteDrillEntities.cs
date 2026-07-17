using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

public class WritingCaseNoteDrill
{
    public Guid Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(64)]
    public string Profession { get; set; } = default!;

    [MaxLength(8)]
    public string LetterType { get; set; } = default!;

    [MaxLength(32)]
    public string Format { get; set; } = "tag-relevance";

    public string CaseNotesMarkdown { get; set; } = default!;

    public int Difficulty { get; set; } = 1;

    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingCaseNoteDrillSentence
{
    public Guid Id { get; set; }

    public Guid DrillId { get; set; }

    public int Ordinal { get; set; }

    public string SentenceText { get; set; } = default!;

    [MaxLength(16)]
    public string RelevanceLabel { get; set; } = "relevant";

    [MaxLength(500)]
    public string? Rationale { get; set; }
}

public class WritingCaseNoteDrillAttempt
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid DrillId { get; set; }

    public string ResponsesJson { get; set; } = "[]";

    public int CorrectCount { get; set; }

    public int TotalCount { get; set; }

    public double ScorePercent { get; set; }

    public int? TimeSpentSeconds { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }
}
