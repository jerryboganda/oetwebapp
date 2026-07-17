using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Admin-authored skeleton for a study plan. The plan generator picks the best matching
/// template for a learner (weeks-to-exam, target band, profession, tier) and materialises
/// it by resolving each slot to concrete content (papers, drills, due review items).
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(IsActive), nameof(ExamTypeCode))]
public class StudyPlanTemplate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(64)]
    public string Slug { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "OET";

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    /// <summary>Inclusive lower bound of total-weeks window where this template applies.</summary>
    public int MinWeeks { get; set; } = 1;

    /// <summary>Inclusive upper bound of total-weeks window where this template applies.</summary>
    public int MaxWeeks { get; set; } = 52;

    [MaxLength(8)]
    public string? TargetBand { get; set; }

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    /// <summary>JSON array of focus tag strings, e.g. ["weak-writing","retake-rescue"].</summary>
    public string FocusTagsJson { get; set; } = "[]";

    public int DefaultMinutesPerDay { get; set; } = 60;

    /// <summary>
    /// JSON document encoding the week → day → slot structure. See
    /// docs/STUDY-PLANNER.md for the schema. Edits via admin UI.
    /// </summary>
    public string TemplateBodyJson { get; set; } = "{\"weeks\":[],\"checkpoints\":[]}";

    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;

    [MaxLength(64)]
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Many-to-many link between a template and the subscription tiers it is available to.
/// A learner's resolved tier must appear in at least one row for the template to be considered.
/// </summary>
[Index(nameof(TemplateId), nameof(TierCode), IsUnique = true)]
public class StudyPlanTemplateTier
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string TemplateId { get; set; } = default!;

    [MaxLength(32)]
    public string TierCode { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
