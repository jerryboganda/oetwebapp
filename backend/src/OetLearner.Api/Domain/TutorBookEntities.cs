using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>OET 2026 Tutor Book — recall update / amendment posted by admin
/// and surfaced inside the learner's Tutor Book reader "Updates" tab.</summary>
[Index(nameof(PublishedAt))]
[Index(nameof(Audience))]
public class TutorBookUpdate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>all | medicine | nursing | pharmacy</summary>
    [MaxLength(16)]
    public string Audience { get; set; } = "all";

    public DateTimeOffset PublishedAt { get; set; }

    public bool IsPublished { get; set; } = true;

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? CreatedByAdminName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>OET 2026 Tutor Book — audio script (per chapter) the learner
/// can play inside the reader. Backed by admin uploads.</summary>
public class TutorBookAudioScript
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string Chapter { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string AudioUrl { get; set; } = default!;

    [MaxLength(1024)]
    public string? TranscriptUrl { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsPublished { get; set; } = true;

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? CreatedByAdminName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
