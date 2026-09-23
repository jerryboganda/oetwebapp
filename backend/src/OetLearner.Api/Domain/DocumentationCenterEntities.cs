using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Owner-only Admin Documentation Center — the evidence-grade technical/innovation
/// record used for the UAE Golden Residence nomination pack (and possible UK
/// Innovator Founder route). Gated on <see cref="AdminPermissions.SystemAdmin"/>
/// everywhere (there is no separate "Owner" role in this codebase; SystemAdmin is
/// already the top tier with <see cref="AdminPermissions.All"/>).
/// </summary>
public enum DocumentationVersionStatus
{
    Draft,
    TechnicalReview,
    OwnerReview,
    Approved,
    Published,
    Superseded,
}

public enum DocumentationEvidenceType
{
    Code,
    Deployment,
    Testing,
    Architecture,
    ProductUi,
    AiModel,
    DataKnowledge,
    Security,
    Reliability,
    SignedDeclaration,
}

public enum DocumentationExportType
{
    Master,
    Module,
    EvidenceAnnex,
}

public enum DocumentationExportMode
{
    /// <summary>Full detail, for the owner only. Never leaves the admin panel un-watermarked.</summary>
    Internal,
    /// <summary>Sanitized: blocks/evidence flagged <c>IsInternalOnly</c> are dropped. Safe for immigration/external review.</summary>
    External,
}

/// <summary>One of the 15 specialist reports, the Master Dossier, or the Evidence Annex.</summary>
public class DocumentationModule
{
    /// <summary>Stable code, e.g. "DOC-01".."DOC-15", "MASTER", "EVIDENCE-ANNEX".</summary>
    [Key]
    [MaxLength(24)]
    public string Id { get; set; } = default!;

    [MaxLength(160)]
    public string Title { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public int SortOrder { get; set; }

    public ICollection<DocumentationVersion> Versions { get; set; } = new List<DocumentationVersion>();
    public ICollection<DocumentationEvidenceItem> EvidenceItems { get; set; } = new List<DocumentationEvidenceItem>();
}

/// <summary>
/// A versioned rendering of one module's content. <see cref="ContentJson"/> is a
/// serialized <c>List&lt;DocumentationSectionBlock&gt;</c> — ordered sections, each
/// independently flaggable as internal-only so the External/Immigration export can
/// drop it without a separate redaction pass.
/// </summary>
public class DocumentationVersion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(24)]
    public string ModuleId { get; set; } = default!;
    public DocumentationModule? Module { get; set; }

    public int VersionNumber { get; set; }

    public DocumentationVersionStatus Status { get; set; } = DocumentationVersionStatus.Published;

    /// <summary>Serialized <c>List&lt;DocumentationSectionBlock&gt;</c> — see <see cref="DocumentationSectionBlock"/>.</summary>
    public string ContentJson { get; set; } = "[]";

    [MaxLength(64)]
    public string? SourceRepoCommitSha { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    [MaxLength(128)]
    public string? ApprovedByName { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Only one version per module may be current; PDF export always reads the current version.</summary>
    public bool IsCurrent { get; set; }
}

/// <summary>One ordered content block within a <see cref="DocumentationVersion"/>. Plain POCO, serialized as JSON — not its own table.</summary>
public sealed record DocumentationSectionBlock(string Heading, string BodyMarkdown, bool IsInternalOnly = false);

/// <summary>
/// A single sourced claim: <c>EV-[MODULE]-[NUMBER]</c> resolving to a real, checkable
/// source (commit SHA, config key, doc path, test run id) — never an unsourced assertion.
/// </summary>
public class DocumentationEvidenceItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>e.g. "EV-WRITE-001". Unique.</summary>
    [MaxLength(40)]
    public string EvidenceId { get; set; } = default!;

    [MaxLength(24)]
    public string ModuleId { get; set; } = default!;
    public DocumentationModule? Module { get; set; }

    public DocumentationEvidenceType EvidenceType { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    /// <summary>The real, checkable source: commit SHA, config key/file path, doc path, or test run id. Can list several sibling files, so kept generous.</summary>
    [MaxLength(512)]
    public string SourceReference { get; set; } = default!;

    /// <summary>Optional attached file (screenshot, signed declaration scan) via the existing <c>MediaAsset</c>/<c>IFileStorage</c> pipeline.</summary>
    [MaxLength(64)]
    public string? MediaAssetId { get; set; }

    /// <summary>Dropped from the External/Immigration export.</summary>
    public bool IsInternalOnly { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Audit trail of every generated/downloaded PDF pack, so a prior export can be reproduced and its hash checked.</summary>
public class DocumentationExport
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    public DocumentationExportType ExportType { get; set; }
    public DocumentationExportMode Mode { get; set; }

    /// <summary>Set only for <see cref="DocumentationExportType.Module"/>.</summary>
    [MaxLength(24)]
    public string? ModuleId { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    [MaxLength(64)]
    public string GeneratedByUserId { get; set; } = default!;

    [MaxLength(128)]
    public string GeneratedByName { get; set; } = default!;

    [MaxLength(64)]
    public string Sha256Hash { get; set; } = default!;

    /// <summary>The generated PDF, stored via <c>IFileStorage</c> so a prior export can be re-downloaded byte-for-byte.</summary>
    [MaxLength(64)]
    public string? MediaAssetId { get; set; }

    /// <summary>Serialized <c>List&lt;string&gt;</c> of the <see cref="DocumentationVersion.Id"/> values included in this export.</summary>
    public string IncludedVersionIdsJson { get; set; } = "[]";
}
