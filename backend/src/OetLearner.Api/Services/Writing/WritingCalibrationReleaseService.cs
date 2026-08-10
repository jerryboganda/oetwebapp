using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingCalibrationReleaseDecision(
    bool CandidateNumericScoreEnabled,
    string ReasonCode,
    WritingAssessmentReleaseGate? Gate);

/// <summary>
/// Candidate numeric-score release gate. The gate is intentionally stricter than
/// a passing calibration run: it also proves the PDF's criterion-priority rule.
/// </summary>
public sealed class WritingCalibrationReleaseService(LearnerDbContext db)
{
    public async Task<WritingCalibrationReleaseDecision> ResolveAsync(
        string modelVersion,
        string calibrationSetVersion,
        CancellationToken ct)
    {
        var gate = await db.WritingAssessmentReleaseGates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ModelVersion == modelVersion
                && x.CalibrationSetVersion == calibrationSetVersion, ct);
        if (gate is null)
            return new(false, "calibration_release_not_configured", null);
        return IsCandidateReleaseAllowed(gate)
            ? new(true, "approved", gate)
            : new(false, "calibration_release_blocked", gate);
    }

    public static bool IsCandidateReleaseAllowed(WritingAssessmentReleaseGate? gate)
    {
        if (gate is null
            || gate.Status != WritingAssessmentReleaseStatus.Approved
            || !gate.CandidateNumericScoreEnabled
            || gate.OwnerApprovedTolerance is not { } tolerance
            || gate.QualifiedReviewerCount < 1
            || gate.HumanRatingsPerBenchmark < 2
            || gate.MeanAbsoluteError is not { } meanAbsoluteError
            || meanAbsoluteError > tolerance
            || gate.InventedClaimRate is not { } inventedClaimRate
            || inventedClaimRate > 0.001m
            || gate.ContentConcisenessCorrelation is not { } contentCorrelation
            || gate.LanguageCorrelation is not { } languageCorrelation
            || contentCorrelation <= languageCorrelation)
        {
            return false;
        }

        return true;
    }
}
