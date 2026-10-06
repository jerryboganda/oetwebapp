using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

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
/// <para/>
/// This projection is the candidate chokepoint (owner handoff, 6 Oct 2026): the stored report is never
/// changed, but nothing internal leaves it. No rule id, validator label, provenance tag, model or pack
/// version, confidence range or blocking code is returned (the keys stay for one release and carry
/// empty or null values), severities are critical | major | minor | advisory, every explanation is run
/// through <see cref="WritingCandidateText"/>, and only the candidate's own wording stays verbatim.
/// </summary>
public sealed class WritingAssessmentV11ResultService(LearnerDbContext db, TimeProvider? clock = null) : IWritingAssessmentV11ResultService
{
    private const string DetectorErrorRuleId = "BUILTIN.internal_detector_error";

    // The text a rewrite has when the finding carries no suggested fix of its own.
    private const string DefaultCorrection = "Rewrite this wording so it reads as a clear, formal clinical letter.";

    public async Task<WritingAssessmentV11ReportResponse?> GetForLearnerAsync(
        string userId,
        Guid submissionId,
        CancellationToken ct)
    {
        var submission = await db.WritingSubmissions.AsNoTracking()
            .Where(x => x.Id == submissionId && x.UserId == userId)
            .Select(x => new { x.ScenarioId, x.Status, x.SubmittedAt })
            .FirstOrDefaultAsync(ct);
        if (submission is null) return null;

        // The 15-minute release window: the result exists only once the grade AND its review are complete
        // and the window has elapsed (allowlisted accounts skip the wait). Until then it is a 404, exactly
        // like a result that is not ready, so nothing is fabricated and nothing leaks early. The allowlist
        // lookup is only needed while the window is still open.
        var now = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var unrestricted = submission.Status == WritingSubmissionStatuses.Graded
            && now < submission.SubmittedAt + WritingGradeTimings.ResultReleaseWindow
            && await WritingUnrestrictedAccounts.IsUnrestrictedAsync(db, userId, ct);
        var release = WritingResultRelease.Describe(submission.Status, submission.SubmittedAt, unrestricted, now);
        if (release.State != WritingResultRelease.Released) return null;

        var report = await db.WritingAssessmentReportsV11.AsNoTracking()
            .Include(x => x.Facts)
            .Include(x => x.Errors)
            .Include(x => x.Criteria)
            .SingleOrDefaultAsync(x => x.SubmissionId == submissionId, ct);
        if (report is null) return null;

        return Map(report, await LoadVerifiedTaskModelAnswerAsync(db, submission.ScenarioId, ct));
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
        int? score = report.CandidateNumericScoreEnabled && candidateVisible
            ? report.EstimatedPracticeScore
            : null;
        if (score is { } unroundedScore)
            score = OetLearner.Api.Services.OetScoring.OetReportedScaledScore(unroundedScore);
        var gradeBand = score is { } scoreValue
            ? OetLearner.Api.Services.OetScoring.OetGradeLabel(
                OetLearner.Api.Services.OetScoring.OetGradeLetterFromScaled(scoreValue))
            : null;

        // A rule check that crashed is an internal fault, never a finding about the candidate's letter. A row
        // the secondary reviewer flagged as a duplicate correction (IsGroupedDuplicate) is skipped here, at
        // the source, so it reaches none of the corrections, the digest findings, the criterion summaries,
        // the Top Priorities or the repeat counts below.
        var storedErrors = candidateVisible
            ? report.Errors
                .Where(x => !x.IsGroupedDuplicate
                    && !string.Equals(x.RuleSource, DetectorErrorRuleId, StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : [];

        // Severity repair at read time (also repairs reports stored, or cloned by grade reuse, before the
        // policy existed): coaching-only findings become advisory and sort last, caps and repeats apply.
        var calibrated = WritingCandidateSeverityPolicy.CalibrateAll(
            storedErrors.Select(x => WritingCandidateSeverityPolicy.InputFor(x.RuleSource, x.Severity)).ToList());
        var rows = storedErrors
            .Select((error, i) => (
                Error: error,
                Severity: calibrated[i],
                Scored: WritingReportDigest.IsScoreBearing(error.RuleSource, calibrated[i])))
            .OrderBy(x => x.Scored ? 0 : 1)
            .ThenBy(x => SeverityRank(x.Severity))
            .ThenBy(x => x.Error.StartOffset ?? int.MaxValue)
            .ToArray();

        // Impact-ordered digest input: priorities and criterion summaries are recomputed here on every
        // read, so reports stored before the digest (duplicate priorities, long per-criterion text) are
        // repaired too. Advisory findings never take a slot.
        var digestFindings = rows
            .Select(x => new WritingDigestFinding(
                x.Error.RuleSource,
                x.Severity,
                x.Error.WhyItMatters,
                x.Error.CandidateWording,
                x.Error.Correction,
                x.Error.PrimaryCriterionCode,
                x.Error.StartOffset,
                x.Scored,
                x.Error.Category))
            .ToArray();

        var criteria = candidateVisible
            ? report.Criteria
                .OrderBy(x => CriterionOrder(x.CriterionCode))
                .Select(x => new WritingAssessmentV11CriterionResponse(
                    x.CriterionCode,
                    x.Score,
                    x.MaximumScore,
                    StrengthFor(x),
                    LimitationFor(x),
                    [],
                    WritingCandidateText.Clean(x.ImprovementAction, WritingCriterionCopy.Improvement(x.CriterionCode)),
                    WritingReportDigest.CriterionSummary(
                        digestFindings.Where(f => f.Criterion == x.CriterionCode))))
                .ToArray()
            : [];

        var errors = candidateVisible
            ? rows
                .Select(x => new WritingAssessmentV11ErrorResponse(
                    x.Error.Id.ToString(),
                    null,
                    // The candidate's own wording stays verbatim.
                    x.Error.CandidateWording,
                    WritingReportDigest.Tidy(x.Error.Correction, DefaultCorrection),
                    WritingCandidateText.PublicCriterionLabel(x.Error.PrimaryCriterionCode),
                    null,
                    WritingReportDigest.Tidy(x.Error.WhyItMatters, WritingCriterionCopy.WhyItMatters(x.Error.PrimaryCriterionCode)),
                    WritingCandidateText.PublicSeverity(x.Severity, x.Scored),
                    string.Empty,
                    x.Error.PrimaryCriterionCode,
                    ParseStringList(x.Error.SecondaryCriterionCodesJson),
                    x.Error.StartOffset,
                    x.Error.EndOffset,
                    null,
                    null))
                .ToArray()
            : [];

        var facts = candidateVisible
            ? report.Facts.Select(x => new WritingAssessmentV11FactResponse(
                x.FactText,
                PlainSourceReference(x.SourceReference),
                WritingCandidateText.PlainLabel(x.Classification),
                WritingCandidateText.PlainLabel(x.CandidateStatus),
                x.CandidateExcerpt,
                WritingCandidateText.CleanOrNull(x.Explanation))).ToArray()
            : [];

        WritingAssessmentV11ModelAnswerResponse? answer = null;
        if (candidateVisible && WritingTaskModelAnswerService.IsVerifiedForCandidates(verifiedModelAnswer))
        {
            answer = new WritingAssessmentV11ModelAnswerResponse(
                verifiedModelAnswer!.Status.ToString(),
                verifiedModelAnswer.ModelAnswerText,
                null,
                [],
                [],
                true);
        }

        // All findings advisory (or none eligible) means NO priorities: the stored list is only used when
        // the report has no finding rows at all, and then only after the same cleaning.
        IReadOnlyList<string> topPriorities = [];
        if (candidateVisible)
        {
            topPriorities = digestFindings.Length > 0
                ? WritingReportDigest.ComposePriorities(digestFindings)
                : WritingReportDigest.CleanStoredPriorities(ParseStringList(report.TopPrioritiesJson));
        }

        return new WritingAssessmentV11ReportResponse(
            report.Id.ToString(),
            report.SubmissionId.ToString(),
            report.Status.ToString(),
            report.Profession,
            WritingCandidateText.PublicLetterTypeLabel(report.LetterType),
            // Version and pack identifiers stay in admin records; the keys remain for one release.
            string.Empty,
            string.Empty,
            string.Empty,
            score,
            "AI Estimated Practice Score — not an official OET result",
            gradeBand,
            report.ScoreRange,
            report.ConfidenceLabel,
            null,
            report.CandidateNumericScoreEnabled && candidateVisible,
            candidateVisible,
            [],
            topPriorities,
            candidateVisible ? WritingCandidateText.CleanList(ParseStringList(report.StrengthsJson)) : [],
            candidateVisible ? WritingCandidateText.CleanList(ParseStringList(report.StudyPlanJson)) : [],
            criteria,
            errors,
            facts,
            answer);
    }

    // Older reports stored internal jargon as the strength/limitation text of a criterion card; those
    // exact strings are replaced with the plain wording. A criterion with full marks never shows a limitation.
    private static string StrengthFor(WritingAssessmentCriterionEvidence x)
    {
        var plain = WritingCriterionCopy.IsLegacyStrength(x.CriterionCode, x.StrengthObservation)
            ? string.Empty
            : WritingCandidateText.Clean(x.StrengthObservation);
        return plain.Length > 0 ? plain : WritingCriterionCopy.Strength(x.CriterionCode, x.Score, x.MaximumScore);
    }

    private static string LimitationFor(WritingAssessmentCriterionEvidence x)
    {
        if (x.Score >= x.MaximumScore) return WritingCriterionCopy.NoLimitation;
        var plain = WritingCriterionCopy.IsLegacyLimitation(x.CriterionCode, x.LimitationObservation)
            ? string.Empty
            : WritingCandidateText.Clean(x.LimitationObservation);
        return plain.Length > 0 ? plain : WritingCriterionCopy.Limitation(x.CriterionCode, x.Score, x.MaximumScore);
    }

    private static string PlainSourceReference(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return string.Empty;
        const string linePrefix = "case-note-line:";
        var value = stored.Trim();
        if (value.StartsWith(linePrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[linePrefix.Length..], out var line))
            return $"Case notes, line {line}";
        return value.StartsWith("case-note", StringComparison.OrdinalIgnoreCase) ? "Case notes" : string.Empty;
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
