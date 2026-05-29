using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
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
        group.MapGet("/profile", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var profile = await db.LearnerReadingProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            return Results.Ok(ToProfileResponse(userId, profile));
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
            return Results.Ok(ToProfileResponse(userId, profile));
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

        group.MapGet("/diagnostic/sessions/{sessionId}/questions", async (
            Guid sessionId,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var session = await db.ReadingPracticeSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.SessionType == "diagnostic", ct);
            if (session is null) return Results.NotFound();

            var questionIds = JsonSerializer.Deserialize<List<string>>(session.QuestionIdsJson) ?? [];
            if (questionIds.Count == 0) return Results.Ok(Array.Empty<DiagnosticQuestionResponse>());

            var questionOrder = questionIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

            var questions = await db.ReadingQuestions.AsNoTracking()
                .Include(q => q.Part)
                .Include(q => q.Text)
                .Where(q => questionIds.Contains(q.Id))
                .ToListAsync(ct);

            var response = questions
                .OrderBy(q => questionOrder.TryGetValue(q.Id, out var index) ? index : int.MaxValue)
                .Select(q => new DiagnosticQuestionResponse(
                    Id: q.Id,
                    PartCode: q.Part?.PartCode.ToString() ?? "",
                    QuestionType: q.QuestionType.ToString(),
                    DisplayOrder: q.DisplayOrder,
                    Stem: q.Stem,
                    Options: ReadingLearnerSafeProjection.ProjectOptionsElement(q.OptionsJson),
                    TextTitle: q.Text?.Title,
                    TextHtml: q.Text?.BodyHtml,
                    SkillCode: q.SkillTag))
                .ToList();

            return Results.Ok(response);
        });

        group.MapPost("/diagnostic/submit", async (
            SubmitDiagnosticRequest request,
            HttpContext http,
            IReadingLearnerPathwayService svc,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var diagnosticSession = await db.ReadingPracticeSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.UserId == userId && s.SessionType == "diagnostic", ct);
            if (diagnosticSession is null) return Results.NotFound();
            if (diagnosticSession.CompletedAt is not null)
                return Results.BadRequest(new { code = "diagnostic_already_submitted", error = "Diagnostic already submitted.", message = "Diagnostic already submitted." });

            var result = await svc.SubmitDiagnosticAsync(userId, request.SessionId, request.Answers, ct);
            var session = await db.ReadingPracticeSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.UserId == userId && s.SessionType == "diagnostic", ct);
            var roadmapWeeks = await GetRoadmapWeeksAsync(userId, db, ct);
            return Results.Ok(new DiagnosticResultResponse(
                SessionId: request.SessionId,
                Score: result.Score,
                TotalQuestions: result.TotalQuestions,
                SkillScores: result.SkillScores,
                EstimatedOetBand: result.EstimatedOetBand,
                EstimatedScaledScore: result.EstimatedScaledScore ?? 0,
                DurationSeconds: session?.DurationSeconds,
                RoadmapWeeks: roadmapWeeks,
                CompletedAt: session?.CompletedAt));
        });

        group.MapGet("/diagnostic/sessions/{sessionId}/results", async (
            Guid sessionId,
            HttpContext http,
            ISkillScoringService scoring,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var session = await db.ReadingPracticeSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.SessionType == "diagnostic", ct);
            if (session is null || session.CompletedAt is null) return Results.NotFound();

            var score = session.Score ?? 0;
            var total = session.TotalQuestions ?? 0;
            var scaled = EstimateScaled(score, total);
            var roadmapWeeks = await GetRoadmapWeeksAsync(userId, db, ct);
            return Results.Ok(new DiagnosticResultResponse(
                SessionId: sessionId,
                Score: score,
                TotalQuestions: total,
                SkillScores: await scoring.GetCurrentScoresAsync(userId, ct),
                EstimatedOetBand: OetScoring.OetGradeLetterFromScaled(scaled),
                EstimatedScaledScore: scaled,
                DurationSeconds: session.DurationSeconds,
                RoadmapWeeks: roadmapWeeks,
                CompletedAt: session.CompletedAt));
        });

        group.MapGet("/pathway", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var profile = await db.LearnerReadingProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            var pathway = await db.LearnerReadingPathways
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            return Results.Ok(ToPathwayResponse(profile, pathway));
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

            if (request.SessionType is not ("drill" or "wrong_review" or "mock"))
                return Results.BadRequest(new { code = "invalid_session_type", error = "Unsupported Reading practice session type.", message = "Unsupported Reading practice session type." });

            // Estimate question count from target minutes (~1.2 q/min, minimum 5).
            int targetCount = Math.Max(5, (int)Math.Round(request.TargetMinutes * 1.2));

            string questionIdsJson;
            int questionCount;

            if (request.SessionType == "mock" && request.MockTemplateId.HasValue)
            {
                var ids = await selection.SelectMockQuestionsAsync(userId, request.MockTemplateId.Value, ct);
                if (ids.Count == 0)
                    return Results.BadRequest(new { code = "mock_template_unavailable", error = "Mock template is not available.", message = "Mock template is not available." });
                questionIdsJson = JsonSerializer.Serialize(ids);
                questionCount = ids.Count;
            }
            else if (request.SessionType == "mock")
            {
                return Results.BadRequest(new { code = "mock_template_required", error = "mockTemplateId required", message = "mockTemplateId required" });
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

            var session = await db.ReadingPracticeSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (session is null) return Results.NotFound();
            if (session.SessionType is "diagnostic" or "mock")
                return Results.BadRequest(new { code = "session_answers_locked", error = "This session type does not reveal per-question correctness.", message = "This session type does not reveal per-question correctness." });
            if (session.CompletedAt is not null)
                return Results.BadRequest(new { code = "session_already_submitted", error = "Session already submitted.", message = "Session already submitted." });

            var sessionQuestionIds = JsonSerializer.Deserialize<List<string>>(session.QuestionIdsJson) ?? [];
            if (sessionQuestionIds.Count > 0 && !sessionQuestionIds.Contains(request.QuestionId, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { code = "question_not_in_session", error = "Question is not part of this session.", message = "Question is not part of this session." });

            // ReadingQuestion.Id is a string PK in the canonical authoring schema.
            var questionStringId = request.QuestionId;
            var question = await db.ReadingQuestions.AsNoTracking()
                .Include(q => q.Part)
                .FirstOrDefaultAsync(q => q.Id == questionStringId, ct);
            if (question is null) return Results.NotFound();

            var attemptQuestionId = StableGuidFromQuestionId(question.Id);

            // Skip duplicate attempts within the same session
            var existing = await db.ReadingQuestionAttempts
                .FirstOrDefaultAsync(a => a.UserId == userId
                    && a.ReadingQuestionId == attemptQuestionId
                    && a.PracticeSessionId == sessionId, ct);

            if (existing is not null)
            {
                if (!string.Equals(existing.SelectedOption, request.SelectedOption, StringComparison.Ordinal))
                    return Results.Conflict(new { code = "answer_already_submitted", error = "Answer already submitted for this question.", message = "Answer already submitted for this question." });

                return Results.Ok(new AnswerResultResponse(existing.IsCorrect, Explanation: null));
            }

            bool isCorrect = CheckAnswer(question, request.SelectedOption);

            var attempt = new ReadingQuestionAttempt
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ReadingQuestionId = attemptQuestionId,
                PracticeSessionId = sessionId,
                SelectedOption = request.SelectedOption,
                IsCorrect = isCorrect,
                TimeSpentSeconds = request.TimeSpentSeconds,
                AttemptedAt = DateTimeOffset.UtcNow,
                InReviewQueue = !isCorrect,
                NextReviewAt = isCorrect ? null : DateTimeOffset.UtcNow.AddDays(1),
            };
            db.ReadingQuestionAttempts.Add(attempt);
            MergeSkillTag(session, attempt.Id, question.SkillTag ?? "S1");
            await UpdatePracticeErrorBankAsync(userId, question, attempt, isCorrect, db, ct);
            await db.SaveChangesAsync(ct);

            await xp.AwardXpAsync(userId, isCorrect ? XpAmounts.PerCorrectAnswer : XpAmounts.PerQuestionAnswered, "question_answered", ct);
            await streak.RecordActivityAsync(userId, 1, ct);

            return Results.Ok(new AnswerResultResponse(isCorrect, Explanation: null));
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

            if (session.CompletedAt is not null)
                return Results.Ok(await BuildPracticeSessionSubmitResponseAsync(session, userId, db, ct));

            session.CompletedAt = DateTimeOffset.UtcNow;
            session.DurationSeconds = (int)(DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds;

            var attempts = await db.ReadingQuestionAttempts
                .Where(a => a.PracticeSessionId == sessionId && a.UserId == userId).CountAsync(ct);
            var correct = await db.ReadingQuestionAttempts
                .Where(a => a.PracticeSessionId == sessionId && a.UserId == userId && a.IsCorrect).CountAsync(ct);

            session.Score = correct;
            session.TotalQuestions = Math.Max(session.TotalQuestions ?? 0, attempts);
            await db.SaveChangesAsync(ct);

            await scoring.UpdateSkillScoresAsync(userId, sessionId, ct);
            if (session.SessionType == "mock")
                await xp.AwardXpAsync(userId, XpAmounts.PerMockCompleted, "session_complete", ct);

            return Results.Ok(await BuildPracticeSessionSubmitResponseAsync(session, userId, db, ct));
        });

        group.MapGet("/practice/sessions/{sessionId}/questions", async (
            Guid sessionId, HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var session = await db.ReadingPracticeSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (session is null) return Results.NotFound();
            if (session.SessionType is "diagnostic" or "mock")
                return Results.BadRequest(new { code = "session_projection_unavailable", error = "Use the dedicated player for this session type.", message = "Use the dedicated player for this session type." });

            var questionIds = JsonSerializer.Deserialize<List<string>>(session.QuestionIdsJson) ?? [];
            if (questionIds.Count == 0)
            {
                return Results.Ok(new PracticeSessionQuestionsResponse(
                    SessionId: session.Id,
                    Mode: session.SessionType,
                    FocusSkill: session.FocusSkill,
                    TimeLimitSeconds: null,
                    Questions: [],
                    Passages: []));
            }

            var questionOrder = questionIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index, StringComparer.OrdinalIgnoreCase);

            var questions = await db.ReadingQuestions.AsNoTracking()
                .Include(q => q.Part)
                .Include(q => q.Text)
                .Where(q => questionIds.Contains(q.Id))
                .ToListAsync(ct);

            var passages = questions
                .Where(q => q.Text is not null)
                .GroupBy(q => q.Text!.Id)
                .Select(group =>
                {
                    var question = group.First();
                    var text = question.Text!;
                    return new
                    {
                        Text = text,
                        PartCode = (int)(question.Part?.PartCode ?? ReadingPartCode.A)
                    };
                })
                .OrderBy(item => item.PartCode)
                .ThenBy(item => item.Text.DisplayOrder)
                .Select(item => new PracticeSessionPassageResponse(
                    Id: item.Text.Id,
                    Title: item.Text.Title,
                    BodyHtml: item.Text.BodyHtml,
                    PartCode: item.PartCode))
                .ToList();

            var responseQuestions = questions
                .OrderBy(q => questionOrder.TryGetValue(q.Id, out var index) ? index : int.MaxValue)
                .Select(q => new PracticeSessionQuestionResponse(
                    Id: q.Id,
                    PassageId: q.Text?.Id ?? q.ReadingTextId ?? string.Empty,
                    Stem: q.Stem,
                    Options: ReadingLearnerSafeProjection.ProjectOptionsElement(q.OptionsJson),
                    QuestionType: q.QuestionType.ToString(),
                    PartCode: (int)(q.Part?.PartCode ?? ReadingPartCode.A),
                    SkillCode: q.SkillTag))
                .ToList();

            return Results.Ok(new PracticeSessionQuestionsResponse(
                SessionId: session.Id,
                Mode: session.SessionType,
                FocusSkill: session.FocusSkill,
                TimeLimitSeconds: null,
                Questions: responseQuestions,
                Passages: passages));
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
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            _ = (userId, questionId, wrongOption, language, explanationSvc, ct);
            return Results.Json(
                new { code = "explanation_route_disabled", error = "Explanations are available only through submitted attempt review.", message = "Explanations are available only through submitted attempt review." },
                statusCode: StatusCodes.Status410Gone);
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
            if (questionIds.Count == 0)
                return Results.BadRequest(new { code = "mock_template_unavailable", error = "Mock template is not available.", message = "Mock template is not available." });
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
            if (session is null || session.SessionType != "mock" || session.CompletedAt is null) return Results.NotFound();

            return Results.Ok(new MockSessionResultResponse(
                Score: session.Score ?? 0,
                TotalQuestions: session.TotalQuestions ?? 0,
                DurationSeconds: session.DurationSeconds,
                ScaledScore: EstimateScaled(session.Score ?? 0, session.TotalQuestions ?? 42)));
        });

        // ── §23.5 Lessons + Strategies ────────────────────────────────────────
        group.MapGet("/lessons", async (
            HttpContext http,
            ILessonService lessonSvc,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var lessons = await lessonSvc.GetLessonsAsync(ct);
            var lessonIds = lessons.Select(l => l.Id).ToList();
            var progressByLessonId = await db.LearnerLessonProgresses.AsNoTracking()
                .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
                .ToDictionaryAsync(p => p.LessonId, ct);
            return Results.Ok(lessons.Select(lesson => new
            {
                lesson,
                progress = progressByLessonId.GetValueOrDefault(lesson.Id)
            }));
        });

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

        group.MapPost("/vocab/lists/{listIdOrSlug}/subscribe", async (
            string listIdOrSlug, HttpContext http, IReadingVocabularyService svc, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            Guid listId;
            if (!Guid.TryParse(listIdOrSlug, out listId))
            {
                var resolved = await db.VocabularyLists.AsNoTracking()
                    .Where(l => l.Slug == listIdOrSlug && l.IsPublished)
                    .Select(l => (Guid?)l.Id)
                    .FirstOrDefaultAsync(ct);
                if (resolved is null) return Results.NotFound();
                listId = resolved.Value;
            }
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
            if (string.IsNullOrWhiteSpace(questionId))
                return Results.BadRequest(new { code = "invalid_question_id", error = "questionId is required.", message = "questionId is required." });

            var questionGuid = StableGuidFromQuestionId(questionId);

            var comments = await db.ReadingQuestionDiscussionComments.AsNoTracking()
                .Where(c => c.ReadingQuestionId == questionGuid && !c.IsHidden)
                .OrderByDescending(c => c.Upvotes)
                .ThenByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.UserId,
                    UserDisplayName = c.IsFromTutor ? "OET Tutor" : "Learner",
                    c.Body,
                    c.Upvotes,
                    IsExpert = c.IsFromTutor,
                    c.CreatedAt
                })
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
            if (string.IsNullOrWhiteSpace(questionId))
                return Results.BadRequest(new { code = "invalid_question_id", error = "questionId is required.", message = "questionId is required." });
            var body = request.Body?.Trim() ?? "";
            if (body.Length == 0)
                return Results.BadRequest(new { code = "empty_comment", error = "Comment body is required.", message = "Comment body is required." });
            if (body.Length > 2000)
                return Results.BadRequest(new { code = "comment_too_long", error = "Comment body must be 2000 characters or fewer.", message = "Comment body must be 2000 characters or fewer." });

            var questionGuid = StableGuidFromQuestionId(questionId);

            var comment = new ReadingQuestionDiscussionComment
            {
                Id = Guid.NewGuid(),
                ReadingQuestionId = questionGuid,
                UserId = userId,
                Body = body,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ReadingQuestionDiscussionComments.Add(comment);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/reading-pathway/questions/{questionId}/comments/{comment.Id}", new
            {
                comment.Id,
                comment.UserId,
                UserDisplayName = "Learner",
                comment.Body,
                comment.Upvotes,
                IsExpert = false,
                comment.CreatedAt
            });
        });

        group.MapPost("/questions/{questionId}/comments/{commentId}/upvote", async (
            string questionId, Guid commentId, LearnerDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(questionId))
                return Results.BadRequest(new { code = "invalid_question_id", error = "questionId is required.", message = "questionId is required." });

            var questionGuid = StableGuidFromQuestionId(questionId);
            var comment = await db.ReadingQuestionDiscussionComments
                .FirstOrDefaultAsync(c => c.Id == commentId && c.ReadingQuestionId == questionGuid && !c.IsHidden, ct);
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
        var projectedRaw = (int)Math.Round(
            (decimal)raw / total * OetScoring.ListeningReadingRawMax,
            MidpointRounding.AwayFromZero);
        return OetScoring.OetRawToScaled(projectedRaw);
    }

    private static async Task<PracticeSessionSubmitResponse> BuildPracticeSessionSubmitResponseAsync(
        ReadingPracticeSession session,
        string userId,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var attempts = await db.ReadingQuestionAttempts.AsNoTracking()
            .Where(a => a.PracticeSessionId == session.Id && a.UserId == userId)
            .CountAsync(ct);
        var correct = await db.ReadingQuestionAttempts.AsNoTracking()
            .Where(a => a.PracticeSessionId == session.Id && a.UserId == userId && a.IsCorrect)
            .CountAsync(ct);
        var totalQuestions = Math.Max(session.TotalQuestions ?? 0, attempts);

        return new PracticeSessionSubmitResponse(
            SessionId: session.Id,
            Score: session.Score ?? correct,
            TotalQuestions: totalQuestions,
            DurationSeconds: session.DurationSeconds,
            ScaledScore: session.SessionType == "mock" ? EstimateScaled(session.Score ?? correct, totalQuestions) : null);
    }

    private static ReadingProfileResponse ToProfileResponse(string userId, LearnerReadingProfile? profile)
    {
        if (profile is null)
        {
            return new ReadingProfileResponse(
                UserId: userId,
                CurrentStage: "onboarding",
                TargetBand: null,
                ExamDate: null,
                HoursPerWeek: null,
                Profession: null,
                HasTakenBefore: false,
                PreviousScore: null,
                SelfRatedSpeed: null,
                SelfRatedVocabulary: null,
                ReadinessScore: null,
                PredictedScore: null,
                OnboardingCompletedAt: null,
                PathwayGeneratedAt: null,
                WeeksRemaining: null,
                DiagnosticCompleted: false);
        }

        int? weeksRemaining = profile.ExamDate.HasValue
            ? Math.Max(0, (int)Math.Ceiling((profile.ExamDate.Value - DateTimeOffset.UtcNow).TotalDays / 7))
            : null;

        return new ReadingProfileResponse(
            UserId: profile.UserId,
            CurrentStage: profile.CurrentStage,
            TargetBand: profile.TargetBand,
            ExamDate: profile.ExamDate,
            HoursPerWeek: profile.HoursPerWeek,
            Profession: profile.Profession,
            HasTakenBefore: profile.HasTakenBefore,
            PreviousScore: profile.PreviousScore,
            SelfRatedSpeed: profile.SelfRatedSpeed,
            SelfRatedVocabulary: profile.SelfRatedVocabulary,
            ReadinessScore: profile.CurrentReadinessScore,
            PredictedScore: profile.PredictedScore,
            OnboardingCompletedAt: profile.OnboardingCompletedAt,
            PathwayGeneratedAt: profile.PathwayGeneratedAt,
            WeeksRemaining: weeksRemaining,
            DiagnosticCompleted: profile.CurrentStage is not "onboarding" and not "diagnostic");
    }

    private static ReadingPathwayResponse ToPathwayResponse(LearnerReadingProfile? profile, LearnerReadingPathway? pathway)
    {
        var weeks = new List<ReadingPathwayWeekResponse>();
        if (!string.IsNullOrWhiteSpace(pathway?.WeeksJson))
        {
            try
            {
                weeks = JsonSerializer.Deserialize<List<ReadingPathwayWeekResponse>>(pathway.WeeksJson) ?? [];
            }
            catch (JsonException)
            {
                weeks = [];
            }
        }

        var currentWeek = weeks.FirstOrDefault(w => !w.IsCompleted)?.WeekNumber
            ?? (weeks.Count > 0 ? weeks.Count : 0);
        var weeksRemaining = weeks.Count > 0
            ? Math.Max(0, weeks.Count(w => !w.IsCompleted))
            : profile?.ExamDate is null
                ? 0
                : Math.Max(0, (int)Math.Ceiling((profile.ExamDate.Value - DateTimeOffset.UtcNow).TotalDays / 7));

        return new ReadingPathwayResponse(
            CurrentStage: profile?.CurrentStage ?? "onboarding",
            TotalWeeks: pathway?.TotalWeeks ?? weeks.Count,
            CurrentWeek: currentWeek,
            WeeksRemaining: weeksRemaining,
            ReadinessScore: profile?.CurrentReadinessScore ?? 0,
            PredictedScore: profile?.PredictedScore,
            GeneratedAt: pathway?.GeneratedAt,
            Weeks: weeks);
    }

    private static Guid StableGuidFromQuestionId(string questionId)
    {
        if (Guid.TryParse(questionId, out var parsed)) return parsed;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(questionId));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static void MergeSkillTag(ReadingPracticeSession session, Guid attemptId, string skillTag)
    {
        var skillTagMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(session.MetadataJson) && session.MetadataJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(session.MetadataJson);
                if (doc.RootElement.TryGetProperty("skillTagMap", out var mapEl)
                    && mapEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in mapEl.EnumerateObject())
                        skillTagMap[prop.Name] = prop.Value.GetString() ?? "S1";
                }
            }
            catch (JsonException)
            {
                skillTagMap.Clear();
            }
        }

        skillTagMap[attemptId.ToString()] = skillTag;
        session.MetadataJson = JsonSerializer.Serialize(new { skillTagMap });
    }

    private static async Task UpdatePracticeErrorBankAsync(
        string userId,
        ReadingQuestion question,
        ReadingQuestionAttempt attempt,
        bool isCorrect,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var entry = await db.ReadingErrorBankEntries
            .FirstOrDefaultAsync(e => e.UserId == userId && e.ReadingQuestionId == question.Id, ct);
        var now = DateTimeOffset.UtcNow;

        if (isCorrect)
        {
            if (entry is { IsResolved: false })
            {
                entry.IsResolved = true;
                entry.ResolvedAt = now;
                entry.ResolvedReason = "answered_correctly";
            }

            return;
        }

        if (entry is null)
        {
            entry = new ReadingErrorBankEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ReadingQuestionId = question.Id,
                PaperId = question.Part?.PaperId ?? question.ReadingPartId,
                PartCode = question.Part?.PartCode ?? ReadingPartCode.A,
                LastWrongAttemptId = attempt.Id.ToString(),
                FirstSeenWrongAt = now,
                LastSeenWrongAt = now,
                TimesWrong = 1,
                IsResolved = false
            };
            db.ReadingErrorBankEntries.Add(entry);
            return;
        }

        entry.LastWrongAttemptId = attempt.Id.ToString();
        entry.LastSeenWrongAt = now;
        entry.TimesWrong += 1;
        entry.PaperId = question.Part?.PaperId ?? question.ReadingPartId;
        entry.PartCode = question.Part?.PartCode ?? ReadingPartCode.A;
        entry.IsResolved = false;
        entry.ResolvedAt = null;
        entry.ResolvedReason = null;
    }

    private static async Task<int> GetRoadmapWeeksAsync(string userId, LearnerDbContext db, CancellationToken ct)
    {
        var pathway = await db.LearnerReadingPathways.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return pathway?.TotalWeeks ?? 0;
    }
}
