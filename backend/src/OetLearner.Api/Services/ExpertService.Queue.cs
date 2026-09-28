using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

public partial class ExpertService
{
    public async Task<ExpertMeResponse> GetMeAsync(string userId, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(userId, ct);

        return new ExpertMeResponse(
            expert.Id,
            expert.Role,
            expert.DisplayName,
            expert.Email,
            expert.Timezone,
            expert.IsActive,
            JsonSupport.Deserialize(expert.SpecialtiesJson, Array.Empty<string>()),
            expert.CreatedAt);
    }

    public async Task<ExpertQueueResponse> GetQueueAsync(string reviewerId, ExpertQueueQueryRequest request, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var page = request.Page is > 0 ? request.Page.Value : 1;
        var pageSize = Math.Clamp(request.PageSize ?? 50, 1, MaxQueuePageSize);
        var now = DateTimeOffset.UtcNow;

        // Writing reviews live in the V2 submission flow (WritingSubmission →
        // WritingTutorReviewAssignment, surfaced by /v1/tutors/writing/queue and the
        // expert writing queue at /expert/queue/assigned). A writing ReviewRequest has
        // no WritingSubmission link, so it can never resolve in the submission-keyed
        // marking workspace — keep it out of this V1 expert queue/dashboard. Speaking
        // (and any future ReviewRequest-based subtest) still belongs here.
        var reviewRequests = await db.ReviewRequests
            .AsNoTracking()
            .Where(rr => (rr.State == ReviewRequestState.Queued || rr.State == ReviewRequestState.InReview)
                         && rr.SubtestCode != "writing")
            .ToListAsync(ct);

        if (reviewRequests.Count == 0)
        {
            return new ExpertQueueResponse([], 0, page, pageSize, now);
        }

        var reviewRequestIds = reviewRequests.Select(rr => rr.Id).ToList();
        var attemptIds = reviewRequests.Select(rr => rr.AttemptId).Distinct().ToList();

        var attempts = await db.Attempts
            .AsNoTracking()
            .Where(attempt => attemptIds.Contains(attempt.Id))
            .ToDictionaryAsync(attempt => attempt.Id, ct);

        var learnerIds = attempts.Values.Select(attempt => attempt.UserId).Distinct().ToList();
        var learners = await db.Users
            .AsNoTracking()
            .Where(user => learnerIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, ct);

        var assignments = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => reviewRequestIds.Contains(assignment.ReviewRequestId))
            .ToListAsync(ct);

        var evaluations = attemptIds.Count == 0
            ? []
            : await ToOrderedListDescendingAsync(
                db.Evaluations
                    .AsNoTracking()
                    .Where(evaluation => attemptIds.Contains(evaluation.AttemptId)),
                evaluation => evaluation.GeneratedAt,
                ct);

        var latestEvaluations = evaluations
            .GroupBy(evaluation => evaluation.AttemptId)
            .ToDictionary(group => group.Key, group => group.First());

        var activeAssignments = assignments
            .Where(assignment => assignment.ClaimState != ExpertAssignmentState.Released)
            .GroupBy(assignment => assignment.ReviewRequestId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(assignment => assignment.AssignedAt ?? DateTimeOffset.MinValue)
                    .ThenByDescending(assignment => assignment.ReleasedAt ?? DateTimeOffset.MinValue)
                    .First());

        var assignedReviewerIds = activeAssignments.Values
            .Select(assignment => assignment.AssignedReviewerId)
            .Where(assignedReviewerId => !string.IsNullOrWhiteSpace(assignedReviewerId))
            .Distinct()
            .Cast<string>()
            .ToList();

        var assignedReviewers = assignedReviewerIds.Count == 0
            ? new Dictionary<string, ExpertUser>()
            : await db.ExpertUsers
                .AsNoTracking()
                .Where(expert => assignedReviewerIds.Contains(expert.Id))
                .ToDictionaryAsync(expert => expert.Id, ct);

        var items = reviewRequests
            .Select(reviewRequest => BuildQueueItem(reviewRequest, attempts, learners, activeAssignments, assignedReviewers, latestEvaluations, reviewerId, now))
            .Where(item => item is not null)
            .Cast<ExpertQueueItemResponse>()
            .ToList();

        items = ApplyQueueFilters(items, request);

        var totalCount = items.Count;
        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new ExpertQueueResponse(pagedItems, totalCount, page, pageSize, now);
    }

    /// <summary>Phase-4 endpoint backing <c>GET /v1/expert/queue/assigned-to-me</c>.
    /// Returns review requests the auto-assigner has already pre-bound to the
    /// calling expert (ClaimState != Released). Items are sorted by their
    /// SLA-due timestamp so the most urgent appears first.</summary>
    public async Task<IReadOnlyList<ExpertAssignedItemResponse>> GetAssignedToMeAsync(
        string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        const int slaHoursStandard = 48;
        const int slaHoursExpress = 12;
        var now = DateTimeOffset.UtcNow;

        var rows = await (
            from a in db.ExpertReviewAssignments.AsNoTracking()
            where a.AssignedReviewerId == reviewerId
               && a.ClaimState != ExpertAssignmentState.Released
            join r in db.ReviewRequests.AsNoTracking() on a.ReviewRequestId equals r.Id
            where r.SubtestCode == "writing" && r.State != ReviewRequestState.Completed
            join attempt in db.Attempts.AsNoTracking() on r.AttemptId equals attempt.Id
            join paper in db.ContentPapers.AsNoTracking() on attempt.ContentId equals paper.Id
            join learner in db.Users.AsNoTracking() on attempt.UserId equals learner.Id into learnerJoin
            from learner in learnerJoin.DefaultIfEmpty()
            select new
            {
                Assignment = a,
                Request = r,
                Attempt = attempt,
                Paper = paper,
                LearnerDisplayName = learner != null ? learner.DisplayName : null,
            }).ToListAsync(ct);

        return rows
            .Select(row =>
            {
                var slaHours = string.Equals(row.Request.TurnaroundOption, "express", StringComparison.OrdinalIgnoreCase)
                    ? slaHoursExpress : slaHoursStandard;
                var slaDueAt = (row.Assignment.AssignedAt ?? row.Request.CreatedAt).AddHours(slaHours);
                var slaState = ComputeSlaState(slaDueAt, now);
                return new ExpertAssignedItemResponse(
                    ReviewRequestId: row.Request.Id,
                    AttemptId: row.Attempt.Id,
                    SubtestCode: "writing",
                    ProfessionId: row.Paper.ProfessionId,
                    TaskTitle: row.Paper.Title,
                    LearnerDisplayName: row.LearnerDisplayName ?? string.Empty,
                    LetterType: row.Paper.LetterType,
                    AssignedAt: row.Assignment.AssignedAt ?? row.Request.CreatedAt,
                    SlaDueAt: slaDueAt,
                    SlaState: slaState,
                    TurnaroundOption: row.Request.TurnaroundOption,
                    ReviewerCompensation: row.Request.ReviewerCompensation,
                    ClaimState: row.Assignment.ClaimState.ToString());
            })
            .OrderBy(x => x.SlaDueAt)
            .ToList();
    }

    private static string ComputeSlaState(DateTimeOffset slaDueAt, DateTimeOffset now)
    {
        if (now >= slaDueAt) return "overdue";
        var remaining = slaDueAt - now;
        return remaining.TotalHours <= 6 ? "at_risk" : "on_track";
    }

    public async Task<ExpertDashboardResponse> GetDashboardAsync(string reviewerId, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);
        var now = DateTimeOffset.UtcNow;

        var assignedQueue = await GetQueueAsync(reviewerId, new ExpertQueueQueryRequest
        {
            Assignment = "assigned",
            Page = 1,
            PageSize = 5
        }, ct);

        var overdueAssignedQueue = await GetQueueAsync(reviewerId, new ExpertQueueQueryRequest
        {
            Assignment = "assigned",
            Overdue = true,
            Page = 1,
            PageSize = 5
        }, ct);

        var draftRows = await ToOrderedListDescendingAsync(
            db.ExpertReviewDrafts
                .AsNoTracking()
                .Where(draft => draft.ReviewerId == reviewerId && (draft.State == null || draft.State != "submitted")),
            draft => draft.DraftSavedAt,
            ct);

        var draftReviewIds = draftRows.Select(draft => draft.ReviewRequestId).Distinct().ToHashSet(StringComparer.Ordinal);
        var resumeDrafts = assignedQueue.Items
            .Where(item => draftReviewIds.Contains(item.Id))
            .OrderBy(item => item.IsOverdue ? 0 : 1)
            .ThenBy(item => item.SlaDue)
            .Take(3)
            .ToList();

        var pendingCalibrationCount = await db.ExpertCalibrationCases
            .AsNoTracking()
            .Where(calibrationCase => !db.ExpertCalibrationResults.Any(result =>
                result.CalibrationCaseId == calibrationCase.Id &&
                result.ReviewerId == reviewerId &&
                !result.IsDraft))
            .CountAsync(ct);

        var assignedLearnerCount = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => assignment.AssignedReviewerId == reviewerId)
            .Join(db.ReviewRequests.AsNoTracking(), assignment => assignment.ReviewRequestId, reviewRequest => reviewRequest.Id, (assignment, reviewRequest) => reviewRequest.AttemptId)
            .Join(db.Attempts.AsNoTracking(), attemptId => attemptId, attempt => attempt.Id, (_, attempt) => attempt.UserId)
            .Distinct()
            .CountAsync(ct);

        var metrics = await GetMetricsAsync(reviewerId, 7, ct);

        var availability = await FirstOrDefaultOrderedDescendingAsync(
            db.ExpertAvailabilities
                .AsNoTracking()
                .Where(existingAvailability => existingAvailability.ReviewerId == reviewerId),
            existingAvailability => existingAvailability.EffectiveFrom,
            ct);

        var todayKey = ResolveTodayKey(expert.Timezone);
        var todaySchedule = availability is null
            ? DefaultScheduleDays().GetValueOrDefault(todayKey)
            : JsonSupport.Deserialize(availability.DaysJson, DefaultScheduleDays()).GetValueOrDefault(todayKey);

        var auditEvents = await ToOrderedListDescendingAsync(
            db.AuditEvents
                .AsNoTracking()
                .Where(auditEvent => auditEvent.ActorId == reviewerId || auditEvent.ActorName == expert.DisplayName),
            auditEvent => auditEvent.OccurredAt,
            ct,
            take: 6);

        var recentActivity = auditEvents
            .Select(auditEvent => new ExpertDashboardActivityResponse(
                auditEvent.OccurredAt,
                "audit",
                auditEvent.Action,
                auditEvent.Details,
                auditEvent.ResourceId is null ? null : BuildReviewRoute(auditEvent.ResourceId)))
            .ToList();

        return new ExpertDashboardResponse(
            metrics.Metrics,
            assignedQueue.TotalCount,
            overdueAssignedQueue.TotalCount,
            draftRows.Count,
            pendingCalibrationCount,
            assignedLearnerCount,
            now,
            new ExpertDashboardAvailabilityResponse(
                availability?.Timezone ?? expert.Timezone,
                todayKey,
                todaySchedule?.Active ?? false,
                todaySchedule is null ? null : $"{todaySchedule.Start}-{todaySchedule.End}",
                availability?.EffectiveFrom),
            assignedQueue.Items,
            resumeDrafts,
            recentActivity);
    }

    public async Task<object> ClaimReviewAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        for (var attemptNumber = 0; attemptNumber < 2; attemptNumber++)
        {
            try
            {
                var expert = await EnsureExpertAsync(reviewerId, ct);
                db.Entry(expert).Property(x => x.IsActive).IsModified = true;

                var reviewRequest = await db.ReviewRequests.FirstOrDefaultAsync(rr => rr.Id == reviewRequestId, ct)
                    ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

                if (reviewRequest.State is ReviewRequestState.Completed or ReviewRequestState.Cancelled or ReviewRequestState.Failed)
                {
                    throw ApiException.Conflict("review_not_claimable", "Only active reviews can be claimed.");
                }

                var assignment = await GetActiveAssignmentAsync(reviewRequestId, tracked: true, ct);

                // If already InReview, only the currently assigned reviewer can re-claim
                if (reviewRequest.State == ReviewRequestState.InReview)
                {
                    if (assignment is not null && !string.Equals(assignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
                    {
                        throw ApiException.Conflict("review_already_claimed", "This review is already assigned to another reviewer.");
                    }
                }
                else if (assignment is not null && !string.Equals(assignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
                {
                    throw ApiException.Conflict("review_already_claimed", "This review is already assigned to another reviewer.");
                }

                if (assignment is null)
                {
                    assignment = new ExpertReviewAssignment
                    {
                        Id = $"era-{Guid.NewGuid():N}",
                        ReviewRequestId = reviewRequestId,
                        AssignedReviewerId = reviewerId,
                        AssignedAt = DateTimeOffset.UtcNow,
                        ClaimState = ExpertAssignmentState.Claimed
                    };
                    db.ExpertReviewAssignments.Add(assignment);
                }
                else
                {
                    assignment.AssignedReviewerId = reviewerId;
                    assignment.AssignedAt ??= DateTimeOffset.UtcNow;
                    assignment.ClaimState = ExpertAssignmentState.Claimed;
                    assignment.ReleasedAt = null;
                    assignment.ReasonCode = null;
                }

                reviewRequest.State = ReviewRequestState.InReview;
                reviewRequest.CompletedAt = null;

                await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Claimed Review", reviewRequestId, "Review claimed from the expert queue.", ct);
                await RecordExpertEventAsync(reviewerId, "expert_review_claimed", new { reviewRequestId }, ct);
                await db.SaveChangesAsync(ct);
                await notifications.CreateForExpertAsync(
                    NotificationEventKey.ExpertReviewClaimed,
                    reviewerId,
                    "review_request",
                    reviewRequestId,
                    (assignment.AssignedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["reviewRequestId"] = reviewRequestId,
                        ["message"] = "You claimed this review from the expert queue."
                    },
                    ct);

                logger.LogInformation("Expert {ReviewerId} claimed review {ReviewRequestId}", reviewerId, reviewRequestId);
                return new { claimed = true, reviewRequestId };
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber == 0)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw ApiException.Conflict(
            "review_claim_conflict",
            "The review was claimed at the same time as your request. Refresh the queue and try again.");
    }

    public async Task<object> ReleaseReviewAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        for (var attemptNumber = 0; attemptNumber < 2; attemptNumber++)
        {
            try
            {
                var expert = await EnsureExpertAsync(reviewerId, ct);
                db.Entry(expert).Property(x => x.IsActive).IsModified = true;

                var reviewRequest = await db.ReviewRequests.FirstOrDefaultAsync(rr => rr.Id == reviewRequestId, ct)
                    ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

                var assignment = await GetActiveAssignmentAsync(reviewRequestId, tracked: true, ct);
                if (assignment is null || !string.Equals(assignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
                {
                    throw ApiException.Forbidden("review_not_owned", "You can only release reviews currently assigned to you.");
                }

                assignment.ClaimState = ExpertAssignmentState.Released;
                assignment.ReleasedAt = DateTimeOffset.UtcNow;
                assignment.ReasonCode = "released";

                if (reviewRequest.State != ReviewRequestState.Completed)
                {
                    reviewRequest.State = ReviewRequestState.Queued;
                    reviewRequest.CompletedAt = null;
                }

                await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Released Review", reviewRequestId, "Review released back to the queue.", ct);
                await RecordExpertEventAsync(reviewerId, "expert_review_released", new { reviewRequestId }, ct);
                await db.SaveChangesAsync(ct);
                await notifications.CreateForExpertAsync(
                    NotificationEventKey.ExpertReviewReleased,
                    reviewerId,
                    "review_request",
                    reviewRequestId,
                    (assignment.ReleasedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["reviewRequestId"] = reviewRequestId,
                        ["message"] = "You released this review back to the shared queue."
                    },
                    ct);

                return new { released = true, reviewRequestId };
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber == 0)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw ApiException.Conflict(
            "review_release_conflict",
            "The review changed while it was being released. Refresh the queue and try again.");
    }

    public async Task<ExpertQueueFilterMetadataResponse> GetQueueFilterMetadataAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var professions = await db.Professions
            .AsNoTracking()
            .Select(p => p.Id)
            .ToListAsync(ct);

        return new ExpertQueueFilterMetadataResponse(
            // Writing is no longer a V1 ReviewRequest-queue subtest (it lives in the V2
            // submission flow / writing review queue), so it is not offered as a filter here.
            Types: ["speaking"],
            Professions: professions.Count > 0 ? professions : ["nursing", "medicine", "dentistry", "pharmacy", "physiotherapy", "radiography", "dietetics", "podiatry", "speech_pathology", "occupational_therapy", "optometry", "veterinary_science"],
            Priorities: ["high", "normal"],
            Statuses: ["queued", "assigned", "in_progress", "overdue", "completed"],
            ConfidenceBands: ["high", "medium", "low", "unknown"],
            AssignmentStates: ["assigned", "unassigned"]);
    }
}
