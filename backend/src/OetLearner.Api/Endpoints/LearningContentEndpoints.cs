using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Endpoints;

public static class LearningContentEndpoints
{
    public static IEndpointRouteBuilder MapLearningContentEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");

        // ── Grammar Lessons ───────────────────────────────────────────────
        // Moved to GrammarLearnerEndpoints.cs (Phase 2 of the Grammar
        // module overhaul). The old inline endpoints trusted a
        // client-computed score; the new endpoints grade
        // server-authoritatively. See docs/GRAMMAR.md.

        // ── Video Lessons ─────────────────────────────────────────────────
        var lessons = v1.MapGroup("/lessons");

        lessons.MapGet("/", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] string? subtestCode,
            [FromQuery] string? category,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var query = db.VideoLessons.Where(l => l.Status == "active");
            if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(l => l.ExamTypeCode == examTypeCode);
            if (!string.IsNullOrEmpty(subtestCode)) query = query.Where(l => l.SubtestCode == subtestCode);
            if (!string.IsNullOrEmpty(category)) query = query.Where(l => l.Category == category);
            var vids = await query.OrderBy(l => l.SortOrder).ToListAsync(ct);
            return Results.Ok(vids.Select(l => new { id = l.Id, title = l.Title, description = l.Description, thumbnailUrl = l.ThumbnailUrl, durationSeconds = l.DurationSeconds, category = l.Category, instructorName = l.InstructorName, sortOrder = l.SortOrder }));
        });

        lessons.MapGet("/{lessonId}", async (HttpContext http, string lessonId, LearnerDbContext db, CancellationToken ct) =>
        {
            var lesson = await db.VideoLessons.FindAsync([lessonId], ct);
            if (lesson == null || lesson.Status != "active") return Results.NotFound(new { error = "NOT_FOUND" });

            var progress = await db.LearnerVideoProgress.FirstOrDefaultAsync(p => p.UserId == http.UserId() && p.VideoLessonId == lessonId, ct);
            return Results.Ok(new
            {
                id = lesson.Id, title = lesson.Title, description = lesson.Description, videoUrl = lesson.VideoUrl,
                thumbnailUrl = lesson.ThumbnailUrl, durationSeconds = lesson.DurationSeconds, category = lesson.Category,
                instructorName = lesson.InstructorName, chaptersJson = lesson.ChaptersJson, resourcesJson = lesson.ResourcesJson,
                progress = progress == null ? null : new { watchedSeconds = progress.WatchedSeconds, completed = progress.Completed, lastWatchedAt = progress.LastWatchedAt }
            });
        });

        lessons.MapPost("/{lessonId}/progress", async (HttpContext http, string lessonId, VideoProgressRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var lesson = await db.VideoLessons.FindAsync([lessonId], ct);
            if (lesson == null) return Results.NotFound(new { error = "NOT_FOUND" });

            var progress = await db.LearnerVideoProgress.FirstOrDefaultAsync(p => p.UserId == http.UserId() && p.VideoLessonId == lessonId, ct);
            if (progress == null)
            {
                progress = new LearnerVideoProgress { Id = Guid.NewGuid(), UserId = http.UserId(), VideoLessonId = lessonId };
                db.LearnerVideoProgress.Add(progress);
            }
            progress.WatchedSeconds = req.WatchedSeconds;
            progress.Completed = req.WatchedSeconds >= lesson.DurationSeconds * 0.9;
            progress.LastWatchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { completed = progress.Completed });
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

public record VideoProgressRequest(int WatchedSeconds);

file static class LearningContentHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
