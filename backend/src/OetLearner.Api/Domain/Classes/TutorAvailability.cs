using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain.Classes;

/// <summary>
/// Recurring weekly availability slot for a <see cref="Tutor"/>. Times are
/// interpreted in the tutor's <see cref="Tutor.TimeZone"/>.
/// </summary>
[Index(nameof(TutorId))]
[Index(nameof(TutorId), nameof(DayOfWeek))]
public class TutorAvailability
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    /// <summary>System day-of-week (Sunday = 0, Saturday = 6).</summary>
    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public bool IsActive { get; set; } = true;

    public Tutor? Tutor { get; set; }
}
