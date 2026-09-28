using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public partial class AdminService
{

    // ════════════════════════════════════════════
    //  Review Ops
    // ════════════════════════════════════════════

    public async Task<object> GetReviewOpsSummaryAsync(CancellationToken ct)
    {
        var pending = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.Queued, ct);
        var inProgress = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.InReview, ct);
        var completed = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.Completed, ct);

        var threshold = DateTimeOffset.UtcNow.AddHours(-48);
        var overdue = await db.ReviewRequests.CountAsync(r =>
            (r.State == ReviewRequestState.Queued || r.State == ReviewRequestState.InReview) && r.CreatedAt < threshold, ct);

        var riskThreshold = DateTimeOffset.UtcNow.AddHours(-24);
        var slaRisk = await db.ReviewRequests.CountAsync(r =>
            (r.State == ReviewRequestState.Queued || r.State == ReviewRequestState.InReview) && r.CreatedAt < riskThreshold && r.CreatedAt >= threshold, ct);

        return new
        {
            backlog = pending + inProgress,
            overdue,
            slaRisk,
            statusDistribution = new { pending, inProgress, completed }
        };
    }

    public async Task<object> GetReviewOpsQueueAsync(string? status, string? priority, CancellationToken ct)
    {
        var query = db.ReviewRequests.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var st = status switch
            {
                "pending" => ReviewRequestState.Queued,
                "in_progress" => ReviewRequestState.InReview,
                "completed" => ReviewRequestState.Completed,
                _ => ReviewRequestState.Queued
            };
            query = query.Where(r => r.State == st);
        }

        var reviews = await query.OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(priority) && priority != "all")
        {
            reviews = reviews
                .Where(r => (r.TurnaroundOption == "express" ? "high" : "normal") == priority)
                .ToList();
        }

        var attemptIds = reviews.Select(r => r.AttemptId).Distinct().ToList();
        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => attemptIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
        var learnerIds = attempts.Values.Select(a => a.UserId).Distinct().ToList();
        var learners = await db.Users.AsNoTracking()
            .Where(u => learnerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var assignments = await db.ExpertReviewAssignments.AsNoTracking()
            .Where(a => reviews.Select(r => r.Id).Contains(a.ReviewRequestId))
            .ToListAsync(ct);
        var reviewIds = reviews.Select(r => r.Id).Distinct().ToList();
        var paperCounts = await db.WritingAttemptAssets.AsNoTracking()
            .Where(asset => attemptIds.Contains(asset.AttemptId))
            .GroupBy(asset => asset.AttemptId)
            .Select(group => new { AttemptId = group.Key, Count = group.Count(), Completed = group.Count(asset => asset.ExtractionState == "completed") })
            .ToDictionaryAsync(row => row.AttemptId, ct);
        var voiceNoteCounts = await db.ReviewVoiceNotes.AsNoTracking()
            .Where(note => reviewIds.Contains(note.ReviewRequestId))
            .GroupBy(note => note.ReviewRequestId)
            .Select(group => new { ReviewRequestId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ReviewRequestId, row => row.Count, ct);

        var items = reviews.Select(r =>
        {
            attempts.TryGetValue(r.AttemptId, out var attempt);
            var assignment = assignments.FirstOrDefault(a => a.ReviewRequestId == r.Id);
            var learnerId = attempt?.UserId ?? "unknown";
            paperCounts.TryGetValue(r.AttemptId, out var paperCount);
            voiceNoteCounts.TryGetValue(r.Id, out var voiceNoteCount);
            return new
            {
                r.Id,
                taskId = r.AttemptId,
                learnerId,
                learnerName = learners.TryGetValue(learnerId, out var learnerName) ? learnerName : learnerId,
                assignedExpertId = assignment?.AssignedReviewerId,
                status = r.State == ReviewRequestState.InReview ? "in_progress"
                       : r.State == ReviewRequestState.Completed ? "completed"
                       : "pending",
                assignedAt = assignment?.AssignedAt ?? r.CreatedAt,
                subtestCode = r.SubtestCode,
                priority = r.TurnaroundOption == "express" ? "high" : "normal",
                submissionMode = ExtractWritingSubmissionMetadata(attempt, "examMode", "computer"),
                assessorType = ExtractWritingSubmissionMetadata(attempt, "assessorType", "instructor"),
                paperAssetCount = paperCount?.Count ?? 0,
                paperAssetsExtracted = paperCount?.Completed ?? 0,
                voiceNoteCount
            };
        }).ToList();

        return items;
    }

    private static string ExtractWritingSubmissionMetadata(Attempt? attempt, string propertyName, string fallback)
    {
        if (attempt is null || string.IsNullOrWhiteSpace(attempt.AnalysisJson)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(attempt.AnalysisJson);
            if (doc.RootElement.TryGetProperty("writingSubmission", out var submission)
                && submission.ValueKind == JsonValueKind.Object
                && submission.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? fallback;
            }
        }
        catch (JsonException)
        {
            return fallback;
        }

        return fallback;
    }

    public async Task<object> AssignReviewAsync(string adminId, string adminName,
        string reviewRequestId, AdminReviewAssignRequest request, CancellationToken ct)
    {
        for (var attemptNumber = 0; attemptNumber < 2; attemptNumber++)
        {
            try
            {
                return await AssignReviewCoreAsync(adminId, adminName, reviewRequestId, request, ct);
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber == 0)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw ApiException.Conflict(
            "review_assignment_conflict",
            "The review was assigned at the same time as your request. Refresh the queue and try again.");
    }

    private async Task<object> AssignReviewCoreAsync(string adminId, string adminName,
        string reviewRequestId, AdminReviewAssignRequest request, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var review = await db.ReviewRequests.FirstOrDefaultAsync(r => r.Id == reviewRequestId, ct)
                     ?? throw ApiException.NotFound("review_not_found", "Review request not found.");

        var assignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId, ct);
        var previousExpertId = assignment?.AssignedReviewerId;

        if (assignment is null)
        {
            assignment = new ExpertReviewAssignment
            {
                Id = $"ASN-{Guid.NewGuid():N}"[..12],
                ReviewRequestId = reviewRequestId,
                AssignedReviewerId = request.ExpertId,
                AssignedBy = adminId,
                AssignedAt = DateTimeOffset.UtcNow,
                ClaimState = ExpertAssignmentState.Assigned
            };
            db.ExpertReviewAssignments.Add(assignment);
        }
        else
        {
            assignment.AssignedReviewerId = request.ExpertId;
            assignment.AssignedBy = adminId;
            assignment.AssignedAt = DateTimeOffset.UtcNow;
            assignment.ClaimState = ExpertAssignmentState.Assigned;
        }

        review.State = ReviewRequestState.InReview;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Assigned Review", "ReviewRequest", reviewRequestId,
            $"Assigned to expert {request.ExpertId}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
        var notificationKey = string.IsNullOrWhiteSpace(previousExpertId) || string.Equals(previousExpertId, request.ExpertId, StringComparison.Ordinal)
            ? NotificationEventKey.ExpertReviewAssigned
            : NotificationEventKey.ExpertReviewReassigned;
        await notifications.CreateForExpertAsync(
            notificationKey,
            request.ExpertId,
            "review_request",
            reviewRequestId,
            (assignment.AssignedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["reviewRequestId"] = reviewRequestId,
                ["message"] = notificationKey == NotificationEventKey.ExpertReviewAssigned
                    ? $"A {review.SubtestCode} review was assigned to you."
                    : $"Review {reviewRequestId} was reassigned to you by admin review ops."
            },
            ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminReviewOpsAction,
            "review_request",
            reviewRequestId,
            (assignment.AssignedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString(),
            $"Review {reviewRequestId} was assigned to expert {request.ExpertId}.",
            ct);
        await CommitIfOwnedAsync(tx, ct);
        return new { id = reviewRequestId, assignedTo = request.ExpertId };
    }

    // ════════════════════════════════════════════
    //  Review Cancel / Reopen  (B6/B7)
    // ════════════════════════════════════════════

    public async Task<object> CancelReviewAsync(string adminId, string adminName,
        string reviewRequestId, AdminReviewCancelRequest request, CancellationToken ct)
    {
        var review = await db.ReviewRequests.FirstOrDefaultAsync(r => r.Id == reviewRequestId, ct)
                     ?? throw ApiException.NotFound("review_not_found", "Review request not found.");

        if (review.State is ReviewRequestState.Completed or ReviewRequestState.Cancelled)
            throw ApiException.Conflict("review_not_cancellable",
                $"Cannot cancel a review in {review.State} state.");

        review.State = ReviewRequestState.Cancelled;
        review.CompletedAt = DateTimeOffset.UtcNow;
        var refundedCredits = 0;
        if (string.Equals(review.PaymentSource, "credits", StringComparison.OrdinalIgnoreCase) && review.PriceSnapshot > 0)
        {
            var reviewCreditCost = (int)review.PriceSnapshot;
            var attempt = await db.Attempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == review.AttemptId, ct);
            var wallet = attempt is null
                ? null
                : await db.Wallets.FirstOrDefaultAsync(w => w.UserId == attempt.UserId, ct);
            if (wallet is not null)
            {
                var existingRefund = await db.WalletTransactions.AsNoTracking().AnyAsync(
                    tx => tx.WalletId == wallet.Id
                          && tx.TransactionType == "refund"
                          && tx.ReferenceType == "review"
                          && tx.ReferenceId == review.Id,
                    ct);
                if (!existingRefund)
                {
                    wallet.CreditBalance += reviewCreditCost;
                    wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
                    refundedCredits = reviewCreditCost;
                    db.WalletTransactions.Add(new WalletTransaction
                    {
                        Id = Guid.NewGuid(),
                        WalletId = wallet.Id,
                        TransactionType = "refund",
                        Amount = reviewCreditCost,
                        BalanceAfter = wallet.CreditBalance,
                        ReferenceType = "review",
                        ReferenceId = review.Id,
                        Description = $"Tutor review cancelled by admin: {request.Reason}",
                        CreatedBy = adminId,
                        CreatedAt = wallet.LastUpdatedAt
                    });
                }
            }
        }

        // Best-effort restore of a writing-assessment entitlement consumed at submission
        // (PaymentSource == "entitlement"). The guarded, once-only Cancelled transition above
        // gives this restore once-only semantics, mirroring the credits refund. We re-resolve a
        // currently-usable subscription for the learner rather than the exact one consumed (no
        // back-reference column exists, by design) and grant +1 writing assessment back. If the
        // learner has no usable subscription anymore (expired/cancelled), the credit is simply
        // not restorable and we move on — this is not an acceptance criterion.
        var restoredWritingAssessment = false;
        if (string.Equals(review.PaymentSource, "entitlement", StringComparison.OrdinalIgnoreCase))
        {
            var now = timeProvider.GetUtcNow();
            var attempt = await db.Attempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == review.AttemptId, ct);
            if (attempt is not null)
            {
                var candidates = await db.Subscriptions
                    .Where(s => s.UserId == attempt.UserId)
                    .ToListAsync(ct);
                var subscription = candidates
                    .Where(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial)
                    .Where(s => s.ExpiresAt is null || s.ExpiresAt > now)
                    .OrderBy(s => s.ExpiresAt == null)
                    .ThenBy(s => s.ExpiresAt)
                    .FirstOrDefault();
                if (subscription is not null)
                {
                    subscription.WritingAssessmentsRemaining = checked(subscription.WritingAssessmentsRemaining + 1);
                    restoredWritingAssessment = true;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        var auditDetails = refundedCredits > 0
            ? $"Cancelled: {request.Reason}. Refunded {refundedCredits} review credit(s)."
            : restoredWritingAssessment
                ? $"Cancelled: {request.Reason}. Restored 1 writing-assessment entitlement."
                : $"Cancelled: {request.Reason}";
        await LogAuditAsync(adminId, adminName, "Cancelled Review", "ReviewRequest", reviewRequestId,
            auditDetails, ct);
        return new { id = reviewRequestId, status = "cancelled", refundedCredits, restoredWritingAssessment };
    }

    public async Task<object> ReopenReviewAsync(string adminId, string adminName,
        string reviewRequestId, AdminReviewReopenRequest request, CancellationToken ct)
    {
        var review = await db.ReviewRequests.FirstOrDefaultAsync(r => r.Id == reviewRequestId, ct)
                     ?? throw ApiException.NotFound("review_not_found", "Review request not found.");

        if (review.State is not (ReviewRequestState.Cancelled or ReviewRequestState.Failed))
            throw ApiException.Conflict("review_not_reopenable",
                $"Only cancelled or failed reviews can be reopened. Current: {review.State}.");

        var paymentSource = (review.PaymentSource ?? string.Empty).Trim().ToLowerInvariant();
        if (paymentSource == "credits" && review.PriceSnapshot > 0)
        {
            var reviewCreditCost = (int)review.PriceSnapshot;
            var attempt = await db.Attempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == review.AttemptId, ct)
                ?? throw ApiException.NotFound("attempt_not_found", "Review attempt not found.");
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == attempt.UserId, ct)
                ?? throw ApiException.NotFound("wallet_not_found", "Learner wallet not found.");
            var wasRefunded = await db.WalletTransactions.AsNoTracking().AnyAsync(
                tx => tx.WalletId == wallet.Id
                      && tx.TransactionType == "refund"
                      && tx.ReferenceType == "review"
                      && tx.ReferenceId == review.Id,
                ct);
            var alreadyRecharged = await db.WalletTransactions.AsNoTracking().AnyAsync(
                tx => tx.WalletId == wallet.Id
                      && tx.TransactionType == "review_reopen_deduction"
                      && tx.ReferenceType == "review"
                      && tx.ReferenceId == review.Id,
                ct);
            if (wasRefunded && !alreadyRecharged)
            {
                if (wallet.CreditBalance < reviewCreditCost)
                {
                    throw ApiException.Conflict("insufficient_credits_to_reopen", "The learner no longer has enough review credits to reopen this tutor review.");
                }

                wallet.CreditBalance -= reviewCreditCost;
                wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
                db.WalletTransactions.Add(new WalletTransaction
                {
                    Id = Guid.NewGuid(),
                    WalletId = wallet.Id,
                    TransactionType = "review_reopen_deduction",
                    Amount = -reviewCreditCost,
                    BalanceAfter = wallet.CreditBalance,
                    ReferenceType = "review",
                    ReferenceId = review.Id,
                    Description = "Tutor review reopened by admin.",
                    CreatedBy = adminId,
                    CreatedAt = wallet.LastUpdatedAt
                });
            }
        }

        // "entitlement" = a bundled writing-assessment review (PriceSnapshot 0, no
        // wallet charge) — it is prepaid, so it must reopen straight to the queue,
        // never AwaitingPayment (which would strand a £0 review behind a payment
        // that never arrives).
        review.State = paymentSource is "credits" or "mock_reserved_credits" or "entitlement"
            ? ReviewRequestState.Queued
            : ReviewRequestState.AwaitingPayment;
        review.CompletedAt = null;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Reopened Review", "ReviewRequest", reviewRequestId,
            request.Reason ?? "Reopened by admin", ct);
        return new { id = reviewRequestId, status = review.State == ReviewRequestState.Queued ? "queued" : "awaiting_payment" };
    }

    // ════════════════════════════════════════════
    //  Review Failures  (B8)
    // ════════════════════════════════════════════

    public async Task<object> GetReviewFailuresAsync(CancellationToken ct)
    {
        var failedReviews = await db.ReviewRequests
            .Where(r => r.State == ReviewRequestState.Failed)
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .Select(r => new
            {
                r.Id,
                attemptId = r.AttemptId,
                subtestCode = r.SubtestCode,
                state = "failed",
                createdAt = r.CreatedAt,
                completedAt = r.CompletedAt
            }).ToListAsync(ct);

        var stuckThreshold = DateTimeOffset.UtcNow.AddHours(-72);
        var stuckReviews = await db.ReviewRequests
            .Where(r => r.State == ReviewRequestState.InReview && r.CreatedAt < stuckThreshold)
            .OrderBy(r => r.CreatedAt)
            .Take(100)
            .Select(r => new
            {
                r.Id,
                attemptId = r.AttemptId,
                subtestCode = r.SubtestCode,
                state = "stuck",
                createdAt = r.CreatedAt,
                completedAt = r.CompletedAt
            }).ToListAsync(ct);

        var failedJobs = await db.BackgroundJobs
            .Where(j => j.State == AsyncState.Failed)
            .OrderByDescending(j => j.CreatedAt)
            .Take(50)
            .Select(j => new
            {
                j.Id,
                type = j.Type.ToString(),
                attemptId = j.AttemptId,
                state = "failed",
                reason = j.StatusReasonCode,
                message = j.StatusMessage,
                retryCount = j.RetryCount,
                createdAt = j.CreatedAt
            }).ToListAsync(ct);

        return new
        {
            failedReviews,
            stuckReviews,
            failedJobs,
            summary = new
            {
                failedReviewCount = failedReviews.Count,
                stuckReviewCount = stuckReviews.Count,
                failedJobCount = failedJobs.Count
            }
        };
    }

    // ════════════════════════════════════════════
    //  Review Escalation (Disagreement Resolution)
    // ════════════════════════════════════════════

    public async Task<object> GetReviewEscalationsAsync(
        string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ReviewEscalations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(e => e.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                id = e.Id,
                reviewRequestId = e.ReviewRequestId,
                originalReviewerId = e.OriginalReviewerId,
                secondReviewerId = e.SecondReviewerId,
                subtestCode = e.SubtestCode,
                triggerCriterion = e.TriggerCriterion,
                aiScore = e.AiScore,
                humanScore = e.HumanScore,
                divergence = e.Divergence,
                status = e.Status,
                resolutionNote = e.ResolutionNote,
                finalScore = e.FinalScore,
                createdAt = e.CreatedAt,
                resolvedAt = e.ResolvedAt
            })
            .ToListAsync(ct);

        return new { items, total, page, pageSize };
    }

    public async Task<object> AssignEscalationReviewerAsync(
        string actorId, string actorName, string escalationId,
        AdminEscalationAssignRequest request, CancellationToken ct)
    {
        var esc = await db.ReviewEscalations.FirstOrDefaultAsync(e => e.Id == escalationId, ct)
                  ?? throw ApiException.NotFound("escalation_not_found", "Escalation not found.");

        if (esc.Status != "pending")
            throw ApiException.Validation("not_pending", "Escalation is not pending.");

        if (request.SecondReviewerId == esc.OriginalReviewerId)
            throw ApiException.Validation("same_reviewer", "Second reviewer must be different from the original reviewer.");

        var reviewer = await db.ApplicationUserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.SecondReviewerId && u.Role == ApplicationUserRoles.Expert, ct)
            ?? throw ApiException.NotFound("reviewer_not_found", "Tutor reviewer not found.");

        esc.SecondReviewerId = request.SecondReviewerId;
        esc.Status = "assigned";
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "AssignEscalation", "ReviewEscalation", escalationId,
            $"Assigned to expert: {request.SecondReviewerId}", ct);

        return new { escalationId, status = "assigned", secondReviewerId = request.SecondReviewerId };
    }

    public async Task<object> ResolveEscalationAsync(
        string actorId, string actorName, string escalationId,
        AdminEscalationResolveRequest request, CancellationToken ct)
    {
        var esc = await db.ReviewEscalations.FirstOrDefaultAsync(e => e.Id == escalationId, ct)
                  ?? throw ApiException.NotFound("escalation_not_found", "Escalation not found.");

        if (esc.Status == "resolved")
            throw ApiException.Validation("already_resolved", "Escalation is already resolved.");

        if (request.FinalScore < 0 || request.FinalScore > 500)
            throw ApiException.Validation("invalid_score", "OET score must be between 0 and 500.");

        esc.FinalScore = request.FinalScore;
        esc.ResolutionNote = request.ResolutionNote;
        esc.Status = "resolved";
        esc.ResolvedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "ResolveEscalation", "ReviewEscalation", escalationId,
            $"Resolved with final score: {request.FinalScore}", ct);

        return new { escalationId, status = "resolved", finalScore = request.FinalScore };
    }

    // ── Score Guarantee Claims ──────────────────────────────────────

    public async Task<object> GetScoreGuaranteeClaimsAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ScoreGuaranteePledges.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.ActivatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new { items, total, page, pageSize };
    }

    public async Task<object> ReviewScoreGuaranteeClaimAsync(
        string actorId, string actorName, string pledgeId, AdminScoreGuaranteeReviewRequest request, CancellationToken ct)
    {
        var pledge = await db.ScoreGuaranteePledges.FirstOrDefaultAsync(p => p.Id == pledgeId, ct)
            ?? throw ApiException.NotFound("pledge_not_found", $"Pledge {pledgeId} not found.");

        if (pledge.Status != "claim_submitted")
            throw ApiException.Validation("invalid_status", "Only submitted claims can be reviewed.");

        var decision = request.Decision.ToLowerInvariant();
        if (decision is not ("approve" or "reject"))
            throw ApiException.Validation("invalid_decision", "Decision must be 'approve' or 'reject'.");

        pledge.Status = decision == "approve" ? "claim_approved" : "claim_rejected";
        pledge.ReviewNote = request.Note;
        pledge.ReviewedBy = actorId;

        if (decision == "approve")
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == pledge.UserId, ct);
            if (wallet != null)
            {
                var refundCredits = 50; // standard guarantee refund credits
                wallet.CreditBalance += refundCredits;
                wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
                db.WalletTransactions.Add(new WalletTransaction
                {
                    Id = Guid.NewGuid(),
                    WalletId = wallet.Id,
                    TransactionType = "refund",
                    Amount = refundCredits,
                    BalanceAfter = wallet.CreditBalance,
                    ReferenceType = "manual",
                    ReferenceId = pledge.Id,
                    Description = "Score guarantee claim approved — refund",
                    CreatedBy = actorId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "ReviewScoreGuaranteeClaim", "ScoreGuaranteePledge", pledgeId,
            $"Decision: {decision}", ct);

        return new { pledgeId, status = pledge.Status, decision };
    }
}
