using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Mocks;

/// <summary>
/// Mocks Module Phase 3.2 — qualitative pass-prediction signal.
///
/// Reads the completed <see cref="MockAttempt"/> + associated
/// <see cref="MockItemAnalysisSnapshot"/> rows for the bundle the learner sat
/// and returns a qualitative confidence band — never a numeric probability.
/// The plan is explicit on this:
///
///   "Pass-prediction is statistically delicate — cap to qualitative bands.
///    Never surface a numeric probability."
///
/// Confidence bands map to <see cref="OetScoring.AdvisoryTier"/> tiers plus a
/// statistical-evidence quality score derived from item-analysis
/// discrimination indices (when present).
/// </summary>
public sealed class MockPassPredictionService(LearnerDbContext db)
{
    /// <summary>"high" confidence — at least the canonical Grade-B anchor with strong discrimination evidence.</summary>
    public const string ConfidenceHigh = "high";

    /// <summary>"medium" confidence — borderline overall or mixed evidence.</summary>
    public const string ConfidenceMedium = "medium";

    /// <summary>"low" confidence — below Grade C+ or insufficient evidence to project.</summary>
    public const string ConfidenceLow = "low";

    public async Task<MockPassPrediction> ComputeAsync(string mockReportId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mockReportId))
        {
            throw ApiException.Validation("MOCK_REPORT_REQUIRED", "mockReportId is required.");
        }

        var report = await db.MockReports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == mockReportId, ct)
            ?? throw ApiException.NotFound("MOCK_REPORT_NOT_FOUND",
                $"Mock report '{mockReportId}' not found.");

        var attempt = await db.MockAttempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == report.MockAttemptId, ct);

        if (attempt is null)
        {
            return new MockPassPrediction(
                ConfidenceBand: ConfidenceLow,
                Verdict: "insufficient_evidence",
                Rationale: "No attempt is associated with this report yet.");
        }

        if (attempt.State != AttemptState.Completed
            && report.State != AsyncState.Completed)
        {
            return new MockPassPrediction(
                ConfidenceBand: ConfidenceLow,
                Verdict: "in_progress",
                Rationale: "Complete the mock to receive a pass-prediction signal.");
        }

        var overall = TryReadOverallScore(report.PayloadJson);
        if (overall is null)
        {
            return new MockPassPrediction(
                ConfidenceBand: ConfidenceLow,
                Verdict: "insufficient_evidence",
                Rationale: "No overall score is available on this report yet.");
        }

        // Pull item-analysis snapshots for the bundle the attempt was sat against
        // so the prediction can incorporate distractor-quality and discrimination.
        var snapshots = string.IsNullOrWhiteSpace(attempt.MockBundleId)
            ? new List<MockItemAnalysisSnapshot>()
            : await db.MockItemAnalysisSnapshots.AsNoTracking()
                .Where(s => s.MockBundleId == attempt.MockBundleId)
                .ToListAsync(ct);

        var evidence = SummariseEvidence(snapshots);
        var tier = OetScoring.AdvisoryTier(overall.Value);

        return ProjectBand(tier, evidence, overall.Value);
    }

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    private static MockPassPrediction ProjectBand(
        OetScoring.AdvisoryTierResult tier,
        ItemAnalysisEvidence evidence,
        int overall)
    {
        // "strong" overall + decent evidence → high confidence.
        if (string.Equals(tier.Tier, "strong", StringComparison.Ordinal) && evidence.Quality >= 0.5)
        {
            return new MockPassPrediction(
                ConfidenceBand: ConfidenceHigh,
                Verdict: "likely_pass",
                Rationale: BuildRationale(tier, evidence, overall));
        }

        // "passing" overall (>= 350 Grade B anchor) → medium-high, contingent on evidence.
        if (string.Equals(tier.Tier, "passing", StringComparison.Ordinal))
        {
            return evidence.Quality >= 0.5
                ? new MockPassPrediction(
                    ConfidenceBand: ConfidenceHigh,
                    Verdict: "likely_pass",
                    Rationale: BuildRationale(tier, evidence, overall))
                : new MockPassPrediction(
                    ConfidenceBand: ConfidenceMedium,
                    Verdict: "borderline_pass",
                    Rationale: BuildRationale(tier, evidence, overall));
        }

        // "developing" overall (300–349) → medium-low: within striking distance.
        if (string.Equals(tier.Tier, "developing", StringComparison.Ordinal))
        {
            return new MockPassPrediction(
                ConfidenceBand: ConfidenceMedium,
                Verdict: "borderline_fail",
                Rationale: BuildRationale(tier, evidence, overall));
        }

        // "foundation" or anything below the C+ line → low confidence in a pass.
        return new MockPassPrediction(
            ConfidenceBand: ConfidenceLow,
            Verdict: "unlikely_pass",
            Rationale: BuildRationale(tier, evidence, overall));
    }

    private static string BuildRationale(OetScoring.AdvisoryTierResult tier, ItemAnalysisEvidence evidence, int overall)
    {
        var bandLabel = tier.Tier switch
        {
            "strong" => "comfortably above the OET pass line",
            "passing" => "at or above the OET pass line",
            "developing" => "within striking distance of the pass line",
            _ => "below the OET pass line",
        };

        var evidenceLabel = evidence.SnapshotCount switch
        {
            0 => "limited item-analysis data available",
            < 10 => "preliminary item-analysis evidence",
            _ when evidence.Quality >= 0.5 => "strong item-analysis evidence",
            _ => "mixed item-analysis evidence",
        };

        return $"Overall {overall}/500 is {bandLabel}; {evidenceLabel}. {tier.Message}";
    }

    private static ItemAnalysisEvidence SummariseEvidence(IList<MockItemAnalysisSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return new ItemAnalysisEvidence(0, 0, 0);
        }

        var withDiscrimination = snapshots
            .Where(s => s.DiscriminationIndex.HasValue && s.TotalAttempts >= 5)
            .ToList();

        if (withDiscrimination.Count == 0)
        {
            // Snapshots exist but not enough attempts to compute discrimination —
            // signal that the bundle has been used but evidence is weak.
            return new ItemAnalysisEvidence(snapshots.Count, 0, 0);
        }

        // Average positive discrimination indicates items reliably separate
        // strong from weak learners. Treat 0.2 as a reasonable lower bound
        // for "well-discriminating" (classical-test-theory rule of thumb).
        var avgDiscrimination = withDiscrimination.Average(s => s.DiscriminationIndex!.Value);
        var goodFraction = withDiscrimination.Count > 0
            ? (double)withDiscrimination.Count(s => s.DiscriminationIndex!.Value >= 0.2)
              / withDiscrimination.Count
            : 0.0;

        // Quality blends "how many items have discrimination data" with
        // "how well-discriminating they are on average". Clamped to [0, 1].
        var quality = Math.Clamp((avgDiscrimination + goodFraction) / 2.0, 0.0, 1.0);

        return new ItemAnalysisEvidence(snapshots.Count, withDiscrimination.Count, quality);
    }

    private static int? TryReadOverallScore(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("overallScore", out var prop))
            {
                return null;
            }

            // overallScore is canonically a string ("380") but be lenient for
            // legacy pre-V1 rows that may have written it numerically.
            return prop.ValueKind switch
            {
                JsonValueKind.Number when prop.TryGetInt32(out var intVal) => intVal,
                JsonValueKind.String when int.TryParse(prop.GetString(), out var parsed) => parsed,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ItemAnalysisEvidence(
        int SnapshotCount,
        int DiscriminationCount,
        double Quality);
}

/// <summary>
/// Qualitative pass-prediction signal — intentionally lacks any numeric
/// probability. <see cref="ConfidenceBand"/> is one of <c>"high"</c>,
/// <c>"medium"</c>, <c>"low"</c> matching the canonical
/// <c>MockReportPassPredictionV1</c> wire contract.
/// </summary>
public sealed record MockPassPrediction(
    string ConfidenceBand,
    string Verdict,
    string Rationale);
