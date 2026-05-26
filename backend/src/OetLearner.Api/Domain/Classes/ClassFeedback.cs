using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain.Classes;

/// <summary>
/// Learner-submitted feedback for a single <see cref="LiveClassSession"/>.
/// A learner can submit at most one feedback row per session — the unique
/// index on (ClassSessionId, UserId) enforces idempotency; submitting
/// again updates the existing row.
/// </summary>
[Index(nameof(ClassSessionId))]
[Index(nameof(UserId))]
[Index(nameof(ClassSessionId), nameof(UserId), IsUnique = true)]
public class ClassFeedback
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ClassSessionId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>1 (lowest) to 5 (highest). Service layer enforces the range.</summary>
    public int Rating { get; set; }

    [MaxLength(4096)]
    public string? Comment { get; set; }

    public bool RecommendToFriend { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
