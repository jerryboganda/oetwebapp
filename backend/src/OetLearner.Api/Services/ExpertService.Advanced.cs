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
    // ── Annotation Templates ─────────────────────────────────────────

    public async Task<List<ExpertAnnotationTemplate>> GetAnnotationTemplatesAsync(
        string expertId, string? subtestCode, string? criterionCode, string? search, CancellationToken ct)
    {
        var query = db.ExpertAnnotationTemplates
            .Where(t => t.CreatedByExpertId == expertId || t.IsShared)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(subtestCode))
            query = query.Where(t => t.SubtestCode == subtestCode);
        if (!string.IsNullOrWhiteSpace(criterionCode))
            query = query.Where(t => t.CriterionCode == criterionCode);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Label.ToLower().Contains(term) || t.TemplateText.ToLower().Contains(term));
        }

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

    // ── Amend submitted review ────────────────────────────────────────

    public async Task<ExpertReviewAmendResponse> AmendReviewAsync(string reviewRequestId, string reviewerId, ExpertReviewAmendRequest request, CancellationToken ct)
    {
        var eligibility = await GetAmendEligibilityAsync(reviewRequestId, reviewerId, ct);
        if (!eligibility.CanAmend)
            throw ApiException.Validation("amend_not_eligible", eligibility.Reason ?? "Cannot amend this review.");

        var context = await LoadWriteContextAsync(reviewRequestId, reviewerId, ct);
        if (context.ReviewRequest.State != ReviewRequestState.Completed)
            throw ApiException.Validation("review_not_completed", "Only completed reviews can be amended.");

        var draft = await db.ExpertReviewDrafts
            .FirstOrDefaultAsync(d => d.ReviewRequestId == reviewRequestId && d.ReviewerId == reviewerId, ct);
        if (draft is null)
            throw ApiException.NotFound("draft_not_found", "No submitted draft found for this review.");

        var beforeSnapshot = new
        {
            scores = JsonSupport.Deserialize<Dictionary<string, int>>(draft.RubricEntriesJson, new Dictionary<string, int>()),
            comments = JsonSupport.Deserialize<Dictionary<string, string>>(draft.CriterionCommentsJson, new Dictionary<string, string>()),
            finalComment = draft.FinalCommentDraft
        };

        var updatedScores = NormalizeScores(request.Scores, context.ReviewRequest.SubtestCode);
        var updatedComments = NormalizeCriterionComments(request.CriterionComments, context.ReviewRequest.SubtestCode);
        var updatedFinal = NormalizeFinalComment(request.FinalComment, required: true);

        draft.RubricEntriesJson = JsonSupport.Serialize(updatedScores);
        draft.CriterionCommentsJson = JsonSupport.Serialize(updatedComments);
        draft.FinalCommentDraft = updatedFinal;
        draft.Version += 1;
        draft.DraftSavedAt = DateTimeOffset.UtcNow;

        var amend = new ExpertReviewAmend
        {
            Id = $"era-{Guid.NewGuid():N}",
            ReviewRequestId = reviewRequestId,
            ReviewerId = reviewerId,
            BeforeSnapshotJson = JsonSupport.Serialize(beforeSnapshot),
            AfterSnapshotJson = JsonSupport.Serialize(new { scores = updatedScores, comments = updatedComments, finalComment = updatedFinal }),
            AmendNumber = eligibility.AmendsUsed + 1,
            AmendedAt = DateTimeOffset.UtcNow
        };
        db.Set<ExpertReviewAmend>().Add(amend);

        await LogExpertAuditAsync(reviewerId, context.Expert.DisplayName, "Amended Review", reviewRequestId, $"Amend #{amend.AmendNumber}", ct);
        await RecordExpertEventAsync(reviewerId, "expert_review_amended", new { reviewRequestId, amendNumber = amend.AmendNumber }, ct);
        await db.SaveChangesAsync(ct);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReviewCompleted,
            context.Attempt.UserId,
            "review_request",
            reviewRequestId,
            DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["attemptId"] = context.Attempt.Id,
                ["reviewRequestId"] = reviewRequestId,
                ["message"] = $"Your {context.ReviewRequest.SubtestCode} review has been updated by the expert."
            },
            ct);

        logger.LogInformation("Expert {ReviewerId} amended review {ReviewRequestId} (amend #{AmendNumber})", reviewerId, reviewRequestId, amend.AmendNumber);
        return new ExpertReviewAmendResponse(reviewRequestId, amend.AmendNumber, amend.AmendedAt);
    }

    public async Task<ExpertAmendEligibilityResponse> GetAmendEligibilityAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var reviewRequest = await db.ReviewRequests.AsNoTracking()
            .FirstOrDefaultAsync(rr => rr.Id == reviewRequestId, ct);
        if (reviewRequest is null)
            throw ApiException.NotFound("review_not_found", "Review request not found.");

        if (reviewRequest.State != ReviewRequestState.Completed)
            return new ExpertAmendEligibilityResponse(false, 0, 2, null, "Review is not yet completed.");

        var assignment = await db.ExpertReviewAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId && (a.AssignedReviewerId == reviewerId || a.ReassignedFrom == reviewerId), ct);
        if (assignment is null)
            return new ExpertAmendEligibilityResponse(false, 0, 2, null, "You were not assigned to this review.");

        var completedAt = reviewRequest.CompletedAt ?? DateTimeOffset.MinValue;
        var hoursSinceCompletion = (DateTimeOffset.UtcNow - completedAt).TotalHours;

        if (hoursSinceCompletion > 24)
            return new ExpertAmendEligibilityResponse(false, 0, 2, null, "The 24-hour amend window has passed.");

        var amendCount = await db.Set<ExpertReviewAmend>()
            .CountAsync(a => a.ReviewRequestId == reviewRequestId, ct);

        if (amendCount >= 2)
            return new ExpertAmendEligibilityResponse(false, amendCount, 2, null, "Maximum number of amends (2) has been reached.");

        var hoursRemaining = 24 - hoursSinceCompletion;
        return new ExpertAmendEligibilityResponse(true, amendCount, 2, hoursRemaining, null);
    }

    // ── Rework chain history ──────────────────────────────────────────

    public async Task<ExpertReworkChainResponse> GetReworkChainAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var assignment = await db.ExpertReviewAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId && (a.AssignedReviewerId == reviewerId || a.ReassignedFrom == reviewerId), ct)
            ?? throw ApiException.Forbidden("not_your_review", "You are not assigned to this review.");

        var drafts = await db.ExpertReviewDrafts
            .AsNoTracking()
            .Where(d => d.ReviewRequestId == reviewRequestId)
            .OrderBy(d => d.DraftSavedAt)
            .ToListAsync(ct);

        if (drafts.Count == 0)
            return new ExpertReworkChainResponse(reviewRequestId, []);

        var reviewerIds = drafts.Select(d => d.ReviewerId).Distinct().ToList();
        var reviewers = await db.ExpertUsers.AsNoTracking()
            .Where(e => reviewerIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var chain = new List<ExpertReworkChainIterationResponse>();
        for (var i = 0; i < drafts.Count; i++)
        {
            var d = drafts[i];
            var scores = JsonSupport.Deserialize<Dictionary<string, int>>(d.RubricEntriesJson, new Dictionary<string, int>());
            reviewers.TryGetValue(d.ReviewerId, out var reviewer);
            chain.Add(new ExpertReworkChainIterationResponse(
                i + 1,
                d.State,
                scores,
                d.FinalCommentDraft,
                d.DraftSavedAt,
                reviewer?.DisplayName));
        }

        return new ExpertReworkChainResponse(reviewRequestId, chain);
    }

    // ── Bulk review operations ────────────────────────────────────────

    public async Task<ExpertBulkClaimResponse> BulkClaimReviewsAsync(string reviewerId, ExpertBulkClaimRequest request, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        if (request.ReviewRequestIds is null || request.ReviewRequestIds.Count == 0)
            throw ApiException.Validation("no_review_ids", "At least one review request ID is required.");

        const int maxBulkClaim = 10;
        if (request.ReviewRequestIds.Count > maxBulkClaim)
            throw ApiException.Validation("bulk_limit_exceeded", $"Cannot claim more than {maxBulkClaim} reviews at once.");

        var claimed = new List<string>();
        var failed = new List<string>();

        foreach (var id in request.ReviewRequestIds)
        {
            try
            {
                await ClaimReviewAsync(id, reviewerId, ct);
                claimed.Add(id);
            }
            catch
            {
                failed.Add(id);
            }
        }

        var activeCount = await db.ExpertReviewAssignments
            .CountAsync(a => a.AssignedReviewerId == reviewerId && a.ClaimState == ExpertAssignmentState.Claimed, ct);

        string? warning = null;
        if (activeCount > maxBulkClaim * 2)
            warning = "You have a high number of active reviews. Consider completing some before claiming more.";

        var expert = await EnsureExpertAsync(reviewerId, ct);
        await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Bulk Claim", null, $"Claimed {claimed.Count}, failed {failed.Count}", ct);
        await RecordExpertEventAsync(reviewerId, "expert_bulk_claim", new { claimedCount = claimed.Count, failedCount = failed.Count }, ct);

        return new ExpertBulkClaimResponse(claimed, failed, warning);
    }

    public async Task<ExpertBulkClaimResponse> BulkReleaseReviewsAsync(string reviewerId, ExpertBulkReleaseRequest request, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        if (request.ReviewRequestIds is null || request.ReviewRequestIds.Count == 0)
            throw ApiException.Validation("no_review_ids", "At least one review request ID is required.");

        var released = new List<string>();
        var failed = new List<string>();

        foreach (var id in request.ReviewRequestIds)
        {
            try
            {
                await ReleaseReviewAsync(id, reviewerId, ct);
                released.Add(id);
            }
            catch
            {
                failed.Add(id);
            }
        }

        var expert = await EnsureExpertAsync(reviewerId, ct);
        await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Bulk Release", null, $"Released {released.Count}, failed {failed.Count}", ct);
        await RecordExpertEventAsync(reviewerId, "expert_bulk_release", new { releasedCount = released.Count, failedCount = failed.Count }, ct);

        return new ExpertBulkClaimResponse(released, failed, null);
    }
}
