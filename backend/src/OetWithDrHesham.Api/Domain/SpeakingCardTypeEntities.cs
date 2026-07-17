using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

// Speaking module rebuild (2026-06-11 spec).
//
// `SpeakingCardType` is a fully admin-configurable taxonomy of role-play card
// types (the owner adds ~6 types later, e.g. "Card 4 – Examination Card").
// The type is HIDDEN from students at all times — it exists only to help human
// examiners and the AI scorer reason about what kind of scenario the card is.
//
// MISSION CRITICAL: no learner-facing endpoint or DTO may ever serialize a
// card type (id or name). It is surfaced only on admin/tutor/AI-scorer paths.
// This mirrors the `InterlocutorScript` leakage guarantee.

[Index(nameof(IsActive), nameof(SortOrder))]
public class SpeakingCardType
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Admin-facing label (e.g. "Examination Card", "Counselling
    /// Card"). Shown in admin/tutor UIs and passed to the AI scorer as
    /// marking guidance — never to the student.</summary>
    [MaxLength(120)]
    public string Name { get; set; } = default!;

    /// <summary>Optional longer description of what this card type tests and
    /// how it should be marked. Fed to the AI scorer prompt as guidance.</summary>
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Display ordering within the admin list (ascending).</summary>
    public int SortOrder { get; set; }

    /// <summary>Soft-delete flag. Card types that are referenced by existing
    /// cards are deactivated (IsActive=false) rather than hard-deleted so
    /// historical cards keep their type label.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
