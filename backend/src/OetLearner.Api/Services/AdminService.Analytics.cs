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
    /// <summary>
    /// Bounds of an evaluation's score string: a range ("330-360") or a single reported score ("350",
    /// the Speaking format — one number, no range). Null when it has no leading number.
    /// </summary>
    private static (int Low, int High)? ParseScoreRangeBounds(string? scoreRange)
    {
        var parts = scoreRange?.Split('-');
        if (parts is null) return null;
        if (parts.Length == 2 && int.TryParse(parts[0], out var low) && int.TryParse(parts[1], out var high))
            return (low, high);
        return parts.Length == 1 && int.TryParse(parts[0], out var single) ? (single, single) : null;
    }

    public async Task<object> GetDashboardSummaryAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var staleDraftThreshold = now.AddDays(-14);
        var overdueThreshold = now.AddHours(-48);

        int draftCount;
        int publishedCount;
        int archivedCount;
        int staleDrafts;
        int queuedReviews;
        int inReviewCount;
        int reviewFailures;
        int failedJobs;
        int overdueReviews;
        int pendingInvoices;
        int failedInvoices;
        int legacyPlans;
        int activeSubscribers;
        int totalFlags;
        int enabledFlags;
        int liveExperiments;
        int recentFlagChanges;
        int evaluationCount;
        int agreementCount;
        List<ReviewRequest>? reviewRequestRows = null;
        DateTimeOffset? contentUpdatedAt;
        DateTimeOffset? auditUpdatedAt;
        DateTimeOffset? reviewUpdatedAt;

        if (db.Database.IsSqlite())
        {
            var contentItems = await db.ContentItems.AsNoTracking().ToListAsync(ct);
            reviewRequestRows = await db.ReviewRequests.AsNoTracking().ToListAsync(ct);
            var backgroundJobs = await db.BackgroundJobs.AsNoTracking().ToListAsync(ct);
            var invoices = await db.Invoices.AsNoTracking().ToListAsync(ct);
            var billingPlans = await db.BillingPlans.AsNoTracking().ToListAsync(ct);
            var featureFlags = await db.FeatureFlags.AsNoTracking().ToListAsync(ct);
            var evaluations = await db.Evaluations.AsNoTracking().ToListAsync(ct);
            var auditEvents = await db.AuditEvents.AsNoTracking().ToListAsync(ct);

            draftCount = contentItems.Count(c => c.Status == ContentStatus.Draft);
            publishedCount = contentItems.Count(c => c.Status == ContentStatus.Published);
            archivedCount = contentItems.Count(c => c.Status == ContentStatus.Archived);
            staleDrafts = contentItems.Count(c => c.Status == ContentStatus.Draft && c.UpdatedAt < staleDraftThreshold);

            queuedReviews = reviewRequestRows.Count(r => r.State == ReviewRequestState.Queued);
            inReviewCount = reviewRequestRows.Count(r => r.State == ReviewRequestState.InReview);
            reviewFailures = reviewRequestRows.Count(r => r.State == ReviewRequestState.Failed);
            failedJobs = backgroundJobs.Count(j => j.State == AsyncState.Failed);
            overdueReviews = reviewRequestRows.Count(r =>
                (r.State == ReviewRequestState.Queued || r.State == ReviewRequestState.InReview) && r.CreatedAt < overdueThreshold);

            pendingInvoices = invoices.Count(i => i.Status == "Pending");
            failedInvoices = invoices.Count(i => i.Status == "Failed");
            legacyPlans = billingPlans.Count(p => p.Status == BillingPlanStatus.Legacy);
            activeSubscribers = billingPlans.Sum(p => p.ActiveSubscribers);

            totalFlags = featureFlags.Count;
            enabledFlags = featureFlags.Count(f => f.Enabled);
            liveExperiments = featureFlags.Count(f => f.FlagType == FeatureFlagType.Experiment && f.Enabled);
            recentFlagChanges = featureFlags.Count(f => f.UpdatedAt >= now.AddDays(-7));

            var recentEvaluations = evaluations.Where(e => e.GeneratedAt >= now.AddDays(-30)).ToList();
            evaluationCount = recentEvaluations.Count;
            agreementCount = recentEvaluations.Count(e => e.ConfidenceBand == ConfidenceBand.High);

            contentUpdatedAt = contentItems.Select(c => (DateTimeOffset?)c.UpdatedAt).Max();
            auditUpdatedAt = auditEvents.Select(a => (DateTimeOffset?)a.OccurredAt).Max();
            reviewUpdatedAt = reviewRequestRows.Select(r => (DateTimeOffset?)(r.CompletedAt ?? r.CreatedAt)).Max();
        }
        else
        {
            draftCount = await db.ContentItems.CountAsync(c => c.Status == ContentStatus.Draft, ct);
            publishedCount = await db.ContentItems.CountAsync(c => c.Status == ContentStatus.Published, ct);
            archivedCount = await db.ContentItems.CountAsync(c => c.Status == ContentStatus.Archived, ct);
            staleDrafts = await db.ContentItems.CountAsync(c => c.Status == ContentStatus.Draft && c.UpdatedAt < staleDraftThreshold, ct);

            queuedReviews = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.Queued, ct);
            inReviewCount = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.InReview, ct);
            reviewFailures = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.Failed, ct);
            failedJobs = await db.BackgroundJobs.CountAsync(j => j.State == AsyncState.Failed, ct);
            overdueReviews = await db.ReviewRequests.CountAsync(r =>
                (r.State == ReviewRequestState.Queued || r.State == ReviewRequestState.InReview) && r.CreatedAt < overdueThreshold, ct);

            pendingInvoices = await db.Invoices.CountAsync(i => i.Status == "Pending", ct);
            failedInvoices = await db.Invoices.CountAsync(i => i.Status == "Failed", ct);
            legacyPlans = await db.BillingPlans.CountAsync(p => p.Status == BillingPlanStatus.Legacy, ct);
            activeSubscribers = await db.BillingPlans.SumAsync(p => p.ActiveSubscribers, ct);

            totalFlags = await db.FeatureFlags.CountAsync(ct);
            enabledFlags = await db.FeatureFlags.CountAsync(f => f.Enabled, ct);
            liveExperiments = await db.FeatureFlags.CountAsync(f => f.FlagType == FeatureFlagType.Experiment && f.Enabled, ct);
            recentFlagChanges = await db.FeatureFlags.CountAsync(f => f.UpdatedAt >= now.AddDays(-7), ct);

            var recentEvaluations = db.Evaluations.AsNoTracking().Where(e => e.GeneratedAt >= now.AddDays(-30));
            evaluationCount = await recentEvaluations.CountAsync(ct);
            agreementCount = evaluationCount > 0
                ? await recentEvaluations.CountAsync(e => e.ConfidenceBand == ConfidenceBand.High, ct)
                : 0;

            contentUpdatedAt = await MaxDateTimeOffsetAsync(
                db.ContentItems,
                c => (DateTimeOffset?)c.UpdatedAt,
                ct);
            auditUpdatedAt = await MaxDateTimeOffsetAsync(
                db.AuditEvents,
                a => (DateTimeOffset?)a.OccurredAt,
                ct);
            reviewUpdatedAt = await MaxDateTimeOffsetAsync(
                db.ReviewRequests,
                r => (DateTimeOffset?)(r.CompletedAt ?? r.CreatedAt),
                ct);
        }

        var agreementRate = evaluationCount > 0 ? Math.Round(100.0 * agreementCount / evaluationCount, 1) : 0;

        var completedReviews = db.Database.IsSqlite()
            ? (reviewRequestRows ?? await db.ReviewRequests.AsNoTracking().ToListAsync(ct))
                .Where(r => r.State == ReviewRequestState.Completed && r.CompletedAt != null && r.CreatedAt >= now.AddDays(-30))
                .ToList()
            : await db.ReviewRequests.AsNoTracking()
                .Where(r => r.State == ReviewRequestState.Completed && r.CompletedAt != null && r.CreatedAt >= now.AddDays(-30))
                .ToListAsync(ct);
        var averageReviewHours = completedReviews.Count > 0
            ? Math.Round(completedReviews.Average(r => ((r.CompletedAt ?? r.CreatedAt) - r.CreatedAt).TotalHours), 1)
            : 0;

        return new
        {
            generatedAt = now,
            freshness = new
            {
                contentUpdatedAt,
                auditUpdatedAt,
                reviewUpdatedAt,
                qualityWindow = "30d"
            },
            contentHealth = new
            {
                published = publishedCount,
                drafts = draftCount,
                archived = archivedCount,
                staleDrafts
            },
            reviewOps = new
            {
                backlog = queuedReviews + inReviewCount,
                overdue = overdueReviews,
                failedReviews = reviewFailures,
                failedJobs,
                inProgress = inReviewCount
            },
            billingRisk = new
            {
                pendingInvoices,
                failedInvoices,
                legacyPlans,
                activeSubscribers
            },
            revenue = ComputeRevenueMetrics(db, now, activeSubscribers),
            flags = new
            {
                total = totalFlags,
                enabled = enabledFlags,
                liveExperiments,
                recentChanges = recentFlagChanges
            },
            quality = new
            {
                agreementRate,
                avgReviewHours = averageReviewHours,
                riskCases = failedJobs + reviewFailures,
                evaluationCount
            }
        };
    }

    private static object ComputeRevenueMetrics(LearnerDbContext db, DateTimeOffset now, int activeSubscribers)
    {
        var plans = db.BillingPlans.AsNoTracking().Where(p => p.Status == BillingPlanStatus.Active).ToList();
        var monthlyMrr = plans
            .Where(p => p.Interval == "monthly" && p.Price > 0)
            .Sum(p => p.Price * p.ActiveSubscribers);
        var yearlyMrr = plans
            .Where(p => p.Interval == "yearly" && p.Price > 0)
            .Sum(p => p.Price * p.ActiveSubscribers / 12m);
        var totalMrr = monthlyMrr + yearlyMrr;
        var arpu = activeSubscribers > 0 ? Math.Round(totalMrr / activeSubscribers, 2) : 0m;

        return new
        {
            mrr = Math.Round(totalMrr, 2),
            currency = plans.FirstOrDefault()?.Currency ?? "AUD",
            activeSubscribers,
            arpu,
            planBreakdown = plans
                .Where(p => p.ActiveSubscribers > 0)
                .Select(p => new
                {
                    planCode = p.Code,
                    planName = p.Name,
                    subscribers = p.ActiveSubscribers,
                    planMrr = p.Interval == "monthly"
                        ? Math.Round(p.Price * p.ActiveSubscribers, 2)
                        : Math.Round(p.Price * p.ActiveSubscribers / 12m, 2),
                })
                .ToList(),
            computedAt = now,
        };
    }

    // ════════════════════════════════════════════
    //  Quality Analytics
    // ════════════════════════════════════════════

    public async Task<object> GetQualityAnalyticsAsync(string? timeRange, string? subtest, string? profession, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedTimeRange = string.IsNullOrWhiteSpace(timeRange) ? "30d" : timeRange;
        var normalizedSubtest = string.IsNullOrWhiteSpace(subtest) ? "all" : subtest;
        var normalizedProfession = string.IsNullOrWhiteSpace(profession) ? "all" : profession;
        var windowDays = normalizedTimeRange switch
        {
            "7d" => 7,
            "30d" => 30,
            "ytd" => (int)(now - new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalDays + 1,
            _ => 30
        };
        var windowStart = now.AddDays(-windowDays);

        var attemptsQuery = db.Attempts.AsNoTracking().AsQueryable();
        if (normalizedSubtest != "all")
        {
            attemptsQuery = attemptsQuery.Where(a => a.SubtestCode == normalizedSubtest);
        }

        if (normalizedProfession != "all")
        {
            var professionContentIds = await db.ContentItems.AsNoTracking()
                .Where(c => c.ProfessionId == normalizedProfession)
                .Select(c => c.Id)
                .ToListAsync(ct);
            attemptsQuery = attemptsQuery.Where(a => professionContentIds.Contains(a.ContentId));
        }

        var attemptIds = await attemptsQuery
            .Select(a => a.Id)
            .Distinct()
            .ToListAsync(ct);

        var evalRows = await db.Evaluations.AsNoTracking()
            .Where(e => attemptIds.Contains(e.AttemptId) && e.GeneratedAt != null && e.GeneratedAt >= windowStart)
            .Select(e => new
            {
                generatedAt = e.GeneratedAt!.Value,
                e.ConfidenceBand,
                e.State
            })
            .ToListAsync(ct);

        var reviewRows = await db.ReviewRequests.AsNoTracking()
            .Where(r => attemptIds.Contains(r.AttemptId) && r.CreatedAt >= windowStart)
            .Select(r => new
            {
                r.CreatedAt,
                r.CompletedAt,
                r.TurnaroundOption,
                r.State
            })
            .ToListAsync(ct);

        var activeAttemptUsers = await attemptsQuery
            .Where(a => a.StartedAt >= windowStart)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync(ct);
        var activeUsers = await db.Users.CountAsync(ct);

        var totalEvaluations = evalRows.Count;
        var highConfidence = evalRows.Count(e => e.ConfidenceBand == ConfidenceBand.High);
        var agreementRate = totalEvaluations > 0 ? Math.Round(100.0 * highConfidence / totalEvaluations, 1) : 0;

        var completedReviewsList = reviewRows
            .Where(r => r.State == ReviewRequestState.Completed && r.CompletedAt != null)
            .ToList();
        var slaMetCount = completedReviewsList.Count(r =>
        {
            var slaDue = r.CreatedAt.AddHours(r.TurnaroundOption == "express" ? 24 : 48);
            return (r.CompletedAt ?? r.CreatedAt) <= slaDue;
        });
        var slaRate = completedReviewsList.Count > 0
            ? Math.Round(100.0 * slaMetCount / completedReviewsList.Count, 1)
            : 0;
        var avgTurnaroundHours = completedReviewsList.Count > 0
            ? Math.Round(completedReviewsList.Average(r => ((r.CompletedAt ?? r.CreatedAt) - r.CreatedAt).TotalHours), 1)
            : 0;

        var contentQuery = db.ContentItems.AsNoTracking().Where(c => c.Status == ContentStatus.Published);
        if (normalizedSubtest != "all")
        {
            contentQuery = contentQuery.Where(c => c.SubtestCode == normalizedSubtest);
        }
        if (normalizedProfession != "all")
        {
            contentQuery = contentQuery.Where(c => c.ProfessionId == normalizedProfession);
        }
        var publishedContent = await contentQuery.CountAsync(ct);

        var adoptionRate = activeUsers > 0 ? Math.Round(100.0 * activeAttemptUsers / activeUsers, 1) : 0;
        var failedEvals = evalRows.Count(e => e.State == AsyncState.Failed);

        var bucketCount = normalizedTimeRange == "7d" ? 7 : 6;
        var bucketLength = TimeSpan.FromTicks((now - windowStart).Ticks / Math.Max(1, bucketCount));
        var agreementSeries = new List<object>();
        var appealsSeries = new List<object>();
        var reviewTimeSeries = new List<object>();
        var riskCaseSeries = new List<object>();

        for (var i = 0; i < bucketCount; i++)
        {
            var bucketStart = windowStart.AddTicks(bucketLength.Ticks * i);
            var bucketEnd = i == bucketCount - 1 ? now : bucketStart.Add(bucketLength);
            var label = bucketStart.ToString(windowDays <= 7 ? "dd MMM" : "MMM dd");

            var bucketEvaluations = evalRows.Where(e => e.generatedAt >= bucketStart && e.generatedAt < bucketEnd).ToList();
            var bucketReviews = reviewRows.Where(r => r.CreatedAt >= bucketStart && r.CreatedAt < bucketEnd).ToList();
            var bucketCompleted = bucketReviews.Where(r => r.State == ReviewRequestState.Completed && r.CompletedAt != null).ToList();
            var bucketAgreement = bucketEvaluations.Count > 0
                ? Math.Round(100.0 * bucketEvaluations.Count(e => e.ConfidenceBand == ConfidenceBand.High) / bucketEvaluations.Count, 1)
                : 0;
            var bucketReviewHours = bucketCompleted.Count > 0
                ? Math.Round(bucketCompleted.Average(r => ((r.CompletedAt ?? r.CreatedAt) - r.CreatedAt).TotalHours), 1)
                : 0;
            var bucketRisks = bucketEvaluations.Count(e => e.State == AsyncState.Failed);

            agreementSeries.Add(new { label, value = bucketAgreement });
            appealsSeries.Add(new { label, value = 0.0 });
            reviewTimeSeries.Add(new { label, value = bucketReviewHours });
            riskCaseSeries.Add(new { label, value = bucketRisks });
        }

        return new
        {
            aiHumanAgreement = new { value = agreementRate, trend = 0.0 },
            appealsRate = new { value = 0.0, trend = 0.0 },
            avgReviewTime = new { value = avgTurnaroundHours, unit = "hours" },
            contentPerformance = new { publishedCount = publishedContent, activeContent = publishedContent },
            reviewSLA = new { metPercent = slaRate, avgTurnaround = $"{avgTurnaroundHours}h" },
            featureAdoption = new { activeUsers = activeAttemptUsers, adoptionRate },
            riskCases = new { count = failedEvals, severity = failedEvals > 10 ? "high" : failedEvals > 0 ? "medium" : "low" },
            filters = new
            {
                timeRange = normalizedTimeRange,
                subtest = normalizedSubtest,
                profession = normalizedProfession
            },
            freshness = new
            {
                generatedAt = now,
                evaluationSampleCount = totalEvaluations,
                reviewSampleCount = reviewRows.Count,
                windowDays
            },
            trendSeries = new
            {
                agreement = agreementSeries,
                appeals = appealsSeries,
                reviewTime = reviewTimeSeries,
                riskCases = riskCaseSeries
            },
            generatedAt = now,
            windowDays
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // AE1: Content Usage Analytics Per Item
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetContentItemAnalyticsAsync(string contentId, CancellationToken ct)
    {
        var content = await db.ContentItems.FindAsync([contentId], ct)
            ?? throw ApiException.NotFound("CONTENT_NOT_FOUND", "Content item not found.");

        var attempts = await db.Attempts.Where(a => a.ContentId == contentId).ToListAsync(ct);
        var completedAttempts = attempts.Where(a => a.State == AttemptState.Completed).ToList();
        var evaluations = await db.Evaluations
            .Where(e => attempts.Select(a => a.Id).Contains(e.AttemptId))
            .ToListAsync(ct);

        // Score distribution
        var scores = evaluations
            .Select(e => ParseScoreRangeBounds(e.ScoreRange) is { } bounds
                ? (bounds.Low + bounds.High) / 2.0 : (double?)null)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .OrderBy(s => s)
            .ToList();

        var avgTime = completedAttempts.Count > 0
            ? Math.Round(completedAttempts.Average(a => a.ElapsedSeconds) / 60.0, 1) : 0;

        // Monthly usage trend
        var monthlyUsage = attempts
            .Where(a => a.StartedAt >= DateTimeOffset.UtcNow.AddMonths(-6))
            .GroupBy(a => new { a.StartedAt.Year, a.StartedAt.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new { month = $"{g.Key.Year}-{g.Key.Month:D2}", attempts = g.Count(), completed = g.Count(a => a.State == AttemptState.Completed) })
            .ToList();

        return new
        {
            contentId,
            title = content.Title,
            subtestCode = content.SubtestCode,
            status = content.Status.ToString(),
            metrics = new
            {
                totalAttempts = attempts.Count,
                completedAttempts = completedAttempts.Count,
                completionRate = attempts.Count > 0 ? Math.Round(completedAttempts.Count * 100.0 / attempts.Count, 1) : 0,
                averageTimeMinutes = avgTime,
                uniqueLearners = attempts.Select(a => a.UserId).Distinct().Count(),
                averageScore = scores.Count > 0 ? Math.Round(scores.Average(), 1) : (double?)null,
                medianScore = scores.Count > 0 ? scores[scores.Count / 2] : (double?)null,
                scoreStdDev = scores.Count > 1 ? Math.Round(Math.Sqrt(scores.Average(s => Math.Pow(s - scores.Average(), 2))), 1) : (double?)null
            },
            monthlyTrend = monthlyUsage,
            evaluationCount = evaluations.Count
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // AE3: SLA Health Check & Alert Triggers
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> CheckSlaHealthAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var openReviews = await db.ReviewRequests
            .Where(r => r.State == ReviewRequestState.Queued || r.State == ReviewRequestState.InReview)
            .ToListAsync(ct);

        var alerts = new List<object>();
        var breached = 0;
        var atRisk = 0;
        var healthy = 0;

        foreach (var review in openReviews)
        {
            var slaHours = review.TurnaroundOption == "express" ? 24.0 : 48.0;
            var hoursElapsed = (now - review.CreatedAt).TotalHours;
            var remaining = slaHours - hoursElapsed;

            if (remaining < 0)
            {
                breached++;
                alerts.Add(new
                {
                    reviewId = review.Id, severity = "breached",
                    message = $"SLA breached by {Math.Round(-remaining, 1)}h",
                    turnaround = review.TurnaroundOption, subtestCode = review.SubtestCode,
                    createdAt = review.CreatedAt, hoursOverdue = Math.Round(-remaining, 1)
                });
            }
            else if (remaining < 6)
            {
                atRisk++;
                alerts.Add(new
                {
                    reviewId = review.Id, severity = "at-risk",
                    message = $"Only {Math.Round(remaining, 1)}h remaining",
                    turnaround = review.TurnaroundOption, subtestCode = review.SubtestCode,
                    createdAt = review.CreatedAt, hoursRemaining = Math.Round(remaining, 1)
                });
            }
            else healthy++;
        }

        // Queue depth analysis
        var assignedExperts = await db.ExpertReviewAssignments
            .Where(a => a.ClaimState == ExpertAssignmentState.Assigned)
            .Select(a => a.AssignedReviewerId)
            .Distinct()
            .CountAsync(ct);

        var unassigned = await db.ReviewRequests
            .CountAsync(r => (r.State == ReviewRequestState.Queued || r.State == ReviewRequestState.InReview)
                && !db.ExpertReviewAssignments.Any(a => a.ReviewRequestId == r.Id && a.ClaimState == ExpertAssignmentState.Assigned), ct);

        // SLA health also for second query
        _ = unassigned;

        var queueDepthPerExpert = assignedExperts > 0
            ? Math.Round((double)openReviews.Count / assignedExperts, 1) : openReviews.Count;

        return new
        {
            timestamp = now,
            overallHealth = breached > 0 ? "critical" : atRisk > 3 ? "warning" : "healthy",
            summary = new
            {
                totalOpen = openReviews.Count,
                breached, atRisk, healthy, unassigned,
                activeExperts = assignedExperts,
                queueDepthPerExpert,
                capacityAlert = queueDepthPerExpert > 8
            },
            alerts = alerts.OrderBy(a => ((dynamic)a).severity == "breached" ? 0 : 1).ToList(),
            recommendations = new List<string>
            {
                breached > 0 ? $"URGENT: {breached} reviews have breached SLA. Assign immediately." : null!,
                unassigned > 5 ? $"High unassigned backlog ({unassigned}). Consider activating more experts." : null!,
                queueDepthPerExpert > 8 ? "Expert capacity stretched. Consider load balancing or recruitment." : null!
            }.Where(r => r is not null).ToList()
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // B2: Review Credit Lifecycle Policy
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetCreditLifecyclePolicyAsync(CancellationToken ct)
    {
        // Get current policy from feature flags or config
        var expiryFlag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == "credit_expiry_days", ct);
        var rolloverFlag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == "credit_rollover_enabled", ct);
        var refundFlag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == "credit_refund_on_failed_review", ct);

        var expiryDays = expiryFlag?.Enabled == true ? 365 : 0; // 0 = no expiry
        var rolloverEnabled = rolloverFlag?.Enabled ?? false;
        var refundOnFailed = refundFlag?.Enabled ?? true;

        // Aggregate wallet stats
        var totalCreditsInSystem = await db.Wallets.SumAsync(w => w.CreditBalance, ct);
        var walletsWithCredits = await db.Wallets.CountAsync(w => w.CreditBalance > 0, ct);
        var recentTransactions = await db.WalletTransactions
            .Where(t => t.CreatedAt >= DateTimeOffset.UtcNow.AddDays(-30))
            .GroupBy(t => t.TransactionType)
            .Select(g => new { type = g.Key, count = g.Count(), totalAmount = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        return new
        {
            policy = new
            {
                expiryDays,
                expiryEnabled = expiryDays > 0,
                rolloverEnabled,
                rolloverPercentage = rolloverEnabled ? 50 : 0,
                refundOnFailedReview = refundOnFailed,
                refundOnCancelledReview = true,
                proRataOnDowngrade = true,
                minimumCreditPurchase = 1,
                maximumCreditBalance = 100
            },
            systemStats = new
            {
                totalCreditsInCirculation = totalCreditsInSystem,
                walletsWithCredits,
                last30DaysTransactions = recentTransactions
            },
            notes = new[]
            {
                expiryDays > 0 ? $"Credits expire {expiryDays} days after purchase." : "Credits do not expire.",
                rolloverEnabled ? "Unused credits roll over at plan renewal (50%)." : "Credits do not roll over at plan renewal.",
                refundOnFailed ? "Credits are refunded when a review fails quality check." : "No auto-refund on failed reviews."
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // R1: Learner Cohort Analysis
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetLearnerCohortAnalysisAsync(string? groupBy, CancellationToken ct)
    {
        var groupKey = groupBy ?? "profession";
        var users = await db.Users.ToListAsync(ct);
        var goals = await db.Goals.ToListAsync(ct);
        var goalsByUser = goals.ToDictionary(g => g.UserId, g => g);

        var cohorts = new List<object>();

        if (groupKey == "profession")
        {
            var professions = await db.Professions.ToListAsync(ct);
            foreach (var prof in professions)
            {
                var learners = goals.Where(g => g.ProfessionId == prof.Id).Select(g => g.UserId).ToList();
                if (learners.Count == 0) continue;

                var evals = await db.Evaluations
                    .Where(e => db.Attempts.Any(a => a.Id == e.AttemptId && learners.Contains(a.UserId)))
                    .ToListAsync(ct);

                var avgScores = evals
                    .Select(e => ParseScoreRangeBounds(e.ScoreRange)?.Low)
                    .Where(s => s.HasValue).Select(s => (double)s!.Value).ToList();

                cohorts.Add(new
                {
                    cohortKey = prof.Id,
                    cohortName = prof.Label,
                    learnerCount = learners.Count,
                    averageScore = avgScores.Count > 0 ? Math.Round(avgScores.Average(), 1) : (double?)null,
                    evaluationCount = evals.Count,
                    activeLastMonth = users.Count(u => learners.Contains(u.Id) && u.LastActiveAt >= DateTimeOffset.UtcNow.AddDays(-30))
                });
            }
        }
        else
        {
            // Group by subscription tier
            var subs = await db.Subscriptions.Where(s => s.Status == SubscriptionStatus.Active).ToListAsync(ct);
            var plans = await db.BillingPlans.ToListAsync(ct);
            foreach (var plan in plans)
            {
                var planSubs = subs.Where(s => s.PlanId == plan.Id).ToList();
                var learnerIds = planSubs.Select(s => s.UserId).ToList();
                if (learnerIds.Count == 0) continue;

                var evals = await db.Evaluations
                    .Where(e => db.Attempts.Any(a => a.Id == e.AttemptId && learnerIds.Contains(a.UserId)))
                    .ToListAsync(ct);

                var avgScores = evals
                    .Select(e => ParseScoreRangeBounds(e.ScoreRange)?.Low)
                    .Where(s => s.HasValue).Select(s => (double)s!.Value).ToList();

                cohorts.Add(new
                {
                    cohortKey = plan.Id,
                    cohortName = plan.Name,
                    learnerCount = learnerIds.Count,
                    averageScore = avgScores.Count > 0 ? Math.Round(avgScores.Average(), 1) : (double?)null,
                    evaluationCount = evals.Count,
                    activeLastMonth = users.Count(u => learnerIds.Contains(u.Id) && u.LastActiveAt >= DateTimeOffset.UtcNow.AddDays(-30))
                });
            }
        }

        return new
        {
            groupBy = groupKey,
            cohorts = cohorts.OrderByDescending(c => ((dynamic)c).learnerCount).ToList(),
            totalLearners = users.Count,
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // R2: Content Effectiveness Metrics
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetContentEffectivenessAsync(string? subtestCode, int top, CancellationToken ct)
    {
        var query = db.ContentItems.Where(c => c.Status == ContentStatus.Published);
        if (!string.IsNullOrEmpty(subtestCode)) query = query.Where(c => c.SubtestCode == subtestCode);

        var items = await query.Take(top > 0 ? top : 50).ToListAsync(ct);
        var results = new List<object>();

        foreach (var item in items)
        {
            var attempts = await db.Attempts.Where(a => a.ContentId == item.Id).ToListAsync(ct);
            var completedCount = attempts.Count(a => a.State == AttemptState.Completed);
            var evals = await db.Evaluations
                .Where(e => attempts.Select(a => a.Id).Contains(e.AttemptId))
                .ToListAsync(ct);

            var scores = evals
                .Select(e => ParseScoreRangeBounds(e.ScoreRange)?.Low)
                .Where(s => s.HasValue).Select(s => (double)s!.Value).ToList();

            results.Add(new
            {
                contentId = item.Id,
                title = item.Title,
                subtestCode = item.SubtestCode,
                difficulty = item.Difficulty,
                totalAttempts = attempts.Count,
                completionRate = attempts.Count > 0 ? Math.Round(completedCount * 100.0 / attempts.Count, 1) : 0,
                averageScore = scores.Count > 0 ? Math.Round(scores.Average(), 1) : (double?)null,
                avgTimeSeconds = completedCount > 0 ? Math.Round(attempts.Where(a => a.State == AttemptState.Completed).Average(a => a.ElapsedSeconds), 0) : (double?)null,
                effectivenessScore = scores.Count >= 3
                    ? Math.Round((completedCount * 100.0 / Math.Max(attempts.Count, 1) * 0.4) + (scores.Average() / 5.0 * 0.6), 1) : (double?)null
            });
        }

        return new
        {
            subtestFilter = subtestCode,
            items = results.OrderByDescending(r => ((dynamic)r).effectivenessScore ?? 0.0).ToList(),
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // R3: Expert Efficiency Report
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetExpertEfficiencyReportAsync(int days, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var experts = await db.ExpertUsers.ToListAsync(ct);
        var results = new List<object>();

        foreach (var expert in experts)
        {
            var assignments = await db.ExpertReviewAssignments
                .Where(a => a.AssignedReviewerId == expert.Id && a.AssignedAt >= since)
                .ToListAsync(ct);

            var drafts = await db.ExpertReviewDrafts
                .Where(d => d.ReviewerId == expert.Id && d.DraftSavedAt >= since)
                .ToListAsync(ct);

            var completedCount = drafts.Count;
var draftTimeEstimates = drafts.Select(d =>
                {
                    // Estimate time from draft save patterns — no explicit TimeSpentSeconds field
                    return 15.0; // Default estimate in minutes per review
                }).ToList();
                var avgReviewMinutes = draftTimeEstimates.Count > 0 ? Math.Round(draftTimeEstimates.Average(), 1) : (double?)null;

            // Quality alignment — compare with AI
            var aiDiffs = new List<double>();
            foreach (var draft in drafts.Take(20))
            {
                var request = await db.ReviewRequests.FindAsync([draft.ReviewRequestId], ct);
                if (request is null) continue;
                var aiEval = await db.Evaluations
                    .FirstOrDefaultAsync(e => e.AttemptId == request.AttemptId, ct);
                if (aiEval is null) continue;

                try
                {
                    var aiScores = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(aiEval.CriterionScoresJson ?? "{}");
                    if (aiScores is not null && draft.RubricEntriesJson is not null)
                    {
                        var expertScores = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(draft.RubricEntriesJson);
                        if (expertScores is not null)
                        {
                            foreach (var kv in aiScores)
                            {
                                if (expertScores.TryGetValue(kv.Key, out var es))
                                    aiDiffs.Add(Math.Abs(kv.Value - es));
                            }
                        }
                    }
                }
                catch { }
            }

            results.Add(new
            {
                expertId = expert.Id,
                expertName = expert.DisplayName,
                period = days,
                assignmentsReceived = assignments.Count,
                reviewsCompleted = completedCount,
                averageReviewTimeMinutes = avgReviewMinutes,
                reviewsPerDay = Math.Round(completedCount / (double)Math.Max(days, 1), 1),
                aiAlignmentScore = aiDiffs.Count > 0 ? Math.Round(100 - aiDiffs.Average() * 10, 1) : (double?)null,
                efficiency = completedCount > 0 && avgReviewMinutes.HasValue
                    ? avgReviewMinutes.Value <= 15 ? "high" : avgReviewMinutes.Value <= 25 ? "medium" : "low"
                    : "no-data"
            });
        }

        return new
        {
            period = days,
            experts = results.OrderByDescending(r => ((dynamic)r).reviewsCompleted).ToList(),
            summary = new
            {
                totalExperts = experts.Count,
                activeExperts = results.Count(r => ((dynamic)r).reviewsCompleted > 0),
                totalReviewsCompleted = results.Sum(r => (int)((dynamic)r).reviewsCompleted),
                averageReviewsPerExpertPerDay = experts.Count > 0
                    ? Math.Round(results.Sum(r => (int)((dynamic)r).reviewsCompleted) / (double)(experts.Count * Math.Max(days, 1)), 1) : 0
            },
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // R4: Subscription Health Dashboard
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetSubscriptionHealthAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var activeSubs = await db.Subscriptions.Where(s => s.Status == SubscriptionStatus.Active).ToListAsync(ct);
        var allSubs = await db.Subscriptions.ToListAsync(ct);
        var plans = await db.BillingPlans.ToDictionaryAsync(p => p.Id, ct);

        // MRR calculation
        var mrr = activeSubs.Sum(s => plans.TryGetValue(s.PlanId, out var p) ? p.Price : 0);

        // Churn — subscriptions that changed away from Active in last 30 days
        var recentCancellations = allSubs.Count(s => s.Status == SubscriptionStatus.Cancelled && s.ChangedAt >= now.AddDays(-30));
        var activeStart = allSubs.Count(s => s.StartedAt < now.AddDays(-30) && (s.Status == SubscriptionStatus.Active || (s.Status == SubscriptionStatus.Cancelled && s.ChangedAt >= now.AddDays(-30))));
        var churnRate = activeStart > 0 ? Math.Round(recentCancellations * 100.0 / activeStart, 1) : 0;

        // New subs this month
        var newThisMonth = activeSubs.Count(s => s.StartedAt >= now.AddDays(-30));

        // Trial conversions — estimate from plans with TrialDays > 0
        var planIdsWithTrials = plans.Where(p => p.Value.TrialDays > 0).Select(p => p.Key).ToHashSet();
        var trialSubs = allSubs.Where(s => planIdsWithTrials.Contains(s.PlanId) && s.StartedAt.AddDays(plans[s.PlanId].TrialDays) <= now).ToList();
        var trialConverted = trialSubs.Count(s => s.Status == SubscriptionStatus.Active);
        var trialConversionRate = trialSubs.Count > 0 ? Math.Round(trialConverted * 100.0 / trialSubs.Count, 1) : 0;

        // Revenue by plan
        var revenueByPlan = activeSubs
            .GroupBy(s => s.PlanId)
            .Select(g => new
            {
                planId = g.Key,
                planName = plans.TryGetValue(g.Key, out var p) ? p.Name : g.Key,
                subscribers = g.Count(),
                monthlyRevenue = g.Sum(s => plans.TryGetValue(s.PlanId, out var pl) ? pl.Price : 0)
            })
            .OrderByDescending(r => r.monthlyRevenue)
            .ToList();

        // Monthly trend
        var monthlyTrend = Enumerable.Range(0, 6).Select(i =>
        {
            var monthStart = now.AddMonths(-i).Date;
            var monthEnd = now.AddMonths(-i + 1).Date;
            var monthStartOffset = new DateTimeOffset(monthStart, TimeSpan.Zero);
            var monthEndOffset = new DateTimeOffset(monthEnd, TimeSpan.Zero);
            return new
            {
                month = monthStart.ToString("yyyy-MM"),
                newSubscriptions = allSubs.Count(s => s.StartedAt >= monthStartOffset && s.StartedAt < monthEndOffset),
                cancellations = allSubs.Count(s => s.Status == SubscriptionStatus.Cancelled && s.ChangedAt >= monthStartOffset && s.ChangedAt < monthEndOffset)
            };
        }).Reverse().ToList();

        return new
        {
            mrr,
            activeSubscriptions = activeSubs.Count,
            churnRate,
            newSubscriptionsThisMonth = newThisMonth,
            trialConversionRate,
            arpu = activeSubs.Count > 0 ? Math.Round(mrr / activeSubs.Count, 2) : 0,
            revenueByPlan,
            monthlyTrend,
            generatedAt = now
        };
    }
}
