using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingPathwayItem
{
    public Guid Id { get; set; }

    public Guid PathwayId { get; set; }

    public int OrderIndex { get; set; }

    [MaxLength(32)]
    public string Stage { get; set; } = "foundation";

    [MaxLength(32)]
    public string Phase { get; set; } = "warm-up";

    [MaxLength(4)]
    public string? FocusSkill { get; set; }

    [MaxLength(32)]
    public string? FocusCriterion { get; set; }

    [MaxLength(32)]
    public string ItemKind { get; set; } = "letter";

    [MaxLength(64)]
    public string? ContentRefId { get; set; }

    public int? WeekNumber { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "pending";

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
