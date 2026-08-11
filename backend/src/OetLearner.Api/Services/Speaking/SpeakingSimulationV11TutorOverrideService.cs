using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Stores human corrections to a completed v1.1 report as immutable revision
/// rows. The generated AI assessment remains untouched and the original JSON
/// is copied into every revision for an auditable before/after comparison.
/// </summary>
public sealed class SpeakingSimulationV11TutorOverrideService(
    LearnerDbContext db,
    TutorAssessmentService tutorAssessment,
    TimeProvider clock,
    ILogger<SpeakingSimulationV11TutorOverrideService> logger)
{
    private const int MaxReportBytes = 1024 * 1024;

    public async Task<SpeakingSimulationV11TutorOverrideResponse> CreateAsync(
        string tutorId,
        string speakingSessionId,
        SpeakingSimulationV11TutorOverrideRequest request,
        CancellationToken ct)
    {
        await tutorAssessment.GetSessionContextForTutorAsync(tutorId, speakingSessionId, ct);

        var assessmentId = request.AssessmentId?.Trim();
        if (string.IsNullOrWhiteSpace(assessmentId))
        {
            throw ApiException.Validation(
                "speaking_v11_tutor_override_assessment_required",
                "The completed v1.1 assessment id is required.");
        }

        var assessment = await db.SpeakingSimulationV11Assessments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == assessmentId
                && x.SpeakingSessionId == speakingSessionId, ct);
        if (assessment is null)
        {
            throw ApiException.NotFound(
                "speaking_v11_assessment_not_found",
                "That v1.1 assessment does not belong to this Speaking session.");
        }
        if (assessment.Status != SpeakingSimulationV11AssessmentStatus.Complete
            || string.IsNullOrWhiteSpace(assessment.ReportJson))
        {
            throw ApiException.Validation(
                "speaking_v11_tutor_override_report_unavailable",
                "A human override can only be created for a completed v1.1 report.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
        {
            throw ApiException.Validation(
                "speaking_v11_tutor_override_reason_invalid",
                "Explain the human correction in 1 to 2,000 characters.");
        }

        ValidateScoreRange(request.EstimatedPracticeScore, request.ScoreRangeLow, request.ScoreRangeHigh);
        var originalReportJson = ValidateReportJson(assessment.ReportJson, "original");
        var overrideReportJson = ValidateReportJson(request.OverrideReportJson, "override");

        var now = clock.GetUtcNow();
        var row = new SpeakingSimulationV11TutorOverride
        {
            Id = $"spv11_tutor_override_{Guid.NewGuid():N}",
            AssessmentId = assessment.Id,
            SpeakingSessionId = speakingSessionId,
            TutorId = tutorId,
            EstimatedPracticeScore = request.EstimatedPracticeScore,
            ScoreRangeLow = request.ScoreRangeLow,
            ScoreRangeHigh = request.ScoreRangeHigh,
            Reason = reason,
            OriginalReportJson = originalReportJson,
            OverrideReportJson = overrideReportJson,
            OriginalSpecVersion = assessment.SpecVersion,
            OriginalRubricVersion = assessment.RubricVersion,
            OriginalCalibrationVersion = assessment.CalibrationVersion,
            OriginalProvider = assessment.Provider,
            OriginalModelName = assessment.ModelName,
            CreatedAt = now,
        };

        db.SpeakingSimulationV11TutorOverrides.Add(row);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = tutorId,
            ActorName = tutorId,
            Action = "SpeakingSimulationV11TutorOverrideCreated",
            ResourceType = "SpeakingSimulationV11Assessment",
            ResourceId = assessment.Id,
            Details = JsonSerializer.Serialize(new
            {
                overrideId = row.Id,
                speakingSessionId,
                assessmentId = assessment.Id,
                estimatedPracticeScore = row.EstimatedPracticeScore,
                scoreRangeLow = row.ScoreRangeLow,
                scoreRangeHigh = row.ScoreRangeHigh,
                originalReportSha256 = Sha256(originalReportJson),
                overrideReportSha256 = Sha256(overrideReportJson),
                reason,
            }),
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Created immutable Speaking v1.1 tutor override {OverrideId} for assessment {AssessmentId} by tutor {TutorId}.",
            row.Id, assessment.Id, tutorId);

        return ProjectForTutor(row);
    }

    public async Task<IReadOnlyList<SpeakingSimulationV11TutorOverrideResponse>> GetForTutorAsync(
        string tutorId,
        string speakingSessionId,
        CancellationToken ct)
    {
        await tutorAssessment.GetSessionContextForTutorAsync(tutorId, speakingSessionId, ct);
        var rows = await db.SpeakingSimulationV11TutorOverrides
            .AsNoTracking()
            .Where(x => x.SpeakingSessionId == speakingSessionId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToArrayAsync(ct);
        return rows.Select(ProjectForTutor).ToArray();
    }

    public async Task<SpeakingSimulationV11LearnerTutorOverrideResponse?> GetForLearnerAsync(
        string learnerId,
        string speakingSessionId,
        CancellationToken ct)
    {
        var ownsSession = await db.SpeakingSessions
            .AsNoTracking()
            .AnyAsync(x => x.Id == speakingSessionId && x.UserId == learnerId, ct);
        if (!ownsSession)
        {
            throw ApiException.NotFound(
                "speaking_session_not_found",
                "Speaking session not found.");
        }

        var row = await db.SpeakingSimulationV11TutorOverrides
            .AsNoTracking()
            .Where(x => x.SpeakingSessionId == speakingSessionId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ProjectForLearner(row);
    }

    private static void ValidateScoreRange(int score, int low, int high)
    {
        if (score is < 0 or > 500 || low is < 0 or > 500 || high is < 0 or > 500 || low > high || score < low || score > high)
        {
            throw ApiException.Validation(
                "speaking_v11_tutor_override_score_invalid",
                "The human practice score and confidence range must be within 0 to 500, with the score inside the range.");
        }
    }

    private static string ValidateReportJson(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > MaxReportBytes)
        {
            throw ApiException.Validation(
                $"speaking_v11_tutor_override_{label}_json_invalid",
                "The human revision must contain a non-empty JSON object no larger than 1 MB.");
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Expected a JSON object.");
            }
        }
        catch (JsonException)
        {
            throw ApiException.Validation(
                $"speaking_v11_tutor_override_{label}_json_invalid",
                "The human revision must contain a valid JSON object no larger than 1 MB.");
        }

        return value;
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static SpeakingSimulationV11TutorOverrideResponse ProjectForTutor(
        SpeakingSimulationV11TutorOverride row)
        => new(
            OverrideId: row.Id,
            AssessmentId: row.AssessmentId,
            SpeakingSessionId: row.SpeakingSessionId,
            TutorId: row.TutorId,
            EstimatedPracticeScore: row.EstimatedPracticeScore,
            ScoreRangeLow: row.ScoreRangeLow,
            ScoreRangeHigh: row.ScoreRangeHigh,
            Reason: row.Reason,
            OriginalReportJson: row.OriginalReportJson,
            OverrideReportJson: row.OverrideReportJson,
            OriginalSpecVersion: row.OriginalSpecVersion,
            OriginalRubricVersion: row.OriginalRubricVersion,
            OriginalCalibrationVersion: row.OriginalCalibrationVersion,
            OriginalProvider: row.OriginalProvider,
            OriginalModelName: row.OriginalModelName,
            CreatedAt: row.CreatedAt);

    private static SpeakingSimulationV11LearnerTutorOverrideResponse ProjectForLearner(
        SpeakingSimulationV11TutorOverride row)
        => new(
            OverrideId: row.Id,
            AssessmentId: row.AssessmentId,
            EstimatedPracticeScore: row.EstimatedPracticeScore,
            ScoreRangeLow: row.ScoreRangeLow,
            ScoreRangeHigh: row.ScoreRangeHigh,
            Reason: row.Reason,
            OverrideReportJson: row.OverrideReportJson,
            CreatedAt: row.CreatedAt);
}
