using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class TutoringSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertUserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? SubtestFocus { get; set; }

    public DateTimeOffset ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 30;

    [MaxLength(32)]
    public string State { get; set; } = "booked";         // "booked", "confirmed", "in_progress", "completed", "cancelled", "no_show"

    [MaxLength(512)]
    public string? RoomUrl { get; set; }

    [MaxLength(2048)]
    public string? LearnerNotes { get; set; }

    [MaxLength(2048)]
    public string? ExpertNotes { get; set; }

    public decimal Price { get; set; }

    [MaxLength(32)]
    public string? PaymentSource { get; set; }             // "credits", "direct"

    public int? LearnerRating { get; set; }                // 1-5 star rating
    public string? LearnerFeedback { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class TutoringAvailability
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ExpertUserId { get; set; } = default!;

    public int DayOfWeek { get; set; }                    // 0=Sunday, 6=Saturday

    [MaxLength(8)]
    public string StartTime { get; set; } = default!;     // "09:00"

    [MaxLength(8)]
    public string EndTime { get; set; } = default!;       // "17:00"

    [MaxLength(64)]
    public string Timezone { get; set; } = "UTC";

    public bool IsActive { get; set; } = true;
}
