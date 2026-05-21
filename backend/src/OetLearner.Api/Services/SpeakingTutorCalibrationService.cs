using System.Text.Json;
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

        var gold = ParseRubricInternal(sample.GoldScoresJson)
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
                        && x.a.AssignedReviewerId == expertId
                        && x.a.ClaimState != ExpertAssignmentState.Released, ct);
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

        // Owner (learner) can always read their own comments. Assigned experts
        // can read their own review attempts; admins retain operational access.
        var isOwner = string.Equals(attempt.UserId, requesterUserId, StringComparison.OrdinalIgnoreCase);
        var isAssignedExpert = isExpert
            && await HasExpertAssignmentAsync(requesterUserId, attemptId, ct);
        if (!isOwner && !isAssignedExpert && !isAdmin)
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

    // ────────────────────────────────────────────────────────────────────
    // Phase 7 — drift report.
    //
    // For each tutor with ≥ minSamples calibration submissions, compute:
    //   * `overallMAE`   = mean of absolute per-criterion errors across
    //                       every submitted sample
    //   * `criterionMAEJson` = per-criterion mean absolute error (drives
    //                          the radar chart in the admin drift tab)
    //   * `samples`      = count of submissions used to compute the MAE
    //   * `lastSubmittedAt` = freshness signal
    //
    // Result is ordered by overall MAE descending — i.e. worst-drift first
    // so admins can prioritise re-training.
    // ────────────────────────────────────────────────────────────────────
    public async Task<TutorCalibrationDriftReport> GetDriftReportAsync(
        int minSamples,
        CancellationToken ct)
    {
        var min = Math.Max(1, minSamples);

        var scores = await db.SpeakingCalibrationScores
            .AsNoTracking()
            .Join(db.SpeakingCalibrationSamples.AsNoTracking(),
                s => s.SampleId,
                sa => sa.Id,
                (s, sa) => new { score = s, sample = sa })
            .Where(x => x.sample.Status == SpeakingCalibrationSampleStatus.Published)
            .ToListAsync(ct);

        if (scores.Count == 0)
        {
            return new TutorCalibrationDriftReport(
                Tutors: Array.Empty<TutorCalibrationDriftRow>(),
                SampleSize: 0,
                SamplesPublished: 0,
                MinSamples: min);
        }

        var tutorIds = scores.Select(x => x.score.TutorId).Distinct().ToArray();
        var tutorNames = await db.ExpertUsers.AsNoTracking()
            .Where(u => tutorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var grouped = scores.GroupBy(x => x.score.TutorId)
            .Where(g => g.Count() >= min)
            .ToList();

        var rows = new List<TutorCalibrationDriftRow>(grouped.Count);
        foreach (var group in grouped)
        {
            // Per-criterion absolute errors → average across the group.
            var perCriterionMae = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var code in AdminService.SpeakingCriterionCodes)
            {
                perCriterionMae[code] = 0.0;
            }

            var samples = 0;
            var totalAbs = 0.0;
            DateTimeOffset lastSubmittedAt = DateTimeOffset.MinValue;

            foreach (var entry in group)
            {
                var tutorScores = ParseRubricInternal(entry.score.ScoresJson);
                var goldScores = ParseRubricInternal(entry.sample.GoldScoresJson);
                if (tutorScores is null || goldScores is null) continue;
                samples++;
                if (entry.score.SubmittedAt > lastSubmittedAt) lastSubmittedAt = entry.score.SubmittedAt;

                perCriterionMae["intelligibility"] += Math.Abs(tutorScores.Intelligibility - goldScores.Intelligibility);
                perCriterionMae["fluency"] += Math.Abs(tutorScores.Fluency - goldScores.Fluency);
                perCriterionMae["appropriateness"] += Math.Abs(tutorScores.Appropriateness - goldScores.Appropriateness);
                perCriterionMae["grammarExpression"] += Math.Abs(tutorScores.GrammarExpression - goldScores.GrammarExpression);
                perCriterionMae["relationshipBuilding"] += Math.Abs(tutorScores.RelationshipBuilding - goldScores.RelationshipBuilding);
                perCriterionMae["patientPerspective"] += Math.Abs(tutorScores.PatientPerspective - goldScores.PatientPerspective);
                perCriterionMae["structure"] += Math.Abs(tutorScores.Structure - goldScores.Structure);
                perCriterionMae["informationGathering"] += Math.Abs(tutorScores.InformationGathering - goldScores.InformationGathering);
                perCriterionMae["informationGiving"] += Math.Abs(tutorScores.InformationGiving - goldScores.InformationGiving);

                totalAbs += entry.score.TotalAbsoluteError;
            }

            if (samples == 0) continue;

            // Average each criterion across the sampled set.
            foreach (var code in perCriterionMae.Keys.ToArray())
            {
                perCriterionMae[code] = Math.Round(perCriterionMae[code] / samples, 3);
            }

            var overallMae = Math.Round(totalAbs / (samples * 9), 3);

            rows.Add(new TutorCalibrationDriftRow(
                TutorId: group.Key,
                DisplayName: tutorNames.TryGetValue(group.Key, out var name) ? name : group.Key,
                Samples: samples,
                OverallMAE: overallMae,
                CriterionMAEJson: JsonSerializer.Serialize(perCriterionMae),
                CriterionMAE: perCriterionMae,
                LastSubmittedAt: lastSubmittedAt == DateTimeOffset.MinValue ? null : lastSubmittedAt));
        }

        return new TutorCalibrationDriftReport(
            Tutors: rows.OrderByDescending(r => r.OverallMAE).ToArray(),
            SampleSize: scores.Count,
            SamplesPublished: scores.Select(x => x.sample.Id).Distinct().Count(),
            MinSamples: min);
    }

    private static SpeakingCriterionScoresPayload? ParseRubricInternal(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            int Get(string key)
            {
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
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
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsValidCriterion(string code)
        => string.Equals(code, "general", StringComparison.OrdinalIgnoreCase)
        || AdminService.SpeakingCriterionCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));

    private async Task<bool> HasExpertAssignmentAsync(string expertId, string attemptId, CancellationToken ct)
        => await db.ExpertReviewAssignments
            .AsNoTracking()
            .Join(db.ReviewRequests.AsNoTracking(),
                assignment => assignment.ReviewRequestId,
                review => review.Id,
                (assignment, review) => new { assignment, review })
            .AnyAsync(row => row.review.AttemptId == attemptId
                && row.assignment.AssignedReviewerId == expertId
                && row.assignment.ClaimState != ExpertAssignmentState.Released,
                ct);

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

}
