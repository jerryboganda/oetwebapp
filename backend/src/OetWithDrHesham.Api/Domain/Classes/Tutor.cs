using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain.Classes;

/// <summary>
/// Tutor profile for the Zoom-backed live classes module. Distinct from
/// <see cref="PrivateSpeakingTutorProfile"/> (1:1 sessions) — this is the
/// principal tutor record for masterclasses, group classes, mock reviews,
/// and office hours.
/// </summary>
/// <remarks>
/// One Tutor row per <see cref="LearnerUser"/> (FK by UserId). Specialties
/// and Languages are stored as JSON arrays for portability. ZoomUserId is
/// the Zoom-side user id used as the meeting host for sessions this tutor
/// owns; null falls back to the platform default host.
/// </remarks>
[Index(nameof(UserId), IsUnique = true)]
[Index(nameof(IsActive))]
public class Tutor
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Foreign key to the underlying <see cref="LearnerUser"/> row.</summary>
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(128)]
    public string? DisplayNameAr { get; set; }

    [MaxLength(4096)]
    public string Bio { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string? BioAr { get; set; }

    [MaxLength(512)]
    public string? AvatarUrl { get; set; }

    /// <summary>JSON array of specialty codes, e.g. ["nursing", "medicine"].</summary>
    [MaxLength(1024)]
    public string SpecialtiesJson { get; set; } = "[]";

    /// <summary>JSON array of BCP-47 language codes, e.g. ["en", "ar"].</summary>
    [MaxLength(512)]
    public string LanguagesJson { get; set; } = "[]";

    /// <summary>Hourly rate in USD. Null = use platform default.</summary>
    public decimal? HourlyRateUsd { get; set; }

    /// <summary>IANA time zone, e.g. "Australia/Sydney".</summary>
    [MaxLength(64)]
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Zoom-side user id used as the meeting host for sessions this tutor
    /// owns. When null, ZoomMeetingService falls back to the platform
    /// default <see cref="OetWithDrHesham.Api.Configuration.ZoomOptions.HostUserId"/>.
    /// </summary>
    [MaxLength(128)]
    public string? ZoomUserId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public LearnerUser? User { get; set; }
    public List<TutorAvailability> Availability { get; set; } = [];
}
