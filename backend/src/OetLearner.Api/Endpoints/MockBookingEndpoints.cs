using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

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
    /// <summary>Working-hour bounds for available slots, in the booking's local timezone.</summary>
    private const int WorkingHourStart = 8;
    private const int WorkingHourEndExclusive = 22;

    /// <summary>Fixed 30-minute slot grid.</summary>
    private const int SlotMinutes = 30;

    /// <summary>Rolling availability window from the supplied <c>date</c>.</summary>
    private const int AvailabilityWindowDays = 14;

    /// <summary>Per-booking reschedule cap (Phase 5).</summary>
    private const int MaxReschedulesPerBooking = 2;

    /// <summary>Cut-off below which reschedules are refused.</summary>
    private static readonly TimeSpan RescheduleCutoff = TimeSpan.FromHours(24);

    /// <summary>Cut-off below which cancellations are refused.</summary>
    private static readonly TimeSpan CancellationCutoff = TimeSpan.FromHours(6);

    public static IEndpointRouteBuilder MapMockBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/mocks")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Learner Mock Bookings");

        group.MapGet("/availability", async (
            HttpContext http,
            string? date,
            string? timezone,
            string? bundleId,
            LearnerDbContext db,
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

        group.MapPost("/bookings", async (
            HttpContext http,
            MockBookingCreateBody body,
            LearnerDbContext db,
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

            // Idempotency guard — same user + same exact slot must not create duplicate rows
            // even under double-submit. Scope/Key form mirrors LearnerService usage.
            var scope = "mock_booking_create";
            var key = $"{userId}:{scheduledStartAt:o}";
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
            var slotTaken = await HasOverlappingBookingAsync(
                db, scheduledStartAt, bundle.EstimatedDurationMinutes, excludeBookingId: null, ct);
            if (slotTaken)
            {
                throw ApiException.Conflict("slot_taken", "That slot is no longer available.");
            }

            var now = DateTimeOffset.UtcNow;
            var booking = new MockBooking
            {
                Id = $"mb-{Guid.NewGuid():N}",
                UserId = userId,
                MockBundleId = bundle.Id,
                // Set when booking was reached from an in-progress mock attempt's
                // Speaking Gateway (2026-07-22 7-day AI/tutor rule) — lets
                // RequireProductiveSectionEvidenceAsync's hasBooking check find
                // this booking for that specific attempt. Null for a standalone
                // ahead-of-time booking made outside any active mock.
                MockAttemptId = string.IsNullOrWhiteSpace(body.MockAttemptId) ? null : body.MockAttemptId,
                ScheduledStartAt = scheduledStartAt,
                TimezoneIana = timezoneIana,
                Status = MockBookingStatuses.Scheduled,
                ConsentToRecording = body.ConsentToRecording ?? false,
                DeliveryMode = MockDeliveryModes.Computer,
                LiveRoomState = MockLiveRoomStates.Waiting,
                CreatedAt = now,
                UpdatedAt = now,
            };
            // Same internal room route the MockService creator stamps — without
            // it, bookings made through this endpoint had no joinUrl and the
            // learner's "join" affordances render nothing.
            booking.ZoomJoinUrl = $"/mocks/speaking-room/{Uri.EscapeDataString(booking.Id)}";
            db.MockBookings.Add(booking);

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
            if (booking.ScheduledStartAt - now < RescheduleCutoff)
            {
                throw ApiException.Conflict("reschedule_window_closed", "Bookings cannot be rescheduled within 24 hours.");
            }
            if (booking.RescheduleCount >= MaxReschedulesPerBooking)
            {
                throw ApiException.Conflict("reschedule_cap_reached", $"Bookings can be rescheduled at most {MaxReschedulesPerBooking} times.");
            }

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
            if (booking.ScheduledStartAt - now < CancellationCutoff)
            {
                throw ApiException.Conflict("cancellation_window_closed", "Bookings cannot be cancelled within 6 hours of the scheduled time.");
            }

            var before = new
            {
                status = booking.Status,
                cancelledAt = booking.CancelledAt,
            };

            booking.Status = MockBookingStatuses.Cancelled;
            booking.CancelledAt = now;
            booking.UpdatedAt = now;

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
            ["scheduledStartAt"] = b.ScheduledStartAt,
            ["timezoneIana"] = b.TimezoneIana,
            ["status"] = b.Status,
            ["liveRoomState"] = b.LiveRoomState,
            ["deliveryMode"] = b.DeliveryMode,
            ["rescheduleCount"] = b.RescheduleCount,
            ["consentToRecording"] = b.ConsentToRecording,
            ["joinUrl"] = b.ZoomJoinUrl,
            ["createdAt"] = b.CreatedAt,
            ["updatedAt"] = b.UpdatedAt,
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
    string? MockSectionId = null);

/// <summary>Request body for <c>PATCH /v1/mocks/bookings/{bookingId}/reschedule</c>.</summary>
public sealed record MockBookingRescheduleBody(DateTimeOffset? ScheduledStartAt);
