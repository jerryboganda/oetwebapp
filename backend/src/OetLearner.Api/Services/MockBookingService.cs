using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

/// <summary>
/// Mocks V2 Wave 4 — Mock booking lifecycle.
/// Operates on the canonical <see cref="MockBooking"/> entity which uses
/// <see cref="MockBookingStatuses"/> string constants and includes Wave 6
/// live-room state + Zoom fields. Reminders (24h / 1h before
/// <c>ScheduledStartAt</c>) are emitted by a background worker that scans
/// upcoming bookings; this service only handles CRUD.
/// </summary>
public sealed class MockBookingService
{
    public const int MaxReschedulesPerBooking = 3;
    public const int MinLeadTimeHours = 12;

    private readonly LearnerDbContext _db;

    public MockBookingService(LearnerDbContext db) { _db = db; }

    public async Task<object> ListForUserAsync(string userId, CancellationToken ct)
    {
        var rows = await _db.MockBookings.AsNoTracking()
            .Include(x => x.MockBundle)
            .Where(x => x.UserId == userId && x.Status != MockBookingStatuses.Cancelled)
            .OrderBy(x => x.ScheduledStartAt)
            .ToListAsync(ct);
        return new
        {
            items = rows.Select(r => Project(r, isAdmin: false)).ToArray(),
            now = DateTimeOffset.UtcNow,
        };
    }

    public async Task<object> ListForAdminAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = _db.MockBookings.AsNoTracking()
            .Include(x => x.MockBundle)
            .AsQueryable();
        if (from.HasValue) query = query.Where(x => x.ScheduledStartAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.ScheduledStartAt <= to.Value);
        var rows = await query.OrderBy(x => x.ScheduledStartAt).ToListAsync(ct);
        return new { items = rows.Select(r => Project(r, isAdmin: true)).ToArray() };
    }

    public async Task<object> CreateAsync(string userId, MockBookingCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MockBundleId))
            throw ApiException.Validation("invalid_request", "mockBundleId is required.");
        if (request.ScheduledStartAt <= DateTimeOffset.UtcNow.AddHours(MinLeadTimeHours))
            throw ApiException.Validation("lead_time_too_short",
                $"Bookings must be at least {MinLeadTimeHours} hours in advance.");

        var bundle = await _db.MockBundles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.MockBundleId, ct)
            ?? throw ApiException.NotFound("bundle_not_found", "Mock bundle not found.");
        if (bundle.Status != ContentStatus.Published)
            throw ApiException.Validation("bundle_not_published", "Bundle must be published before booking.");

        var deliveryMode = string.IsNullOrWhiteSpace(request.DeliveryMode)
            ? MockDeliveryModes.Computer
            : (MockDeliveryModes.IsValid(request.DeliveryMode) ? request.DeliveryMode! : MockDeliveryModes.Computer);

        var now = DateTimeOffset.UtcNow;
        var booking = new MockBooking
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            MockBundleId = bundle.Id,
            ScheduledStartAt = request.ScheduledStartAt.ToUniversalTime(),
            TimezoneIana = string.IsNullOrWhiteSpace(request.TimezoneIana) ? "UTC" : request.TimezoneIana!,
            Status = MockBookingStatuses.Scheduled,
            DeliveryMode = deliveryMode,
            ConsentToRecording = request.ConsentToRecording,
            LearnerNotes = request.LearnerNotes,
            LiveRoomState = MockLiveRoomStates.Waiting,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.MockBookings.Add(booking);
        await _db.SaveChangesAsync(ct);
        return Project(booking, isAdmin: false, bundle);
    }

    public async Task<object> RescheduleAsync(string userId, string bookingId, MockBookingRescheduleRequest request, CancellationToken ct)
    {
        var booking = await _db.MockBookings
            .Include(x => x.MockBundle)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("booking_not_found", "Booking not found.");
        if (IsTerminal(booking.Status))
            throw ApiException.Validation("booking_finalized", "This booking can no longer be rescheduled.");
        if (booking.RescheduleCount >= MaxReschedulesPerBooking)
            throw ApiException.Validation("reschedule_cap_reached",
                $"Bookings can be rescheduled up to {MaxReschedulesPerBooking} times.");
        if (request.ScheduledStartAt <= DateTimeOffset.UtcNow.AddHours(MinLeadTimeHours))
            throw ApiException.Validation("lead_time_too_short",
                $"Reschedule must land at least {MinLeadTimeHours} hours in the future.");

        booking.ScheduledStartAt = request.ScheduledStartAt.ToUniversalTime();
        if (!string.IsNullOrWhiteSpace(request.TimezoneIana)) booking.TimezoneIana = request.TimezoneIana!;
        booking.RescheduleCount++;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Project(booking, isAdmin: false);
    }

    public async Task<object> CancelAsync(string userId, string bookingId, CancellationToken ct)
    {
        var booking = await _db.MockBookings
            .Include(x => x.MockBundle)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("booking_not_found", "Booking not found.");
        if (booking.Status == MockBookingStatuses.Completed || booking.Status == MockBookingStatuses.Cancelled)
        {
            return Project(booking, isAdmin: false);
        }
        booking.Status = MockBookingStatuses.Cancelled;
        booking.CancelledAt = DateTimeOffset.UtcNow;
        booking.UpdatedAt = booking.CancelledAt.Value;
        await _db.SaveChangesAsync(ct);
        return Project(booking, isAdmin: false);
    }

    public async Task<object> AssignStaffAsync(string adminId, string bookingId, MockBookingAssignmentRequest request, CancellationToken ct)
    {
        var booking = await _db.MockBookings.FirstOrDefaultAsync(x => x.Id == bookingId, ct)
            ?? throw ApiException.NotFound("booking_not_found", "Booking not found.");
        if (request.AssignedTutorId is not null) booking.AssignedTutorId = request.AssignedTutorId;
        if (request.AssignedInterlocutorId is not null) booking.AssignedInterlocutorId = request.AssignedInterlocutorId;
        if (!string.IsNullOrWhiteSpace(request.Status) && MockBookingStatuses.IsValid(request.Status))
        {
            booking.Status = request.Status!;
        }
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = booking.UpdatedAt,
            ActorId = adminId,
            ActorName = adminId,
            Action = "mock_booking_assigned",
            ResourceType = "MockBooking",
            ResourceId = booking.Id,
            Details = JsonSupport.Serialize(new { request.AssignedTutorId, request.AssignedInterlocutorId, request.Status }),
        });
        await _db.SaveChangesAsync(ct);
        return Project(booking, isAdmin: true);
    }

    /// <summary>
    /// Mocks V2 Wave 6 — transition the live-room state for a Speaking mock.
    /// Allowed transitions enforced server-side.
    /// </summary>
    public async Task<object> TransitionLiveRoomAsync(string actorId, bool isAdmin, string bookingId, string targetState, CancellationToken ct)
    {
        if (!MockLiveRoomStates.IsValid(targetState))
            throw ApiException.Validation("invalid_state", "Unknown live-room state.");

        var booking = await _db.MockBookings.FirstOrDefaultAsync(x => x.Id == bookingId, ct)
            ?? throw ApiException.NotFound("booking_not_found", "Booking not found.");
        if (!isAdmin && booking.UserId != actorId && booking.AssignedTutorId != actorId && booking.AssignedInterlocutorId != actorId)
            throw ApiException.Forbidden("forbidden", "You cannot transition this booking.");

        // Only forward transitions are allowed; tutor_no_show is a terminal admin-only state.
        var current = booking.LiveRoomState;
        var allowed = (current, targetState) switch
        {
            (MockLiveRoomStates.Waiting, MockLiveRoomStates.InProgress) => true,
            (MockLiveRoomStates.InProgress, MockLiveRoomStates.Completed) => true,
            (MockLiveRoomStates.Waiting, MockLiveRoomStates.TutorNoShow) => isAdmin,
            (MockLiveRoomStates.InProgress, MockLiveRoomStates.TutorNoShow) => isAdmin,
            _ => false,
        };
        if (!allowed) throw ApiException.Validation("invalid_transition", $"Cannot move from {current} to {targetState}.");

        booking.LiveRoomState = targetState;
        if (targetState == MockLiveRoomStates.InProgress) booking.Status = MockBookingStatuses.InProgress;
        if (targetState == MockLiveRoomStates.Completed)
        {
            booking.Status = MockBookingStatuses.Completed;
            booking.CompletedAt = DateTimeOffset.UtcNow;
        }
        if (targetState == MockLiveRoomStates.TutorNoShow) booking.Status = MockBookingStatuses.TutorNoShow;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Project(booking, isAdmin: isAdmin);
    }

    /// <summary>
    /// Mocks V2 Wave 6 — fetch a single booking, projected for the requesting
    /// learner. Owner-or-assigned-staff only; tutor identity, Zoom start URL,
    /// and the interlocutor card are excluded by <see cref="Project"/>.
    /// </summary>
    public async Task<object> GetForUserAsync(string userId, string bookingId, CancellationToken ct)
    {
        var booking = await _db.MockBookings.AsNoTracking()
            .Include(x => x.MockBundle)
            .FirstOrDefaultAsync(x => x.Id == bookingId, ct)
            ?? throw ApiException.NotFound("booking_not_found", "Booking not found.");
        if (booking.UserId != userId)
        {
            throw ApiException.Forbidden("forbidden", "You cannot view this booking.");
        }
        return Project(booking, isAdmin: false);
    }

    /// <summary>
    /// Mocks V2 Wave 6 — fetch a single booking with the full tutor-side
    /// projection including the interlocutor card content lifted from the
    /// bound Speaking <see cref="ContentPaper"/>. Caller must be admin or the
    /// assigned tutor / interlocutor; learners are refused even if they own
    /// the booking, because this projection exposes tutor-only fields.
    /// </summary>
    public async Task<object> GetForExpertAsync(string actorId, bool isAdmin, string bookingId, CancellationToken ct)
    {
        var booking = await _db.MockBookings.AsNoTracking()
            .Include(x => x.MockBundle!).ThenInclude(b => b!.Sections)
            .FirstOrDefaultAsync(x => x.Id == bookingId, ct)
            ?? throw ApiException.NotFound("booking_not_found", "Booking not found.");
        if (!isAdmin && booking.AssignedTutorId != actorId && booking.AssignedInterlocutorId != actorId)
        {
            throw ApiException.Forbidden("forbidden", "You are not assigned to this booking.");
        }

        var projection = (Dictionary<string, object?>)Project(booking, isAdmin: true);

        // Embed the speaking interlocutor card content from the bound speaking
        // section, if any. This payload is tutor-only — the learner-side
        // projection deliberately omits it.
        var speakingSection = booking.MockBundle?.Sections
            .OrderBy(s => s.SectionOrder)
            .FirstOrDefault(s => string.Equals(s.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase));
        if (speakingSection is not null)
        {
            var paper = await _db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == speakingSection.ContentPaperId, ct);
            if (paper is not null)
            {
                projection["speakingPaperId"] = paper.Id;
                projection["speakingContent"] = SpeakingContentStructure.BuildContentItemDetail(paper);
            }
        }

        return projection;
    }

    private static bool IsTerminal(string status) => status == MockBookingStatuses.Completed
        || status == MockBookingStatuses.Cancelled
        || status == MockBookingStatuses.LearnerNoShow
        || status == MockBookingStatuses.TutorNoShow;

    private static object Project(MockBooking b, bool isAdmin, MockBundle? bundle = null)
    {
        bundle ??= b.MockBundle;
        var baseObj = new Dictionary<string, object?>
        {
            ["id"] = b.Id,
            ["bookingId"] = b.Id,
            ["mockBundleId"] = b.MockBundleId,
            ["title"] = bundle?.Title ?? "Scheduled mock",
            ["mockBundleTitle"] = bundle?.Title,
            ["mockAttemptId"] = b.MockAttemptId,
            ["scheduledStartAt"] = b.ScheduledStartAt,
            ["timezoneIana"] = b.TimezoneIana,
            ["status"] = b.Status,
            ["liveRoomState"] = b.LiveRoomState,
            ["deliveryMode"] = b.DeliveryMode,
            ["rescheduleCount"] = b.RescheduleCount,
            ["consentToRecording"] = b.ConsentToRecording,
            ["releasePolicy"] = bundle?.ReleasePolicy ?? MockReleasePolicies.Instant,
            ["createdAt"] = b.CreatedAt,
            ["updatedAt"] = b.UpdatedAt,
            ["completedAt"] = b.CompletedAt,
            ["cancelledAt"] = b.CancelledAt,
        };
        if (isAdmin)
        {
            baseObj["userId"] = b.UserId;
            baseObj["assignedTutorId"] = b.AssignedTutorId;
            baseObj["assignedInterlocutorId"] = b.AssignedInterlocutorId;
            baseObj["learnerNotes"] = b.LearnerNotes;
            baseObj["zoomMeetingId"] = b.ZoomMeetingId;
            baseObj["zoomJoinUrl"] = b.ZoomJoinUrl;
            baseObj["zoomStartUrl"] = b.ZoomStartUrl;
            baseObj["candidateCardVisible"] = true;
            baseObj["interlocutorCardVisible"] = true;
        }
        else
        {
            // Learner-facing: never expose start URL, password, or interlocutor identity.
            baseObj["joinUrl"] = b.ZoomJoinUrl;
            baseObj["zoomJoinUrl"] = b.ZoomJoinUrl;
            baseObj["candidateCardVisible"] = true;
            baseObj["interlocutorCardVisible"] = false;
        }
        return baseObj;
    }
}
