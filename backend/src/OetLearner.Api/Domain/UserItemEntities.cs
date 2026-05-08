using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Phase 5 — Tool calling. User-scoped note that can be authored either by
/// the learner directly or by an AI tool (<c>save_user_note</c>) on the
/// learner's behalf. Source is recorded so admins can audit AI authorship.
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
public class UserNote
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(120)]
    public string Title { get; set; } = default!;

    [MaxLength(2000)]
    public string BodyMarkdown { get; set; } = default!;

    /// <summary>"user" | "ai_tool".</summary>
    [MaxLength(16)]
    public string Source { get; set; } = "user";

    /// <summary>When <see cref="Source"/> = "ai_tool", the feature code that
    /// authored it (e.g. <c>writing.coach.suggest</c>). Null otherwise.</summary>
    [MaxLength(64)]
    public string? CreatedByFeatureCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Phase 5 — Tool calling. Bookmark of a vocabulary / recall term to the
/// learner's revision queue.
/// </summary>
[Index(nameof(UserId), nameof(VocabularyTermId), IsUnique = true)]
[Index(nameof(UserId), nameof(CreatedAt))]
public class RecallBookmark
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string VocabularyTermId { get; set; } = default!;

    /// <summary>"user" | "ai_tool".</summary>
    [MaxLength(16)]
    public string Source { get; set; } = "user";

    [MaxLength(64)]
    public string? CreatedByFeatureCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
