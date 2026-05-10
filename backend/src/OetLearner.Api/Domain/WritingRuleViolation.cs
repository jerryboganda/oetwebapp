using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// First-class deterministic Writing rule violation row.
/// Persisted in addition to the JSON blob on <see cref="Evaluation.RuleViolationsJson"/>
/// so per-rule analytics ("which rule fails most across cohort?") become possible.
/// Created by <c>BackgroundJobProcessor.CompleteWritingEvaluationAsync</c>.
/// </summary>
[Index(nameof(RuleId), nameof(CreatedAt))]
[Index(nameof(UserId), nameof(CreatedAt))]
[Index(nameof(EvaluationId))]
public class WritingRuleViolation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string EvaluationId { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Rulebook id, e.g. "R14.6" or detector check id like "no_contractions".</summary>
    [MaxLength(64)]
    public string RuleId { get; set; } = default!;

    /// <summary>"critical" | "major" | "minor" | "info".</summary>
    [MaxLength(16)]
    public string Severity { get; set; } = default!;

    /// <summary>"purpose" | "content" | "conciseness_clarity" | "genre_style" | "organisation_layout" | "language".</summary>
    [MaxLength(32)]
    public string CriterionCode { get; set; } = default!;

    public string? Quote { get; set; }
    public string? Message { get; set; }
    public string? FixSuggestion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Evaluation Evaluation { get; set; } = default!;
}
