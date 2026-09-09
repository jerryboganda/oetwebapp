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
    /// <summary>
    /// Candidate release default. 2026-09-09 owner decision (Dr Ahmed Hesham):
    /// Writing AI grading is a fully automated product feature — a completed
    /// human/tutor calibration study is NOT a runtime release requirement and
    /// must never block a candidate's own successful AI grading from being
    /// shown to them. Release is therefore opt-out, not opt-in: a configured
    /// gate row is only an explicit admin kill switch (Status Blocked or
    /// Retired) for a specific model+calibration-set pairing known to be bad.
    /// Formally APPROVING a gate (POST /release-gates/{id}/approve) still
    /// enforces the full rigorous calibration metrics via
    /// IsCandidateReleaseAllowed below, for teams that choose to run human
    /// calibration as optional offline QA — see the FINAL OWNER DECISION —
    /// WRITING AI SCORE RELEASE note this method implements.
    /// </summary>
    public async Task<WritingCalibrationReleaseDecision> ResolveAsync(
        string modelVersion,
        string calibrationSetVersion,
        CancellationToken ct)
    {
        var gate = await db.WritingAssessmentReleaseGates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ModelVersion == modelVersion
                && x.CalibrationSetVersion == calibrationSetVersion, ct);
        var blockedByAdmin = gate is not null
            && gate.Status is WritingAssessmentReleaseStatus.Blocked or WritingAssessmentReleaseStatus.Retired;
        return blockedByAdmin
            ? new(false, "release_blocked_by_admin", gate)
            : new(true, gate is null ? "released_by_default" : gate.Status.ToString().ToLowerInvariant(), gate);
    }

    /// <summary>
    /// Rigorous formal calibration sign-off used only by the admin
    /// POST /release-gates/{id}/approve endpoint (optional offline QA — see
    /// ResolveAsync above for the actual candidate-release default).
    /// </summary>
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
