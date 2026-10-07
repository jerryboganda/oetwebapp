using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// A tutor/support handoff prepared by Sami (F-107/F-108/F-123, SAMI §8): a structured
/// summary of the learner's scores, evidenced errors and the exact issue from the chat,
/// created from the thread so the learner never repeats themselves. Tutors/admins read
/// these through the admin list endpoint; the learner sees status.
/// </summary>
public class CompanionHandoff
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>tutor (academic escalation) | support (technical/account).</summary>
    [MaxLength(16)]
    public string Route { get; set; } = "tutor";

    [MaxLength(64)]
    public string ThreadId { get; set; } = default!;

    /// <summary>The learner's own statement of the issue, verbatim.</summary>
    [MaxLength(1024)]
    public string Issue { get; set; } = default!;

    /// <summary>The prepared summary shown to the learner before sending.</summary>
    [MaxLength(4096)]
    public string Summary { get; set; } = default!;

    /// <summary>Scores/exam context at handoff time (structured, from memory).</summary>
    public string? ScoresJson { get; set; }

    /// <summary>Top Error DNA patterns at handoff time.</summary>
    public string? TopErrorsJson { get; set; }

    /// <summary>open | claimed | resolved.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "open";

    [MaxLength(64)]
    public string? HandledBy { get; set; }
    public DateTimeOffset? HandledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
