using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// DB-managed practice-collection labels (recall sets) used to categorise
/// vocabulary terms. Replaces the static <see cref="RecallSetCodes"/>
/// registry — the static class is kept as a compile-time set of canonical
/// codes that are seeded into this table on first boot, but admins can
/// add/edit/archive their own labels from <c>/admin/content/vocabulary/recall-set-tags</c>.
/// </summary>
[Index(nameof(Code), IsUnique = true)]
[Index(nameof(IsActive), nameof(SortOrder))]
public class RecallSetTag
{
    /// <summary>Lowercase code persisted in <see cref="VocabularyTerm.RecallSetCodesJson"/>. Never rename.</summary>
    [Key]
    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(200)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(64)]
    public string? ShortLabel { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Optional scope: when set, this tag is only offered for the given exam (e.g. "oet").</summary>
    [MaxLength(16)]
    public string? ExamTypeCode { get; set; }

    [MaxLength(64)]
    public string? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
