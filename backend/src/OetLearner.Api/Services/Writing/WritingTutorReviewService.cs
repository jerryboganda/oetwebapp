using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing.Configuration;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingTutorQueueEntry(
    Guid SubmissionId,
    string LearnerId,
    DateTimeOffset SubmittedAt,
    string LetterType,
    string Profession,
    int WordCount,
    DateTimeOffset? ClaimedAt,
    string? ClaimedByTutorId,
    string Status,
    int? WaitMinutes);

public sealed record WritingTutorReviewView(
    Guid Id,
    Guid SubmissionId,
    string TutorId,
    string Status,
    string? FreeTextFeedback,
    string? PerCriterionCommentsJson,
    string? ScoreOverrideJson,
    DateTimeOffset? SubmittedAt);

public sealed record WritingTutorReviewInternalSubmitRequest(
    string FreeTextFeedback,
    IReadOnlyDictionary<string, string>? PerCriterionComments,
    IReadOnlyDictionary<string, int>? ScoreOverride);

/// <summary>
/// Tutor marking-surface submit payload (WS-B4). Mirrors the
/// <c>POST /v1/writing/tutor/reviews/{id}</c> request body. Distinct from
/// <see cref="WritingTutorReviewInternalSubmitRequest"/> (queue submit): it also
/// carries the content-checklist verdict, marker sequence and accepted-AI flag,
/// and the score override is a strongly-typed <see cref="WritingCriteriaScores"/>.
/// </summary>
public sealed record WritingTutorReviewSubmitInput(
    string? FreeTextFeedback,
    IReadOnlyDictionary<string, string>? PerCriterionComments,
    WritingCriteriaScores? ScoreOverride,
    IReadOnlyDictionary<string, string>? ContentChecklistVerdict,
    string? MarkerSequence,
    bool AcceptedAiPreAssessment);

/// <summary>Result of a marking-surface submit: the persisted review plus the
/// moderation row when double-marking advanced one (else null).</summary>
public sealed record WritingMarkingReviewResult(
    WritingTutorReview Review,
    WritingModeration? Moderation);

public sealed record WritingTutorQueueStatus(bool Paused, string? Reason, int CurrentDepth, int OldestWaitHours);

public interface IWritingTutorReviewService
{
    Task<IReadOnlyList<WritingTutorQueueEntry>> GetQueueAsync(string tutorId, CancellationToken ct);
    Task<WritingTutorQueueStatus> GetQueueStatusAsync(CancellationToken ct);
    Task<WritingTutorReviewView> ClaimAsync(string tutorId, Guid submissionId, CancellationToken ct);
    Task<WritingTutorReviewView> SubmitReviewAsync(string tutorId, Guid submissionId, WritingTutorReviewInternalSubmitRequest request, CancellationToken ct);

    /// <summary>
    /// Tutor marking-surface submit (WS-B4): upserts the review keyed by
    /// submission + tutor, applies any score override to the grade, and — for
    /// double-marking scenarios with an override — records the marker score on the
    /// moderation row. Returns the persisted review and the moderation row (if any).
    /// </summary>
    Task<WritingMarkingReviewResult> SubmitMarkingReviewAsync(Guid submissionId, string tutorId, WritingTutorReviewSubmitInput input, CancellationToken ct);
    Task<WritingTutorReviewView?> GetReviewForSubmissionAsync(string userId, Guid submissionId, CancellationToken ct);
    Task<WritingTutorReviewView> RequestReviewAsync(string userId, Guid submissionId, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingTutorReviewResponse?> RequestTutorReviewAsync(string userId, Guid submissionId, string? priority, CancellationToken ct);
    Task<WritingTutorReviewResponse?> GetTutorReviewAsync(string userId, Guid submissionId, CancellationToken ct);
    Task<WritingTutorQueueResponse> GetTutorQueueAsync(string tutorId, string? status, CancellationToken ct);
    Task<WritingTutorReviewResponse?> ClaimSubmissionForReviewAsync(string tutorId, Guid submissionId, CancellationToken ct);
    Task<WritingTutorReviewResponse> SubmitTutorReviewAsync(string tutorId, WritingTutorReviewSubmitRequest request, CancellationToken ct);
    Task<WritingTutorCalibrationResponse> GetTutorCalibrationAsync(string tutorId, CancellationToken ct);
}

public sealed class WritingTutorReviewService(
    LearnerDbContext db,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    IWritingModerationService moderation,
    ILogger<WritingTutorReviewService> logger) : IWritingTutorReviewService
{
    public async Task<IReadOnlyList<WritingTutorQueueEntry>> GetQueueAsync(string tutorId, CancellationToken ct)
    {
        _ = tutorId;
        var pending = await db.WritingTutorReviewAssignments.AsNoTracking()
            .Where(a => a.Status == "pending" || a.Status == "claimed")
            .OrderBy(a => a.ClaimedAt)
            .Take(50)
            .ToListAsync(ct);
        if (pending.Count == 0) return Array.Empty<WritingTutorQueueEntry>();
        var submissionIds = pending.Select(a => a.SubmissionId).ToList();
        var submissions = await db.WritingSubmissions.AsNoTracking()
            .Where(s => submissionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s, ct);
        var scenarios = await db.WritingScenarios.AsNoTracking()
            .Where(s => submissions.Select(x => x.Value.ScenarioId).Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new { s.LetterType, s.Profession }, ct);
        var now = clock.GetUtcNow();
        var result = new List<WritingTutorQueueEntry>(pending.Count);
        foreach (var assignment in pending)
        {
            if (!submissions.TryGetValue(assignment.SubmissionId, out var sub)) continue;
            scenarios.TryGetValue(sub.ScenarioId, out var meta);
            var wait = (int)(now - sub.SubmittedAt).TotalMinutes;
            result.Add(new WritingTutorQueueEntry(
                sub.Id,
                sub.UserId,
                sub.SubmittedAt,
                meta?.LetterType ?? "unknown",
                meta?.Profession ?? "unknown",
                sub.WordCount,
                assignment.Status == "pending" ? null : assignment.ClaimedAt,
                string.IsNullOrWhiteSpace(assignment.TutorId) ? null : assignment.TutorId,
                assignment.Status,
                wait));
        }
        return result;
    }

    public async Task<WritingTutorQueueStatus> GetQueueStatusAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var pendingCount = await db.WritingTutorReviewAssignments.AsNoTracking().CountAsync(a => a.Status == "pending" || a.Status == "claimed", ct);
        var oldest = await db.WritingTutorReviewAssignments.AsNoTracking()
            .Where(a => a.Status == "pending" || a.Status == "claimed")
            .OrderBy(a => a.ClaimedAt)
            .Select(a => (DateTimeOffset?)a.ClaimedAt)
            .FirstOrDefaultAsync(ct);
        var waitHours = oldest is null ? 0 : (int)Math.Max(0, (clock.GetUtcNow() - oldest.Value).TotalHours);
        if (pendingCount > opts.TutorReviewQueueMaxDepth)
        {
            return new WritingTutorQueueStatus(true, $"queue_depth_{pendingCount}_above_{opts.TutorReviewQueueMaxDepth}", pendingCount, waitHours);
        }
        if (waitHours > opts.TutorReviewMaxWaitHours)
        {
            return new WritingTutorQueueStatus(true, $"oldest_wait_{waitHours}h_above_{opts.TutorReviewMaxWaitHours}h", pendingCount, waitHours);
        }
        return new WritingTutorQueueStatus(false, null, pendingCount, waitHours);
    }

    public async Task<WritingTutorReviewView> ClaimAsync(string tutorId, Guid submissionId, CancellationToken ct)
    {
        var assignment = await db.WritingTutorReviewAssignments
            .FirstOrDefaultAsync(a => a.SubmissionId == submissionId, ct)
            ?? throw ApiException.NotFound("writing_tutor_assignment_not_found", "Tutor assignment was not found.");
        if (assignment.Status is "submitted" or "released")
        {
            throw ApiException.Conflict("writing_tutor_assignment_closed", "Tutor assignment has already been submitted.");
        }
        if (assignment.Status == "claimed" && !string.Equals(assignment.TutorId, tutorId, StringComparison.Ordinal))
        {
            throw ApiException.Conflict("writing_tutor_assignment_taken", "Another tutor has already claimed this submission.");
        }
        if (assignment.Status != "pending" && assignment.Status != "claimed")
        {
            throw ApiException.Conflict("writing_tutor_assignment_locked", "Tutor assignment cannot be claimed in its current state.");
        }
        var now = clock.GetUtcNow();
        if (assignment.Status == "pending" && db.Database.IsRelational())
        {
            var updated = await db.WritingTutorReviewAssignments
                .Where(a => a.Id == assignment.Id && a.Status == "pending")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(a => a.TutorId, tutorId)
                    .SetProperty(a => a.ClaimedAt, now)
                    .SetProperty(a => a.DueAt, now.AddHours(24))
                    .SetProperty(a => a.Status, "claimed"), ct);
            if (updated == 0)
            {
                throw ApiException.Conflict("writing_tutor_assignment_taken", "Another tutor has already claimed this submission.");
            }
        }
        assignment.TutorId = tutorId;
        assignment.ClaimedAt = now;
        assignment.DueAt = now.AddHours(24);
        assignment.Status = "claimed";
        var review = await db.WritingTutorReviews.FirstOrDefaultAsync(r => r.SubmissionId == submissionId && r.TutorId == tutorId, ct);
        if (review is null)
        {
            review = new WritingTutorReview
            {
                Id = Guid.NewGuid(),
                SubmissionId = submissionId,
                TutorId = tutorId,
                Status = "claimed",
                CreatedAt = now,
            };
            db.WritingTutorReviews.Add(review);
        }
        await db.SaveChangesAsync(ct);
        return ToView(review);
    }

    public async Task<WritingTutorReviewView> SubmitReviewAsync(string tutorId, Guid submissionId, WritingTutorReviewInternalSubmitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var review = await db.WritingTutorReviews
            .FirstOrDefaultAsync(r => r.SubmissionId == submissionId && r.TutorId == tutorId, ct)
            ?? throw ApiException.NotFound("writing_tutor_review_not_found", "Tutor review was not found. Claim the submission first.");
        var assignment = await db.WritingTutorReviewAssignments.FirstOrDefaultAsync(a => a.SubmissionId == submissionId, ct)
            ?? throw ApiException.NotFound("writing_tutor_assignment_not_found", "Tutor assignment was not found.");
        if (!string.Equals(assignment.TutorId, tutorId, StringComparison.Ordinal) || assignment.Status != "claimed")
        {
            throw ApiException.Forbidden("writing_tutor_assignment_forbidden", "This submission is not claimed by the current tutor.");
        }
        var scoreOverride = NormalizeScoreOverride(request.ScoreOverride);
        var now = clock.GetUtcNow();
        review.FreeTextFeedback = request.FreeTextFeedback ?? string.Empty;
        review.PerCriterionCommentsJson = request.PerCriterionComments is null
            ? "{}"
            : JsonSerializer.Serialize(request.PerCriterionComments);
        review.ScoreOverrideJson = scoreOverride is null ? null : JsonSerializer.Serialize(scoreOverride);
        review.Status = "submitted";
        review.SubmittedAt = now;

        await ApplyTutorGradeAsync(review, scoreOverride, now, ct);
        assignment.Status = "submitted";
        assignment.ReleasedAt = now;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Tutor review submitted for submission {SubmissionId} by tutor {TutorId}.", submissionId, tutorId);
        return ToView(review);
    }

    public async Task<WritingMarkingReviewResult> SubmitMarkingReviewAsync(Guid submissionId, string tutorId, WritingTutorReviewSubmitInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var submission = await db.WritingSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw ApiException.NotFound("writing_submission_not_found", "Writing submission was not found.");
        var scenario = await db.WritingScenarios.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == submission.ScenarioId, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Writing scenario was not found.");

        var now = clock.GetUtcNow();
        var review = await db.WritingTutorReviews
            .FirstOrDefaultAsync(r => r.SubmissionId == submissionId && r.TutorId == tutorId, ct);
        if (review is null)
        {
            review = new WritingTutorReview
            {
                Id = Guid.NewGuid(),
                SubmissionId = submissionId,
                TutorId = tutorId,
                CreatedAt = now,
            };
            db.WritingTutorReviews.Add(review);
        }

        var scoreOverride = NormalizeScoreOverride(ScoreOverrideMap(input.ScoreOverride));

        review.FreeTextFeedback = input.FreeTextFeedback ?? string.Empty;
        review.PerCriterionCommentsJson = input.PerCriterionComments is null
            ? "{}"
            : JsonSerializer.Serialize(input.PerCriterionComments);
        review.ScoreOverrideJson = scoreOverride is null ? null : JsonSerializer.Serialize(scoreOverride);
        review.ContentChecklistVerdictJson = input.ContentChecklistVerdict is null
            ? "{}"
            : JsonSerializer.Serialize(input.ContentChecklistVerdict);
        review.IsContentChecklistMarked = input.ContentChecklistVerdict is { Count: > 0 };
        review.MarkerSequence = string.IsNullOrWhiteSpace(input.MarkerSequence)
            ? review.MarkerSequence
            : input.MarkerSequence;
        // The accepted-AI flag clears any stale audit JSON when the tutor opts out;
        // when accepted, any pre-assessment JSON already captured on the row is kept.
        if (!input.AcceptedAiPreAssessment)
        {
            review.AcceptedAiPreAssessmentJson = null;
        }
        review.Status = "submitted";
        review.SubmittedAt = now;

        await ApplyTutorGradeAsync(review, scoreOverride, now, ct);

        WritingModeration? moderationRow = null;
        if (string.Equals(scenario.MarkingMode, "double", StringComparison.OrdinalIgnoreCase)
            && input.ScoreOverride is not null)
        {
            moderationRow = await moderation.RecordMarkerScoreAsync(
                submissionId,
                tutorId,
                review.MarkerSequence,
                input.ScoreOverride,
                WritingModerationService.DefaultVarianceThreshold,
                ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Tutor marking review submitted for submission {SubmissionId} by tutor {TutorId} (sequence {MarkerSequence}).",
            submissionId, tutorId, review.MarkerSequence);
        return new WritingMarkingReviewResult(review, moderationRow);
    }

    /// <summary>Strongly-typed override → canonical c1Purpose..c6Language map (null when no override).</summary>
    private static IReadOnlyDictionary<string, int>? ScoreOverrideMap(WritingCriteriaScores? score)
        => score is null
            ? null
            : new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["c1Purpose"] = score.C1Purpose,
                ["c2Content"] = score.C2Content,
                ["c3Conciseness"] = score.C3Conciseness,
                ["c4Genre"] = score.C4Genre,
                ["c5Organisation"] = score.C5Organisation,
                ["c6Language"] = score.C6Language,
            };

    private async Task ApplyTutorGradeAsync(WritingTutorReview review, IReadOnlyDictionary<string, int>? scoreOverride, DateTimeOffset now, CancellationToken ct)
    {
        var current = await db.WritingGrades
            .Where(g => g.SubmissionId == review.SubmissionId)
            .OrderByDescending(g => g.AppealedByGradeId != null || g.TutorReviewId != null)
            .ThenByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct);
        if (current is null) return;

        if (scoreOverride is null || scoreOverride.Count == 0)
        {
            current.TutorReviewId = review.Id;
            current.ConfidenceFlag = "tutor_reviewed";
            return;
        }

        var scores = new[]
        {
            scoreOverride.GetValueOrDefault("c1Purpose", current.C1Purpose),
            scoreOverride.GetValueOrDefault("c2Content", current.C2Content),
            scoreOverride.GetValueOrDefault("c3Conciseness", current.C3Conciseness),
            scoreOverride.GetValueOrDefault("c4Genre", current.C4Genre),
            scoreOverride.GetValueOrDefault("c5Organisation", current.C5Organisation),
            scoreOverride.GetValueOrDefault("c6Language", current.C6Language),
        };
        var rawTotal = scores.Sum();
        db.WritingGrades.Add(new WritingGrade
        {
            Id = Guid.NewGuid(),
            SubmissionId = current.SubmissionId,
            C1Purpose = (short)scores[0],
            C2Content = (short)scores[1],
            C3Conciseness = (short)scores[2],
            C4Genre = (short)scores[3],
            C5Organisation = (short)scores[4],
            C6Language = (short)scores[5],
            RawTotal = (short)rawTotal,
            EstimatedBand = rawTotal,
            BandLabel = RawBandLabel(rawTotal),
            PerCriterionFeedbackJson = current.PerCriterionFeedbackJson,
            TopThreePrioritiesJson = current.TopThreePrioritiesJson,
            ConfidenceFlag = "tutor_reviewed",
            ModelUsed = current.ModelUsed,
            CanonVersion = current.CanonVersion,
            AppealedByGradeId = current.Id,
            TutorReviewId = review.Id,
            GradedAt = now,
            CreatedAt = now,
        });
    }

    private static IReadOnlyDictionary<string, int>? NormalizeScoreOverride(IReadOnlyDictionary<string, int>? scoreOverride)
    {
        if (scoreOverride is null || scoreOverride.Count == 0) return null;
        var normalized = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in scoreOverride)
        {
            var (canonical, max) = NormalizeScoreKey(key);
            normalized[canonical] = Math.Clamp(value, 0, max);
        }
        return normalized;
    }

    private static (string Canonical, int Max) NormalizeScoreKey(string key)
        => key.Trim().ToLowerInvariant() switch
        {
            "c1" or "c1purpose" or "purpose" => ("c1Purpose", 3),
            "c2" or "c2content" or "content" => ("c2Content", 7),
            "c3" or "c3conciseness" or "conciseness" => ("c3Conciseness", 7),
            "c4" or "c4genre" or "genre" => ("c4Genre", 7),
            "c5" or "c5organisation" or "c5organization" or "organisation" or "organization" => ("c5Organisation", 7),
            "c6" or "c6language" or "language" => ("c6Language", 7),
            _ => throw ApiException.Validation("writing_tutor_invalid_score_key", $"Unknown tutor score criterion '{key}'."),
        };

    private static string RawBandLabel(int rawTotal)
    {
        if (rawTotal >= 38) return "A";
        if (rawTotal >= 34) return "B+";
        if (rawTotal >= 30) return "B";
        if (rawTotal >= 24) return "C+";
        if (rawTotal >= 18) return "C";
        if (rawTotal >= 12) return "D";
        return "E";
    }

    private static string? NormalizeQueueStatus(string? status)
        => string.IsNullOrWhiteSpace(status)
            ? null
            : status.Trim().ToLowerInvariant() switch
            {
                "pending" => "pending",
                "claimed" => "claimed",
                "submitted" => "submitted",
                _ => throw ApiException.Validation("writing_tutor_invalid_queue_status", "Unsupported tutor queue status."),
            };

    public async Task<WritingTutorReviewView?> GetReviewForSubmissionAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var submission = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null) return null;
        if (!string.Equals(submission.UserId, userId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("writing_tutor_review_forbidden", "Tutor review belongs to another learner.");
        }
        var review = await db.WritingTutorReviews.AsNoTracking().FirstOrDefaultAsync(r => r.SubmissionId == submissionId, ct);
        return review is null ? null : ToView(review);
    }

    public async Task<WritingTutorReviewView> RequestReviewAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var submission = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("writing_submission_not_found", "Submission was not found.");
        var status = await GetQueueStatusAsync(ct);
        if (status.Paused)
        {
            throw ApiException.ServiceUnavailable("writing_tutor_queue_paused", $"Tutor review queue is paused ({status.Reason}). Please try again later.");
        }
        var now = clock.GetUtcNow();
        var assignment = await db.WritingTutorReviewAssignments.FirstOrDefaultAsync(a => a.SubmissionId == submission.Id, ct);
        if (assignment is null)
        {
            assignment = new WritingTutorReviewAssignment
            {
                Id = Guid.NewGuid(),
                SubmissionId = submission.Id,
                TutorId = string.Empty,
                ClaimedAt = now,
                DueAt = now.AddHours(24),
                Status = "pending",
            };
            db.WritingTutorReviewAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
        }
        return new WritingTutorReviewView(Guid.Empty, submission.Id, string.Empty, assignment.Status, null, null, null, null);
    }

    private static WritingTutorReviewView ToView(WritingTutorReview review)
        => new(review.Id, review.SubmissionId, review.TutorId, review.Status,
            review.FreeTextFeedback, review.PerCriterionCommentsJson, review.ScoreOverrideJson, review.SubmittedAt);

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingTutorReviewResponse?> RequestTutorReviewAsync(string userId, Guid submissionId, string? priority, CancellationToken ct)
    {
        _ = priority;
        var submission = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId && s.UserId == userId, ct);
        if (submission is null) return null;
        var view = await RequestReviewAsync(userId, submissionId, ct);
        return WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingTutorReviewResponse?> GetTutorReviewAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var view = await GetReviewForSubmissionAsync(userId, submissionId, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingTutorQueueResponse> GetTutorQueueAsync(string tutorId, string? status, CancellationToken ct)
    {
        var rows = await GetQueueAsync(tutorId, ct);
        var normalizedStatus = NormalizeQueueStatus(status);
        if (normalizedStatus is not null)
        {
            rows = rows.Where(r => string.Equals(r.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return new WritingTutorQueueResponse(rows.Select(WritingV2ResponseMapper.ToResponse).ToList());
    }

    public async Task<WritingTutorReviewResponse?> ClaimSubmissionForReviewAsync(string tutorId, Guid submissionId, CancellationToken ct)
    {
        try
        {
            var view = await ClaimAsync(tutorId, submissionId, ct);
            return WritingV2ResponseMapper.ToResponse(view);
        }
        catch (ApiException ex) when (ex.ErrorCode == "writing_tutor_assignment_not_found")
        {
            return null;
        }
    }

    public async Task<WritingTutorReviewResponse> SubmitTutorReviewAsync(string tutorId, WritingTutorReviewSubmitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyDictionary<string, int>? scoreOverride = request.ScoreOverride is null
            ? null
            : request.ScoreOverride.ToDictionary(kvp => kvp.Key, kvp => (int)Math.Round(kvp.Value));
        var view = await SubmitReviewAsync(tutorId, request.SubmissionId, new WritingTutorReviewInternalSubmitRequest(
            FreeTextFeedback: request.FreeTextFeedback ?? string.Empty,
            PerCriterionComments: request.PerCriterionComments,
            ScoreOverride: scoreOverride), ct);
        return WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingTutorCalibrationResponse> GetTutorCalibrationAsync(string tutorId, CancellationToken ct)
    {
        var cal = await db.WritingTutorCalibrations.AsNoTracking().FirstOrDefaultAsync(c => c.TutorId == tutorId, ct);
        var coefficient = cal is null ? 0d : (double)cal.AgreementCoefficient;
        return new WritingTutorCalibrationResponse(
            TutorId: tutorId,
            AgreementCoefficient: coefficient,
            RequiresRecalibration: coefficient < 0.7d,
            LastCalibratedAt: cal?.LastCalibratedAt);
    }
}
