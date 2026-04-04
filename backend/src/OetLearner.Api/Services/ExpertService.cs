using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class ExpertService(LearnerDbContext db, ILogger<ExpertService> logger, MediaStorageService mediaStorage, PlatformLinkService platformLinks, NotificationService notifications)
{
    private static readonly string[] WritingCriteria = ["purpose", "content", "conciseness", "genre", "organization", "language"];
    private static readonly string[] SpeakingCriteria = ["intelligibility", "fluency", "appropriateness", "grammar", "clinicalCommunication"];
    private const int MaxQueuePageSize = 100;
    private const int MaxLearnerPageSize = 100;
    private const int MaxFinalCommentLength = 4000;
    private const int MaxCriterionCommentLength = 1500;
    private const int MaxCommentTextLength = 1500;
    private const int MaxScratchpadLength = 4000;
    private const int MaxChecklistItemLabelLength = 200;
    private const int MaxChecklistItemCount = 12;
    private const int MaxReworkReasonLength = 1000;
    private const int MaxCalibrationNotesLength = 1500;

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

        var reviewRequests = await db.ReviewRequests
            .AsNoTracking()
            .Where(rr => rr.State == ReviewRequestState.Queued || rr.State == ReviewRequestState.InReview)
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
            .Where(calibrationCase => !db.ExpertCalibrationResults.Any(result => result.CalibrationCaseId == calibrationCase.Id && result.ReviewerId == reviewerId))
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

    public async Task<ExpertLearnerDirectoryResponse> GetLearnersAsync(string reviewerId, ExpertLearnersQueryRequest request, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var page = request.Page is > 0 ? request.Page.Value : 1;
        var pageSize = Math.Clamp(request.PageSize ?? 25, 1, MaxLearnerPageSize);
        var now = DateTimeOffset.UtcNow;

        var assignments = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => assignment.AssignedReviewerId == reviewerId)
            .ToListAsync(ct);

        if (assignments.Count == 0)
        {
            return new ExpertLearnerDirectoryResponse([], 0, page, pageSize, now);
        }

        var reviewIds = assignments.Select(assignment => assignment.ReviewRequestId).Distinct().ToList();
        var reviewRequests = await db.ReviewRequests
            .AsNoTracking()
            .Where(reviewRequest => reviewIds.Contains(reviewRequest.Id))
            .ToListAsync(ct);

        if (reviewRequests.Count == 0)
        {
            return new ExpertLearnerDirectoryResponse([], 0, page, pageSize, now);
        }

        var attemptIds = reviewRequests.Select(reviewRequest => reviewRequest.AttemptId).Distinct().ToList();
        var attempts = await db.Attempts
            .AsNoTracking()
            .Where(attempt => attemptIds.Contains(attempt.Id))
            .ToListAsync(ct);

        if (attempts.Count == 0)
        {
            return new ExpertLearnerDirectoryResponse([], 0, page, pageSize, now);
        }

        var learnerIds = attempts.Select(attempt => attempt.UserId).Distinct().ToList();
        var learners = await db.Users
            .AsNoTracking()
            .Where(user => learnerIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, ct);

        var goals = await db.Goals
            .AsNoTracking()
            .Where(goal => learnerIds.Contains(goal.UserId))
            .ToDictionaryAsync(goal => goal.UserId, ct);

        var items = learnerIds
            .Select(learnerId =>
            {
                if (!learners.TryGetValue(learnerId, out var learner))
                {
                    return null;
                }

                var learnerAttempts = attempts.Where(attempt => string.Equals(attempt.UserId, learnerId, StringComparison.Ordinal)).ToList();
                var learnerReviewRequests = reviewRequests.Where(reviewRequest => learnerAttempts.Any(attempt => string.Equals(attempt.Id, reviewRequest.AttemptId, StringComparison.Ordinal))).ToList();
                if (learnerReviewRequests.Count == 0)
                {
                    return null;
                }

                var lastReview = learnerReviewRequests
                    .OrderByDescending(reviewRequest => reviewRequest.CompletedAt ?? reviewRequest.CreatedAt)
                    .First();

                goals.TryGetValue(learnerId, out var goal);
                var goalScore = goal is null
                    ? "Review context only"
                    : string.Join(" / ", new[]
                    {
                        goal.TargetWritingScore is not null ? $"W {goal.TargetWritingScore}" : null,
                        goal.TargetSpeakingScore is not null ? $"S {goal.TargetSpeakingScore}" : null
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));

                if (string.IsNullOrWhiteSpace(goalScore))
                {
                    goalScore = "Review context only";
                }

                return new ExpertLearnerListItemResponse(
                    learner.Id,
                    learner.DisplayName,
                    learner.ActiveProfessionId ?? "nursing",
                    goalScore,
                    goal?.TargetExamDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    learnerReviewRequests.Count,
                    learnerReviewRequests.Select(reviewRequest => reviewRequest.SubtestCode).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
                    lastReview.Id,
                    lastReview.SubtestCode,
                    MapReviewRequestState(lastReview, assignments.FirstOrDefault(assignment => string.Equals(assignment.ReviewRequestId, lastReview.Id, StringComparison.Ordinal)), reviewerId, now),
                    lastReview.CompletedAt ?? lastReview.CreatedAt);
            })
            .Where(item => item is not null)
            .Cast<ExpertLearnerListItemResponse>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            items = items
                .Where(item =>
                    item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.LastReviewId.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.Profession))
        {
            items = items
                .Where(item => string.Equals(item.Profession, request.Profession, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.SubTest))
        {
            items = items
                .Where(item => item.SubTests.Any(subTest => string.Equals(subTest, request.SubTest, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.Relevance))
        {
            items = request.Relevance.Trim().ToLowerInvariant() switch
            {
                "active" => items.Where(item => item.LastReviewState is "queued" or "assigned" or "in_progress" or "overdue").ToList(),
                "completed" => items.Where(item => item.LastReviewState == "completed").ToList(),
                "overdue" => items.Where(item => item.LastReviewState == "overdue").ToList(),
                "rework" => items.Where(item => item.LastReviewState == "queued").ToList(),
                _ => items
            };
        }

        items = items
            .OrderByDescending(item => item.LastReviewAt)
            .ThenBy(item => item.Name)
            .ToList();

        var totalCount = items.Count;
        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new ExpertLearnerDirectoryResponse(pagedItems, totalCount, page, pageSize, now);
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

    public async Task<ExpertWritingReviewBundleResponse> GetWritingReviewBundleAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var context = await LoadReadContextAsync(reviewRequestId, reviewerId, ct, requireActiveAssignment: true);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a writing review.");
        }

        var writingScores = NormalizeAiSuggestedScores(context.Evaluation, isWriting: true);
        return new ExpertWritingReviewBundleResponse(
            context.ReviewRequest.Id,
            context.Attempt.UserId,
            context.Learner?.DisplayName ?? "Unknown learner",
            context.Learner?.ActiveProfessionId ?? "nursing",
            "writing",
            "writing",
            ToAiConfidence(context.Evaluation?.ConfidenceBand),
            MapPriority(context.ReviewRequest.TurnaroundOption),
            CalculateSlaDueAt(context.ReviewRequest),
            MapSlaState(context.ReviewRequest, DateTimeOffset.UtcNow),
            IsOverdue(context.ReviewRequest, DateTimeOffset.UtcNow),
            context.ActiveAssignment?.AssignedReviewerId,
            ResolveReviewerName(context.ActiveAssignment?.AssignedReviewerId, context.AssignedReviewers),
            MapAssignmentState(context.ActiveAssignment),
            MapQueueStatus(context.ReviewRequest, context.ActiveAssignment, reviewerId, DateTimeOffset.UtcNow),
            context.Attempt.ContentId,
            context.Attempt.Id,
            context.ReviewRequest.CreatedAt,
            context.Attempt.DraftContent,
            context.Content?.CaseNotes ?? "Case notes are not available for this writing review.",
            BuildWritingAiDraftFeedback(context.Evaluation),
            writingScores,
            ExtractModelAnswer(context.Content),
            context.Draft,
            BuildPermissions(context.ReviewRequest, context.ActiveAssignment, reviewerId),
            new Dictionary<string, ExpertArtifactStateResponse>(StringComparer.OrdinalIgnoreCase)
            {
                ["aiDraftFeedback"] = BuildEvaluationArtifactState(context.Evaluation, "AI draft feedback is still being prepared.")
            });
    }

    public async Task<ExpertSpeakingReviewBundleResponse> GetSpeakingReviewBundleAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var context = await LoadReadContextAsync(reviewRequestId, reviewerId, ct, requireActiveAssignment: true);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a speaking review.");
        }

        var transcriptLines = ExtractTranscriptLines(context.Attempt);
        return new ExpertSpeakingReviewBundleResponse(
            context.ReviewRequest.Id,
            context.Attempt.UserId,
            context.Learner?.DisplayName ?? "Unknown learner",
            context.Learner?.ActiveProfessionId ?? "nursing",
            "speaking",
            "speaking",
            ToAiConfidence(context.Evaluation?.ConfidenceBand),
            MapPriority(context.ReviewRequest.TurnaroundOption),
            CalculateSlaDueAt(context.ReviewRequest),
            MapSlaState(context.ReviewRequest, DateTimeOffset.UtcNow),
            IsOverdue(context.ReviewRequest, DateTimeOffset.UtcNow),
            context.ActiveAssignment?.AssignedReviewerId,
            ResolveReviewerName(context.ActiveAssignment?.AssignedReviewerId, context.AssignedReviewers),
            MapAssignmentState(context.ActiveAssignment),
            MapQueueStatus(context.ReviewRequest, context.ActiveAssignment, reviewerId, DateTimeOffset.UtcNow),
            context.Attempt.ContentId,
            context.Attempt.Id,
            context.ReviewRequest.CreatedAt,
            platformLinks.BuildApiUrl($"/v1/expert/reviews/{Uri.EscapeDataString(reviewRequestId)}/speaking/audio"),
            transcriptLines,
            ExtractRoleCard(context.Content),
            ExtractAiFlags(context.Evaluation),
            NormalizeAiSuggestedScores(context.Evaluation, isWriting: false),
            context.Draft,
            BuildPermissions(context.ReviewRequest, context.ActiveAssignment, reviewerId),
            new Dictionary<string, ExpertArtifactStateResponse>(StringComparer.OrdinalIgnoreCase)
            {
                ["audio"] = BuildAudioArtifactState(context.Attempt),
                ["transcript"] = BuildTranscriptArtifactState(transcriptLines),
                ["aiFlags"] = BuildEvaluationArtifactState(context.Evaluation, "AI analysis is still being prepared.")
            });
    }

    public async Task<StoredMediaFile> GetSpeakingReviewAudioAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var context = await LoadReadContextAsync(reviewRequestId, reviewerId, ct, requireActiveAssignment: true);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review does not include a speaking audio recording.");
        }

        if (string.IsNullOrWhiteSpace(context.Attempt.AudioObjectKey))
        {
            throw ApiException.NotFound("audio_not_found", "No uploaded audio is available for this speaking attempt.");
        }

        var metadata = JsonSupport.Deserialize(context.Attempt.AudioMetadataJson, new Dictionary<string, object?>());
        var contentType = metadata.TryGetValue("contentType", out var value) ? value?.ToString() : null;
        return mediaStorage.OpenRead(context.Attempt.AudioObjectKey, contentType);
    }

    public async Task<ExpertDraftResponse> SaveDraftAsync(string reviewRequestId, string reviewerId, ExpertDraftSaveRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        ValidateDraftRequest(context.ReviewRequest.SubtestCode, request);

        var draft = await db.ExpertReviewDrafts
            .FirstOrDefaultAsync(existingDraft => existingDraft.ReviewRequestId == reviewRequestId && existingDraft.ReviewerId == reviewerId, ct);

        if (draft is null)
        {
            draft = new ExpertReviewDraft
            {
                Id = $"erd-{Guid.NewGuid():N}",
                ReviewRequestId = reviewRequestId,
                ReviewerId = reviewerId,
                Version = 0
            };
            db.ExpertReviewDrafts.Add(draft);
        }

        if (request.Version is not null && request.Version != draft.Version)
        {
            throw ApiException.Conflict(
                "draft_version_conflict",
                "This draft has changed since you opened it.",
                [new ApiFieldError("version", "conflict", "Reload the review to merge the latest saved draft before continuing.")]);
        }

        var normalizedScores = NormalizeScores(request.Scores, context.ReviewRequest.SubtestCode);
        var normalizedCriterionComments = NormalizeCriterionComments(request.CriterionComments, context.ReviewRequest.SubtestCode);
        var finalComment = NormalizeFinalComment(request.FinalComment, required: false);
        var anchoredComments = NormalizeAnchoredComments(request.AnchoredComments);
        var timestampComments = NormalizeTimestampComments(request.TimestampComments);
        var scratchpad = NormalizeScratchpad(request.Scratchpad);
        var checklistItems = NormalizeChecklistItems(request.ChecklistItems);

        draft.RubricEntriesJson = JsonSupport.Serialize(normalizedScores);
        draft.CriterionCommentsJson = JsonSupport.Serialize(normalizedCriterionComments);
        draft.FinalCommentDraft = finalComment;
        draft.AnchoredCommentsJson = JsonSupport.Serialize(anchoredComments);
        draft.TimestampCommentsJson = JsonSupport.Serialize(timestampComments);
        draft.ScratchpadJson = JsonSupport.Serialize(scratchpad);
        draft.ChecklistItemsJson = JsonSupport.Serialize(checklistItems);
        draft.Version += 1;
        draft.State = "saved";
        draft.DraftSavedAt = DateTimeOffset.UtcNow;

        context.ReviewRequest.State = ReviewRequestState.InReview;
        context.ActiveAssignment.ClaimState = ExpertAssignmentState.Claimed;

        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, "Saved Review Draft", reviewRequestId, "Expert review draft saved.", ct);
        await RecordExpertEventAsync(reviewerId, "expert_review_draft_saved", new { reviewRequestId, version = draft.Version }, ct);
        await db.SaveChangesAsync(ct);

        return BuildDraftResponse(draft)!;
    }

    public async Task<object> SubmitWritingReviewAsync(string reviewRequestId, string reviewerId, ExpertReviewSubmitRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a writing review.");
        }

        await SubmitReviewAsync(context, reviewerId, request, ct, "Submitted Writing Review", "expert_writing_review_submitted");
        logger.LogInformation("Expert {ReviewerId} submitted writing review for {ReviewRequestId}", reviewerId, reviewRequestId);
        return new { success = true, reviewRequestId };
    }

    public async Task<object> SubmitSpeakingReviewAsync(string reviewRequestId, string reviewerId, ExpertReviewSubmitRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        if (!string.Equals(context.ReviewRequest.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("review_type_mismatch", "This review is not a speaking review.");
        }

        await SubmitReviewAsync(context, reviewerId, request, ct, "Submitted Speaking Review", "expert_speaking_review_submitted");
        logger.LogInformation("Expert {ReviewerId} submitted speaking review for {ReviewRequestId}", reviewerId, reviewRequestId);
        return new { success = true, reviewRequestId };
    }

    public async Task<object> RequestReworkAsync(string reviewRequestId, string reviewerId, ExpertReworkRequest request, CancellationToken ct)
    {
        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        var reason = NormalizeReworkReason(request.Reason);

        context.ReviewRequest.State = ReviewRequestState.Queued;
        context.ReviewRequest.CompletedAt = null;
        context.ActiveAssignment.ClaimState = ExpertAssignmentState.Released;
        context.ActiveAssignment.ReleasedAt = DateTimeOffset.UtcNow;
        context.ActiveAssignment.ReasonCode = reason;

        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, "Requested Review Rework", reviewRequestId, reason, ct);
        await RecordExpertEventAsync(reviewerId, "expert_review_rework_requested", new { reviewRequestId, reason }, ct);
        await db.SaveChangesAsync(ct);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReviewReworkRequested,
            context.Attempt.UserId,
            "review_request",
            reviewRequestId,
            (context.ActiveAssignment.ReleasedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["attemptId"] = context.Attempt.Id,
                ["reviewRequestId"] = reviewRequestId,
                ["subtest"] = context.ReviewRequest.SubtestCode,
                ["message"] = $"Your reviewer requested follow-up work before finalising the {context.ReviewRequest.SubtestCode} review: {reason}"
            },
            ct);
        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminReviewOpsAction,
            "review_request",
            reviewRequestId,
            (context.ActiveAssignment.ReleasedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["reviewRequestId"] = reviewRequestId,
                ["message"] = $"Expert {context.Expert.DisplayName} requested rework on review {reviewRequestId}: {reason}"
            },
            ct);

        return new { success = true, reviewRequestId };
    }

    public async Task<ExpertLearnerProfileResponse> GetLearnerProfileAsync(string learnerId, string reviewerId, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        var accessibleReviewRequests = await LoadAccessibleLearnerReviewRequestsAsync(learnerId, reviewerId, ct);
        if (accessibleReviewRequests.Count == 0)
        {
            throw ApiException.Forbidden("learner_context_forbidden", "You can only view learners connected to reviews assigned to you.");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == learnerId, ct)
            ?? throw ApiException.NotFound("learner_not_found", "The requested learner does not exist.");

        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(existingGoal => existingGoal.UserId == learnerId, ct);
        var attempts = await db.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == learnerId && (attempt.SubtestCode == "writing" || attempt.SubtestCode == "speaking") && attempt.State == AttemptState.Completed)
            .ToListAsync(ct);

        var attemptIds = attempts.Select(attempt => attempt.Id).ToList();
        var evaluations = attemptIds.Count == 0
            ? []
            : await ToOrderedListDescendingAsync(
                db.Evaluations
                    .AsNoTracking()
                    .Where(evaluation => attemptIds.Contains(evaluation.AttemptId)),
                evaluation => evaluation.GeneratedAt,
                ct);

        var evaluationByAttemptId = evaluations
            .GroupBy(evaluation => evaluation.AttemptId)
            .ToDictionary(group => group.Key, group => group.First());

        var subTestScores = attempts
            .GroupBy(attempt => attempt.SubtestCode)
            .Select(group =>
            {
                var latestAttempt = group.OrderByDescending(attempt => attempt.CompletedAt ?? attempt.SubmittedAt ?? attempt.StartedAt).First();
                evaluationByAttemptId.TryGetValue(latestAttempt.Id, out var latestEvaluation);
                return new ExpertLearnerSubtestScoreResponse(
                    group.Key,
                    ParseScoreRangeAverage(latestEvaluation?.ScoreRange),
                    latestEvaluation?.GradeRange,
                    group.Count());
            })
            .OrderBy(item => item.SubTest)
            .ToList();

        var historicalAssignments = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => accessibleReviewRequests.Select(rr => rr.Id).Contains(assignment.ReviewRequestId))
            .ToListAsync(ct);

        var reviewerIds = historicalAssignments
            .Select(assignment => assignment.AssignedReviewerId)
            .Where(assignedReviewerId => !string.IsNullOrWhiteSpace(assignedReviewerId))
            .Distinct()
            .Cast<string>()
            .ToList();

        var reviewers = reviewerIds.Count == 0
            ? new Dictionary<string, ExpertUser>()
            : await db.ExpertUsers
                .AsNoTracking()
                .Where(existingReviewer => reviewerIds.Contains(existingReviewer.Id))
                .ToDictionaryAsync(existingReviewer => existingReviewer.Id, ct);

        var drafts = await db.ExpertReviewDrafts
            .AsNoTracking()
            .Where(draft => accessibleReviewRequests.Select(rr => rr.Id).Contains(draft.ReviewRequestId))
            .ToListAsync(ct);

        var submittedDrafts = drafts
            .Where(draft => string.Equals(draft.State, "submitted", StringComparison.OrdinalIgnoreCase))
            .GroupBy(draft => draft.ReviewRequestId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(draft => draft.DraftSavedAt).First());

        var priorReviews = accessibleReviewRequests
            .Where(reviewRequest => reviewRequest.State == ReviewRequestState.Completed)
            .OrderByDescending(reviewRequest => reviewRequest.CompletedAt ?? reviewRequest.CreatedAt)
            .Take(10)
            .Select(reviewRequest =>
            {
                submittedDrafts.TryGetValue(reviewRequest.Id, out var draft);
                var reviewAssignment = historicalAssignments
                    .Where(assignment => assignment.ReviewRequestId == reviewRequest.Id)
                    .OrderByDescending(assignment => assignment.AssignedAt ?? DateTimeOffset.MinValue)
                    .FirstOrDefault();
                var reviewerName = reviewAssignment?.AssignedReviewerId is not null && reviewers.TryGetValue(reviewAssignment.AssignedReviewerId, out var reviewer)
                    ? reviewer.DisplayName
                    : expert.DisplayName;

                return new ExpertPriorReviewResponse(
                    reviewRequest.Id,
                    reviewRequest.SubtestCode,
                    reviewerName,
                    reviewRequest.CompletedAt ?? reviewRequest.CreatedAt,
                    draft?.FinalCommentDraft ?? "Review completed.");
            })
            .ToList();

        var goalScore = goal is null
            ? "Review context only"
            : string.Join(" / ", new[]
                {
                    goal.TargetWritingScore is not null ? $"W {goal.TargetWritingScore}" : null,
                    goal.TargetSpeakingScore is not null ? $"S {goal.TargetSpeakingScore}" : null
                }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(goalScore))
        {
            goalScore = "Review context only";
        }

        return new ExpertLearnerProfileResponse(
            user.Id,
            user.DisplayName,
            user.ActiveProfessionId ?? "nursing",
            goalScore,
            goal?.TargetExamDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            attempts.Count,
            user.CreatedAt,
            accessibleReviewRequests.Count,
            subTestScores,
            priorReviews,
            "review_context_only");
    }

    public async Task<IReadOnlyList<ExpertCalibrationCaseSummaryResponse>> GetCalibrationCasesAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var cases = await ToOrderedListDescendingAsync(
            db.ExpertCalibrationCases
                .AsNoTracking(),
            calibrationCase => calibrationCase.CreatedAt,
            ct);

        var results = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(result => result.ReviewerId == reviewerId)
            .ToDictionaryAsync(result => result.CalibrationCaseId, ct);

        return cases.Select(calibrationCase =>
        {
            results.TryGetValue(calibrationCase.Id, out var result);
            return new ExpertCalibrationCaseSummaryResponse(
                calibrationCase.Id,
                calibrationCase.Title,
                calibrationCase.ProfessionId,
                calibrationCase.SubtestCode,
                calibrationCase.SubtestCode,
                calibrationCase.BenchmarkScore,
                result?.ReviewerScore,
                result is not null ? "completed" : "pending",
                calibrationCase.CreatedAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<ExpertCalibrationNoteResponse>> GetCalibrationNotesAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var notes = await ToOrderedListDescendingAsync(
            db.ExpertCalibrationNotes
                .AsNoTracking()
                .Where(note => note.ReviewerId == reviewerId || note.ReviewerId == null),
            note => note.CreatedAt,
            ct,
            take: 50);

        return notes.Select(note => new ExpertCalibrationNoteResponse(
            note.Id,
            note.Type.ToString().ToLowerInvariant(),
            note.Message,
            note.CaseId,
            note.CreatedAt)).ToList();
    }

    public async Task<ExpertCalibrationCaseDetailResponse> GetCalibrationCaseDetailAsync(string caseId, string reviewerId, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        var calibrationCase = await db.ExpertCalibrationCases
            .AsNoTracking()
            .FirstOrDefaultAsync(existingCase => existingCase.Id == caseId, ct)
            ?? throw ApiException.NotFound("calibration_case_not_found", "The requested calibration case does not exist.");

        var existingSubmission = await db.ExpertCalibrationResults
            .AsNoTracking()
            .FirstOrDefaultAsync(result => result.CalibrationCaseId == caseId && result.ReviewerId == reviewerId, ct);

        var artifacts = DeserializeCalibrationArtifacts(calibrationCase);
        var benchmarkRubric = DeserializeCalibrationRubric(calibrationCase);
        var referenceNotes = DeserializeCalibrationReferenceNotes(calibrationCase);

        return new ExpertCalibrationCaseDetailResponse(
            calibrationCase.Id,
            calibrationCase.Title,
            calibrationCase.ProfessionId,
            calibrationCase.SubtestCode,
            calibrationCase.SubtestCode,
            calibrationCase.BenchmarkLabel,
            calibrationCase.BenchmarkScore,
            calibrationCase.Difficulty,
            existingSubmission is not null ? "completed" : "pending",
            calibrationCase.CreatedAt,
            artifacts,
            benchmarkRubric,
            referenceNotes,
            existingSubmission is null
                ? null
                : new ExpertCalibrationSubmissionResponse(
                    reviewerId,
                    expert.DisplayName,
                    existingSubmission.ReviewerScore,
                    existingSubmission.AlignmentScore,
                    existingSubmission.DisagreementSummary,
                    existingSubmission.Notes,
                    JsonSupport.Deserialize(existingSubmission.SubmittedRubricJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
                    existingSubmission.SubmittedAt));
    }

    public async Task<object> SubmitCalibrationAsync(string caseId, string reviewerId, ExpertCalibrationSubmitRequest request, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        if (request.Scores.Count == 0)
        {
            throw ApiException.Validation(
                "calibration_scores_required",
                "Provide at least one calibration score before submitting.",
                [new ApiFieldError("scores", "required", "Add one or more scores before submitting this calibration case.")]);
        }

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Trim().Length > MaxCalibrationNotesLength)
        {
            throw ApiException.Validation(
                "calibration_notes_too_long",
                "Calibration notes are too long.",
                [new ApiFieldError("notes", "too_long", $"Calibration notes cannot exceed {MaxCalibrationNotesLength} characters.")]);
        }

        var calibrationCase = await db.ExpertCalibrationCases.FirstOrDefaultAsync(existingCase => existingCase.Id == caseId, ct)
            ?? throw ApiException.NotFound("calibration_case_not_found", "The requested calibration case does not exist.");

        var existingResult = await db.ExpertCalibrationResults
            .FirstOrDefaultAsync(result => result.CalibrationCaseId == caseId && result.ReviewerId == reviewerId, ct);
        if (existingResult is not null)
        {
            throw ApiException.Conflict("calibration_already_submitted", "This calibration case has already been submitted.");
        }

        var normalizedScores = NormalizeScores(request.Scores, calibrationCase.SubtestCode);
        var benchmarkRubric = DeserializeCalibrationRubric(calibrationCase);
        var benchmarkLookup = benchmarkRubric.ToDictionary(
            entry => entry.Criterion,
            entry => entry.BenchmarkScore,
            StringComparer.OrdinalIgnoreCase);

        var reviewerScore = normalizedScores.Count == 0
            ? 0
            : (int)Math.Round(normalizedScores.Values.Average(), MidpointRounding.AwayFromZero);

        var maxCriterionScore = string.Equals(calibrationCase.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? 7 : 6;
        var comparableCriteria = normalizedScores.Keys
            .Where(criterion => benchmarkLookup.ContainsKey(criterion))
            .ToList();

        var alignment = comparableCriteria.Count == 0
            ? calibrationCase.BenchmarkScore > 0
                ? Math.Round(100.0 - Math.Abs(reviewerScore - calibrationCase.BenchmarkScore) / calibrationCase.BenchmarkScore * 100.0, 1)
                : 100.0
            : Math.Round(
                Math.Max(
                    0.0,
                    100.0 - comparableCriteria.Sum(criterion => Math.Abs(normalizedScores[criterion] - benchmarkLookup[criterion])) * 100.0 / (comparableCriteria.Count * maxCriterionScore)),
                1);

        var largestDelta = comparableCriteria
            .Select(criterion => new
            {
                Criterion = criterion,
                Gap = Math.Abs(normalizedScores[criterion] - benchmarkLookup[criterion])
            })
            .OrderByDescending(item => item.Gap)
            .FirstOrDefault();

        var disagreementSummary = largestDelta is null || largestDelta.Gap == 0
            ? "Aligned with benchmark."
            : $"{ToLabel(largestDelta.Criterion)} differs from benchmark by {largestDelta.Gap} point(s).";

        db.ExpertCalibrationResults.Add(new ExpertCalibrationResult
        {
            Id = $"ecr-{Guid.NewGuid():N}",
            CalibrationCaseId = caseId,
            ReviewerId = reviewerId,
            SubmittedRubricJson = JsonSupport.Serialize(normalizedScores),
            ReviewerScore = reviewerScore,
            AlignmentScore = alignment,
            DisagreementSummary = disagreementSummary,
            Notes = request.Notes?.Trim() ?? string.Empty,
            SubmittedAt = DateTimeOffset.UtcNow
        });

        db.ExpertCalibrationNotes.Add(new ExpertCalibrationNote
        {
            Id = $"ecn-{Guid.NewGuid():N}",
            Type = CalibrationNoteType.Completed,
            Message = $"Completed {calibrationCase.Title}. Alignment: {alignment}%.",
            CaseId = caseId,
            ReviewerId = reviewerId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Submitted Calibration", caseId, disagreementSummary, ct);
        await RecordExpertEventAsync(reviewerId, "expert_calibration_submitted", new { caseId, alignment }, ct);
        await db.SaveChangesAsync(ct);

        return new { success = true, caseId, alignment };
    }

    public async Task<object> GetAvailabilityAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var availability = await FirstOrDefaultOrderedDescendingAsync(
            db.ExpertAvailabilities
                .AsNoTracking()
                .Where(existingAvailability => existingAvailability.ReviewerId == reviewerId),
            existingAvailability => existingAvailability.EffectiveFrom,
            ct);

        if (availability is null)
        {
            return new
            {
                timezone = "UTC",
                days = DefaultScheduleDays(),
                lastUpdatedAt = (DateTimeOffset?)null
            };
        }

        return new
        {
            timezone = availability.Timezone,
            days = JsonSupport.Deserialize(availability.DaysJson, DefaultScheduleDays()),
            lastUpdatedAt = availability.EffectiveFrom
        };
    }

    public async Task<object> SaveAvailabilityAsync(string reviewerId, ExpertAvailabilityUpdateRequest request, CancellationToken ct)
    {
        ValidateAvailabilityRequest(request);

        var expert = await EnsureExpertAsync(reviewerId, ct);
        var availability = await db.ExpertAvailabilities
            .FirstOrDefaultAsync(existingAvailability => existingAvailability.ReviewerId == reviewerId, ct);

        if (availability is null)
        {
            availability = new ExpertAvailability
            {
                Id = $"ea-{Guid.NewGuid():N}",
                ReviewerId = reviewerId,
                EffectiveFrom = DateTimeOffset.UtcNow
            };
            db.ExpertAvailabilities.Add(availability);
        }

        availability.Timezone = request.Timezone.Trim();
        availability.DaysJson = JsonSupport.Serialize(request.Days);
        availability.EffectiveFrom = DateTimeOffset.UtcNow;
        expert.Timezone = availability.Timezone;

        await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Updated Availability", reviewerId, $"Timezone set to {availability.Timezone}.", ct);
        await RecordExpertEventAsync(reviewerId, "expert_schedule_saved", new { timezone = availability.Timezone }, ct);
        await db.SaveChangesAsync(ct);

        return new
        {
            timezone = availability.Timezone,
            days = request.Days,
            lastUpdatedAt = availability.EffectiveFrom
        };
    }

    public async Task<ExpertQueueFilterMetadataResponse> GetQueueFilterMetadataAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var professions = await db.Professions
            .AsNoTracking()
            .Select(p => p.Id)
            .ToListAsync(ct);

        return new ExpertQueueFilterMetadataResponse(
            Types: ["writing", "speaking"],
            Professions: professions.Count > 0 ? professions : ["nursing", "medicine", "dentistry", "pharmacy", "physiotherapy", "radiography", "dietetics", "podiatry", "speech_pathology", "occupational_therapy", "optometry", "veterinary_science"],
            Priorities: ["high", "normal"],
            Statuses: ["queued", "assigned", "in_progress", "overdue", "completed"],
            ConfidenceBands: ["high", "medium", "low", "unknown"],
            AssignmentStates: ["assigned", "unassigned"]);
    }

    public async Task<ExpertReviewHistoryResponse> GetReviewHistoryAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var reviewRequest = await db.ReviewRequests.AsNoTracking()
            .FirstOrDefaultAsync(rr => rr.Id == reviewRequestId, ct)
            ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

        var assignments = await db.ExpertReviewAssignments.AsNoTracking()
            .Where(a => a.ReviewRequestId == reviewRequestId)
            .OrderBy(a => a.AssignedAt ?? DateTimeOffset.MinValue)
            .ToListAsync(ct);

        // Verify the requesting expert has access (current or historical)
        var hasAccess = assignments.Any(a => string.Equals(a.AssignedReviewerId, reviewerId, StringComparison.Ordinal));
        if (!hasAccess)
        {
            throw ApiException.Forbidden("review_history_forbidden", "You can only view history for reviews you are or were assigned to.");
        }

        var reviewerIds = assignments
            .Select(a => a.AssignedReviewerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .Cast<string>()
            .ToList();

        var reviewers = reviewerIds.Count == 0
            ? new Dictionary<string, ExpertUser>()
            : await db.ExpertUsers.AsNoTracking()
                .Where(e => reviewerIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, ct);

        var drafts = await db.ExpertReviewDrafts.AsNoTracking()
            .Where(d => d.ReviewRequestId == reviewRequestId)
            .OrderBy(d => d.DraftSavedAt)
            .ToListAsync(ct);

        var auditEvents = await db.AuditEvents.AsNoTracking()
            .Where(ae => ae.ResourceId == reviewRequestId && ae.ResourceType == "ExpertReview")
            .OrderBy(ae => ae.OccurredAt)
            .Take(100)
            .ToListAsync(ct);

        var historyEntries = new List<ExpertReviewHistoryEntryResponse>();

        // Add assignment events
        foreach (var assignment in assignments)
        {
            var reviewerName = assignment.AssignedReviewerId is not null && reviewers.TryGetValue(assignment.AssignedReviewerId, out var r)
                ? r.DisplayName : assignment.AssignedReviewerId ?? "Unknown";

            historyEntries.Add(new ExpertReviewHistoryEntryResponse(
                assignment.AssignedAt ?? DateTimeOffset.MinValue,
                assignment.ClaimState.ToString().ToLowerInvariant(),
                reviewerName,
                assignment.ReasonCode));

            if (assignment.ReleasedAt is not null)
            {
                historyEntries.Add(new ExpertReviewHistoryEntryResponse(
                    assignment.ReleasedAt.Value,
                    "released",
                    reviewerName,
                    assignment.ReasonCode));
            }
        }

        // Add audit trail entries
        foreach (var ae in auditEvents)
        {
            historyEntries.Add(new ExpertReviewHistoryEntryResponse(
                ae.OccurredAt,
                ae.Action.ToLowerInvariant(),
                ae.ActorName,
                ae.Details));
        }

        historyEntries = historyEntries.OrderBy(e => e.Timestamp).ToList();

        return new ExpertReviewHistoryResponse(
            reviewRequestId,
            reviewRequest.State.ToString().ToLowerInvariant(),
            reviewRequest.CreatedAt,
            reviewRequest.CompletedAt,
            drafts.Count,
            historyEntries);
    }

    public async Task<ExpertLearnerReviewContextResponse> GetLearnerReviewContextAsync(string learnerId, string reviewerId, CancellationToken ct)
    {
        var accessibleReviewRequests = await LoadAccessibleLearnerReviewRequestsAsync(learnerId, reviewerId, ct);
        if (accessibleReviewRequests.Count == 0)
        {
            throw ApiException.Forbidden("learner_context_forbidden", "You can only view learners connected to reviews assigned to you.");
        }

        var profile = await GetLearnerProfileAsync(learnerId, reviewerId, ct);
        return new ExpertLearnerReviewContextResponse(
            profile.Id,
            profile.Name,
            profile.Profession,
            profile.GoalScore,
            profile.ExamDate,
            accessibleReviewRequests.Count,
            profile.SubTestScores,
            profile.PriorReviews.Take(3).ToList());
    }

    public async Task<ExpertMetricsResponse> GetMetricsAsync(string reviewerId, int days, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var clampedDays = Math.Clamp(days, 1, 180);
        var windowStart = DateTimeOffset.UtcNow.Date.AddDays(-(clampedDays - 1));

        var assignments = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => assignment.AssignedReviewerId == reviewerId)
            .ToListAsync(ct);

        var reviewIds = assignments.Select(assignment => assignment.ReviewRequestId).Distinct().ToList();
        var reviewRequests = reviewIds.Count == 0
            ? []
            : await db.ReviewRequests
                .AsNoTracking()
                .Where(reviewRequest => reviewIds.Contains(reviewRequest.Id))
                .ToListAsync(ct);

        var handledReviewIds = reviewRequests.Select(reviewRequest => reviewRequest.Id).Distinct().ToHashSet(StringComparer.Ordinal);
        var completedReviews = reviewRequests
            .Where(reviewRequest => reviewRequest.State == ReviewRequestState.Completed && reviewRequest.CompletedAt is not null && reviewRequest.CompletedAt >= windowStart)
            .ToList();

        var totalReviewsCompleted = completedReviews.Count;
        var draftReviews = await db.ExpertReviewDrafts
            .AsNoTracking()
            .CountAsync(draft => draft.ReviewerId == reviewerId && (draft.State == null || draft.State != "submitted"), ct);

        var slaHitRate = totalReviewsCompleted == 0
            ? 100.0
            : Math.Round(completedReviews.Count(reviewRequest => (reviewRequest.CompletedAt ?? reviewRequest.CreatedAt) <= CalculateSlaDueAt(reviewRequest)) * 100.0 / totalReviewsCompleted, 1);

        var avgTurnaroundHours = totalReviewsCompleted == 0
            ? 0.0
            : Math.Round(completedReviews.Average(reviewRequest => ((reviewRequest.CompletedAt ?? reviewRequest.CreatedAt) - reviewRequest.CreatedAt).TotalHours), 1);

        var calibrationAlignment = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(result => result.ReviewerId == reviewerId)
            .Select(result => (double?)result.AlignmentScore)
            .AverageAsync(ct) ?? 100.0;

        var reworkCount = assignments.Count(assignment => !string.IsNullOrWhiteSpace(assignment.ReasonCode) && !string.Equals(assignment.ReasonCode, "submitted", StringComparison.OrdinalIgnoreCase));
        var reworkRate = handledReviewIds.Count == 0
            ? 0.0
            : Math.Round(reworkCount * 100.0 / handledReviewIds.Count, 1);

        var completionData = Enumerable.Range(0, clampedDays)
            .Select(offset =>
            {
                var day = windowStart.AddDays(offset);
                var count = completedReviews.Count(reviewRequest => (reviewRequest.CompletedAt ?? reviewRequest.CreatedAt).Date == day.Date);
                return new ExpertCompletionPointResponse(day.ToString("ddd", CultureInfo.InvariantCulture), count);
            })
            .ToList();

        return new ExpertMetricsResponse(
            new ExpertMetricsSummaryResponse(
                totalReviewsCompleted,
                draftReviews,
                slaHitRate,
                Math.Round(calibrationAlignment, 1),
                reworkRate,
                avgTurnaroundHours),
            completionData,
            clampedDays,
            DateTimeOffset.UtcNow);
    }

    private async Task SubmitReviewAsync(TrackedWriteContext context, string reviewerId, ExpertReviewSubmitRequest request, CancellationToken ct, string auditAction, string analyticsEvent)
    {
        ValidateSubmitRequest(context.ReviewRequest.SubtestCode, request);

        var draft = await db.ExpertReviewDrafts
            .FirstOrDefaultAsync(existingDraft => existingDraft.ReviewRequestId == context.ReviewRequest.Id && existingDraft.ReviewerId == reviewerId, ct);

        if (draft is null)
        {
            draft = new ExpertReviewDraft
            {
                Id = $"erd-{Guid.NewGuid():N}",
                ReviewRequestId = context.ReviewRequest.Id,
                ReviewerId = reviewerId,
                Version = 0
            };
            db.ExpertReviewDrafts.Add(draft);
        }

        if (request.Version is not null && request.Version != draft.Version)
        {
            throw ApiException.Conflict(
                "draft_version_conflict",
                "This draft has changed since you opened it.",
                [new ApiFieldError("version", "conflict", "Reload the review to merge the latest saved draft before submitting.")]);
        }

        draft.RubricEntriesJson = JsonSupport.Serialize(NormalizeScores(request.Scores, context.ReviewRequest.SubtestCode));
        draft.CriterionCommentsJson = JsonSupport.Serialize(NormalizeCriterionComments(request.CriterionComments, context.ReviewRequest.SubtestCode));
        draft.FinalCommentDraft = NormalizeFinalComment(request.FinalComment, required: true);
        draft.Version += 1;
        draft.State = "submitted";
        draft.DraftSavedAt = DateTimeOffset.UtcNow;

        context.ReviewRequest.State = ReviewRequestState.Completed;
        context.ReviewRequest.CompletedAt = DateTimeOffset.UtcNow;
        context.ActiveAssignment.ClaimState = ExpertAssignmentState.Released;
        context.ActiveAssignment.ReleasedAt = DateTimeOffset.UtcNow;
        context.ActiveAssignment.ReasonCode = "submitted";

        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, auditAction, context.ReviewRequest.Id, draft.FinalCommentDraft, ct);
        await RecordExpertEventAsync(reviewerId, analyticsEvent, new { reviewRequestId = context.ReviewRequest.Id, version = draft.Version }, ct);
        await db.SaveChangesAsync(ct);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReviewCompleted,
            context.Attempt.UserId,
            "review_request",
            context.ReviewRequest.Id,
            (context.ReviewRequest.CompletedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["attemptId"] = context.Attempt.Id,
                ["reviewRequestId"] = context.ReviewRequest.Id,
                ["subtest"] = context.ReviewRequest.SubtestCode,
                ["message"] = $"Your {context.ReviewRequest.SubtestCode} expert review is now ready."
            },
            ct);
    }

    private async Task<ExpertUser> EnsureExpertAsync(string reviewerId, CancellationToken ct)
    {
        var expert = await db.ExpertUsers.FirstOrDefaultAsync(existingExpert => existingExpert.Id == reviewerId, ct);
        if (expert is null)
        {
            throw ApiException.Forbidden("expert_profile_not_found", "Expert profile not found.");
        }

        if (!expert.IsActive)
        {
            throw ApiException.Forbidden("account_suspended", "This expert account is not available.");
        }

        return expert;
    }

    private async Task<ReadContext> LoadReadContextAsync(string reviewRequestId, string reviewerId, CancellationToken ct, bool requireActiveAssignment)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var reviewRequest = await db.ReviewRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(existingReviewRequest => existingReviewRequest.Id == reviewRequestId, ct)
            ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

        var attempt = await db.Attempts
            .AsNoTracking()
            .FirstOrDefaultAsync(existingAttempt => existingAttempt.Id == reviewRequest.AttemptId, ct)
            ?? throw ApiException.NotFound("attempt_not_found", "The attempt linked to this review could not be found.");

        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == attempt.UserId, ct);
        var content = await db.ContentItems.AsNoTracking().FirstOrDefaultAsync(item => item.Id == attempt.ContentId, ct);
        var evaluation = await FirstOrDefaultOrderedDescendingAsync(
            db.Evaluations
                .AsNoTracking()
                .Where(existingEvaluation => existingEvaluation.AttemptId == attempt.Id),
            existingEvaluation => existingEvaluation.GeneratedAt,
            ct);

        var assignments = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ReviewRequestId == reviewRequestId)
            .ToListAsync(ct);

        var activeAssignment = assignments
            .Where(assignment => assignment.ClaimState != ExpertAssignmentState.Released)
            .OrderByDescending(assignment => assignment.AssignedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        var hasHistoricalAccess = assignments.Any(assignment => string.Equals(assignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
            || await db.ExpertReviewDrafts.AsNoTracking().AnyAsync(draft => draft.ReviewRequestId == reviewRequestId && draft.ReviewerId == reviewerId, ct);

        if (requireActiveAssignment)
        {
            if (activeAssignment is null || !string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
            {
                throw ApiException.Forbidden("review_not_owned", "Claim this review before opening the workspace.");
            }
        }
        else if (activeAssignment is not null && !string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal) && !hasHistoricalAccess)
        {
            throw ApiException.Forbidden("review_not_visible", "This review is assigned to another reviewer.");
        }

        var assignedReviewerIds = assignments
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

        var draftEntity = await FirstOrDefaultOrderedDescendingAsync(
            db.ExpertReviewDrafts
                .AsNoTracking()
                .Where(existingDraft => existingDraft.ReviewRequestId == reviewRequestId && existingDraft.ReviewerId == reviewerId),
            existingDraft => existingDraft.DraftSavedAt,
            ct);

        return new ReadContext(reviewRequest, attempt, learner, activeAssignment, content, evaluation, BuildDraftResponse(draftEntity), assignedReviewers);
    }

    private async Task<TrackedWriteContext> LoadWriteContextAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        var reviewRequest = await db.ReviewRequests.FirstOrDefaultAsync(existingReviewRequest => existingReviewRequest.Id == reviewRequestId, ct)
            ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

        if (reviewRequest.State == ReviewRequestState.Completed || reviewRequest.State == ReviewRequestState.Cancelled)
        {
            throw ApiException.Conflict("review_not_editable", "Completed reviews cannot be modified.");
        }

        var activeAssignment = await GetActiveAssignmentAsync(reviewRequestId, tracked: true, ct);
        if (activeAssignment is null || !string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("review_not_owned", "You can only modify reviews currently assigned to you.");
        }

        var attempt = await db.Attempts.FirstOrDefaultAsync(existingAttempt => existingAttempt.Id == reviewRequest.AttemptId, ct)
            ?? throw ApiException.NotFound("attempt_not_found", "The attempt linked to this review could not be found.");

        return new TrackedWriteContext(reviewRequest, attempt, activeAssignment, expert);
    }

    private async Task<List<ReviewRequest>> LoadAccessibleLearnerReviewRequestsAsync(string learnerId, string reviewerId, CancellationToken ct)
    {
        var learnerAttemptIds = await db.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == learnerId)
            .Select(attempt => attempt.Id)
            .ToListAsync(ct);

        if (learnerAttemptIds.Count == 0)
        {
            return [];
        }

        var accessibleReviewIds = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(assignment => assignment.AssignedReviewerId == reviewerId)
            .Select(assignment => assignment.ReviewRequestId)
            .Distinct()
            .ToListAsync(ct);

        if (accessibleReviewIds.Count == 0)
        {
            return [];
        }

        return await ToOrderedListDescendingAsync(
            db.ReviewRequests
                .AsNoTracking()
                .Where(reviewRequest => learnerAttemptIds.Contains(reviewRequest.AttemptId) && accessibleReviewIds.Contains(reviewRequest.Id)),
            reviewRequest => reviewRequest.CompletedAt ?? reviewRequest.CreatedAt,
            ct);
    }

    private async Task<ExpertReviewAssignment?> GetActiveAssignmentAsync(string reviewRequestId, bool tracked, CancellationToken ct)
    {
        var query = db.ExpertReviewAssignments.Where(assignment => assignment.ReviewRequestId == reviewRequestId && assignment.ClaimState != ExpertAssignmentState.Released);
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(assignment => assignment.AssignedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(assignment => assignment.ReleasedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefaultAsync(ct);
        }

        return (await query.ToListAsync(ct))
            .OrderByDescending(assignment => assignment.AssignedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(assignment => assignment.ReleasedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private async Task<List<TItem>> ToOrderedListDescendingAsync<TItem, TKey>(
        IQueryable<TItem> query,
        Expression<Func<TItem, TKey>> orderBy,
        CancellationToken ct,
        int? take = null)
    {
        if (!db.Database.IsSqlite())
        {
            IQueryable<TItem> orderedQuery = query.OrderByDescending(orderBy);
            if (take is int takeCount)
            {
                orderedQuery = orderedQuery.Take(takeCount);
            }

            return await orderedQuery.ToListAsync(ct);
        }

        IEnumerable<TItem> orderedItems = (await query.ToListAsync(ct))
            .OrderByDescending(orderBy.Compile());

        if (take is int takeLimit)
        {
            orderedItems = orderedItems.Take(takeLimit);
        }

        return orderedItems.ToList();
    }

    private async Task<TItem?> FirstOrDefaultOrderedDescendingAsync<TItem, TKey>(
        IQueryable<TItem> query,
        Expression<Func<TItem, TKey>> orderBy,
        CancellationToken ct)
    {
        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(orderBy)
                .FirstOrDefaultAsync(ct);
        }

        return (await query.ToListAsync(ct))
            .OrderByDescending(orderBy.Compile())
            .FirstOrDefault();
    }

    private static List<ExpertQueueItemResponse> ApplyQueueFilters(List<ExpertQueueItemResponse> items, ExpertQueueQueryRequest request)
    {
        var filtered = items.AsEnumerable();
        var typeFilter = SplitCsv(request.Type);
        var professionFilter = SplitCsv(request.Profession);
        var priorityFilter = SplitCsv(request.Priority);
        var statusFilter = SplitCsv(request.Status);
        var confidenceFilter = SplitCsv(request.Confidence);
        var assignmentFilter = SplitCsv(request.Assignment);

        if (typeFilter.Count > 0)
        {
            filtered = filtered.Where(item => typeFilter.Contains(item.Type));
        }

        if (professionFilter.Count > 0)
        {
            filtered = filtered.Where(item => professionFilter.Contains(item.Profession));
        }

        if (priorityFilter.Count > 0)
        {
            filtered = filtered.Where(item => priorityFilter.Contains(item.Priority));
        }

        if (statusFilter.Count > 0)
        {
            filtered = filtered.Where(item => statusFilter.Contains(item.Status));
        }

        if (confidenceFilter.Count > 0)
        {
            filtered = filtered.Where(item => confidenceFilter.Contains(item.AiConfidence));
        }

        if (assignmentFilter.Count > 0)
        {
            filtered = filtered.Where(item =>
            {
                var isAssigned = !string.Equals(item.AssignmentState, "unassigned", StringComparison.OrdinalIgnoreCase);
                return (assignmentFilter.Contains("assigned") && isAssigned) || (assignmentFilter.Contains("unassigned") && !isAssigned);
            });
        }

        if (request.Overdue == true)
        {
            filtered = filtered.Where(item => item.IsOverdue);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var query = request.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(item => item.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.LearnerName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .OrderByDescending(item => item.IsOverdue)
            .ThenBy(item => item.SlaDue)
            .ThenBy(item => PriorityWeight(item.Priority))
            .ThenBy(item => item.CreatedAt)
            .ToList();
    }

    private static HashSet<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ExpertQueueItemResponse? BuildQueueItem(
        ReviewRequest reviewRequest,
        IReadOnlyDictionary<string, Attempt> attempts,
        IReadOnlyDictionary<string, LearnerUser> learners,
        IReadOnlyDictionary<string, ExpertReviewAssignment> activeAssignments,
        IReadOnlyDictionary<string, ExpertUser> reviewers,
        IReadOnlyDictionary<string, Evaluation> evaluations,
        string reviewerId,
        DateTimeOffset now)
    {
        if (!attempts.TryGetValue(reviewRequest.AttemptId, out var attempt))
        {
            return null;
        }

        activeAssignments.TryGetValue(reviewRequest.Id, out var activeAssignment);
        var isVisible = activeAssignment is null || string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal);
        if (!isVisible)
        {
            return null;
        }

        learners.TryGetValue(attempt.UserId, out var learner);
        evaluations.TryGetValue(attempt.Id, out var evaluation);
        var type = string.Equals(reviewRequest.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase) ? "speaking" : "writing";
        var assignedReviewerName = ResolveReviewerName(activeAssignment?.AssignedReviewerId, reviewers);
        var slaDue = CalculateSlaDueAt(reviewRequest);
        var isOverdue = IsOverdue(reviewRequest, now);

        return new ExpertQueueItemResponse(
            reviewRequest.Id,
            attempt.UserId,
            learner?.DisplayName ?? "Unknown learner",
            learner?.ActiveProfessionId ?? "nursing",
            reviewRequest.SubtestCode,
            type,
            ToAiConfidence(evaluation?.ConfidenceBand),
            MapPriority(reviewRequest.TurnaroundOption),
            slaDue,
            MapSlaState(reviewRequest, now),
            isOverdue,
            activeAssignment?.AssignedReviewerId,
            assignedReviewerName,
            MapAssignmentState(activeAssignment),
            MapQueueStatus(reviewRequest, activeAssignment, reviewerId, now),
            attempt.ContentId,
            attempt.Id,
            reviewRequest.CreatedAt,
            BuildPermissions(reviewRequest, activeAssignment, reviewerId));
    }

    private static ExpertReviewActionsResponse BuildPermissions(ReviewRequest reviewRequest, ExpertReviewAssignment? activeAssignment, string reviewerId)
    {
        var isOwnedByReviewer = activeAssignment is not null && string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal);
        var isClaimedByReviewer = isOwnedByReviewer && activeAssignment!.ClaimState == ExpertAssignmentState.Claimed;
        var isCompleted = reviewRequest.State == ReviewRequestState.Completed;
        var canClaim = !isCompleted && (activeAssignment is null || isOwnedByReviewer) && !isClaimedByReviewer;
        var canRelease = !isCompleted && isOwnedByReviewer;
        var canOpen = isOwnedByReviewer;
        var canMutate = isOwnedByReviewer && !isCompleted;

        return new ExpertReviewActionsResponse(
            canClaim,
            canRelease,
            canOpen,
            canMutate,
            canMutate,
            canMutate,
            isCompleted);
    }

    private static string MapQueueStatus(ReviewRequest reviewRequest, ExpertReviewAssignment? activeAssignment, string reviewerId, DateTimeOffset now)
    {
        if (IsOverdue(reviewRequest, now))
        {
            return "overdue";
        }

        if (reviewRequest.State == ReviewRequestState.Completed)
        {
            return "completed";
        }

        if (activeAssignment is not null && string.Equals(activeAssignment.AssignedReviewerId, reviewerId, StringComparison.Ordinal))
        {
            return activeAssignment.ClaimState == ExpertAssignmentState.Claimed ? "in_progress" : "assigned";
        }

        return reviewRequest.State switch
        {
            ReviewRequestState.InReview => "assigned",
            ReviewRequestState.Failed => "blocked",
            _ => "queued"
        };
    }

    private static string MapAssignmentState(ExpertReviewAssignment? activeAssignment)
    {
        if (activeAssignment is null)
        {
            return "unassigned";
        }

        return activeAssignment.ClaimState switch
        {
            ExpertAssignmentState.Assigned => "assigned",
            ExpertAssignmentState.Claimed => "claimed",
            ExpertAssignmentState.Reassigned => "reassigned",
            _ => "assigned"
        };
    }

    private static string MapPriority(string? turnaround)
    {
        return turnaround?.ToLowerInvariant() switch
        {
            "express" => "high",
            "standard" => "normal",
            _ => "normal"
        };
    }

    private static int PriorityWeight(string priority)
    {
        return priority.ToLowerInvariant() switch
        {
            "high" => 0,
            "normal" => 1,
            "low" => 2,
            _ => 3
        };
    }

    private static DateTimeOffset CalculateSlaDueAt(ReviewRequest reviewRequest)
        => reviewRequest.CreatedAt.AddHours(string.Equals(reviewRequest.TurnaroundOption, "express", StringComparison.OrdinalIgnoreCase) ? 24 : 48);

    private static bool IsOverdue(ReviewRequest reviewRequest, DateTimeOffset now)
        => reviewRequest.State != ReviewRequestState.Completed && CalculateSlaDueAt(reviewRequest) <= now;

    private static string MapSlaState(ReviewRequest reviewRequest, DateTimeOffset now)
    {
        var slaDue = CalculateSlaDueAt(reviewRequest);

        if (reviewRequest.State == ReviewRequestState.Completed)
        {
            return (reviewRequest.CompletedAt ?? now) <= slaDue ? "completed_on_time" : "completed_late";
        }

        if (slaDue <= now)
        {
            return "overdue";
        }

        return slaDue - now <= TimeSpan.FromHours(6) ? "at_risk" : "on_track";
    }

    private static string? ResolveReviewerName(string? reviewerId, IReadOnlyDictionary<string, ExpertUser> reviewers)
    {
        if (string.IsNullOrWhiteSpace(reviewerId))
        {
            return null;
        }

        return reviewers.TryGetValue(reviewerId, out var reviewer) ? reviewer.DisplayName : reviewerId;
    }

    private static string ToAiConfidence(ConfidenceBand? confidenceBand)
    {
        return confidenceBand?.ToString().ToLowerInvariant() ?? "unknown";
    }

    private static Dictionary<string, int> NormalizeAiSuggestedScores(Evaluation? evaluation, bool isWriting)
    {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var payload = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation?.CriterionScoresJson, []);
        foreach (var item in payload)
        {
            var criterionCode = NormalizeCriterionKey(item.TryGetValue("criterionCode", out var value) ? value?.ToString() : null, isWriting ? WritingCriteria : SpeakingCriteria);
            if (criterionCode is null)
            {
                continue;
            }

            normalized[criterionCode] = ParseScoreRangeAverage(item.TryGetValue("scoreRange", out var scoreRange) ? scoreRange?.ToString() : null) ?? 0;
        }

        if (normalized.Count > 0)
        {
            return normalized;
        }

        return isWriting
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["purpose"] = 4,
                ["content"] = 4,
                ["conciseness"] = 3,
                ["genre"] = 4,
                ["organization"] = 4,
                ["language"] = 4
            }
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["intelligibility"] = 4,
                ["fluency"] = 3,
                ["appropriateness"] = 4,
                ["grammar"] = 4,
                ["clinicalCommunication"] = 4
            };
    }

    private static ExpertArtifactStateResponse BuildEvaluationArtifactState(Evaluation? evaluation, string pendingMessage)
    {
        if (evaluation is null)
        {
            return new ExpertArtifactStateResponse("queued", false, pendingMessage);
        }

        var state = evaluation.State switch
        {
            AsyncState.Completed => "completed",
            AsyncState.Failed => "failed",
            AsyncState.Processing => "processing",
            _ => "queued"
        };

        return new ExpertArtifactStateResponse(state, false, state == "completed" ? null : pendingMessage);
    }

    private static ExpertArtifactStateResponse BuildTranscriptArtifactState(IReadOnlyList<ExpertTranscriptLineResponse> transcriptLines)
    {
        return transcriptLines.Count == 0
            ? new ExpertArtifactStateResponse("processing", false, "Transcript is still being processed.")
            : new ExpertArtifactStateResponse("completed", false, null);
    }

    private static ExpertArtifactStateResponse BuildAudioArtifactState(Attempt attempt)
    {
        if (!string.IsNullOrWhiteSpace(attempt.AudioObjectKey))
        {
            return new ExpertArtifactStateResponse("completed", false, null);
        }

        return attempt.AudioUploadState switch
        {
            UploadState.Failed => new ExpertArtifactStateResponse("failed", false, "The learner audio upload did not complete successfully."),
            UploadState.Pending => new ExpertArtifactStateResponse("queued", false, "Audio is not available yet."),
            _ => new ExpertArtifactStateResponse("processing", false, "Audio is still being finalized.")
        };
    }

    private static string BuildWritingAiDraftFeedback(Evaluation? evaluation)
    {
        if (evaluation is null)
        {
            return "AI feedback is still being prepared for this writing review.";
        }

        var feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, []);
        var messages = feedbackItems
            .Select(item => item.TryGetValue("message", out var message) ? message?.ToString() : null)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => $"- {message}")
            .ToList();

        if (messages.Count > 0)
        {
            return string.Join(Environment.NewLine, messages);
        }

        var strengths = JsonSupport.Deserialize(evaluation.StrengthsJson, Array.Empty<string>());
        var issues = JsonSupport.Deserialize(evaluation.IssuesJson, Array.Empty<string>());
        var sections = new List<string>();
        if (strengths.Length > 0)
        {
            sections.Add("Strengths:" + Environment.NewLine + string.Join(Environment.NewLine, strengths.Select(strength => $"- {strength}")));
        }

        if (issues.Length > 0)
        {
            sections.Add("Improvement areas:" + Environment.NewLine + string.Join(Environment.NewLine, issues.Select(issue => $"- {issue}")));
        }

        return sections.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, sections)
            : "AI feedback is available but did not contain reviewer-ready notes.";
    }

    private static string? ExtractModelAnswer(ContentItem? content)
    {
        if (content is null || string.IsNullOrWhiteSpace(content.ModelAnswerJson))
        {
            return null;
        }

        var payload = JsonSupport.Deserialize(content.ModelAnswerJson, new Dictionary<string, object?>());
        if (payload.TryGetValue("text", out var value) && value is not null)
        {
            return value.ToString();
        }

        return content.ModelAnswerJson;
    }

    private static ExpertSpeakingRoleCardResponse ExtractRoleCard(ContentItem? content)
    {
        var fallback = new ExpertSpeakingRoleCardResponse("Nurse", "Ward", "Patient", "Provide a clinical handover.", null);
        if (content is null)
        {
            return fallback;
        }

        var payload = JsonSupport.Deserialize(content.DetailJson, new Dictionary<string, object?>());
        return new ExpertSpeakingRoleCardResponse(
            payload.TryGetValue("role", out var role) ? role?.ToString() ?? fallback.Role : fallback.Role,
            payload.TryGetValue("setting", out var setting) ? setting?.ToString() ?? fallback.Setting : fallback.Setting,
            payload.TryGetValue("patient", out var patient) ? patient?.ToString() ?? fallback.Patient : fallback.Patient,
            payload.TryGetValue("task", out var task) ? task?.ToString() ?? fallback.Task : fallback.Task,
            payload.TryGetValue("background", out var background) ? background?.ToString() : fallback.Background);
    }

    private static List<ExpertTranscriptLineResponse> ExtractTranscriptLines(Attempt attempt)
    {
        var transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []);
        return transcript
            .Select((line, index) => new ExpertTranscriptLineResponse(
                line.TryGetValue("id", out var id) ? id?.ToString() ?? $"line-{index + 1}" : $"line-{index + 1}",
                line.TryGetValue("speaker", out var speaker) ? NormalizeSpeaker(speaker?.ToString()) : "candidate",
                ToDouble(line.TryGetValue("startTime", out var startTime) ? startTime : null),
                ToDouble(line.TryGetValue("endTime", out var endTime) ? endTime : null),
                line.TryGetValue("text", out var text) ? text?.ToString() ?? string.Empty : string.Empty))
            .OrderBy(line => line.StartTime)
            .ToList();
    }

    private static List<ExpertAiFlagResponse> ExtractAiFlags(Evaluation? evaluation)
    {
        var feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation?.FeedbackItemsJson, []);
        return feedbackItems.Select((item, index) =>
        {
            var anchor = item.TryGetValue("anchor", out var anchorValue)
                ? JsonSupport.Deserialize(JsonSupport.Serialize(anchorValue), new Dictionary<string, object?>())
                : new Dictionary<string, object?>();
            return new ExpertAiFlagResponse(
                item.TryGetValue("feedbackItemId", out var id) ? id?.ToString() ?? $"flag-{index + 1}" : $"flag-{index + 1}",
                NormalizeFlagType(item.TryGetValue("criterionCode", out var criterionCode) ? criterionCode?.ToString() : null),
                item.TryGetValue("message", out var message) ? message?.ToString() ?? "AI flagged this segment for review." : "AI flagged this segment for review.",
                ToDouble(anchor.TryGetValue("startTime", out var startTime) ? startTime : null),
                anchor.TryGetValue("endTime", out var endTime) ? ToNullableDouble(endTime) : null,
                NormalizeSeverity(item.TryGetValue("severity", out var severity) ? severity?.ToString() : null));
        }).ToList();
    }

    private static string NormalizeSpeaker(string? value)
    {
        return string.Equals(value, "interlocutor", StringComparison.OrdinalIgnoreCase) ? "interlocutor" : "candidate";
    }

    private static string NormalizeFlagType(string? value)
    {
        return NormalizeCriterionKey(value, SpeakingCriteria) ?? "review_flag";
    }

    private static string NormalizeSeverity(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "error" => "error",
            "warning" => "warning",
            _ => "info"
        };
    }

    private static string ToLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Criterion";
        }

        var withSpaces = System.Text.RegularExpressions.Regex.Replace(
            value.Replace("_", " ", StringComparison.Ordinal),
            "([a-z])([A-Z])",
            "$1 $2");

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(withSpaces.ToLowerInvariant());
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0,
            JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.Number => element.GetDouble(),
            JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            double number => number,
            float number => number,
            decimal number => (double)number,
            int number => number,
            long number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static double? ToNullableDouble(object? value)
    {
        return value is null ? null : ToDouble(value);
    }

    private static ExpertDraftResponse? BuildDraftResponse(ExpertReviewDraft? draft)
    {
        if (draft is null)
        {
            return null;
        }

        return new ExpertDraftResponse(
            draft.Version,
            draft.State,
            JsonSupport.Deserialize(draft.RubricEntriesJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
            JsonSupport.Deserialize(draft.CriterionCommentsJson, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            draft.FinalCommentDraft,
            JsonSupport.Deserialize(draft.AnchoredCommentsJson, new List<ExpertAnchoredCommentResponse>()),
            JsonSupport.Deserialize(draft.TimestampCommentsJson, new List<ExpertTimestampCommentResponse>()),
            JsonSupport.Deserialize(draft.ScratchpadJson, string.Empty),
            JsonSupport.Deserialize(draft.ChecklistItemsJson, new List<ExpertChecklistItemResponse>()),
            draft.DraftSavedAt);
    }

    private static string ResolveTodayKey(string timezone)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DayOfWeek.ToString().ToLowerInvariant();
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTimeOffset.UtcNow.DayOfWeek.ToString().ToLowerInvariant();
        }
        catch (InvalidTimeZoneException)
        {
            return DateTimeOffset.UtcNow.DayOfWeek.ToString().ToLowerInvariant();
        }
    }

    private static string? BuildReviewRoute(string reviewRequestId)
    {
        if (string.IsNullOrWhiteSpace(reviewRequestId))
        {
            return null;
        }

        if (reviewRequestId.StartsWith("cal-", StringComparison.OrdinalIgnoreCase))
        {
            return $"/expert/calibration/{Uri.EscapeDataString(reviewRequestId)}";
        }

        if (reviewRequestId.StartsWith("review-", StringComparison.OrdinalIgnoreCase))
        {
            return "/expert/queue";
        }

        return null;
    }

    private static string MapReviewRequestState(ReviewRequest reviewRequest, ExpertReviewAssignment? assignment, string reviewerId, DateTimeOffset now)
        => MapQueueStatus(reviewRequest, assignment, reviewerId, now);

    private static List<ExpertCalibrationArtifactResponse> DeserializeCalibrationArtifacts(ExpertCalibrationCase calibrationCase)
    {
        var artifacts = JsonSupport.Deserialize<List<ExpertCalibrationArtifactResponse>>(calibrationCase.CaseArtifactsJson, []);
        if (artifacts.Count > 0)
        {
            return artifacts;
        }

        return string.Equals(calibrationCase.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            ? new List<ExpertCalibrationArtifactResponse>
            {
                new("role_card", "Role Card", "You are the ward doctor handing over a patient with post-operative pain escalation and new abnormal observations."),
                new("transcript", "Candidate Transcript", "Doctor, I am calling about a patient whose pain has worsened despite the current analgesia plan. We need to review the escalation steps and safety-net advice."),
                new("benchmark_focus", "Benchmark Focus", "Benchmark case tests structured handover, prioritisation, and safe escalation language under time pressure.")
            }
            : new List<ExpertCalibrationArtifactResponse>
            {
                new("case_notes", "Case Notes", "Mrs Khan requires a referral following post-operative complications after laparoscopic cholecystectomy. Include wound concerns, analgesia response, and follow-up plan."),
                new("learner_response", "Learner Response", "Dear Dr Patel, thank you for seeing Mrs Khan, who has persistent abdominal pain, mild wound ooze, and difficulty mobilising after surgery."),
                new("benchmark_focus", "Benchmark Focus", "Benchmark case tests clear purpose, clinical relevance filtering, and concise sequencing of referral information.")
            };
    }

    private static List<ExpertCalibrationRubricEntryResponse> DeserializeCalibrationRubric(ExpertCalibrationCase calibrationCase)
    {
        var rubric = JsonSupport.Deserialize<List<ExpertCalibrationRubricEntryResponse>>(calibrationCase.ReferenceRubricJson, []);
        if (rubric.Count > 0)
        {
            return rubric;
        }

        return string.Equals(calibrationCase.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            ? new List<ExpertCalibrationRubricEntryResponse>
            {
                new("intelligibility", 5, "Speech remains easy to follow with only minor stress-related hesitation."),
                new("fluency", 5, "Delivery is steady and recovers quickly after clarification moments."),
                new("appropriateness", 4, "Register is professional but one reassurance phrase is slightly abrupt."),
                new("grammar", 5, "Grammar is controlled throughout the handover."),
                new("clinicalCommunication", 5, "Escalation, prioritisation, and safety-netting are explicit and well organised.")
            }
            : new List<ExpertCalibrationRubricEntryResponse>
            {
                new("purpose", 4, "Purpose is established immediately and sustained throughout the letter."),
                new("content", 5, "Relevant post-operative facts are selected accurately for referral."),
                new("conciseness", 3, "A few low-value details reduce efficiency."),
                new("genre", 4, "Register and format match a professional referral letter."),
                new("organization", 4, "Information flows logically from reason for referral to current concerns."),
                new("language", 4, "Language is mostly controlled with minor slips that do not impede meaning.")
            };
    }

    private static List<string> DeserializeCalibrationReferenceNotes(ExpertCalibrationCase calibrationCase)
    {
        var notes = JsonSupport.Deserialize<List<string>>(calibrationCase.ReferenceNotesJson, []);
        if (notes.Count > 0)
        {
            return notes;
        }

        return string.Equals(calibrationCase.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            ? new List<string>
            {
                "Benchmark expects a concise opening summary before detailed escalation points.",
                "Full marks require explicit clinical prioritisation and a clear follow-up request.",
                "Minor alignment loss is acceptable when reassurance language is warm but slightly repetitive."
            }
            : new List<string>
            {
                "Benchmark prioritises referral purpose, current complication, and follow-up request in the opening half of the letter.",
                "Low-value surgical background should be compressed unless it changes the referral decision.",
                "Language control is important, but information selection remains the main separator in this case."
            };
    }

    private static Dictionary<string, int> NormalizeScores(Dictionary<string, int> scores, string subtestCode)
    {
        var criteria = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? WritingCriteria : SpeakingCriteria;
        var maxScore = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? 7 : 6;
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in scores)
        {
            var normalizedKey = NormalizeCriterionKey(key, criteria);
            if (normalizedKey is null)
            {
                throw ApiException.Validation(
                    "invalid_rubric_criterion",
                    "One or more rubric criteria are invalid.",
                    [new ApiFieldError("scores", "invalid_criterion", $"'{key}' is not a valid criterion for this review.")]);
            }

            if (value < 0 || value > maxScore)
            {
                throw ApiException.Validation(
                    "invalid_rubric_score",
                    "One or more rubric scores are outside the allowed range.",
                    [new ApiFieldError($"scores.{normalizedKey}", "out_of_range", $"Scores for {normalizedKey} must be between 0 and {maxScore}.")]);
            }

            normalized[normalizedKey] = value;
        }

        return normalized;
    }

    private static Dictionary<string, string> NormalizeCriterionComments(Dictionary<string, string> criterionComments, string subtestCode)
    {
        var criteria = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? WritingCriteria : SpeakingCriteria;
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in criterionComments)
        {
            var normalizedKey = NormalizeCriterionKey(key, criteria);
            if (normalizedKey is null)
            {
                throw ApiException.Validation(
                    "invalid_rubric_comment_criterion",
                    "One or more rubric comment criteria are invalid.",
                    [new ApiFieldError("criterionComments", "invalid_criterion", $"'{key}' is not a valid criterion for this review.")]);
            }

            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length > MaxCriterionCommentLength)
            {
                throw ApiException.Validation(
                    "criterion_comment_too_long",
                    "One or more criterion comments are too long.",
                    [new ApiFieldError($"criterionComments.{normalizedKey}", "too_long", $"Criterion comments cannot exceed {MaxCriterionCommentLength} characters.")]);
            }

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                normalized[normalizedKey] = trimmed;
            }
        }

        return normalized;
    }

    private static string NormalizeFinalComment(string? finalComment, bool required)
    {
        var trimmed = finalComment?.Trim() ?? string.Empty;
        if (required && string.IsNullOrWhiteSpace(trimmed))
        {
            throw ApiException.Validation(
                "final_comment_required",
                "Provide a final overall comment before submitting.",
                [new ApiFieldError("finalComment", "required", "Add a final overall comment before submitting this review.")]);
        }

        if (trimmed.Length > MaxFinalCommentLength)
        {
            throw ApiException.Validation(
                "final_comment_too_long",
                "The final overall comment is too long.",
                [new ApiFieldError("finalComment", "too_long", $"Final comments cannot exceed {MaxFinalCommentLength} characters.")]);
        }

        return trimmed;
    }

    private static List<ExpertAnchoredCommentResponse> NormalizeAnchoredComments(List<ExpertAnchoredCommentDto>? comments)
    {
        if (comments is null)
        {
            return [];
        }

        return comments.Select(comment =>
        {
            var text = (comment.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw ApiException.Validation(
                    "anchored_comment_required",
                    "Anchored comments cannot be empty.",
                    [new ApiFieldError("anchoredComments", "required", "Provide comment text for each anchored comment.")]);
            }

            if (text.Length > MaxCommentTextLength)
            {
                throw ApiException.Validation(
                    "anchored_comment_too_long",
                    "Anchored comments are too long.",
                    [new ApiFieldError("anchoredComments", "too_long", $"Anchored comments cannot exceed {MaxCommentTextLength} characters.")]);
            }

            if (comment.StartOffset < 0 || comment.EndOffset <= comment.StartOffset)
            {
                throw ApiException.Validation(
                    "anchored_comment_offset_invalid",
                    "Anchored comment offsets are invalid.",
                    [new ApiFieldError("anchoredComments", "invalid_offset", "Anchored comment ranges must end after they begin.")]);
            }

            return new ExpertAnchoredCommentResponse(
                string.IsNullOrWhiteSpace(comment.Id) ? $"ac-{Guid.NewGuid():N}" : comment.Id,
                comment.Criterion,
                text,
                comment.StartOffset,
                comment.EndOffset,
                ParseCreatedAt(comment.CreatedAt));
        }).ToList();
    }

    private static List<ExpertTimestampCommentResponse> NormalizeTimestampComments(List<ExpertTimestampCommentDto>? comments)
    {
        if (comments is null)
        {
            return [];
        }

        return comments.Select(comment =>
        {
            var text = (comment.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw ApiException.Validation(
                    "timestamp_comment_required",
                    "Timestamp comments cannot be empty.",
                    [new ApiFieldError("timestampComments", "required", "Provide comment text for each timestamp comment.")]);
            }

            if (text.Length > MaxCommentTextLength)
            {
                throw ApiException.Validation(
                    "timestamp_comment_too_long",
                    "Timestamp comments are too long.",
                    [new ApiFieldError("timestampComments", "too_long", $"Timestamp comments cannot exceed {MaxCommentTextLength} characters.")]);
            }

            if (comment.TimestampStart < 0 || (comment.TimestampEnd is not null && comment.TimestampEnd <= comment.TimestampStart))
            {
                throw ApiException.Validation(
                    "timestamp_comment_range_invalid",
                    "Timestamp comment ranges are invalid.",
                    [new ApiFieldError("timestampComments", "invalid_range", "Timestamp comment ranges must end after they begin.")]);
            }

            return new ExpertTimestampCommentResponse(
                string.IsNullOrWhiteSpace(comment.Id) ? $"tc-{Guid.NewGuid():N}" : comment.Id,
                comment.Criterion,
                text,
                comment.TimestampStart,
                comment.TimestampEnd,
                ParseCreatedAt(comment.CreatedAt));
        }).ToList();
    }

    private static DateTimeOffset ParseCreatedAt(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static void ValidateDraftRequest(string subtestCode, ExpertDraftSaveRequest request)
    {
        _ = NormalizeScores(request.Scores, subtestCode);
        _ = NormalizeCriterionComments(request.CriterionComments, subtestCode);
        _ = NormalizeFinalComment(request.FinalComment, required: false);
        _ = NormalizeAnchoredComments(request.AnchoredComments);
        _ = NormalizeTimestampComments(request.TimestampComments);
        _ = NormalizeScratchpad(request.Scratchpad);
        _ = NormalizeChecklistItems(request.ChecklistItems);
    }

    private static void ValidateSubmitRequest(string subtestCode, ExpertReviewSubmitRequest request)
    {
        var normalizedScores = NormalizeScores(request.Scores, subtestCode);
        _ = NormalizeCriterionComments(request.CriterionComments, subtestCode);
        _ = NormalizeFinalComment(request.FinalComment, required: true);

        var requiredCriteria = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? WritingCriteria : SpeakingCriteria;
        var missing = requiredCriteria.Where(criterion => !normalizedScores.ContainsKey(criterion)).ToArray();
        if (missing.Length > 0)
        {
            throw ApiException.Validation(
                "rubric_incomplete",
                "Complete every required rubric score before submitting.",
                missing.Select(criterion => new ApiFieldError($"scores.{criterion}", "required", $"A score for {criterion} is required before submission.")));
        }
    }

    private static void ValidateAvailabilityRequest(ExpertAvailabilityUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Timezone) || request.Timezone.Length > 64)
        {
            throw ApiException.Validation(
                "timezone_invalid",
                "Provide a valid timezone identifier.",
                [new ApiFieldError("timezone", "invalid", "Timezone is required and must be shorter than 64 characters.")]);
        }

        var requiredDays = new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
        foreach (var day in requiredDays)
        {
            if (!request.Days.TryGetValue(day, out var scheduleDay))
            {
                throw ApiException.Validation(
                    "schedule_day_missing",
                    "Every day of the week must be present in the schedule payload.",
                    [new ApiFieldError($"days.{day}", "required", $"Add schedule data for {day}.")]);
            }

            if (!TimeOnly.TryParseExact(scheduleDay.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !TimeOnly.TryParseExact(scheduleDay.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                throw ApiException.Validation(
                    "schedule_time_invalid",
                    "Schedule times must use HH:mm format.",
                    [new ApiFieldError($"days.{day}", "invalid_time", $"Use HH:mm for the {day} schedule.")]);
            }

            if (scheduleDay.Active && end <= start)
            {
                throw ApiException.Validation(
                    "schedule_range_invalid",
                    "Availability end times must be later than start times.",
                    [new ApiFieldError($"days.{day}", "invalid_range", $"End time must be later than start time for {day}.")]);
            }
        }
    }

    private static string NormalizeReworkReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw ApiException.Validation(
                "rework_reason_required",
                "Provide a reason before requesting rework.",
                [new ApiFieldError("reason", "required", "Add a reason for the rework request.")]);
        }

        if (trimmed.Length > MaxReworkReasonLength)
        {
            throw ApiException.Validation(
                "rework_reason_too_long",
                "The rework reason is too long.",
                [new ApiFieldError("reason", "too_long", $"Rework reasons cannot exceed {MaxReworkReasonLength} characters.")]);
        }

        return trimmed;
    }

    private static string NormalizeScratchpad(string? scratchpad)
    {
        var normalized = scratchpad?.Trim() ?? string.Empty;
        if (normalized.Length > MaxScratchpadLength)
        {
            throw ApiException.Validation(
                "scratchpad_too_long",
                "Scratchpad notes are too long.",
                [new ApiFieldError("scratchpad", "too_long", $"Scratchpad notes cannot exceed {MaxScratchpadLength} characters.")]);
        }

        return normalized;
    }

    private static List<ExpertChecklistItemResponse> NormalizeChecklistItems(List<ExpertChecklistItemDto>? checklistItems)
    {
        if (checklistItems is null)
        {
            return [];
        }

        if (checklistItems.Count > MaxChecklistItemCount)
        {
            throw ApiException.Validation(
                "checklist_too_long",
                "Too many checklist items were submitted.",
                [new ApiFieldError("checklistItems", "too_many", $"Checklist items cannot exceed {MaxChecklistItemCount} entries.")]);
        }

        return checklistItems.Select(item =>
        {
            var id = item.Id?.Trim() ?? string.Empty;
            var label = item.Label?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(id))
            {
                throw ApiException.Validation(
                    "checklist_item_id_required",
                    "Checklist items must include an id.",
                    [new ApiFieldError("checklistItems", "required", "Every checklist item must include an id.")]);
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                throw ApiException.Validation(
                    "checklist_item_label_required",
                    "Checklist items must include a label.",
                    [new ApiFieldError("checklistItems", "required", "Every checklist item must include a label.")]);
            }

            if (label.Length > MaxChecklistItemLabelLength)
            {
                throw ApiException.Validation(
                    "checklist_item_label_too_long",
                    "Checklist item labels are too long.",
                    [new ApiFieldError("checklistItems", "too_long", $"Checklist labels cannot exceed {MaxChecklistItemLabelLength} characters.")]);
            }

            return new ExpertChecklistItemResponse(id, label, item.Checked);
        }).ToList();
    }

    private static string? NormalizeCriterionKey(string? rawKey, IReadOnlyCollection<string> allowedCriteria)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        var normalized = rawKey.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

        return allowedCriteria.FirstOrDefault(candidate =>
        {
            var candidateKey = candidate.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            return candidateKey == normalized
                || (candidate == "grammar" && normalized == "grammarexpression")
                || (candidate == "clinicalCommunication" && normalized == "clinicalcommunicationskills");
        });
    }

    private static int? ParseScoreRangeAverage(string? scoreRange)
    {
        if (string.IsNullOrWhiteSpace(scoreRange))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(scoreRange, "(\\d+)(?:-(\\d+))?");
        if (!match.Success)
        {
            return null;
        }

        var first = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var second = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : first;
        return (int)Math.Round((first + second) / 2.0, MidpointRounding.AwayFromZero);
    }

    private async Task LogExpertAuditAsync(string actorId, string actorName, string action, string? resourceId, string? details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"aud-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = "ExpertReview",
            ResourceId = resourceId,
            Details = details
        });

        await Task.CompletedTask;
    }

    private async Task RecordExpertEventAsync(string reviewerId, string eventName, object payload, CancellationToken ct)
    {
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = reviewerId,
            EventName = eventName,
            PayloadJson = JsonSupport.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow
        });

        await Task.CompletedTask;
    }

    private static Dictionary<string, ExpertScheduleDayDto> DefaultScheduleDays()
    {
        return new Dictionary<string, ExpertScheduleDayDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["monday"] = new(true, "09:00", "17:00"),
            ["tuesday"] = new(true, "09:00", "17:00"),
            ["wednesday"] = new(true, "09:00", "17:00"),
            ["thursday"] = new(true, "09:00", "17:00"),
            ["friday"] = new(true, "09:00", "16:00"),
            ["saturday"] = new(false, "09:00", "12:00"),
            ["sunday"] = new(false, "09:00", "12:00")
        };
    }

    private sealed record ReadContext(
        ReviewRequest ReviewRequest,
        Attempt Attempt,
        LearnerUser? Learner,
        ExpertReviewAssignment? ActiveAssignment,
        ContentItem? Content,
        Evaluation? Evaluation,
        ExpertDraftResponse? Draft,
        IReadOnlyDictionary<string, ExpertUser> AssignedReviewers);

    private sealed record TrackedWriteContext(
        ReviewRequest ReviewRequest,
        Attempt Attempt,
        ExpertReviewAssignment ActiveAssignment,
        ExpertUser Expert);
}
