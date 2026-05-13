using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

[Index(nameof(AttemptId), nameof(PageNumber))]
[Index(nameof(MediaAssetId))]
[Index(nameof(UserId), nameof(CreatedAt))]
public class WritingAttemptAsset
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    [MaxLength(32)]
    public string AssetKind { get; set; } = "submission_page";

    public int PageNumber { get; set; }

    [MaxLength(32)]
    public string ExtractionState { get; set; } = "queued";

    public string ExtractedText { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? ExtractionProvider { get; set; }

    [MaxLength(128)]
    public string? ExtractionReasonCode { get; set; }

    [MaxLength(1024)]
    public string? ExtractionMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExtractedAt { get; set; }

    public Attempt? Attempt { get; set; }
    public MediaAsset? MediaAsset { get; set; }
}

[Index(nameof(ReviewRequestId), nameof(CreatedAt))]
[Index(nameof(MediaAssetId))]
public class ReviewVoiceNote
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewRequestId { get; set; } = default!;

    [MaxLength(64)]
    public string UploadedByReviewerId { get; set; } = default!;

    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public int? DurationSeconds { get; set; }
    public string TranscriptText { get; set; } = string.Empty;
    public string WrittenNotes { get; set; } = string.Empty;
    public string RubricJson { get; set; } = "{}";

    [MaxLength(32)]
    public string Status { get; set; } = "ready";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReviewRequest? ReviewRequest { get; set; }
    public MediaAsset? MediaAsset { get; set; }
}