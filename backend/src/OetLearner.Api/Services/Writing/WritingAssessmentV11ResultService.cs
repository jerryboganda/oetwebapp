using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public interface IWritingAssessmentV11ResultService
{
    Task<WritingAssessmentV11ReportResponse?> GetForLearnerAsync(
        string userId,
        Guid submissionId,
        CancellationToken ct);
}

/// <summary>
/// Candidate-facing v1.1 projection. It never returns the legacy raw-total
/// field, but does surface an OET grade band derived from the /500 practice
/// score (2026-09-09 owner decision — see WritingCalibrationReleaseService).
/// A report remains an internal restricted snapshot until
/// WritingCalibrationReleaseService.ResolveAsync makes it candidate-visible.
/// </summary>
public sealed class WritingAssessmentV11ResultService(LearnerDbContext db) : IWritingAssessmentV11ResultService
{
    public async Task<WritingAssessmentV11ReportResponse?> GetForLearnerAsync(
        string userId,
        Guid submissionId,
        CancellationToken ct)
    {
        var scenarioId = await db.WritingSubmissions.AsNoTracking()
            .Where(x => x.Id == submissionId && x.UserId == userId)
            .Select(x => (Guid?)x.ScenarioId)
            .FirstOrDefaultAsync(ct);
        if (scenarioId is null) return null;

        var report = await db.WritingAssessmentReportsV11.AsNoTracking()
            .Include(x => x.Facts)
            .Include(x => x.Errors)
            .Include(x => x.Criteria)
            .SingleOrDefaultAsync(x => x.SubmissionId == submissionId, ct);
        if (report is null) return null;

        return Map(report, await LoadVerifiedTaskModelAnswerAsync(db, scenarioId.Value, ct));
    }

    /// <summary>
    /// The Model Answer shown with a result is resolved at READ time: the task's
    /// live saved answer, only while it is verified for candidates under the
    /// running validator (<see cref="WritingTaskModelAnswerService.CandidateVisibleVerified"/>).
    /// Never the per-submission snapshot copied at grading time, so a repaired
    /// and republished answer instantly refreshes every existing result page
    /// and an answer invalidated by a validator change disappears everywhere
    /// (Addendum Rev8 §19.6).
    /// </summary>
    public static Task<WritingTaskModelAnswer?> LoadVerifiedTaskModelAnswerAsync(
        LearnerDbContext db,
        Guid scenarioId,
        CancellationToken ct)
        => db.WritingTaskModelAnswers.AsNoTracking()
            .Where(a => a.ScenarioId == scenarioId)
            .Where(WritingTaskModelAnswerService.CandidateVisibleVerified)
            .FirstOrDefaultAsync(ct);

    public static WritingAssessmentV11ReportResponse Map(
        WritingAssessmentReportV11 report,
        WritingTaskModelAnswer? verifiedModelAnswer)
    {
        var candidateVisible = report.Status == WritingAssessmentV11Status.CandidateReady
            && report.CandidateReportVisible;
        var score = report.CandidateNumericScoreEnabled && candidateVisible
            ? report.EstimatedPracticeScore
            : null;
        var gradeBand = score is { } scoreValue
            ? OetLearner.Api.Services.OetScoring.OetGradeLabel(
                OetLearner.Api.Services.OetScoring.OetGradeLetterFromScaled(scoreValue))
            : null;
        var blockingCodes = ParseBlockingCodes(report.ClassificationJson);
        if (!candidateVisible && blockingCodes.Count == 0)
            blockingCodes = [report.Status.ToString().ToLowerInvariant()];

        var criteria = candidateVisible
            ? report.Criteria
                .OrderBy(x => CriterionOrder(x.CriterionCode))
                .Select(x => new WritingAssessmentV11CriterionResponse(
                    x.CriterionCode,
                    x.Score,
                    x.MaximumScore,
                    x.StrengthObservation,
                    x.LimitationObservation,
                    ParseStringList(x.EvidenceJson),
                    x.ImprovementAction))
                .ToArray()
            : [];
        var errors = candidateVisible
            ? report.Errors
                .OrderBy(x => SeverityRank(x.Severity))
                .ThenBy(x => x.StartOffset ?? int.MaxValue)
                .Select(x => new WritingAssessmentV11ErrorResponse(
                    x.Id.ToString(),
                    x.Location,
                    x.CandidateWording,
                    x.Correction,
                    x.Category,
                    x.RuleSource,
                    x.WhyItMatters,
                    x.Severity,
                    x.Confidence,
                    x.PrimaryCriterionCode,
                    ParseStringList(x.SecondaryCriterionCodesJson),
                    x.StartOffset,
                    x.EndOffset))
                .ToArray()
            : [];
        var facts = candidateVisible
            ? report.Facts.Select(x => new WritingAssessmentV11FactResponse(
                x.FactText,
                x.SourceReference,
                x.Classification,
                x.CandidateStatus,
                x.CandidateExcerpt,
                x.Explanation)).ToArray()
            : [];

        WritingAssessmentV11ModelAnswerResponse? answer = null;
        if (candidateVisible && WritingTaskModelAnswerService.IsVerifiedForCandidates(verifiedModelAnswer))
        {
            answer = new WritingAssessmentV11ModelAnswerResponse(
                verifiedModelAnswer!.Status.ToString(),
                verifiedModelAnswer.ModelAnswerText,
                null,
                [],
                ParseStringList(verifiedModelAnswer.GroundedFactReferencesJson),
                true);
        }

        return new WritingAssessmentV11ReportResponse(
            report.Id.ToString(),
            report.SubmissionId.ToString(),
            report.Status.ToString(),
            report.Profession,
            report.LetterType,
            report.RulePackVersion,
            report.ModelVersion,
            report.CalibrationSetVersion,
            score,
            "AI Estimated Practice Score — not an official OET result",
            gradeBand,
            report.ScoreRange,
            report.ConfidenceLabel,
            report.ConfidenceRange,
            report.CandidateNumericScoreEnabled && candidateVisible,
            candidateVisible,
            blockingCodes,
            candidateVisible ? ParseStringList(report.TopPrioritiesJson) : [],
            candidateVisible ? ParseStringList(report.StrengthsJson) : [],
            candidateVisible ? ParseStringList(report.StudyPlanJson) : [],
            criteria,
            errors,
            facts,
            answer);
    }

    private static List<string> ParseBlockingCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("missingInputCodes", out var missing)
                && !document.RootElement.TryGetProperty("releaseBlockCodes", out missing))
                return [];
            return missing.ValueKind == JsonValueKind.Array
                ? missing.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int CriterionOrder(string code) => code switch
    {
        "purpose" => 0,
        "content" => 1,
        "conciseness_clarity" => 2,
        "genre_style" => 3,
        "organisation_layout" => 4,
        "language" => 5,
        _ => 99,
    };

    private static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => 0,
        "major" => 1,
        "moderate" => 2,
        "minor" => 3,
        _ => 4,
    };
}
