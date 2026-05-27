using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public interface IWritingSubmissionService
{
    Task<WritingSubmissionResponse> CreateSubmissionAsync(string userId, WritingSubmissionCreateRequest request, CancellationToken ct);
    Task<WritingSubmissionResponse?> GetSubmissionAsync(string userId, Guid submissionId, CancellationToken ct);
    Task<WritingGradeResponseV2?> GetSubmissionGradeAsync(string userId, Guid submissionId, CancellationToken ct);
    Task<WritingSubmissionResponse?> ReviseSubmissionAsync(string userId, Guid originalSubmissionId, WritingReviseRequest request, CancellationToken ct);
}

/// <summary>
/// Learner-facing submission orchestration. Persists a <see cref="WritingSubmission"/>
/// via the V2 evaluation pipeline, then returns immediately while grading runs
/// asynchronously. Ownership is enforced on every method: a learner can only
/// see/revise their own submissions.
/// </summary>
public sealed class WritingSubmissionService(
    LearnerDbContext db,
    IWritingSubmissionEvaluationPipeline pipeline,
    IWritingExemplarService exemplarService,
    ILogger<WritingSubmissionService> logger) : IWritingSubmissionService
{
    public async Task<WritingSubmissionResponse> CreateSubmissionAsync(string userId, WritingSubmissionCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scenarioExists = await db.WritingScenarios.AsNoTracking().AnyAsync(s => s.Id == request.ScenarioId, ct);
        if (!scenarioExists)
        {
            throw ApiException.NotFound("writing_scenario_not_found", "Scenario was not found.");
        }
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(0, request.TimeSpentSeconds));
        var submissionId = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
            UserId: userId,
            ScenarioId: request.ScenarioId,
            Mode: NormalizeMode(request.Mode),
            GradingTier: "express",
            InputSource: NormalizeInputSource(request.InputSource),
            LetterContent: request.LetterContent,
            TimeSpentSeconds: request.TimeSpentSeconds,
            StartedAt: startedAt,
            IsRevision: false,
            OriginalSubmissionId: null), ct);
        var outcome = await pipeline.EvaluateAsync(submissionId, ct);
        await EnsureGradeForSubmissionAsync(submissionId, outcome, ct);

        var entity = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw new InvalidOperationException("Submission missing after create.");
        return WritingV2ResponseMapper.ToSubmissionResponse(entity);
    }

    public async Task<WritingSubmissionResponse?> GetSubmissionAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var s = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == submissionId && x.UserId == userId, ct);
        return s is null ? null : WritingV2ResponseMapper.ToSubmissionResponse(s);
    }

    public async Task<WritingGradeResponseV2?> GetSubmissionGradeAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var s = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == submissionId && x.UserId == userId, ct);
        if (s is null) return null;
        var grade = await db.WritingGrades.AsNoTracking()
            .Where(g => g.SubmissionId == submissionId)
            .OrderByDescending(g => g.AppealedByGradeId != null || g.TutorReviewId != null)
            .ThenByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct);
        if (grade is null) return null;
        var violations = await db.WritingCanonViolations.AsNoTracking()
            .Where(v => v.SubmissionId == submissionId)
            .ToListAsync(ct);
        var ruleIds = violations.Select(v => v.RuleId).Distinct().ToList();
        var ruleText = await db.WritingCanonRules.AsNoTracking()
            .Where(r => ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.RuleText, ct);

        WritingExemplarComparisonResponse? comparison = null;
        try
        {
            var exemplar = await exemplarService.GetClosestToScenarioAsync(userId, s.ScenarioId, ct);
            if (exemplar is not null)
            {
                comparison = new WritingExemplarComparisonResponse(
                    ExemplarId: exemplar.Id,
                    ExemplarLetterType: exemplar.LetterType,
                    SimilarityScore: 0.0,
                    HighlightedDifferences: Array.Empty<WritingExemplarComparisonHighlightResponse>());
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Exemplar comparison lookup failed (non-fatal) for submission {SubmissionId}.", submissionId);
        }

        return WritingV2ResponseMapper.ToGradeResponse(grade, violations, ruleText, comparison);
    }

    public async Task<WritingSubmissionResponse?> ReviseSubmissionAsync(string userId, Guid originalSubmissionId, WritingReviseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var original = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == originalSubmissionId && x.UserId == userId, ct);
        if (original is null) return null;
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(0, request.TimeSpentSeconds));
        var newId = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
            UserId: userId,
            ScenarioId: original.ScenarioId,
            Mode: original.Mode,
            GradingTier: original.GradingTier,
            InputSource: original.InputSource,
            LetterContent: request.LetterContent,
            TimeSpentSeconds: request.TimeSpentSeconds,
            StartedAt: startedAt,
            IsRevision: true,
            OriginalSubmissionId: originalSubmissionId), ct);
        var outcome = await pipeline.EvaluateAsync(newId, ct);
        await EnsureGradeForSubmissionAsync(newId, outcome, ct);
        var entity = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == newId, ct)
            ?? throw new InvalidOperationException("Revision submission missing after create.");
        return WritingV2ResponseMapper.ToSubmissionResponse(entity);
    }

    private async Task EnsureGradeForSubmissionAsync(Guid submissionId, WritingSubmissionGradeOutcome outcome, CancellationToken ct)
    {
        if (await db.WritingGrades.AsNoTracking().AnyAsync(g => g.SubmissionId == submissionId, ct)) return;
        var reused = await db.WritingGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == outcome.GradeId, ct);
        if (reused is null) return;
        db.WritingGrades.Add(new WritingGrade
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            C1Purpose = reused.C1Purpose,
            C2Content = reused.C2Content,
            C3Conciseness = reused.C3Conciseness,
            C4Genre = reused.C4Genre,
            C5Organisation = reused.C5Organisation,
            C6Language = reused.C6Language,
            RawTotal = reused.RawTotal,
            EstimatedBand = reused.EstimatedBand,
            BandLabel = reused.BandLabel,
            PerCriterionFeedbackJson = reused.PerCriterionFeedbackJson,
            TopThreePrioritiesJson = reused.TopThreePrioritiesJson,
            ConfidenceFlag = reused.ConfidenceFlag,
            ModelUsed = reused.ModelUsed,
            CanonVersion = reused.CanonVersion,
            GradedAt = reused.GradedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var reusedViolations = await db.WritingCanonViolations.AsNoTracking()
            .Where(v => v.SubmissionId == reused.SubmissionId)
            .ToListAsync(ct);
        foreach (var violation in reusedViolations)
        {
            db.WritingCanonViolations.Add(new WritingCanonViolation
            {
                Id = Guid.NewGuid(),
                SubmissionId = submissionId,
                RuleId = violation.RuleId,
                Severity = violation.Severity,
                Snippet = violation.Snippet,
                LineNumber = violation.LineNumber,
                CharStart = violation.CharStart,
                CharEnd = violation.CharEnd,
                SuggestedFix = violation.SuggestedFix,
                Disputed = violation.Disputed,
                DisputeResolution = violation.DisputeResolution,
                DetectedAt = violation.DetectedAt,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "practice";
        return mode.Trim().ToLowerInvariant() switch
        {
            "practice" => "practice",
            "coached" => "coached",
            "timed" => "timed",
            "diagnostic" => "diagnostic",
            "mock" => "mock",
            "revision" => "revision",
            _ => throw ApiException.Validation("writing_submission_invalid_mode", "Unsupported writing submission mode."),
        };
    }

    private static string NormalizeInputSource(string? inputSource)
    {
        if (string.IsNullOrWhiteSpace(inputSource)) return "editor";
        return inputSource.Trim().ToLowerInvariant() switch
        {
            "editor" or "typed" => "editor",
            "paper-ocr" => "paper-ocr",
            "voice-draft" => "voice-draft",
            _ => throw ApiException.Validation("writing_submission_invalid_input_source", "Unsupported writing input source."),
        };
    }
}
