using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class LearningContentEndpoints
{
    public static IEndpointRouteBuilder MapLearningContentEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");

        // Learner-visible release gates. Keep this allow-listed so internal
        // operational flags are not exposed through the learner API surface.
        var features = v1.MapGroup("/features");
        features.MapGet("/{featureKey}", async (string featureKey, VideoLessonService videoLessons, CancellationToken ct) =>
        {
            var normalized = featureKey.Trim().ToLowerInvariant();
            if (normalized is not ("video_lessons" or "video-lessons"))
            {
                return Results.NotFound(new { error = "NOT_FOUND" });
            }

            return Results.Ok(new LearnerFeatureFlagResponse("video_lessons", await videoLessons.IsEnabledAsync(ct)));
        });

        // ── Grammar Lessons ───────────────────────────────────────────────
        var grammar = v1.MapGroup("/grammar");

        grammar.MapGet("/lessons", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] string? category,
            [FromQuery] string? level,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var query = db.GrammarLessons.Where(l => l.Status == "active");
            if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(l => l.ExamTypeCode == examTypeCode);
            if (!string.IsNullOrEmpty(category)) query = query.Where(l => l.Category == category);
            if (!string.IsNullOrEmpty(level)) query = query.Where(l => l.Level == level);
            var lessons = await query.OrderBy(l => l.SortOrder).ToListAsync(ct);
            return Results.Ok(lessons.Select(l => new { id = l.Id, title = l.Title, description = l.Description, category = l.Category, level = l.Level, estimatedMinutes = l.EstimatedMinutes, sortOrder = l.SortOrder }));
        });

        grammar.MapGet("/lessons/{lessonId}", async (HttpContext http, string lessonId, LearnerDbContext db, CancellationToken ct) =>
        {
            var lesson = await db.GrammarLessons.FindAsync([lessonId], ct);
            if (lesson == null || lesson.Status != "active") return Results.NotFound(new { error = "NOT_FOUND" });

            var progress = await db.LearnerGrammarProgress.FirstOrDefaultAsync(p => p.UserId == http.UserId() && p.LessonId == lessonId, ct);
            return Results.Ok(new
            {
                id = lesson.Id, title = lesson.Title, description = lesson.Description, category = lesson.Category,
                level = lesson.Level, contentHtml = lesson.ContentHtml, exercisesJson = lesson.ExercisesJson,
                estimatedMinutes = lesson.EstimatedMinutes, prerequisiteLessonId = lesson.PrerequisiteLessonId,
                progress = progress == null ? null : new { status = progress.Status, exerciseScore = progress.ExerciseScore, startedAt = progress.StartedAt, completedAt = progress.CompletedAt }
            });
        });

        grammar.MapPost("/lessons/{lessonId}/start", async (HttpContext http, string lessonId, LearnerDbContext db, CancellationToken ct) =>
        {
            var lesson = await db.GrammarLessons.FindAsync([lessonId], ct);
            if (lesson == null) return Results.NotFound(new { error = "NOT_FOUND" });

            var progress = await db.LearnerGrammarProgress.FirstOrDefaultAsync(p => p.UserId == http.UserId() && p.LessonId == lessonId, ct);
            if (progress == null)
            {
                progress = new LearnerGrammarProgress { Id = Guid.NewGuid(), UserId = http.UserId(), LessonId = lessonId, Status = "in_progress", StartedAt = DateTimeOffset.UtcNow };
                db.LearnerGrammarProgress.Add(progress);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { status = progress.Status });
        });

        grammar.MapPost("/lessons/{lessonId}/complete", async (HttpContext http, string lessonId, GrammarCompletionRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var progress = await db.LearnerGrammarProgress.FirstOrDefaultAsync(p => p.UserId == http.UserId() && p.LessonId == lessonId, ct);
            if (progress == null) return Results.NotFound(new { error = "NOT_STARTED" });

            progress.Status = "completed";
            progress.ExerciseScore = req.Score;
            progress.AnswersJson = req.AnswersJson;
            progress.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { status = "completed", score = req.Score });
        });

        // ── Video Lessons ─────────────────────────────────────────────────
        var lessons = v1.MapGroup("/lessons");
        static IResult FeatureDisabled() => Results.NotFound(new { code = "FEATURE_DISABLED", message = "Video lessons are not enabled." });

        lessons.MapGet("/", async (
            HttpContext http,
            [FromQuery] string? examTypeCode,
            [FromQuery] string? subtestCode,
            [FromQuery] string? category,
            VideoLessonService service,
            CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled();
            }

            return Results.Ok(await service.ListLessonsAsync(http.UserId(), examTypeCode ?? "oet", subtestCode, category, ct));
        });

        lessons.MapGet("/programs/{programId}", async (HttpContext http, string programId, VideoLessonService service, CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled();
            }

            var program = await service.GetProgramAsync(http.UserId(), programId, ct);
            return program is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(program);
        });

        lessons.MapGet("/{lessonId}", async (HttpContext http, string lessonId, VideoLessonService service, CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled();
            }

            var lesson = await service.GetLessonAsync(http.UserId(), lessonId, ct);
            return lesson is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(lesson);
        });

        lessons.MapPost("/{lessonId}/progress", async (HttpContext http, string lessonId, VideoProgressRequest req, VideoLessonService service, CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled();
            }

            var progress = await service.UpdateProgressAsync(http.UserId(), lessonId, req.WatchedSeconds, ct);
            return progress is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(progress);
        });

        // ── Strategy Guides ───────────────────────────────────────────────
        var strategies = v1.MapGroup("/strategies");

        strategies.MapGet("/", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] string? subtestCode,
            [FromQuery] string? category,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var query = db.StrategyGuides.Where(g => g.Status == "active");
            if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(g => g.ExamTypeCode == examTypeCode);
            if (!string.IsNullOrEmpty(subtestCode)) query = query.Where(g => g.SubtestCode == subtestCode);
            if (!string.IsNullOrEmpty(category)) query = query.Where(g => g.Category == category);
            var guides = await query.OrderBy(g => g.SortOrder).ToListAsync(ct);
            return Results.Ok(guides.Select(g => new { id = g.Id, title = g.Title, summary = g.Summary, examTypeCode = g.ExamTypeCode, subtestCode = g.SubtestCode, category = g.Category, readingTimeMinutes = g.ReadingTimeMinutes, sortOrder = g.SortOrder, publishedAt = g.PublishedAt }));
        });

        strategies.MapGet("/{guideId}", async (string guideId, LearnerDbContext db, CancellationToken ct) =>
        {
            var guide = await db.StrategyGuides.FindAsync([guideId], ct);
            if (guide == null || guide.Status != "active") return Results.NotFound(new { error = "NOT_FOUND" });
            return Results.Ok(new { id = guide.Id, title = guide.Title, summary = guide.Summary, contentHtml = guide.ContentHtml, examTypeCode = guide.ExamTypeCode, subtestCode = guide.SubtestCode, category = guide.Category, readingTimeMinutes = guide.ReadingTimeMinutes, publishedAt = guide.PublishedAt });
        });

        // Pronunciation drills are already mapped in PronunciationEndpoints — removed duplicate

        return app;
    }
}

public record GrammarCompletionRequest(int Score, string AnswersJson);

public record LearnerFeatureFlagResponse(string Key, bool Enabled);

file static class LearningContentHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
