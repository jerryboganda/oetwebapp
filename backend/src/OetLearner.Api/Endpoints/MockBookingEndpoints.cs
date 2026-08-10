using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Mocks;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Mocks Module Phase 5 — learner-facing mock booking endpoints under
/// <c>/v1/mocks</c>. Provides slot availability lookup plus create / reschedule /
/// cancel operations against the canonical <see cref="MockBooking"/> entity.
///
/// The existing <c>/v1/mock-bookings</c> learner endpoints (registered by
/// <c>LearnerEndpoints</c>) are intentionally left untouched — this group adds
/// the Phase 5 calendar-style flow with 30-minute slot semantics, a 14-day
/// availability window, idempotent creates, and stricter reschedule / cancel
/// guardrails (24h reschedule cut-off, 6h cancellation cut-off, max 2
/// reschedules per booking).
///
/// The reminder worker (<c>MockBookingReminderWorker</c>) automatically picks
/// up newly-created bookings — no manual trigger is needed here.
/// </summary>
public static class MockBookingEndpoints
{
    /// <summary>Fallback duration for bundles without a configured duration.</summary>
    private const int SlotMinutes = 30;

    /// <summary>Rolling availability window from the supplied <c>date</c>.</summary>
    private const int AvailabilityWindowDays = 14;

    public static IEndpointRouteBuilder MapMockBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/mocks")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Learner Mock Bookings");

        group.MapGet("/bookings", async (
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = UserId(http);
            var bookings = await db.MockBookings.AsNoTracking()
                .Include(booking => booking.MockBundle)
                .Where(booking => booking.UserId == userId)
                .OrderByDescending(booking => booking.ScheduledStartAt)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items = bookings.Select(booking => ProjectBooking(booking, booking.MockBundle)).ToArray(),
                now = DateTimeOffset.UtcNow,
            });
        });

        group.MapGet("/availability", async (
            HttpContext http,
            string? date,
            string? timezone,
            string? bundleId,
            LearnerDbContext db,
            PrivateSpeakingService speakingService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParse(date, out var startDate))
            {
                throw ApiException.Validation("invalid_date", "Query parameter 'date' is required in YYYY-MM-DD form.");
            }

            var tzId = string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone!.Trim();
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            }
            catch (TimeZoneNotFoundException)
            {
                throw ApiException.Validation("invalid_timezone", $"Unknown timezone '{tzId}'.");
            }
            catch (InvalidTimeZoneException)
            {
                throw ApiException.Validation("invalid_timezone", $"Unknown timezone '{tzId}'.");
            }

            // `bundleId` sizes the slot: a mock that runs for N minutes needs N
            // minutes clear, not just the 30-minute grid cell it starts in. An
            // unknown id is rejected rather than silently ignored.
            MockBundle? requestedBundle = null;
            if (!string.IsNullOrWhiteSpace(bundleId))
            {
                requestedBundle = await db.MockBundles.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == bundleId, ct)
                    ?? throw ApiException.NotFound("bundle_not_found", "Mock bundle not found.");
            }

            var requestedMinutes = requestedBundle is { EstimatedDurationMinutes: > 0 }
                ? requestedBundle.EstimatedDurationMinutes
                : SlotMinutes;

            var isSpeakingBundle = requestedBundle is not null
                && (string.Equals(requestedBundle.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
                    || await db.MockBundleSections.AsNoTracking().AnyAsync(section =>
                        section.MockBundleId == requestedBundle.Id
                        && section.SubtestCode == "speaking", ct));
            var enforcedTargetExamDate = await db.Goals.AsNoTracking()
                .Where(goal => goal.UserId == UserId(http))
                .Select(goal => (DateOnly?)goal.TargetExamDate)
                .SingleOrDefaultAsync(ct);
            var speakingTutorClosed = isSpeakingBundle
                && SpeakingBookingPolicy.TutorWindowClosed(
                    enforcedTargetExamDate,
                    DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

            // Full Mock Speaking availability is a projection of the canonical
            // private-speaking tutor calendar. No synthetic fallback slots are
            // returned.
            var canonicalCalendarSlots = await speakingService.GetAllAvailableSlotsAsync(
                startDate, startDate.AddDays(AvailabilityWindowDays - 1), ct);
            var canonicalWindowStart = canonicalCalendarSlots.Count == 0
                ? DateTimeOffset.UtcNow.AddDays(-1)
                : canonicalCalendarSlots.Min(slot => slot.StartTimeUtc).AddMinutes(-requestedMinutes);
            var canonicalWindowEnd = canonicalCalendarSlots.Count == 0
                ? DateTimeOffset.UtcNow.AddDays(AvailabilityWindowDays + 1)
                : canonicalCalendarSlots.Max(slot => slot.EndTimeUtc).AddMinutes(requestedMinutes);
            var longestCanonicalMinutes = await db.MockBundles.AsNoTracking()
                .Select(bundle => (int?)bundle.EstimatedDurationMinutes)
                .MaxAsync(ct) ?? SlotMinutes;
            var canonicalCollisionLookback = TimeSpan.FromMinutes(
                Math.Max(longestCanonicalMinutes, SlotMinutes));
            var canonicalBusy = await db.MockBookings.AsNoTracking()
                .Where(booking => booking.Status != MockBookingStatuses.Cancelled
                    && booking.ScheduledStartAt < canonicalWindowEnd
                    && booking.ScheduledStartAt >= canonicalWindowStart - canonicalCollisionLookback)
                .Select(booking => new { booking.ScheduledStartAt, booking.MockBundleId })
                .ToListAsync(ct);
            var canonicalBundleIds = canonicalBusy.Select(item => item.MockBundleId).Distinct().ToArray();
            var canonicalDurations = await db.MockBundles.AsNoTracking()
                .Where(bundle => canonicalBundleIds.Contains(bundle.Id))
                .ToDictionaryAsync(
                    bundle => bundle.Id,
                    bundle => bundle.EstimatedDurationMinutes > 0 ? bundle.EstimatedDurationMinutes : SlotMinutes,
                    ct);
            var canonicalSlots = canonicalCalendarSlots
                .Where(slot => slot.DurationMinutes >= requestedMinutes)
                .Select(slot =>
                {
                    var endAt = slot.StartTimeUtc.AddMinutes(requestedMinutes);
                    var taken = canonicalBusy.Any(existing =>
                        existing.ScheduledStartAt < endAt
                        && existing.ScheduledStartAt.AddMinutes(canonicalDurations.GetValueOrDefault(existing.MockBundleId, requestedMinutes)) > slot.StartTimeUtc);
                    return new
                    {
                        tutorProfileId = slot.TutorProfileId,
                        tutorDisplayName = slot.TutorDisplayName,
                        tutorTimezone = slot.TutorTimezone,
                        startAt = slot.StartTimeUtc,
                        endAt,
                        isAvailable = !taken,
                        blockedReason = taken ? "slot_taken" : null,
                    };
                })
                .ToArray();

            return Results.Ok(new
            {
                date = startDate.ToString("yyyy-MM-dd"),
                timezone = tzId,
                bundleId,
                requiresAiOnly = speakingTutorClosed,
                slots = speakingTutorClosed ? canonicalSlots.Take(0).ToArray() : canonicalSlots,
            });
        });

            // Historical fixed-grid implementation retained behind a disabled
            // preprocessor block for source archaeology only.
#if false
            // Compute window bounds in UTC so we can fetch any colliding bookings in one query.
            var windowStartLocal = new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var windowStartUtc = TimeZoneInfo.ConvertTimeToUtc(windowStartLocal, tz);
            var windowEndUtc = windowStartUtc.AddDays(AvailabilityWindowDays);

            // A booking that starts before the window can still run into it, so
            // look back by the longest mock we could collide with.
            var longestBundleMinutes = await db.MockBundles.AsNoTracking()
                .Select(b => (int?)b.EstimatedDurationMinutes)
                .MaxAsync(ct) ?? 0;
            var lookback = TimeSpan.FromMinutes(Math.Max(longestBundleMinutes, SlotMinutes));

            var busy = await (
                from b in db.MockBookings.AsNoTracking()
                join bu in db.MockBundles.AsNoTracking() on b.MockBundleId equals bu.Id into bundleJoin
                from bu in bundleJoin.DefaultIfEmpty()
                where b.Status != MockBookingStatuses.Cancelled
                      && b.ScheduledStartAt >= windowStartUtc - lookback
                      && b.ScheduledStartAt < windowEndUtc
                select new
                {
                    b.ScheduledStartAt,
                    Minutes = bu != null ? bu.EstimatedDurationMinutes : 0,
                }).ToListAsync(ct);

            var busySpans = busy
                .Select(x => (
                    Start: x.ScheduledStartAt,
                    End: x.ScheduledStartAt.AddMinutes(x.Minutes > 0 ? x.Minutes : SlotMinutes)))
                .ToList();

            var slots = new List<object>();
            for (var dayOffset = 0; dayOffset < AvailabilityWindowDays; dayOffset++)
            {
                var dayLocalDate = startDate.AddDays(dayOffset);

                // Latest instant the mock may still be running on this local day.
                var closingLocal = new DateTime(dayLocalDate.Year, dayLocalDate.Month, dayLocalDate.Day, 0, 0, 0, DateTimeKind.Unspecified)
                    .AddHours(WorkingHourEndExclusive);

                for (var hour = WorkingHourStart; hour < WorkingHourEndExclusive; hour++)
                {
                    for (var minute = 0; minute < 60; minute += SlotMinutes)
                    {
                        var localStart = new DateTime(dayLocalDate.Year, dayLocalDate.Month, dayLocalDate.Day, hour, minute, 0, DateTimeKind.Unspecified);
                        DateTime utcStart;
                        try
                        {
                            utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
                        }
                        catch (ArgumentException)
                        {
                            // DST spring-forward: this local time does not exist — skip the slot.
                            continue;
                        }

                        var startAt = new DateTimeOffset(utcStart, TimeSpan.Zero);
                        var endAt = startAt.AddMinutes(requestedMinutes);

                        // Half-open overlap: [start, end) against each busy span.
                        var isTaken = busySpans.Any(s => s.Start < endAt && startAt < s.End);
                        var overrunsDay = localStart.AddMinutes(requestedMinutes) > closingLocal;

                        var blockedReason = isTaken
                            ? "slot_taken"
                            : overrunsDay ? "outside_working_hours" : null;

                        slots.Add(new
                        {
                            startAt,
                            endAt,
                            isAvailable = blockedReason is null,
                            blockedReason,
                        });
                    }
                }
            }

            return Results.Ok(new
            {
                date = startDate.ToString("yyyy-MM-dd"),
                timezone = tzId,
                bundleId,
                slots,
            });
        });

#endif
        group.MapPost("/bookings", async (
            HttpContext http,
            MockBookingCreateBody body,
            LearnerDbContext db,
            PrivateSpeakingService speakingService,
            IAiPackageCreditService aiPackageCreditService,
            IMockEntitlementService mockEntitlementService,
            ZoomMeetingService zoomService,
            CancellationToken ct) =>
        {
            var userId = UserId(http);

            if (string.IsNullOrWhiteSpace(body.BundleId))
            {
                throw ApiException.Validation("invalid_request", "bundleId is required.");
            }
            if (body.ScheduledStartAt is null)
            {
                throw ApiException.Validation("invalid_request", "scheduledStartAt is required.");
            }

            var scheduledStartAt = body.ScheduledStartAt.Value.ToUniversalTime();
            var timezoneIana = string.IsNullOrWhiteSpace(body.Timezone) ? "UTC" : body.Timezone!.Trim();

            var bundle = await db.MockBundles.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == body.BundleId, ct)
                ?? throw ApiException.NotFound("bundle_not_found", "Mock bundle not found.");

            var isSpeakingBundle = string.Equals(bundle.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
                || await db.MockBundleSections.AsNoTracking().AnyAsync(section =>
                    section.MockBundleId == bundle.Id
                    && section.SubtestCode == "speaking", ct);
            if (!isSpeakingBundle)
            {
                throw ApiException.Validation("speaking_bundle_required", "Only Full Mock Speaking bookings use this tutor workflow.");
            }
            if (string.IsNullOrWhiteSpace(body.TutorProfileId))
            {
                throw ApiException.Validation("tutor_required", "Select an available tutor slot before booking.");
            }

            var tutor = await db.PrivateSpeakingTutorProfiles.AsNoTracking()
                .FirstOrDefaultAsync(profile => profile.Id == body.TutorProfileId && profile.IsActive, ct)
                ?? throw ApiException.Conflict("tutor_unavailable", "The selected tutor is no longer available.");
            var enforcedTargetExamDate = await db.Goals.AsNoTracking()
                .Where(goal => goal.UserId == userId)
                .Select(goal => (DateOnly?)goal.TargetExamDate)
                .SingleOrDefaultAsync(ct);
            if (SpeakingBookingPolicy.TutorWindowClosed(
                    enforcedTargetExamDate,
                    DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime))
                )
            {
                throw ApiException.Conflict(
                    "speaking_tutor_window_closed",
                    "A Full Mock Speaking tutor session is available only when the exam is at least 7 days away.");
            }

            var tutorTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tutor.Timezone);
            var tutorLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(scheduledStartAt, tutorTimeZone).DateTime);
            var tutorSlots = await speakingService.GetAvailableSlotsAsync(
                tutor.Id, tutorLocalDate, tutorLocalDate, ct);
            var requestedDuration = bundle.EstimatedDurationMinutes > 0 ? bundle.EstimatedDurationMinutes : SlotMinutes;
            if (!tutorSlots.Any(slot =>
                    slot.StartTimeUtc == scheduledStartAt
                    && slot.DurationMinutes >= requestedDuration))
            {
                throw ApiException.Conflict(
                    "tutor_slot_unavailable",
                    "The selected time is not currently available in the tutor calendar.");
            }

            if (!string.IsNullOrWhiteSpace(body.MockAttemptId))
            {
                var attemptOwned = await db.MockAttempts.AsNoTracking()
                    .AnyAsync(a => a.Id == body.MockAttemptId && a.UserId == userId, ct);
                if (!attemptOwned)
                {
                    throw ApiException.NotFound("mock_attempt_not_found", "Mock attempt not found.");
                }

                // Server-side 7-day AI/tutor rule (2026-07-22). The Speaking
                // Gateway already hides the tutor option client-side; enforce
                // here too so a crafted request can't book a tutor inside the
                // AI-only window. Only gateway-scoped bookings (mockAttemptId
                // present) fall under the rule — standalone bookings don't.
                var targetExamDate = await db.Goals.AsNoTracking()
                    .Where(g => g.UserId == userId)
                    .Select(g => (DateOnly?)g.TargetExamDate)
                    .SingleOrDefaultAsync(ct);
                var daysUntilExam = targetExamDate is null
                    ? (int?)null
                    : targetExamDate.Value.DayNumber - DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).DayNumber;
                if (SpeakingBookingPolicy.TutorWindowClosed(
                        targetExamDate,
                        DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime)))
                {
                    throw ApiException.Conflict(
                        "speaking_tutor_window_closed",
                        "Your exam is less than 7 days away — Speaking in this mock must be completed with AI.");
                }
            }

            // Idempotency guard — same user + same exact slot must not create duplicate rows
            // even under double-submit. Scope/Key form mirrors LearnerService usage.
            var scope = "mock_booking_create";
            var key = $"{userId}:{body.TutorProfileId}:{scheduledStartAt:o}";
            var existingIdem = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, ct);
            if (existingIdem is not null)
            {
                var cached = JsonSupport.Deserialize<Dictionary<string, object?>>(
                    existingIdem.ResponseJson,
                    new Dictionary<string, object?>());
                return Results.Ok(cached);
            }

            // Slot collision check — any non-cancelled booking whose span overlaps
            // this mock's span blocks creation. Matches /availability exactly, so
            // a slot shown as free cannot be rejected here (and vice versa).
            if (!await zoomService.IsEnabledAsync(ct))
            {
                throw ApiException.Conflict(
                    "zoom_unavailable",
                    "Live tutor bookings are temporarily unavailable until the required Zoom integration is configured.");
            }

            var slotTaken = await HasOverlappingBookingAsync(
                db, scheduledStartAt, bundle.EstimatedDurationMinutes, excludeBookingId: null, ct);
            if (slotTaken)
            {
                throw ApiException.Conflict("slot_taken", "That slot is no longer available.");
            }

            var now = DateTimeOffset.UtcNow;
            var bookingId = $"mb-{Guid.NewGuid():N}";
            var entitlementReferenceId = string.IsNullOrWhiteSpace(body.MockAttemptId)
                ? bookingId
                : body.MockAttemptId!;
            var entitlementSource = string.IsNullOrWhiteSpace(body.MockAttemptId)
                ? "none"
                : "attempt_prepaid";

            // A standalone Full Mock booking reserves one mock entitlement. A
            // booking reached from an existing mock attempt is already paid for
            // by that attempt and must not be double-debited.
            if (string.IsNullOrWhiteSpace(body.MockAttemptId))
            {
                var packageDebit = await aiPackageCreditService.DeductMockAsync(
                    userId, entitlementReferenceId, ct);
                if (!packageDebit.Debited)
                {
                    throw ApiException.PaymentRequired(
                        packageDebit.ErrorCode ?? "no_mock_exams",
                        packageDebit.ErrorMessage ?? "You have no mock exams remaining. Purchase a package to continue.");
                }

                if (packageDebit.Bypassed)
                {
                    var creditDebit = await mockEntitlementService.DebitAsync(
                        userId, bundle.MockType, entitlementReferenceId, ct);
                    if (!creditDebit.Success)
                    {
                        throw ApiException.PaymentRequired(creditDebit.Reason, creditDebit.Message);
                    }

                    entitlementSource = creditDebit.LedgerEntryId is null
                        ? "subscription"
                        : "mock_credit";
                }
                else
                {
                    entitlementSource = "ai_package";
                }
            }

            var booking = new MockBooking
            {
                Id = bookingId,
                UserId = userId,
                MockBundleId = bundle.Id,
                // Set when booking was reached from an in-progress mock attempt's
                // Speaking Gateway (2026-07-22 7-day AI/tutor rule) — lets
                // RequireProductiveSectionEvidenceAsync's hasBooking check find
                // this booking for that specific attempt. Null for a standalone
                // ahead-of-time booking made outside any active mock.
                MockAttemptId = string.IsNullOrWhiteSpace(body.MockAttemptId) ? null : body.MockAttemptId,
                MockSectionId = string.IsNullOrWhiteSpace(body.MockSectionId) ? null : body.MockSectionId,
                TutorProfileId = body.TutorProfileId,
                AssignedTutorId = tutor.ExpertUserId,
                EntitlementReferenceId = entitlementReferenceId,
                EntitlementSource = entitlementSource,
                ScheduledStartAt = scheduledStartAt,
                TimezoneIana = timezoneIana,
                Status = MockBookingStatuses.Scheduled,
                ConsentToRecording = body.ConsentToRecording ?? false,
                DeliveryMode = MockDeliveryModes.Computer,
                LiveRoomState = MockLiveRoomStates.Waiting,
                ZoomStatus = MockBookingZoomStatuses.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.MockBookings.Add(booking);
            // Real Zoom meeting is provisioned out-of-band; commits atomically
            // with the booking row.
            MockBookingZoomProvisioner.QueueZoomCreateJob(db, booking.Id);

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = userId,
                ActorName = userId,
                Action = "mock_booking_created",
                ResourceType = "MockBooking",
                ResourceId = booking.Id,
                Details = JsonSupport.Serialize(new
                {
                    bundleId = bundle.Id,
                    scheduledStartAt = booking.ScheduledStartAt,
                    timezoneIana = booking.TimezoneIana,
                    consentToRecording = booking.ConsentToRecording,
                    mockAttemptId = booking.MockAttemptId,
                    mockSectionId = booking.MockSectionId,
                }),
            });

            var projection = ProjectBooking(booking, bundle);

            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = $"idem-{Guid.NewGuid():N}",
                Scope = scope,
                Key = key,
                ResponseJson = JsonSupport.Serialize(projection),
                CreatedAt = now,
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Lost the race on the idempotency key — return the previously stored response.
                db.ChangeTracker.Clear();
                var raced = await db.IdempotencyRecords.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, ct);
                if (raced is not null)
                {
                    var cached = JsonSupport.Deserialize<Dictionary<string, object?>>(
                        raced.ResponseJson,
                        new Dictionary<string, object?>());
                    return Results.Ok(cached);
                }
                throw;
            }

            return Results.Created($"/v1/mocks/bookings/{booking.Id}", projection);
        });

        group.MapPatch("/bookings/{bookingId}/reschedule", async (
            HttpContext http,
            string bookingId,
            MockBookingRescheduleBody body,
            LearnerDbContext db,
            PrivateSpeakingService speakingService,
            CancellationToken ct) =>
        {
            var userId = UserId(http);

            if (body.ScheduledStartAt is null)
            {
                throw ApiException.Validation("invalid_request", "scheduledStartAt is required.");
            }

            var booking = await db.MockBookings
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
            if (booking is null || booking.UserId != userId)
            {
                throw ApiException.NotFound("mock_booking_not_found", "Mock booking not found.");
            }
            if (booking.Status == MockBookingStatuses.Cancelled || booking.Status == MockBookingStatuses.Completed)
            {
                throw ApiException.Conflict("booking_finalized", "This booking can no longer be rescheduled.");
            }

            var now = DateTimeOffset.UtcNow;
            var newStart = body.ScheduledStartAt.Value.ToUniversalTime();
            if (newStart <= now)
            {
                throw ApiException.Validation("invalid_request", "scheduledStartAt must be in the future.");
            }

            // Slot collision check on the new target, span-aware and excluding
            // the booking being moved.
            var bookingMinutes = await db.MockBundles.AsNoTracking()
                .Where(b => b.Id == booking.MockBundleId)
                .Select(b => (int?)b.EstimatedDurationMinutes)
                .FirstOrDefaultAsync(ct) ?? 0;
            if (string.IsNullOrWhiteSpace(booking.TutorProfileId))
            {
                throw ApiException.Conflict("tutor_unavailable", "This booking has no canonical tutor calendar assignment.");
            }
            var tutor = await db.PrivateSpeakingTutorProfiles.AsNoTracking()
                .FirstOrDefaultAsync(profile => profile.Id == booking.TutorProfileId && profile.IsActive, ct)
                ?? throw ApiException.Conflict("tutor_unavailable", "The assigned tutor is no longer available.");
            var tutorTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tutor.Timezone);
            var tutorLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(newStart, tutorTimeZone).DateTime);
            var tutorSlots = await speakingService.GetAvailableSlotsAsync(
                tutor.Id, tutorLocalDate, tutorLocalDate, ct);
            var requestedMinutes = bookingMinutes > 0 ? bookingMinutes : SlotMinutes;
            if (!tutorSlots.Any(slot => slot.StartTimeUtc == newStart && slot.DurationMinutes >= requestedMinutes))
            {
                throw ApiException.Conflict(
                    "tutor_slot_unavailable",
                    "Rescheduling is available only to a slot currently open in the tutor calendar.");
            }
            var slotTaken = await HasOverlappingBookingAsync(
                db, newStart, bookingMinutes, booking.Id, ct);
            if (slotTaken)
            {
                throw ApiException.Conflict("slot_taken", "That slot is no longer available.");
            }

            var before = new
            {
                scheduledStartAt = booking.ScheduledStartAt,
                rescheduleCount = booking.RescheduleCount,
                status = booking.Status,
            };

            booking.ScheduledStartAt = newStart;
            booking.RescheduleCount++;
            booking.UpdatedAt = now;

            // The Zoom meeting carries the old start time — re-provision (the
            // job deletes the stale meeting before creating the new one).
            if (booking.ZoomStatus is not null)
            {
                booking.ZoomStatus = MockBookingZoomStatuses.Pending;
                MockBookingZoomProvisioner.QueueZoomCreateJob(db, booking.Id);
            }

            var after = new
            {
                scheduledStartAt = booking.ScheduledStartAt,
                rescheduleCount = booking.RescheduleCount,
                status = booking.Status,
            };

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = userId,
                ActorName = userId,
                Action = "mock_booking_rescheduled",
                ResourceType = "MockBooking",
                ResourceId = booking.Id,
                Details = JsonSupport.Serialize(new
                {
                    beforeJson = before,
                    afterJson = after,
                }),
            });

            await db.SaveChangesAsync(ct);

            return Results.Ok(ProjectBooking(booking, bundle: null));
        });

        group.MapDelete("/bookings/{bookingId}", async (
            HttpContext http,
            string bookingId,
            LearnerDbContext db,
            MockBookingZoomProvisioner zoomProvisioner,
            IAiPackageCreditService aiPackageCreditService,
            IMockEntitlementService mockEntitlementService,
            CancellationToken ct) =>
        {
            var userId = UserId(http);

            var booking = await db.MockBookings
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
            if (booking is null || booking.UserId != userId)
            {
                throw ApiException.NotFound("mock_booking_not_found", "Mock booking not found.");
            }

            // Idempotent: cancelling an already-cancelled booking is a no-op.
            if (booking.Status == MockBookingStatuses.Cancelled)
            {
                return Results.NoContent();
            }
            if (booking.Status == MockBookingStatuses.Completed)
            {
                throw ApiException.Conflict("booking_finalized", "Completed bookings cannot be cancelled.");
            }

            var now = DateTimeOffset.UtcNow;
            if (booking.ScheduledStartAt <= now)
            {
                throw ApiException.Conflict("booking_started", "Bookings cannot be cancelled after the session has started.");
            }
            var fullRefundEligible = SpeakingBookingPolicy.FullRefundEligible(booking.ScheduledStartAt, now);
            booking.RefundDecision = fullRefundEligible
                ? "full_refund_eligible"
                : "full_refund_unavailable";
            booking.RefundIssued = false;

            if (fullRefundEligible)
            {
                var entitlementReferenceId = booking.EntitlementReferenceId;
                var noChargeToRestore = string.IsNullOrWhiteSpace(booking.EntitlementSource)
                    || string.Equals(booking.EntitlementSource, "none", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(booking.EntitlementSource, "subscription", StringComparison.OrdinalIgnoreCase);
                var aiRefunded = false;
                var creditRefunded = false;
                if (!string.IsNullOrWhiteSpace(entitlementReferenceId))
                {
                    aiRefunded = await aiPackageCreditService.RefundAsync(
                        userId,
                        entitlementReferenceId,
                        $"mock-booking-refund:{booking.Id}",
                        "Full Mock booking cancelled more than 24 hours before start",
                        ct);

                    var bundleMockType = await db.MockBundles.AsNoTracking()
                        .Where(bundle => bundle.Id == booking.MockBundleId)
                        .Select(bundle => bundle.MockType)
                        .FirstOrDefaultAsync(ct) ?? "full";
                    creditRefunded = await mockEntitlementService.RefundAsync(
                        userId,
                        bundleMockType,
                        entitlementReferenceId,
                        $"mock-booking-refund:{booking.Id}",
                        ct);
                }

                if (!aiRefunded && !creditRefunded && !noChargeToRestore)
                {
                    throw ApiException.Conflict(
                        "refund_unavailable",
                        "The booking entitlement could not be located, so the booking was not cancelled. Please retry while billing is available.");
                }

                booking.RefundIssued = true;
                booking.RefundDecision = aiRefunded || creditRefunded
                    ? "full_refund_issued"
                    : "full_refund_not_charged";
            }

            var before = new
            {
                status = booking.Status,
                cancelledAt = booking.CancelledAt,
            };

            booking.Status = MockBookingStatuses.Cancelled;
            booking.CancelledAt = now;
            booking.UpdatedAt = now;

            // Best-effort: a Zoom outage must never block a cancellation.
            await zoomProvisioner.DeleteZoomMeetingBestEffortAsync(booking, ct);

            var after = new
            {
                status = booking.Status,
                cancelledAt = booking.CancelledAt,
            };

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = userId,
                ActorName = userId,
                Action = "mock_booking_cancelled",
                ResourceType = "MockBooking",
                ResourceId = booking.Id,
                Details = JsonSupport.Serialize(new
                {
                    beforeJson = before,
                    afterJson = after,
                }),
            });

            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        return app;
    }

    private static string UserId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    /// <summary>
    /// True when a mock of <paramref name="minutes"/> starting at
    /// <paramref name="startAt"/> would overlap a live booking.
    /// <para>
    /// A booking occupies its whole span — start plus its bundle's estimated
    /// duration — not just the instant it starts, so a three-hour mock reserves
    /// three hours rather than its first half-hour. Bundles with no recorded
    /// duration fall back to the <see cref="SlotMinutes"/> grid cell.
    /// </para>
    /// </summary>
    /// <param name="excludeBookingId">Booking to ignore (the row being moved).</param>
    private static async Task<bool> HasOverlappingBookingAsync(
        LearnerDbContext db,
        DateTimeOffset startAt,
        int minutes,
        string? excludeBookingId,
        CancellationToken ct)
    {
        var endAt = startAt.AddMinutes(minutes > 0 ? minutes : SlotMinutes);

        // A booking starting before us can still run into our span, so look back
        // by the longest mock on record. Anything starting at/after our end
        // cannot overlap, which bounds the upper side exactly.
        var longestBundleMinutes = await db.MockBundles.AsNoTracking()
            .Select(b => (int?)b.EstimatedDurationMinutes)
            .MaxAsync(ct) ?? 0;
        var lookback = TimeSpan.FromMinutes(Math.Max(longestBundleMinutes, SlotMinutes));

        var candidates = await (
            from b in db.MockBookings.AsNoTracking()
            join bu in db.MockBundles.AsNoTracking() on b.MockBundleId equals bu.Id into bundleJoin
            from bu in bundleJoin.DefaultIfEmpty()
            where b.Status != MockBookingStatuses.Cancelled
                  && (excludeBookingId == null || b.Id != excludeBookingId)
                  && b.ScheduledStartAt >= startAt - lookback
                  && b.ScheduledStartAt < endAt
            select new
            {
                b.ScheduledStartAt,
                Minutes = bu != null ? bu.EstimatedDurationMinutes : 0,
            }).ToListAsync(ct);

        return candidates.Any(c =>
            startAt < c.ScheduledStartAt.AddMinutes(c.Minutes > 0 ? c.Minutes : SlotMinutes));
    }

    /// <summary>
    /// Learner-facing projection. Mirrors the shape used by
    /// <c>MockBookingService.Project</c> with <c>isAdmin = false</c>; tutor /
    /// interlocutor identity and Zoom start URL are deliberately excluded.
    /// <c>joinUrl</c> is always the in-app speaking-room route; the real Zoom
    /// join link is surfaced separately once provisioning succeeded.
    /// </summary>
    private static Dictionary<string, object?> ProjectBooking(MockBooking b, MockBundle? bundle)
    {
        bundle ??= b.MockBundle;
        return new Dictionary<string, object?>
        {
            ["id"] = b.Id,
            ["bookingId"] = b.Id,
            ["mockBundleId"] = b.MockBundleId,
            ["mockBundleTitle"] = bundle?.Title,
            ["mockAttemptId"] = b.MockAttemptId,
            ["mockSectionId"] = b.MockSectionId,
            ["scheduledStartAt"] = b.ScheduledStartAt,
            ["timezoneIana"] = b.TimezoneIana,
            ["status"] = b.Status,
            ["liveRoomState"] = b.LiveRoomState,
            ["deliveryMode"] = b.DeliveryMode,
            ["rescheduleCount"] = b.RescheduleCount,
            ["consentToRecording"] = b.ConsentToRecording,
            ["joinUrl"] = MockBookingPresentation.RoomRoute(b),
            ["zoomJoinUrl"] = MockBookingPresentation.LearnerZoomJoinUrl(b),
            ["createdAt"] = b.CreatedAt,
            ["updatedAt"] = b.UpdatedAt,
            ["refundDecision"] = b.RefundDecision,
            ["refundIssued"] = b.RefundIssued,
            ["cancelledAt"] = b.CancelledAt,
            ["completedAt"] = b.CompletedAt,
        };
    }
}

/// <summary>Request body for <c>POST /v1/mocks/bookings</c>.</summary>
public sealed record MockBookingCreateBody(
    string? BundleId,
    DateTimeOffset? ScheduledStartAt,
    string? Timezone,
    bool? ConsentToRecording,
    string? MockAttemptId = null,
    string? MockSectionId = null,
    string? TutorProfileId = null);

/// <summary>Request body for <c>PATCH /v1/mocks/bookings/{bookingId}/reschedule</c>.</summary>
public sealed record MockBookingRescheduleBody(DateTimeOffset? ScheduledStartAt);
