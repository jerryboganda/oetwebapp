using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class PrivateSpeakingEndpoints
{
    public static IEndpointRouteBuilder MapPrivateSpeakingEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Learner Routes ──────────────────────────────────────────────
        var learner = app.MapGroup("/v1/private-speaking").RequireAuthorization("LearnerOnly");

        learner.MapGet("/config", async (PrivateSpeakingService svc, CancellationToken ct) =>
        {
            var config = await svc.GetConfigAsync(ct);
            return Results.Ok(new
            {
                config.IsEnabled,
                config.DefaultPriceMinorUnits,
                config.Currency,
                config.DefaultSlotDurationMinutes,
                config.MinBookingLeadTimeHours,
                config.MaxBookingAdvanceDays,
                config.CancellationWindowHours,
                config.RescheduleWindowHours,
                config.ReservationTimeoutMinutes
            });
        });

        learner.MapGet("/tutors", async (
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

        learner.MapGet("/tutors/{tutorProfileId}/slots", async (
            string tutorProfileId,
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

            var slots = await svc.GetAvailableSlotsAsync(tutorProfileId, fromDate, toDate, ct);
            return Results.Ok(slots);
        });

        learner.MapGet("/slots", async (
            [FromQuery] string from,
            [FromQuery] string to,
            PrivateSpeakingService svc, CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
                return Results.BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });
            if ((toDate.ToDateTime(default) - fromDate.ToDateTime(default)).TotalDays > 30)
                return Results.BadRequest(new { error = "Date range must not exceed 30 days." });

            var slots = await svc.GetAllAvailableSlotsAsync(fromDate, toDate, ct);
            return Results.Ok(slots);
        });

        learner.MapPost("/bookings", async (
            HttpContext http,
            CreatePrivateSpeakingBookingRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            var result = await svc.CreateBookingAndCheckoutAsync(
                userId, req.TutorProfileId,
                req.SessionStartUtc, req.DurationMinutes,
                req.LearnerTimezone, req.LearnerNotes,
                req.IdempotencyKey, ct);

            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            return Results.Ok(new
            {
                result.BookingId,
                result.CheckoutSessionId,
                result.CheckoutUrl
            });
        });

        learner.MapGet("/bookings", async (
            HttpContext http,
            [FromQuery] string? status,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var bookings = await svc.GetLearnerBookingsAsync(http.UserId(), status, ct);
            return Results.Ok(bookings.Select(MapBookingResponse));
        });

        learner.MapGet("/bookings/{bookingId}", async (
            HttpContext http,
            string bookingId,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var booking = await svc.GetBookingAsync(bookingId, ct);
            if (booking is null || booking.LearnerUserId != http.UserId())
                return Results.NotFound(new { error = "NOT_FOUND" });

            return Results.Ok(MapBookingDetailResponse(booking));
        });

        learner.MapPost("/bookings/{bookingId}/cancel", async (
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

        learner.MapPost("/bookings/{bookingId}/rate", async (
            HttpContext http,
            string bookingId,
            RateSessionRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            if (req.Rating is < 1 or > 5)
                return Results.BadRequest(new { error = "Rating must be 1-5." });

            try
            {
                await svc.RateSessionAsync(bookingId, http.UserId(), req.Rating, req.Feedback, ct);
                return Results.Ok(new { rated = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── Expert Routes ───────────────────────────────────────────────
        var expert = app.MapGroup("/v1/expert/private-speaking").RequireAuthorization("ExpertOnly");

        expert.MapGet("/profile", async (
            HttpContext http,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null) return Results.NotFound(new { error = "NO_PROFILE" });
            return Results.Ok(new
            {
                profile.Id, profile.DisplayName, profile.Bio, profile.Timezone,
                profile.PriceOverrideMinorUnits, profile.SlotDurationOverrideMinutes,
                profile.SpecialtiesJson, profile.IsActive,
                profile.AverageRating, profile.TotalSessions
            });
        });

        expert.MapGet("/sessions", async (
            HttpContext http,
            [FromQuery] string? status,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var bookings = await svc.GetExpertBookingsAsync(http.UserId(), status, ct);
            return Results.Ok(bookings.Select(MapBookingResponse));
        });

        expert.MapGet("/sessions/{bookingId}", async (
            HttpContext http,
            string bookingId,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var booking = await svc.GetBookingAsync(bookingId, ct);
            if (booking is null) return Results.NotFound(new { error = "NOT_FOUND" });

            // Verify this expert owns the linked profile
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null || booking.TutorProfileId != profile.Id)
                return Results.NotFound(new { error = "NOT_FOUND" });

            return Results.Ok(MapBookingDetailResponse(booking));
        });

        expert.MapGet("/availability", async (
            HttpContext http,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null) return Results.NotFound(new { error = "NO_PROFILE" });

            var rules = await svc.GetAvailabilityRulesAsync(profile.Id, ct);
            return Results.Ok(rules.Select(r => new
            {
                r.Id, r.DayOfWeek, r.StartTime, r.EndTime,
                r.EffectiveFrom, r.EffectiveTo, r.IsActive
            }));
        });

        expert.MapPost("/availability", async (
            HttpContext http,
            CreateAvailabilityRuleRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null) return Results.NotFound(new { error = "NO_PROFILE" });

            var rule = await svc.CreateAvailabilityRuleAsync(profile.Id, req.DayOfWeek,
                req.StartTime, req.EndTime,
                req.EffectiveFrom, req.EffectiveTo, http.UserId(), ct);
            return Results.Created($"/v1/expert/private-speaking/availability/{rule.Id}", new
            {
                rule.Id, rule.DayOfWeek, rule.StartTime, rule.EndTime,
                rule.EffectiveFrom, rule.EffectiveTo, rule.IsActive
            });
        });

        expert.MapDelete("/availability/{ruleId}", async (
            HttpContext http,
            string ruleId,
            PrivateSpeakingService svc,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileByExpertIdAsync(http.UserId(), ct);
            if (profile is null) return Results.NotFound(new { error = "NO_PROFILE" });

            var rule = await db.PrivateSpeakingAvailabilityRules
                .FirstOrDefaultAsync(r => r.Id == ruleId && r.TutorProfileId == profile.Id, ct);
            if (rule is null) return Results.NotFound(new { error = "NOT_FOUND" });

            db.PrivateSpeakingAvailabilityRules.Remove(rule);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true });
        });

        // ── Admin Routes ────────────────────────────────────────────────
        var admin = app.MapGroup("/v1/admin/private-speaking").RequireAuthorization("AdminOnly");

        // Config management
        admin.MapGet("/config", async (PrivateSpeakingService svc, CancellationToken ct) =>
        {
            var config = await svc.GetConfigAsync(ct);
            return Results.Ok(config);
        });

        admin.MapPut("/config", async (
            HttpContext http,
            UpdatePrivateSpeakingConfigRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var config = await svc.UpdateConfigAsync(c =>
            {
                if (req.IsEnabled.HasValue) c.IsEnabled = req.IsEnabled.Value;
                if (req.DefaultPriceMinorUnits.HasValue) c.DefaultPriceMinorUnits = req.DefaultPriceMinorUnits.Value;
                if (req.Currency is not null) c.Currency = req.Currency;
                if (req.DefaultSlotDurationMinutes.HasValue) c.DefaultSlotDurationMinutes = req.DefaultSlotDurationMinutes.Value;
                if (req.BufferMinutesBetweenSlots.HasValue) c.BufferMinutesBetweenSlots = req.BufferMinutesBetweenSlots.Value;
                if (req.MinBookingLeadTimeHours.HasValue) c.MinBookingLeadTimeHours = req.MinBookingLeadTimeHours.Value;
                if (req.MaxBookingAdvanceDays.HasValue) c.MaxBookingAdvanceDays = req.MaxBookingAdvanceDays.Value;
                if (req.CancellationWindowHours.HasValue) c.CancellationWindowHours = req.CancellationWindowHours.Value;
                if (req.RescheduleWindowHours.HasValue) c.RescheduleWindowHours = req.RescheduleWindowHours.Value;
                if (req.ReservationTimeoutMinutes.HasValue) c.ReservationTimeoutMinutes = req.ReservationTimeoutMinutes.Value;
                if (req.ReminderOffsetsHoursJson is not null) c.ReminderOffsetsHoursJson = req.ReminderOffsetsHoursJson;
            }, http.UserId(), ct);
            return Results.Ok(config);
        });

        // Dashboard stats
        admin.MapGet("/stats", async (PrivateSpeakingService svc, CancellationToken ct) =>
        {
            var stats = await svc.GetDashboardStatsAsync(ct);
            return Results.Ok(stats);
        });

        // Tutor profile management
        admin.MapGet("/tutors", async (
            [FromQuery] bool? activeOnly,
            PrivateSpeakingService svc, CancellationToken ct) =>
        {
            var profiles = await svc.ListTutorProfilesAsync(activeOnly, ct);
            return Results.Ok(profiles);
        });

        admin.MapGet("/tutors/{profileId}", async (
            string profileId, PrivateSpeakingService svc, CancellationToken ct) =>
        {
            var profile = await svc.GetTutorProfileAsync(profileId, ct);
            return profile is null
                ? Results.NotFound(new { error = "NOT_FOUND" })
                : Results.Ok(profile);
        });

        admin.MapPost("/tutors", async (
            HttpContext http,
            CreateTutorProfileRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            try
            {
                var profile = await svc.CreateTutorProfileAsync(
                    req.ExpertUserId, req.DisplayName, req.Timezone, req.Bio,
                    req.PriceOverrideMinorUnits, req.SlotDurationOverrideMinutes,
                    req.SpecialtiesJson ?? "[]", http.UserId(), ct);
                return Results.Ok(profile);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        admin.MapPut("/tutors/{profileId}", async (
            HttpContext http,
            string profileId,
            UpdateTutorProfileRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            try
            {
                var profile = await svc.UpdateTutorProfileAsync(profileId, p =>
                {
                    if (req.DisplayName is not null) p.DisplayName = req.DisplayName;
                    if (req.Bio is not null) p.Bio = req.Bio;
                    if (req.Timezone is not null) p.Timezone = req.Timezone;
                    if (req.PriceOverrideMinorUnits.HasValue) p.PriceOverrideMinorUnits = req.PriceOverrideMinorUnits;
                    if (req.SlotDurationOverrideMinutes.HasValue) p.SlotDurationOverrideMinutes = req.SlotDurationOverrideMinutes;
                    if (req.SpecialtiesJson is not null) p.SpecialtiesJson = req.SpecialtiesJson;
                    if (req.IsActive.HasValue) p.IsActive = req.IsActive.Value;
                }, http.UserId(), ct);
                return Results.Ok(profile);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Availability rule management
        admin.MapGet("/tutors/{profileId}/availability", async (
            string profileId, PrivateSpeakingService svc, CancellationToken ct) =>
        {
            var rules = await svc.GetAvailabilityRulesAsync(profileId, ct);
            return Results.Ok(rules);
        });

        admin.MapPost("/tutors/{profileId}/availability", async (
            HttpContext http,
            string profileId,
            CreateAvailabilityRuleRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var rule = await svc.CreateAvailabilityRuleAsync(
                profileId, req.DayOfWeek, req.StartTime, req.EndTime,
                req.EffectiveFrom, req.EffectiveTo, http.UserId(), ct);
            return Results.Ok(rule);
        });

        admin.MapDelete("/tutors/{profileId}/availability/{ruleId}", async (
            HttpContext http,
            string profileId, string ruleId,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
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

        // Overrides management
        admin.MapGet("/tutors/{profileId}/overrides", async (
            string profileId,
            [FromQuery] string? from,
            [FromQuery] string? to,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            DateOnly? fromDate = from is not null ? DateOnly.Parse(from) : null;
            DateOnly? toDate = to is not null ? DateOnly.Parse(to) : null;
            var overrides = await svc.GetOverridesAsync(profileId, fromDate, toDate, ct);
            return Results.Ok(overrides);
        });

        admin.MapPost("/tutors/{profileId}/overrides", async (
            HttpContext http,
            string profileId,
            CreateOverrideRequest req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var over = await svc.CreateOverrideAsync(
                profileId, req.Date, req.OverrideType,
                req.StartTime, req.EndTime, req.Reason,
                http.UserId(), ct);
            return Results.Ok(over);
        });

        admin.MapDelete("/tutors/{profileId}/overrides/{overrideId}", async (
            HttpContext http,
            string profileId, string overrideId,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            try
            {
                await svc.DeleteOverrideAsync(overrideId, http.UserId(), ct);
                return Results.Ok(new { deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Booking management
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
            DateOnly? fromDate = from is not null ? DateOnly.Parse(from) : null;
            DateOnly? toDate = to is not null ? DateOnly.Parse(to) : null;

            var bookings = await svc.GetAllBookingsAsync(
                tutorProfileId, status, learnerId, fromDate, toDate, page, pageSize, ct);
            var total = await svc.GetBookingCountAsync(tutorProfileId, status, ct);

            return Results.Ok(new
            {
                items = bookings.Select(MapBookingResponse),
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)total / pageSize)
            });
        });

        admin.MapGet("/bookings/{bookingId}", async (
            string bookingId, PrivateSpeakingService svc, CancellationToken ct) =>
        {
            var booking = await svc.GetBookingAsync(bookingId, ct);
            return booking is null
                ? Results.NotFound(new { error = "NOT_FOUND" })
                : Results.Ok(MapBookingDetailResponse(booking));
        });

        admin.MapPost("/bookings/{bookingId}/cancel", async (
            HttpContext http,
            string bookingId,
            CancelBookingRequest? req,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            var (success, error) = await svc.CancelBookingAsync(
                bookingId, http.UserId(), "admin", req?.Reason, ct);
            return success
                ? Results.Ok(new { cancelled = true })
                : Results.BadRequest(new { error });
        });

        admin.MapPost("/bookings/{bookingId}/complete", async (
            HttpContext http,
            string bookingId,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            await svc.MarkSessionCompletedAsync(bookingId, http.UserId(), ct);
            return Results.Ok(new { completed = true });
        });

        admin.MapPost("/bookings/{bookingId}/retry-zoom", async (
            string bookingId, PrivateSpeakingService svc, CancellationToken ct) =>
        {
            try
            {
                await svc.CreateZoomMeetingForBookingAsync(bookingId, ct);
                return Results.Ok(new { retried = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Audit logs
        admin.MapGet("/audit-logs", async (
            [FromQuery] string? bookingId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            PrivateSpeakingService svc,
            CancellationToken ct) =>
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 50;
            var logs = await svc.GetAuditLogsAsync(bookingId, page, pageSize, ct);
            return Results.Ok(logs);
        });

        return app;
    }

    // ── Response Mappers ────────────────────────────────────────────────

    private static object MapBookingResponse(PrivateSpeakingBooking b) => new
    {
        b.Id,
        b.TutorProfileId,
        tutorName = b.TutorProfile?.DisplayName,
        b.Status,
        b.SessionStartUtc,
        b.DurationMinutes,
        b.TutorTimezone,
        b.LearnerTimezone,
        b.PriceMinorUnits,
        b.Currency,
        b.PaymentStatus,
        b.ZoomStatus,
        b.CreatedAt
    };

    private static object MapBookingDetailResponse(PrivateSpeakingBooking b) => new
    {
        b.Id,
        b.LearnerUserId,
        b.TutorProfileId,
        tutorName = b.TutorProfile?.DisplayName,
        b.Status,
        b.SessionStartUtc,
        b.DurationMinutes,
        b.TutorTimezone,
        b.LearnerTimezone,
        b.PriceMinorUnits,
        b.Currency,
        b.PaymentStatus,
        b.PaymentConfirmedAt,
        b.ZoomStatus,
        b.ZoomJoinUrl,
        b.ZoomStartUrl,
        b.ZoomMeetingPassword,
        b.LearnerNotes,
        b.LearnerRating,
        b.LearnerFeedback,
        b.CancelledBy,
        b.CancellationReason,
        b.CancelledAt,
        b.CompletedAt,
        b.CreatedAt
    };
}

// ── Request DTOs ────────────────────────────────────────────────────────

public record CreatePrivateSpeakingBookingRequest(
    string TutorProfileId,
    DateTimeOffset SessionStartUtc,
    int DurationMinutes,
    string LearnerTimezone,
    string? LearnerNotes,
    string IdempotencyKey);

public record CancelBookingRequest(string? Reason);

public record UpdatePrivateSpeakingConfigRequest(
    bool? IsEnabled,
    int? DefaultPriceMinorUnits,
    string? Currency,
    int? DefaultSlotDurationMinutes,
    int? BufferMinutesBetweenSlots,
    int? MinBookingLeadTimeHours,
    int? MaxBookingAdvanceDays,
    int? CancellationWindowHours,
    int? RescheduleWindowHours,
    int? ReservationTimeoutMinutes,
    string? ReminderOffsetsHoursJson);

public record CreateTutorProfileRequest(
    string ExpertUserId,
    string DisplayName,
    string Timezone,
    string? Bio,
    int? PriceOverrideMinorUnits,
    int? SlotDurationOverrideMinutes,
    string? SpecialtiesJson);

public record UpdateTutorProfileRequest(
    string? DisplayName,
    string? Bio,
    string? Timezone,
    int? PriceOverrideMinorUnits,
    int? SlotDurationOverrideMinutes,
    string? SpecialtiesJson,
    bool? IsActive);

public record CreateAvailabilityRuleRequest(
    int DayOfWeek,
    string StartTime,
    string EndTime,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo);

public record CreateOverrideRequest(
    DateOnly Date,
    PrivateSpeakingOverrideType OverrideType,
    string? StartTime,
    string? EndTime,
    string? Reason);

file static class PrivateSpeakingHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
