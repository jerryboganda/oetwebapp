using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ═════════════════════════════════════════════════════════════════════════════
// Content Paper subsystem — Slice 1
//
// A ContentPaper is the curatorial unit a learner picks ("Listening Sample 1",
// "Writing 3 (Urgent Referral)", "Speaking Card 4"). A paper bundles one or
// more typed assets (ContentPaperAsset) each pointing at a physical file
// (MediaAsset). This layering replaces the prior ContentItem single-document
// assumption for real OET material.
//
// See docs/CONTENT-UPLOAD-PLAN.md.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// The role that a file plays within a paper. Extend by adding enum values
/// — do not repurpose existing ones (their integer values are stored in
/// the DB and are part of the audit trail).
/// </summary>
public enum PaperAssetRole
{
    /// <summary>Listening MP3 (the sole audio for the paper).</summary>
    Audio = 0,
    /// <summary>PDF stimulus for any subtest.</summary>
    QuestionPaper = 1,
    /// <summary>Listening transcript.</summary>
    AudioScript = 2,
    /// <summary>Listening / Reading answer key.</summary>
    AnswerKey = 3,
    /// <summary>Writing stimulus case-notes.</summary>
    CaseNotes = 4,
    /// <summary>Writing reference / model answer.</summary>
    ModelAnswer = 5,
    /// <summary>Speaking role-play card.</summary>
    RoleCard = 6,
    /// <summary>Cross-cutting reference doc (e.g. Speaking Assessment Criteria).</summary>
    AssessmentCriteria = 7,
    /// <summary>Cross-cutting warm-up questions (Speaking).</summary>
    WarmUpQuestions = 8,
    /// <summary>Anything else the convention parser couldn't classify.</summary>
    Supplementary = 99,
}

/// <summary>
/// Curatorial unit. One paper = one user-selectable practice item, possibly
/// bundling multiple files (e.g. Listening audio + question paper + answer
/// key + transcript).
/// </summary>
[Index(nameof(SubtestCode), nameof(Status))]
[Index(nameof(ProfessionId), nameof(SubtestCode))]
[Index(nameof(CardType))]
[Index(nameof(LetterType))]
[Index(nameof(Slug), IsUnique = true)]
public class ContentPaper
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>listening | reading | writing | speaking</summary>
    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    /// <summary>URL-safe, unique. e.g. "listening-sample-1".</summary>
    [MaxLength(200)]
    public string Slug { get; set; } = default!;

    /// <summary>FK to <see cref="ProfessionReference"/>. Null when
    /// <see cref="AppliesToAllProfessions"/> is true.</summary>
    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    /// <summary>Explicit "same for all professions" flag. Listening and
    /// Reading papers carry this true. Preferred over null-ProfessionId so
    /// the learner data layer can query it directly.</summary>
    public bool AppliesToAllProfessions { get; set; }

    [MaxLength(32)]
    public string Difficulty { get; set; } = "standard";

    public int EstimatedDurationMinutes { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    /// <summary>FK to <see cref="ContentRevision"/> — the currently-published
    /// snapshot. Null until first publish.</summary>
    [MaxLength(64)]
    public string? PublishedRevisionId { get; set; }

    /// <summary>Speaking only. Matches <c>AiGroundingContext.CardType</c>:
    /// already_known_pt | follow_up_visit | examination |
    /// first_visit_emergency | first_visit_routine | breaking_bad_news.</summary>
    [MaxLength(64)]
    public string? CardType { get; set; }

    /// <summary>Writing only. Matches <c>AiGroundingContext.LetterType</c>:
    /// routine_referral | urgent_referral | non_medical_referral |
    /// update_discharge | update_referral_specialist_to_gp | transfer_letter.</summary>
    [MaxLength(64)]
    public string? LetterType { get; set; }

    /// <summary>Editorial weighting. Higher = surfaced first. "MOST
    /// IMPORTANT TYPE" folders map to 100; default 0.</summary>
    public int Priority { get; set; }

    /// <summary>Comma-separated free-form tags for filtering.</summary>
    [MaxLength(512)]
    public string TagsCsv { get; set; } = string.Empty;

    /// <summary>Copyright provenance — required before publish (Slice 8
    /// enforces). Recommended default:
    /// "Source: Project Real Content folder supplied by the project owner.
    /// Internal practice use only. Redistribution requires rights review."</summary>
    [MaxLength(256)]
    public string? SourceProvenance { get; set; }

    /// <summary>JSON blob of extracted text per asset. Populated async by
    /// Slice 7's PDF extraction worker. Used by the AI grounded gateway to
    /// build stimulus-aware prompts without re-reading PDFs at call time.</summary>
    public string ExtractedTextJson { get; set; } = "{}";

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    // Navigation
    public ICollection<ContentPaperAsset> Assets { get; set; } = new List<ContentPaperAsset>();
}

/// <summary>
/// One file slot on a paper. Uniqueness constraint: at most one primary
/// asset per (paper, role, part). Non-primary rows are alternates / older
/// revisions kept for audit.
/// </summary>
[Index(nameof(PaperId), nameof(Role))]
[Index(nameof(PaperId), nameof(Role), nameof(Part), nameof(IsPrimary),
    IsUnique = true, Name = "UX_PaperAsset_Primary_Per_RolePart")]
public class ContentPaperAsset
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PaperId { get; set; } = default!;

    public PaperAssetRole Role { get; set; }

    /// <summary>Part label. Reading uses "A", "B+C". Listening uses
    /// "Section1" / "Section2" / … if the team later splits one MP3 into
    /// part-level audio. Nullable — most papers have a single part per
    /// role.</summary>
    [MaxLength(16)]
    public string? Part { get; set; }

    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    [MaxLength(200)]
    public string? Title { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>True for the currently-displayed file for this role/part.
    /// The unique index keyed on (Paper, Role, Part, IsPrimary) — combined
    /// with the fact that only IsPrimary=true rows are enforced-unique via
    /// partial-index where possible — prevents duplicate "main" assets.
    /// Implementations that cannot express partial-unique (e.g. in-memory
    /// tests) rely on application-level checks in the service layer.</summary>
    public bool IsPrimary { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public ContentPaper? Paper { get; set; }
    public MediaAsset? MediaAsset { get; set; }
}
