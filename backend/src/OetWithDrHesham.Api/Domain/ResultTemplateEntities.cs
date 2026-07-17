using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Visual templates for OET-style result/score-report tables. After a learner
/// finishes a mock, the active template's image is shown on the result page
/// to match the visual style of a real OET candidate result.
/// </summary>
[Index(nameof(IsActive), nameof(SortOrder))]
[Index(nameof(ProfessionId), nameof(IsActive))]
[Index(nameof(TemplateKey), IsUnique = true)]
public class ResultTemplateAsset
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable slug, e.g. "ielts-style-band-table".</summary>
    [MaxLength(128)]
    public string TemplateKey { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    public string? Description { get; set; }

    /// <summary>Optional profession scope; null = shared across all professions.</summary>
    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    /// <summary>FK to <see cref="MediaAsset"/> (JPG / PNG).</summary>
    [MaxLength(64)]
    public string MediaAssetId { get; set; } = default!;

    public MediaAsset? MediaAsset { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    [MaxLength(64)]
    public string? UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
