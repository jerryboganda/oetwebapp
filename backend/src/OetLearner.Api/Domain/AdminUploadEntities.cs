using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ═════════════════════════════════════════════════════════════════════════════
// Content Upload — Slice 2
//
// Chunked / resumable uploads for admin content-authoring workflow. Sits
// alongside (not replacing) the learner-facing UploadSession used for
// recording submissions. Dedicated entity keeps the two workflows cleanly
// separated.
// ═════════════════════════════════════════════════════════════════════════════

public enum AdminUploadState
{
    Started = 0,
    Uploading = 1,
    Completed = 2,
    Aborted = 3,
    Expired = 4,
}

/// <summary>
/// Admin chunked-upload session. One per file. Parts land in
/// <c>uploads/staging/{AdminId}/{Id}/{PartNumber}.bin</c>. On commit we
/// stream-hash all parts into a single content-addressed file under
/// <c>uploads/published/{sha[0..2]}/{sha[2..4]}/{sha}.{ext}</c>.
/// </summary>
[Index(nameof(AdminUserId), nameof(ExpiresAt))]
[Index(nameof(State), nameof(ExpiresAt))]
public class AdminUploadSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AdminUserId { get; set; } = default!;

    [MaxLength(256)]
    public string OriginalFilename { get; set; } = default!;

    [MaxLength(16)]
    public string Extension { get; set; } = default!;

    [MaxLength(64)]
    public string DeclaredMimeType { get; set; } = default!;

    /// <summary>Client-declared content size. Verified on commit.</summary>
    public long DeclaredSizeBytes { get; set; }

    public long ReceivedBytes { get; set; }
    public int TotalParts { get; set; }
    public int PartsReceived { get; set; }

    /// <summary>Intended use for the file. Drives per-role size validation
    /// on commit. Stringly-typed to avoid tight coupling to
    /// <c>PaperAssetRole</c> at this layer.</summary>
    [MaxLength(32)]
    public string IntendedRole { get; set; } = "Supplementary";

    public AdminUploadState State { get; set; } = AdminUploadState.Started;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Populated on commit.</summary>
    [MaxLength(64)]
    public string? Sha256 { get; set; }

    /// <summary>Populated on commit.</summary>
    [MaxLength(64)]
    public string? MediaAssetId { get; set; }
}
