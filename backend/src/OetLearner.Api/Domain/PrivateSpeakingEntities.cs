using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// ── Module Configuration ──────────────────────────────────────────────

/// <summary>Global configuration for the Private Speaking Sessions module.</summary>
public class PrivateSpeakingConfig
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = "ps-config-singleton";

    public bool IsEnabled { get; set; } = true;

    /// <summary>Default slot duration in minutes.</summary>
    public int DefaultSlotDurationMinutes { get; set; } = 30;

    /// <summary>Buffer time in minutes between consecutive tutor slots.</summary>
    public int BufferMinutesBetweenSlots { get; set; } = 10;

    /// <summary>Minimum lead time in hours before a slot can be booked.</summary>
    public int MinBookingLeadTimeHours { get; set; } = 24;

    /// <summary>Maximum advance days a slot can be booked.</summary>
    public int MaxBookingAdvanceDays { get; set; } = 30;

    /// <summary>How long (minutes) a slot is held during checkout before auto-release.</summary>
    public int ReservationTimeoutMinutes { get; set; } = 15;

    /// <summary>Default price in minor units (cents). Can be overridden per tutor.</summary>
    public int DefaultPriceMinorUnits { get; set; } = 5000; // $50.00

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    /// <summary>Cancellation window in hours before session start.</summary>
    public int CancellationWindowHours { get; set; } = 24;

    /// <summary>Whether learners can reschedule confirmed bookings.</summary>
    public bool AllowReschedule { get; set; } = true;

    /// <summary>Reschedule window in hours before session start.</summary>
    public int RescheduleWindowHours { get; set; } = 24;

    /// <summary>JSON array of reminder offsets in hours before session, e.g. [24, 1].</summary>
    [MaxLength(256)]
    public string ReminderOffsetsHoursJson { get; set; } = "[24, 1]";

    /// <summary>Whether to send daily digest reminders for upcoming sessions.</summary>
    public bool DailyReminderEnabled { get; set; } = true;

    /// <summary>Hour of the day (UTC) to send daily reminders.</summary>
    public int DailyReminderHourUtc { get; set; } = 8;

    [MaxLength(2048)]
    public string? CancellationPolicyText { get; set; }

    [MaxLength(2048)]
    public string? BookingPolicyText { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

// ── Tutor Profile ─────────────────────────────────────────────────────

/// <summary>Profile for an expert/tutor eligible for Private Speaking Sessions.</summary>
[Index(nameof(ExpertUserId), IsUnique = true)]
public class PrivateSpeakingTutorProfile
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertUserId { get; set; } = default!;

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(256)]
    public string? Bio { get; set; }

    [MaxLength(64)]
    public string Timezone { get; set; } = "UTC";

    /// <summary>Tutor-specific price override in minor units. Null = use module default.</summary>
    public int? PriceOverrideMinorUnits { get; set; }

    /// <summary>Tutor-specific slot duration override. Null = use module default.</summary>
    public int? SlotDurationOverrideMinutes { get; set; }

    /// <summary>JSON array of specialties, e.g. ["medicine", "nursing"].</summary>
    [MaxLength(1024)]
    public string SpecialtiesJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;

    public int TotalSessions { get; set; }

    public double AverageRating { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ExpertUser? ExpertUser { get; set; }
}

// ── Availability Rules ────────────────────────────────────────────────

/// <summary>Recurring weekly availability rule for a tutor.</summary>
[Index(nameof(TutorProfileId), nameof(DayOfWeek))]
public class PrivateSpeakingAvailabilityRule
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string TutorProfileId { get; set; } = default!;

    /// <summary>0 = Sunday, 6 = Saturday.</summary>
    public int DayOfWeek { get; set; }

    /// <summary>Start time in HH:mm format.</summary>
    [MaxLength(8)]
    public string StartTime { get; set; } = default!;

    /// <summary>End time in HH:mm format.</summary>
    [MaxLength(8)]
    public string EndTime { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    /// <summary>Optional effective start date for this rule.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>Optional effective end date for this rule.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public PrivateSpeakingTutorProfile? TutorProfile { get; set; }
}

/// <summary>Date-specific availability override (block or add availability on a specific date).</summary>
[Index(nameof(TutorProfileId), nameof(Date))]
public class PrivateSpeakingAvailabilityOverride
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string TutorProfileId { get; set; } = default!;

    public DateOnly Date { get; set; }

    /// <summary>Whether this date is blocked (unavailable) or an extra availability window.</summary>
    public PrivateSpeakingOverrideType OverrideType { get; set; }

    /// <summary>Start time for extra availability. null if blocked.</summary>
    [MaxLength(8)]
    public string? StartTime { get; set; }

    /// <summary>End time for extra availability. null if blocked.</summary>
    [MaxLength(8)]
    public string? EndTime { get; set; }

    [MaxLength(256)]
    public string? Reason { get; set; }

    public PrivateSpeakingTutorProfile? TutorProfile { get; set; }
}

// ── Booking ───────────────────────────────────────────────────────────

/// <summary>A Private Speaking Session booking record.</summary>
[Index(nameof(LearnerUserId), nameof(Status))]
[Index(nameof(TutorProfileId), nameof(SessionStartUtc))]
[Index(nameof(StripeCheckoutSessionId))]
[Index(nameof(IdempotencyKey), IsUnique = true)]
public class PrivateSpeakingBooking
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    [MaxLength(64)]
    public string TutorProfileId { get; set; } = default!;

    /// <summary>Booking lifecycle status.</summary>
    public PrivateSpeakingBookingStatus Status { get; set; } = PrivateSpeakingBookingStatus.Reserved;

    /// <summary>Session start time in UTC.</summary>
    public DateTimeOffset SessionStartUtc { get; set; }

    /// <summary>Session duration in minutes.</summary>
    public int DurationMinutes { get; set; } = 30;

    /// <summary>Tutor timezone at time of booking (for display).</summary>
    [MaxLength(64)]
    public string TutorTimezone { get; set; } = "UTC";

    /// <summary>Learner timezone at time of booking (for display).</summary>
    [MaxLength(64)]
    public string LearnerTimezone { get; set; } = "UTC";

    /// <summary>Price charged in minor units (cents).</summary>
    public int PriceMinorUnits { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    // ── Payment ──

    [MaxLength(256)]
    public string? StripeCheckoutSessionId { get; set; }

    [MaxLength(256)]
    public string? StripePaymentIntentId { get; set; }

    public PrivateSpeakingPaymentStatus PaymentStatus { get; set; } = PrivateSpeakingPaymentStatus.Pending;

    public DateTimeOffset? PaymentConfirmedAt { get; set; }

    // ── Zoom ──

    public long? ZoomMeetingId { get; set; }

    [MaxLength(512)]
    public string? ZoomJoinUrl { get; set; }

    [MaxLength(512)]
    public string? ZoomStartUrl { get; set; }

    [MaxLength(64)]
    public string? ZoomMeetingPassword { get; set; }

    public PrivateSpeakingZoomStatus ZoomStatus { get; set; } = PrivateSpeakingZoomStatus.Pending;

    [MaxLength(512)]
    public string? ZoomError { get; set; }

    public int ZoomRetryCount { get; set; }

    // ── Reservation ──

    /// <summary>When the slot reservation expires if payment not completed.</summary>
    public DateTimeOffset? ReservationExpiresAt { get; set; }

    /// <summary>Unique key for idempotent booking creation.</summary>
    [MaxLength(128)]
    public string? IdempotencyKey { get; set; }

    // ── Session Details ──

    [MaxLength(2048)]
    public string? LearnerNotes { get; set; }

    [MaxLength(2048)]
    public string? TutorNotes { get; set; }

    public int? LearnerRating { get; set; }

    [MaxLength(1024)]
    public string? LearnerFeedback { get; set; }

    // ── Cancellation / Reschedule ──

    [MaxLength(64)]
    public string? CancelledBy { get; set; }

    [MaxLength(512)]
    public string? CancellationReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    [MaxLength(64)]
    public string? RescheduledFromBookingId { get; set; }

    // ── Reminders ──

    /// <summary>JSON array of reminder offsets already sent, e.g. [24, 1].</summary>
    [MaxLength(256)]
    public string RemindersSentJson { get; set; } = "[]";

    public bool DailyReminderSent { get; set; }

    // ── Audit ──

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // ── Navigation ──

    public PrivateSpeakingTutorProfile? TutorProfile { get; set; }
}

// ── Audit Log ─────────────────────────────────────────────────────────

/// <summary>Audit trail for critical Private Speaking actions.</summary>
[Index(nameof(BookingId))]
[Index(nameof(ActorId), nameof(CreatedAt))]
public class PrivateSpeakingAuditLog
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? BookingId { get; set; }

    [MaxLength(64)]
    public string ActorId { get; set; } = default!;

    [MaxLength(32)]
    public string ActorRole { get; set; } = default!;

    [MaxLength(64)]
    public string Action { get; set; } = default!;

    [MaxLength(2048)]
    public string? Details { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

// ── Enums ─────────────────────────────────────────────────────────────

public enum PrivateSpeakingOverrideType
{
    Blocked,
    ExtraAvailability
}

public enum PrivateSpeakingBookingStatus
{
    Reserved,          // Slot held during checkout
    PendingPayment,    // Checkout started but not completed
    Confirmed,         // Payment succeeded, booking confirmed
    ZoomPending,       // Confirmed, Zoom meeting creation in progress
    ZoomCreated,       // Zoom meeting successfully created
    InProgress,        // Session currently happening
    Completed,         // Session finished
    Cancelled,         // Cancelled (by learner, tutor, or admin)
    Refunded,          // Refund processed
    NoShow,            // Learner did not attend
    Expired,           // Reservation expired without payment
    Failed             // Payment or system failure
}

public enum PrivateSpeakingPaymentStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Refunded,
    PartialRefund
}

public enum PrivateSpeakingZoomStatus
{
    Pending,
    Creating,
    Created,
    Failed,
    Deleted
}
