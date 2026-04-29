using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class SocialEndpoints
{
    private const int MaxTutoringDurationMinutes = 240;

    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");

        // ── Certificates ──────────────────────────────────────────────────
        var certs = v1.MapGroup("/certificates");

        certs.MapGet("/", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var list = await db.Certificates.Where(c => c.UserId == http.UserId()).OrderByDescending(c => c.IssuedAt).ToListAsync(ct);
            return Results.Ok(list.Select(c => new { id = c.Id, type = c.Type, title = c.Title, description = c.Description, pdfUrl = c.PdfUrl, verificationCode = c.VerificationCode, issuedAt = c.IssuedAt }));
        });

        certs.MapGet("/verify/{code}", async (string code, LearnerDbContext db, CancellationToken ct) =>
        {
            var cert = await db.Certificates.FirstOrDefaultAsync(c => c.VerificationCode == code, ct);
            if (cert == null) return Results.NotFound(new { valid = false });
            return Results.Ok(new { valid = true, userDisplayName = cert.UserDisplayName, type = cert.Type, title = cert.Title, issuedAt = cert.IssuedAt });
        });

        // ── Referrals ─────────────────────────────────────────────────────
        var referrals = v1.MapGroup("/referrals");

        referrals.MapGet("/my-code", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var code = await db.ReferralCodes.FirstOrDefaultAsync(r => r.UserId == http.UserId(), ct);
            if (code != null) return Results.Ok(MapReferralCode(code));

            // Auto-generate code
            var user = await db.Users.FindAsync([http.UserId()], ct);
            var newCode = GenerateReferralCode(user?.DisplayName ?? http.UserId());
            var rc = new ReferralCode
            {
                Id = $"rc-{Guid.NewGuid():N}",
                UserId = http.UserId(),
                Code = newCode,
                TotalReferrals = 0,
                ConvertedReferrals = 0,
                TotalCreditsEarned = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ReferralCodes.Add(rc);
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapReferralCode(rc));
        });

        referrals.MapGet("/my-referrals", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var refs = await db.Referrals.Where(r => r.ReferrerUserId == http.UserId())
                .OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
            return Results.Ok(refs.Select(r => new { id = r.Id, referredEmail = r.ReferredEmail, status = r.Status, creditAmount = r.CreditAmount, createdAt = r.CreatedAt, convertedAt = r.ConvertedAt }));
        });

        referrals.MapPost("/apply", async (HttpContext http, ApplyReferralRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var code = await db.ReferralCodes.FirstOrDefaultAsync(rc => rc.Code == req.Code, ct);
            if (code == null) return Results.BadRequest(new { error = "INVALID_CODE" });
            if (code.UserId == http.UserId()) return Results.BadRequest(new { error = "OWN_CODE" });

            var existing = await db.Referrals.AnyAsync(r => r.ReferredUserId == http.UserId(), ct);
            if (existing) return Results.BadRequest(new { error = "ALREADY_REFERRED" });

            db.Referrals.Add(new Referral
            {
                Id = $"ref-{Guid.NewGuid():N}",
                ReferrerUserId = code.UserId,
                ReferredUserId = http.UserId(),
                Status = "pending",
                CreditAmount = 10,
                CreatedAt = DateTimeOffset.UtcNow,
                RegisteredAt = DateTimeOffset.UtcNow
            });
            code.TotalReferrals++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { applied = true });
        });

        // ── Exam Booking ──────────────────────────────────────────────────
        var bookings = v1.MapGroup("/exam-bookings");

        bookings.MapGet("/", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var list = await db.ExamBookings.Where(b => b.UserId == http.UserId()).OrderByDescending(b => b.ExamDate).ToListAsync(ct);
            return Results.Ok(list.Select(b => new { id = b.Id, examTypeCode = b.ExamTypeCode, examDate = b.ExamDate, status = b.Status, testCenter = b.TestCenter, bookingReference = b.BookingReference, externalUrl = b.ExternalUrl, createdAt = b.CreatedAt }));
        });

        bookings.MapPost("/", async (HttpContext http, CreateBookingRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var booking = new ExamBooking
            {
                Id = $"eb-{Guid.NewGuid():N}",
                UserId = http.UserId(),
                ExamTypeCode = req.ExamTypeCode,
                ExamDate = req.ExamDate,
                BookingReference = req.BookingReference,
                ExternalUrl = req.ExternalUrl,
                Status = "planned",
                TestCenter = req.TestCenter,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ExamBookings.Add(booking);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = booking.Id });
        });

        bookings.MapDelete("/{bookingId}", async (HttpContext http, string bookingId, LearnerDbContext db, CancellationToken ct) =>
        {
            var booking = await db.ExamBookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == http.UserId(), ct);
            if (booking == null) return Results.NotFound(new { error = "NOT_FOUND" });
            db.ExamBookings.Remove(booking);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = true });
        });

        // ── Tutoring ──────────────────────────────────────────────────────
        var tutoring = v1.MapGroup("/tutoring");

        tutoring.MapGet("/sessions", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var sessions = await db.TutoringSessions.Where(s => s.LearnerUserId == http.UserId()).OrderByDescending(s => s.ScheduledAt).ToListAsync(ct);
            return Results.Ok(sessions.Select(s => new { id = s.Id, expertUserId = s.ExpertUserId, examTypeCode = s.ExamTypeCode, subtestFocus = s.SubtestFocus, scheduledAt = s.ScheduledAt, durationMinutes = s.DurationMinutes, state = s.State, price = s.Price, learnerRating = s.LearnerRating }));
        });

        tutoring.MapPost("/sessions", async (HttpContext http, BookTutoringRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var price = await ResolveTutoringPriceAsync(req, db, ct);

            var session = new TutoringSession
            {
                Id = $"ts-{Guid.NewGuid():N}",
                LearnerUserId = http.UserId(),
                ExpertUserId = req.ExpertUserId,
                ExamTypeCode = req.ExamTypeCode,
                SubtestFocus = req.SubtestFocus,
                ScheduledAt = req.ScheduledAt,
                DurationMinutes = req.DurationMinutes,
                State = "booked",
                LearnerNotes = req.LearnerNotes,
                Price = price,
                PaymentSource = "credits",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.TutoringSessions.Add(session);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = session.Id });
        });

        tutoring.MapPost("/sessions/{sessionId}/rate", async (HttpContext http, string sessionId, RateSessionRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var session = await db.TutoringSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.LearnerUserId == http.UserId(), ct);
            if (session == null) return Results.NotFound(new { error = "NOT_FOUND" });
            session.LearnerRating = req.Rating;
            session.LearnerFeedback = req.Feedback;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { rated = true });
        });

        return app;
    }

    private static string GenerateReferralCode(string displayName)
    {
        var prefix = new string(displayName.Where(char.IsLetterOrDigit).Take(4).Select(char.ToUpper).ToArray());
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpper();
        return $"{prefix}{suffix}";
    }

    private static object MapReferralCode(ReferralCode rc) => new
    {
        code = rc.Code,
        totalReferrals = rc.TotalReferrals,
        convertedReferrals = rc.ConvertedReferrals,
        totalCreditsEarned = rc.TotalCreditsEarned,
        createdAt = rc.CreatedAt
    };

    private static async Task<decimal> ResolveTutoringPriceAsync(BookTutoringRequest req, LearnerDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ExpertUserId))
        {
            throw ApiException.Validation(
                "expert_required",
                "Expert user id is required.",
                [new ApiFieldError("expertUserId", "required", "Expert user id is required.")]);
        }

        if (req.DurationMinutes <= 0 || req.DurationMinutes > MaxTutoringDurationMinutes)
        {
            throw ApiException.Validation(
                "invalid_tutoring_duration",
                $"Tutoring duration must be between 1 and {MaxTutoringDurationMinutes} minutes.",
                [new ApiFieldError("durationMinutes", "out_of_range", $"Duration must be between 1 and {MaxTutoringDurationMinutes} minutes.")]);
        }

        var expertIsActive = await db.ExpertUsers
            .AsNoTracking()
            .AnyAsync(expert => expert.Id == req.ExpertUserId && expert.IsActive, ct);
        if (!expertIsActive)
        {
            throw ApiException.NotFound("expert_unavailable", "Expert is unavailable for tutoring.");
        }

        var onboarding = await db.ExpertOnboardingProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(progress => progress.ExpertUserId == req.ExpertUserId, ct);
        if (onboarding is null || !onboarding.IsComplete)
        {
            throw ExpertRatesUnavailable();
        }

        var rates = JsonSupport.Deserialize<ExpertOnboardingRatesDto?>(onboarding.RatesJson, null);
        if (rates is null || rates.HourlyRateMinorUnits < 0 || rates.SessionRateMinorUnits < 0)
        {
            throw ExpertRatesUnavailable();
        }

        return CalculateTutoringPrice(rates, req.DurationMinutes);
    }

    private static decimal CalculateTutoringPrice(ExpertOnboardingRatesDto rates, int durationMinutes)
    {
        var minorUnits = rates.SessionRateMinorUnits > 0
            ? rates.SessionRateMinorUnits
            : (decimal)rates.HourlyRateMinorUnits * durationMinutes / 60m;

        return minorUnits / 100m;
    }

    private static ApiException ExpertRatesUnavailable()
        => ApiException.Conflict("expert_rates_unavailable", "Expert tutoring rates are not available.");
}

public record ApplyReferralRequest(string Code);
public record CreateBookingRequest(string ExamTypeCode, DateOnly ExamDate, string? BookingReference, string? ExternalUrl, string? TestCenter);
public record BookTutoringRequest(string ExpertUserId, string ExamTypeCode, string? SubtestFocus, DateTimeOffset ScheduledAt, int DurationMinutes, string? LearnerNotes, decimal Price);
public record RateSessionRequest(int Rating, string? Feedback);

file static class SocialHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
