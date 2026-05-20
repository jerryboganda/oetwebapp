using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Admin-curated reference PDFs (monthly / yearly "recall" digests) shown
/// to learners as a read-only library. Separate from the spaced-repetition
/// ReviewItem model; those are per-learner study tasks, these are shared
/// documents.
/// </summary>
[Index(nameof(SubtestCode), nameof(Status))]
[Index(nameof(ProfessionId), nameof(Status))]
[Index(nameof(Status), nameof(SortOrder))]
public class RecallDocument
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    /// <summary>listening | reading | writing | speaking | cross</summary>
    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    /// <summary>Free-form period label, e.g. "2026 Q1", "2023-2025", "Old".</summary>
    [MaxLength(64)]
    public string PeriodLabel { get; set; } = default!;

    /// <summary>FK to <see cref="ProfessionReference"/>. Null means "all professions".</summary>
    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    /// <summary>FK to <see cref="MediaAsset"/>.</summary>
    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public MediaAsset? MediaAsset { get; set; }

    /// <summary>Optional admin-authored description (markdown).</summary>
    public string? DescriptionMarkdown { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public int SortOrder { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    [MaxLength(64)]
    public string? UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}
