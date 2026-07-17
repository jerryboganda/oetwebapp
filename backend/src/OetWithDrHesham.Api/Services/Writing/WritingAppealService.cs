using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Rulebook;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.Writing;

public sealed record WritingAppealInternalRequest(string Reason);

public sealed record WritingAppealResult(
    Guid AppealId,
    Guid SubmissionId,
    Guid OriginalGradeId,
    Guid? NewGradeId,
    int OriginalRawTotal,
    int? SecondOpinionRawTotal,
    int? FinalRawTotal,
    string? Reasoning,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResolvedAt);

public interface IWritingAppealService
{
    Task<WritingAppealResult> RequestAppealAsync(string userId, Guid submissionId, WritingAppealInternalRequest request, CancellationToken ct);
    Task<IReadOnlyList<WritingAppealResult>> ListForUserAsync(string userId, CancellationToken ct);

    // ── V2 endpoint contract adapter ─────────────────────────────────────────
    Task<WritingScoreAppealResponse?> RequestAppealAsync(string userId, Guid submissionId, string? reason, CancellationToken ct);

    /// <summary>
    /// Returns the latest appeal for <paramref name="submissionId"/> owned
    /// by <paramref name="userId"/>, or <c>null</c> if no appeal exists or
    /// the submission is not theirs. Used by the appeal UI to poll status
    /// without re-triggering the AI call.
    /// </summary>
    Task<WritingScoreAppealResponse?> GetLatestAppealAsync(string userId, Guid submissionId, CancellationToken ct);
}

public sealed class WritingAppealService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    TimeProvider clock,
    IRuntimeSettingsProvider settingsProvider,
    ILogger<WritingAppealService> logger) : IWritingAppealService
{
    public async Task<WritingAppealResult> RequestAppealAsync(string userId, Guid submissionId, WritingAppealInternalRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        // DB-over-env appeals feature flag (admin-configurable, 30s cache).
        if (!(await settingsProvider.GetAsync(ct)).Writing.AppealsEnabled)
        {
            throw ApiException.ServiceUnavailable("writing_appeals_disabled", "Score appeals are currently disabled.");
        }
        var submission = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("writing_submission_not_found", "Submission was not found.");
        var grade = await db.WritingGrades
            .Where(g => g.SubmissionId == submissionId)
            .OrderByDescending(g => g.AppealedByGradeId != null || g.TutorReviewId != null)
            .ThenByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Conflict("writing_grade_missing", "Submission is not yet graded.");

        var existingAppeal = await db.WritingScoreAppeals.FirstOrDefaultAsync(a => a.SubmissionId == submissionId && a.Status != "resolved", ct);
        if (existingAppeal is not null)
        {
            var originalAppealGrade = await db.WritingGrades.AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == existingAppeal.OriginalGradeId, ct) ?? grade;
            var existingNewGrade = existingAppeal.NewGradeId is { } newGradeId
                ? await db.WritingGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == newGradeId, ct)
                : null;
            return ToResult(existingAppeal, originalAppealGrade, existingNewGrade);
        }

        var now = clock.GetUtcNow();
        var appeal = new WritingScoreAppeal
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            OriginalGradeId = grade.Id,
            UserId = userId,
            Reason = request.Reason ?? string.Empty,
            Status = "pending",
            RequestedAt = now,
        };
        db.WritingScoreAppeals.Add(appeal);
        await db.SaveChangesAsync(ct);

        try
        {
            var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Task = AiTaskMode.Score,
            });
            var input = BuildAppealInput(submission, grade, request.Reason);
            var result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = input,
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.WritingAppealV1,
                PromptTemplateId = "writing.appeal.v1",
                UserId = userId,
            }, ct);
            var parsed = ParseAppealResponse(result.Completion);
            appeal.Status = "in_progress";
            appeal.ResolutionNote = parsed.Rationale;

            if (parsed.Scores is not null)
            {
                var originalTotal = grade.RawTotal;
                var newTotal = parsed.Scores.Sum();
                appeal.DeltaRawPoints = newTotal - originalTotal;
                if (Math.Abs(appeal.DeltaRawPoints.Value) > 3)
                {
                    var avg = AverageScores(grade, parsed.Scores);
                    var appealGrade = CloneAdjustedGrade(grade, avg, clock.GetUtcNow(), "appeal_adjusted");
                    db.WritingGrades.Add(appealGrade);
                    appeal.NewGradeId = appealGrade.Id;
                    appeal.Resolution = "averaged";
                }
                else
                {
                    appeal.Resolution = "no_change";
                }
            }
            appeal.Status = "resolved";
            appeal.ResolvedAt = clock.GetUtcNow();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing appeal LLM call failed for submission {SubmissionId}", submissionId);
            appeal.Status = "pending_manual";
            appeal.ResolutionNote = "AI appeal service unavailable; tutor review queued.";
        }

        await db.SaveChangesAsync(ct);
        var newGrade = appeal.NewGradeId is { } resolvedGradeId
            ? await db.WritingGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == resolvedGradeId, ct)
            : null;
        return ToResult(appeal, grade, newGrade);
    }

    public async Task<IReadOnlyList<WritingAppealResult>> ListForUserAsync(string userId, CancellationToken ct)
    {
        var appeals = await db.WritingScoreAppeals.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.RequestedAt)
            .ToListAsync(ct);
        if (appeals.Count == 0) return Array.Empty<WritingAppealResult>();
        var gradeIds = appeals.SelectMany(a => a.NewGradeId is { } newGradeId
                ? new[] { a.OriginalGradeId, newGradeId }
                : new[] { a.OriginalGradeId })
            .Distinct()
            .ToList();
        var grades = await db.WritingGrades.AsNoTracking()
            .Where(g => gradeIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g, ct);
        return appeals.Select(a => ToResult(
            a,
            grades.GetValueOrDefault(a.OriginalGradeId),
            a.NewGradeId is { } newGradeId ? grades.GetValueOrDefault(newGradeId) : null)).ToList();
    }

    private static string BuildAppealInput(WritingSubmission submission, WritingGrade grade, string reason)
    {
        return string.Join('\n',
            "Submission to re-score (second opinion).",
            $"Letter content:\n---\n{submission.LetterContent}\n---",
            $"Original scores: C1={grade.C1Purpose}/3 C2={grade.C2Content}/7 C3={grade.C3Conciseness}/7 C4={grade.C4Genre}/7 C5={grade.C5Organisation}/7 C6={grade.C6Language}/7 raw={grade.RawTotal}",
            $"Original band: {grade.BandLabel}",
            "Learner's appeal reason:",
            reason,
            "Return JSON { c1, c2, c3, c4, c5, c6, rawTotal, estimatedBand, rationale } scoring the same letter independently.");
    }

    private static AppealParsedResponse ParseAppealResponse(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return new AppealParsedResponse(null, null);
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return new AppealParsedResponse(null, null);
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            int Get(string name, int max)
            {
                if (!doc.RootElement.TryGetProperty(name, out var el) || !el.TryGetInt32(out var v)) return 0;
                return Math.Clamp(v, 0, max);
            }
            var scores = new[]
            {
                Get("c1", 3),
                Get("c2", 7),
                Get("c3", 7),
                Get("c4", 7),
                Get("c5", 7),
                Get("c6", 7),
            };
            var rationale = doc.RootElement.TryGetProperty("rationale", out var rEl) && rEl.ValueKind == JsonValueKind.String ? rEl.GetString() : null;
            return new AppealParsedResponse(scores, rationale);
        }
        catch (JsonException) { return new AppealParsedResponse(null, null); }
    }

    private static int[] AverageScores(WritingGrade grade, int[] appeal)
    {
        var original = new[] { (int)grade.C1Purpose, (int)grade.C2Content, (int)grade.C3Conciseness, (int)grade.C4Genre, (int)grade.C5Organisation, (int)grade.C6Language };
        var averaged = new int[6];
        for (var i = 0; i < 6; i++)
        {
            averaged[i] = (int)Math.Round((original[i] + appeal[i]) / 2.0);
        }
        return averaged;
    }

    private static WritingGrade CloneAdjustedGrade(WritingGrade original, IReadOnlyList<int> scores, DateTimeOffset now, string confidenceFlag)
    {
        var rawTotal = scores.Sum();
        return new WritingGrade
        {
            Id = Guid.NewGuid(),
            SubmissionId = original.SubmissionId,
            C1Purpose = (short)scores[0],
            C2Content = (short)scores[1],
            C3Conciseness = (short)scores[2],
            C4Genre = (short)scores[3],
            C5Organisation = (short)scores[4],
            C6Language = (short)scores[5],
            RawTotal = (short)rawTotal,
            EstimatedBand = rawTotal,
            BandLabel = RawBandLabel(rawTotal),
            PerCriterionFeedbackJson = original.PerCriterionFeedbackJson,
            TopThreePrioritiesJson = original.TopThreePrioritiesJson,
            ConfidenceFlag = confidenceFlag,
            ModelUsed = original.ModelUsed,
            CanonVersion = original.CanonVersion,
            AppealedByGradeId = original.Id,
            GradedAt = now,
            CreatedAt = now,
        };
    }

    private static string RawBandLabel(int rawTotal)
    {
        if (rawTotal >= 38) return "A";
        if (rawTotal >= 34) return "B+";
        if (rawTotal >= 30) return "B";
        if (rawTotal >= 24) return "C+";
        if (rawTotal >= 18) return "C";
        if (rawTotal >= 12) return "D";
        return "E";
    }

    private static WritingAppealResult ToResult(WritingScoreAppeal appeal, WritingGrade? originalGrade, WritingGrade? newGrade)
    {
        return new WritingAppealResult(
            appeal.Id,
            appeal.SubmissionId,
            appeal.OriginalGradeId,
            appeal.NewGradeId,
            originalGrade?.RawTotal ?? 0,
            appeal.DeltaRawPoints.HasValue ? (originalGrade?.RawTotal ?? 0) + appeal.DeltaRawPoints.Value : null,
            appeal.Resolution == "averaged" ? newGrade?.RawTotal : null,
            appeal.ResolutionNote,
            appeal.Status,
            appeal.RequestedAt,
            appeal.ResolvedAt);
    }

    private sealed record AppealParsedResponse(int[]? Scores, string? Rationale);

    public async Task<WritingScoreAppealResponse?> RequestAppealAsync(string userId, Guid submissionId, string? reason, CancellationToken ct)
    {
        try
        {
            var result = await RequestAppealAsync(userId, submissionId, new WritingAppealInternalRequest(reason ?? string.Empty), ct);
            return WritingV2ResponseMapper.ToResponse(result);
        }
        catch (ApiException ex) when (ex.ErrorCode == "writing_submission_not_found")
        {
            return null;
        }
    }

    public async Task<WritingScoreAppealResponse?> GetLatestAppealAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        // Ownership check first — never leak appeal status across users.
        var submissionOwned = await db.WritingSubmissions.AsNoTracking()
            .AnyAsync(s => s.Id == submissionId && s.UserId == userId, ct);
        if (!submissionOwned) return null;

        var appeal = await db.WritingScoreAppeals.AsNoTracking()
            .Where(a => a.SubmissionId == submissionId && a.UserId == userId)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (appeal is null) return null;

        var originalGrade = await db.WritingGrades.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == appeal.OriginalGradeId, ct);
        var newGrade = appeal.NewGradeId is { } newGradeId
            ? await db.WritingGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == newGradeId, ct)
            : null;
        return WritingV2ResponseMapper.ToResponse(ToResult(appeal, originalGrade, newGrade));
    }
}
