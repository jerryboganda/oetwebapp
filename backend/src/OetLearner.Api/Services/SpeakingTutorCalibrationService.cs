using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

// Wave 4 of docs/SPEAKING-MODULE-PLAN.md.
//
// Tutor side of the calibration loop, plus the inline-comment surface.
// Lives in its own service so the existing `ExpertService` (already
// large) is untouched. All operations are scoped to the authenticated
// expert id.
//
// Authorisation:
//  - Calibration submit/list: any authenticated expert (gated at the
//    endpoint via the existing `ExpertOnly` policy).
//  - Inline transcript comments (write): only experts who currently own
//    the speaking ReviewRequest backing the attempt OR an admin
//    (admin-side write is exposed separately if needed; this service
//    only handles the expert path).
//  - Inline transcript comments (read): the attempt's owner (learner)
//    and any expert that has touched the attempt's review.
public class SpeakingTutorCalibrationService(LearnerDbContext db)
{
    public async Task<object> ListPublishedSamplesForTutorAsync(string tutorId, CancellationToken ct)
    {
        // Tutor sees: every published sample, with their own submission
        // (if any) so they can re-open / re-submit.
        var samples = await db.SpeakingCalibrationSamples
            .AsNoTracking()
            .Where(x => x.Status == SpeakingCalibrationSampleStatus.Published)
            .OrderByDescending(x => x.PublishedAt)
            .ToListAsync(ct);

        var ids = samples.Select(s => s.Id).ToArray();
        var mySubmissions = await db.SpeakingCalibrationScores
            .AsNoTracking()
            .Where(s => s.TutorId == tutorId && ids.Contains(s.SampleId))
            .ToDictionaryAsync(x => x.SampleId, ct);

        return new
        {
            samples = samples.Select(s =>
            {
                mySubmissions.TryGetValue(s.Id, out var mine);
                return new
                {
                    sampleId = s.Id,
                    title = s.Title,
                    description = s.Description,
                    sourceAttemptId = s.SourceAttemptId,
                    professionId = s.ProfessionId,
                    difficulty = s.Difficulty,
                    publishedAt = s.PublishedAt,
                    // Gold rubric is intentionally NOT exposed to tutors —
                    // it would defeat the purpose of calibration. The
                    // service exposes only the tutor's own most-recent
                    // drift score so they can self-track improvement.
                    submitted = mine is not null,
                    mySubmission = mine is null ? null : new
                    {
                        submittedAt = mine.SubmittedAt,
                        totalAbsoluteError = mine.TotalAbsoluteError,
                    },
                };
            }).ToArray(),
        };
    }

    public async Task<object> SubmitCalibrationScoresAsync(
        string tutorId,
        string sampleId,
        TutorSpeakingCalibrationSubmitRequest req,
        CancellationToken ct)
    {
        var sample = await db.SpeakingCalibrationSamples
            .FirstOrDefaultAsync(x => x.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found",
                "That calibration sample does not exist.");
        if (sample.Status != SpeakingCalibrationSampleStatus.Published)
        {
            throw ApiException.Conflict("speaking_calibration_not_published",
                "Calibration sample is not currently open for submissions.");
        }
        AdminService.ValidateRubricPayload(req.Scores);

        var gold = AdminService_ParseRubricInternal(sample.GoldScoresJson)
            ?? throw ApiException.Conflict("speaking_calibration_invalid_gold",
                "Gold rubric for this sample is corrupt — admin must republish.");

        var existing = await db.SpeakingCalibrationScores
            .FirstOrDefaultAsync(x => x.SampleId == sampleId && x.TutorId == tutorId, ct);
        var totalAbs = AdminService.ComputeTotalAbsoluteError(req.Scores, gold);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new SpeakingCalibrationScore
            {
                Id = $"scsr-{Guid.NewGuid():N}",
                SampleId = sampleId,
                TutorId = tutorId,
                ScoresJson = AdminService.SerialiseRubric(req.Scores),
                Notes = (req.Notes ?? string.Empty).Trim(),
                TotalAbsoluteError = totalAbs,
                SubmittedAt = now,
            };
            db.SpeakingCalibrationScores.Add(existing);
        }
        else
        {
            existing.ScoresJson = AdminService.SerialiseRubric(req.Scores);
            existing.Notes = (req.Notes ?? string.Empty).Trim();
            existing.TotalAbsoluteError = totalAbs;
            existing.SubmittedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return new
        {
            sampleId,
            tutorId,
            submittedAt = existing.SubmittedAt,
            totalAbsoluteError = existing.TotalAbsoluteError,
            // Per-criterion drift is returned so the tutor sees exactly
            // where they diverged — that's the whole point of calibration.
            perCriterionDelta = new
            {
                intelligibility = req.Scores.Intelligibility - gold.Intelligibility,
                fluency = req.Scores.Fluency - gold.Fluency,
                appropriateness = req.Scores.Appropriateness - gold.Appropriateness,
                grammarExpression = req.Scores.GrammarExpression - gold.GrammarExpression,
                relationshipBuilding = req.Scores.RelationshipBuilding - gold.RelationshipBuilding,
                patientPerspective = req.Scores.PatientPerspective - gold.PatientPerspective,
                structure = req.Scores.Structure - gold.Structure,
                informationGathering = req.Scores.InformationGathering - gold.InformationGathering,
                informationGiving = req.Scores.InformationGiving - gold.InformationGiving,
            },
        };
    }

    public async Task<object> PostInlineCommentAsync(
        string expertId,
        string attemptId,
        ExpertSpeakingFeedbackCommentRequest req,
        CancellationToken ct)
    {
        var attempt = await db.Attempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw ApiException.NotFound("speaking_attempt_not_found", "That speaking attempt does not exist.");
        if (!string.Equals(attempt.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("SPEAKING_ATTEMPT_WRONG_SUBTEST",
                "Inline comments are only valid on speaking attempts.");
        }
        if (req.TranscriptLineIndex < 0)
        {
            throw ApiException.Validation("SPEAKING_COMMENT_LINE_INVALID",
                "transcriptLineIndex must be ≥ 0.");
        }
        if (string.IsNullOrWhiteSpace(req.Body))
        {
            throw ApiException.Validation("SPEAKING_COMMENT_BODY_REQUIRED",
                "Comment body is required.");
        }

        // Expert must have at least one ExpertReviewAssignment for a
        // ReviewRequest backing this attempt with themselves as the
        // assigned reviewer. Mirrors the read-context guard in
        // `ExpertService.LoadReadContextAsync`.
        var hasAssignment = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Join(db.ReviewRequests.AsNoTracking(),
                a => a.ReviewRequestId,
                r => r.Id,
                (a, r) => new { a, r })
            .AnyAsync(x => x.r.AttemptId == attemptId
                        && x.a.AssignedReviewerId == expertId, ct);
        if (!hasAssignment)
        {
            throw ApiException.Forbidden("speaking_comment_not_authorised",
                "You must be assigned to this attempt to comment on it.");
        }

        var criterion = string.IsNullOrWhiteSpace(req.CriterionCode)
            ? "general"
            : req.CriterionCode!.Trim();
        if (!IsValidCriterion(criterion))
        {
            throw ApiException.Validation("SPEAKING_COMMENT_CRITERION_INVALID",
                $"criterionCode must be 'general' or one of: {string.Join(", ", AdminService.SpeakingCriterionCodes)}.");
        }

        var entity = new SpeakingFeedbackComment
        {
            Id = $"sfc-{Guid.NewGuid():N}",
            AttemptId = attemptId,
            ExpertId = expertId,
            TranscriptLineIndex = req.TranscriptLineIndex,
            CriterionCode = criterion,
            Body = req.Body.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SpeakingFeedbackComments.Add(entity);
        await db.SaveChangesAsync(ct);
        return ProjectComment(entity);
    }

    public async Task<object> ListCommentsForAttemptAsync(
        string requesterUserId,
        bool isExpert,
        bool isAdmin,
        string attemptId,
        CancellationToken ct)
    {
        var attempt = await db.Attempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw ApiException.NotFound("speaking_attempt_not_found", "That speaking attempt does not exist.");

        // Owner (learner) can always read their own comments. Experts and
        // admins can read comments on any attempt they can see.
        var isOwner = string.Equals(attempt.UserId, requesterUserId, StringComparison.OrdinalIgnoreCase);
        if (!isOwner && !isExpert && !isAdmin)
        {
            throw ApiException.Forbidden("speaking_comment_read_forbidden",
                "You cannot read comments on this attempt.");
        }

        var rows = await db.SpeakingFeedbackComments
            .AsNoTracking()
            .Where(c => c.AttemptId == attemptId)
            .OrderBy(c => c.TranscriptLineIndex)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return new
        {
            comments = rows.Select(ProjectComment).ToArray(),
        };
    }

    private static bool IsValidCriterion(string code)
        => string.Equals(code, "general", StringComparison.OrdinalIgnoreCase)
        || AdminService.SpeakingCriterionCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));

    private static object ProjectComment(SpeakingFeedbackComment c) => new
    {
        commentId = c.Id,
        attemptId = c.AttemptId,
        expertId = c.ExpertId,
        transcriptLineIndex = c.TranscriptLineIndex,
        criterionCode = c.CriterionCode,
        body = c.Body,
        createdAt = c.CreatedAt,
    };

    // Bridge to AdminService.ParseRubric (private). We only need read
    // access to the gold rubric; we don't want to make the helper public
    // because it is an internal serialisation detail.
    private static SpeakingCriterionScoresPayload? AdminService_ParseRubricInternal(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            int Get(string key)
            {
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    return v.GetInt32();
                }
                return 0;
            }
            return new SpeakingCriterionScoresPayload(
                Intelligibility: Get("intelligibility"),
                Fluency: Get("fluency"),
                Appropriateness: Get("appropriateness"),
                GrammarExpression: Get("grammarExpression"),
                RelationshipBuilding: Get("relationshipBuilding"),
                PatientPerspective: Get("patientPerspective"),
                Structure: Get("structure"),
                InformationGathering: Get("informationGathering"),
                InformationGiving: Get("informationGiving"));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
