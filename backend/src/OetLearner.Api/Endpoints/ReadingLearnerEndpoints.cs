using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
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

        group.MapGet("/home", async (
            HttpContext http,
            LearnerDbContext db,
            IReadingPolicyService policy,
            IContentEntitlementService contentEntitlements,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            return Results.Ok(await LearnerEndpoints.GetStructuredReadingHomeAsync(
                userId, db, policy, contentEntitlements, ct));
        });

        // ── Fetch structure (no answers, no explanations) ───────────────────
        group.MapGet("/papers/{paperId}/structure", async (
            string paperId,
            HttpContext http,
            LearnerDbContext db,
            IReadingPolicyService policyService,
            IContentEntitlementService entitlements,
            CancellationToken ct) =>
        {
            var paper = await db.ContentPapers.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paperId && p.Status == ContentStatus.Published, ct);
            if (paper is null || !string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound();

            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            if (!await CanLearnerSeePaperAsync(db, http, userId, paper, ct))
                return Results.NotFound();

            await entitlements.RequireAccessAsync(userId, paper, ct);
            var resolvedPolicy = await policyService.ResolveForUserAsync(userId, ct);

            List<QuestionPaperAssetDto> questionPaperAssets = [];
            if (resolvedPolicy.AllowPaperReadingMode)
            {
                questionPaperAssets = await db.ContentPaperAssets.AsNoTracking()
                    .Where(a => a.PaperId == paperId
                        && a.Role == PaperAssetRole.QuestionPaper
                        && a.IsPrimary
                        && a.MediaAsset != null
                        && a.MediaAsset.Status == MediaAssetStatus.Ready)
                    .Include(a => a.MediaAsset)
                    .OrderBy(a => a.DisplayOrder)
                    .ThenBy(a => a.Part)
                    .Select(a => new QuestionPaperAssetDto(
                        a.Id,
                        a.Part,
                        a.Title ?? a.MediaAsset!.OriginalFilename,
                        $"/v1/media/{a.MediaAssetId}/content"))
                    .ToListAsync(ct);
            }

            var parts = await db.ReadingParts.AsNoTracking()
                .Where(p => p.PaperId == paperId)
                .Include(p => p.Texts.OrderBy(t => t.DisplayOrder))
                .Include(p => p.Questions.OrderBy(q => q.DisplayOrder))
                .OrderBy(p => p.PartCode)
                .ToListAsync(ct);

            // Project every question to the learner-safe shape. Critical.
            return Results.Ok(new
            {
                paper = new
                {
                    paper.Id,
                    paper.Title,
                    paper.Slug,
                    paper.SubtestCode,
                    allowPaperReadingMode = resolvedPolicy.AllowPaperReadingMode,
                    questionPaperAssets,
                },
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
                        options = SafeProjectOptions(q.OptionsJson),
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

        group.MapPost("/attempts/{attemptId}/break/resume", async (
            string attemptId,
            IReadingAttemptService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            try
            {
                var resumed = await svc.ResumePartABreakAsync(userId, attemptId, ct);
                return Results.Ok(resumed);
            }
            catch (ReadingAttemptException ex)
            {
                return Results.BadRequest(new { code = ex.Code, error = ex.Message, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { code = "reading_break_rejected", error = ex.Message, message = ex.Message });
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
            var scopeIds = ParseScopeQuestionIdsForPlayer(attempt.ScopeJson);
            var totalQuestions = scopeIds?.Count ?? await CountQuestionsForPaperAsync(db, attempt.PaperId, ct);
            var now = DateTimeOffset.UtcNow;
            var (partADeadline, partBCDeadline) = ResolveAttemptDeadlines(attempt, policy, now);
            var breakWindowActive = attempt.Mode == ReadingAttemptMode.Exam
                && !attempt.PartABreakUsed
                && now < partADeadline.AddSeconds(ReadingAttemptService.PartABreakMaxSeconds);

            return Results.Ok(new
            {
                attempt.Id,
                attempt.PaperId,
                status = attempt.Status.ToString(),
                mode = attempt.Mode.ToString(),
                scopeQuestionIds = scopeIds,
                attempt.StartedAt,
                attempt.DeadlineAt,
                attempt.SubmittedAt,
                attempt.RawScore,
                attempt.ScaledScore,
                attempt.MaxRawScore,
                partADeadlineAt = partADeadline,
                partBCDeadlineAt = partBCDeadline,
                partABreakAvailable = attempt.Mode == ReadingAttemptMode.Exam,
                partABreakResumed = attempt.Mode != ReadingAttemptMode.Exam || attempt.PartABreakUsed,
                partBCTimerPausedAt = breakWindowActive
                    ? partADeadline
                    : (DateTimeOffset?)null,
                partBCPausedSeconds = ResolveEffectivePartBCPausedSeconds(attempt, policy, now),
                partABreakMaxSeconds = attempt.Mode == ReadingAttemptMode.Exam
                    ? ReadingAttemptService.PartABreakMaxSeconds
                    : 0,
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
            var scopeIds = ParseScopeQuestionIdsForPlayer(attempt.ScopeJson);
            var scopedQuestionIds = scopeIds is { Count: > 0 } ? scopeIds.ToHashSet(StringComparer.Ordinal) : null;
            var now = DateTimeOffset.UtcNow;
            var (partADeadline, partBCDeadline) = ResolveAttemptDeadlines(attempt, policy, now);
            var breakWindowActive = attempt.Mode == ReadingAttemptMode.Exam
                && !attempt.PartABreakUsed
                && now < partADeadline.AddSeconds(ReadingAttemptService.PartABreakMaxSeconds);
            var items = parts
                .SelectMany(part => part.Questions
                    .Where(q => scopedQuestionIds is null || scopedQuestionIds.Contains(q.Id))
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
                    questions = g.Select(i => new
                    {
                        i.PartCode,
                        i.DisplayOrder,
                        label = $"Part {i.PartCode} Q{i.DisplayOrder}",
                    }).ToList(),
                })
                .OrderByDescending(x => x.incorrectCount)
                .ThenBy(x => x.label)
                .ToList();

            var partBreakdown = parts
                .Select(part =>
                {
                    var partCode = part.PartCode.ToString();
                    var partItems = items.Where(i => i.PartCode == partCode).ToList();
                    var maxRawScore = part.Questions
                        .Where(q => scopedQuestionIds is null || scopedQuestionIds.Contains(q.Id))
                        .Sum(q => q.Points);
                    return new
                    {
                        partCode,
                        rawScore = partItems.Sum(i => i.PointsEarned),
                        maxRawScore,
                        correctCount = partItems.Count(i => i.IsCorrect),
                        incorrectCount = partItems.Count(i => !i.IsCorrect && i.UserAnswer is not null),
                        unansweredCount = partItems.Count(i => i.UserAnswer is null),
                    };
                })
                .Where(part => scopedQuestionIds is null || part.maxRawScore > 0)
                .ToList();

            var skillBreakdown = items
                .GroupBy(i => string.IsNullOrWhiteSpace(i.SkillTag) ? i.QuestionType : i.SkillTag)
                .Select(g => new
                {
                    label = g.Key,
                    correctCount = g.Count(i => i.IsCorrect),
                    incorrectCount = g.Count(i => !i.IsCorrect && i.UserAnswer is not null),
                    unansweredCount = g.Count(i => i.UserAnswer is null),
                    totalCount = g.Count(),
                })
                .OrderByDescending(x => x.incorrectCount + x.unansweredCount)
                .ThenBy(x => x.label)
                .ToList();

            var gradeLetter = attempt.ScaledScore is int scaledScore
                ? OetScoring.OetGradeLetterFromScaled(scaledScore)
                : "—";

            return Results.Ok(new
            {
                attempt = new
                {
                    attempt.Id,
                    attempt.PaperId,
                    status = attempt.Status.ToString(),
                    mode = attempt.Mode.ToString(),
                    scopeQuestionIds = scopeIds,
                    attempt.StartedAt,
                    attempt.SubmittedAt,
                    attempt.RawScore,
                    attempt.MaxRawScore,
                    attempt.ScaledScore,
                    gradeLetter,
                    partADeadlineAt = partADeadline,
                    partBCDeadlineAt = partBCDeadline,
                    partABreakAvailable = attempt.Mode == ReadingAttemptMode.Exam,
                    partABreakResumed = attempt.Mode != ReadingAttemptMode.Exam || attempt.PartABreakUsed,
                    partBCTimerPausedAt = breakWindowActive
                        ? partADeadline
                        : (DateTimeOffset?)null,
                    partBCPausedSeconds = ResolveEffectivePartBCPausedSeconds(attempt, policy, now),
                    partABreakMaxSeconds = attempt.Mode == ReadingAttemptMode.Exam
                        ? ReadingAttemptService.PartABreakMaxSeconds
                        : 0,
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
                partBreakdown,
                skillBreakdown,
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

        // ── Course pathway snapshot (diagnostic → drills → mocks → ready) ──
        group.MapGet("/me/pathway", async (
            IReadingPathwayService pathway, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
            var snap = await pathway.GetPathwayAsync(userId, ct);
            return Results.Ok(snap);
        }).RequireAuthorization();

        // ════════════════════════════════════════════════════════════════
        // Phase 3 — Practice Mode + Error Bank
        // ════════════════════════════════════════════════════════════════

        // ── Start a Learning-mode attempt against a published paper ──────
        group.MapPost("/papers/{paperId}/practice/learning", async (
            string paperId, IReadingAttemptService svc, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            try
            {
                var scopeJson = JsonSerializer.Serialize(new { kind = "learning" });
                var started = await svc.StartInModeAsync(
                    userId, paperId, ReadingAttemptMode.Learning, scopeJson, ct);
                return Results.Ok(new
                {
                    mode = "Learning",
                    started.AttemptId,
                    started.StartedAt,
                    started.DeadlineAt,
                    started.PartADeadlineAt,
                    started.PartBCDeadlineAt,
                    started.PaperTitle,
                    started.PartATimerMinutes,
                    started.PartBCTimerMinutes,
                    playerRoute = $"/reading/paper/{paperId}?attemptId={started.AttemptId}&mode=learning",
                });
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

        // ── List the learner's current Error Bank ────────────────────────
        group.MapGet("/practice/error-bank", async (
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct,
            string? partCode = null,
            int? limit = null) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");

            var take = Math.Clamp(limit ?? 50, 1, 200);
            var query = db.ReadingErrorBankEntries.AsNoTracking()
                .Where(e => e.UserId == userId && !e.IsResolved);
            if (!string.IsNullOrWhiteSpace(partCode)
                && Enum.TryParse<ReadingPartCode>(partCode, ignoreCase: true, out var pc))
            {
                query = query.Where(e => e.PartCode == pc);
            }

            var entries = await query
                .OrderByDescending(e => e.LastSeenWrongAt)
                .Take(take)
                .ToListAsync(ct);

            var questionIds = entries.Select(e => e.ReadingQuestionId).ToList();
            var paperIds = entries.Select(e => e.PaperId).Distinct().ToList();
            var questions = await db.ReadingQuestions.AsNoTracking()
                .Where(q => questionIds.Contains(q.Id))
                .ToDictionaryAsync(q => q.Id, ct);
            var papers = await db.ContentPapers.AsNoTracking()
                .Where(p => paperIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Title, p.Slug })
                .ToDictionaryAsync(p => p.Id, ct);

            var byPart = entries
                .GroupBy(e => e.PartCode)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var summary = await db.ReadingErrorBankEntries.AsNoTracking()
                .Where(e => e.UserId == userId)
                .GroupBy(e => e.IsResolved)
                .Select(g => new { resolved = g.Key, count = g.Count() })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                totals = new
                {
                    open = summary.FirstOrDefault(s => !s.resolved)?.count ?? 0,
                    resolved = summary.FirstOrDefault(s => s.resolved)?.count ?? 0,
                    byPart,
                },
                entries = entries.Select(e =>
                {
                    questions.TryGetValue(e.ReadingQuestionId, out var q);
                    papers.TryGetValue(e.PaperId, out var p);
                    return new
                    {
                        e.Id,
                        e.ReadingQuestionId,
                        partCode = e.PartCode.ToString(),
                        e.TimesWrong,
                        e.LastSeenWrongAt,
                        e.LastWrongAttemptId,
                        questionStem = q?.Stem,
                        questionType = q?.QuestionType.ToString(),
                        skillTag = q?.SkillTag,
                        paper = p is null ? null : new { p.Id, p.Title, p.Slug },
                    };
                }),
            });
        });

        // ── Clear an Error Bank entry (learner is confident) ─────────────
        group.MapDelete("/practice/error-bank/{entryId}", async (
            string entryId,
            LearnerDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            var entry = await db.ReadingErrorBankEntries
                .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId, ct);
            if (entry is null) return Results.NotFound();
            if (entry.IsResolved) return Results.NoContent();
            entry.IsResolved = true;
            entry.ResolvedAt = DateTimeOffset.UtcNow;
            entry.ResolvedReason = "cleared_by_user";
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        // ── Catalogue: list available drill templates ────────────────────
        group.MapGet("/practice/drills", () =>
        {
            return Results.Ok(new
            {
                drills = ReadingDrillCatalogue.All.Select(d => new
                {
                    d.Code,
                    d.Title,
                    d.Description,
                    partCode = d.PartCode.ToString(),
                    d.SkillTag,
                    d.QuestionCount,
                    d.Minutes,
                }),
                miniTests = new[]
                {
                    new { minutes = 5, label = "5-minute mini-test", questionCount = 6 },
                    new { minutes = 10, label = "10-minute mini-test", questionCount = 12 },
                    new { minutes = 15, label = "15-minute mini-test", questionCount = 18 },
                },
            });
        });

        // ── Start a Drill attempt against a paper ────────────────────────
        group.MapPost("/papers/{paperId}/practice/drills/{drillCode}", async (
            string paperId, string drillCode,
            IReadingAttemptService svc, LearnerDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");

            var template = ReadingDrillCatalogue.Find(drillCode);
            if (template is null)
                return Results.BadRequest(new
                {
                    code = "drill_unknown",
                    error = $"Unknown drill code '{drillCode}'.",
                    message = $"Unknown drill code '{drillCode}'.",
                });

            var sample = await ReadingPracticeSampler.SampleAsync(
                db, paperId, template.PartCode, template.SkillTag, template.QuestionCount, ct);
            if (sample.Count == 0)
                return Results.BadRequest(new
                {
                    code = "drill_no_questions",
                    error = "No matching questions are authored for this drill on this paper.",
                    message = "No matching questions are authored for this drill on this paper.",
                });

            var scope = new
            {
                kind = "drill",
                drillCode = template.Code,
                label = template.Title,
                partCode = template.PartCode.ToString(),
                skillTag = template.SkillTag,
                minutes = template.Minutes,
                questionIds = sample,
            };
            try
            {
                var started = await svc.StartInModeAsync(
                    userId, paperId, ReadingAttemptMode.Drill,
                    JsonSerializer.Serialize(scope), ct);
                return Results.Ok(new
                {
                    mode = "Drill",
                    started.AttemptId,
                    started.StartedAt,
                    started.DeadlineAt,
                    started.PaperTitle,
                    minutes = template.Minutes,
                    questionCount = sample.Count,
                    drill = new { template.Code, template.Title, partCode = template.PartCode.ToString() },
                    playerRoute = $"/reading/paper/{paperId}?attemptId={started.AttemptId}&mode=drill",
                });
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

        // ── Start a 5/10/15-minute Mini-Test against a paper ─────────────
        group.MapPost("/papers/{paperId}/practice/mini-test", async (
            string paperId, MiniTestRequest dto,
            IReadingAttemptService svc, LearnerDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");
            if (dto.Minutes is not (5 or 10 or 15))
                return Results.BadRequest(new
                {
                    code = "minitest_minutes_invalid",
                    error = "Mini-test minutes must be 5, 10, or 15.",
                    message = "Mini-test minutes must be 5, 10, or 15.",
                });

            // Heuristic: ~1 question per 50s of available time (matches OET pace).
            var targetCount = dto.Minutes switch { 5 => 6, 10 => 12, _ => 18 };
            var sample = await ReadingPracticeSampler.SampleMixedAsync(db, paperId, targetCount, ct);
            if (sample.Count == 0)
                return Results.BadRequest(new
                {
                    code = "minitest_no_questions",
                    error = "This paper has no authored questions to sample.",
                    message = "This paper has no authored questions to sample.",
                });

            var scope = new
            {
                kind = "mini-test",
                label = $"{dto.Minutes}-minute mini-test",
                minutes = dto.Minutes,
                questionIds = sample,
            };
            try
            {
                var started = await svc.StartInModeAsync(
                    userId, paperId, ReadingAttemptMode.MiniTest,
                    JsonSerializer.Serialize(scope), ct);
                return Results.Ok(new
                {
                    mode = "MiniTest",
                    started.AttemptId,
                    started.StartedAt,
                    started.DeadlineAt,
                    started.PaperTitle,
                    minutes = dto.Minutes,
                    questionCount = sample.Count,
                    playerRoute = $"/reading/paper/{paperId}?attemptId={started.AttemptId}&mode=mini-test",
                });
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

        // ── Start an Error-Bank retest run ───────────────────────────────
        group.MapPost("/practice/error-bank/retest", async (
            ErrorBankRetestRequest dto,
            IReadingAttemptService svc, LearnerDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("auth required");

            var take = Math.Clamp(dto.Limit ?? 10, 1, 50);
            var query = db.ReadingErrorBankEntries.AsNoTracking()
                .Where(e => e.UserId == userId && !e.IsResolved);
            if (!string.IsNullOrWhiteSpace(dto.PartCode)
                && Enum.TryParse<ReadingPartCode>(dto.PartCode, ignoreCase: true, out var pc))
            {
                query = query.Where(e => e.PartCode == pc);
            }

            var entries = await query
                .OrderByDescending(e => e.LastSeenWrongAt)
                .Take(take)
                .Select(e => new { e.ReadingQuestionId, e.PaperId })
                .ToListAsync(ct);
            if (entries.Count == 0)
                return Results.BadRequest(new
                {
                    code = "error_bank_empty",
                    error = "No open Error Bank entries to retest.",
                    message = "No open Error Bank entries to retest.",
                });

            // Group by paper so the attempt's PaperId is well defined. The
            // first paper with the most missed questions wins.
            var topPaperId = entries
                .GroupBy(e => e.PaperId)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;
            var inPaper = entries.Where(e => e.PaperId == topPaperId)
                .Select(e => e.ReadingQuestionId).ToList();
            // Allow ~30 seconds per question
            var minutes = Math.Clamp((int)Math.Ceiling(inPaper.Count * 0.5), 3, 30);

            var scope = new
            {
                kind = "error-bank",
                label = $"Retest {inPaper.Count} missed question(s)",
                minutes,
                questionIds = inPaper,
            };
            try
            {
                var started = await svc.StartInModeAsync(
                    userId, topPaperId, ReadingAttemptMode.ErrorBank,
                    JsonSerializer.Serialize(scope), ct);
                return Results.Ok(new
                {
                    mode = "ErrorBank",
                    started.AttemptId,
                    started.PaperTitle,
                    started.StartedAt,
                    started.DeadlineAt,
                    minutes,
                    questionCount = inPaper.Count,
                    playerRoute = $"/reading/paper/{topPaperId}?attemptId={started.AttemptId}&mode=error-bank",
                });
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

        return app;
    }

    private sealed record MiniTestRequest(int Minutes);
    private sealed record ErrorBankRetestRequest(string? PartCode, int? Limit);

    private static List<string>? ParseScopeQuestionIdsForPlayer(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            if (!doc.RootElement.TryGetProperty("questionIds", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<string>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } s)
                    list.Add(s);
            }
            return list.Count > 0 ? list : null;
        }
        catch (JsonException) { return null; }
    }

    private static (DateTimeOffset PartADeadlineAt, DateTimeOffset PartBCDeadlineAt) ResolveAttemptDeadlines(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy,
        DateTimeOffset now)
    {
        if (attempt.Mode == ReadingAttemptMode.Exam)
        {
            var partADeadline = attempt.StartedAt.AddMinutes(policy.PartATimerMinutes);
            return (
                partADeadline,
                attempt.StartedAt
                    .AddMinutes(policy.PartATimerMinutes + policy.PartBCTimerMinutes)
                    .AddSeconds(ResolveEffectivePartBCPausedSeconds(attempt, policy, now)));
        }

        var answerDeadline = ResolvePracticeAnswerDeadline(attempt, policy);
        return (answerDeadline, answerDeadline);
    }

    private static int ResolveEffectivePartBCPausedSeconds(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy,
        DateTimeOffset now)
    {
        var persisted = Math.Clamp(attempt.PartBCPausedSeconds, 0, ReadingAttemptService.PartABreakMaxSeconds);
        if (attempt.Mode != ReadingAttemptMode.Exam || attempt.PartABreakUsed)
        {
            return persisted;
        }

        var partADeadline = attempt.StartedAt.AddMinutes(policy.PartATimerMinutes);
        var elapsedBreakSeconds = (int)Math.Floor((now - partADeadline).TotalSeconds);
        return Math.Clamp(Math.Max(persisted, elapsedBreakSeconds), 0, ReadingAttemptService.PartABreakMaxSeconds);
    }

    private static DateTimeOffset ResolvePracticeAnswerDeadline(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy)
    {
        if (attempt.DeadlineAt is DateTimeOffset deadline)
        {
            return deadline.AddSeconds(-Math.Max(0, policy.GracePeriodSeconds));
        }

        var minutes = TryReadMinutesFromScope(attempt.ScopeJson)
            ?? (attempt.Mode == ReadingAttemptMode.Learning
                ? Math.Max(60, policy.PartATimerMinutes + policy.PartBCTimerMinutes) * 4
                : policy.PartATimerMinutes + policy.PartBCTimerMinutes);
        return attempt.StartedAt.AddMinutes(minutes);
    }

    private static int? TryReadMinutesFromScope(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            if (doc.RootElement.TryGetProperty("minutes", out var minutesElement)
                && minutesElement.ValueKind == JsonValueKind.Number
                && minutesElement.TryGetInt32(out var minutes)
                && minutes is > 0 and <= 240)
            {
                return minutes;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static IReadOnlyList<object> SafeProjectOptions(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<object>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<object>();
            }

            var options = new List<object>();
            foreach (var option in doc.RootElement.EnumerateArray())
            {
                if (option.ValueKind == JsonValueKind.String)
                {
                    options.Add(option.GetString() ?? string.Empty);
                    continue;
                }

                if (option.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var value = ReadSafeOptionText(option, "value")
                    ?? ReadSafeOptionText(option, "label")
                    ?? ReadSafeOptionText(option, "text")
                    ?? ReadSafeOptionText(option, "title");
                var label = ReadSafeOptionText(option, "label")
                    ?? ReadSafeOptionText(option, "text")
                    ?? ReadSafeOptionText(option, "title")
                    ?? value;
                if (!string.IsNullOrWhiteSpace(label))
                {
                    options.Add(new { value = string.IsNullOrWhiteSpace(value) ? label : value, label });
                }
            }

            return options;
        }
        catch (JsonException)
        {
            return Array.Empty<object>();
        }
    }

    private static string? ReadSafeOptionText(JsonElement option, string propertyName)
    {
        if (!option.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
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
        var partIds = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == paperId)
            .Select(p => p.Id)
            .ToListAsync(ct);
        return await db.ReadingQuestions.AsNoTracking()
            .CountAsync(q => partIds.Contains(q.ReadingPartId), ct);
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
            ShortAnswerNormalisation: "trim_only",
            // OET-faithful default. Synonym acceptance is non-standard mode.
            ShortAnswerAcceptSynonyms: false,
            MatchingAllowPartialCredit: false,
            UnknownTypeFallbackPolicy: "skip_with_zero",
            ShowExplanationsAfterSubmit: false,
            ShowExplanationsOnlyIfWrong: false,
            ShowCorrectAnswerOnReview: false,
            SubmitRateLimitPerMinute: 5,
            AutosaveRateLimitPerMinute: 120,
            ExtraTimeEntitlementPct: 0,
            AllowMultipleConcurrentAttempts: false,
            AllowPausingAttempt: false,
            AllowResumeAfterExpiry: false,
            AllowPaperReadingMode: false);
    }

    private static async Task<bool> CanLearnerSeePaperAsync(
        LearnerDbContext db,
        HttpContext http,
        string userId,
        ContentPaper paper,
        CancellationToken ct)
    {
        if (paper.AppliesToAllProfessions)
        {
            return true;
        }

        var profession = http.User.FindFirstValue("prof") ?? http.User.FindFirstValue("profession");
        if (string.IsNullOrWhiteSpace(profession))
        {
            profession = await db.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.ActiveProfessionId)
                .SingleOrDefaultAsync(ct);
        }

        return !string.IsNullOrWhiteSpace(profession)
            && string.Equals(paper.ProfessionId, profession, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record QuestionPaperAssetDto(
        string Id,
        string? Part,
        string Title,
        string DownloadPath);

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
