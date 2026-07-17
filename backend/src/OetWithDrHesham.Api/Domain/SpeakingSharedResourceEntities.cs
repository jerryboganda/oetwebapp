using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Shared resources used during Speaking practice that aren't tied to a
/// specific card. Currently: Warm-up Questions (read at the start of every
/// session) and Assessment Criteria (the rubric). Multiple versions may
/// exist; only Published versions are surfaced to learners, and the
/// most-recent one per (Kind, ProfessionId-or-null) is used.
/// </summary>
[Index(nameof(Kind), nameof(Status))]
[Index(nameof(ProfessionId), nameof(Kind))]
public class SpeakingSharedResource
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>WarmUpQuestions | AssessmentCriteria</summary>
    [MaxLength(32)]
    public string Kind { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    /// <summary>Null = applies to all professions.</summary>
    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    /// <summary>FK to <see cref="MediaAsset"/> (PDF).</summary>
    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public MediaAsset? MediaAsset { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }

    [MaxLength(64)]
    public string? UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SpeakingSharedResourceKinds
{
    public const string WarmUpQuestions = "WarmUpQuestions";
    public const string AssessmentCriteria = "AssessmentCriteria";
}
