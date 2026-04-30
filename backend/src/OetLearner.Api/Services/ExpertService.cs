using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

public class ExpertService(LearnerDbContext db, ILogger<ExpertService> logger, MediaStorageService mediaStorage, PlatformLinkService platformLinks, NotificationService notifications, PronunciationService pronunciationService)
{
    // Canonical writing criterion codes (match rulebooks/writing/common/assessment-criteria.json).
    // Purpose is scored 0\u20133; all others 0\u20137 (rulebook R16.1 / R16.2). British spelling is intentional.
    private static readonly string[] WritingCriteria = ["purpose", "content", "conciseness_clarity", "genre_style", "organisation_layout", "language"];

    // Canonical OET Speaking 9-criterion codes per official CBLA format
    // (source: rulebooks/speaking/common/assessment-criteria.json; Dr. Ahmed Hesham corrections April 2026).
    //   Linguistic (4, scale 0\u20136 each):
    //     intelligibility, fluency, appropriateness, grammar (Resources of Grammar & Expression)
    //   Clinical Communication (5, scale 0\u20133 each):
    //     relationshipBuilding, patientPerspective, providingStructure,
    //     informationGathering, informationGiving
    // The legacy aggregate "clinicalCommunication" key is DEPRECATED; it is not accepted on new writes.
    private static readonly string[] SpeakingCriteria = [
        "intelligibility", "fluency", "appropriateness", "grammar",
        "relationshipBuilding", "patientPerspective", "providingStructure",
        "informationGathering", "informationGiving"
    ];
    private static readonly string[] SpeakingLinguisticCriteria = ["intelligibility", "fluency", "appropriateness", "grammar"];
    private static readonly string[] SpeakingClinicalCriteria = ["relationshipBuilding", "patientPerspective", "providingStructure", "informationGathering", "informationGiving"];
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

        // Wave 7 of docs/SPEAKING-MODULE-PLAN.md - any non-owner access
        // to a speaking recording is a privacy-sensitive event and must
        // be auditable. We write the audit row before opening the file
        // stream so the side-effect is durable even if the client
        // disconnects mid-stream.
        var actorName = context.AssignedReviewers.TryGetValue(reviewerId, out var assignedExpert)
            ? assignedExpert.DisplayName
            : (await db.ExpertUsers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == reviewerId, ct))?.DisplayName
              ?? reviewerId;
        await LogExpertAuditAsync(
            reviewerId,
            actorName,
            "speaking_recording_accessed",
            reviewRequestId,
            $"Tutor streamed learner speaking audio for attempt {context.Attempt.Id}.",
            ct);
        await db.SaveChangesAsync(ct);

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

        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, "Saved Review Draft", reviewRequestId, "Tutor review draft saved.", ct);
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

        // ── L8: Bridge speaking review → pronunciation assessment ──
        try
        {
            var criterionScores = new Dictionary<string, object?>();
            if (request.Scores is not null)
            {
                foreach (var score in request.Scores)
                    criterionScores[score.Key] = (double)score.Value;
            }
            await pronunciationService.CreateFromSpeakingReviewAsync(
                context.Attempt.UserId, context.Attempt.Id, criterionScores, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Non-critical: failed to create pronunciation assessment from speaking review {ReviewRequestId}", reviewRequestId);
        }

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
            var status = result is null
                ? "pending"
                : result.IsDraft ? "draft" : "completed";
            var alignmentScore = result is null || result.IsDraft
                ? null
                : (double?)ResolveCalibrationAlignment(calibrationCase, result);
            return new ExpertCalibrationCaseSummaryResponse(
                calibrationCase.Id,
                calibrationCase.Title,
                calibrationCase.ProfessionId,
                calibrationCase.SubtestCode,
                calibrationCase.SubtestCode,
                calibrationCase.BenchmarkScore,
                result is null || result.IsDraft ? null : result.ReviewerScore,
                alignmentScore,
                status,
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
            existingSubmission is not null ? (existingSubmission.IsDraft ? "draft" : "completed") : "pending",
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
                    existingSubmission.IsDraft ? 0 : ResolveCalibrationAlignment(calibrationCase, existingSubmission),
                    existingSubmission.DisagreementSummary,
                    existingSubmission.Notes,
                    JsonSupport.Deserialize(existingSubmission.SubmittedRubricJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
                    existingSubmission.SubmittedAt,
                    existingSubmission.IsDraft,
                    existingSubmission.UpdatedAt));
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
        if (existingResult is not null && !existingResult.IsDraft)
        {
            throw ApiException.Conflict("calibration_already_submitted", "This calibration case has already been submitted.");
        }

        var normalizedScores = NormalizeScores(request.Scores, calibrationCase.SubtestCode);
        var benchmarkLookup = NormalizeCalibrationBenchmarkScores(
            DeserializeCalibrationRubric(calibrationCase),
            calibrationCase.SubtestCode);
        ValidateCompleteCalibrationScores(normalizedScores, benchmarkLookup);

        var reviewerScore = CalculateCalibrationReviewerScore(normalizedScores);
        var alignment = CalculateCalibrationAlignment(normalizedScores, benchmarkLookup, calibrationCase.SubtestCode);

        var largestDelta = benchmarkLookup.Keys
            .Select(criterion => new
            {
                Criterion = criterion,
                Gap = Math.Abs(normalizedScores[criterion] - benchmarkLookup[criterion]),
                NormalizedGap = Math.Abs(normalizedScores[criterion] - benchmarkLookup[criterion]) /
                    Math.Max(1.0, MaxScoreForCriterion(calibrationCase.SubtestCode, criterion))
            })
            .OrderByDescending(item => item.NormalizedGap)
            .FirstOrDefault();

        var disagreementSummary = largestDelta is null || largestDelta.Gap == 0
            ? "Aligned with benchmark."
            : $"{ToLabel(largestDelta.Criterion)} differs from benchmark by {largestDelta.Gap} point(s).";

        if (existingResult is null)
        {
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
                SubmittedAt = DateTimeOffset.UtcNow,
                IsDraft = false,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            // Upgrade an existing draft into a final submission.
            existingResult.SubmittedRubricJson = JsonSupport.Serialize(normalizedScores);
            existingResult.ReviewerScore = reviewerScore;
            existingResult.AlignmentScore = alignment;
            existingResult.DisagreementSummary = disagreementSummary;
            existingResult.Notes = request.Notes?.Trim() ?? string.Empty;
            existingResult.SubmittedAt = DateTimeOffset.UtcNow;
            existingResult.UpdatedAt = DateTimeOffset.UtcNow;
            existingResult.IsDraft = false;
        }

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

    /// <summary>
    /// Saves a calibration submission as a draft so the reviewer can resume later without losing work.
    /// Supplement §4.8: preserves reviewer work where possible. Drafts never contribute to
    /// alignment/history aggregates and are replaced (not duplicated) on subsequent saves.
    /// Supplement: <c>POST /v1/expert/calibration/cases/{caseId}/draft</c>.
    /// </summary>
    public async Task<object> SaveCalibrationDraftAsync(string caseId, string reviewerId, ExpertCalibrationSubmitRequest request, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Trim().Length > MaxCalibrationNotesLength)
        {
            throw ApiException.Validation(
                "calibration_notes_too_long",
                "Calibration notes are too long.",
                [new ApiFieldError("notes", "too_long", $"Calibration notes cannot exceed {MaxCalibrationNotesLength} characters.")]);
        }

        var calibrationCase = await db.ExpertCalibrationCases
            .FirstOrDefaultAsync(existingCase => existingCase.Id == caseId, ct)
            ?? throw ApiException.NotFound("calibration_case_not_found", "The requested calibration case does not exist.");

        var existing = await db.ExpertCalibrationResults
            .FirstOrDefaultAsync(result => result.CalibrationCaseId == caseId && result.ReviewerId == reviewerId, ct);
        if (existing is not null && !existing.IsDraft)
        {
            throw ApiException.Conflict(
                "calibration_already_submitted",
                "This calibration case has already been submitted and cannot be saved as a draft.");
        }

        var normalizedScores = request.Scores.Count == 0
            ? new Dictionary<string, int>()
            : NormalizeScores(request.Scores, calibrationCase.SubtestCode);

        var reviewerScore = normalizedScores.Count == 0
            ? 0
            : (int)Math.Round(normalizedScores.Values.Average(), MidpointRounding.AwayFromZero);

        var normalizedNotes = request.Notes?.Trim() ?? string.Empty;
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            existing = new ExpertCalibrationResult
            {
                Id = $"ecr-{Guid.NewGuid():N}",
                CalibrationCaseId = caseId,
                ReviewerId = reviewerId,
                SubmittedAt = now
            };
            db.ExpertCalibrationResults.Add(existing);
        }

        existing.SubmittedRubricJson = JsonSupport.Serialize(normalizedScores);
        existing.ReviewerScore = reviewerScore;
        existing.AlignmentScore = 0;
        existing.DisagreementSummary = string.Empty;
        existing.Notes = normalizedNotes;
        existing.IsDraft = true;
        existing.UpdatedAt = now;

        await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Saved Calibration Draft", caseId, "Calibration draft saved.", ct);
        await RecordExpertEventAsync(reviewerId, "expert_calibration_draft_saved", new { caseId, scoreCount = normalizedScores.Count }, ct);
        await db.SaveChangesAsync(ct);

        return new
        {
            success = true,
            caseId,
            isDraft = true,
            scores = normalizedScores,
            notes = normalizedNotes,
            updatedAt = existing.UpdatedAt
        };
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

    /// <summary>
    /// Returns static business rules for reviewer availability so the Schedule page can
    /// validate user input client-side without the server silently rejecting edits.
    /// Supplement: <c>GET /v1/expert/availability/constraints</c>.
    /// </summary>
    public async Task<ExpertAvailabilityConstraintsResponse> GetAvailabilityConstraintsAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        // Intentionally static; elevate to admin-configurable later if business rules change.
        return new ExpertAvailabilityConstraintsResponse(
            MinNoticeHours: 24,
            MaxHoursPerWeek: 60,
            MaxExceptionsPerMonth: 12,
            MinSlotDuration: "00:30",
            MaxSlotDuration: "12:00",
            SupportedTimezones: new[]
            {
                "UTC",
                "Europe/London",
                "Europe/Dublin",
                "Europe/Berlin",
                "America/New_York",
                "America/Chicago",
                "America/Denver",
                "America/Los_Angeles",
                "Asia/Dubai",
                "Asia/Karachi",
                "Asia/Kolkata",
                "Asia/Singapore",
                "Asia/Tokyo",
                "Australia/Sydney",
                "Pacific/Auckland"
            },
            DayKeys: new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" });
    }

    /// <summary>
    /// Returns the reviewer's calibration submission history ordered newest first.
    /// Supplement: <c>GET /v1/expert/calibration/history</c>.
    /// </summary>
    public async Task<ExpertCalibrationHistoryResponse> GetCalibrationHistoryAsync(string reviewerId, int limit, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);
        var effectiveLimit = Math.Clamp(limit, 1, 200);

        var results = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(r => r.ReviewerId == reviewerId && !r.IsDraft)
            .OrderByDescending(r => r.SubmittedAt)
            .Take(effectiveLimit)
            .ToListAsync(ct);

        var total = await db.ExpertCalibrationResults
            .AsNoTracking()
            .CountAsync(r => r.ReviewerId == reviewerId && !r.IsDraft, ct);

        if (results.Count == 0)
        {
            return new ExpertCalibrationHistoryResponse(Array.Empty<ExpertCalibrationHistoryEntryResponse>(), 0, DateTimeOffset.UtcNow);
        }

        var caseIds = results.Select(r => r.CalibrationCaseId).Distinct().ToList();
        var cases = await db.ExpertCalibrationCases
            .AsNoTracking()
            .Where(c => caseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var professionIds = cases.Values.Select(c => c.ProfessionId).Distinct().ToList();
        var professionNames = await db.Professions
            .AsNoTracking()
            .Where(p => professionIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Label, ct);

        var entries = results.Select(r =>
        {
            cases.TryGetValue(r.CalibrationCaseId, out var @case);
            var professionName = @case is not null && professionNames.TryGetValue(@case.ProfessionId, out var pn)
                ? pn
                : @case?.ProfessionId ?? string.Empty;

            return new ExpertCalibrationHistoryEntryResponse(
                Id: r.Id,
                CaseId: r.CalibrationCaseId,
                CaseTitle: @case?.Title ?? "(deleted case)",
                Profession: professionName,
                SubTest: @case?.SubtestCode ?? string.Empty,
                BenchmarkScore: @case?.BenchmarkScore ?? 0,
                ReviewerScore: r.ReviewerScore,
                AlignmentScore: @case is null ? r.AlignmentScore : ResolveCalibrationAlignment(@case, r),
                DisagreementSummary: r.DisagreementSummary ?? string.Empty,
                SubmittedAt: r.SubmittedAt);
        }).ToList();

        return new ExpertCalibrationHistoryResponse(entries, total, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Returns aggregate alignment statistics across the reviewer's calibration submissions,
    /// plus per-sub-test breakdown and a 12-point trend. Supplement:
    /// <c>GET /v1/expert/calibration/alignment</c>.
    /// </summary>
    public async Task<ExpertCalibrationAlignmentResponse> GetCalibrationAlignmentAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var results = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(r => r.ReviewerId == reviewerId && !r.IsDraft)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);

        if (results.Count == 0)
        {
            return new ExpertCalibrationAlignmentResponse(
                TotalSubmissions: 0,
                OverallAverageAlignment: 0,
                LatestAlignment: null,
                PreviousAlignment: null,
                DeltaFromPrevious: null,
                PerSubTest: Array.Empty<ExpertCalibrationAlignmentBreakdownResponse>(),
                Trend: Array.Empty<ExpertCalibrationAlignmentTrendPointResponse>(),
                GeneratedAt: DateTimeOffset.UtcNow);
        }

        var caseIds = results.Select(r => r.CalibrationCaseId).Distinct().ToList();
        var cases = await db.ExpertCalibrationCases
            .AsNoTracking()
            .Where(c => caseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var scoredResults = results
            .Select(r =>
            {
                cases.TryGetValue(r.CalibrationCaseId, out var calibrationCase);
                var alignment = calibrationCase is null ? r.AlignmentScore : ResolveCalibrationAlignment(calibrationCase, r);
                return new
                {
                    Result = r,
                    Alignment = alignment,
                    SubTest = calibrationCase?.SubtestCode ?? "unknown"
                };
            })
            .ToList();

        var overallAverage = Math.Round(scoredResults.Average(r => r.Alignment), 1);
        var latest = scoredResults[0].Alignment;
        double? previous = scoredResults.Count > 1 ? scoredResults[1].Alignment : null;
        double? delta = previous is null ? null : Math.Round(latest - previous.Value, 1);

        var perSubTest = scoredResults
            .GroupBy(r => r.SubTest)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(r => r.Result.SubmittedAt).ToList();
                return new ExpertCalibrationAlignmentBreakdownResponse(
                    SubTest: g.Key,
                    SubmissionCount: ordered.Count,
                    AverageAlignment: Math.Round(ordered.Average(r => r.Alignment), 1),
                    LatestAlignment: ordered[0].Alignment);
            })
            .OrderBy(b => b.SubTest, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var trend = scoredResults
            .OrderBy(r => r.Result.SubmittedAt)
            .TakeLast(12)
            .Select(r => new ExpertCalibrationAlignmentTrendPointResponse(r.Result.SubmittedAt, r.Alignment))
            .ToList();

        return new ExpertCalibrationAlignmentResponse(
            TotalSubmissions: results.Count,
            OverallAverageAlignment: overallAverage,
            LatestAlignment: latest,
            PreviousAlignment: previous,
            DeltaFromPrevious: delta,
            PerSubTest: perSubTest,
            Trend: trend,
            GeneratedAt: DateTimeOffset.UtcNow);
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

        List<ExpertReviewAssignment> assignments;
        var assignmentsQuery = db.ExpertReviewAssignments.AsNoTracking()
            .Where(a => a.ReviewRequestId == reviewRequestId);

        if (!db.Database.IsSqlite())
        {
            assignments = await assignmentsQuery
                .OrderBy(a => a.AssignedAt ?? DateTimeOffset.MinValue)
                .ToListAsync(ct);
        }
        else
        {
            assignments = (await assignmentsQuery.ToListAsync(ct))
                .OrderBy(a => a.AssignedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }

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

        List<ExpertReviewDraft> drafts;
        var draftsQuery = db.ExpertReviewDrafts.AsNoTracking()
            .Where(d => d.ReviewRequestId == reviewRequestId);

        if (!db.Database.IsSqlite())
        {
            drafts = await draftsQuery
                .OrderBy(d => d.DraftSavedAt)
                .ToListAsync(ct);
        }
        else
        {
            drafts = (await draftsQuery.ToListAsync(ct))
                .OrderBy(d => d.DraftSavedAt)
                .ToList();
        }

        List<AuditEvent> auditEvents;
        var auditEventsQuery = db.AuditEvents.AsNoTracking()
            .Where(ae => ae.ResourceId == reviewRequestId && ae.ResourceType == "ExpertReview");

        if (!db.Database.IsSqlite())
        {
            auditEvents = await auditEventsQuery
                .OrderBy(ae => ae.OccurredAt)
                .Take(100)
                .ToListAsync(ct);
        }
        else
        {
            auditEvents = (await auditEventsQuery.ToListAsync(ct))
                .OrderBy(ae => ae.OccurredAt)
                .Take(100)
                .ToList();
        }

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

        var completedCalibrationResults = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(result => result.ReviewerId == reviewerId && !result.IsDraft)
            .ToListAsync(ct);
        var completedCalibrationCaseIds = completedCalibrationResults.Select(result => result.CalibrationCaseId).Distinct().ToArray();
        var completedCalibrationCases = await db.ExpertCalibrationCases
            .AsNoTracking()
            .Where(calibrationCase => completedCalibrationCaseIds.Contains(calibrationCase.Id))
            .ToDictionaryAsync(calibrationCase => calibrationCase.Id, ct);
        var calibrationAlignment = completedCalibrationResults.Count == 0
            ? 100.0
            : completedCalibrationResults
                .Select(result => completedCalibrationCases.TryGetValue(result.CalibrationCaseId, out var calibrationCase)
                    ? ResolveCalibrationAlignment(calibrationCase, result)
                    : result.AlignmentScore)
                .Average();

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
                ["message"] = $"Your {context.ReviewRequest.SubtestCode} tutor review is now ready."
            },
            ct);

        // ── Escalation auto-trigger: compare AI vs human scores ──
        await TryCreateEscalationAsync(context, reviewerId, request.Scores, ct);
    }

    /// <summary>
    /// Creates a ReviewEscalation if the average divergence between AI-suggested and human scores exceeds the threshold.
    /// </summary>
    private async Task TryCreateEscalationAsync(TrackedWriteContext context, string reviewerId, Dictionary<string, int> humanScores, CancellationToken ct)
    {
        const int DivergenceThreshold = 40; // OET 0-500 scale; ~1 band difference across criteria

        try
        {
            var evaluation = await db.Evaluations
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.AttemptId == context.Attempt.Id, ct);

            if (evaluation is null || evaluation.State != AsyncState.Completed)
                return;

            var isWriting = string.Equals(context.ReviewRequest.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase);
            var aiScores = NormalizeAiSuggestedScores(evaluation, isWriting);

            if (aiScores.Count == 0 || humanScores.Count == 0)
                return;

            var aiAvg = aiScores.Values.Average();
            var humanAvg = humanScores.Values.Average();
            var scaledAi = (int)Math.Round(aiAvg * (500.0 / (isWriting ? 7.0 : 6.0)));
            var scaledHuman = (int)Math.Round(humanAvg * (500.0 / (isWriting ? 7.0 : 6.0)));
            var divergence = Math.Abs(scaledAi - scaledHuman);

            if (divergence < DivergenceThreshold)
                return;

            var escalation = new ReviewEscalation
            {
                Id = $"ESC-{Guid.NewGuid():N}",
                ReviewRequestId = context.ReviewRequest.Id,
                OriginalReviewerId = reviewerId,
                SubtestCode = context.ReviewRequest.SubtestCode,
                TriggerCriterion = "average_divergence",
                AiScore = scaledAi,
                HumanScore = scaledHuman,
                Divergence = divergence,
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.ReviewEscalations.Add(escalation);
            await db.SaveChangesAsync(ct);

            logger.LogWarning(
                "Escalation {EscalationId} created: AI={AiScore} Human={HumanScore} Divergence={Divergence} for ReviewRequest={ReviewRequestId}",
                escalation.Id, scaledAi, scaledHuman, divergence, context.ReviewRequest.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check/create escalation for review {ReviewRequestId}", context.ReviewRequest.Id);
        }
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
                // Purpose is the ONLY writing criterion on the 0\u20133 scale; others 0\u20137 (rulebook R16.1 / R16.2).
                ["purpose"] = 2,
                ["content"] = 4,
                ["conciseness_clarity"] = 4,
                ["genre_style"] = 4,
                ["organisation_layout"] = 4,
                ["language"] = 4
            }
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // Linguistic (0–6)
                ["intelligibility"] = 4,
                ["fluency"] = 3,
                ["appropriateness"] = 4,
                ["grammar"] = 4,
                // Clinical Communication (0–3)
                ["relationshipBuilding"] = 2,
                ["patientPerspective"] = 2,
                ["providingStructure"] = 2,
                ["informationGathering"] = 2,
                ["informationGiving"] = 2
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
        var fallback = new ExpertSpeakingRoleCardResponse(
            "Nurse",
            "Ward",
            "Patient",
            "Provide a clinical handover.",
            null,
            [],
            "neutral",
            "Build rapport and complete the clinical task.",
            "roleplay",
            [],
            SpeakingContentStructure.DefaultPrepTimeSeconds,
            SpeakingContentStructure.DefaultRoleplayTimeSeconds,
            null,
            SpeakingContentStructure.PracticeDisclaimer);
        if (content is null)
        {
            return fallback;
        }

        var payload = SpeakingContentStructure.ExtractStructure(content.DetailJson);
        var candidate = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(payload, "candidateCard"));
        var interlocutor = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(payload, "interlocutorCard"));
        var tasks = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(candidate, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(payload, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(payload, "roleObjectives")));
        var warmUps = SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(payload, "warmUpQuestions"));

        return new ExpertSpeakingRoleCardResponse(
            SpeakingContentStructure.ReadString(candidate, "candidateRole", "role")
                ?? SpeakingContentStructure.ReadString(payload, "candidateRole", "role")
                ?? fallback.Role,
            SpeakingContentStructure.ReadString(candidate, "setting")
                ?? SpeakingContentStructure.ReadString(payload, "setting")
                ?? fallback.Setting,
            SpeakingContentStructure.ReadString(candidate, "patientRole", "patient")
                ?? SpeakingContentStructure.ReadString(payload, "patientRole", "patient")
                ?? fallback.Patient,
            SpeakingContentStructure.ReadString(candidate, "task", "brief")
                ?? SpeakingContentStructure.ReadString(payload, "task", "brief")
                ?? fallback.Task,
            SpeakingContentStructure.ReadString(candidate, "background")
                ?? SpeakingContentStructure.ReadString(payload, "background", "caseNotes")
                ?? fallback.Background,
            tasks,
            SpeakingContentStructure.ReadString(payload, "patientEmotion") ?? fallback.PatientEmotion,
            SpeakingContentStructure.ReadString(payload, "communicationGoal", "purpose") ?? fallback.CommunicationGoal,
            SpeakingContentStructure.ReadString(payload, "clinicalTopic") ?? content.ScenarioType ?? fallback.ClinicalTopic,
            warmUps,
            SpeakingContentStructure.ReadInt(payload, "prepTimeSeconds") ?? fallback.PrepTimeSeconds,
            SpeakingContentStructure.ReadInt(payload, "roleplayTimeSeconds") ?? fallback.RoleplayTimeSeconds,
            interlocutor.Count > 0 ? interlocutor : null,
            SpeakingContentStructure.ReadString(payload, "disclaimer") ?? fallback.Disclaimer);
    }

    private static List<string> FirstNonEmptyList(params List<string>[] lists)
        => lists.FirstOrDefault(list => list.Count > 0) ?? [];

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
                // Linguistic criteria (0–6 scale)
                new("intelligibility", 5, "Speech remains easy to follow with only minor stress-related hesitation."),
                new("fluency", 5, "Delivery is steady and recovers quickly after clarification moments."),
                new("appropriateness", 4, "Register is professional but one reassurance phrase is slightly abrupt."),
                new("grammar", 5, "Grammar and expression are controlled throughout the handover."),
                // Clinical Communication criteria (0–3 scale)
                new("relationshipBuilding", 2, "Respectful attitude and empathy are evident; introductions are complete."),
                new("patientPerspective", 2, "The candidate acknowledges the patient's concerns but misses one cue."),
                new("providingStructure", 3, "Clear signposting and logical sequencing of the handover."),
                new("informationGathering", 2, "Uses open-then-closed questioning; one compound question observed."),
                new("informationGiving", 2, "Pauses to check understanding; one safety-net checkback missed.")
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
        var isWriting = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase);
        var criteria = isWriting ? WritingCriteria : SpeakingCriteria;
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

            var maxScore = MaxScoreForCriterion(subtestCode, normalizedKey);
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

    /// <summary>
    /// Per-criterion max score. Writing: Purpose=3, others=7 (rulebook R16.1/R16.2).
    /// Speaking: linguistic=6, clinical-communication cluster=3 (OET CBLA official).
    /// </summary>
    private static int MaxScoreForCriterion(string subtestCode, string criterionCode)
    {
        if (string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(criterionCode, "purpose", StringComparison.OrdinalIgnoreCase) ? 3 : 7;
        }
        return SpeakingClinicalCriteria.Contains(criterionCode, StringComparer.OrdinalIgnoreCase) ? 3 : 6;
    }

    private static Dictionary<string, int> NormalizeCalibrationBenchmarkScores(
        IReadOnlyCollection<ExpertCalibrationRubricEntryResponse> benchmarkRubric,
        string subtestCode)
    {
        var criteria = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? WritingCriteria : SpeakingCriteria;
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in benchmarkRubric)
        {
            var normalizedKey = NormalizeCriterionKey(entry.Criterion, criteria);
            if (normalizedKey is null)
            {
                continue;
            }

            var maxScore = MaxScoreForCriterion(subtestCode, normalizedKey);
            normalized[normalizedKey] = Math.Clamp(entry.BenchmarkScore, 0, maxScore);
        }

        return normalized;
    }

    private static void ValidateCompleteCalibrationScores(
        IReadOnlyDictionary<string, int> normalizedScores,
        IReadOnlyDictionary<string, int> benchmarkLookup)
    {
        if (benchmarkLookup.Count == 0)
        {
            throw ApiException.Validation(
                "calibration_rubric_missing",
                "This calibration case does not have a benchmark rubric.",
                [new ApiFieldError("scores", "missing_benchmark", "A benchmark rubric is required before this calibration case can be submitted.")]);
        }

        var missing = benchmarkLookup.Keys
            .Where(criterion => !normalizedScores.ContainsKey(criterion))
            .ToArray();
        if (missing.Length > 0)
        {
            throw ApiException.Validation(
                "calibration_scores_incomplete",
                "Complete every benchmark criterion before submitting.",
                missing.Select(criterion => new ApiFieldError($"scores.{criterion}", "required", $"A score for {criterion} is required before final submission.")));
        }
    }

    private static int CalculateCalibrationReviewerScore(IReadOnlyDictionary<string, int> normalizedScores)
    {
        return normalizedScores.Count == 0
            ? 0
            : (int)Math.Round(normalizedScores.Values.Average(), MidpointRounding.AwayFromZero);
    }

    private static double CalculateCalibrationAlignment(
        IReadOnlyDictionary<string, int> normalizedScores,
        IReadOnlyDictionary<string, int> benchmarkLookup,
        string subtestCode)
    {
        var comparableCriteria = benchmarkLookup.Keys
            .Where(normalizedScores.ContainsKey)
            .ToList();
        if (comparableCriteria.Count == 0)
        {
            return 0;
        }

        var averageSimilarity = comparableCriteria.Average(criterion =>
        {
            var criterionMax = Math.Max(1.0, MaxScoreForCriterion(subtestCode, criterion));
            var delta = Math.Abs(normalizedScores[criterion] - benchmarkLookup[criterion]);
            return Math.Max(0.0, 1.0 - delta / criterionMax);
        });

        return Math.Round(averageSimilarity * 100.0, 1);
    }

    private static double ResolveCalibrationAlignment(ExpertCalibrationCase calibrationCase, ExpertCalibrationResult result)
    {
        if (result.IsDraft)
        {
            return 0;
        }

        try
        {
            var criteria = string.Equals(calibrationCase.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase)
                ? WritingCriteria
                : SpeakingCriteria;
            var rawScores = JsonSupport.Deserialize(result.SubmittedRubricJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
            var normalizedScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in rawScores)
            {
                var normalizedKey = NormalizeCriterionKey(key, criteria);
                if (normalizedKey is null)
                {
                    continue;
                }

                var maxScore = MaxScoreForCriterion(calibrationCase.SubtestCode, normalizedKey);
                normalizedScores[normalizedKey] = Math.Clamp(value, 0, maxScore);
            }

            var benchmarkLookup = NormalizeCalibrationBenchmarkScores(
                DeserializeCalibrationRubric(calibrationCase),
                calibrationCase.SubtestCode);

            return CalculateCalibrationAlignment(normalizedScores, benchmarkLookup, calibrationCase.SubtestCode);
        }
        catch
        {
            return result.AlignmentScore;
        }
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
                // Backward-compat aliases for legacy writing criterion codes stored before canonical rename.
                || (candidate == "conciseness_clarity" && (normalized == "conciseness" || normalized == "clarity"))
                || (candidate == "genre_style" && (normalized == "genre" || normalized == "style"))
                || (candidate == "organisation_layout" && (normalized == "organisation" || normalized == "organization" || normalized == "layout"))
                // Speaking aliases: collapse the many grammar spellings to canonical "grammar".
                || (candidate == "grammar" && (normalized == "grammarexpression" || normalized == "resources" || normalized == "resourcesofgrammarandexpression" || normalized == "resourcesofgrammarexpression"))
                // Speaking clinical criteria: accept snake_case / spaced / partial forms.
                || (candidate == "relationshipBuilding" && (normalized == "relationshipbuilding" || normalized == "relationship"))
                || (candidate == "patientPerspective" && (normalized == "patientperspective" || normalized == "understandingpatientperspective" || normalized == "understandingandincorporatingpatientsperspective" || normalized == "patientperspectives"))
                || (candidate == "providingStructure" && (normalized == "providingstructure" || normalized == "structure"))
                || (candidate == "informationGathering" && (normalized == "informationgathering" || normalized == "gathering"))
                || (candidate == "informationGiving" && (normalized == "informationgiving" || normalized == "giving"));
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

    // ── Annotation Templates ─────────────────────────────────────────

    public async Task<List<ExpertAnnotationTemplate>> GetAnnotationTemplatesAsync(
        string expertId, string? subtestCode, string? criterionCode, CancellationToken ct)
    {
        var query = db.ExpertAnnotationTemplates
            .Where(t => t.CreatedByExpertId == expertId || t.IsShared)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(subtestCode))
            query = query.Where(t => t.SubtestCode == subtestCode);
        if (!string.IsNullOrWhiteSpace(criterionCode))
            query = query.Where(t => t.CriterionCode == criterionCode);

        return await query.OrderByDescending(t => t.UsageCount).ThenBy(t => t.Label).ToListAsync(ct);
    }

    public async Task<ExpertAnnotationTemplate> CreateAnnotationTemplateAsync(
        string expertId, ExpertAnnotationTemplateRequest request, CancellationToken ct)
    {
        var template = new ExpertAnnotationTemplate
        {
            Id = $"annot-{Guid.NewGuid():N}",
            CreatedByExpertId = expertId,
            SubtestCode = request.SubtestCode.Trim(),
            CriterionCode = request.CriterionCode.Trim(),
            Label = request.Label.Trim(),
            TemplateText = request.TemplateText.Trim(),
            IsShared = request.IsShared,
            UsageCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.ExpertAnnotationTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<ExpertAnnotationTemplate> UpdateAnnotationTemplateAsync(
        string templateId, string expertId, ExpertAnnotationTemplateRequest request, CancellationToken ct)
    {
        var template = await db.ExpertAnnotationTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new KeyNotFoundException($"Template {templateId} not found.");

        if (template.CreatedByExpertId != expertId)
            throw new UnauthorizedAccessException("You can only edit your own templates.");

        template.SubtestCode = request.SubtestCode.Trim();
        template.CriterionCode = request.CriterionCode.Trim();
        template.Label = request.Label.Trim();
        template.TemplateText = request.TemplateText.Trim();
        template.IsShared = request.IsShared;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<object> DeleteAnnotationTemplateAsync(
        string templateId, string expertId, CancellationToken ct)
    {
        var template = await db.ExpertAnnotationTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new KeyNotFoundException($"Template {templateId} not found.");

        if (template.CreatedByExpertId != expertId)
            throw new UnauthorizedAccessException("You can only delete your own templates.");

        db.ExpertAnnotationTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return new { deleted = true };
    }

    // ══════════════════════════════════════════════════════
    // P13 · Schedule Exceptions
    // ══════════════════════════════════════════════════════

    public async Task<object> CreateScheduleExceptionAsync(string reviewerId, CreateScheduleExceptionRequest request, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw ApiException.Validation("date_invalid", "Provide a valid date in yyyy-MM-dd format.",
                [new ApiFieldError("date", "invalid", "Date must be in yyyy-MM-dd format.")]);
        }

        if (!request.IsBlocked)
        {
            if (string.IsNullOrWhiteSpace(request.StartTime) || string.IsNullOrWhiteSpace(request.EndTime))
            {
                throw ApiException.Validation("custom_hours_required", "Custom-hours exceptions require start and end times.",
                    [new ApiFieldError("startTime", "required", "Provide start and end times for custom-hours exceptions.")]);
            }

            if (!TimeOnly.TryParseExact(request.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !TimeOnly.TryParseExact(request.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                throw ApiException.Validation("time_invalid", "Times must use HH:mm format.",
                    [new ApiFieldError("startTime", "invalid_time", "Use HH:mm for schedule exception times.")]);
            }

            if (end <= start)
            {
                throw ApiException.Validation("time_range_invalid", "End time must be later than start time.",
                    [new ApiFieldError("endTime", "invalid_range", "End time must be later than start time.")]);
            }
        }

        var existing = await db.ScheduleExceptions
            .AnyAsync(e => e.ReviewerId == reviewerId && e.Date == date, ct);
        if (existing)
        {
            throw ApiException.Validation("duplicate_exception", "An exception already exists for this date.",
                [new ApiFieldError("date", "duplicate", "Remove the existing exception before creating a new one for this date.")]);
        }

        var entity = new ScheduleException
        {
            Id = $"se-{Guid.NewGuid():N}",
            ReviewerId = reviewerId,
            Date = date,
            IsBlocked = request.IsBlocked,
            StartTime = request.IsBlocked ? null : request.StartTime?.Trim(),
            EndTime = request.IsBlocked ? null : request.EndTime?.Trim(),
            Reason = request.Reason?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ScheduleExceptions.Add(entity);
        await db.SaveChangesAsync(ct);

        return new
        {
            entity.Id,
            date = entity.Date.ToString("yyyy-MM-dd"),
            entity.IsBlocked,
            entity.StartTime,
            entity.EndTime,
            entity.Reason,
            entity.CreatedAt
        };
    }

    public async Task<object> GetScheduleExceptionsAsync(string reviewerId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var query = db.ScheduleExceptions
            .AsNoTracking()
            .Where(e => e.ReviewerId == reviewerId);

        if (from.HasValue)
            query = query.Where(e => e.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.Date <= to.Value);

        var exceptions = await query
            .OrderBy(e => e.Date)
            .Select(e => new
            {
                e.Id,
                date = e.Date.ToString("yyyy-MM-dd"),
                e.IsBlocked,
                e.StartTime,
                e.EndTime,
                e.Reason,
                e.CreatedAt
            })
            .ToListAsync(ct);

        return new { exceptions };
    }

    public async Task<object> DeleteScheduleExceptionAsync(string reviewerId, string exceptionId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var entity = await db.ScheduleExceptions.FirstOrDefaultAsync(e => e.Id == exceptionId, ct)
            ?? throw new KeyNotFoundException($"Schedule exception {exceptionId} not found.");

        if (entity.ReviewerId != reviewerId)
            throw new UnauthorizedAccessException("You can only delete your own schedule exceptions.");

        db.ScheduleExceptions.Remove(entity);
        await db.SaveChangesAsync(ct);

        return new { deleted = true };
    }

    // ══════════════════════════════════════════════════════
    // X3 · Expert Scoring Quality Metrics
    // ══════════════════════════════════════════════════════

    public async Task<object> GetScoringQualityMetricsAsync(string reviewerId, int days, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var windowStart = DateTimeOffset.UtcNow.Date.AddDays(-(Math.Clamp(days, 1, 180) - 1));

        // Get this expert's completed reviews within window
        var assignments = await db.ExpertReviewAssignments
            .Where(a => a.AssignedReviewerId == reviewerId && a.ClaimState == ExpertAssignmentState.Released
                        && a.ReleasedAt >= windowStart && a.ReasonCode == "submitted")
            .ToListAsync(ct);

        var reviewRequestIds = assignments.Select(a => a.ReviewRequestId).ToList();

        // Get all drafts for these reviews
        var drafts = await db.ExpertReviewDrafts
            .Where(d => d.ReviewerId == reviewerId && reviewRequestIds.Contains(d.ReviewRequestId) && d.State == "submitted")
            .ToListAsync(ct);

        // Get evaluations for these review requests (AI scores for comparison)
        var attemptIds = await db.ReviewRequests
            .Where(r => reviewRequestIds.Contains(r.Id))
            .Select(r => r.AttemptId)
            .ToListAsync(ct);

        var evaluations = await db.Evaluations
            .Where(e => attemptIds.Contains(e.AttemptId) && e.State == AsyncState.Completed)
            .ToListAsync(ct);

        // Calculate scoring distribution per criterion
        var scoringDistribution = new Dictionary<string, List<int>>();
        foreach (var draft in drafts)
        {
            var rubricEntries = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(draft.RubricEntriesJson ?? "[]", []);
            foreach (var entry in rubricEntries)
            {
                var criterion = entry.TryGetValue("criterionCode", out var cc) ? cc?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(criterion)) continue;

                if (!scoringDistribution.ContainsKey(criterion))
                    scoringDistribution[criterion] = [];

                if (entry.TryGetValue("score", out var sv) && sv is not null)
                {
                    if (sv is System.Text.Json.JsonElement je && je.TryGetInt32(out var intVal))
                        scoringDistribution[criterion].Add(intVal);
                    else if (int.TryParse(sv.ToString(), out var parsed))
                        scoringDistribution[criterion].Add(parsed);
                }
            }
        }

        // AI-Human agreement: compare expert scores to AI evaluation scores
        var aiHumanDifferences = new List<double>();
        foreach (var draft in drafts)
        {
            var rr = await db.ReviewRequests.FirstOrDefaultAsync(r => r.Id == draft.ReviewRequestId, ct);
            if (rr == null) continue;

            var eval = evaluations.FirstOrDefault(e => e.AttemptId == rr.AttemptId);
            if (eval == null) continue;

            var expertScores = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(draft.RubricEntriesJson ?? "[]", []);
            var aiScores = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(eval.CriterionScoresJson, []);

            var expertAvg = expertScores.Average(s =>
            {
                if (s.TryGetValue("score", out var sv) && sv is not null)
                {
                    if (sv is System.Text.Json.JsonElement je && je.TryGetDouble(out var d)) return d;
                    if (double.TryParse(sv.ToString(), out var p)) return p;
                }
                return 0.0;
            });
            var aiAvg = aiScores.Average(s =>
            {
                if (s.TryGetValue("score", out var sv) && sv is not null)
                {
                    if (sv is System.Text.Json.JsonElement je && je.TryGetDouble(out var d)) return d;
                    if (double.TryParse(sv.ToString(), out var p)) return p;
                }
                return 0.0;
            });

            aiHumanDifferences.Add(Math.Abs(expertAvg - aiAvg));
        }

        // Calibration drift — track average score over time
        var chronologicalDrafts = drafts.OrderBy(d => d.DraftSavedAt).ToList();
        var calibrationTrend = new List<object>();
        for (var i = 0; i < chronologicalDrafts.Count; i += Math.Max(1, chronologicalDrafts.Count / 10))
        {
            var d = chronologicalDrafts[i];
            var rubric = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(d.RubricEntriesJson ?? "[]", []);
            var avg = rubric.Count > 0 ? rubric.Average(r =>
            {
                if (r.TryGetValue("score", out var sv) && sv is not null)
                {
                    if (sv is System.Text.Json.JsonElement je && je.TryGetDouble(out var dd)) return dd;
                    if (double.TryParse(sv.ToString(), out var p)) return p;
                }
                return 0.0;
            }) : 0;

            calibrationTrend.Add(new { date = d.DraftSavedAt, averageScore = Math.Round(avg, 2) });
        }

        return new
        {
            totalReviewsInWindow = drafts.Count,
            days,
            scoringDistribution = scoringDistribution.Select(kv => new
            {
                criterion = kv.Key,
                mean = kv.Value.Count > 0 ? Math.Round(kv.Value.Average(), 2) : 0,
                stdDev = kv.Value.Count > 1 ? Math.Round(Math.Sqrt(kv.Value.Average(v => Math.Pow(v - kv.Value.Average(), 2))), 2) : 0,
                min = kv.Value.Count > 0 ? kv.Value.Min() : 0,
                max = kv.Value.Count > 0 ? kv.Value.Max() : 0,
                count = kv.Value.Count
            }).ToList(),
            aiHumanAgreement = new
            {
                comparisons = aiHumanDifferences.Count,
                averageDifference = aiHumanDifferences.Count > 0 ? Math.Round(aiHumanDifferences.Average(), 2) : 0,
                maxDifference = aiHumanDifferences.Count > 0 ? Math.Round(aiHumanDifferences.Max(), 2) : 0,
                agreementRate = aiHumanDifferences.Count > 0
                    ? Math.Round(aiHumanDifferences.Count(d => d <= 1.0) * 100.0 / aiHumanDifferences.Count, 1) : 0
            },
            calibrationTrend,
            reworkRate = assignments.Count > 0
                ? Math.Round(assignments.Count(a => a.ReasonCode != "submitted") * 100.0 / assignments.Count, 1) : 0
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // XE1: Queue Priority Visibility — WHY an item is high priority
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetQueueWithPriorityReasonsAsync(string expertId, CancellationToken ct)
    {
        var assignments = await db.ExpertReviewAssignments
            .Where(a => a.AssignedReviewerId == expertId && a.ClaimState == ExpertAssignmentState.Assigned)
            .ToListAsync(ct);

        var items = new List<object>();
        foreach (var assignment in assignments)
        {
            var review = await db.ReviewRequests.FindAsync([assignment.ReviewRequestId], ct);
            if (review is null) continue;

            var attempt = await db.Attempts.FindAsync([review.AttemptId], ct);
            if (attempt is null) continue;

            // Check learner's exam date for urgency
            var learnerGoal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == attempt.UserId, ct);
            DateOnly? examDate = learnerGoal?.TargetExamDate;

            var daysToExam = examDate.HasValue
                ? (examDate.Value.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow).Days
                : (int?)null;

            // Check if re-submission
            var isResubmission = await db.ReviewRequests
                .CountAsync(r => r.AttemptId == review.AttemptId && r.CreatedAt < review.CreatedAt, ct) > 0;

            // SLA time remaining
            var hoursWaiting = (DateTimeOffset.UtcNow - review.CreatedAt).TotalHours;
            var slaHours = review.TurnaroundOption == "express" ? 24.0 : 48.0;
            var slaRemaining = slaHours - hoursWaiting;

            // Build priority reasons
            var reasons = new List<string>();
            var priority = "normal";

            if (slaRemaining < 6) { reasons.Add($"SLA expires in {Math.Round(slaRemaining, 1)}h"); priority = "critical"; }
            else if (slaRemaining < 12) { reasons.Add($"SLA at risk ({Math.Round(slaRemaining, 1)}h remaining)"); priority = "high"; }

            if (daysToExam.HasValue && daysToExam.Value <= 7) { reasons.Add($"Learner exam in {daysToExam.Value} days"); priority = "critical"; }
            else if (daysToExam.HasValue && daysToExam.Value <= 14) { reasons.Add($"Learner exam in {daysToExam.Value} days"); if (priority == "normal") priority = "high"; }

            if (isResubmission) { reasons.Add("Re-submission after revision"); if (priority == "normal") priority = "high"; }
            if (review.TurnaroundOption == "express") { reasons.Add("Express turnaround requested"); if (priority == "normal") priority = "high"; }

            if (reasons.Count == 0) reasons.Add("Standard review");

            items.Add(new
            {
                assignmentId = assignment.Id,
                reviewRequestId = review.Id,
                attemptId = review.AttemptId,
                subtestCode = review.SubtestCode,
                priority,
                reasons,
                daysToExam,
                slaRemainingHours = Math.Round(slaRemaining, 1),
                isResubmission,
                turnaround = review.TurnaroundOption,
                hoursWaiting = Math.Round(hoursWaiting, 1),
                createdAt = review.CreatedAt
            });
        }

        return new
        {
            items = items.OrderBy(i => ((dynamic)i).priority == "critical" ? 0 : ((dynamic)i).priority == "high" ? 1 : 2)
                        .ThenBy(i => ((dynamic)i).slaRemainingHours)
                        .ToList(),
            summary = new
            {
                total = items.Count,
                critical = items.Count(i => ((dynamic)i).priority == "critical"),
                high = items.Count(i => ((dynamic)i).priority == "high"),
                normal = items.Count(i => ((dynamic)i).priority == "normal")
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // XE2: AI Pre-Fill for Expert Reviews
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetAiPreFillForReviewAsync(string expertId, string reviewRequestId, CancellationToken ct)
    {
        var review = await db.ReviewRequests.FindAsync([reviewRequestId], ct)
            ?? throw ApiException.NotFound("REVIEW_NOT_FOUND", "Review request not found.");

        // Verify expert is assigned
        var assignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId && a.AssignedReviewerId == expertId, ct)
            ?? throw ApiException.Forbidden("NOT_ASSIGNED", "You are not assigned to this review.");

        var attempt = await db.Attempts.FindAsync([review.AttemptId], ct);

        // Get AI evaluation if available
        var aiEval = await db.Evaluations
            .Where(e => e.AttemptId == review.AttemptId)
            .OrderByDescending(e => e.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (aiEval is null)
        {
            return new
            {
                reviewRequestId,
                hasAiPreFill = false,
                message = "No AI evaluation available for pre-fill. Score from scratch."
            };
        }

        // Parse AI criterion scores
        var aiCriteria = new List<object>();
        try
        {
            var criterionScores = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(aiEval.CriterionScoresJson ?? "{}");
            if (criterionScores is not null)
            {
                foreach (var kv in criterionScores)
                {
                    var score = kv.Value.ValueKind == System.Text.Json.JsonValueKind.Number ? kv.Value.GetDouble() : 0;
                    aiCriteria.Add(new
                    {
                        criterionCode = kv.Key,
                        aiScore = score,
                        aiConfidence = aiEval.ConfidenceBand.ToString().ToLowerInvariant(),
                        note = "AI-suggested starting point. Accept, adjust, or override."
                    });
                }
            }
        }
        catch { }

        // Parse AI feedback items
        var aiCommentary = new List<object>();
        try
        {
            var comments = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(aiEval.FeedbackItemsJson ?? "[]");
            if (comments is not null)
            {
                foreach (var comment in comments)
                {
                    aiCommentary.Add(new
                    {
                        criterion = comment.TryGetValue("criterion", out var c) ? c.GetString() : null,
                        text = comment.TryGetValue("text", out var t) ? t.GetString() : null,
                        type = comment.TryGetValue("type", out var tp) ? tp.GetString() : "suggestion"
                    });
                }
            }
        }
        catch { }

        return new
        {
            reviewRequestId,
            hasAiPreFill = true,
            aiEvaluationId = aiEval.Id,
            aiScoreRange = aiEval.ScoreRange,
            aiConfidence = aiEval.ConfidenceBand,
            aiGeneratedAt = aiEval.GeneratedAt,
            subtestCode = review.SubtestCode,
            suggestedScores = aiCriteria,
            suggestedComments = aiCommentary,
            instructions = new
            {
                guidance = "Use AI scores as a starting point. Validate each criterion independently.",
                actions = new[] { "Accept", "Adjust (modify score)", "Override (score from scratch)" },
                note = "Your expert judgment always takes priority over AI suggestions."
            }
        };
    }

    // ── E7: Ask an Expert — Community Q&A ────────────────────────

    public async Task<object> GetAskAnExpertThreadsAsync(int page, int pageSize, CancellationToken ct)
    {
        var askAnExpertCategory = await db.ForumCategories
            .FirstOrDefaultAsync(c => c.Name == "Ask an Expert" && c.Status == "active", ct);

        if (askAnExpertCategory == null)
            return new { total = 0, threads = Array.Empty<object>() };

        var query = db.ForumThreads.Where(t => t.CategoryId == askAnExpertCategory.Id);
        var total = await query.CountAsync(ct);
        var threads = await query
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.LastActivityAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Check which threads already have an expert-verified reply
        var threadIds = threads.Select(t => t.Id).ToList();
        var threadsWithExpertReply = await db.ForumReplies
            .Where(r => threadIds.Contains(r.ThreadId) && r.IsExpertVerified)
            .Select(r => r.ThreadId)
            .Distinct()
            .ToListAsync(ct);
        var answeredSet = threadsWithExpertReply.ToHashSet();

        return new
        {
            total,
            categoryId = askAnExpertCategory.Id,
            threads = threads.Select(t => new
            {
                id = t.Id,
                title = t.Title,
                authorDisplayName = t.AuthorDisplayName,
                replyCount = t.ReplyCount,
                viewCount = t.ViewCount,
                hasExpertAnswer = answeredSet.Contains(t.Id),
                createdAt = t.CreatedAt,
                lastActivityAt = t.LastActivityAt
            })
        };
    }

    public async Task<object> PostVerifiedReplyAsync(string expertId, string threadId, string body, CancellationToken ct)
    {
        var thread = await db.ForumThreads.FindAsync([threadId], ct)
            ?? throw new InvalidOperationException("Thread not found.");

        if (thread.IsLocked)
            throw new InvalidOperationException("Thread is locked.");

        var expert = await db.ExpertUsers.FindAsync([expertId], ct);

        var reply = new ForumReply
        {
            Id = $"fr-{Guid.NewGuid():N}",
            ThreadId = threadId,
            AuthorUserId = expertId,
            AuthorDisplayName = expert?.DisplayName ?? "Expert",
            AuthorRole = "expert",
            Body = body,
            IsExpertVerified = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ForumReplies.Add(reply);
        thread.ReplyCount++;
        thread.LastActivityAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new { id = reply.Id, isExpertVerified = true };
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
