using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    public async Task<object> GetStudyPlanAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
        var items = await db.StudyPlanItems.Where(x => x.StudyPlanId == plan.Id).OrderBy(x => x.DueDate).ToListAsync(cancellationToken);
        var latestJob = await GetLatestStudyPlanRegenerationJobAsync(plan.Id, cancellationToken);

        return new
        {
            planId = plan.Id,
            version = plan.Version,
            generatedAt = plan.GeneratedAt,
            state = ToAsyncState(plan.State),
            checkpoint = plan.Checkpoint,
            weakSkillFocus = plan.WeakSkillFocus,
            items = items.Select(StudyPlanItemDto).ToList(),
            statusReasonCode = latestJob?.StatusReasonCode,
            statusMessage = latestJob?.StatusMessage,
            retryAfterMs = latestJob?.RetryAfterMs,
            lastTransitionAt = latestJob?.LastTransitionAt
        };
    }

    public async Task<object> RegenerateStudyPlanAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
        plan.State = AsyncState.Queued;
        var payloadJson = JsonSupport.Serialize(new { userId, trigger = "Manual" });
        await QueueJobAsync(JobType.StudyPlanRegeneration, resourceId: plan.Id, payloadJson: payloadJson, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { planId = plan.Id, state = "queued", nextPollAfterMs = 2000 };
    }

    public async Task<object> CompleteStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        return await CompleteStudyPlanItemInternalAsync(userId, itemId, feedbackRating: null, actualMinutesSpent: null, cancellationToken);
    }

    public async Task<object> CompleteStudyPlanItemAsync(string userId, string itemId, int? feedbackRating, int? actualMinutesSpent, CancellationToken cancellationToken)
    {
        return await CompleteStudyPlanItemInternalAsync(userId, itemId, feedbackRating, actualMinutesSpent, cancellationToken);
    }

    private async Task<object> CompleteStudyPlanItemInternalAsync(string userId, string itemId, int? feedbackRating, int? actualMinutesSpent, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        var alreadyCompleted = item.Status == StudyPlanItemStatus.Completed;
        item.Status = StudyPlanItemStatus.Completed;
        item.CompletedAt ??= DateTimeOffset.UtcNow;
        if (feedbackRating is not null) item.FeedbackRating = feedbackRating;
        if (actualMinutesSpent is not null) item.ActualMinutesSpent = actualMinutesSpent;
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_completed", new { itemId = item.Id, planId = item.StudyPlanId, subtest = item.SubtestCode, feedbackRating, actualMinutesSpent }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (!alreadyCompleted)
        {
            await AwardCompletionRewardsAsync(plan.UserId, item, cancellationToken);
            await TryRefreshReadinessAsync(plan.UserId, cancellationToken);
        }

        return StudyPlanItemDto(item);
    }

    private async Task TryRefreshReadinessAsync(string userId, CancellationToken cancellationToken)
    {
        if (readinessComputation is null) return;
        try
        {
            await readinessComputation.ComputeAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[readiness] Compute failed for user {UserId}", userId);
        }
    }

    private async Task AwardCompletionRewardsAsync(string userId, StudyPlanItem item, CancellationToken cancellationToken)
    {
        // XP: scale modestly with task duration so a 5-min flashcard ≠ a 45-min mock.
        if (gamification is not null)
        {
            var xp = Math.Clamp(10 + (item.DurationMinutes / 5), 10, 60);
            try
            {
                await gamification.AwardXpAsync(userId, xp, $"study_plan:{item.SubtestCode}", cancellationToken);
            }
            catch (Exception ex)
            {
                // XP award is non-critical — log only.
                logger?.LogWarning(ex, "[plan-complete] XP award failed for user {UserId} item {ItemId}", userId, item.Id);
            }
        }

        // Spaced-repetition progression when this plan item linked back to a ReviewItem.
        if (!string.IsNullOrWhiteSpace(item.LinkedReviewItemId) && spacedRepetition is not null)
        {
            var quality = MapFeedbackToSm2Quality(item.FeedbackRating);
            try
            {
                await spacedRepetition.SubmitReviewAsync(userId, item.LinkedReviewItemId!, quality, cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[plan-complete] SM-2 update failed for user {UserId} review {ReviewItemId}", userId, item.LinkedReviewItemId);
            }
        }
    }

    private static int MapFeedbackToSm2Quality(int? feedbackRating) => feedbackRating switch
    {
        1 => 5,   // "Too easy" → perfect recall
        2 => 4,   // "Just right" → good
        3 => 2,   // "Too hard" → hard
        _ => 4    // No feedback → assume good
    };

    public async Task<object> SkipStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.Skipped;
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_skipped", new { itemId = item.Id, planId = item.StudyPlanId, subtest = item.SubtestCode }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> RescheduleStudyPlanItemAsync(string userId, string itemId, StudyPlanRescheduleRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.Rescheduled;
        item.DueDate = request.DueDate ?? item.DueDate.AddDays(1);
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_rescheduled", new { itemId = item.Id, planId = item.StudyPlanId, dueDate = item.DueDate, subtest = item.SubtestCode }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> ResetStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.NotStarted;
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> SwapStudyPlanItemAsync(string userId, string itemId, StudyPlanSwapRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);

        // Mode 1: candidates-only — list 3 alternatives without mutating.
        if (string.IsNullOrWhiteSpace(request.ReplacementContentId))
        {
            if (studyPlanContentPicker is null)
            {
                return new { candidates = Array.Empty<object>(), note = "ContentPicker unavailable" };
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            var candidates = await studyPlanContentPicker.ResolveAlternativesAsync(item, user?.ActiveProfessionId, count: 3, cancellationToken);
            return new
            {
                itemId = item.Id,
                candidates = candidates.Select(c => new
                {
                    contentId = c.ContentId,
                    title = c.Title,
                    route = c.Route,
                    durationMinutes = c.DurationMinutes
                })
            };
        }

        // Mode 2: apply swap — mark original Replaced, insert new item.
        var replacement = await db.ContentItems
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ReplacementContentId, cancellationToken);

        var newItem = new StudyPlanItem
        {
            Id = $"plan-item-{Guid.NewGuid():N}",
            StudyPlanId = item.StudyPlanId,
            Title = replacement?.Title ?? item.Title,
            SubtestCode = item.SubtestCode,
            DurationMinutes = replacement?.EstimatedDurationMinutes > 0 ? replacement.EstimatedDurationMinutes : item.DurationMinutes,
            Rationale = $"Swapped from \"{item.Title}\" at your request. Comparable scope and duration.",
            DueDate = item.DueDate,
            Status = StudyPlanItemStatus.NotStarted,
            Section = item.Section,
            ContentId = request.ReplacementContentId,
            SourceContentId = request.ReplacementContentId,
            ContentRoute = BuildRouteForReplacement(item.SubtestCode, request.ReplacementContentId, item.ItemType),
            ItemType = item.ItemType,
            PriorityScore = item.PriorityScore,
            WeekIndex = item.WeekIndex,
            SlotKind = item.SlotKind
        };
        db.StudyPlanItems.Add(newItem);

        // Swap state: keep enum stable, mark via ReplacedById link. UI filters
        // out items where ReplacedById is non-null so they no longer surface.
        item.Status = StudyPlanItemStatus.Skipped;
        item.ReplacedById = newItem.Id;

        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_swapped", new
        {
            originalItemId = item.Id,
            newItemId = newItem.Id,
            planId = item.StudyPlanId,
            subtest = item.SubtestCode,
            replacementContentId = request.ReplacementContentId
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(newItem);
    }

    private static string BuildRouteForReplacement(string subtest, string contentId, string itemType)
    {
        var lower = (subtest ?? string.Empty).ToLowerInvariant();
        if (string.Equals(itemType, "mock", StringComparison.OrdinalIgnoreCase))
        {
            return $"/{lower}/mocks";
        }
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return $"/{lower}";
        }
        return lower switch
        {
            "reading" => $"/reading/paper/{Uri.EscapeDataString(contentId)}",
            "listening" => $"/listening/player/{Uri.EscapeDataString(contentId)}",
            "writing" => "/writing/practice/library",
            "speaking" => $"/speaking/roleplay/{Uri.EscapeDataString(contentId)}",
            _ => $"/{lower}"
        };
    }

    private async Task<BackgroundJobItem?> GetLatestStudyPlanRegenerationJobAsync(string planId, CancellationToken cancellationToken)
    {
        var query = db.BackgroundJobs
            .Where(x => x.Type == JobType.StudyPlanRegeneration && x.ResourceId == planId);

        if (!db.Database.IsSqlite())
        {
            return await query
                .OrderByDescending(x => x.LastTransitionAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var jobs = await query.ToListAsync(cancellationToken);
        return jobs
            .OrderByDescending(x => x.LastTransitionAt)
            .FirstOrDefault();
    }

    private static object StudyPlanItemDto(StudyPlanItem item) => new
    {
        itemId = item.Id,
        title = item.Title,
        subtest = item.SubtestCode,
        durationMinutes = item.DurationMinutes,
        rationale = item.Rationale,
        dueDate = item.DueDate,
        status = ToStudyPlanItemState(item.Status),
        section = item.Section,
        contentId = item.ContentId,
        itemType = item.ItemType,
        route = string.IsNullOrWhiteSpace(item.ContentRoute) ? StudyPlanRouteForItem(item) : item.ContentRoute,
        slotKind = item.SlotKind,
        weekIndex = item.WeekIndex,
        priorityScore = item.PriorityScore,
        linkedReviewItemId = item.LinkedReviewItemId,
        feedbackRating = item.FeedbackRating,
        completedAt = item.CompletedAt,
        actualMinutesSpent = item.ActualMinutesSpent,
        replacedById = item.ReplacedById
    };

    private static string StudyPlanRouteForItem(StudyPlanItem item)
    {
        if (string.Equals(item.ItemType, "mock", StringComparison.OrdinalIgnoreCase)) return "/mocks";
        if (string.Equals(item.SubtestCode, "vocabulary", StringComparison.OrdinalIgnoreCase)) return "/vocabulary";
        if (string.IsNullOrWhiteSpace(item.ContentId)) return $"/{item.SubtestCode.ToLowerInvariant()}";

        return item.SubtestCode.ToLowerInvariant() switch
        {
            "writing" => "/writing/practice/library",
            "speaking" => $"/speaking/roleplay/{Uri.EscapeDataString(item.ContentId)}",
            "reading" => "/reading",
            "listening" => $"/listening/player/{Uri.EscapeDataString(item.ContentId)}",
            _ => $"/{item.SubtestCode.ToLowerInvariant()}"
        };
    }
}
