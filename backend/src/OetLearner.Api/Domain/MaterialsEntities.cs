using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Controls who can see a MaterialFolder.
/// Inherit resolves upward to the nearest non-Inherit ancestor;
/// a root folder left as Inherit is hidden from all candidates (safe default).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MaterialAudienceMode
{
    Inherit,
    Everyone,
    Restricted,
}

/// <summary>
/// Nestable container for material files. Audience assignment lives here;
/// files inherit visibility from their parent folder.
/// </summary>
[Index(nameof(ParentFolderId), nameof(SortOrder))]
[Index(nameof(Status), nameof(SortOrder))]
public class MaterialFolder
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? ParentFolderId { get; set; }

    public MaterialFolder? ParentFolder { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    /// <summary>Optional organisational hint only — does not gate access.</summary>
    [MaxLength(32)]
    public string? SubtestCode { get; set; }

    public MaterialAudienceMode AudienceMode { get; set; } = MaterialAudienceMode.Inherit;

    public int SortOrder { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    [MaxLength(64)]
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<MaterialFolderAudience> Audiences { get; set; } = [];
}

/// <summary>
/// A single downloadable file. Wraps a MediaAsset; Kind is derived
/// server-side from MediaAsset.Format — never trusted from the client.
/// </summary>
[Index(nameof(FolderId), nameof(SortOrder))]
[Index(nameof(SubtestCode), nameof(Status))]
[Index(nameof(MediaAssetId))]
public class MaterialFile
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Null = root-level file (visible to all authenticated candidates when published).</summary>
    [MaxLength(64)]
    public string? FolderId { get; set; }

    public MaterialFolder? Folder { get; set; }

    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public MediaAsset? MediaAsset { get; set; }

    /// <summary>Required; lowercased. Drives the file-type rule.</summary>
    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    /// <summary>"pdf" | "audio" — derived from MediaAsset.Format on create/update; never accepted from client.</summary>
    [MaxLength(8)]
    public string Kind { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    [MaxLength(64)]
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Course/cohort targeting row for a MaterialFolder.
/// Only consulted when the folder's AudienceMode == Restricted.
/// </summary>
[Index(nameof(FolderId), nameof(TargetType), nameof(TargetId), IsUnique = true)]
public class MaterialFolderAudience
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string FolderId { get; set; } = default!;

    public MaterialFolder? Folder { get; set; }

    /// <summary>"plan" | "cohort" | "institution"</summary>
    [MaxLength(16)]
    public string TargetType { get; set; } = default!;

    /// <summary>PlanId or PlanCode for "plan"; CohortId for "cohort"; SponsorId for "institution".</summary>
    [MaxLength(64)]
    public string TargetId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}
