using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.StudyPlanner;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing endpoints for Study Planner v2. Adds calendar view, ICS
/// export, and a richer `/items/{id}` detail endpoint on top of the legacy
/// <c>/v1/study-plan</c> surface (kept in <c>LearnerEndpoints</c> for backward
/// compatibility).
/// </summary>
public static class StudyPlannerLearnerEndpoints
{
    public static IEndpointRouteBuilder MapStudyPlannerLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/study-plan")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        // v2 plan DTO (richer than the legacy). Learners hitting old clients
        // continue to use the LearnerEndpoints route; new clients use this.
        g.MapGet("/v2", async (
            IStudyPlannerService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var plan = await svc.GetOrCreatePlanAsync(userId, ct);
            var items = await svc.GetItemsAsync(userId, ct);
            return Results.Ok(new
            {
                plan = new
                {
                    plan.Id, plan.Version, plan.State, plan.Checkpoint, plan.WeakSkillFocus,
                    plan.TemplateId, plan.GeneratedAt,
                },
                items,
            });
        });

        // Calendar window
        g.MapGet("/calendar", async (
            IStudyPlannerService svc, HttpContext http, string? from, string? to, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var items = await svc.GetItemsAsync(userId, ct);
            DateOnly? fromD = DateOnly.TryParse(from, out var f) ? f : null;
            DateOnly? toD = DateOnly.TryParse(to, out var t) ? t : null;
            var filtered = items.Where(i =>
                (fromD is null || i.DueDate >= fromD) &&
                (toD is null || i.DueDate <= toD)).ToList();
            return Results.Ok(filtered);
        });

        // Item detail (for lazy rationale load + start URL resolution)
        g.MapGet("/items/{itemId}", async (
            string itemId, IStudyPlannerService svc, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            // Must belong to this learner
            var item = await db.StudyPlanItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId, ct);
            if (item is null) return Results.NotFound();
            var plan = await db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.StudyPlanId, ct);
            if (plan is null || plan.UserId != userId) return Results.NotFound();
            return Results.Ok(StudyPlannerService.ToView(item));
        });

        // Start action: records `StartedAt`, returns the deep-link URL.
        g.MapPost("/items/{itemId}/start", async (
            string itemId, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var item = await db.StudyPlanItems.FirstOrDefaultAsync(x => x.Id == itemId, ct);
            if (item is null) return Results.NotFound();
            var plan = await db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.StudyPlanId, ct);
            if (plan is null || plan.UserId != userId) return Results.NotFound();
            if (item.StartedAt is null) item.StartedAt = DateTimeOffset.UtcNow;
            if (item.Status == StudyPlanItemStatus.NotStarted) item.Status = StudyPlanItemStatus.InProgress;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            db.AnalyticsEvents.Add(new AnalyticsEventRecord
            {
                Id = $"evt-{Guid.NewGuid():N}",
                UserId = userId,
                EventName = "study_plan_item_started",
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { itemId, item.ContentPaperId, item.SubtestCode }),
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { startUrl = StudyPlannerService.BuildStartUrl(item) });
        });

        // Snooze (new in v2)
        g.MapPost("/items/{itemId}/snooze", async (
            string itemId, SnoozeRequest body, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var item = await db.StudyPlanItems.FirstOrDefaultAsync(x => x.Id == itemId, ct);
            if (item is null) return Results.NotFound();
            var plan = await db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.StudyPlanId, ct);
            if (plan is null || plan.UserId != userId) return Results.NotFound();
            var until = body.Until ?? DateTimeOffset.UtcNow.AddDays(1);
            item.SnoozedUntil = until;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Drift (now admin-policy-driven)
        g.MapGet("/drift-v2", async (
            IStudyPlannerService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var report = await svc.DetectDriftAsync(userId, allowAutoRegen: false, ct);
            return Results.Ok(new
            {
                level = report.Level,
                completionRate = report.CompletionRate,
                overdueItems = report.OverdueItems,
                driftDays = report.DriftDays,
                shouldRegenerate = report.ShouldRegenerate,
                recommendation = report.Recommendation,
            });
        });

        // iCalendar export (no auth via query string — consumes learner JWT)
        g.MapGet("/ics", async (
            IStudyPlannerService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var ics = await svc.ExportIcsAsync(userId, ct);
            return Results.Text(ics, "text/calendar", System.Text.Encoding.UTF8);
        });

        // V2 regenerate — actually runs the new generator
        g.MapPost("/regenerate-v2", async (
            IStudyPlannerService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var plan = await svc.GenerateForLearnerAsync(userId, "manual", ct);
            return Results.Ok(new { planId = plan.Id, version = plan.Version, state = plan.State.ToString().ToLowerInvariant() });
        })
        .RequireRateLimiting("PerUserWrite");

        // ── Google Calendar integration ─────────────────────────────────────
        var gc = g.MapGroup("/google-calendar");

        gc.MapGet("/authorize", (IGoogleCalendarService svc, HttpContext http) =>
        {
            var userId = RequireUserId(http);
            // Use userId as state so the callback can resolve the user without
            // a separate server-side state cookie. In production you should
            // sign this with HMAC to prevent tampering.
            var state = userId;
            var url = svc.BuildAuthorizationUrl(state);
            return Results.Ok(new { url });
        });

        gc.MapPost("/callback", async (
            GoogleCallbackBody body, IGoogleCalendarService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            if (string.IsNullOrEmpty(body.Code)) return Results.BadRequest(new { error = "code required" });
            try
            {
                var link = await svc.ExchangeCodeAsync(userId, body.Code, ct);
                return Results.Ok(new { connected = true, link.CalendarId, link.TokenHint });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        })
        .RequireRateLimiting("PerUserWrite");

        gc.MapGet("", async (IGoogleCalendarService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var link = await svc.GetLinkAsync(userId, ct);
            return Results.Ok(new
            {
                connected = link is not null,
                calendarId = link?.CalendarId,
                tokenHint = link?.TokenHint,
                lastSyncedAt = link?.LastSyncedAt,
                lastError = link?.LastError,
            });
        });

        gc.MapDelete("", async (IGoogleCalendarService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            await svc.DisconnectAsync(userId, ct);
            return Results.NoContent();
        });

        gc.MapPost("/sync", async (IGoogleCalendarService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = RequireUserId(http);
            var pushed = await svc.PushPlanAsync(userId, ct);
            return Results.Ok(new { pushed });
        })
        .RequireRateLimiting("PerUserWrite");

        return app;
    }

    private static string RequireUserId(HttpContext http)
    {
        var id = http.User.FindFirstValue("user_id")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("learner context required");
        return id;
    }
}

public sealed record SnoozeRequest(DateTimeOffset? Until);
public sealed record GoogleCallbackBody(string Code);
