using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Error-DNA feeder (SAMI Wave 4, F-044): turns PUBLISHED writing findings into the
/// learner's evidenced recurring-error record. Best-effort by design — a feeder failure
/// must never fail the grading pipeline; it logs and moves on. Only CandidateReady
/// reports (validated, learner-visible) are fed, so blocked/calibration runs teach nothing.
/// </summary>
public interface IWritingErrorDnaFeeder
{
    Task<int> FeedFromReportAsync(Guid reportId, string userId, CancellationToken ct);
}

public sealed class WritingErrorDnaFeeder(
    LearnerDbContext db,
    IErrorDnaService errorDna,
    ILogger<WritingErrorDnaFeeder> logger) : IWritingErrorDnaFeeder
{
    public async Task<int> FeedFromReportAsync(Guid reportId, string userId, CancellationToken ct)
    {
        var report = await db.WritingAssessmentReportsV11.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null || report.Status != WritingAssessmentV11Status.CandidateReady) return 0;
        if (string.IsNullOrWhiteSpace(userId)) return 0;

        var errors = await db.WritingAssessmentErrors.AsNoTracking()
            .Where(e => e.ReportId == reportId)
            .ToListAsync(ct);

        var fed = 0;
        foreach (var error in errors)
        {
            // High-confidence findings only: feeding uncertain detections would
            // fabricate weaknesses the learner does not actually have (the Error
            // DNA contract is evidence, never guesswork).
            if (error.Confidence is "low") continue;
            if (string.IsNullOrWhiteSpace(error.Category)) continue;
            var pattern = BuildPattern(error);
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            try
            {
                await errorDna.RecordEvidenceAsync(
                    userId!,
                    MapCategory(error.Category, error.PrimaryCriterionCode),
                    pattern,
                    "writing",
                    "writing_grade",
                    reportId.ToString(),
                    ct);
                fed += 1;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Error-DNA feeder: failed to record one writing finding for report {ReportId}", reportId);
            }
        }
        return fed;
    }

    /// <summary>Stable, learner-readable pattern: the rule source when present,
    /// else the correction category with the criterion code.</summary>
    private static string BuildPattern(WritingAssessmentError error)
    {
        if (!string.IsNullOrWhiteSpace(error.RuleSource))
            return error.RuleSource.Trim();
        if (!string.IsNullOrWhiteSpace(error.Correction) && !string.IsNullOrWhiteSpace(error.CandidateWording))
            return $"{error.Category}: \"{Trim(error.CandidateWording)}\" → \"{Trim(error.Correction)}\"";
        return string.IsNullOrWhiteSpace(error.PrimaryCriterionCode)
            ? error.Category
            : $"{error.Category} ({error.PrimaryCriterionCode})";
    }

    private static string Trim(string value)
    {
        value = value.Trim();
        return value.Length <= 80 ? value : value[..80];
    }

    private static string MapCategory(string category, string criterionCode)
    {
        var c = category.Trim().ToLowerInvariant();
        if (c.Contains("grammar") || c.Contains("tense") || c.Contains("agreement")) return "grammar";
        if (c.Contains("vocab") || c.Contains("collocation") || c.Contains("word choice")) return "vocabulary";
        if (c.Contains("organis") || c.Contains("organiz") || c.Contains("structure") || c.Contains("selection")) return "writing_content";
        if (c.Contains("register") || c.Contains("tone")) return "writing_language";
        if (c.Contains("punctuat") || c.Contains("spelling")) return "grammar";
        return criterionCode.Contains("LANG", StringComparison.OrdinalIgnoreCase) ? "writing_language" : "writing_language";
    }
}
