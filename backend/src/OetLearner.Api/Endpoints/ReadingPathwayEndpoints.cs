using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Endpoints;

public static class ReadingPathwayEndpoints
{
    public static IEndpointRouteBuilder MapReadingPathwayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/reading-pathway")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        // ── §23.1 Pathway ─────────────────────────────────────────────────────
        group.MapGet("/profile", async (HttpContext http, IReadingLearnerPathwayService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var stage = await svc.GetCurrentStageAsync(userId, ct);
            return Results.Ok(stage);
        });

        group.MapPost("/onboarding", async (
            Contracts.StartOnboardingRequest request,
            HttpContext http,
            IReadingLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var profile = await svc.StartOnboardingAsync(userId,
                new Services.Reading.StartOnboardingRequest(
                    request.TargetBand, request.ExamDate, request.HoursPerWeek,
                    request.Profession, request.HasTakenBefore, request.PreviousScore,
                    request.SelfRatedSpeed, request.SelfRatedVocabulary), ct);
            return Results.Ok(profile);
        });

        group.MapPost("/diagnostic/start", async (
            HttpContext http,
            IReadingLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var result = await svc.StartDiagnosticAsync(userId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/diagnostic/submit", async (
            SubmitDiagnosticRequest request,
            HttpContext http,
            IReadingLearnerPathwayService svc,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var result = await svc.SubmitDiagnosticAsync(userId, request.SessionId, request.Answers, ct);
            return Results.Ok(result);
        });

        group.MapGet("/pathway", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var pathway = await db.LearnerReadingPathways
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            return pathway is null ? Results.NotFound() : Results.Ok(pathway);
        });

        group.MapGet("/stage", async (HttpContext http, IReadingLearnerPathwayService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            return Results.Ok(await svc.GetCurrentStageAsync(userId, ct));
        });

        // ── §23.2 Daily plan ──────────────────────────────────────────────────
        group.MapGet("/plan/today", async (HttpContext http, IDailyPlanService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            await svc.GenerateTodayAsync(userId, ct);
            return Results.Ok(await svc.GetTodayPlanAsync(userId, ct));
        });

        group.MapPost("/plan/items/{id}/start", async (
            Guid id, HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var item = await db.ReadingDailyPlanItems.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);
            if (item is null) return Results.NotFound();
            item.Status = "in_progress";
            item.StartedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/plan/items/{id}/complete", async (
            Guid id, HttpContext http, IDailyPlanService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            await svc.MarkItemCompletedAsync(id, userId, ct);
            return Results.NoContent();
        });

        group.MapPost("/plan/items/{id}/skip", async (
            Guid id, HttpContext http, IDailyPlanService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            await svc.SkipItemAsync(id, userId, "user_skip", ct);
            return Results.NoContent();
        });

        // ── §23.3 Practice sessions ───────────────────────────────────────────
        group.MapPost("/practice/sessions", async (
            StartPracticeSessionRequest request,
            HttpContext http,
            IPracticeSelectionService selection,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");

            // Estimate question count from target minutes (~1.2 q/min, minimum 5).
            int targetCount = Math.Max(5, (int)Math.Round(request.TargetMinutes * 1.2));

            string questionIdsJson;
            int questionCount;

            if (request.SessionType == "mock" && request.MockTemplateId.HasValue)
            {
                var ids = await selection.SelectMockQuestionsAsync(userId, request.MockTemplateId.Value, ct);
                questionIdsJson = JsonSerializer.Serialize(ids);
                questionCount = ids.Count;
            }
            else if (request.SessionType == "wrong_review")
            {
                var ids = await selection.SelectWrongAnswerReviewQueueAsync(userId, 10, ct);
                questionIdsJson = JsonSerializer.Serialize(ids);
                questionCount = ids.Count;
            }
            else
            {
                // Drill — returns string IDs from ReadingQuestion.Id
                var ids = await selection.SelectQuestionsForDrillAsync(
                    userId, request.FocusSkill ?? "S1", targetCount, ct);
                questionIdsJson = JsonSerializer.Serialize(ids);
                questionCount = ids.Count;
            }

            var session = new ReadingPracticeSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SessionType = request.SessionType,
                FocusSkill = request.FocusSkill,
                QuestionIdsJson = questionIdsJson,
                TotalQuestions = questionCount,
                StartedAt = DateTimeOffset.UtcNow,
            };
            db.ReadingPracticeSessions.Add(session);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { sessionId = session.Id, questionCount, sessionType = request.SessionType });
        });

        group.MapPost("/practice/sessions/{sessionId}/answers", async (
            Guid sessionId,
            SubmitAnswerRequest request,
            HttpContext http,
            ISkillScoringService scoring,
            IXpService xp,
            IStreakService streak,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");

            // ReadingQuestion.Id is a string PK — convert Guid to string for lookup
            var questionStringId = request.QuestionId.ToString();
            var question = await db.ReadingQuestions.AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == questionStringId, ct);
            if (question is null) return Results.NotFound();

            // Skip duplicate attempts within the same session
            var existing = await db.ReadingQuestionAttempts
                .FirstOrDefaultAsync(a => a.UserId == userId
                    && a.ReadingQuestionId == request.QuestionId
                    && a.PracticeSessionId == sessionId, ct);

            bool isCorrect = CheckAnswer(question, request.SelectedOption);

            if (existing is null)
            {
                var attempt = new ReadingQuestionAttempt
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ReadingQuestionId = request.QuestionId,
                    PracticeSessionId = sessionId,
                    SelectedOption = request.SelectedOption,
                    IsCorrect = isCorrect,
                    TimeSpentSeconds = request.TimeSpentSeconds,
                    AttemptedAt = DateTimeOffset.UtcNow,
                    InReviewQueue = !isCorrect,
                    NextReviewAt = isCorrect ? null : DateTimeOffset.UtcNow.AddDays(1),
                };
                db.ReadingQuestionAttempts.Add(attempt);
                await db.SaveChangesAsync(ct);

                await xp.AwardXpAsync(userId, isCorrect ? XpAmounts.PerCorrectAnswer : XpAmounts.PerQuestionAnswered, "question_answered", ct);
                await streak.RecordActivityAsync(userId, 1, ct);
            }

            // Surface pre-authored explanation (no AI on hot path)
            string? explanationMarkdown = string.IsNullOrEmpty(question.ExplanationMarkdown)
                ? null
                : question.ExplanationMarkdown;

            return Results.Ok(new { isCorrect, explanation = explanationMarkdown });
        });

        group.MapPost("/practice/sessions/{sessionId}/submit", async (
            Guid sessionId,
            HttpContext http,
            ISkillScoringService scoring,
            IXpService xp,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var session = await db.ReadingPracticeSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (session is null) return Results.NotFound();

            session.CompletedAt = DateTimeOffset.UtcNow;
            session.DurationSeconds = (int)(DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds;

            var attempts = await db.ReadingQuestionAttempts
                .Where(a => a.PracticeSessionId == sessionId && a.UserId == userId).CountAsync(ct);
            var correct = await db.ReadingQuestionAttempts
                .Where(a => a.PracticeSessionId == sessionId && a.UserId == userId && a.IsCorrect).CountAsync(ct);

            session.Score = correct;
            session.TotalQuestions = attempts;
            await db.SaveChangesAsync(ct);

            await scoring.UpdateSkillScoresAsync(userId, sessionId, ct);
            if (session.SessionType == "mock")
                await xp.AwardXpAsync(userId, XpAmounts.PerMockCompleted, "session_complete", ct);

            return Results.Ok(new { score = correct, totalQuestions = attempts, sessionId });
        });

        group.MapGet("/practice/sessions/{sessionId}", async (
            Guid sessionId, HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var session = await db.ReadingPracticeSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        group.MapGet("/questions/{questionId}/explanation", async (
            string questionId,
            string? wrongOption,
            string? language,
            HttpContext http,
            IReadingExplanationService explanationSvc,
            CancellationToken ct) =>
        {
            var _ = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var explanation = await explanationSvc.GetExplanationAsync(
                questionId, wrongOption ?? "", language ?? "en", ct);
            return Results.Ok(explanation);
        });

        // ── §23.4 Mocks ───────────────────────────────────────────────────────
        group.MapGet("/mocks", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var mocks = await db.ReadingMockTemplates.AsNoTracking()
                .Where(m => m.IsPublished).ToListAsync(ct);
            return Results.Ok(mocks);
        });

        group.MapPost("/mocks/start", async (
            StartPracticeSessionRequest request,
            HttpContext http,
            IPracticeSelectionService selection,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            if (!request.MockTemplateId.HasValue)
                return Results.BadRequest(new { code = "mock_template_required", error = "mockTemplateId required", message = "mockTemplateId required" });

            // Returns List<Guid> (ReadingMockTemplate question IDs)
            var questionIds = await selection.SelectMockQuestionsAsync(userId, request.MockTemplateId.Value, ct);
            var session = new ReadingPracticeSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SessionType = "mock",
                QuestionIdsJson = JsonSerializer.Serialize(questionIds),
                TotalQuestions = questionIds.Count,
                StartedAt = DateTimeOffset.UtcNow,
            };
            db.ReadingPracticeSessions.Add(session);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { sessionId = session.Id, questionCount = questionIds.Count, timeLimitMinutes = 60 });
        });

        group.MapGet("/mocks/sessions/{sessionId}/results", async (
            Guid sessionId, HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var session = await db.ReadingPracticeSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (session is null) return Results.NotFound();

            var attempts = await db.ReadingQuestionAttempts.AsNoTracking()
                .Where(a => a.PracticeSessionId == sessionId && a.UserId == userId)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                session.Score,
                session.TotalQuestions,
                session.DurationSeconds,
                ScaledScore = EstimateScaled(session.Score ?? 0, session.TotalQuestions ?? 42),
                Attempts = attempts,
            });
        });

        // ── §23.5 Lessons + Strategies ────────────────────────────────────────
        group.MapGet("/lessons", async (ILessonService lessonSvc, CancellationToken ct) =>
            Results.Ok(await lessonSvc.GetLessonsAsync(ct)));

        group.MapGet("/lessons/{slug}", async (
            string slug, HttpContext http, ILessonService lessonSvc,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var lesson = await lessonSvc.GetLessonAsync(slug, ct);
            if (lesson is null) return Results.NotFound();
            var progress = await db.LearnerLessonProgresses
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lesson.Id, ct);
            return Results.Ok(new { lesson, progress });
        });

        group.MapPost("/lessons/{slug}/progress", async (
            string slug,
            LessonProgressRequest request,
            HttpContext http,
            ILessonService lessonSvc,
            LearnerDbContext db,
            IXpService xp,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var lesson = await db.ReadingLessons.FirstOrDefaultAsync(l => l.Slug == slug, ct);
            if (lesson is null) return Results.NotFound();

            var progress = await lessonSvc.UpdateProgressAsync(userId, lesson.Id,
                new LessonProgressUpdate(
                    request.VideoWatched, request.BodyRead,
                    request.Drill1Completed, request.Drill2Completed, request.Drill3Completed,
                    request.QuizScore), ct);

            if (progress.CompletedAt.HasValue)
                await xp.AwardXpAsync(userId, XpAmounts.PerLessonCompleted, "lesson_completed", ct);

            return Results.Ok(progress);
        });

        group.MapGet("/strategies", async (
            string? category, string? stage, IStrategyService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetStrategiesAsync(category, stage, ct)));

        group.MapGet("/strategies/{slug}", async (
            string slug, HttpContext http, IStrategyService svc, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var strategy = await svc.GetStrategyAsync(slug, ct);
            if (strategy is null) return Results.NotFound();
            var progress = await svc.GetProgressAsync(userId, strategy.Id, ct);
            return Results.Ok(new { strategy, progress });
        });

        group.MapPost("/strategies/{slug}/mark-read", async (
            string slug, HttpContext http, IStrategyService svc, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var strategy = await db.ReadingStrategies.FirstOrDefaultAsync(s => s.Slug == slug, ct);
            if (strategy is null) return Results.NotFound();
            await svc.MarkReadAsync(userId, strategy.Id, ct);
            return Results.NoContent();
        });

        // ── §23.6 Vocabulary ──────────────────────────────────────────────────
        group.MapGet("/vocab", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var items = await db.LearnerVocabularyItems
                .AsNoTracking()
                .Where(v => v.UserId == userId)
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        group.MapPost("/vocab", async (
            VocabAddRequest request, HttpContext http, IReadingVocabularyService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var item = await svc.AddWordAsync(userId, request.Word, request.Source, ct);
            return Results.Ok(item);
        });

        group.MapGet("/vocab/due", async (HttpContext http, IReadingVocabularyService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var due = await svc.GetDueForReviewAsync(userId, ct);
            return Results.Ok(due);
        });

        group.MapPost("/vocab/{itemId}/review", async (
            Guid itemId, VocabReviewRequest request, HttpContext http, IReadingVocabularyService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var updated = await svc.SubmitReviewAsync(itemId, userId, request.Quality, ct);
            return Results.Ok(updated);
        });

        group.MapDelete("/vocab/{itemId}", async (
            Guid itemId, HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var item = await db.LearnerVocabularyItems
                .FirstOrDefaultAsync(v => v.Id == itemId && v.UserId == userId, ct);
            if (item is null) return Results.NotFound();
            db.LearnerVocabularyItems.Remove(item);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/vocab/lists", async (LearnerDbContext db, CancellationToken ct) =>
            Results.Ok(await db.VocabularyLists.AsNoTracking().Where(l => l.IsPublished).ToListAsync(ct)));

        group.MapPost("/vocab/lists/{listId}/subscribe", async (
            Guid listId, HttpContext http, IReadingVocabularyService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            await svc.SubscribeToListAsync(userId, listId, ct);
            return Results.NoContent();
        });

        group.MapGet("/vocab/stats", async (HttpContext http, IReadingVocabularyService svc, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            return Results.Ok(await svc.GetStatsAsync(userId, ct));
        });

        // ── §23.7 Analytics ───────────────────────────────────────────────────
        group.MapGet("/stats/dashboard", async (
            HttpContext http,
            ISkillScoringService scoring,
            IStreakService streak,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var skillScores = await scoring.GetCurrentScoresAsync(userId, ct);
            var streakStatus = await streak.GetStreakStatusAsync(userId, ct);
            var profile = await db.LearnerReadingProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            return Results.Ok(new
            {
                skillScores,
                streakStatus,
                predictedScore = profile?.PredictedScore,
                readinessScore = profile?.CurrentReadinessScore,
            });
        });

        group.MapGet("/stats/skills", async (
            HttpContext http, ISkillScoringService scoring, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var scores = await scoring.GetCurrentScoresAsync(userId, ct);
            var baselines = await db.LearnerSkillScores.AsNoTracking()
                .Where(s => s.UserId == userId)
                .ToDictionaryAsync(s => s.SkillCode, s => s.DiagnosticScore, ct);
            return Results.Ok(new { current = scores, baseline = baselines });
        });

        group.MapGet("/stats/history", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var mocks = await db.ReadingPracticeSessions.AsNoTracking()
                .Where(s => s.UserId == userId && s.SessionType == "mock" && s.CompletedAt != null)
                .OrderBy(s => s.CompletedAt)
                .Select(s => new { s.Id, s.Score, s.TotalQuestions, s.CompletedAt })
                .ToListAsync(ct);
            return Results.Ok(mocks);
        });

        group.MapGet("/stats/readiness", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var profile = await db.LearnerReadingProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            return Results.Ok(profile?.CurrentReadinessScore ?? 0);
        });

        group.MapGet("/stats/calendar", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
            var streaks = await db.StreakRecords.AsNoTracking()
                .Where(s => s.UserId == userId && s.Date >= cutoff)
                .ToListAsync(ct);
            return Results.Ok(streaks);
        });

        // ── §23.8 Community ───────────────────────────────────────────────────
        group.MapGet("/questions/{questionId}/comments", async (
            string questionId, LearnerDbContext db, CancellationToken ct) =>
        {
            // questionId routed as string to match ReadingQuestion.Id PK type
            if (!Guid.TryParse(questionId, out var questionGuid))
                return Results.BadRequest(new { code = "invalid_question_id", error = "questionId must be a valid GUID.", message = "questionId must be a valid GUID." });

            var comments = await db.ReadingQuestionDiscussionComments.AsNoTracking()
                .Where(c => c.ReadingQuestionId == questionGuid && !c.IsHidden)
                .OrderByDescending(c => c.Upvotes)
                .ThenByDescending(c => c.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(comments);
        });

        group.MapPost("/questions/{questionId}/comments", async (
            string questionId,
            PostCommentRequest request,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            if (!Guid.TryParse(questionId, out var questionGuid))
                return Results.BadRequest(new { code = "invalid_question_id", error = "questionId must be a valid GUID.", message = "questionId must be a valid GUID." });

            var comment = new ReadingQuestionDiscussionComment
            {
                Id = Guid.NewGuid(),
                ReadingQuestionId = questionGuid,
                UserId = userId,
                Body = request.Body,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ReadingQuestionDiscussionComments.Add(comment);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/reading-pathway/questions/{questionId}/comments/{comment.Id}", comment);
        });

        group.MapPost("/questions/{questionId}/comments/{commentId}/upvote", async (
            Guid commentId, LearnerDbContext db, CancellationToken ct) =>
        {
            var comment = await db.ReadingQuestionDiscussionComments
                .FirstOrDefaultAsync(c => c.Id == commentId, ct);
            if (comment is null) return Results.NotFound();
            comment.Upvotes++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { upvotes = comment.Upvotes });
        });

        // ── §23.9 AI passage Q&A ──────────────────────────────────────────────
        group.MapPost("/ai/passage-qna", async (
            PassageQnaRequest request,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Stub — real implementation requires AI service integration
            var __ = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            return Results.Ok(new { reply = "AI passage Q&A is being set up. Please try again shortly." });
        });

        return app;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Grades a single answer against a ReadingQuestion.
    /// For MCQ types, compares the selected key against CorrectAnswerJson.
    /// For short-answer types, also consults AcceptedSynonymsJson.
    /// </summary>
    private static bool CheckAnswer(ReadingQuestion q, string selected)
    {
        if (string.IsNullOrWhiteSpace(selected)) return false;

        // MCQ types: CorrectAnswerJson is a quoted option key e.g. "\"A\""
        if (q.QuestionType is ReadingQuestionType.MultipleChoice3 or ReadingQuestionType.MultipleChoice4)
        {
            var correctRaw = q.CorrectAnswerJson.Trim();
            // Strip surrounding quotes if present (stored as JSON string)
            if (correctRaw.StartsWith("\"") && correctRaw.EndsWith("\"") && correctRaw.Length >= 2)
                correctRaw = correctRaw[1..^1];
            return string.Equals(correctRaw, selected, StringComparison.OrdinalIgnoreCase);
        }

        // Short-answer / sentence-completion: check canonical answer then synonyms
        var canonical = q.CorrectAnswerJson.Trim();
        if (canonical.StartsWith("\"") && canonical.EndsWith("\"") && canonical.Length >= 2)
            canonical = canonical[1..^1];

        if (string.Equals(canonical.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(q.AcceptedSynonymsJson))
        {
            try
            {
                var synonyms = JsonSerializer.Deserialize<List<string>>(q.AcceptedSynonymsJson) ?? [];
                return synonyms.Any(s => string.Equals(s.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            catch (JsonException) { /* ignore malformed JSON */ }
        }

        // MatchingTextReference: CorrectAnswerJson may be an array ["1","3"]
        if (q.QuestionType == ReadingQuestionType.MatchingTextReference
            && q.CorrectAnswerJson.TrimStart().StartsWith("["))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<string>>(q.CorrectAnswerJson) ?? [];
                return arr.Any(s => string.Equals(s.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            catch (JsonException) { /* ignore */ }
        }

        return false;
    }

    /// <summary>
    /// Quick heuristic OET scaled score estimate from raw accuracy.
    /// Range 80–500, matching the OET scale.
    /// </summary>
    private static int EstimateScaled(int raw, int total)
    {
        if (total <= 0) return 0;
        var accuracy = (decimal)raw / total;
        return (int)(accuracy * 420 + 80);
    }
}
