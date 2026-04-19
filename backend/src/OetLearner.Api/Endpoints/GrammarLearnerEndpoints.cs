using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Grammar;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing Grammar endpoints. MISSION-CRITICAL DTO projection:
/// learner payloads NEVER carry correct answers, accepted synonyms, or
/// explanations until after a submission has been graded.
/// </summary>
public static class GrammarLearnerEndpoints
{
    public static IEndpointRouteBuilder MapGrammarLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1/grammar").RequireAuthorization("LearnerOnly");

        // ── Overview ─────────────────────────────────────────────────────
        v1.MapGet("/overview", async (HttpContext http, [FromQuery] string? examTypeCode, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var code = (examTypeCode ?? "oet").ToLowerInvariant();

            var topics = await db.GrammarTopics
                .Where(t => t.ExamTypeCode == code && t.Status == "published")
                .OrderBy(t => t.SortOrder)
                .ToListAsync(ct);

            var topicIds = topics.Select(t => t.Id).ToList();
            var lessonsByTopic = await db.GrammarLessons
                .Where(l => topicIds.Contains(l.TopicId!) && l.PublishState == "published")
                .ToListAsync(ct);

            var progresses = await db.LearnerGrammarProgress
                .Where(p => p.UserId == userId)
                .ToListAsync(ct);
            var progressByLesson = progresses.ToDictionary(p => p.LessonId);

            var summaries = await db.LearnerGrammarMasterySummaries
                .Where(s => s.UserId == userId && topicIds.Contains(s.TopicId))
                .ToListAsync(ct);
            var summaryByTopic = summaries.ToDictionary(s => s.TopicId);

            var topicDtos = topics.Select(t =>
            {
                var lessons = lessonsByTopic.Where(l => l.TopicId == t.Id).ToList();
                var completed = 0; var mastered = 0;
                foreach (var l in lessons)
                {
                    if (progressByLesson.TryGetValue(l.Id, out var pr))
                    {
                        if (pr.Status == "completed") completed++;
                        if (pr.MasteryScore >= 80) mastered++;
                    }
                }
                var avg = summaryByTopic.TryGetValue(t.Id, out var sum) ? sum.AvgMasteryScore : 0;
                return new GrammarTopicLearnerDto(
                    Id: t.Id,
                    Slug: t.Slug,
                    ExamTypeCode: t.ExamTypeCode,
                    Name: t.Name,
                    Description: t.Description,
                    IconEmoji: t.IconEmoji,
                    LevelHint: t.LevelHint,
                    SortOrder: t.SortOrder,
                    LessonCount: lessons.Count,
                    CompletedCount: completed,
                    MasteredCount: mastered,
                    AvgMasteryScore: avg);
            }).ToList();

            var recommendations = await db.GrammarRecommendations
                .Where(r => r.UserId == userId && r.DismissedAt == null)
                .OrderByDescending(r => r.Relevance)
                .ThenByDescending(r => r.CreatedAt)
                .Take(8)
                .ToListAsync(ct);
            var recLessonIds = recommendations.Select(r => r.LessonId).ToList();
            var recLessons = await db.GrammarLessons
                .Where(l => recLessonIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);

            var recDtos = recommendations
                .Where(r => recLessons.ContainsKey(r.LessonId))
                .Select(r => new GrammarRecommendationDto(
                    Id: r.Id,
                    LessonId: r.LessonId,
                    LessonTitle: recLessons[r.LessonId].Title,
                    Source: r.Source,
                    SourceRefId: r.SourceRefId,
                    RuleId: r.RuleId,
                    Relevance: r.Relevance,
                    CreatedAt: r.CreatedAt,
                    DismissedAt: r.DismissedAt))
                .ToList();

            var totalLessons = lessonsByTopic.Count;
            var overallCompleted = progresses.Count(p => p.Status == "completed");
            var overallMastered = progresses.Count(p => p.MasteryScore >= 80);
            var overallMastery = progresses.Count == 0 ? 0 : progresses.Average(p => (double)p.MasteryScore);

            return Results.Ok(new GrammarOverviewDto(
                Topics: topicDtos,
                Recommendations: recDtos,
                LessonsCompleted: overallCompleted,
                LessonsMastered: overallMastered,
                LessonsTotal: totalLessons,
                OverallMasteryScore: overallMastery));
        });

        // ── Topics ────────────────────────────────────────────────────────
        v1.MapGet("/topics", async ([FromQuery] string? examTypeCode, LearnerDbContext db, CancellationToken ct) =>
        {
            var code = (examTypeCode ?? "oet").ToLowerInvariant();
            var topics = await db.GrammarTopics
                .Where(t => t.ExamTypeCode == code && t.Status == "published")
                .OrderBy(t => t.SortOrder)
                .ToListAsync(ct);
            return Results.Ok(topics.Select(t => new
            {
                id = t.Id,
                slug = t.Slug,
                name = t.Name,
                description = t.Description,
                iconEmoji = t.IconEmoji,
                levelHint = t.LevelHint,
                sortOrder = t.SortOrder,
            }));
        });

        v1.MapGet("/topics/{slug}", async (string slug, [FromQuery] string? examTypeCode, HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var code = (examTypeCode ?? "oet").ToLowerInvariant();
            var topic = await db.GrammarTopics
                .FirstOrDefaultAsync(t => t.Slug == slug && t.ExamTypeCode == code && t.Status == "published", ct);
            if (topic is null) return Results.NotFound(new { error = "TOPIC_NOT_FOUND" });

            var lessons = await db.GrammarLessons
                .Where(l => l.TopicId == topic.Id && l.PublishState == "published")
                .OrderBy(l => l.SortOrder)
                .ToListAsync(ct);
            var lessonIds = lessons.Select(l => l.Id).ToList();

            var progresses = await db.LearnerGrammarProgress
                .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
                .ToDictionaryAsync(p => p.LessonId, ct);

            var exerciseCounts = await db.GrammarExercises
                .Where(e => lessonIds.Contains(e.LessonId))
                .GroupBy(e => e.LessonId)
                .Select(g => new { LessonId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.LessonId, g => g.Count, ct);

            var lessonDtos = lessons.Select(l =>
            {
                var pr = progresses.GetValueOrDefault(l.Id);
                return new GrammarLessonSummaryDto(
                    Id: l.Id,
                    ExamTypeCode: l.ExamTypeCode,
                    TopicId: l.TopicId,
                    TopicSlug: topic.Slug,
                    Title: l.Title,
                    Description: l.Description,
                    Level: l.Level,
                    Category: l.Category,
                    EstimatedMinutes: l.EstimatedMinutes,
                    SortOrder: l.SortOrder,
                    ProgressStatus: pr?.Status ?? "not_started",
                    MasteryScore: pr?.MasteryScore ?? 0,
                    ExerciseCount: exerciseCounts.GetValueOrDefault(l.Id));
            }).ToList();

            return Results.Ok(new
            {
                topic = new { id = topic.Id, slug = topic.Slug, name = topic.Name, description = topic.Description, iconEmoji = topic.IconEmoji, levelHint = topic.LevelHint },
                lessons = lessonDtos,
            });
        });

        // ── Lesson list (filter: exam + level + topic) ───────────────────
        v1.MapGet("/lessons", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] string? category,
            [FromQuery] string? level,
            [FromQuery] string? topicSlug,
            HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var query = db.GrammarLessons.AsQueryable();

            if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(l => l.ExamTypeCode == examTypeCode);
            if (!string.IsNullOrEmpty(category)) query = query.Where(l => l.Category == category);
            if (!string.IsNullOrEmpty(level) && level != "all") query = query.Where(l => l.Level == level);

            string? topicId = null;
            string? slug = null;
            if (!string.IsNullOrEmpty(topicSlug))
            {
                var t = await db.GrammarTopics.FirstOrDefaultAsync(x => x.Slug == topicSlug, ct);
                topicId = t?.Id;
                slug = t?.Slug;
                query = query.Where(l => l.TopicId == topicId);
            }

            // Published if PublishState exists, otherwise fall back to legacy Status=="active"
            query = query.Where(l => l.PublishState == "published" || (l.PublishState == "draft" && l.Status == "active" && l.TopicId == null));

            var lessons = await query.OrderBy(l => l.SortOrder).Take(200).ToListAsync(ct);
            var lessonIds = lessons.Select(l => l.Id).ToList();

            var progresses = await db.LearnerGrammarProgress
                .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
                .ToDictionaryAsync(p => p.LessonId, ct);

            var exerciseCounts = await db.GrammarExercises
                .Where(e => lessonIds.Contains(e.LessonId))
                .GroupBy(e => e.LessonId)
                .Select(g => new { LessonId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.LessonId, g => g.Count, ct);

            var topicIds = lessons.Where(l => l.TopicId is not null).Select(l => l.TopicId!).Distinct().ToList();
            var topicSlugById = await db.GrammarTopics
                .Where(t => topicIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Slug, ct);

            var result = lessons.Select(l =>
            {
                var pr = progresses.GetValueOrDefault(l.Id);
                return new GrammarLessonSummaryDto(
                    Id: l.Id,
                    ExamTypeCode: l.ExamTypeCode,
                    TopicId: l.TopicId,
                    TopicSlug: l.TopicId != null && topicSlugById.TryGetValue(l.TopicId, out var ts) ? ts : slug,
                    Title: l.Title,
                    Description: l.Description,
                    Level: l.Level,
                    Category: l.Category,
                    EstimatedMinutes: l.EstimatedMinutes,
                    SortOrder: l.SortOrder,
                    ProgressStatus: pr?.Status ?? "not_started",
                    MasteryScore: pr?.MasteryScore ?? 0,
                    ExerciseCount: exerciseCounts.GetValueOrDefault(l.Id));
            }).ToList();

            return Results.Ok(result);
        });

        // ── Lesson detail (strict DTO) ───────────────────────────────────
        v1.MapGet("/lessons/{lessonId}", async (HttpContext http, string lessonId, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var lesson = await db.GrammarLessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
            if (lesson is null) return Results.NotFound(new { error = "NOT_FOUND" });
            if (lesson.PublishState == "archived" || lesson.Status == "archived") return Results.NotFound(new { error = "ARCHIVED" });
            if (lesson.PublishState != "published" && lesson.Status != "active") return Results.NotFound(new { error = "NOT_PUBLISHED" });

            var blocks = await db.GrammarContentBlocks
                .Where(b => b.LessonId == lessonId)
                .OrderBy(b => b.SortOrder)
                .ToListAsync(ct);
            var exercises = await db.GrammarExercises
                .Where(e => e.LessonId == lessonId)
                .OrderBy(e => e.SortOrder)
                .ToListAsync(ct);

            string? topicSlug = null;
            if (!string.IsNullOrWhiteSpace(lesson.TopicId))
            {
                var t = await db.GrammarTopics.FindAsync(new object[] { lesson.TopicId }, ct);
                topicSlug = t?.Slug;
            }

            var progress = await db.LearnerGrammarProgress
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId, ct);

            var blockDtos = blocks.Select(b =>
            {
                JsonElement? json = null;
                if (!string.IsNullOrWhiteSpace(b.ContentJson) && b.ContentJson != "{}")
                {
                    try { json = JsonDocument.Parse(b.ContentJson).RootElement.Clone(); } catch { json = null; }
                }
                return new GrammarContentBlockLearnerDto(b.Id, b.SortOrder, b.Type, b.ContentMarkdown, json);
            }).ToList();

            var exerciseDtos = exercises.Select(e =>
            {
                JsonElement opts;
                try { opts = JsonDocument.Parse(string.IsNullOrWhiteSpace(e.OptionsJson) ? "[]" : e.OptionsJson).RootElement.Clone(); }
                catch { opts = JsonDocument.Parse("[]").RootElement; }

                // SECURITY: intentionally NOT populating CorrectAnswer / Explanation here.
                return new GrammarExerciseLearnerDto(
                    Id: e.Id,
                    SortOrder: e.SortOrder,
                    Type: e.Type,
                    PromptMarkdown: e.PromptMarkdown,
                    Options: opts,
                    Difficulty: e.Difficulty,
                    Points: e.Points);
            }).ToList();

            // ── LEGACY FALLBACK: lesson pre-dates v2, only has ExercisesJson ──
            if (exerciseDtos.Count == 0 && !string.IsNullOrWhiteSpace(lesson.ExercisesJson) && lesson.ExercisesJson != "[]")
            {
                // surface legacy prose content via a block
                if (blockDtos.Count == 0 && !string.IsNullOrWhiteSpace(lesson.ContentHtml))
                {
                    blockDtos.Add(new GrammarContentBlockLearnerDto(
                        Id: $"legacy-{lesson.Id}",
                        SortOrder: 1,
                        Type: "prose",
                        ContentMarkdown: GrammarContentSanitiser.Sanitise(lesson.ContentHtml),
                        Content: null));
                }
            }
            else if (blockDtos.Count == 0 && !string.IsNullOrWhiteSpace(lesson.ContentHtml))
            {
                blockDtos.Add(new GrammarContentBlockLearnerDto(
                    Id: $"legacy-{lesson.Id}",
                    SortOrder: 1,
                    Type: "prose",
                    ContentMarkdown: GrammarContentSanitiser.Sanitise(lesson.ContentHtml),
                    Content: null));
            }

            var dto = new GrammarLessonLearnerDto(
                Id: lesson.Id,
                ExamTypeCode: lesson.ExamTypeCode,
                TopicId: lesson.TopicId,
                TopicSlug: topicSlug,
                Title: lesson.Title,
                Description: lesson.Description,
                Level: lesson.Level,
                Category: lesson.Category,
                EstimatedMinutes: lesson.EstimatedMinutes,
                PrerequisiteLessonId: lesson.PrerequisiteLessonId,
                ContentBlocks: blockDtos,
                Exercises: exerciseDtos,
                Progress: progress is null ? null : new GrammarLessonProgressDto(
                    Status: progress.Status,
                    ExerciseScore: progress.ExerciseScore,
                    MasteryScore: progress.MasteryScore,
                    AttemptCount: progress.AttemptCount,
                    StartedAt: progress.StartedAt,
                    LastAttemptedAt: progress.LastAttemptedAt,
                    CompletedAt: progress.CompletedAt));

            return Results.Ok(dto);
        });

        // ── Start (idempotent) ───────────────────────────────────────────
        v1.MapPost("/lessons/{lessonId}/start", async (HttpContext http, string lessonId, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var lesson = await db.GrammarLessons.FindAsync(new object[] { lessonId }, ct);
            if (lesson is null) return Results.NotFound(new { error = "NOT_FOUND" });

            var progress = await db.LearnerGrammarProgress
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId, ct);
            if (progress is null)
            {
                progress = new LearnerGrammarProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    LessonId = lessonId,
                    Status = "in_progress",
                    StartedAt = DateTimeOffset.UtcNow,
                };
                db.LearnerGrammarProgress.Add(progress);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { status = progress.Status, masteryScore = progress.MasteryScore });
        });

        // ── Attempts (server-authoritative grading) ───────────────────────
        v1.MapPost("/lessons/{lessonId}/attempts", async (
            HttpContext http, string lessonId,
            [FromBody] GrammarAttemptRequestDto body,
            IGrammarGradingService grading,
            IGrammarService service,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            var lesson = await db.GrammarLessons.FindAsync(new object[] { lessonId }, ct);
            if (lesson is null) return Results.NotFound(new { error = "NOT_FOUND" });

            var answers = body.Answers ?? new();
            var result = await grading.GradeAttemptAsync(userId, lessonId, answers, ct);
            var summary = await service.ApplyPostGradeHooksAsync(userId, result, ct);

            var dto = new GrammarAttemptResultDto(
                LessonId: result.LessonId,
                Score: result.Score,
                PointsEarned: result.PointsEarned,
                MaxPoints: result.MaxPoints,
                CorrectCount: result.CorrectCount,
                IncorrectCount: result.IncorrectCount,
                MasteryScore: result.MasteryScore,
                Mastered: result.Mastered,
                XpAwarded: summary.XpAwarded,
                ReviewItemsCreated: summary.ReviewItemsCreated,
                Exercises: result.Exercises.Select(e => new GrammarExerciseResultDto(
                    ExerciseId: e.ExerciseId,
                    Type: e.Type,
                    IsCorrect: e.IsCorrect,
                    PointsEarned: e.PointsEarned,
                    MaxPoints: e.MaxPoints,
                    UserAnswer: e.UserAnswer,
                    CorrectAnswer: e.CorrectAnswer,
                    ExplanationMarkdown: e.ExplanationMarkdown)).ToList());

            return Results.Ok(dto);
        });

        // ── Complete (legacy shim → forwards to /attempts with server-side grading) ──
        v1.MapPost("/lessons/{lessonId}/complete", async (
            HttpContext http, string lessonId,
            [FromBody] LegacyGrammarCompletionRequest req,
            IGrammarGradingService grading, IGrammarService service, LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            var lesson = await db.GrammarLessons.FindAsync(new object[] { lessonId }, ct);
            if (lesson is null) return Results.NotFound(new { error = "NOT_FOUND" });

            Dictionary<string, JsonElement> answers;
            try
            {
                answers = string.IsNullOrWhiteSpace(req.AnswersJson)
                    ? new Dictionary<string, JsonElement>()
                    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(req.AnswersJson) ?? new();
            }
            catch
            {
                answers = new Dictionary<string, JsonElement>();
            }

            var result = await grading.GradeAttemptAsync(userId, lessonId, answers, ct);
            var summary = await service.ApplyPostGradeHooksAsync(userId, result, ct);
            return Results.Ok(new { status = "completed", score = result.Score, masteryScore = result.MasteryScore, xpAwarded = summary.XpAwarded });
        });

        // ── Progress summary ─────────────────────────────────────────────
        v1.MapGet("/progress", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var progresses = await db.LearnerGrammarProgress
                .Where(p => p.UserId == userId)
                .ToListAsync(ct);

            var total = progresses.Count;
            var completed = progresses.Count(p => p.Status == "completed");
            var mastered = progresses.Count(p => p.MasteryScore >= 80);
            var avg = progresses.Count == 0 ? 0.0 : progresses.Average(p => (double)p.MasteryScore);

            var summaries = await db.LearnerGrammarMasterySummaries
                .Where(s => s.UserId == userId)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                totalStarted = total,
                completed,
                mastered,
                avgMasteryScore = avg,
                topics = summaries.Select(s => new
                {
                    topicId = s.TopicId,
                    lessonsCompleted = s.LessonsCompleted,
                    lessonsMastered = s.LessonsMastered,
                    avgMasteryScore = s.AvgMasteryScore,
                }),
            });
        });

        // ── Recommendations ───────────────────────────────────────────────
        v1.MapGet("/recommendations", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var recs = await db.GrammarRecommendations
                .Where(r => r.UserId == userId && r.DismissedAt == null)
                .OrderByDescending(r => r.Relevance)
                .ThenByDescending(r => r.CreatedAt)
                .Take(20)
                .ToListAsync(ct);

            var lessonIds = recs.Select(r => r.LessonId).Distinct().ToList();
            var lessons = await db.GrammarLessons
                .Where(l => lessonIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);

            return Results.Ok(recs.Where(r => lessons.ContainsKey(r.LessonId)).Select(r => new GrammarRecommendationDto(
                Id: r.Id,
                LessonId: r.LessonId,
                LessonTitle: lessons[r.LessonId].Title,
                Source: r.Source,
                SourceRefId: r.SourceRefId,
                RuleId: r.RuleId,
                Relevance: r.Relevance,
                CreatedAt: r.CreatedAt,
                DismissedAt: r.DismissedAt)));
        });

        v1.MapPost("/recommendations/{id}/dismiss", async (HttpContext http, string id, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var rec = await db.GrammarRecommendations.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
            if (rec is null) return Results.NotFound(new { error = "NOT_FOUND" });
            rec.DismissedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { dismissed = true });
        });

        return app;
    }
}

public sealed record LegacyGrammarCompletionRequest(int Score, string AnswersJson);

file static class GrammarLearnerHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
