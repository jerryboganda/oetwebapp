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
}
