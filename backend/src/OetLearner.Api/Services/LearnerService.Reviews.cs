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

    public async Task<object> GetReviewVoiceNotesAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await GetReviewRequestOwnedByUserAsync(userId, reviewRequestId, cancellationToken);
        if (review.State != ReviewRequestState.Completed)
        {
            return new { reviewRequestId, items = Array.Empty<ReviewVoiceNoteResponse>() };
        }

        var notes = await LoadReviewVoiceNoteResponsesAsync(reviewRequestId, readyOnly: true, cancellationToken);
        return new { reviewRequestId, items = notes };
    }

    public async Task<object> GetReviewResultAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await GetReviewRequestOwnedByUserAsync(userId, reviewRequestId, cancellationToken);
        if (review.State != ReviewRequestState.Completed)
        {
            throw ApiException.NotFound("review_result_not_ready", "This tutor review result is not ready yet.");
        }

        var submittedDraft = await db.ExpertReviewDrafts
            .AsNoTracking()
            .Where(draft => draft.ReviewRequestId == reviewRequestId && draft.State == "submitted")
            .OrderByDescending(draft => draft.DraftSavedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw ApiException.NotFound("review_result_not_found", "Tutor review result was not found.");

        var scores = JsonSupport.Deserialize(submittedDraft.RubricEntriesJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var comments = JsonSupport.Deserialize(submittedDraft.CriterionCommentsJson, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var criteria = OrderReviewCriteria(review.SubtestCode, scores.Keys)
            .Select(code =>
            {
                var score = scores.GetValueOrDefault(code);
                var maxScore = ReviewCriterionMaxScore(review.SubtestCode, code);
                return new
                {
                    code,
                    name = CriterionLabelFromCode(code),
                    score,
                    maxScore,
                    explanation = comments.GetValueOrDefault(code) ?? string.Empty
                };
            })
            .ToList();
        var totalScore = criteria.Sum(criterion => criterion.score);
        var totalMaxScore = criteria.Sum(criterion => criterion.maxScore);

        return new
        {
            reviewRequestId = review.Id,
            attemptId = review.AttemptId,
            subtest = review.SubtestCode,
            state = ToReviewRequestState(review.State),
            completedAt = review.CompletedAt,
            submittedAt = submittedDraft.DraftSavedAt,
            finalComment = submittedDraft.FinalCommentDraft,
            scoreLabel = totalMaxScore > 0 ? $"Dr. Ahmed rubric {totalScore}/{totalMaxScore}" : "Dr. Ahmed reviewed",
            scores,
            criterionComments = comments,
            criteria
        };
    }

    private async Task<IReadOnlyList<ReviewVoiceNoteResponse>> LoadReviewVoiceNoteResponsesAsync(string reviewRequestId, bool readyOnly, CancellationToken cancellationToken)
    {
        var query = db.ReviewVoiceNotes
            .AsNoTracking()
            .Where(note => note.ReviewRequestId == reviewRequestId);
        if (readyOnly)
        {
            query = query.Where(note => note.Status == "ready");
        }

        var rows = await query
            .OrderByDescending(note => note.CreatedAt)
            .Join(db.MediaAssets.AsNoTracking(), note => note.MediaAssetId, media => media.Id, (note, media) => new { note, media })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new ReviewVoiceNoteResponse(
            row.note.Id,
            row.note.ReviewRequestId,
            row.media.Id,
            row.media.OriginalFilename,
            row.media.MimeType,
            row.note.DurationSeconds,
            row.note.TranscriptText,
            row.note.WrittenNotes,
            JsonSupport.Deserialize<Dictionary<string, int>>(row.note.RubricJson, new Dictionary<string, int>()),
            row.note.Status,
            row.note.CreatedAt,
            $"/v1/media/{row.media.Id}/content")).ToList();
    }

    public async Task<object> GetReviewsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var attemptIds = await db.Attempts.Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        var reviews = await db.ReviewRequests.Where(x => attemptIds.Contains(x.AttemptId)).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        return new
        {
            items = reviews.Select(x => new
            {
                reviewRequestId = x.Id,
                attemptId = x.AttemptId,
                subtest = x.SubtestCode,
                state = ToReviewRequestState(x.State),
                turnaroundOption = x.TurnaroundOption,
                focusAreas = JsonSupport.Deserialize<List<string>>(x.FocusAreasJson, []),
                createdAt = x.CreatedAt,
                completedAt = x.CompletedAt
            })
        };
    }

    public async Task<object> GetReviewEligibilityAsync(string userId, string? attemptId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        Attempt? attempt = null;
        if (!string.IsNullOrWhiteSpace(attemptId))
        {
            attempt = await db.Attempts.FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, cancellationToken);
        }

        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        var canRequest = attempt is not null && attempt.State == AttemptState.Completed && attempt.SubtestCode is "writing" or "speaking";
        var reasons = new List<string>();
        if (attempt is null) reasons.Add("attempt_not_found");
        else if (attempt.State != AttemptState.Completed) reasons.Add("attempt_not_completed");
        else if (attempt.SubtestCode is not ("writing" or "speaking")) reasons.Add("unsupported_subtest");

        return new
        {
            attemptId,
            canRequestReview = canRequest,
            canPurchaseExtras = true,
            availableCredits = wallet.CreditBalance,
            turnaroundOptions = new[]
            {
                new { id = "standard", label = "Standard", time = "48-72 hours", cost = 1, description = "Detailed written feedback within three business days." },
                new { id = "express", label = "Express", time = "24 hours", cost = 2, description = "Priority turnaround within 24 hours." }
            },
            eligibilityReasonCodes = reasons
        };
    }

    public async Task<object> CreateReviewRequestAsync(string userId, ReviewRequestCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        // Higher retry count tolerates cross-worker wallet contention during
        // parallel CI Playwright shards. The helper retries on the resulting
        // 409 wallet_update_conflict as well.
        for (var attemptNumber = 0; attemptNumber < 4; attemptNumber++)
        {
            try
            {
                return await CreateReviewRequestCoreAsync(userId, request, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber < 3)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw ApiException.Conflict(
            "wallet_update_conflict",
            "Your review credits were updated at the same time as this request. Please try again.");
    }

    private async Task<object> CreateReviewRequestCoreAsync(string userId, ReviewRequestCreateRequest request, CancellationToken cancellationToken)
    {
        var user = await EnsureLearnerProfileAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await GetIdempotentResponseAsync("review-request", request.IdempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, request.AttemptId, cancellationToken);
        var turnaroundOption = (request.TurnaroundOption ?? string.Empty).Trim().ToLowerInvariant();
        if (turnaroundOption is not ("standard" or "express"))
        {
            throw ApiException.Validation(
                "invalid_turnaround_option",
                "Choose a valid tutor review turnaround option.",
                [new ApiFieldError("turnaroundOption", "invalid", "Turnaround must be standard or express.")]);
        }

        var paymentSource = (request.PaymentSource ?? string.Empty).Trim().ToLowerInvariant();
        if (paymentSource != "credits")
        {
            throw ApiException.Validation(
                "unsupported_payment_source",
                "Tutor review requests currently use review credits.",
                [new ApiFieldError("paymentSource", "unsupported", "Only review credits are supported for tutor review requests.")]);
        }

        var cost = turnaroundOption == "express" ? 2 : 1;

        if (attempt.SubtestCode is not ("writing" or "speaking") || attempt.State != AttemptState.Completed)
        {
            throw ApiException.Validation(
                "review_not_eligible",
                "This attempt is not eligible for tutor review.",
                [new ApiFieldError("attemptId", "not_eligible", "Only completed writing and speaking attempts can be sent for tutor review.")]);
        }

        if (!string.Equals(request.Subtest, attempt.SubtestCode, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "review_subtest_mismatch",
                "The review request subtest does not match the attempt.",
                [new ApiFieldError("subtest", "mismatch", "Use the same subtest as the selected attempt.")]);
        }

        var existingActiveReview = await db.ReviewRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AttemptId == request.AttemptId
                                      && x.State != ReviewRequestState.Completed
                                      && x.State != ReviewRequestState.Failed
                                      && x.State != ReviewRequestState.Cancelled,
                cancellationToken);
        if (existingActiveReview is not null)
        {
            throw ApiException.Conflict(
                "review_already_active",
                "This attempt already has an active tutor review request.");
        }

        var now = DateTimeOffset.UtcNow;
        var reviewId = $"review-{Guid.NewGuid():N}";

        // Spec ("Decrement writing_assessments_remaining on submission"): a learner with
        // bundled/add-on writing assessments consumes one (free) instead of paying when
        // submitting a WRITING letter for expert review. Resolve a tracked, eligible
        // subscription so the decrement participates in the same SaveChanges + outer
        // DbUpdateConcurrencyException retry that protects the wallet path from a racing
        // double-spend. The idempotency cache above already short-circuits a retried
        // submit before reaching this point, so a retry never double-decrements.
        var isWritingReview = string.Equals(attempt.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase);
        var prepaidSubscription = isWritingReview
            ? await ResolveEligibleWritingSubscriptionAsync(userId, now, cancellationToken)
            : null;

        string effectivePaymentSource;
        decimal priceSnapshot;
        object eligibilitySnapshot;

        if (prepaidSubscription is not null)
        {
            // Prepaid (entitlement) path: consume one bundled writing assessment and skip
            // the wallet load, wallet debit, and WalletTransaction entirely. PriceSnapshot
            // is 0 so downstream credits-only refund/reporting logic (which all gate on
            // PaymentSource == "credits" AND PriceSnapshot > 0) correctly treats this as a
            // non-credit, nothing-to-refund request.
            prepaidSubscription.WritingAssessmentsRemaining = Math.Max(0, prepaidSubscription.WritingAssessmentsRemaining - 1);
            effectivePaymentSource = "entitlement";
            priceSnapshot = 0m;
            eligibilitySnapshot = new
            {
                canRequestReview = true,
                paymentSource = "entitlement",
                writingAssessmentsRemaining = prepaidSubscription.WritingAssessmentsRemaining,
                entitlementSubscriptionId = prepaidSubscription.Id
            };
        }
        else
        {
            // Wallet (credits) path — unchanged.
            var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
            if (wallet.CreditBalance < cost)
            {
                throw ApiException.Validation(
                    "insufficient_credits",
                    "You do not have enough review credits for this request.",
                    [new ApiFieldError("paymentSource", "insufficient_credits", "Buy more credits or choose a different payment flow.")]);
            }

            wallet.CreditBalance -= cost;
            wallet.LastUpdatedAt = now;
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                TransactionType = "review_deduction",
                Amount = -cost,
                BalanceAfter = wallet.CreditBalance,
                ReferenceType = "review",
                ReferenceId = reviewId,
                Description = $"Tutor review request for {attempt.SubtestCode} attempt {attempt.Id}.",
                CreatedBy = userId,
                CreatedAt = now
            });
            effectivePaymentSource = paymentSource;
            priceSnapshot = cost;
            eligibilitySnapshot = new { canRequestReview = true, availableCredits = wallet.CreditBalance };
        }

        db.Entry(user).Property(x => x.AccountStatus).IsModified = true;

        var review = new ReviewRequest
        {
            Id = reviewId,
            AttemptId = request.AttemptId,
            SubtestCode = request.Subtest,
            State = ReviewRequestState.Queued,
            TurnaroundOption = turnaroundOption,
            FocusAreasJson = JsonSupport.Serialize(request.FocusAreas),
            LearnerNotes = request.LearnerNotes ?? string.Empty,
            PaymentSource = effectivePaymentSource,
            PriceSnapshot = priceSnapshot,
            CreatedAt = now,
            EligibilitySnapshotJson = JsonSupport.Serialize(eligibilitySnapshot)
        };

        db.ReviewRequests.Add(review);

        await RecordEventAsync(userId, "review_requested", new { reviewRequestId = review.Id, attemptId = review.AttemptId, subtest = review.SubtestCode, turnaroundOption = review.TurnaroundOption }, cancellationToken);
        LogAudit(userId, "Created", "ReviewRequest", review.Id, prepaidSubscription is not null
            ? $"Tutor review requested for {review.SubtestCode} attempt {review.AttemptId}, funded by writing-assessment entitlement (subscription {prepaidSubscription.Id})"
            : $"Tutor review requested for {review.SubtestCode} attempt {review.AttemptId}, cost={cost} credits");
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync("review-request", request.IdempotencyKey, new { reviewRequestId = review.Id }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReviewRequested,
            userId,
            "review_request",
            review.Id,
            review.CreatedAt.UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["attemptId"] = review.AttemptId,
                ["reviewRequestId"] = review.Id,
                ["subtest"] = review.SubtestCode,
                ["message"] = prepaidSubscription is not null
                    ? "Your tutor review request is queued. One bundled writing assessment was used."
                    : $"Your tutor review request is queued. {cost} review credit{(cost == 1 ? string.Empty : "s")} used."
            },
            cancellationToken);
        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminReviewOpsAction,
            "review_request",
            review.Id,
            review.CreatedAt.UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["reviewRequestId"] = review.Id,
                ["attemptId"] = review.AttemptId,
                ["message"] = $"Learner {user.DisplayName} requested a {review.SubtestCode} tutor review with {review.TurnaroundOption} turnaround."
            },
            cancellationToken);
        return await GetReviewRequestAsync(userId, review.Id, cancellationToken);
    }

    /// <summary>
    /// Resolves a <b>tracked</b> <see cref="Subscription"/> for the learner that has at least
    /// one bundled/add-on writing assessment remaining and is currently usable, so a writing
    /// expert-review submission can consume it instead of charging wallet credits.
    /// Mirrors <c>PrivateSpeakingService.ResolveEligibleSpeakingSubscriptionAsync</c> but is
    /// intentionally self-contained (a direct query rather than a dependency on the entitlement
    /// resolver) to keep this change's footprint minimal. The result is tracked (NOT
    /// AsNoTracking) because the caller mutates <see cref="Subscription.WritingAssessmentsRemaining"/>.
    /// Eligibility: status Active or Trial, not expired (<see cref="Subscription.ExpiresAt"/>
    /// null or in the future), and <see cref="Subscription.WritingAssessmentsRemaining"/> &gt; 0.
    /// When several rows qualify, the one expiring soonest is consumed first (nulls last).
    /// </summary>
    private async Task<Subscription?> ResolveEligibleWritingSubscriptionAsync(
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = await db.Subscriptions
            .Where(subscription => subscription.UserId == userId
                && subscription.WritingAssessmentsRemaining > 0)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(subscription => subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial)
            .Where(subscription => subscription.ExpiresAt is null || subscription.ExpiresAt > now)
            .OrderBy(subscription => subscription.ExpiresAt == null)
            .ThenBy(subscription => subscription.ExpiresAt)
            .FirstOrDefault();
    }

    public async Task<object> GetReviewRequestAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await GetReviewRequestOwnedByUserAsync(userId, reviewRequestId, cancellationToken);
        return new
        {
            reviewRequestId = review.Id,
            attemptId = review.AttemptId,
            subtest = review.SubtestCode,
            state = ToReviewRequestState(review.State),
            turnaroundOption = review.TurnaroundOption,
            focusAreas = JsonSupport.Deserialize<List<string>>(review.FocusAreasJson, []),
            learnerNotes = review.LearnerNotes,
            paymentSource = review.PaymentSource,
            priceSnapshot = review.PriceSnapshot,
            createdAt = review.CreatedAt,
            completedAt = review.CompletedAt,
            eligibilitySnapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(review.EligibilitySnapshotJson, new Dictionary<string, object?>())
        };
    }

    public object GetReviewOptions() => new
    {
        items = new[]
        {
            new { id = "standard", label = "Standard Review", turnaround = "48-72 hours", price = 1, currency = "credit", description = "Detailed tutor review with criterion-level notes." },
            new { id = "express", label = "Express Review", turnaround = "24 hours", price = 2, currency = "credit", description = "Priority tutor review returned within a day." }
        }
    };
}
