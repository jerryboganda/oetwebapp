using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Bunny Stream encode lifecycle for a <see cref="LibraryVideo"/>. Mirrors the
/// Bunny video status integers loosely — Bunny 3 (Finished) and 4 (Resolution
/// finished) both collapse to <see cref="Ready"/> here.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VideoEncodeStatus
{
    NotUploaded = 0,
    Uploading = 1,
    Queued = 2,
    Processing = 3,
    Encoding = 4,
    Ready = 5,
    Failed = 6,
}

/// <summary>
/// A Video Library video. The source media lives on Bunny Stream
/// (<see cref="BunnyVideoId"/>); playback URLs are minted per-session by
/// <c>VideoPlaybackSessionService</c> and are never persisted here.
/// </summary>
[Index(nameof(Status), nameof(PublishAt))]
[Index(nameof(BunnyVideoId))]
[Index(nameof(IsFeatured), nameof(SortOrder))]
public class LibraryVideo
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(256)]
    public string Title { get; set; } = default!;

    [MaxLength(2048)]
    public string? Description { get; set; }

    [MaxLength(512)]
    public string? TagsCsv { get; set; }

    /// <summary>'foundation' | 'core' | 'advanced'.</summary>
    [MaxLength(16)]
    public string? Difficulty { get; set; }

    [MaxLength(32)]
    public string? SubtestCode { get; set; }

    [MaxLength(16)]
    public string? ExamTypeCode { get; set; }

    [MaxLength(64)]
    public string? BunnyVideoId { get; set; }

    [MaxLength(32)]
    public string? BunnyLibraryId { get; set; }

    /// <summary>
    /// The Bunny collection (folder) this video belongs to, mirrored from the
    /// live library so the admin can filter/assign per video. Nullable = the
    /// global default collection is used at upload time. Bunny remains the
    /// source of truth for membership; this is a convenience mirror kept in
    /// sync by the collections console + the wizard picker.
    /// </summary>
    [MaxLength(64)]
    public string? BunnyCollectionId { get; set; }

    public VideoEncodeStatus EncodeStatus { get; set; } = VideoEncodeStatus.NotUploaded;

    public int EncodeProgress { get; set; }

    [MaxLength(512)]
    public string? EncodeError { get; set; }

    public int DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    [MaxLength(512)]
    public string? BunnyThumbnailUrl { get; set; }

    [MaxLength(64)]
    public string? CustomThumbnailMediaAssetId { get; set; }

    public MediaAsset? CustomThumbnailMediaAsset { get; set; }

    /// <summary>"free" | "premium".</summary>
    [MaxLength(16)]
    public string AccessTier { get; set; } = "premium";

    /// <summary>JSON array of profession ids; empty array = visible to all professions.</summary>
    public string ProfessionIdsJson { get; set; } = "[]";

    /// <summary>
    /// Instruction language of the video: "en" | "ar". Null = unspecified (legacy).
    /// Independent of <see cref="ProfessionIdsJson"/> gating — purely drives the
    /// learner English/Arabic filter and the admin language badge. Per the Course
    /// Videos content model, English is one shared set shown to every profession
    /// while Arabic sets are profession-scoped (Physiotherapy/Dentistry/Radiography
    /// alias to Medicine via the profession list, not this field).
    /// </summary>
    [MaxLength(8)]
    public string? Language { get; set; }

    /// <summary>Operational profession-map folder for Writing/Speaking: "sessions" | "workshops".</summary>
    [MaxLength(32)]
    public string? CourseFolder { get; set; }

    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public long ViewCount { get; set; }

    /// <summary>JSON array of {timeSeconds,title}.</summary>
    public string ChaptersJson { get; set; } = "[]";

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTimeOffset? PublishAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

/// <summary>Curated shelf/category on the learner Video Library home.</summary>
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(Status), nameof(DisplayOrder))]
public class VideoCategory
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Title { get; set; } = default!;

    [MaxLength(128)]
    public string Slug { get; set; } = default!;

    [MaxLength(512)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Published;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Membership row: which videos appear in which category, ordered.</summary>
[Index(nameof(CategoryId), nameof(VideoId), IsUnique = true)]
[Index(nameof(VideoId))]
public class VideoCategoryItem
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string CategoryId { get; set; } = default!;

    public VideoCategory? Category { get; set; }

    [MaxLength(64)]
    public string VideoId { get; set; } = default!;

    public LibraryVideo? Video { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Caption/subtitle track sourced from an uploaded VTT MediaAsset and pushed to Bunny.</summary>
[Index(nameof(VideoId), nameof(LanguageCode), IsUnique = true)]
public class VideoCaptionTrack
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string VideoId { get; set; } = default!;

    public LibraryVideo? Video { get; set; }

    [MaxLength(16)]
    public string LanguageCode { get; set; } = default!;

    [MaxLength(64)]
    public string Label { get; set; } = default!;

    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public MediaAsset? MediaAsset { get; set; }

    public DateTimeOffset? SyncedToBunnyAt { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Downloadable attachment (PDF worksheet etc.) served via /v1/media/{id}/content.</summary>
[Index(nameof(VideoId), nameof(SortOrder))]
public class VideoAttachment
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string VideoId { get; set; } = default!;

    public LibraryVideo? Video { get; set; }

    [MaxLength(128)]
    public string Title { get; set; } = default!;

    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public MediaAsset? MediaAsset { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Per-user watch progress. UserId naming keeps this on the
/// UserHardDeleteService auto-purge path.</summary>
[Index(nameof(UserId), nameof(VideoId), IsUnique = true)]
[Index(nameof(UserId), nameof(LastWatchedAt))]
public class LearnerVideoLibraryProgress
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string VideoId { get; set; } = default!;

    /// <summary>Last playhead position (resume point).</summary>
    public int PositionSeconds { get; set; }

    /// <summary>Monotonic max of watched seconds — never decreases.</summary>
    public int WatchedSeconds { get; set; }

    public bool Completed { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset LastWatchedAt { get; set; }
}

/// <summary>Per-user bookmark toggle.</summary>
[Index(nameof(UserId), nameof(VideoId), IsUnique = true)]
public class LearnerVideoBookmark
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string VideoId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// An issued playback session for an attested native client. Holds no signed
/// URL (URLs are re-minted on renew); expiry bounds the CDN token lifetime.
/// </summary>
[Index(nameof(UserId), nameof(IssuedAt))]
[Index(nameof(VideoId), nameof(IssuedAt))]
public class VideoPlaybackSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string VideoId { get; set; } = default!;

    /// <summary>"tauri" | "capacitor-android" | "capacitor-ios".
    /// NOTE: spec said string(16) but "capacitor-android" is 17 chars — widened to 32.</summary>
    [MaxLength(32)]
    public string Platform { get; set; } = default!;

    [MaxLength(32)]
    public string AttestationKeyId { get; set; } = default!;

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(256)]
    public string? UserAgent { get; set; }

    [MaxLength(128)]
    public string? DeviceId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Single-use attestation nonce. Id IS the nonce (base64url, 32 random bytes).
/// Consumed atomically on playback-session verification.
/// </summary>
[Index(nameof(UserId), nameof(IssuedAt))]
public class VideoAttestationChallenge
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Widened to 32 — "capacitor-android" exceeds the specced 16.</summary>
    [MaxLength(32)]
    public string? Platform { get; set; }
}

/// <summary>Playback telemetry event (play/pause/seek/heartbeat/...).</summary>
[Index(nameof(VideoId), nameof(OccurredAt))]
[Index(nameof(UserId), nameof(OccurredAt))]
public class VideoPlaybackEvent
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string VideoId { get; set; } = default!;

    [MaxLength(64)]
    public string? SessionId { get; set; }

    [MaxLength(32)]
    public string EventType { get; set; } = default!;

    public int PositionSeconds { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string PayloadJson { get; set; } = "{}";
}

/// <summary>
/// Capture-protection / tamper telemetry from the video player (Course
/// Platform Security Requirements §2). Deliberately separate from
/// <see cref="VideoPlaybackEvent"/> (analytics) — these are security signals
/// that may trigger an audit write or a policy-gated session revoke; mixing
/// them into the analytics event stream would pollute both. Modeled on
/// <c>MockProctoringEvent</c> (kind whitelist + severity + capped batch ingest).
/// </summary>
[Index(nameof(UserId), nameof(OccurredAt))]
[Index(nameof(SessionId))]
[Index(nameof(Kind))]
public class VideoProtectionEvent
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? VideoId { get; set; }

    [MaxLength(64)]
    public string? SessionId { get; set; }

    /// <summary>One of <see cref="VideoProtectionKinds"/>.All.</summary>
    [MaxLength(48)]
    public string Kind { get; set; } = default!;

    /// <summary>"info" | "warning" | "critical".</summary>
    [MaxLength(16)]
    public string Severity { get; set; } = "info";

    [MaxLength(32)]
    public string? Platform { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string MetadataJson { get; set; } = "{}";
}

/// <summary>
/// Canonical <see cref="VideoProtectionEvent.Kind"/> whitelist. Frontend
/// (lib/api/video-protection.ts) and backend MUST share this contract.
/// </summary>
public static class VideoProtectionKinds
{
    /// <summary>OS-level capture protection engaged successfully on mount
    /// (Windows WDA_EXCLUDEFROMCAPTURE, macOS NSWindow.sharingType=None,
    /// Android FLAG_SECURE).</summary>
    public const string ProtectionEngaged = "protection_engaged";

    /// <summary>The current shell/platform has no capture-protection API
    /// (web, or a native shell build predating it) — expected on old shells,
    /// not itself a tamper signal.</summary>
    public const string ProtectionUnavailable = "protection_unavailable";

    /// <summary>iOS: `UIScreen.isCaptured` went true (screen recording or
    /// mirroring in progress).</summary>
    public const string CaptureDetected = "capture_detected";

    /// <summary>iOS: `UIApplication.userDidTakeScreenshotNotification` fired.
    /// Detection-after-the-fact — iOS cannot block a still screenshot of an
    /// arbitrary WKWebView (documented platform limitation).</summary>
    public const string ScreenshotDetected = "screenshot_detected";

    /// <summary>The watermark overlay's integrity watchdog found the node
    /// hidden, detached, or CSS-tampered.</summary>
    public const string WatermarkTampered = "watermark_tampered";

    /// <summary>Heuristic devtools-open signal (desktop runtime only) — a
    /// risk signal, never a sole gate.</summary>
    public const string DevtoolsSuspected = "devtools_suspected";

    /// <summary>Tab/window hidden while playing — risk signal only.</summary>
    public const string VisibilityHidden = "visibility_hidden";

    /// <summary>Window lost focus while playing — risk signal only.</summary>
    public const string FocusLost = "focus_lost";

    /// <summary>Root/jailbreak/emulator/debugger heuristic signal from the
    /// native attestation plugin (see Phase 8 — device-integrity checks).</summary>
    public const string IntegritySignal = "integrity_signal";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ProtectionEngaged, ProtectionUnavailable, CaptureDetected, ScreenshotDetected,
        WatermarkTampered, DevtoolsSuspected, VisibilityHidden, FocusLost, IntegritySignal,
    };

    public static readonly IReadOnlySet<string> Severities = new HashSet<string>(StringComparer.Ordinal)
    {
        "info", "warning", "critical",
    };

    /// <summary>Kinds that also warrant an AuditEvent write (high-severity
    /// forensic signal, not just telemetry) — mirrors the precedent in
    /// <c>VideoAttestationService.RecordFailureAsync</c>.</summary>
    public static readonly IReadOnlySet<string> AuditWorthy = new HashSet<string>(StringComparer.Ordinal)
    {
        CaptureDetected, ScreenshotDetected, WatermarkTampered, IntegritySignal,
    };

    public static string DefaultSeverity(string kind) => kind switch
    {
        CaptureDetected or ScreenshotDetected or WatermarkTampered or IntegritySignal => "critical",
        DevtoolsSuspected => "warning",
        _ => "info",
    };
}
