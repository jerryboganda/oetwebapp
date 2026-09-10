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

/// <summary>
/// Recall spelling — one row per (learner, recall word) the learner has spelled
/// incorrectly in Practice Spelling or the mini Spelling Test.
///
/// Deliberately stores only a reference to the existing
/// <see cref="VocabularyTerm"/>: the canonical spelling and the ElevenLabs audio
/// already live on that row, so nothing is duplicated and no audio is ever
/// regenerated. Removal is a delete (not a flag) — a word leaves the "Review
/// Mistakes" list the moment the learner spells it correctly, and re-enters with
/// a fresh count if they later miss it again.
///
/// Persisted server-side (not device-local) so the list survives logout, app
/// restart and switching devices. No AI/LLM involvement: pass/fail is a direct
/// string comparison against the stored canonical word.
/// </summary>
[Index(nameof(UserId), nameof(VocabularyTermId), IsUnique = true)]
[Index(nameof(UserId), nameof(LastWrongAt))]
public class RecallSpellingMistake
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>FK to the existing recall word. The word itself is never copied.</summary>
    [MaxLength(64)]
    public string VocabularyTermId { get; set; } = default!;

    /// <summary>How many times this word has been spelled incorrectly. Incremented
    /// on every subsequent miss; the row is deleted on a correct answer.</summary>
    public int WrongAttemptCount { get; set; }

    /// <summary>When the word was most recently spelled incorrectly.</summary>
    public DateTimeOffset LastWrongAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
