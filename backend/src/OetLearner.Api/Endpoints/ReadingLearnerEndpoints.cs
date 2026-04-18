using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
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
            .RequireAuthorization()
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
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
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
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── Submit ─────────────────────────────────────────────────────────
        group.MapPost("/attempts/{attemptId}/submit", async (
            string attemptId,
            IReadingAttemptService svc, IReadingPolicyService pol,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            try
            {
                var result = await svc.SubmitAsync(userId, attemptId, ct);
                var policy = await pol.ResolveForUserAsync(userId, ct);
                return Results.Ok(new
                {
                    result.RawScore,
                    result.MaxRawScore,
                    result.ScaledScore,
                    result.GradeLetter,
                    result.CorrectCount,
                    result.IncorrectCount,
                    result.UnansweredCount,
                    // Per-answer breakdown respects review policy.
                    answers = policy.ShowCorrectAnswerOnReview
                        ? result.Answers
                        : result.Answers.Select(a => a with { }).ToList(), // same shape currently; reserved for future redaction
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireRateLimiting("PerUserWrite");

        // ── Read an existing attempt (for resume + results page) ───────────
        group.MapGet("/attempts/{attemptId}", async (
            string attemptId,
            LearnerDbContext db,
            IReadingPolicyService pol,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var attempt = await db.ReadingAttempts.AsNoTracking()
                .Include(a => a.Answers)
                .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct);
            if (attempt is null) return Results.NotFound();

            var policy = await pol.ResolveForUserAsync(userId, ct);
            var showExplain = attempt.Status == ReadingAttemptStatus.Submitted
                && policy.ShowExplanationsAfterSubmit;

            return Results.Ok(new
            {
                attempt.Id,
                attempt.PaperId,
                attempt.Status,
                attempt.StartedAt,
                attempt.DeadlineAt,
                attempt.SubmittedAt,
                attempt.RawScore,
                attempt.ScaledScore,
                attempt.MaxRawScore,
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
}

public sealed record AnswerSaveDto(string UserAnswerJson);
