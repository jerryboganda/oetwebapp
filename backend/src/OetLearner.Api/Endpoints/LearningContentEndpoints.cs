using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        features.MapGet("/{featureKey}", async (
            string featureKey,
            VideoLessonService videoLessons,
            StrategyGuideService strategyGuides,
            CancellationToken ct) =>
        {
            var normalized = featureKey.Trim().ToLowerInvariant();
            return normalized switch
            {
                "video_lessons" or "video-lessons" =>
                    Results.Ok(new LearnerFeatureFlagResponse("video_lessons", await videoLessons.IsEnabledAsync(ct))),
                "strategy_guides" or "strategy-guides" =>
                    Results.Ok(new LearnerFeatureFlagResponse("strategy_guides", await strategyGuides.IsEnabledAsync(ct))),
                _ => Results.NotFound(new { code = "NOT_FOUND", message = "Feature flag is not exposed to learners." })
            };
        })
        .WithName("GetLearnerFeatureFlag")
        .WithSummary("Returns a learner-visible feature release gate.");

        // ── Grammar Lessons ───────────────────────────────────────────────
        var grammar = v1.MapGroup("/grammar");

        grammar.MapGet("/entitlement", async (
            HttpContext http,
            OetLearner.Api.Services.Grammar.IGrammarEntitlementService entitlement,
            CancellationToken ct) =>
        {
            var result = await entitlement.CheckAsync(http.UserId(), ct);
            return Results.Ok(new
            {
                allowed = result.Allowed,
                tier = result.Tier,
                remaining = result.Remaining == int.MaxValue ? (int?)null : result.Remaining,
                limitPerWindow = result.LimitPerWindow == int.MaxValue ? (int?)null : result.LimitPerWindow,
                windowDays = result.WindowDays,
                resetAt = result.ResetAt,
                reason = result.Reason,
            });
        });

        grammar.MapGet("/lessons", async (
            HttpContext http,
            [FromQuery] string? examTypeCode,
            [FromQuery] string? category,
            [FromQuery] string? level,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var query = db.GrammarLessons.Where(l => l.Status == "active");
            if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(l => l.ExamTypeCode == examTypeCode);
            if (!string.IsNullOrEmpty(category)) query = query.Where(l => l.Category == category);
            if (!string.IsNullOrEmpty(level)) query = query.Where(l => l.Level == level);

            var progressByLessonId = await db.LearnerGrammarProgress
                .Where(p => p.UserId == http.UserId())
                .ToDictionaryAsync(p => p.LessonId, ct);

            var lessons = await query.OrderBy(l => l.SortOrder).ToListAsync(ct);
            return Results.Ok(lessons.Select(l => new
            {
                id = l.Id,
                title = l.Title,
                description = l.Description,
                category = l.Category,
                level = l.Level,
                estimatedMinutes = l.EstimatedMinutes,
                sortOrder = l.SortOrder,
                progress = GrammarLessonEndpointHelpers.MapGrammarProgress(progressByLessonId.TryGetValue(l.Id, out var progress) ? progress : null)
            }));
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
                progress = GrammarLessonEndpointHelpers.MapGrammarProgress(progress)
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
            else if (progress.StartedAt is null)
            {
                progress.StartedAt = DateTimeOffset.UtcNow;
                if (progress.Status != "completed")
                {
                    progress.Status = "in_progress";
                }
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { status = progress.Status, progress = GrammarLessonEndpointHelpers.MapGrammarProgress(progress) });
        });

        grammar.MapPost("/lessons/{lessonId}/submit", async (
            HttpContext http,
            string lessonId,
            GrammarSubmitRequest req,
            LearnerDbContext db,
            GamificationService gamification,
            OetLearner.Api.Services.Grammar.IGrammarEntitlementService entitlement,
            CancellationToken ct) =>
        {
            var lesson = await db.GrammarLessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct);
            if (lesson == null || lesson.Status != "active") return Results.NotFound(new { error = "NOT_FOUND" });

            var progress = await db.LearnerGrammarProgress.FirstOrDefaultAsync(p => p.UserId == http.UserId() && p.LessonId == lessonId, ct);
            var wasCompleted = progress?.Status == "completed";

            // Enforce free-tier entitlement — re-attempts of already-completed
            // lessons never cost quota; only new completions do.
            if (!wasCompleted)
            {
                var entitlementCheck = await entitlement.CheckAsync(http.UserId(), ct);
                if (!entitlementCheck.Allowed)
                {
                    return Results.Json(new
                    {
                        errorCode = "grammar_quota_exceeded",
                        error = entitlementCheck.Reason,
                        tier = entitlementCheck.Tier,
                        remaining = entitlementCheck.Remaining,
                        limitPerWindow = entitlementCheck.LimitPerWindow,
                        resetAt = entitlementCheck.ResetAt,
                    }, statusCode: 402);
                }
            }

            if (progress == null)
            {
                progress = new LearnerGrammarProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = http.UserId(),
                    LessonId = lessonId,
                    Status = "in_progress",
                    StartedAt = DateTimeOffset.UtcNow
                };
                db.LearnerGrammarProgress.Add(progress);
            }
            else if (progress.StartedAt is null)
            {
                progress.StartedAt = DateTimeOffset.UtcNow;
            }

            var graded = await GrammarLessonEndpointHelpers.GradeGrammarAttemptAsync(lesson, req.AnswersJson, db, http.UserId(), ct);
            var xpAwarded = wasCompleted ? 0 : graded.XpAwarded;

            progress.Status = "completed";
            progress.ExerciseScore = progress.ExerciseScore is null ? graded.Score : Math.Max(progress.ExerciseScore.Value, graded.Score);
            progress.AnswersJson = graded.SerializedAnswersJson;
            progress.CompletedAt = DateTimeOffset.UtcNow;

            await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, http.UserId(), ct);
            await db.SaveChangesAsync(ct);

            await gamification.RecordActivityAsync(http.UserId(), ct);
            if (xpAwarded > 0)
            {
                await gamification.AwardXpAsync(http.UserId(), xpAwarded, $"Grammar lesson: {lesson.Title}", ct);
            }

            return Results.Ok(new
            {
                lessonId = lesson.Id,
                score = graded.Score,
                masteryScore = graded.MasteryScore,
                correctCount = graded.CorrectCount,
                incorrectCount = graded.IncorrectCount,
                mastered = graded.Mastered,
                xpAwarded,
                reviewItemsCreated = graded.ReviewItemsCreated,
                exercises = graded.Exercises.Select(exercise => new
                {
                    exerciseId = exercise.ExerciseId,
                    isCorrect = exercise.IsCorrect,
                    pointsAwarded = exercise.PointsAwarded,
                    pointsPossible = exercise.PointsPossible,
                    userAnswer = exercise.UserAnswer,
                    correctAnswer = exercise.CorrectAnswer,
                    explanationMarkdown = exercise.ExplanationMarkdown,
                    reviewItemCreated = exercise.ReviewItemCreated
                }),
                progress = GrammarLessonEndpointHelpers.MapGrammarProgress(progress)
            });
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
            return Results.Ok(new { status = "completed", score = req.Score, progress = GrammarLessonEndpointHelpers.MapGrammarProgress(progress) });
        });

        // ── Video Lessons ─────────────────────────────────────────────────
        var lessons = v1.MapGroup("/lessons");
        static IResult FeatureDisabled(string featureName) => Results.NotFound(new { code = "FEATURE_DISABLED", message = $"{featureName} are not enabled." });

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
                return FeatureDisabled("Video lessons");
            }

            return Results.Ok(await service.ListLessonsAsync(http.UserId(), examTypeCode ?? "oet", subtestCode, category, ct));
        });

        lessons.MapGet("/programs/{programId}", async (HttpContext http, string programId, VideoLessonService service, CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled("Video lessons");
            }

            var program = await service.GetProgramAsync(http.UserId(), programId, ct);
            return program is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(program);
        });

        lessons.MapGet("/{lessonId}", async (HttpContext http, string lessonId, VideoLessonService service, CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled("Video lessons");
            }

            var lesson = await service.GetLessonAsync(http.UserId(), lessonId, ct);
            return lesson is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(lesson);
        });

        lessons.MapPost("/{lessonId}/progress", async (HttpContext http, string lessonId, VideoProgressRequest req, VideoLessonService service, CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled("Video lessons");
            }

            var progress = await service.UpdateProgressAsync(http.UserId(), lessonId, req.WatchedSeconds, ct);
            return progress is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(progress);
        });

        // ── Strategy Guides ───────────────────────────────────────────────
        var strategies = v1.MapGroup("/strategies");

        strategies.MapGet("/", async (
            HttpContext http,
            [FromQuery] string? examTypeCode,
            [FromQuery] string? subtestCode,
            [FromQuery] string? category,
            [FromQuery] string? q,
            [FromQuery] bool? recommended,
            StrategyGuideService service,
            CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled("Strategy guides");
            }

            return Results.Ok(await service.ListGuidesAsync(
                http.UserId(),
                examTypeCode ?? "oet",
                subtestCode,
                category,
                q,
                recommended == true,
                ct));
        })
        .WithName("ListStrategyGuides")
        .WithSummary("Lists searchable, access-aware learner strategy guides.");

        strategies.MapGet("/{guideId}", async (HttpContext http, string guideId, StrategyGuideService service, CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled("Strategy guides");
            }

            var guide = await service.GetGuideAsync(http.UserId(), guideId, ct);
            return guide is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(guide);
        })
        .WithName("GetStrategyGuide")
        .WithSummary("Gets a strategy guide with progress, related guides, and navigation hints.");

        strategies.MapPost("/{guideId}/progress", async (
            HttpContext http,
            string guideId,
            StrategyGuideProgressRequest req,
            StrategyGuideService service,
            CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled("Strategy guides");
            }

            var progress = await service.UpdateProgressAsync(http.UserId(), guideId, req.ReadPercent, ct);
            return progress is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(progress);
        })
        .WithName("UpdateStrategyGuideProgress")
        .WithSummary("Updates learner read progress for a strategy guide.");

        strategies.MapPost("/{guideId}/bookmark", async (
            HttpContext http,
            string guideId,
            StrategyGuideBookmarkRequest req,
            StrategyGuideService service,
            CancellationToken ct) =>
        {
            if (!await service.IsEnabledAsync(ct))
            {
                return FeatureDisabled("Strategy guides");
            }

            var progress = await service.SetBookmarkAsync(http.UserId(), guideId, req.Bookmarked, ct);
            return progress is null ? Results.NotFound(new { error = "NOT_FOUND" }) : Results.Ok(progress);
        })
        .WithName("SetStrategyGuideBookmark")
        .WithSummary("Sets or clears a learner strategy guide bookmark.");

        // Pronunciation drills are already mapped in PronunciationEndpoints — removed duplicate

        return app;
    }
}

public record GrammarCompletionRequest(int Score, string AnswersJson);

public record GrammarSubmitRequest(string AnswersJson);

public record LearnerFeatureFlagResponse(string Key, bool Enabled);

file static class LearningContentHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

file sealed record GrammarLessonDocument
{
    public string? TopicId { get; init; }
    public string? Category { get; init; }
    public string? SourceProvenance { get; init; }
    public IReadOnlyList<string>? PrerequisiteLessonIds { get; init; }
    public IReadOnlyList<GrammarContentBlockDocument>? ContentBlocks { get; init; }
    public IReadOnlyList<GrammarExerciseDocument>? Exercises { get; init; }
    public int Version { get; init; } = 1;
    public string? UpdatedAt { get; init; }
}

file sealed record GrammarContentBlockDocument
{
    public string? Id { get; init; }
    public int SortOrder { get; init; }
    public string? Type { get; init; }
    public string? ContentMarkdown { get; init; }
}

file sealed record GrammarExerciseDocument
{
    public string? Id { get; init; }
    public int SortOrder { get; init; }
    public string? Type { get; init; }
    public string? PromptMarkdown { get; init; }
    public JsonElement Options { get; init; }
    public JsonElement CorrectAnswer { get; init; }
    public IReadOnlyList<string>? AcceptedAnswers { get; init; }
    public string? ExplanationMarkdown { get; init; }
    public string? Difficulty { get; init; }
    public int Points { get; init; } = 1;
}

file sealed record GrammarExerciseGrade(
    string ExerciseId,
    bool IsCorrect,
    int PointsAwarded,
    int PointsPossible,
    JsonElement? UserAnswer,
    JsonElement? CorrectAnswer,
    string? ExplanationMarkdown,
    bool ReviewItemCreated);

file sealed record GrammarAttemptGrade(
    IReadOnlyList<GrammarExerciseGrade> Exercises,
    string SerializedAnswersJson,
    int Score,
    int MasteryScore,
    int CorrectCount,
    int IncorrectCount,
    bool Mastered,
    int XpAwarded,
    int ReviewItemsCreated);

file static class GrammarLessonEndpointHelpers
{
internal static object? MapGrammarProgress(LearnerGrammarProgress? progress)
    => progress == null
        ? null
        : new
        {
            status = progress.Status,
            score = progress.ExerciseScore,
            exerciseScore = progress.ExerciseScore,
            masteryScore = progress.ExerciseScore,
            startedAt = progress.StartedAt,
            completedAt = progress.CompletedAt
        };

internal static async Task<GrammarAttemptGrade> GradeGrammarAttemptAsync(
    GrammarLesson lesson,
    string answersJson,
    LearnerDbContext db,
    string userId,
    CancellationToken ct)
{
    var document = ParseGrammarLessonDocument(lesson);
    var exercises = GetGrammarExercises(lesson, document);
    var submittedAnswers = JsonSupport.Deserialize<Dictionary<string, JsonElement>>(answersJson, new Dictionary<string, JsonElement>());
    var serializedAnswersJson = JsonSupport.Serialize(submittedAnswers);

    var gradedExercises = new List<GrammarExerciseGrade>(exercises.Count);
    var totalPointsPossible = 0;
    var totalPointsAwarded = 0;
    var correctCount = 0;
    var incorrectCount = 0;
    var reviewItemsCreated = 0;

    foreach (var exercise in exercises)
    {
        var exerciseId = GetGrammarExerciseId(exercise);
        var submittedAnswer = submittedAnswers.TryGetValue(exerciseId, out var answer) ? answer : (JsonElement?)null;
        var result = GradeGrammarExercise(exercise, submittedAnswer);

        gradedExercises.Add(result);
        totalPointsPossible += result.PointsPossible;
        totalPointsAwarded += result.PointsAwarded;

        if (result.IsCorrect)
        {
            correctCount++;
            continue;
        }

        incorrectCount++;

        if (!await db.ReviewItems.AnyAsync(r => r.UserId == userId && r.SourceType == "grammar_error" && r.SourceId == $"{lesson.Id}:{exerciseId}", ct))
        {
            db.ReviewItems.Add(new ReviewItem
            {
                Id = $"ri-{Guid.NewGuid():N}",
                UserId = userId,
                ExamTypeCode = lesson.ExamTypeCode,
                SourceType = "grammar_error",
                SourceId = $"{lesson.Id}:{exerciseId}",
                SubtestCode = "grammar",
                CriterionCode = NormalizeGrammarExerciseType(exercise.Type),
                QuestionJson = JsonSupport.Serialize(new
                {
                    text = exercise.PromptMarkdown ?? string.Empty,
                    lessonId = lesson.Id,
                    exerciseId
                }),
                AnswerJson = JsonSupport.Serialize(new
                {
                    text = FormatGrammarExerciseAnswer(exercise),
                    explanation = exercise.ExplanationMarkdown ?? string.Empty
                }),
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
                CreatedAt = DateTimeOffset.UtcNow,
                Status = "active"
            });
            reviewItemsCreated++;
            result = result with { ReviewItemCreated = true };
            gradedExercises[^1] = result;
        }
    }

    var score = totalPointsPossible > 0
        ? (int)Math.Round(totalPointsAwarded * 100.0 / totalPointsPossible, MidpointRounding.AwayFromZero)
        : 0;
    var masteryScore = score;
    var mastered = masteryScore >= 80;
    var xpAwarded = exercises.Count == 0
        ? 0
        : 10 + (correctCount * 2) + (mastered ? 10 : 0);

    return new GrammarAttemptGrade(
        gradedExercises,
        serializedAnswersJson,
        score,
        masteryScore,
        correctCount,
        incorrectCount,
        mastered,
        xpAwarded,
        reviewItemsCreated);
}

internal static GrammarLessonDocument ParseGrammarLessonDocument(GrammarLesson lesson)
{
    var document = JsonSupport.Deserialize<GrammarLessonDocument>(lesson.ContentHtml, new GrammarLessonDocument());
    return document with
    {
        TopicId = string.IsNullOrWhiteSpace(document.TopicId) ? lesson.Category : document.TopicId,
        Category = string.IsNullOrWhiteSpace(document.Category) ? lesson.Category : document.Category,
        SourceProvenance = document.SourceProvenance,
        PrerequisiteLessonIds = document.PrerequisiteLessonIds ?? [],
        ContentBlocks = document.ContentBlocks ?? [],
        Exercises = document.Exercises ?? [],
        UpdatedAt = document.UpdatedAt
    };
}

internal static List<GrammarExerciseDocument> GetGrammarExercises(GrammarLesson lesson, GrammarLessonDocument document)
{
    var exercises = document.Exercises is { Count: > 0 }
        ? document.Exercises.ToList()
        : JsonSupport.Deserialize<List<GrammarExerciseDocument>>(lesson.ExercisesJson, []);

    return exercises
        .OrderBy(exercise => exercise.SortOrder)
        .ToList();
}

internal static GrammarExerciseGrade GradeGrammarExercise(
    GrammarExerciseDocument exercise,
    JsonElement? submittedAnswer)
{
    var exerciseType = NormalizeGrammarExerciseType(exercise.Type);
    var pointsPossible = Math.Max(1, exercise.Points);
    var correctAnswer = exercise.CorrectAnswer.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : exercise.CorrectAnswer;

    if (exerciseType == "matching")
    {
        var correctPairs = ReadGrammarMatchingPairs(correctAnswer);
        var userPairs = ReadGrammarMatchingPairs(submittedAnswer);
        var matchedPairs = correctPairs.Count == 0
            ? 0
            : correctPairs.Count(pair => userPairs.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));
        var isCorrect = correctPairs.Count > 0 && matchedPairs == correctPairs.Count && userPairs.Count == correctPairs.Count;
        var pointsAwarded = isCorrect
            ? pointsPossible
            : (int)Math.Round(pointsPossible * (correctPairs.Count == 0 ? 0 : matchedPairs / (double)correctPairs.Count), MidpointRounding.AwayFromZero);

        return new GrammarExerciseGrade(
            GetGrammarExerciseId(exercise),
            isCorrect,
            Math.Max(0, Math.Min(pointsPossible, pointsAwarded)),
            pointsPossible,
            submittedAnswer,
            correctAnswer,
            exercise.ExplanationMarkdown,
            false);
    }

    var userAnswer = ReadGrammarScalarAnswer(submittedAnswer);
    var acceptableAnswers = new List<string>();
    acceptableAnswers.AddRange(ReadGrammarTextVariants(correctAnswer));
    acceptableAnswers.AddRange(exercise.AcceptedAnswers ?? []);
    var isCorrectText = !string.IsNullOrWhiteSpace(userAnswer)
        && acceptableAnswers.Any(answer => string.Equals(NormalizeGrammarAnswerText(answer), NormalizeGrammarAnswerText(userAnswer), StringComparison.OrdinalIgnoreCase));

    return new GrammarExerciseGrade(
        GetGrammarExerciseId(exercise),
        isCorrectText,
        isCorrectText ? pointsPossible : 0,
        pointsPossible,
        submittedAnswer,
        correctAnswer,
        exercise.ExplanationMarkdown,
        false);
}

internal static string GetGrammarExerciseId(GrammarExerciseDocument exercise)
    => !string.IsNullOrWhiteSpace(exercise.Id) ? exercise.Id : $"exercise-{exercise.SortOrder}";

internal static string NormalizeGrammarExerciseType(string? value)
{
    var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
    return normalized is "mcq" or "fill_blank" or "error_correction" or "sentence_transformation" or "matching"
        ? normalized
        : "fill_blank";
}

internal static string NormalizeGrammarAnswerText(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), "\\s+", " ");
    return normalized.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
}

internal static string? ReadGrammarScalarAnswer(JsonElement? value)
{
    if (value is null)
    {
        return null;
    }

    return value.Value.ValueKind switch
    {
        JsonValueKind.String => value.Value.GetString(),
        JsonValueKind.Number => value.Value.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.Value.GetRawText()
    };
}

internal static IReadOnlyList<string> ReadGrammarTextVariants(JsonElement? value)
{
    if (value is null)
    {
        return [];
    }

    return value.Value.ValueKind switch
    {
        JsonValueKind.String => [value.Value.GetString() ?? string.Empty],
        JsonValueKind.Number => [value.Value.ToString()],
        JsonValueKind.True => ["true"],
        JsonValueKind.False => ["false"],
        JsonValueKind.Array => value.Value.EnumerateArray().SelectMany(item => ReadGrammarTextVariants(item)).ToArray(),
        JsonValueKind.Object when value.Value.TryGetProperty("text", out var text) => ReadGrammarTextVariants(text),
        JsonValueKind.Object when value.Value.TryGetProperty("label", out var label) => ReadGrammarTextVariants(label),
        _ => []
    };
}

internal static Dictionary<string, string> ReadGrammarMatchingPairs(JsonElement? value)
{
    var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (value is null)
    {
        return pairs;
    }

    switch (value.Value.ValueKind)
    {
        case JsonValueKind.Array:
            foreach (var item in value.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("left", out var left) || !item.TryGetProperty("right", out var right)) continue;
                var leftText = ReadGrammarScalarAnswer(left);
                var rightText = ReadGrammarScalarAnswer(right);
                if (string.IsNullOrWhiteSpace(leftText) || string.IsNullOrWhiteSpace(rightText)) continue;
                pairs[NormalizeGrammarAnswerText(leftText)] = NormalizeGrammarAnswerText(rightText);
            }
            break;
        case JsonValueKind.Object:
            foreach (var property in value.Value.EnumerateObject())
            {
                var rightText = ReadGrammarScalarAnswer(property.Value);
                if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(rightText)) continue;
                pairs[NormalizeGrammarAnswerText(property.Name)] = NormalizeGrammarAnswerText(rightText);
            }
            break;
    }

    return pairs;
}

internal static string FormatGrammarExerciseAnswer(GrammarExerciseDocument exercise)
{
    var exerciseType = NormalizeGrammarExerciseType(exercise.Type);
    var rawCorrectAnswer = exercise.CorrectAnswer.ValueKind == JsonValueKind.Undefined
        ? string.Empty
        : exercise.CorrectAnswer.GetRawText();

    if (exerciseType == "matching")
    {
        var pairs = ReadGrammarMatchingPairs(exercise.CorrectAnswer.ValueKind == JsonValueKind.Undefined ? null : exercise.CorrectAnswer);
        return pairs.Count == 0
            ? rawCorrectAnswer
            : string.Join("\n", pairs.Select(pair => $"{pair.Key} → {pair.Value}"));
    }

    var answers = ReadGrammarTextVariants(exercise.CorrectAnswer.ValueKind == JsonValueKind.Undefined ? null : exercise.CorrectAnswer);
    return answers.Count > 0 ? string.Join(" / ", answers) : rawCorrectAnswer;
}
}
