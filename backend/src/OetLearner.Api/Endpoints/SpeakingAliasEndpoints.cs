using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// T3b — thin ALIAS layer exposing the PDF's exact public Speaking-booking route
/// surface (/v1/speaking, /v1/me/speaking, /v1/tutor, /v1/admin/speaking) over the
/// existing <see cref="PrivateSpeakingService"/> engine. Booking responses are
/// projected with the PDF <c>SpeakingSessionStatus</c> vocabulary via
/// <see cref="SpeakingSessionStatusMapper"/>. This is purely additive: the existing
/// /v1/private-speaking, /v1/expert/private-speaking and /v1/admin/private-speaking
/// endpoints are untouched and keep working.
/// </summary>
public static class SpeakingAliasEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingAliasEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Tutor (expert) — PDF /v1/tutor ───────────────────────────────
        var tutor = app.MapGroup("/v1/tutor").RequireAuthorization("ExpertOnly");

        tutor.MapPost("/calendar/google/connect", async (
            HttpContext http,
            PrivateSpeakingCalendarService calendarService,
            CancellationToken ct) =>
        {
            var result = await calendarService.BuildGoogleConnectUrlAsync(http.UserId(), ct);
            return Results.Ok(result);
        });

        tutor.MapGet("/calendar/google/status", async (
            HttpContext http,
            PrivateSpeakingCalendarService calendarService,
            CancellationToken ct) =>
        {
            var status = await calendarService.GetStatusAsync(http.UserId(), ct);
            return Results.Ok(status);
        });

        tutor.MapGet("/availability-rules", async (
            HttpContext http,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null) return Results.Ok(Array.Empty<object>());

            var rules = await svc.GetAvailabilityRulesAsync(profile.Id, ct);
            return Results.Ok(rules.Select(r => new
            {
                r.Id, r.DayOfWeek, r.StartTime, r.EndTime,
                r.EffectiveFrom, r.EffectiveTo, r.IsActive
            }));
        });

        tutor.MapPost("/availability-rules", async (
            HttpContext http,
            CreateAvailabilityRuleRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null) return Results.NotFound(new { error = "NO_PROFILE" });

            var rule = await svc.CreateAvailabilityRuleAsync(
                profile.Id, req.DayOfWeek, req.StartTime, req.EndTime,
                req.EffectiveFrom, req.EffectiveTo, http.UserId(), ct);
            return Results.Created($"/v1/tutor/availability-rules/{rule.Id}", new
            {
                rule.Id, rule.DayOfWeek, rule.StartTime, rule.EndTime,
                rule.EffectiveFrom, rule.EffectiveTo, rule.IsActive
            });
        });

        tutor.MapPut("/availability-rules/{ruleId}", async (
            HttpContext http,
            string ruleId,
            UpdateAvailabilityRuleRequest req,
            PrivateSpeakingService svc,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null) return Results.NotFound(new { error = "NO_PROFILE" });

            var owned = await db.PrivateSpeakingAvailabilityRules
                .AnyAsync(r => r.Id == ruleId && r.TutorProfileId == profile.Id, ct);
            if (!owned) return Results.NotFound(new { error = "NOT_FOUND" });

            try
            {
                var rule = await svc.UpdateAvailabilityRuleAsync(
                    ruleId, req.DayOfWeek, req.StartTime, req.EndTime,
                    req.EffectiveFrom, req.EffectiveTo, req.IsActive, http.UserId(), ct);
                return Results.Ok(new
                {
                    rule.Id, rule.DayOfWeek, rule.StartTime, rule.EndTime,
                    rule.EffectiveFrom, rule.EffectiveTo, rule.IsActive
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        tutor.MapDelete("/availability-rules/{ruleId}", async (
            HttpContext http,
            string ruleId,
            PrivateSpeakingService svc,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null) return Results.NotFound(new { error = "NO_PROFILE" });

            var owned = await db.PrivateSpeakingAvailabilityRules
                .AnyAsync(r => r.Id == ruleId && r.TutorProfileId == profile.Id, ct);
            if (!owned) return Results.NotFound(new { error = "NOT_FOUND" });

            try
            {
                await svc.DeleteAvailabilityRuleAsync(ruleId, http.UserId(), ct);
                return Results.Ok(new { deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        tutor.MapGet("/speaking-bookings", async (
            HttpContext http,
            [FromQuery] string? status,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var bookings = await svc.GetExpertBookingsAsync(http.UserId(), status, ct);
            return Results.Ok(bookings.Select(ToPdfResponse));
        });

        // ── Candidate (learner) — PDF /v1/speaking ───────────────────────
        var speaking = app.MapGroup("/v1/speaking").RequireAuthorization("LearnerOnly");

        speaking.MapGet("/tutors", async (
            PrivateSpeakingService svc, CancellationToken ct) =>
        {
            var profiles = await svc.ListTutorProfilesAsync(activeOnly: true, ct);
            return Results.Ok(profiles.Select(p => new
            {
                p.Id, p.DisplayName, p.Bio, p.Timezone,
                p.PriceOverrideMinorUnits, p.SlotDurationOverrideMinutes,
                p.SpecialtiesJson, p.AverageRating, p.TotalSessions
            }));
        });

        speaking.MapGet("/available-slots", async (
            [FromQuery] string? tutorId,
            [FromQuery] string from,
            [FromQuery] string to,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
                return Results.BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });
            if (toDate < fromDate)
                return Results.BadRequest(new { error = "'to' must be >= 'from'." });
            if ((toDate.ToDateTime(default) - fromDate.ToDateTime(default)).TotalDays > 30)
                return Results.BadRequest(new { error = "Date range must not exceed 30 days." });

            var slots = string.IsNullOrWhiteSpace(tutorId)
                ? await svc.GetAllAvailableSlotsAsync(fromDate, toDate, ct)
                : await svc.GetAvailableSlotsAsync(tutorId, fromDate, toDate, ct);
            return Results.Ok(slots);
        });

        speaking.MapPost("/bookings", async (
            HttpContext http,
            CreateSpeakingBookingRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateBookingAndCheckoutAsync(
                http.UserId(), req.TutorProfileId,
                req.SessionStartUtc, req.DurationMinutes,
                req.LearnerTimezone, req.LearnerNotes,
                req.ProfessionTrack, req.IdempotencyKey, req.SessionFormat, ct);

            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            return Results.Ok(new
            {
                result.BookingId,
                result.CheckoutSessionId,
                result.CheckoutUrl,
                result.EntitlementUsed,
                result.SpeakingSessionsRemaining
            });
        });

        // ── Candidate "me" — PDF /v1/me/speaking ─────────────────────────
        var me = app.MapGroup("/v1/me/speaking").RequireAuthorization("LearnerOnly");

        me.MapGet("/bookings", async (
            HttpContext http,
            [FromQuery] string? status,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var bookings = await svc.GetLearnerBookingsAsync(http.UserId(), status, ct);
            return Results.Ok(bookings.Select(ToPdfResponse));
        });

        me.MapPost("/bookings/{bookingId}/cancel", async (
            HttpContext http,
            string bookingId,
            CancelBookingRequest? req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var (success, error) = await svc.CancelBookingAsync(
                bookingId, http.UserId(), "learner", req?.Reason, ct);
            return success
                ? Results.Ok(new { cancelled = true })
                : Results.BadRequest(new { error });
        });

        me.MapPost("/bookings/{bookingId}/reschedule", async (
            HttpContext http,
            string bookingId,
            RescheduleSpeakingRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var result = await svc.RescheduleBookingAsync(
                bookingId, http.UserId(),
                req.SessionStartUtc, req.LearnerTimezone,
                req.LearnerNotes, req.IdempotencyKey, ct);

            return result.Success
                ? Results.Ok(new
                {
                    result.BookingId,
                    result.CheckoutSessionId,
                    result.CheckoutUrl,
                    result.EntitlementUsed,
                    result.SpeakingSessionsRemaining
                })
                : Results.BadRequest(new { error = result.Error });
        });

        me.MapPost("/bookings/{bookingId}/join-token", async (
            HttpContext http,
            string bookingId,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var token = await svc.CreateLearnerJoinTokenAsync(bookingId, http.UserId(), ct);
            return Results.Ok(token);
        });

        // ── Admin — PDF /v1/admin/speaking ───────────────────────────────
        var admin = app.MapGroup("/v1/admin/speaking")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/bookings", async (
            [FromQuery] string? tutorProfileId,
            [FromQuery] string? status,
            [FromQuery] string? learnerId,
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 20;
            DateOnly? fromDate = null;
            DateOnly? toDate = null;
            if (from is not null)
            {
                if (!DateOnly.TryParse(from, out var parsedFrom))
                    return Results.BadRequest(new { error = "Invalid 'from'/'to' date. Use yyyy-MM-dd." });
                fromDate = parsedFrom;
            }
            if (to is not null)
            {
                if (!DateOnly.TryParse(to, out var parsedTo))
                    return Results.BadRequest(new { error = "Invalid 'from'/'to' date. Use yyyy-MM-dd." });
                toDate = parsedTo;
            }

            var bookings = await svc.GetAllBookingsAsync(
                tutorProfileId, status, learnerId, fromDate, toDate, page, pageSize, ct);
            var total = await svc.GetBookingCountAsync(tutorProfileId, status, ct);

            return Results.Ok(new
            {
                items = bookings.Select(ToPdfResponse),
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)total / pageSize)
            });
        }).WithAdminRead("AdminReviewOps");

        admin.MapPut("/bookings/{bookingId}", async (
            HttpContext http,
            string bookingId,
            AdminEditBookingRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var booking = await svc.AdminEditBookingAsync(
                bookingId, http.UserId(),
                req.SessionStartUtc, req.DurationMinutes,
                req.ProfessionTrack, req.TutorNotes, ct);
            return booking is null
                ? Results.NotFound(new { error = "NOT_FOUND" })
                : Results.Ok(ToPdfResponse(booking));
        }).WithAdminWrite("AdminReviewOps");

        admin.MapPost("/bookings/{bookingId}/override-refund", async (
            HttpContext http,
            string bookingId,
            OverrideRefundRequest? req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var (success, error) = await svc.OverrideRefundAsync(
                bookingId, http.UserId(), req?.AmountMinorUnits, req?.Reason, ct);
            return success
                ? Results.Ok(new { refunded = true })
                : Results.BadRequest(new { error });
        }).WithAdminWrite("AdminReviewOps");

        admin.MapPost("/bookings/{bookingId}/manual-reschedule", async (
            HttpContext http,
            string bookingId,
            AdminManualRescheduleRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var (success, error) = await svc.AdminManualRescheduleAsync(
                bookingId, http.UserId(), req.NewSessionStartUtc, req.Reason, ct);
            return success
                ? Results.Ok(new { rescheduled = true })
                : Results.BadRequest(new { error });
        }).WithAdminWrite("AdminReviewOps");

        return app;
    }

    /// <summary>
    /// Projects a <see cref="PrivateSpeakingBooking"/> onto the PDF booking shape,
    /// surfacing the public <c>SpeakingSessionStatus</c> name. Pure function — unit
    /// tested directly (see SpeakingAliasProjectionTests).
    /// </summary>
    public static SpeakingBookingPdfDto ToPdfResponse(PrivateSpeakingBooking b) => new(
        Id: b.Id,
        LearnerUserId: b.LearnerUserId,
        TutorProfileId: b.TutorProfileId,
        TutorName: b.TutorProfile?.DisplayName,
        Status: SpeakingSessionStatusMapper.Map(b),
        ScheduledStartAt: b.SessionStartUtc,
        ScheduledEndAt: b.SessionStartUtc.AddMinutes(b.DurationMinutes),
        DurationMinutes: b.DurationMinutes,
        TimeZone: b.LearnerTimezone,
        TutorTimezone: b.TutorTimezone,
        ProfessionTrack: b.ProfessionTrack,
        PriceMinorUnits: b.PriceMinorUnits,
        Currency: b.Currency,
        PaymentStatus: b.PaymentStatus.ToString(),
        RefundIssued: b.RefundIssued,
        RefundAmountMinorUnits: b.RefundAmountMinorUnits,
        PenaltyAmountMinorUnits: b.PenaltyAmountMinorUnits,
        ZoomStatus: b.ZoomStatus.ToString(),
        GoogleCalendarSyncStatus: b.GoogleCalendarSyncStatus,
        CreatedAt: b.CreatedAt);
}

// ── PDF booking projection DTO ───────────────────────────────────────────

/// <summary>
/// PDF-conformant Speaking booking projection. <c>Status</c> carries the public
/// <c>SpeakingSessionStatus</c> vocabulary (PendingPayment / Confirmed /
/// Rescheduled / CancelledWithRefund / CancelledWithoutRefund / Completed / NoShow).
/// </summary>
public record SpeakingBookingPdfDto(
    string Id,
    string LearnerUserId,
    string TutorProfileId,
    string? TutorName,
    string Status,
    DateTimeOffset ScheduledStartAt,
    DateTimeOffset ScheduledEndAt,
    int DurationMinutes,
    string TimeZone,
    string TutorTimezone,
    string? ProfessionTrack,
    int PriceMinorUnits,
    string Currency,
    string PaymentStatus,
    bool RefundIssued,
    int? RefundAmountMinorUnits,
    int? PenaltyAmountMinorUnits,
    string ZoomStatus,
    string? GoogleCalendarSyncStatus,
    DateTimeOffset CreatedAt);

// ── Request DTOs (only those not already declared in PrivateSpeakingEndpoints) ──

public record CreateSpeakingBookingRequest(
    string TutorProfileId,
    DateTimeOffset SessionStartUtc,
    int DurationMinutes,
    string LearnerTimezone,
    string? ProfessionTrack,
    string? LearnerNotes,
    string IdempotencyKey,
    // Speaking module rebuild (2026-06-11): "practice" (default) or "exam".
    string? SessionFormat = null);

public record RescheduleSpeakingRequest(
    DateTimeOffset SessionStartUtc,
    string LearnerTimezone,
    string? LearnerNotes,
    string IdempotencyKey);

file static class SpeakingAliasHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
