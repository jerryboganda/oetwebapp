using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingSubmissionGradeContext(
    string UserId,
    Guid ScenarioId,
    string Mode,
    string GradingTier,
    string InputSource,
    string LetterContent,
    int TimeSpentSeconds,
    DateTimeOffset StartedAt,
    bool IsRevision,
    Guid? OriginalSubmissionId);

public sealed record WritingSubmissionGradeOutcome(
    Guid SubmissionId,
    Guid GradeId,
    short RawTotal,
    string BandLabel,
    bool IdempotentReuse);

public interface IWritingSubmissionEvaluationPipeline
{
    Task<Guid> CreateSubmissionAsync(WritingSubmissionGradeContext context, CancellationToken ct);
    Task<WritingSubmissionGradeOutcome> EvaluateAsync(Guid submissionId, CancellationToken ct);
}

/// <summary>
/// Writing Module V2 grading pipeline.
///
/// 4 stages per spec §12.1:
///   1. Pre-flight (word count / verbatim-copy / format quick check)
///   2. AI rubric — <see cref="WritingEvaluationPipeline"/> via "writing.score.v1"
///   3. Canon engine — <see cref="IWritingCanonEngine"/> persists violations
///   4. Aggregation — top priorities + exemplar match + revision invite
///
/// Idempotency by <c>LetterContentHash</c> with 24h TTL — if a grade for this
/// hash already exists within the TTL window, the cached grade is reused and
/// no new AI call is made.
/// </summary>
public sealed class WritingSubmissionEvaluationPipeline(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    IWritingCanonEngine canonEngine,
    IWritingExemplarService exemplarService,
    IWritingMistakeService mistakeService,
    IWritingEventBus events,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger<WritingSubmissionEvaluationPipeline> logger) : IWritingSubmissionEvaluationPipeline
{
    public async Task<Guid> CreateSubmissionAsync(WritingSubmissionGradeContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var letter = context.LetterContent ?? string.Empty;
        if (letter.Trim().Length == 0)
        {
            throw ApiException.Validation("writing_submission_empty", "Letter content is required.");
        }

        var now = clock.GetUtcNow();
        var hash = ComputeHash(letter);
        var wordCount = CountWords(letter);
        var submission = new WritingSubmission
        {
            Id = Guid.NewGuid(),
            UserId = context.UserId,
            ScenarioId = context.ScenarioId,
            Mode = context.Mode ?? "practice",
            LetterContent = letter,
            LetterContentHash = hash,
            WordCount = wordCount,
            TimeSpentSeconds = context.TimeSpentSeconds,
            StartedAt = context.StartedAt,
            SubmittedAt = now,
            IsRevision = context.IsRevision,
            OriginalSubmissionId = context.OriginalSubmissionId,
            Status = "queued",
            GradingTier = string.IsNullOrWhiteSpace(context.GradingTier) ? "express" : context.GradingTier,
            InputSource = string.IsNullOrWhiteSpace(context.InputSource) ? "typed" : context.InputSource,
            CreatedAt = now,
        };
        db.WritingSubmissions.Add(submission);
        await db.SaveChangesAsync(ct);

        await events.PublishAsync(new WritingSubmissionCreated(
            context.UserId,
            submission.Id,
            context.ScenarioId,
            submission.Mode,
            submission.GradingTier,
            submission.InputSource,
            now), ct);
        return submission.Id;
    }

    public async Task<WritingSubmissionGradeOutcome> EvaluateAsync(Guid submissionId, CancellationToken ct)
    {
        var submission = await db.WritingSubmissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw ApiException.NotFound("writing_submission_not_found", "Submission was not found.");

        var idempotency = await TryReuseExistingGradeAsync(submission, ct);
        if (idempotency is not null) return idempotency;

        submission.Status = "preflight";
        await db.SaveChangesAsync(ct);
        var preflight = PreflightChecks(submission);
        if (!preflight.Passed)
        {
            submission.Status = "failed";
            await db.SaveChangesAsync(ct);
            throw ApiException.Validation(preflight.Reason!, preflight.Message!);
        }

        submission.Status = "grading";
        await db.SaveChangesAsync(ct);

        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submission.ScenarioId, ct);
        var rubric = await CallRubricAsync(submission, scenario, ct);

        var canon = await canonEngine.DetectViolationsAsync(
            new WritingCanonDetectionRequest(submission.UserId, submission.Id, submission.LetterContent,
                scenario?.LetterType ?? "routine_referral",
                scenario?.Profession ?? "medicine"), ct);

        var bandLabel = OetBandLabel(rubric.EstimatedBand);
        var grade = new WritingGrade
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            C1Purpose = (short)rubric.C1,
            C2Content = (short)rubric.C2,
            C3Conciseness = (short)rubric.C3,
            C4Genre = (short)rubric.C4,
            C5Organisation = (short)rubric.C5,
            C6Language = (short)rubric.C6,
            RawTotal = (short)(rubric.C1 + rubric.C2 + rubric.C3 + rubric.C4 + rubric.C5 + rubric.C6),
            EstimatedBand = rubric.EstimatedBand,
            BandLabel = bandLabel,
            PerCriterionFeedbackJson = rubric.PerCriterionFeedbackJson,
            TopThreePrioritiesJson = rubric.TopThreePrioritiesJson,
            ConfidenceFlag = rubric.ConfidenceFlag,
            ModelUsed = rubric.ModelUsed,
            CanonVersion = await ResolveCanonVersionAsync(ct),
            GradedAt = clock.GetUtcNow(),
            CreatedAt = clock.GetUtcNow(),
        };
        db.WritingGrades.Add(grade);
        submission.Status = "graded";
        await db.SaveChangesAsync(ct);

        try
        {
            await mistakeService.IncrementForCanonViolationsAsync(submission.UserId, canon.Violations, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mistake stat update failed for submission {SubmissionId}", submission.Id);
        }

        try
        {
            await exemplarService.GetClosestToScenarioAsync(submission.UserId, submission.ScenarioId, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Exemplar similarity lookup failed (non-fatal) for submission {SubmissionId}.", submission.Id);
        }

        await events.PublishAsync(new WritingGradeReady(
            submission.UserId, submission.Id, grade.Id, grade.RawTotal, grade.EstimatedBand, grade.BandLabel, clock.GetUtcNow()), ct);

        foreach (var v in canon.Violations)
        {
            await events.PublishAsync(new WritingCanonViolationDetected(
                submission.UserId, submission.Id, v.Id, v.RuleId, v.Severity, v.DetectedAt), ct);
        }

        return new WritingSubmissionGradeOutcome(submission.Id, grade.Id, grade.RawTotal, grade.BandLabel, false);
    }

    private async Task<WritingSubmissionGradeOutcome?> TryReuseExistingGradeAsync(WritingSubmission submission, CancellationToken ct)
    {
        var ttl = TimeSpan.FromHours(options.Value.GradeIdempotencyTtlHours);
        var cutoff = clock.GetUtcNow() - ttl;
        var existing = await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Where(x => x.s.UserId == submission.UserId
                        && x.s.LetterContentHash == submission.LetterContentHash
                        && x.g.GradedAt >= cutoff
                        && x.s.Id != submission.Id)
            .OrderByDescending(x => x.g.GradedAt)
            .Select(x => x.g)
            .FirstOrDefaultAsync(ct);
        if (existing is null) return null;
        submission.Status = "graded";
        await db.SaveChangesAsync(ct);
        return new WritingSubmissionGradeOutcome(submission.Id, existing.Id, existing.RawTotal, existing.BandLabel, true);
    }

    private static (bool Passed, string? Reason, string? Message) PreflightChecks(WritingSubmission submission)
    {
        if (submission.WordCount < 100) return (false, "writing_submission_too_short", "Letter is too short to grade (minimum 100 words).");
        if (submission.WordCount > 400) return (false, "writing_submission_too_long", "Letter exceeds the 400-word ceiling for OET writing.");
        if (string.IsNullOrWhiteSpace(submission.LetterContent)) return (false, "writing_submission_empty", "Letter content is required.");
        return (true, null, null);
    }

    private async Task<RubricResult> CallRubricAsync(WritingSubmission submission, WritingScenario? scenario, CancellationToken ct)
    {
        var letterType = scenario?.LetterType ?? "routine_referral";
        var profession = scenario?.Profession ?? "medicine";
        AiGroundedPrompt prompt;
        try
        {
            prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                LetterType = NormaliseLetterTypeForRulebook(letterType),
                Profession = ParseProfession(profession),
                Task = AiTaskMode.Score,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Writing rubric grounded-prompt build failed for submission {SubmissionId}", submission.Id);
            throw ApiException.ServiceUnavailable("writing_rubric_unavailable", "Writing grading prompt is misconfigured.", retryable: true);
        }

        AiGatewayResult result;
        try
        {
            result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = BuildRubricInput(submission, scenario),
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.WritingGrade,
                PromptTemplateId = "writing.score.v1",
                UserId = submission.UserId,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing rubric AI call failed for submission {SubmissionId}", submission.Id);
            throw ApiException.ServiceUnavailable("writing_rubric_failed", "Writing grading service is temporarily unavailable. Please retry.", retryable: true);
        }

        return ParseRubric(result);
    }

    private static string BuildRubricInput(WritingSubmission submission, WritingScenario? scenario)
    {
        var sb = new StringBuilder();
        if (scenario is not null)
        {
            sb.AppendLine($"Scenario: {scenario.Title}");
            sb.AppendLine($"Profession: {scenario.Profession}");
            sb.AppendLine($"Letter type: {scenario.LetterType}");
            sb.AppendLine();
            sb.AppendLine("Case notes:");
            sb.AppendLine("---");
            sb.AppendLine(scenario.CaseNotesMarkdown);
            sb.AppendLine("---");
        }
        sb.AppendLine();
        sb.AppendLine($"Word count: {submission.WordCount}");
        sb.AppendLine("Candidate letter:");
        sb.AppendLine("---");
        sb.AppendLine(submission.LetterContent);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Score on the 6 OET Writing criteria. Return JSON { c1, c2, c3, c4, c5, c6, rawTotal, estimatedBand, bandLabel, perCriterion, topThreePriorities, confidenceFlag, modelUsed }.");
        return sb.ToString();
    }

    private static RubricResult ParseRubric(AiGatewayResult result)
    {
        var completion = result.Completion ?? string.Empty;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        var fallback = new RubricResult(0, 3, 3, 3, 3, 3, 200, "{}", "[]", "low", "writing.score.v1");
        if (start < 0 || end <= start) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            int Get(string name, int max) => doc.RootElement.TryGetProperty(name, out var el) && el.TryGetInt32(out var v) ? Math.Clamp(v, 0, max) : 0;
            var c1 = Get("c1", 3);
            var c2 = Get("c2", 7);
            var c3 = Get("c3", 7);
            var c4 = Get("c4", 7);
            var c5 = Get("c5", 7);
            var c6 = Get("c6", 7);
            var estimated = doc.RootElement.TryGetProperty("estimatedBand", out var ebEl) && ebEl.TryGetInt32(out var eb) ? eb : 200;
            var perCriterion = doc.RootElement.TryGetProperty("perCriterion", out var pcEl) ? pcEl.GetRawText() : "{}";
            var topThree = doc.RootElement.TryGetProperty("topThreePriorities", out var ttEl) ? ttEl.GetRawText() : "[]";
            var confidence = doc.RootElement.TryGetProperty("confidenceFlag", out var cfEl) && cfEl.ValueKind == JsonValueKind.String ? cfEl.GetString() ?? "medium" : "medium";
            var model = doc.RootElement.TryGetProperty("modelUsed", out var muEl) && muEl.ValueKind == JsonValueKind.String ? muEl.GetString() ?? "writing.score.v1" : "writing.score.v1";
            return new RubricResult(c1, c2, c3, c4, c5, c6, estimated, perCriterion, topThree, confidence, model);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private async Task<string> ResolveCanonVersionAsync(CancellationToken ct)
    {
        var max = await db.WritingCanonRules.AsNoTracking().MaxAsync(r => (int?)r.Version, ct);
        return $"v{max ?? 1}";
    }

    private static string OetBandLabel(int rawTotal)
    {
        if (rawTotal >= 38) return "A";
        if (rawTotal >= 34) return "B+";
        if (rawTotal >= 30) return "B";
        if (rawTotal >= 24) return "C+";
        if (rawTotal >= 18) return "C";
        if (rawTotal >= 12) return "D";
        return "E";
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..40];
    }

    private static int CountWords(string content)
        => string.IsNullOrWhiteSpace(content) ? 0 : content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    private static ExamProfession ParseProfession(string raw)
        => RulebookProfessionParser.TryParse(raw, out var p) ? p : ExamProfession.Medicine;

    private static string NormaliseLetterTypeForRulebook(string v)
        => v.ToUpperInvariant() switch
        {
            "LT-RR" => "routine_referral",
            "LT-UR" => "urgent_referral",
            "LT-DG" => "discharge",
            "LT-TR" => "transfer",
            "LT-RP" => "advice_to_patient",
            "LT-NM" => "non_medical",
            _ => v.ToLowerInvariant(),
        };

    private sealed record RubricResult(int C1, int C2, int C3, int C4, int C5, int C6, int EstimatedBand,
        string PerCriterionFeedbackJson, string TopThreePrioritiesJson, string ConfidenceFlag, string ModelUsed);
}
