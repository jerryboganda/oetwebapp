using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ── Content Hierarchy: Program → Track → Module → Lesson / Reference ──

[Index(nameof(Status), nameof(DisplayOrder))]
[Index(nameof(ProgramType), nameof(InstructionLanguage))]
public class ContentProgram
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    [MaxLength(8)]
    public string InstructionLanguage { get; set; } = "en";   // "en", "ar", "ar+en"

    [MaxLength(32)]
    public string ProgramType { get; set; } = "full_course";  // full_course, crash_course, foundation, combo

    public ContentStatus Status { get; set; }

    [MaxLength(512)]
    public string? ThumbnailUrl { get; set; }

    public int DisplayOrder { get; set; }
    public int EstimatedDurationMinutes { get; set; }

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

[Index(nameof(ProgramId), nameof(DisplayOrder))]
public class ContentTrack
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ProgramId { get; set; } = default!;

    [MaxLength(32)]
    public string? SubtestCode { get; set; }   // null for foundation/general tracks

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }
    public ContentStatus Status { get; set; }
}

[Index(nameof(TrackId), nameof(DisplayOrder))]
public class ContentModule
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string TrackId { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }
    public int EstimatedDurationMinutes { get; set; }

    [MaxLength(64)]
    public string? PrerequisiteModuleId { get; set; }

    public ContentStatus Status { get; set; }
}

[Index(nameof(ModuleId), nameof(DisplayOrder))]
public class ContentLesson
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ModuleId { get; set; } = default!;

    [MaxLength(64)]
    public string? ContentItemId { get; set; }   // links to existing ContentItem for practice tasks

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string LessonType { get; set; } = "video_lesson";  // video_lesson, strategy_guide, session_replay, reading_material, practice_task

    [MaxLength(64)]
    public string? MediaAssetId { get; set; }

    public int DisplayOrder { get; set; }
    public ContentStatus Status { get; set; }
}

[Index(nameof(ModuleId), nameof(DisplayOrder))]
public class ContentReference
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ModuleId { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string ReferenceType { get; set; } = "pdf";  // pdf, vocab_list, grammar_sheet, external_link

    [MaxLength(64)]
    public string? MediaAssetId { get; set; }

    [MaxLength(1024)]
    public string? ExternalUrl { get; set; }

    public int DisplayOrder { get; set; }
    public ContentStatus Status { get; set; }
}

// ── Packages & Entitlements ──

[Index(nameof(Code), IsUnique = true)]
[Index(nameof(Status), nameof(DisplayOrder))]
public class ContentPackage
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    [MaxLength(32)]
    public string PackageType { get; set; } = "full_course";  // full_course, crash_course, combo, foundation, standalone

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    [MaxLength(8)]
    public string InstructionLanguage { get; set; } = "en";

    [MaxLength(64)]
    public string? BillingPlanId { get; set; }   // links to existing BillingPlan

    public ContentStatus Status { get; set; }

    [MaxLength(512)]
    public string? ThumbnailUrl { get; set; }

    /// <summary>JSON array of feature strings for marketing comparison grid.</summary>
    public string ComparisonFeaturesJson { get; set; } = "[]";

    public int DisplayOrder { get; set; }

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

[Index(nameof(PackageId), nameof(RuleType))]
public class PackageContentRule
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PackageId { get; set; } = default!;

    [MaxLength(32)]
    public string RuleType { get; set; } = default!;   // include_program, include_track, include_module, include_content_item, exclude_program, etc.

    [MaxLength(64)]
    public string TargetId { get; set; } = default!;

    [MaxLength(32)]
    public string TargetType { get; set; } = default!;  // program, track, module, content_item
}

// ── Media Assets ──

[Index(nameof(Status))]
public class MediaAsset
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(256)]
    public string OriginalFilename { get; set; } = default!;

    [MaxLength(64)]
    public string MimeType { get; set; } = default!;

    [MaxLength(16)]
    public string Format { get; set; } = default!;   // mp4, mp3, pdf, png, jpg, docx, pptx

    public long SizeBytes { get; set; }
    public int? DurationSeconds { get; set; }

    [MaxLength(512)]
    public string StoragePath { get; set; } = default!;

    [MaxLength(512)]
    public string? ThumbnailPath { get; set; }

    [MaxLength(512)]
    public string? CaptionPath { get; set; }

    [MaxLength(512)]
    public string? TranscriptPath { get; set; }

    public MediaAssetStatus Status { get; set; }

    /// <summary>SHA-256 of the stored file, hex-encoded lowercase. Used for
    /// content-addressed storage and cross-upload dedup (Slice 2).
    /// Nullable so the column can be back-filled for rows that pre-date
    /// the Content Upload subsystem.</summary>
    [MaxLength(64)]
    public string? Sha256 { get; set; }

    /// <summary>Optional classification to aid query ("image" | "audio" |
    /// "document"). Derived from MimeType at upload time.</summary>
    [MaxLength(16)]
    public string? MediaKind { get; set; }

    [MaxLength(64)]
    public string? UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

// ── Testimonials & Marketing ──

[Index(nameof(DisplayApproved), nameof(DisplayOrder))]
public class TestimonialAsset
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string? LearnerDisplayName { get; set; }

    [MaxLength(32)]
    public string? Profession { get; set; }

    public DateOnly? TestDate { get; set; }

    [MaxLength(8)]
    public string? OverallGrade { get; set; }

    /// <summary>JSON object with per-subtest grades.</summary>
    public string? SubtestGradesJson { get; set; }

    public string? TestimonialText { get; set; }

    [MaxLength(64)]
    public string? MediaAssetId { get; set; }   // screenshot or video

    [MaxLength(16)]
    public string ConsentStatus { get; set; } = "pending";  // pending, granted, revoked

    public bool DisplayApproved { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

[Index(nameof(AssetType), nameof(Status))]
public class MarketingAsset
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string AssetType { get; set; } = default!;   // package_graphic, promo_video, poster, schedule

    [MaxLength(64)]
    public string? MediaAssetId { get; set; }

    [MaxLength(64)]
    public string? PackageId { get; set; }

    public ContentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

[Index(nameof(Status), nameof(DisplayOrder))]
public class FreePreviewAsset
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string PreviewType { get; set; } = default!;  // webinar_replay, sample_lesson, sample_task

    [MaxLength(64)]
    public string? ContentItemId { get; set; }

    [MaxLength(64)]
    public string? MediaAssetId { get; set; }

    [MaxLength(256)]
    public string? ConversionCtaText { get; set; }

    [MaxLength(64)]
    public string? TargetPackageId { get; set; }

    public ContentStatus Status { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// ── Foundation / Remediation Resources ──

[Index(nameof(ResourceType), nameof(Status))]
public class FoundationResource
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string ResourceType { get; set; } = default!;  // basic_english, grammar_foundation, medical_vocabulary, common_words

    public string? ContentBody { get; set; }   // markdown or structured content

    [MaxLength(64)]
    public string? MediaAssetId { get; set; }

    [MaxLength(16)]
    public string Difficulty { get; set; } = "beginner";

    [MaxLength(64)]
    public string? PrerequisiteResourceId { get; set; }

    public int DisplayOrder { get; set; }
    public ContentStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// ── Cohort Content Release Overlay ──

[Index(nameof(ProgramId), nameof(CohortCode))]
public class ContentCohortOverlay
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ProgramId { get; set; } = default!;

    [MaxLength(64)]
    public string CohortCode { get; set; } = default!;

    [MaxLength(200)]
    public string CohortTitle { get; set; } = default!;

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>JSON array of {moduleId, releaseDate} for drip-release scheduling.</summary>
    public string ReleaseScheduleJson { get; set; } = "[]";

    public ContentStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// ── Content Import / Migration ──

[Index(nameof(Status), nameof(CreatedAt))]
public class ContentImportBatch
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(32)]
    public string Status { get; set; } = "pending";  // pending, processing, completed, failed, rolled_back

    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int FailedItems { get; set; }

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    public string? ErrorLogJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
