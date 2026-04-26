using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing Reading endpoints (Slice R4).
///
/// MISSION CRITICAL:
/// Question payloads served to learners NEVER include correct answers,
/// explanations, or accepted-synonyms. Two separate DTOs — projection
/// enforced at the endpoint layer, not relied on at call sites.
///
/// See docs/READING-AUTHORING-PLAN.md.
/// </summary>
public static class ReadingLearnerEndpoints
{
    public static IEndpointRouteBuilder MapReadingLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        // NOTE: under /v1/reading-papers/ (not /v1/reading/) to avoid collision
        // with the legacy LearnerService-backed /v1/reading/attempts/* routes
        // used by the ContentItem / Diagnostic flow.
        var group = app.MapGroup("/v1/reading-papers")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        // ── Fetch structure (no answers, no explanations) ───────────────────
        group.MapGet("/papers/{paperId}/structure", async (
            string paperId,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paperId && p.Status == ContentStatus.Published, ct);
            if (paper is null || !string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound();

            var parts = await db.ReadingParts.AsNoTracking()
                .Where(p => p.PaperId == paperId)
                .Include(p => p.Texts.OrderBy(t => t.DisplayOrder))
                .Include(p => p.Questions.OrderBy(q => q.DisplayOrder))
                .OrderBy(p => p.PartCode)
                .ToListAsync(ct);

            // Project every question to the learner-safe shape. Critical.
            return Results.Ok(new
            {
                paper = new { paper.Id, paper.Title, paper.Slug, paper.SubtestCode },
                parts = parts.Select(p => new
                {
                    p.Id,
                    partCode = p.PartCode.ToString(),
                    p.TimeLimitMinutes,
                    p.MaxRawScore,
                    p.Instructions,
                    texts = p.Texts.Select(t => new
                    {
                        t.Id, t.DisplayOrder, t.Title, t.Source, t.BodyHtml, t.WordCount, t.TopicTag,
                    }),
                    questions = p.Questions.Select(q => new
                    {
                        q.Id,
                        q.ReadingTextId,
                        q.DisplayOrder,
                        q.Points,
                        questionType = q.QuestionType.ToString(),
                        q.Stem,
                        // options visible; correct answer + explanation + synonyms NOT.
                        options = SafeParseOptions(q.OptionsJson),
                    }),
                }),
            });
        });

        // ── Start attempt ──────────────────────────────────────────────────
        group.MapPost("/papers/{paperId}/attempts", async (
            string paperId, IReadingAttemptService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            try
            {
                var started = await svc.StartAsync(userId, paperId, ct);
                return Results.Ok(started);
            }
            catch (ReadingAttemptException ex)
            {
                return Results.BadRequest(new { code = ex.Code, error = ex.Message, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { code = "reading_attempt_rejected", error = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerUserWrite");

        // ── Autosave one answer ────────────────────────────────────────────
        group.MapPut("/attempts/{attemptId}/answers/{questionId}", async (
            string attemptId, string questionId,
            AnswerSaveDto dto,
            IReadingAttemptService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            try
            {
                await svc.SaveAnswerAsync(userId, attemptId, questionId, dto.UserAnswerJson, ct);
                return Results.NoContent();
            }
            catch (ReadingAttemptException ex)
            {
                return Results.BadRequest(new { code = ex.Code, error = ex.Message, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { code = "reading_answer_rejected", error = ex.Message, message = ex.Message });
            }
        });

        // ── Submit ─────────────────────────────────────────────────────────
        group.MapPost("/attempts/{attemptId}/submit", async (
            string attemptId,
            IReadingAttemptService svc, LearnerDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            try
            {
                var result = await svc.SubmitAsync(userId, attemptId, ct);
                var paperId = await db.ReadingAttempts.AsNoTracking()
                    .Where(a => a.Id == attemptId && a.UserId == userId)
                    .Select(a => a.PaperId)
                    .FirstOrDefaultAsync(ct);
                return Results.Ok(new
                {
                    attemptId,
                    result.RawScore,
                    result.MaxRawScore,
                    result.ScaledScore,
                    result.GradeLetter,
                    result.CorrectCount,
                    result.IncorrectCount,
                    result.UnansweredCount,
                    reviewRoute = string.IsNullOrWhiteSpace(paperId)
                        ? null
                        : $"/reading/paper/{paperId}/results?attemptId={attemptId}",
                    answers = result.Answers,
                });
            }
            catch (ReadingAttemptException ex)
            {
                return Results.BadRequest(new { code = ex.Code, error = ex.Message, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { code = "reading_submit_rejected", error = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerUserWrite");

        // ── Read an existing attempt (for resume + results page) ───────────
        group.MapGet("/attempts/{attemptId}", async (
            string attemptId,
            LearnerDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var attempt = await db.ReadingAttempts.AsNoTracking()
                .Include(a => a.Answers)
                .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct);
            if (attempt is null) return Results.NotFound();

            var policy = ResolvePolicySnapshot(attempt.PolicySnapshotJson);
            var showExplain = attempt.Status == ReadingAttemptStatus.Submitted
                && policy.ShowExplanationsAfterSubmit;
            var totalQuestions = await CountQuestionsForPaperAsync(db, attempt.PaperId, ct);
            var partADeadline = attempt.StartedAt.AddMinutes(policy.PartATimerMinutes);
            var partBCDeadline = attempt.StartedAt.AddMinutes(policy.PartATimerMinutes + policy.PartBCTimerMinutes);
            var now = DateTimeOffset.UtcNow;

            return Results.Ok(new
            {
                attempt.Id,
                attempt.PaperId,
                status = attempt.Status.ToString(),
                attempt.StartedAt,
                attempt.DeadlineAt,
                attempt.SubmittedAt,
                attempt.RawScore,
                attempt.ScaledScore,
                attempt.MaxRawScore,
                partADeadlineAt = partADeadline,
                partBCDeadlineAt = partBCDeadline,
                answeredCount = attempt.Answers.Count,
                totalQuestions,
                canResume = attempt.Status == ReadingAttemptStatus.InProgress
                    && (attempt.DeadlineAt is null || attempt.DeadlineAt >= now || policy.AllowResumeAfterExpiry),
                answers = attempt.Answers.Select(a => new
                {
                    a.ReadingQuestionId,
                    a.UserAnswerJson,
                    a.IsCorrect,
                    a.PointsEarned,
                    a.AnsweredAt,
                }),
                showExplanations = showExplain,
            });
        });

        // ── Policy-safe review after submit ──────────────────────────────────
        group.MapGet("/attempts/{attemptId}/review", async (
            string attemptId,
            LearnerDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var attempt = await db.ReadingAttempts.AsNoTracking()
                .Include(a => a.Answers)
                .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.Status != ReadingAttemptStatus.Submitted)
                return Results.BadRequest(new
                {
                    code = "reading_review_unavailable",
                    error = "Review is available after the attempt is submitted.",
                    message = "Review is available after the attempt is submitted.",
                });

            var paper = await db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == attempt.PaperId, ct);
            if (paper is null) return Results.NotFound();

            var policy = ResolvePolicySnapshot(attempt.PolicySnapshotJson);
            var parts = await db.ReadingParts.AsNoTracking()
                .Where(p => p.PaperId == attempt.PaperId)
                .Include(p => p.Questions.OrderBy(q => q.DisplayOrder))
                .OrderBy(p => p.PartCode)
                .ToListAsync(ct);
            var answers = attempt.Answers.ToDictionary(a => a.ReadingQuestionId);
            var showCorrect = policy.ShowCorrectAnswerOnReview;
            var showExplanations = policy.ShowExplanationsAfterSubmit;
            var items = parts
                .SelectMany(part => part.Questions
                    .OrderBy(q => q.DisplayOrder)
                    .Select(q =>
                    {
                        answers.TryGetValue(q.Id, out var answer);
                        var isCorrect = answer?.IsCorrect ?? false;
                        var explanationVisible = showExplanations
                            && (!policy.ShowExplanationsOnlyIfWrong || !isCorrect);
                        return new ReadingReviewItem(
                            QuestionId: q.Id,
                            PartCode: part.PartCode.ToString(),
                            DisplayOrder: q.DisplayOrder,
                            QuestionType: q.QuestionType.ToString(),
                            Stem: q.Stem,
                            SkillTag: q.SkillTag,
                            UserAnswer: SafeParseJson(answer?.UserAnswerJson),
                            IsCorrect: isCorrect,
                            PointsEarned: answer?.PointsEarned ?? 0,
                            MaxPoints: q.Points,
                            CorrectAnswer: showCorrect ? SafeParseJson(q.CorrectAnswerJson) : null,
                            ExplanationMarkdown: explanationVisible ? q.ExplanationMarkdown : null);
                    }))
                .ToList();

            var clusters = items
                .Where(i => !i.IsCorrect)
                .GroupBy(i => string.IsNullOrWhiteSpace(i.SkillTag) ? i.QuestionType : i.SkillTag)
                .Select(g => new
                {
                    label = g.Key,
                    incorrectCount = g.Count(),
                    questionIds = g.Select(i => i.QuestionId).ToList(),
                })
                .OrderByDescending(x => x.incorrectCount)
                .ThenBy(x => x.label)
                .ToList();

            return Results.Ok(new
            {
                attempt = new
                {
                    attempt.Id,
                    attempt.PaperId,
                    status = attempt.Status.ToString(),
                    attempt.StartedAt,
                    attempt.SubmittedAt,
                    attempt.RawScore,
                    attempt.MaxRawScore,
                    attempt.ScaledScore,
                    gradeLetter = OetScoring.OetGradeLetterFromScaled(
                        attempt.ScaledScore ?? OetScoring.OetRawToScaled(attempt.RawScore ?? 0)),
                    partADeadlineAt = attempt.StartedAt.AddMinutes(policy.PartATimerMinutes),
                    partBCDeadlineAt = attempt.StartedAt.AddMinutes(policy.PartATimerMinutes + policy.PartBCTimerMinutes),
                },
                paper = new { paper.Id, paper.Title, paper.Slug, paper.SubtestCode },
                policy = new
                {
                    policy.ShowCorrectAnswerOnReview,
                    policy.ShowExplanationsAfterSubmit,
                    policy.ShowExplanationsOnlyIfWrong,
                },
                items,
                clusters,
            });
        });

        // ── Per-user policy snapshot (what timer / limits apply to me) ─────
        group.MapGet("/me/policy", async (
            IReadingPolicyService pol, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var policy = await pol.ResolveForUserAsync(userId, ct);
            return Results.Ok(policy);
        });

        return app;
    }

    private static object SafeParseOptions(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static object? SafeParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static async Task<int> CountQuestionsForPaperAsync(
        LearnerDbContext db,
        string paperId,
        CancellationToken ct)
    {
        return await db.ReadingQuestions.AsNoTracking()
            .CountAsync(q => db.ReadingParts.Any(p => p.PaperId == paperId && p.Id == q.ReadingPartId), ct);
    }

    private static ReadingResolvedPolicy ResolvePolicySnapshot(string json)
    {
        try
        {
            var policy = JsonSerializer.Deserialize<ReadingResolvedPolicy>(json);
            if (policy is not null
                && !string.IsNullOrWhiteSpace(policy.PartATimerStrictness)
                && !string.IsNullOrWhiteSpace(policy.ShortAnswerNormalisation)
                && !string.IsNullOrWhiteSpace(policy.UnknownTypeFallbackPolicy))
            {
                return policy;
            }
        }
        catch (JsonException)
        {
            // Fall back to the safest learner-visible defaults below.
        }

        return new ReadingResolvedPolicy(
            AttemptsPerPaperPerUser: 0,
            AttemptCooldownMinutes: 0,
            PartATimerStrictness: "hard_lock",
            PartATimerMinutes: 15,
            PartBCTimerMinutes: 45,
            GracePeriodSeconds: 10,
            OnExpirySubmitPolicy: "auto_submit_graded",
            CountdownWarnings: new[] { 300, 60, 15 },
            EnabledQuestionTypes: new[]
            {
                nameof(ReadingQuestionType.MatchingTextReference),
                nameof(ReadingQuestionType.ShortAnswer),
                nameof(ReadingQuestionType.SentenceCompletion),
                nameof(ReadingQuestionType.MultipleChoice3),
                nameof(ReadingQuestionType.MultipleChoice4),
            },
            ShortAnswerNormalisation: "trim_collapse_case_insensitive",
            // OET-faithful default. Synonym acceptance is non-standard mode.
            ShortAnswerAcceptSynonyms: false,
            MatchingAllowPartialCredit: true,
            UnknownTypeFallbackPolicy: "skip_with_zero",
            ShowExplanationsAfterSubmit: true,
            ShowExplanationsOnlyIfWrong: false,
            ShowCorrectAnswerOnReview: true,
            SubmitRateLimitPerMinute: 5,
            AutosaveRateLimitPerMinute: 120,
            ExtraTimeEntitlementPct: 0,
            AllowMultipleConcurrentAttempts: false,
            AllowPausingAttempt: false,
            AllowResumeAfterExpiry: false);
    }

    private sealed record ReadingReviewItem(
        string QuestionId,
        string PartCode,
        int DisplayOrder,
        string QuestionType,
        string Stem,
        string? SkillTag,
        object? UserAnswer,
        bool IsCorrect,
        int PointsEarned,
        int MaxPoints,
        object? CorrectAnswer,
        string? ExplanationMarkdown);
}

public sealed record AnswerSaveDto(string UserAnswerJson);
