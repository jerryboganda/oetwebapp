using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
using System.Security.Claims;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Listening V2 endpoint group. Mounted at <c>/v1/listening/v2/...</c>
/// alongside the legacy <c>/v1/listening-papers</c> tree so the V1 surface
/// keeps working through the migration window. Wave 2 §3.
/// </summary>
public static class ListeningV2Endpoints
{
    public sealed record AdvanceRequest(string ToState, string? ConfirmToken);
    public sealed record TechReadinessRequest(bool AudioOk, int DurationMs);
    public sealed record AudioResumeRequest(int CuePointMs);
    public sealed record SubmitRequest(Dictionary<string, string?>? Answers);
    public sealed record GradeRequest();
    public sealed record CreateClassRequest(string Name, string? Description);
    public sealed record AddMemberRequest(string MemberUserId);

    public static IEndpointRouteBuilder MapListeningV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/listening/v2")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        var teacherGroup = app.MapGroup("/v1/listening/v2/teacher")
            .RequireAuthorization("TeachingStaffOnly")
            .RequireRateLimiting("PerUser");

        // ─── Session FSM ───
        group.MapGet("/attempts/{attemptId}/state", async (
            string attemptId, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await session.GetStateAsync(attemptId, http.UserId(), ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .WithName("GetListeningV2State")
        .WithSummary("Listening V2 — read FSM state for an attempt");

        // R06.10 — two-step confirm-token advance
        group.MapPost("/attempts/{attemptId}/advance", async (
            string attemptId, AdvanceRequest req, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                var r = await session.AdvanceAsync(
                    attemptId, http.UserId(),
                    new AdvanceCommand(req.ToState, req.ConfirmToken), ct);
                return r.Outcome switch
                {
                    "applied" => Results.Ok(r),
                    "confirm-required" => Results.Json(r, statusCode: StatusCodes.Status412PreconditionFailed),
                    "rejected" => Results.UnprocessableEntity(r),
                    _ => Results.Ok(r),
                };
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("AdvanceListeningV2State")
        .WithSummary("Listening V2 — advance the FSM (two-step confirm protocol)")
        .Produces<AdvanceResultDto>(StatusCodes.Status200OK)
        .Produces<AdvanceResultDto>(StatusCodes.Status412PreconditionFailed)
        .Produces<AdvanceResultDto>(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/attempts/{attemptId}/tech-readiness", async (
            string attemptId, TechReadinessRequest req, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await session.RecordTechReadinessAsync(
                    attemptId,
                    http.UserId(),
                    new TechReadinessCommand(req.AudioOk, req.DurationMs),
                    ct));
            }
            catch (ArgumentException) { return Results.BadRequest(); }
            catch (InvalidOperationException ex)
            {
                return Results.UnprocessableEntity(new { reason = "attempt-not-in-progress", detail = ex.Message });
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("RecordListeningV2TechReadiness")
        .WithSummary("Listening V2 — record R10 tech readiness before strict attempt start")
        .Produces<TechReadinessDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/attempts/{attemptId}/audio-resume", async (
            string attemptId, AudioResumeRequest req, HttpContext http,
            ListeningSessionService session, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await session.AudioResumeAsync(
                    attemptId, http.UserId(), req.CuePointMs, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("AudioResumeListeningV2")
        .WithSummary("Listening V2 — resume audio after a transient disconnection");

        group.MapPut("/attempts/{attemptId}/answers/{questionId}", async (
            string attemptId,
            string questionId,
            ListeningAnswerSaveRequest req,
            HttpContext http,
            ListeningLearnerService learner,
            CancellationToken ct) =>
        {
            await learner.SaveAnswerAsync(http.UserId(), attemptId, questionId, req, ct);
            return Results.NoContent();
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("SaveListeningV2Answer")
        .WithSummary("Listening V2 — save one answer through the relational attempt facade")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/attempts/{attemptId}/submit", async (
            string attemptId,
            SubmitRequest? req,
            HttpContext http,
            ListeningLearnerService learner,
            ListeningPathwayProgressService pathway,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            var review = await learner.SubmitAsync(userId, attemptId, req?.Answers, ct);
            await pathway.RecomputeAsync(userId, ct);
            return Results.Ok(review);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("SubmitListeningV2Attempt")
        .WithSummary("Listening V2 — submit final answers and return the full learner review DTO")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // ─── Grading ───
        group.MapPost("/attempts/{attemptId}/grade", async (
            string attemptId, HttpContext http,
            ListeningGradingService grading,
            ListeningPathwayProgressService pathway,
            CancellationToken ct) =>
        {
            try
            {
                var result = await grading.GradeAsync(attemptId, http.UserId(), ct);
                await pathway.RecomputeAsync(http.UserId(), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("GradeListeningV2Attempt")
        .WithSummary("Listening V2 — grade an attempt + recompute pathway");

        // ─── Pathway ───
        group.MapGet("/me/pathway", async (
            HttpContext http,
            Data.LearnerDbContext db,
            IContentEntitlementService entitlements,
            ListeningPathwayProgressService pathway,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            await pathway.RecomputeAsync(userId, ct);
            var launchPaperId = await ResolvePathwayLaunchPaperIdAsync(db, entitlements, userId, ct);
            var rows = await db.ListeningPathwayProgress
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToListAsync(ct);
            // Project to a learner-safe view that ships only the canonical
            // 12 stages in declared order.
            var byStage = rows.ToDictionary(r => r.StageCode, r => r);
            var view = ListeningPathwayProgressService.PathwayStages.Select(stage =>
            {
                byStage.TryGetValue(stage, out var row);
                return new
                {
                    stage,
                    status = (row?.Status ?? Domain.ListeningPathwayStageStatus.Locked).ToString(),
                    scaledScore = row?.ScaledScore,
                    completedAt = row?.CompletedAt,
                    actionHref = ListeningPathwayLaunchTargets.BuildActionHref(
                        stage,
                        row?.Status ?? Domain.ListeningPathwayStageStatus.Locked,
                        launchPaperId),
                };
            });
            return Results.Ok(view);
        })
        .WithName("GetListeningV2Pathway")
        .WithSummary("Listening V2 — 12-stage pathway snapshot");

        // ─── Teacher classes (cross-skill) ───
        teacherGroup.MapGet("/classes", async (
            HttpContext http, TeacherClassService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListMineAsync(http.UserId(), ct)))
            .WithName("ListMyTeacherClasses");

        teacherGroup.MapPost("/classes", async (
            CreateClassRequest req, HttpContext http,
            TeacherClassService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateAsync(http.UserId(), req.Name, req.Description, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("CreateTeacherClass");

        teacherGroup.MapDelete("/classes/{classId}", async (
            string classId, HttpContext http,
            TeacherClassService svc, CancellationToken ct) =>
        {
            try { await svc.DeleteAsync(http.UserId(), classId, ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("DeleteTeacherClass");

        teacherGroup.MapPost("/classes/{classId}/members", async (
            string classId, AddMemberRequest req, HttpContext http,
            TeacherClassService svc, CancellationToken ct) =>
        {
            try { await svc.AddMemberAsync(http.UserId(), classId, req.MemberUserId, ct); return Results.NoContent(); }
            catch (ArgumentException) { return Results.BadRequest(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("AddTeacherClassMember");

        teacherGroup.MapDelete("/classes/{classId}/members/{memberUserId}", async (
            string classId, string memberUserId, HttpContext http,
            TeacherClassService svc, CancellationToken ct) =>
        {
            try { await svc.RemoveMemberAsync(http.UserId(), classId, memberUserId, ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("RemoveTeacherClassMember");

        teacherGroup.MapGet("/classes/{classId}/analytics", async (
            string classId,
            int? days,
            HttpContext http,
            IListeningAnalyticsService analytics,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await analytics.GetClassAnalyticsAsync(
                    http.UserId(), classId, days ?? 30, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        })
        .WithName("GetTeacherClassListeningAnalytics")
        .WithSummary("Listening V2 — owner-scoped teacher class analytics");

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    private static async Task<string?> ResolvePathwayLaunchPaperIdAsync(
        Data.LearnerDbContext db,
        IContentEntitlementService entitlements,
        string userId,
        CancellationToken ct)
    {
        var profession = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.ActiveProfessionId)
            .SingleOrDefaultAsync(ct);

        var candidates = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published
                && p.SubtestCode == "listening"
                && (p.AppliesToAllProfessions
                    || (!string.IsNullOrWhiteSpace(profession) && p.ProfessionId == profession))
                && db.ListeningQuestions.Any(q => q.PaperId == p.Id))
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.PublishedAt)
            .ThenBy(p => p.Title)
            .Take(20)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            try
            {
                var access = await entitlements.AllowAccessAsync(userId, candidate, ct);
                if (access.Allowed)
                {
                    return candidate.Id;
                }
            }
            catch
            {
                // Keep the pathway snapshot available even if one candidate's
                // entitlement lookup fails; attempt start still enforces access.
            }
        }

        return null;
    }
}
