using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public partial class AdminService(
    LearnerDbContext db,
    EmailOtpService emailOtpService,
    IPasswordHasher<ApplicationUserAccount> passwordHasher,
    TimeProvider timeProvider,
    NotificationService notifications,
    OetLearner.Api.Services.Caching.IReferenceDataCache referenceCache)
{
    private const string ActiveUserStatus = "active";
    private const string SuspendedUserStatus = "suspended";
    private const string DeletedUserStatus = "deleted";

    // ════════════════════════════════════════════
    //  Transaction + Audit helpers
    // ════════════════════════════════════════════

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return null;
        if (db.Database.IsInMemory()) return null;
        return await db.Database.BeginTransactionAsync(ct);
    }

    private async Task CommitIfOwnedAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private async Task LogAuditAsync(string actorId, string actorName, string action,
        string resourceType, string? resourceId, string? details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Load the effective permission set for an admin user.</summary>
    private async Task<HashSet<string>> GetEffectivePermissionsAsync(string adminId, CancellationToken ct)
    {
        var grants = await db.AdminPermissionGrants
            .AsNoTracking()
            .Where(g => g.AdminUserId == adminId)
            .Select(g => g.Permission)
            .ToListAsync(ct);
        var perms = new HashSet<string>(grants, StringComparer.OrdinalIgnoreCase);
        // Backward compat: admins with no explicit grants are treated as system_admin
        if (perms.Count == 0)
            perms.Add(AdminPermissions.SystemAdmin);
        return perms;
    }

    private Task NotifyAdminsAsync(
        NotificationEventKey eventKey,
        string entityType,
        string entityId,
        string versionOrDateBucket,
        string message,
        CancellationToken ct)
        => notifications.CreateForAdminsAsync(
            eventKey,
            entityType,
            entityId,
            versionOrDateBucket,
            new Dictionary<string, object?>
            {
                ["message"] = message
            },
            ct);

    private sealed record AdminUserTarget(
        string Id,
        string Role,
        string Email,
        string Name,
        string Status,
        string? AuthAccountId,
        string? ProfessionId);

    private sealed record AdminUserListRow(
        string Id,
        string Name,
        string Email,
        string Role,
        string Status,
        string? AuthAccountId,
        DateTimeOffset? LastLogin);

    private static string NormalizeStoredUserStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is ActiveUserStatus or SuspendedUserStatus or DeletedUserStatus
            ? normalized
            : ActiveUserStatus;
    }

    private static string ResolveUserStatus(string? storedStatus, bool isDeleted)
        => isDeleted ? DeletedUserStatus : NormalizeStoredUserStatus(storedStatus);

    private async Task<AdminUserTarget> ResolveUserTargetAsync(string userId, CancellationToken ct)
    {
        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is not null)
        {
            var learnerAuthAccount = learner.AuthAccountId is not null
                ? await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == learner.AuthAccountId, ct)
                : null;
            return new AdminUserTarget(
                learner.Id,
                learner.Role,
                learner.Email,
                learner.DisplayName,
                ResolveUserStatus(learner.AccountStatus, learnerAuthAccount?.DeletedAt is not null),
                learner.AuthAccountId,
                learner.ActiveProfessionId);
        }

        var expert = await db.ExpertUsers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            var expertAuthAccount = expert.AuthAccountId is not null
                ? await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == expert.AuthAccountId, ct)
                : null;
            var specialties = JsonSupport.Deserialize(expert.SpecialtiesJson, Array.Empty<string>());
            return new AdminUserTarget(
                expert.Id,
                expert.Role,
                expert.Email,
                expert.DisplayName,
                ResolveUserStatus(expert.IsActive ? ActiveUserStatus : SuspendedUserStatus, expertAuthAccount?.DeletedAt is not null),
                expert.AuthAccountId,
                specialties.FirstOrDefault());
        }

        var adminAccount = await db.ApplicationUserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == userId && a.Role == ApplicationUserRoles.Admin, ct);
        if (adminAccount is not null)
        {
            return new AdminUserTarget(
                adminAccount.Id,
                adminAccount.Role,
                adminAccount.Email,
                adminAccount.Email,
                ResolveUserStatus(ActiveUserStatus, adminAccount.DeletedAt is not null),
                adminAccount.Id,
                null);
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    private static string GenerateAccountId(string role)
    {
        var value = $"auth_{role}_{Guid.NewGuid():N}";
        return value[..Math.Min(64, value.Length)];
    }

    private static string GenerateDomainId(string prefix)
    {
        var value = $"{prefix}-{Guid.NewGuid():N}";
        return value[..Math.Min(64, value.Length)];
    }

    private static string EscapeCsv(string? value)
    {
        var normalized = (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{normalized}\"";
    }

    private static BillingPlanStatus ParseBillingPlanStatus(string? status, BillingPlanStatus fallback = BillingPlanStatus.Active)
        => Enum.TryParse<BillingPlanStatus>(status, true, out var parsed) ? parsed : fallback;

    private static BillingAddOnStatus ParseBillingAddOnStatus(string? status, BillingAddOnStatus fallback = BillingAddOnStatus.Active)
        => Enum.TryParse<BillingAddOnStatus>(status, true, out var parsed) ? parsed : fallback;

    private static BillingCouponStatus ParseBillingCouponStatus(string? status, BillingCouponStatus fallback = BillingCouponStatus.Active)
        => Enum.TryParse<BillingCouponStatus>(status, true, out var parsed) ? parsed : fallback;

    private static BillingDiscountType ParseBillingDiscountType(string? status)
        => Enum.TryParse<BillingDiscountType>(status, true, out var parsed) ? parsed : BillingDiscountType.Percentage;

    private async Task<List<TItem>> ToOrderedListDescendingAsync<TItem, TKey>(
        IQueryable<TItem> query,
        Expression<Func<TItem, TKey>> orderBy,
        CancellationToken ct,
        int? skip = null,
        int? take = null)
    {
        if (!db.Database.IsSqlite())
        {
            IQueryable<TItem> orderedQuery = query.OrderByDescending(orderBy);
            if (skip is int skipCount)
            {
                orderedQuery = orderedQuery.Skip(skipCount);
            }

            if (take is int takeCount)
            {
                orderedQuery = orderedQuery.Take(takeCount);
            }

            return await orderedQuery.ToListAsync(ct);
        }

        IEnumerable<TItem> orderedItems = (await query.ToListAsync(ct))
            .OrderByDescending(orderBy.Compile());

        if (skip is int skipLimit)
        {
            orderedItems = orderedItems.Skip(skipLimit);
        }

        if (take is int takeLimit)
        {
            orderedItems = orderedItems.Take(takeLimit);
        }

        return orderedItems.ToList();
    }

    private async Task<DateTimeOffset?> MaxDateTimeOffsetAsync<TItem>(
        IQueryable<TItem> query,
        Expression<Func<TItem, DateTimeOffset?>> selector,
        CancellationToken ct)
    {
        if (!db.Database.IsSqlite())
        {
            return await query.MaxAsync(selector, ct);
        }

        return (await query.ToListAsync(ct))
            .Select(selector.Compile())
            .Max();
    }

    private static object MapBillingPlan(BillingPlan plan) => new
    {
        plan.Id,
        code = plan.Code,
        plan.Name,
        plan.Description,
        plan.Price,
        plan.Currency,
        plan.Interval,
        plan.DurationMonths,
        plan.IncludedCredits,
        plan.DisplayOrder,
        plan.IsVisible,
        plan.IsRenewable,
        plan.TrialDays,
        activeSubscribers = plan.ActiveSubscribers,
        status = plan.Status.ToString().ToLowerInvariant(),
        includedSubtests = JsonSupport.Deserialize<List<string>>(plan.IncludedSubtestsJson, []),
        entitlements = JsonSupport.Deserialize<Dictionary<string, object?>>(plan.EntitlementsJson, new Dictionary<string, object?>()),
        archivedAt = plan.ArchivedAt,
        plan.UpdatedAt,
        plan.CreatedAt
    };

    private static object MapBillingAddOn(BillingAddOn addOn) => new
    {
        addOn.Id,
        code = addOn.Code,
        addOn.Name,
        addOn.Description,
        addOn.Price,
        addOn.Currency,
        addOn.Interval,
        addOn.DurationDays,
        addOn.GrantCredits,
        addOn.DisplayOrder,
        addOn.IsRecurring,
        addOn.AppliesToAllPlans,
        addOn.IsStackable,
        addOn.QuantityStep,
        addOn.MaxQuantity,
        status = addOn.Status.ToString().ToLowerInvariant(),
        compatiblePlanCodes = JsonSupport.Deserialize<List<string>>(addOn.CompatiblePlanCodesJson, []),
        grantEntitlements = JsonSupport.Deserialize<Dictionary<string, object?>>(addOn.GrantEntitlementsJson, new Dictionary<string, object?>()),
        addOn.CreatedAt,
        addOn.UpdatedAt
    };

    private static object MapBillingCoupon(BillingCoupon coupon) => new
    {
        coupon.Id,
        code = coupon.Code,
        coupon.Name,
        coupon.Description,
        discountType = coupon.DiscountType.ToString().ToLowerInvariant(),
        coupon.DiscountValue,
        coupon.Currency,
        coupon.StartsAt,
        coupon.EndsAt,
        coupon.UsageLimitTotal,
        coupon.UsageLimitPerUser,
        coupon.MinimumSubtotal,
        coupon.IsStackable,
        status = coupon.Status.ToString().ToLowerInvariant(),
        applicablePlanCodes = JsonSupport.Deserialize<List<string>>(coupon.ApplicablePlanCodesJson, []),
        applicableAddOnCodes = JsonSupport.Deserialize<List<string>>(coupon.ApplicableAddOnCodesJson, []),
        coupon.RedemptionCount,
        coupon.Notes,
        coupon.CreatedAt,
        coupon.UpdatedAt
    };

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

    // ════════════════════════════════════════════
    //  Content Management
    // ════════════════════════════════════════════

    public async Task<object> GetContentListAsync(string? contentType, string? profession,
        string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(contentType))
            query = query.Where(c => c.ContentType == contentType);
        if (!string.IsNullOrWhiteSpace(profession))
            query = query.Where(c => c.ProfessionId == profession);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsedStatus = Enum.Parse<ContentStatus>(status, true);
            query = query.Where(c => c.Status == parsedStatus);
        }
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Title.Contains(search) || c.Id.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await ToOrderedListDescendingAsync(
            query,
            c => c.UpdatedAt,
            ct,
            skip: (page - 1) * pageSize,
            take: pageSize);

        var result = items.Select(c => new
        {
            c.Id,
            c.Title,
            type = c.ContentType,
            profession = c.ProfessionId,
            status = c.Status.ToString().ToLowerInvariant(),
            sourceType = c.SourceType,
            qaStatus = c.QaStatus,
            updatedAt = c.UpdatedAt,
            author = c.CreatedBy ?? "System",
            revisionCount = db.ContentRevisions.Count(r => r.ContentItemId == c.Id)
        });

        return new { total, page, pageSize, items = result };
    }

    public async Task<object> GetContentDetailAsync(string contentId, CancellationToken ct)
    {
        var c = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        var revisions = await db.ContentRevisions
            .Where(r => r.ContentItemId == contentId)
            .OrderByDescending(r => r.RevisionNumber)
            .Take(5)
            .Select(r => new { r.Id, r.RevisionNumber, r.State, r.ChangeNote, r.CreatedBy, r.CreatedAt })
            .ToListAsync(ct);

        return new
        {
            c.Id,
            c.Title,
            type = c.ContentType,
            subtestCode = c.SubtestCode,
            professionId = c.ProfessionId,
            difficulty = c.Difficulty,
            estimatedDurationMinutes = c.EstimatedDurationMinutes,
            status = c.Status.ToString().ToLowerInvariant(),
            sourceType = c.SourceType,
            qaStatus = c.QaStatus,
            detail = c.DetailJson,
            modelAnswer = c.ModelAnswerJson,
            criteriaFocus = c.CriteriaFocusJson,
            createdBy = c.CreatedBy,
            updatedAt = c.UpdatedAt,
            publishedAt = c.PublishedAt,
            revisions
        };
    }

    public async Task<object> CreateContentAsync(string adminId, string adminName,
        AdminContentCreateRequest request, CancellationToken ct)
    {
        var id = $"CNT-{Guid.NewGuid():N}"[..12];
        var now = DateTimeOffset.UtcNow;

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var item = new ContentItem
        {
            Id = id,
            ContentType = request.ContentType,
            SubtestCode = request.SubtestCode,
            ProfessionId = request.ProfessionId,
            Title = request.Title,
            Difficulty = request.Difficulty ?? "medium",
            EstimatedDurationMinutes = request.EstimatedDurationMinutes ?? 45,
            CriteriaFocusJson = request.CriteriaFocus ?? "[]",
            PublishedRevisionId = "",
            Status = ContentStatus.Draft,
            DetailJson = JsonSupport.Serialize(new { description = request.Description, caseNotes = request.CaseNotes }),
            ModelAnswerJson = request.ModelAnswer ?? "{}",
            CreatedBy = adminName,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ContentItems.Add(item);

        db.ContentRevisions.Add(new ContentRevision
        {
            Id = $"REV-{Guid.NewGuid():N}"[..12],
            ContentItemId = id,
            RevisionNumber = 1,
            State = "draft",
            ChangeNote = "Initial creation",
            SnapshotJson = JsonSupport.Serialize(new { item.Title, item.ContentType, item.SubtestCode, item.DetailJson, item.ModelAnswerJson, item.CriteriaFocusJson, item.ProfessionId, item.Difficulty }),
            CreatedBy = adminName,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "Content", id, $"Created content: {request.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id, status = "draft" };
    }

    public async Task<object> UpdateContentAsync(string adminId, string adminName,
        string contentId, AdminContentUpdateRequest request, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        if (request.Title is not null) item.Title = request.Title;
        if (request.ContentType is not null) item.ContentType = request.ContentType;
        if (request.SubtestCode is not null) item.SubtestCode = request.SubtestCode;
        if (request.ProfessionId is not null) item.ProfessionId = request.ProfessionId;
        if (request.Difficulty is not null) item.Difficulty = request.Difficulty;
        if (request.EstimatedDurationMinutes.HasValue) item.EstimatedDurationMinutes = request.EstimatedDurationMinutes.Value;
        if (request.ModelAnswer is not null) item.ModelAnswerJson = request.ModelAnswer;
        if (request.CriteriaFocus is not null) item.CriteriaFocusJson = request.CriteriaFocus;
        if (request.Description is not null || request.CaseNotes is not null)
        {
            item.DetailJson = JsonSupport.Serialize(new { description = request.Description, caseNotes = request.CaseNotes });
        }
        item.UpdatedAt = DateTimeOffset.UtcNow;

        var revCount = await db.ContentRevisions.CountAsync(r => r.ContentItemId == contentId, ct);
        db.ContentRevisions.Add(new ContentRevision
        {
            Id = $"REV-{Guid.NewGuid():N}"[..12],
            ContentItemId = contentId,
            RevisionNumber = revCount + 1,
            State = item.Status.ToString().ToLowerInvariant(),
            ChangeNote = request.ChangeNote ?? "Updated",
            SnapshotJson = JsonSupport.Serialize(new { item.Title, item.ContentType, item.SubtestCode, item.DetailJson, item.ModelAnswerJson, item.CriteriaFocusJson, item.ProfessionId, item.Difficulty }),
            CreatedBy = adminName,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "Content", contentId, $"Updated content: {item.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id = contentId, status = item.Status.ToString().ToLowerInvariant() };
    }

    public async Task<object> PublishContentAsync(string adminId, string adminName, string contentId, CancellationToken ct)
    {
        // Direct publish is now gated — only admins with content:publish or content:publisher_approval can bypass workflow
        var perms = await GetEffectivePermissionsAsync(adminId, ct);
        if (!perms.Contains(AdminPermissions.ContentPublish) && !perms.Contains(AdminPermissions.ContentPublisherApproval) && !perms.Contains(AdminPermissions.SystemAdmin))
            throw ApiException.Forbidden("insufficient_permission", "Only publishers can directly publish content. Use the multi-stage approval workflow instead.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        item.Status = ContentStatus.Published;
        item.PublishedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Published", "Content", contentId, $"Published (direct bypass): {item.Title}", ct);
        return new { id = contentId, status = "published" };
    }

    public async Task<object> ArchiveContentAsync(string adminId, string adminName, string contentId, CancellationToken ct)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        item.Status = ContentStatus.Archived;
        item.ArchivedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Archived", "Content", contentId, $"Archived: {item.Title}", ct);
        return new { id = contentId, status = "archived" };
    }

    public async Task<object> GetContentRevisionsAsync(string contentId, CancellationToken ct)
    {
        if (!await db.ContentItems.AnyAsync(x => x.Id == contentId, ct))
            throw ApiException.NotFound("content_not_found", "Content item not found.");

        var revisions = await db.ContentRevisions
            .Where(r => r.ContentItemId == contentId)
            .OrderByDescending(r => r.RevisionNumber)
            .Select(r => new
            {
                r.Id,
                contentId = r.ContentItemId,
                date = r.CreatedAt,
                author = r.CreatedBy,
                state = r.State,
                note = r.ChangeNote
            }).ToListAsync(ct);

        return revisions;
    }

    public async Task<object> RestoreRevisionAsync(string adminId, string adminName,
        string contentId, string revisionId, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var revision = await db.ContentRevisions.FirstOrDefaultAsync(
            r => r.Id == revisionId && r.ContentItemId == contentId, ct)
            ?? throw ApiException.NotFound("revision_not_found", "Revision not found.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        // Restore content fields from the snapshot
        var snapshot = JsonSupport.Deserialize(revision.SnapshotJson, new Dictionary<string, object?>());
        if (snapshot.TryGetValue("Title", out var title) && title is not null)
            item.Title = title.ToString()!;
        if (snapshot.TryGetValue("ContentType", out var contentType) && contentType is not null)
            item.ContentType = contentType.ToString()!;
        if (snapshot.TryGetValue("SubtestCode", out var subtestCode) && subtestCode is not null)
            item.SubtestCode = subtestCode.ToString()!;
        if (snapshot.TryGetValue("DetailJson", out var detailJson) && detailJson is not null)
            item.DetailJson = detailJson.ToString()!;
        if (snapshot.TryGetValue("ModelAnswerJson", out var modelAnswer) && modelAnswer is not null)
            item.ModelAnswerJson = modelAnswer.ToString()!;
        if (snapshot.TryGetValue("CriteriaFocusJson", out var criteriaFocus) && criteriaFocus is not null)
            item.CriteriaFocusJson = criteriaFocus.ToString()!;
        if (snapshot.TryGetValue("ProfessionId", out var professionId) && professionId is not null)
            item.ProfessionId = professionId.ToString();
        if (snapshot.TryGetValue("Difficulty", out var difficulty) && difficulty is not null)
            item.Difficulty = difficulty.ToString()!;

        var revCount = await db.ContentRevisions.CountAsync(r => r.ContentItemId == contentId, ct);
        db.ContentRevisions.Add(new ContentRevision
        {
            Id = $"REV-{Guid.NewGuid():N}"[..12],
            ContentItemId = contentId,
            RevisionNumber = revCount + 1,
            State = "restored",
            ChangeNote = $"Restored from revision {revision.RevisionNumber}",
            SnapshotJson = revision.SnapshotJson,
            CreatedBy = adminName,
            CreatedAt = DateTimeOffset.UtcNow
        });

        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Restored", "Content", contentId,
            $"Restored to revision {revision.RevisionNumber}", ct);
        await CommitIfOwnedAsync(tx, ct);
        return new { id = contentId, restoredRevision = revision.RevisionNumber };
    }

    // ════════════════════════════════════════════
    //  Taxonomy (Professions)
    // ════════════════════════════════════════════

    public async Task<object> GetTaxonomyListAsync(string? type, string? status, CancellationToken ct)
    {
        var query = db.Professions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(p => p.Status == status);

        var professions = await query.OrderBy(p => p.SortOrder).ToListAsync(ct);
        var nodes = professions.Select(p => new
        {
            p.Id,
            label = p.Label,
            slug = p.Code,
            type = "profession",
            status = p.Status,
            contentCount = db.ContentItems.Count(c => c.ProfessionId == p.Id)
        });

        return nodes;
    }

    public async Task<object> CreateTaxonomyNodeAsync(string adminId, string adminName,
        AdminTaxonomyCreateRequest request, CancellationToken ct)
    {
        var id = request.Code;
        var maxSort = await db.Professions.AnyAsync(ct)
            ? await db.Professions.MaxAsync(p => p.SortOrder, ct)
            : 0;

        db.Professions.Add(new ProfessionReference
        {
            Id = id,
            Code = request.Code,
            Label = request.Label,
            Status = "active",
            SortOrder = maxSort + 1
        });
        await db.SaveChangesAsync(ct);
        referenceCache.InvalidateProfessions();

        await LogAuditAsync(adminId, adminName, "Created", "Taxonomy", id, $"Created profession: {request.Label}", ct);
        return new { id, status = "active" };
    }

    public async Task<object> UpdateTaxonomyNodeAsync(string adminId, string adminName,
        string professionId, AdminTaxonomyUpdateRequest request, CancellationToken ct)
    {
        var p = await db.Professions.FirstOrDefaultAsync(x => x.Id == professionId, ct)
                ?? throw ApiException.NotFound("profession_not_found", "Profession not found.");

        if (request.Label is not null) p.Label = request.Label;
        if (request.Code is not null) p.Code = request.Code;
        if (request.Status is not null) p.Status = request.Status;
        await db.SaveChangesAsync(ct);
        referenceCache.InvalidateProfessions();

        await LogAuditAsync(adminId, adminName, "Updated", "Taxonomy", professionId, $"Updated profession: {p.Label}", ct);
        return new { id = professionId, status = p.Status };
    }

    public async Task<object> ArchiveTaxonomyNodeAsync(string adminId, string adminName,
        string professionId, CancellationToken ct)
    {
        var p = await db.Professions.FirstOrDefaultAsync(x => x.Id == professionId, ct)
                ?? throw ApiException.NotFound("profession_not_found", "Profession not found.");

        p.Status = "archived";
        await db.SaveChangesAsync(ct);
        referenceCache.InvalidateProfessions();

        await LogAuditAsync(adminId, adminName, "Archived", "Taxonomy", professionId, $"Archived profession: {p.Label}", ct);
        return new { id = professionId, status = "archived" };
    }

    // ════════════════════════════════════════════
    //  Criteria / Rubric Mapping
    // ════════════════════════════════════════════

    public async Task<object> GetCriteriaListAsync(string? subtestCode, string? status, CancellationToken ct)
    {
        var query = db.Criteria.AsQueryable();

        if (!string.IsNullOrWhiteSpace(subtestCode))
            query = query.Where(c => c.SubtestCode == subtestCode);

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(c => c.Status == status);

        var items = await query.OrderBy(c => c.SortOrder)
            .Select(c => new
            {
                c.Id,
                name = c.Label,
                type = c.SubtestCode,
                weight = c.SortOrder,
                status = c.Status,
                description = c.Description
            }).ToListAsync(ct);

        return items;
    }

    public async Task<object> CreateCriterionAsync(string adminId, string adminName,
        AdminCriterionCreateRequest request, CancellationToken ct)
    {
        var id = $"CRI-{Guid.NewGuid():N}"[..10];
        var maxSort = await db.Criteria.Where(c => c.SubtestCode == request.SubtestCode).AnyAsync(ct)
            ? await db.Criteria.Where(c => c.SubtestCode == request.SubtestCode).MaxAsync(c => c.SortOrder, ct)
            : 0;

        db.Criteria.Add(new CriterionReference
        {
            Id = id,
            SubtestCode = request.SubtestCode,
            Code = request.Name.ToLowerInvariant().Replace(' ', '_'),
            Label = request.Name,
            Description = request.Description ?? "",
            Status = "active",
            SortOrder = maxSort + 1
        });
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "Criterion", id, $"Created criterion: {request.Name}", ct);
        return new { id, status = "active" };
    }

    public async Task<object> UpdateCriterionAsync(string adminId, string adminName,
        string criterionId, AdminCriterionUpdateRequest request, CancellationToken ct)
    {
        var c = await db.Criteria.FirstOrDefaultAsync(x => x.Id == criterionId, ct)
                ?? throw ApiException.NotFound("criterion_not_found", "Criterion not found.");

        if (request.Name is not null) c.Label = request.Name;
        if (request.Description is not null) c.Description = request.Description;
        if (request.Weight.HasValue) c.SortOrder = request.Weight.Value;
        if (request.Status is not null) c.Status = request.Status;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Updated", "Criterion", criterionId, $"Updated criterion: {c.Label}", ct);
        return new { id = criterionId, status = c.Status };
    }

    // ════════════════════════════════════════════
    //  AI Evaluation Config
    // ════════════════════════════════════════════

    public async Task<object> GetAIConfigListAsync(string? status, CancellationToken ct)
    {
        var query = db.AIConfigVersions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = Enum.Parse<AIConfigStatus>(status, true);
            query = query.Where(a => a.Status == parsedStatus);
        }

        var configs = await query.OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var items = configs.Select(a => new
        {
            a.Id,
            model = a.Model,
            provider = a.Provider,
            taskType = a.TaskType,
            status = a.Status.ToString().ToLowerInvariant(),
            accuracy = a.Accuracy,
            confidenceThreshold = a.ConfidenceThreshold,
            routingRule = a.RoutingRule,
            experimentFlag = a.ExperimentFlag,
            promptLabel = a.PromptLabel
        });

        return items;
    }

    public async Task<object> CreateAIConfigAsync(string adminId, string adminName,
        AdminAIConfigCreateRequest request, CancellationToken ct)
    {
        var id = $"AIC-{Guid.NewGuid():N}"[..12];
        var createdAt = DateTimeOffset.UtcNow;
        var config = new AIConfigVersion
        {
            Id = id,
            Model = request.Model,
            Provider = request.Provider,
            TaskType = request.TaskType,
            Status = !string.IsNullOrWhiteSpace(request.Status)
                ? Enum.Parse<AIConfigStatus>(request.Status, true)
                : AIConfigStatus.Testing,
            Accuracy = request.Accuracy,
            ConfidenceThreshold = request.ConfidenceThreshold,
            RoutingRule = request.RoutingRule ?? "",
            ExperimentFlag = request.ExperimentFlag ?? "",
            PromptLabel = request.PromptLabel ?? "",
            CreatedBy = adminName,
            CreatedAt = createdAt
        };
        db.AIConfigVersions.Add(config);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "AIConfig", id, $"Created AI config: {request.Model}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminAiConfigChanged,
            "ai_config",
            id,
            createdAt.UtcDateTime.Ticks.ToString(),
            $"AI config {config.Model} for {config.TaskType} was created.",
            ct);
        return new { id, status = "testing" };
    }

    public async Task<object> UpdateAIConfigAsync(string adminId, string adminName,
        string configId, AdminAIConfigUpdateRequest request, CancellationToken ct)
    {
        var a = await db.AIConfigVersions.FirstOrDefaultAsync(x => x.Id == configId, ct)
                ?? throw ApiException.NotFound("ai_config_not_found", "AI config not found.");

        if (request.Model is not null) a.Model = request.Model;
        if (request.Provider is not null) a.Provider = request.Provider;
        if (request.TaskType is not null) a.TaskType = request.TaskType;
        if (request.Status is not null) a.Status = Enum.Parse<AIConfigStatus>(request.Status, true);
        if (request.Accuracy.HasValue) a.Accuracy = request.Accuracy.Value;
        if (request.ConfidenceThreshold.HasValue) a.ConfidenceThreshold = request.ConfidenceThreshold.Value;
        if (request.RoutingRule is not null) a.RoutingRule = request.RoutingRule;
        if (request.ExperimentFlag is not null) a.ExperimentFlag = request.ExperimentFlag;
        if (request.PromptLabel is not null) a.PromptLabel = request.PromptLabel;
        var updatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Updated", "AIConfig", configId, $"Updated AI config: {a.Model}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminAiConfigChanged,
            "ai_config",
            configId,
            updatedAt.UtcDateTime.Ticks.ToString(),
            $"AI config {a.Model} for {a.TaskType} was updated.",
            ct);
        return new { id = configId, status = a.Status.ToString().ToLowerInvariant() };
    }

    // ════════════════════════════════════════════
    //  Feature Flags
    // ════════════════════════════════════════════

    public async Task<object> GetFlagListAsync(string? flagType, CancellationToken ct)
    {
        var query = db.FeatureFlags.AsQueryable();

        if (!string.IsNullOrWhiteSpace(flagType) && flagType != "all")
        {
            var parsedType = Enum.Parse<FeatureFlagType>(flagType, true);
            query = query.Where(f => f.FlagType == parsedType);
        }

        var flags = await ToOrderedListDescendingAsync(query, f => f.UpdatedAt, ct);
        var items = flags.Select(f => new
        {
            f.Id,
            f.Name,
            f.Key,
            enabled = f.Enabled,
            type = f.FlagType.ToString().ToLowerInvariant(),
            rolloutPercentage = f.RolloutPercentage,
            description = f.Description,
            owner = f.Owner
        });

        return items;
    }

    public async Task<object> CreateFlagAsync(string adminId, string adminName,
        AdminFlagCreateRequest request, CancellationToken ct)
    {
        if (await db.FeatureFlags.AnyAsync(f => f.Key == request.Key, ct))
            throw ApiException.Conflict("flag_key_duplicate", "A flag with this key already exists.");

        var id = $"FLG-{Guid.NewGuid():N}"[..12];
        var now = DateTimeOffset.UtcNow;

        var flag = new FeatureFlag
        {
            Id = id,
            Name = request.Name,
            Key = request.Key,
            FlagType = !string.IsNullOrWhiteSpace(request.FlagType)
                ? Enum.Parse<FeatureFlagType>(request.FlagType, true)
                : FeatureFlagType.Release,
            Enabled = request.Enabled,
            RolloutPercentage = request.RolloutPercentage,
            Description = request.Description,
            Owner = request.Owner,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.FeatureFlags.Add(flag);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "Flag", id, $"Created flag: {request.Name}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminFeatureFlagChanged,
            "feature_flag",
            id,
            now.UtcDateTime.Ticks.ToString(),
            $"Feature flag {flag.Name} was created.",
            ct);
        return new { id, enabled = request.Enabled };
    }

    public async Task<object> UpdateFlagAsync(string adminId, string adminName,
        string flagId, AdminFlagUpdateRequest request, CancellationToken ct)
    {
        var f = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Id == flagId, ct)
                ?? throw ApiException.NotFound("flag_not_found", "Feature flag not found.");

        if (request.Name is not null) f.Name = request.Name;
        if (request.Key is not null) f.Key = request.Key;
        if (request.FlagType is not null) f.FlagType = Enum.Parse<FeatureFlagType>(request.FlagType, true);
        if (request.Enabled.HasValue) f.Enabled = request.Enabled.Value;
        if (request.RolloutPercentage.HasValue) f.RolloutPercentage = request.RolloutPercentage.Value;
        if (request.Description is not null) f.Description = request.Description;
        if (request.Owner is not null) f.Owner = request.Owner;
        f.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var action = request.Enabled.HasValue ? (request.Enabled.Value ? "Enabled" : "Disabled") : "Updated";
        await LogAuditAsync(adminId, adminName, action, "Flag", flagId, $"{action} flag: {f.Name}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminFeatureFlagChanged,
            "feature_flag",
            flagId,
            f.UpdatedAt.UtcDateTime.Ticks.ToString(),
            $"Feature flag {f.Name} was {action.ToLowerInvariant()}.",
            ct);
        return new { id = flagId, enabled = f.Enabled };
    }

    // ════════════════════════════════════════════
    //  Audit Logs
    // ════════════════════════════════════════════

    public async Task<object> GetAuditEventsAsync(string? action, string? actor, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.AuditEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(action) && action != "all")
            query = query.Where(e => e.Action == action);
        if (!string.IsNullOrWhiteSpace(actor) && actor != "all")
            query = query.Where(e => e.ActorName == actor || e.ActorId == actor);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => (e.Details != null && e.Details.Contains(search))
                                      || e.Action.Contains(search)
                                      || e.ActorName.Contains(search)
                                      || (e.ResourceId != null && e.ResourceId.Contains(search)));

        var total = await query.CountAsync(ct);
        var events = await ToOrderedListDescendingAsync(
            query,
            e => e.OccurredAt,
            ct,
            skip: (page - 1) * pageSize,
            take: pageSize);

        var items = events
            .Select(e => new
            {
                e.Id,
                timestamp = e.OccurredAt,
                actor = e.ActorName,
                action = e.Action,
                resource = e.ResourceId,
                details = e.Details
            })
            .ToList();

        return new { total, page, pageSize, items };
    }

    public async Task<(byte[] Bytes, string FileName)> ExportAuditEventsCsvAsync(
        string? action,
        string? actor,
        string? search,
        CancellationToken ct)
    {
        var query = db.AuditEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(action) && action != "all")
            query = query.Where(e => e.Action == action);
        if (!string.IsNullOrWhiteSpace(actor) && actor != "all")
            query = query.Where(e => e.ActorName == actor || e.ActorId == actor);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => (e.Details != null && e.Details.Contains(search))
                                      || e.Action.Contains(search)
                                      || e.ActorName.Contains(search)
                                      || (e.ResourceId != null && e.ResourceId.Contains(search)));

        var items = await ToOrderedListDescendingAsync(query, e => e.OccurredAt, ct, take: 5000);

        var builder = new StringBuilder();
        builder.AppendLine("Id,Timestamp,Actor,Action,ResourceType,ResourceId,Details");

        foreach (var item in items)
        {
            builder.AppendJoin(',',
                EscapeCsv(item.Id),
                EscapeCsv(item.OccurredAt.ToString("O")),
                EscapeCsv(item.ActorName),
                EscapeCsv(item.Action),
                EscapeCsv(item.ResourceType),
                EscapeCsv(item.ResourceId),
                EscapeCsv(item.Details));
            builder.AppendLine();
        }

        return (Encoding.UTF8.GetBytes(builder.ToString()), $"audit-logs-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    // ════════════════════════════════════════════
    //  User Ops
    // ════════════════════════════════════════════

    public async Task<object> GetUserListAsync(string? role, string? status, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);

        var rows = new List<AdminUserListRow>();

        rows.AddRange(await db.Users.AsNoTracking()
            .Select(u => new AdminUserListRow(
                u.Id,
                u.DisplayName,
                u.Email,
                u.Role,
                u.AccountStatus ?? ActiveUserStatus,
                u.AuthAccountId,
                u.LastActiveAt))
            .ToListAsync(ct));

        rows.AddRange(await db.ExpertUsers.AsNoTracking()
            .Select(e => new AdminUserListRow(
                e.Id,
                e.DisplayName,
                e.Email,
                e.Role,
                e.IsActive ? ActiveUserStatus : SuspendedUserStatus,
                e.AuthAccountId,
                e.CreatedAt))
            .ToListAsync(ct));

        rows.AddRange(await db.ApplicationUserAccounts.AsNoTracking()
            .Where(a => a.Role == ApplicationUserRoles.Admin)
            .Select(a => new AdminUserListRow(
                a.Id,
                "Admin Account",
                a.Email,
                a.Role,
                ActiveUserStatus,
                a.Id,
                a.LastLoginAt ?? a.UpdatedAt))
            .ToListAsync(ct));

        var deletedAuthAccountIds = await db.ApplicationUserAccounts.AsNoTracking()
            .Where(a => a.DeletedAt != null)
            .Select(a => a.Id)
            .ToListAsync(ct);
        var deletedLookup = deletedAuthAccountIds.ToHashSet(StringComparer.Ordinal);

        rows = rows.Select(row => new AdminUserListRow(
            row.Id,
            row.Name,
            row.Email,
            row.Role,
            ResolveUserStatus(row.Status, !string.IsNullOrWhiteSpace(row.AuthAccountId) && deletedLookup.Contains(row.AuthAccountId)),
            row.AuthAccountId,
            row.LastLogin)).ToList();

        IEnumerable<AdminUserListRow> filtered = rows;

        if (!string.IsNullOrWhiteSpace(role) && role != "all")
            filtered = filtered.Where(u => u.Role == role);
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            filtered = filtered.Where(u => u.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(u =>
                u.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || u.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
                || u.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var total = filtered.Count();
        var items = filtered
            .OrderByDescending(u => u.LastLogin ?? DateTimeOffset.MinValue)
            .Skip((page - 1) * clampedPageSize)
            .Take(clampedPageSize)
            .Select(u => new
            {
                id = u.Id,
                name = u.Name,
                email = u.Email,
                role = u.Role,
                status = u.Status,
                lastLogin = u.LastLogin
            })
            .ToList();

        return new { total, page, pageSize = clampedPageSize, items };
    }

    public async Task<object> GetUserDetailAsync(string userId, CancellationToken ct)
    {
        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is not null)
        {
            var authAccount = learner.AuthAccountId is not null
                ? await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == learner.AuthAccountId, ct)
                : null;
            var status = ResolveUserStatus(learner.AccountStatus, authAccount?.DeletedAt is not null);
            var attemptCount = await db.Attempts.CountAsync(a => a.UserId == userId, ct);
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
            return new
            {
                learner.Id,
                name = learner.DisplayName,
                learner.Email,
                role = learner.Role,
                status,
                lastLogin = authAccount?.LastLoginAt ?? learner.LastActiveAt,
                tasksCompleted = attemptCount,
                creditBalance = wallet?.CreditBalance ?? 0,
                profession = learner.ActiveProfessionId,
                authAccountId = learner.AuthAccountId,
                createdAt = learner.CreatedAt,
                availableActions = new
                {
                    canSuspend = status is not DeletedUserStatus,
                    canDelete = status is not DeletedUserStatus,
                    canRestore = status is DeletedUserStatus && !string.Equals(learner.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal),
                    canAdjustCredits = status is not DeletedUserStatus,
                    canTriggerPasswordReset = learner.AuthAccountId is not null && status is not DeletedUserStatus
                }
            };
        }

        var expert = await db.ExpertUsers.FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            var authAccount = expert.AuthAccountId is not null
                ? await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == expert.AuthAccountId, ct)
                : null;
            var status = ResolveUserStatus(expert.IsActive ? ActiveUserStatus : SuspendedUserStatus, authAccount?.DeletedAt is not null);
            var reviewCount = await db.ExpertReviewAssignments.CountAsync(a => a.AssignedReviewerId == userId, ct);
            return new
            {
                expert.Id,
                name = expert.DisplayName,
                expert.Email,
                role = expert.Role,
                status,
                lastLogin = authAccount?.LastLoginAt ?? expert.CreatedAt,
                tasksGraded = reviewCount,
                specialties = JsonSupport.Deserialize(expert.SpecialtiesJson, Array.Empty<string>()),
                authAccountId = expert.AuthAccountId,
                createdAt = expert.CreatedAt,
                availableActions = new
                {
                    canSuspend = status is not DeletedUserStatus,
                    canDelete = status is not DeletedUserStatus,
                    canRestore = status is DeletedUserStatus && !string.Equals(expert.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal),
                    canAdjustCredits = false,
                    canTriggerPasswordReset = expert.AuthAccountId is not null && status is not DeletedUserStatus
                }
            };
        }

        var adminAccount = await db.ApplicationUserAccounts.FirstOrDefaultAsync(
            a => a.Id == userId && a.Role == ApplicationUserRoles.Admin,
            ct);
        if (adminAccount is not null)
        {
            var status = ResolveUserStatus(ActiveUserStatus, adminAccount.DeletedAt is not null);
            return new
            {
                adminAccount.Id,
                name = "Admin Account",
                adminAccount.Email,
                role = adminAccount.Role,
                status,
                lastLogin = adminAccount.LastLoginAt ?? adminAccount.UpdatedAt,
                authAccountId = adminAccount.Id,
                createdAt = adminAccount.CreatedAt,
                availableActions = new
                {
                    canSuspend = false,
                    canDelete = false,
                    canRestore = false,
                    canAdjustCredits = false,
                    canTriggerPasswordReset = status is not DeletedUserStatus
                }
            };
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    public async Task<object> InviteUserAsync(
        string adminId,
        string adminName,
        AdminUserInviteRequest request,
        CancellationToken ct)
    {
        var role = (request.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (role is not (ApplicationUserRoles.Learner or ApplicationUserRoles.Expert or ApplicationUserRoles.Admin))
        {
            throw ApiException.Validation("invalid_role", "Role must be learner, expert, or admin.");
        }

        var email = AuthEmailAddress.TrimAndValidateOrThrow(request.Email);
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.ApplicationUserAccounts.AnyAsync(a => a.NormalizedEmail == normalizedEmail, ct))
        {
            throw ApiException.Conflict("email_already_exists", "An account with this email already exists.");
        }

        var displayName = string.IsNullOrWhiteSpace(request.Name) ? email : request.Name.Trim();
        var now = timeProvider.GetUtcNow();
        var authAccountId = GenerateAccountId(role);
        var tempPassword = $"Tmp!{Guid.NewGuid():N}";

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var authAccount = new ApplicationUserAccount
        {
            Id = authAccountId,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Role = role,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        authAccount.PasswordHash = passwordHasher.HashPassword(authAccount, tempPassword);
        db.ApplicationUserAccounts.Add(authAccount);

        string userId;
        switch (role)
        {
            case ApplicationUserRoles.Learner:
                userId = GenerateDomainId("usr");
                db.Users.Add(new LearnerUser
                {
                    Id = userId,
                    AuthAccountId = authAccountId,
                    Role = ApplicationUserRoles.Learner,
                    DisplayName = displayName,
                    Email = email,
                    Timezone = "UTC",
                    Locale = "en-AU",
                    ActiveProfessionId = request.ProfessionId,
                    CreatedAt = now,
                    LastActiveAt = now,
                    AccountStatus = "active"
                });
                db.Wallets.Add(new Wallet
                {
                    Id = GenerateDomainId("wallet"),
                    UserId = userId,
                    CreditBalance = 0,
                    LedgerSummaryJson = "[]",
                    LastUpdatedAt = now
                });
                break;
            case ApplicationUserRoles.Expert:
                userId = GenerateDomainId("expert");
                db.ExpertUsers.Add(new ExpertUser
                {
                    Id = userId,
                    AuthAccountId = authAccountId,
                    Role = ApplicationUserRoles.Expert,
                    DisplayName = displayName,
                    Email = email,
                    SpecialtiesJson = JsonSupport.Serialize(string.IsNullOrWhiteSpace(request.ProfessionId) ? Array.Empty<string>() : new[] { request.ProfessionId }),
                    Timezone = "UTC",
                    IsActive = true,
                    CreatedAt = now
                });
                break;
            default:
                userId = authAccountId;
                break;
        }

        await db.SaveChangesAsync(ct);

        var inviteChallenge = await emailOtpService.RequestPasswordResetOtpAsync(email, ct);
        await LogAuditAsync(adminId, adminName, "Invited User", "User", userId, $"Invited {role}: {email}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new
        {
            id = userId,
            email,
            role,
            invitation = new
            {
                purpose = inviteChallenge.Purpose,
                deliveryChannel = inviteChallenge.DeliveryChannel,
                destinationHint = inviteChallenge.DestinationHint,
                expiresAt = inviteChallenge.ExpiresAt,
                retryAfterSeconds = inviteChallenge.RetryAfterSeconds
            }
        };
    }

    // ── Bulk User Import ─────────────────────────────────

    private const int MaxImportFileBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxImportRows = 1000;
    private static readonly HashSet<string> ValidImportRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationUserRoles.Learner,
        ApplicationUserRoles.Expert,
        ApplicationUserRoles.Admin
    };
    private static readonly EmailAddressAttribute EmailValidator = new();

    public async Task<object> BulkImportUsersAsync(
        string adminId,
        string adminName,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw ApiException.Validation("file_required", "A CSV file is required.");
        }

        if (file.Length > MaxImportFileBytes)
        {
            throw ApiException.Validation("file_too_large", "CSV file must be under 5 MB.");
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!contentType.Contains("csv", StringComparison.Ordinal) && !contentType.Contains("text/plain", StringComparison.Ordinal)
            && !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("invalid_file_type", "Only CSV files are accepted.");
        }

        List<CsvUserRow> rows;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            rows = ParseCsvRows(await reader.ReadToEndAsync(ct));
        }

        if (rows.Count == 0)
        {
            throw ApiException.Validation("empty_csv", "CSV contains no data rows.");
        }

        if (rows.Count > MaxImportRows)
        {
            throw ApiException.Validation("too_many_rows", $"CSV must not exceed {MaxImportRows} rows. Found {rows.Count}.");
        }

        var errors = new List<object>();
        var created = 0;
        var skipped = 0;
        var now = timeProvider.GetUtcNow();

        // Pre-fetch existing emails for duplicate detection
        var importEmails = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .Select(r => r.Email!.Trim().ToUpperInvariant())
            .Where(e => e.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingEmailsList = await db.ApplicationUserAccounts
            .Where(a => importEmails.Contains(a.NormalizedEmail))
            .Select(a => a.NormalizedEmail)
            .ToListAsync(ct);
        var existingEmails = new HashSet<string>(existingEmailsList, StringComparer.OrdinalIgnoreCase);

        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2; // +2 because row 1 is header, data starts at 2

            // Validate email
            var rawEmail = (row.Email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawEmail) || !EmailValidator.IsValid(rawEmail))
            {
                errors.Add(new { row = rowNumber, email = rawEmail, error = "Invalid email format." });
                continue;
            }

            var normalizedEmail = rawEmail.ToUpperInvariant();

            // Skip duplicates within the CSV itself
            if (!seenEmails.Add(normalizedEmail))
            {
                skipped++;
                continue;
            }

            // Skip existing accounts
            if (existingEmails.Contains(normalizedEmail))
            {
                skipped++;
                continue;
            }

            // Validate role
            var role = (row.Role ?? string.Empty).Trim().ToLowerInvariant();
            if (!ValidImportRoles.Contains(role))
            {
                errors.Add(new { row = rowNumber, email = rawEmail, error = $"Invalid role '{row.Role}'. Must be learner, expert, or admin." });
                continue;
            }

            // Sanitize name fields
            var firstName = SanitizeField(row.FirstName, 100);
            var lastName = SanitizeField(row.LastName, 100);
            var displayName = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
                ? rawEmail
                : $"{firstName} {lastName}".Trim();
            var profession = SanitizeField(row.Profession, 100);

            var authAccountId = GenerateAccountId(role);
            var tempPassword = $"Tmp!{Guid.NewGuid():N}";

            var authAccount = new ApplicationUserAccount
            {
                Id = authAccountId,
                Email = rawEmail,
                NormalizedEmail = normalizedEmail,
                PasswordHash = string.Empty,
                Role = role,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            authAccount.PasswordHash = passwordHasher.HashPassword(authAccount, tempPassword);
            db.ApplicationUserAccounts.Add(authAccount);

            switch (role)
            {
                case ApplicationUserRoles.Learner:
                    var learnerId = GenerateDomainId("usr");
                    db.Users.Add(new LearnerUser
                    {
                        Id = learnerId,
                        AuthAccountId = authAccountId,
                        Role = ApplicationUserRoles.Learner,
                        DisplayName = displayName,
                        Email = rawEmail,
                        Timezone = "UTC",
                        Locale = "en-AU",
                        ActiveProfessionId = string.IsNullOrWhiteSpace(profession) ? null : profession,
                        CreatedAt = now,
                        LastActiveAt = now,
                        AccountStatus = "active"
                    });
                    db.Wallets.Add(new Wallet
                    {
                        Id = GenerateDomainId("wallet"),
                        UserId = learnerId,
                        CreditBalance = 0,
                        LedgerSummaryJson = "[]",
                        LastUpdatedAt = now
                    });
                    break;
                case ApplicationUserRoles.Expert:
                    db.ExpertUsers.Add(new ExpertUser
                    {
                        Id = GenerateDomainId("expert"),
                        AuthAccountId = authAccountId,
                        Role = ApplicationUserRoles.Expert,
                        DisplayName = displayName,
                        Email = rawEmail,
                        SpecialtiesJson = JsonSupport.Serialize(string.IsNullOrWhiteSpace(profession) ? Array.Empty<string>() : new[] { profession }),
                        Timezone = "UTC",
                        IsActive = true,
                        CreatedAt = now
                    });
                    break;
                default:
                    // Admin — no separate domain entity
                    break;
            }

            created++;
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Bulk Import Users", "User", "bulk",
            $"Imported {created} users, skipped {skipped}, errors {errors.Count}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new
        {
            total = rows.Count,
            created,
            skipped,
            errors
        };
    }

    private static List<CsvUserRow> ParseCsvRows(string csvContent)
    {
        var rows = new List<CsvUserRow>();
        using var reader = new StringReader(csvContent);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine)) return rows;

        var headers = ParseCsvLine(headerLine)
            .Select(h => h.Trim().ToLowerInvariant())
            .ToArray();

        var emailIdx = Array.IndexOf(headers, "email");
        var firstNameIdx = Array.IndexOf(headers, "firstname");
        var lastNameIdx = Array.IndexOf(headers, "lastname");
        var roleIdx = Array.IndexOf(headers, "role");
        var professionIdx = Array.IndexOf(headers, "profession");

        if (emailIdx < 0)
        {
            throw ApiException.Validation("missing_email_column", "CSV must have an 'email' column header.");
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line);

            rows.Add(new CsvUserRow
            {
                Email = GetField(fields, emailIdx),
                FirstName = GetField(fields, firstNameIdx),
                LastName = GetField(fields, lastNameIdx),
                Role = GetField(fields, roleIdx),
                Profession = GetField(fields, professionIdx),
            });
        }

        return rows;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static string? GetField(string[] fields, int index)
        => index >= 0 && index < fields.Length ? fields[index] : null;

    private static string SanitizeField(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // Strip control characters and trim
        var sanitized = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }

    private sealed class CsvUserRow
    {
        public string? Email { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? Role { get; init; }
        public string? Profession { get; init; }
    }

    public async Task<object> UpdateUserStatusAsync(string adminId, string adminName,
        string userId, AdminUserStatusRequest request, CancellationToken ct)
    {
        var requestedStatus = NormalizeUserStatus(request.Status);
        var target = await ResolveUserTargetAsync(userId, ct);

        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Restore the account before changing its status.");
        }

        var expert = await db.ExpertUsers.FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            expert.IsActive = requestedStatus == ActiveUserStatus;
            if (!expert.IsActive)
            {
                await RevokeRefreshTokensAsync(expert.AuthAccountId, ct);
            }

            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, requestedStatus == ActiveUserStatus ? "Reactivated User" : "Suspended User",
                "User", userId, $"Status changed to {requestedStatus}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
            await NotifyAdminsAsync(
                NotificationEventKey.AdminUserLifecycleAction,
                "user",
                userId,
                DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
                $"Expert {expert.DisplayName} status changed to {requestedStatus}.",
                ct);
            return new { id = userId, status = requestedStatus };
        }

        if (await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            var learner = await db.Users.FirstAsync(u => u.Id == userId, ct);
            learner.AccountStatus = requestedStatus;
            if (!string.Equals(requestedStatus, ActiveUserStatus, StringComparison.Ordinal))
            {
                await RevokeRefreshTokensAsync(learner.AuthAccountId, ct);
            }

            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, requestedStatus == ActiveUserStatus ? "Reactivated User" : "Suspended User",
                "User", userId, $"Status changed to {requestedStatus}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerAccountStatusChanged,
                learner.Id,
                "user",
                userId,
                DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
                new Dictionary<string, object?>
                {
                    ["message"] = $"Your account status changed to {requestedStatus}."
                },
                ct);
            await NotifyAdminsAsync(
                NotificationEventKey.AdminUserLifecycleAction,
                "user",
                userId,
                DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
                $"Learner {learner.DisplayName} status changed to {requestedStatus}.",
                ct);
            return new { id = userId, status = requestedStatus };
        }

        if (await db.ApplicationUserAccounts.AnyAsync(a => a.Id == userId && a.Role == ApplicationUserRoles.Admin, ct))
        {
            throw ApiException.Validation("admin_status_immutable",
                "Admin account suspension is not supported by the current account model.");
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    private static string NormalizeUserStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw ApiException.Validation("invalid_user_status", "Status is required.");
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is ActiveUserStatus or SuspendedUserStatus
            ? normalized
            : throw ApiException.Validation("invalid_user_status", "Status must be 'active' or 'suspended'.");
    }

    public async Task<object> DeleteUserAsync(
        string adminId,
        string adminName,
        string userId,
        AdminUserLifecycleRequest request,
        CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "This account is already deleted.");
        }

        if (target.Role == ApplicationUserRoles.Admin)
        {
            throw ApiException.Validation("admin_lifecycle_immutable", "Admin account deletion is not supported by the current account model.");
        }

        var now = timeProvider.GetUtcNow();
        if (target.Role == ApplicationUserRoles.Learner)
        {
            var learner = await db.Users.SingleAsync(u => u.Id == userId, ct);
            learner.AccountStatus = DeletedUserStatus;
            if (learner.AuthAccountId is not null)
            {
                var authAccount = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == learner.AuthAccountId, ct);
                authAccount.DeletedAt = now;
                authAccount.UpdatedAt = now;
                await RevokeRefreshTokensAsync(authAccount.Id, ct);
            }
        }
        else if (target.Role == ApplicationUserRoles.Expert)
        {
            var expert = await db.ExpertUsers.SingleAsync(e => e.Id == userId, ct);
            expert.IsActive = false;
            if (expert.AuthAccountId is not null)
            {
                var authAccount = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == expert.AuthAccountId, ct);
                authAccount.DeletedAt = now;
                authAccount.UpdatedAt = now;
                await RevokeRefreshTokensAsync(authAccount.Id, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(
            adminId,
            adminName,
            "Deleted User",
            "User",
            userId,
            $"Deleted {target.Role} account{(string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $": {request.Reason}")}",
            ct);

        return new { id = userId, status = DeletedUserStatus };
    }

    public async Task<object> RestoreUserAsync(
        string adminId,
        string adminName,
        string userId,
        AdminUserLifecycleRequest request,
        CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status != DeletedUserStatus)
        {
            throw ApiException.Validation("account_not_deleted", "Only deleted accounts can be restored.");
        }

        if (target.Role == ApplicationUserRoles.Admin)
        {
            throw ApiException.Validation("admin_lifecycle_immutable", "Admin account restoration is not supported by the current account model.");
        }

        var now = timeProvider.GetUtcNow();
        if (target.Role == ApplicationUserRoles.Learner)
        {
            var learner = await db.Users.SingleAsync(u => u.Id == userId, ct);
            learner.AccountStatus = ActiveUserStatus;
            if (learner.AuthAccountId is not null)
            {
                var authAccount = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == learner.AuthAccountId, ct);
                authAccount.DeletedAt = null;
                authAccount.UpdatedAt = now;
            }
        }
        else if (target.Role == ApplicationUserRoles.Expert)
        {
            var expert = await db.ExpertUsers.SingleAsync(e => e.Id == userId, ct);
            expert.IsActive = true;
            if (expert.AuthAccountId is not null)
            {
                var authAccount = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == expert.AuthAccountId, ct);
                authAccount.DeletedAt = null;
                authAccount.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(
            adminId,
            adminName,
            "Restored User",
            "User",
            userId,
            $"Restored {target.Role} account{(string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $": {request.Reason}")}",
            ct);

        return new { id = userId, status = ActiveUserStatus };
    }

    private async Task RevokeRefreshTokensAsync(string? authAccountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authAccountId))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var activeRefreshTokens = await db.RefreshTokenRecords
            .Where(token => token.ApplicationUserAccountId == authAccountId && token.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevokedAt = now;
        }
    }

    public async Task<object> AdjustUserCreditsAsync(string adminId, string adminName,
        string userId, AdminUserCreditsRequest request, CancellationToken ct)
    {
        for (var attemptNumber = 0; attemptNumber < 2; attemptNumber++)
        {
            try
            {
                return await AdjustUserCreditsCoreAsync(adminId, adminName, userId, request, ct);
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber == 0)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw ApiException.Conflict(
            "wallet_update_conflict",
            "The user's credit balance changed while the adjustment was being applied. Please retry.");
    }

    private async Task<object> AdjustUserCreditsCoreAsync(string adminId, string adminName,
        string userId, AdminUserCreditsRequest request, CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Deleted accounts cannot be adjusted.");
        }

        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is null)
        {
            throw ApiException.NotFound("user_not_found", "User not found.");
        }

        db.Entry(learner).Property(x => x.AccountStatus).IsModified = true;

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            wallet = new Wallet { Id = Guid.NewGuid().ToString(), UserId = userId, CreditBalance = 0, LastUpdatedAt = DateTimeOffset.UtcNow };
            db.Wallets.Add(wallet);
        }

        wallet.CreditBalance += request.Amount;
        if (wallet.CreditBalance < 0)
        {
            throw ApiException.Validation("insufficient_credits",
                "Credit adjustment would result in a negative balance.",
                [new ApiFieldError("amount", "insufficient", $"Current balance ({wallet.CreditBalance - request.Amount}) plus adjustment ({request.Amount}) would be negative.")]);
        }
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Credit Adjustment", "User", userId,
            $"Adjusted credits by {request.Amount}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
        return new { id = userId, newBalance = wallet.CreditBalance };
    }

    public async Task<object> TriggerUserPasswordResetAsync(
        string adminId,
        string adminName,
        string userId,
        CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Deleted accounts cannot receive password resets.");
        }

        if (string.IsNullOrWhiteSpace(target.AuthAccountId))
        {
            throw ApiException.Validation("password_reset_unavailable", "This user does not have a password-based sign-in account.");
        }

        var challenge = await emailOtpService.RequestPasswordResetOtpAsync(target.Email, ct);
        await LogAuditAsync(adminId, adminName, "Triggered Password Reset", "User", userId, $"Triggered password reset for {target.Email}", ct);

        return new
        {
            userId = target.Id,
            target.Email,
            purpose = challenge.Purpose,
            deliveryChannel = challenge.DeliveryChannel,
            destinationHint = challenge.DestinationHint,
            expiresAt = challenge.ExpiresAt,
            retryAfterSeconds = challenge.RetryAfterSeconds
        };
    }

    // ════════════════════════════════════════════
    //  Billing Ops
    // ════════════════════════════════════════════

    public async Task<object> GetBillingPlansAsync(string? status, CancellationToken ct)
    {
        var query = db.BillingPlans.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = ParseBillingPlanStatus(status, BillingPlanStatus.Active);
            query = query.Where(p => p.Status == parsedStatus);
        }

        List<BillingPlan> plans;
        if (!db.Database.IsSqlite())
        {
            plans = await query
                .OrderBy(p => p.DisplayOrder)
                .ThenByDescending(p => p.UpdatedAt)
                .ToListAsync(ct);
        }
        else
        {
            plans = (await query.ToListAsync(ct))
                .OrderBy(p => p.DisplayOrder)
                .ThenByDescending(p => p.UpdatedAt)
                .ToList();
        }

        return plans.Select(MapBillingPlan);
    }

    public async Task<object> CreateBillingPlanAsync(string adminId, string adminName,
        AdminBillingPlanCreateRequest request, CancellationToken ct)
    {
        if (await db.BillingPlans.AnyAsync(plan => plan.Code == request.Code, ct))
        {
            throw ApiException.Validation("billing_plan_code_exists", "A plan with this code already exists.");
        }

        var idValue = $"plan-{Guid.NewGuid():N}";
        var id = idValue[..Math.Min(64, idValue.Length)];
        var now = DateTimeOffset.UtcNow;
        var status = ParseBillingPlanStatus(request.Status, BillingPlanStatus.Active);

        var plan = new BillingPlan
        {
            Id = id,
            Code = request.Code.Trim(),
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Currency = request.Currency,
            Interval = request.Interval,
            DurationMonths = request.DurationMonths,
            IncludedCredits = request.IncludedCredits,
            DisplayOrder = request.DisplayOrder,
            IsVisible = request.IsVisible,
            IsRenewable = request.IsRenewable,
            TrialDays = request.TrialDays,
            IncludedSubtestsJson = request.IncludedSubtestsJson ?? "[]",
            EntitlementsJson = request.EntitlementsJson ?? "{}",
            ActiveSubscribers = 0,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.BillingPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        referenceCache.InvalidateBillingPlans();

        await LogAuditAsync(adminId, adminName, "Created", "BillingPlan", id, $"Created plan: {request.Name}", ct);
        return MapBillingPlan(plan);
    }

    public async Task<object> UpdateBillingPlanAsync(string adminId, string adminName, string planId, AdminBillingPlanUpdateRequest request, CancellationToken ct)
    {
        var plan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Id == planId || p.Code == planId, ct)
            ?? throw ApiException.NotFound("billing_plan_not_found", "Billing plan not found.");

        var now = DateTimeOffset.UtcNow;
        plan.Code = request.Code.Trim();
        plan.Name = request.Name;
        plan.Description = request.Description;
        plan.Price = request.Price;
        plan.Currency = request.Currency;
        plan.Interval = request.Interval;
        plan.DurationMonths = request.DurationMonths;
        plan.IncludedCredits = request.IncludedCredits;
        plan.DisplayOrder = request.DisplayOrder;
        plan.IsVisible = request.IsVisible;
        plan.IsRenewable = request.IsRenewable;
        plan.TrialDays = request.TrialDays;
        plan.IncludedSubtestsJson = request.IncludedSubtestsJson ?? "[]";
        plan.EntitlementsJson = request.EntitlementsJson ?? "{}";
        plan.Status = ParseBillingPlanStatus(request.Status, plan.Status);
        plan.ArchivedAt = plan.Status == BillingPlanStatus.Archived ? now : plan.ArchivedAt;
        plan.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        referenceCache.InvalidateBillingPlans();
        await LogAuditAsync(adminId, adminName, "Updated", "BillingPlan", plan.Id, $"Updated plan: {request.Name}", ct);
        return MapBillingPlan(plan);
    }

    public async Task<object> GetBillingAddOnsAsync(string? status, CancellationToken ct)
    {
        var query = db.BillingAddOns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = ParseBillingAddOnStatus(status, BillingAddOnStatus.Active);
            query = query.Where(addOn => addOn.Status == parsedStatus);
        }

        List<BillingAddOn> addOns;
        if (!db.Database.IsSqlite())
        {
            addOns = await query
                .OrderBy(addOn => addOn.DisplayOrder)
                .ThenByDescending(addOn => addOn.UpdatedAt)
                .ToListAsync(ct);
        }
        else
        {
            addOns = (await query.ToListAsync(ct))
                .OrderBy(addOn => addOn.DisplayOrder)
                .ThenByDescending(addOn => addOn.UpdatedAt)
                .ToList();
        }

        return addOns.Select(MapBillingAddOn);
    }

    public async Task<object> CreateBillingAddOnAsync(string adminId, string adminName, AdminBillingAddOnCreateRequest request, CancellationToken ct)
    {
        if (await db.BillingAddOns.AnyAsync(addOn => addOn.Code == request.Code, ct))
        {
            throw ApiException.Validation("billing_addon_code_exists", "An add-on with this code already exists.");
        }

        var addOnIdValue = $"addon-{Guid.NewGuid():N}";
        var id = addOnIdValue[..Math.Min(64, addOnIdValue.Length)];
        var now = DateTimeOffset.UtcNow;
        var addOn = new BillingAddOn
        {
            Id = id,
            Code = request.Code.Trim(),
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Currency = request.Currency,
            Interval = request.Interval,
            DurationDays = request.DurationDays,
            GrantCredits = request.GrantCredits,
            DisplayOrder = request.DisplayOrder,
            IsRecurring = request.IsRecurring,
            AppliesToAllPlans = request.AppliesToAllPlans,
            IsStackable = request.IsStackable,
            QuantityStep = request.QuantityStep,
            MaxQuantity = request.MaxQuantity,
            Status = ParseBillingAddOnStatus(request.Status, BillingAddOnStatus.Active),
            CompatiblePlanCodesJson = request.CompatiblePlanCodesJson ?? "[]",
            GrantEntitlementsJson = request.GrantEntitlementsJson ?? "{}",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.BillingAddOns.Add(addOn);
        await db.SaveChangesAsync(ct);
        referenceCache.InvalidateBillingAddOns();
        await LogAuditAsync(adminId, adminName, "Created", "BillingAddOn", addOn.Id, $"Created add-on: {request.Name}", ct);
        return MapBillingAddOn(addOn);
    }

    public async Task<object> UpdateBillingAddOnAsync(string adminId, string adminName, string addOnId, AdminBillingAddOnUpdateRequest request, CancellationToken ct)
    {
        var addOn = await db.BillingAddOns.FirstOrDefaultAsync(addOn => addOn.Id == addOnId || addOn.Code == addOnId, ct)
            ?? throw ApiException.NotFound("billing_addon_not_found", "Billing add-on not found.");

        var now = DateTimeOffset.UtcNow;
        addOn.Code = request.Code.Trim();
        addOn.Name = request.Name;
        addOn.Description = request.Description;
        addOn.Price = request.Price;
        addOn.Currency = request.Currency;
        addOn.Interval = request.Interval;
        addOn.DurationDays = request.DurationDays;
        addOn.GrantCredits = request.GrantCredits;
        addOn.DisplayOrder = request.DisplayOrder;
        addOn.IsRecurring = request.IsRecurring;
        addOn.AppliesToAllPlans = request.AppliesToAllPlans;
        addOn.IsStackable = request.IsStackable;
        addOn.QuantityStep = request.QuantityStep;
        addOn.MaxQuantity = request.MaxQuantity;
        addOn.Status = ParseBillingAddOnStatus(request.Status, addOn.Status);
        addOn.CompatiblePlanCodesJson = request.CompatiblePlanCodesJson ?? "[]";
        addOn.GrantEntitlementsJson = request.GrantEntitlementsJson ?? "{}";
        addOn.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        referenceCache.InvalidateBillingAddOns();
        await LogAuditAsync(adminId, adminName, "Updated", "BillingAddOn", addOn.Id, $"Updated add-on: {request.Name}", ct);
        return MapBillingAddOn(addOn);
    }

    public async Task<object> GetBillingCouponsAsync(string? status, CancellationToken ct)
    {
        var query = db.BillingCoupons.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = ParseBillingCouponStatus(status, BillingCouponStatus.Active);
            query = query.Where(coupon => coupon.Status == parsedStatus);
        }

        var coupons = await ToOrderedListDescendingAsync(query, coupon => coupon.CreatedAt, ct);

        return coupons.Select(MapBillingCoupon);
    }

    public async Task<object> CreateBillingCouponAsync(string adminId, string adminName, AdminBillingCouponCreateRequest request, CancellationToken ct)
    {
        if (await db.BillingCoupons.AnyAsync(coupon => coupon.Code == request.Code, ct))
        {
            throw ApiException.Validation("billing_coupon_code_exists", "A coupon with this code already exists.");
        }

        var couponIdValue = $"coupon-{Guid.NewGuid():N}";
        var id = couponIdValue[..Math.Min(64, couponIdValue.Length)];
        var now = DateTimeOffset.UtcNow;
        var coupon = new BillingCoupon
        {
            Id = id,
            Code = request.Code.Trim(),
            Name = request.Name,
            Description = request.Description,
            DiscountType = ParseBillingDiscountType(request.DiscountType),
            DiscountValue = request.DiscountValue,
            Currency = request.Currency,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            UsageLimitTotal = request.UsageLimitTotal,
            UsageLimitPerUser = request.UsageLimitPerUser,
            MinimumSubtotal = request.MinimumSubtotal,
            IsStackable = request.IsStackable,
            Status = ParseBillingCouponStatus(request.Status, BillingCouponStatus.Active),
            ApplicablePlanCodesJson = request.ApplicablePlanCodesJson ?? "[]",
            ApplicableAddOnCodesJson = request.ApplicableAddOnCodesJson ?? "[]",
            Notes = request.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.BillingCoupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "BillingCoupon", coupon.Id, $"Created coupon: {request.Code}", ct);
        return MapBillingCoupon(coupon);
    }

    public async Task<object> UpdateBillingCouponAsync(string adminId, string adminName, string couponId, AdminBillingCouponUpdateRequest request, CancellationToken ct)
    {
        var coupon = await db.BillingCoupons.FirstOrDefaultAsync(coupon => coupon.Id == couponId || coupon.Code == couponId, ct)
            ?? throw ApiException.NotFound("billing_coupon_not_found", "Billing coupon not found.");

        var now = DateTimeOffset.UtcNow;
        coupon.Code = request.Code.Trim();
        coupon.Name = request.Name;
        coupon.Description = request.Description;
        coupon.DiscountType = ParseBillingDiscountType(request.DiscountType);
        coupon.DiscountValue = request.DiscountValue;
        coupon.Currency = request.Currency;
        coupon.StartsAt = request.StartsAt;
        coupon.EndsAt = request.EndsAt;
        coupon.UsageLimitTotal = request.UsageLimitTotal;
        coupon.UsageLimitPerUser = request.UsageLimitPerUser;
        coupon.MinimumSubtotal = request.MinimumSubtotal;
        coupon.IsStackable = request.IsStackable;
        coupon.Status = ParseBillingCouponStatus(request.Status, coupon.Status);
        coupon.ApplicablePlanCodesJson = request.ApplicablePlanCodesJson ?? "[]";
        coupon.ApplicableAddOnCodesJson = request.ApplicableAddOnCodesJson ?? "[]";
        coupon.Notes = request.Notes;
        coupon.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "BillingCoupon", coupon.Id, $"Updated coupon: {request.Code}", ct);
        return MapBillingCoupon(coupon);
    }

    public async Task<object> GetBillingSubscriptionsAsync(string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Subscriptions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            if (Enum.TryParse<SubscriptionStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(subscription => subscription.Status == parsedStatus);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim();
            query = query.Where(subscription => subscription.UserId.Contains(normalized) || subscription.PlanId.Contains(normalized));
        }

        var total = await query.CountAsync(ct);
        var subscriptions = await ToOrderedListDescendingAsync(
            query,
            subscription => subscription.ChangedAt,
            ct,
            skip: (page - 1) * pageSize,
            take: pageSize);

        var userIds = subscriptions.Select(subscription => subscription.UserId).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);

        var planCodes = subscriptions.Select(subscription => subscription.PlanId).Distinct().ToList();
        var planNames = await db.BillingPlans.AsNoTracking()
            .Where(plan => planCodes.Contains(plan.Code) || planCodes.Contains(plan.Id))
            .ToDictionaryAsync(plan => plan.Code, plan => plan.Name, ct);

        var items = subscriptions.Select(subscription => new
        {
            subscription.Id,
            subscription.UserId,
            userName = userNames.TryGetValue(subscription.UserId, out var name) ? name : subscription.UserId,
            planId = subscription.PlanId,
            planName = planNames.TryGetValue(subscription.PlanId, out var planName) ? planName : subscription.PlanId,
            status = subscription.Status.ToString().ToLowerInvariant(),
            subscription.NextRenewalAt,
            subscription.StartedAt,
            subscription.ChangedAt,
            price = subscription.PriceAmount,
            subscription.Currency,
            subscription.Interval,
            addOnCount = 0
        }).ToList();

        return new { total, page, pageSize, items };
    }

    public async Task<object> GetBillingCouponRedemptionsAsync(string? couponCode, string? userId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.BillingCouponRedemptions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(couponCode) && couponCode != "all")
        {
            query = query.Where(redemption => redemption.CouponCode == couponCode);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(redemption => redemption.UserId == userId);
        }

        var total = await query.CountAsync(ct);
        var redemptions = await ToOrderedListDescendingAsync(
            query,
            redemption => redemption.RedeemedAt,
            ct,
            skip: (page - 1) * pageSize,
            take: pageSize);

        var items = redemptions.Select(redemption => new
        {
            redemption.Id,
            redemption.CouponCode,
            redemption.UserId,
            redemption.QuoteId,
            redemption.CheckoutSessionId,
            redemption.SubscriptionId,
            redemption.DiscountAmount,
            redemption.Currency,
            status = redemption.Status.ToString().ToLowerInvariant(),
            redemption.RedeemedAt
        }).ToList();

        return new { total, page, pageSize, items };
    }

    public async Task<object> GetBillingInvoicesAsync(string? status, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => i.UserId.Contains(search) || i.Description.Contains(search));

        var total = await query.CountAsync(ct);
        var invoices = await ToOrderedListDescendingAsync(
            query,
            i => i.IssuedAt,
            ct,
            skip: (page - 1) * pageSize,
            take: pageSize);

        var userIds = invoices.Select(i => i.UserId).Distinct().ToList();
        var learnerNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = invoices.Select(i => new
        {
            i.Id,
            userId = i.UserId,
            userName = learnerNames.TryGetValue(i.UserId, out var name) ? name : i.UserId,
            amount = i.Amount,
            currency = i.Currency,
            status = i.Status,
            date = i.IssuedAt,
            plan = i.Description
        }).ToList();

        return new { total, page, pageSize, items };
    }

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

        var items = reviews.Select(r =>
        {
            attempts.TryGetValue(r.AttemptId, out var attempt);
            var assignment = assignments.FirstOrDefault(a => a.ReviewRequestId == r.Id);
            var learnerId = attempt?.UserId ?? "unknown";
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
                priority = r.TurnaroundOption == "express" ? "high" : "normal"
            };
        }).ToList();

        return items;
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
    //  Content Bulk Actions  (B1)
    // ════════════════════════════════════════════

    public async Task<object> BulkActionContentAsync(string adminId, string adminName,
        AdminBulkActionRequest request, CancellationToken ct)
    {
        if (request.ContentIds.Length == 0)
            throw ApiException.Validation("empty_ids", "At least one content ID is required.");

        var validActions = new[] { "publish", "archive", "delete" };
        if (!validActions.Contains(request.Action, StringComparer.OrdinalIgnoreCase))
            throw ApiException.Validation("invalid_action", $"Action must be one of: {string.Join(", ", validActions)}.");

        var items = await db.ContentItems
            .Where(c => request.ContentIds.Contains(c.Id))
            .ToListAsync(ct);

        var results = new List<object>();
        var succeeded = 0;
        var failed = 0;

        foreach (var id in request.ContentIds)
        {
            var item = items.FirstOrDefault(c => c.Id == id);
            if (item is null)
            {
                failed++;
                results.Add(new { id, success = false, error = "not_found" });
                continue;
            }

            var canApply = request.Action.ToLowerInvariant() switch
            {
                "publish" => item.Status == ContentStatus.Draft,
                "archive" => item.Status != ContentStatus.Archived,
                "delete" => item.Status == ContentStatus.Draft || item.Status == ContentStatus.Archived,
                _ => false
            };

            if (!canApply)
            {
                failed++;
                results.Add(new { id, success = false, error = $"invalid_state_{item.Status.ToString().ToLowerInvariant()}" });
                continue;
            }

            if (!request.DryRun)
            {
                switch (request.Action.ToLowerInvariant())
                {
                    case "publish":
                        item.Status = ContentStatus.Published;
                        item.PublishedAt = DateTimeOffset.UtcNow;
                        break;
                    case "archive":
                        item.Status = ContentStatus.Archived;
                        item.ArchivedAt = DateTimeOffset.UtcNow;
                        break;
                    case "delete":
                        db.ContentItems.Remove(item);
                        break;
                }
                item.UpdatedAt = DateTimeOffset.UtcNow;
            }

            succeeded++;
            results.Add(new { id, success = true, error = (string?)null });
        }

        if (!request.DryRun && succeeded > 0)
        {
            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, $"Bulk {request.Action}", "Content", null,
                $"Bulk {request.Action} on {succeeded} items ({failed} failed)", ct);
        }

        return new { action = request.Action, dryRun = request.DryRun, total = request.ContentIds.Length, succeeded, failed, results };
    }

    // ════════════════════════════════════════════
    //  Impact Summary  (B2/B3)
    // ════════════════════════════════════════════

    public async Task<object> GetContentImpactSummaryAsync(string contentId, CancellationToken ct)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        var attemptCount = await db.Attempts.CountAsync(a => a.ContentId == contentId, ct);
        var evaluationCount = await db.Evaluations.CountAsync(e =>
            db.Attempts.Any(a => a.ContentId == contentId && a.Id == e.AttemptId), ct);
        var studyPlanRefs = await db.StudyPlanItems.CountAsync(s => s.ContentId == contentId, ct);
        var activeAttempts = await db.Attempts.CountAsync(a =>
            a.ContentId == contentId && a.State != AttemptState.Completed && a.State != AttemptState.Abandoned, ct);

        return new
        {
            contentId,
            title = item.Title,
            status = item.Status.ToString().ToLowerInvariant(),
            usage = new { attemptCount, evaluationCount, studyPlanReferences = studyPlanRefs, activeAttempts },
            safeToArchive = activeAttempts == 0,
            safeToDelete = attemptCount == 0 && studyPlanRefs == 0
        };
    }

    public async Task<object> GetTaxonomyImpactSummaryAsync(string professionId, CancellationToken ct)
    {
        var p = await db.Professions.FirstOrDefaultAsync(x => x.Id == professionId, ct)
                ?? throw ApiException.NotFound("profession_not_found", "Profession not found.");

        var contentCount = await db.ContentItems.CountAsync(c => c.ProfessionId == professionId, ct);
        var learnerCount = await db.Users.CountAsync(u => u.ActiveProfessionId == professionId, ct);
        var goalCount = await db.Goals.CountAsync(g => g.ProfessionId == professionId, ct);

        return new
        {
            professionId,
            label = p.Label,
            status = p.Status,
            usage = new { contentCount, learnerCount, goalCount },
            safeToArchive = contentCount == 0 && learnerCount == 0
        };
    }

    // ════════════════════════════════════════════
    //  AI Config Activate  (B4)
    // ════════════════════════════════════════════

    public async Task<object> ActivateAIConfigAsync(string adminId, string adminName, string configId, CancellationToken ct)
    {
        var config = await db.AIConfigVersions.FirstOrDefaultAsync(x => x.Id == configId, ct)
                     ?? throw ApiException.NotFound("ai_config_not_found", "AI config not found.");

        if (config.Status == AIConfigStatus.Active)
            return new { id = configId, status = "active", message = "Already active." };

        // Deactivate other configs of the same task type
        var sameTask = await db.AIConfigVersions
            .Where(a => a.TaskType == config.TaskType && a.Status == AIConfigStatus.Active)
            .ToListAsync(ct);
        foreach (var other in sameTask)
            other.Status = AIConfigStatus.Deprecated;

        config.Status = AIConfigStatus.Active;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Activated", "AIConfig", configId,
            $"Activated AI config: {config.Model} for {config.TaskType}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminAiConfigChanged,
            "ai_config",
            configId,
            DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
            $"AI config {config.Model} for {config.TaskType} was activated.",
            ct);
        return new { id = configId, status = "active" };
    }

    // ════════════════════════════════════════════
    //  Flag Activate / Deactivate  (B5)
    // ════════════════════════════════════════════

    public async Task<object> ActivateFlagAsync(string adminId, string adminName, string flagId, CancellationToken ct)
    {
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Id == flagId, ct)
                   ?? throw ApiException.NotFound("flag_not_found", "Feature flag not found.");

        flag.Enabled = true;
        flag.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Activated", "Flag", flagId, $"Activated flag: {flag.Name}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminFeatureFlagChanged,
            "feature_flag",
            flagId,
            flag.UpdatedAt.UtcDateTime.Ticks.ToString(),
            $"Feature flag {flag.Name} was activated.",
            ct);
        return new { id = flagId, enabled = true };
    }

    public async Task<object> DeactivateFlagAsync(string adminId, string adminName, string flagId, CancellationToken ct)
    {
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Id == flagId, ct)
                   ?? throw ApiException.NotFound("flag_not_found", "Feature flag not found.");

        flag.Enabled = false;
        flag.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Deactivated", "Flag", flagId, $"Deactivated flag: {flag.Name}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminFeatureFlagChanged,
            "feature_flag",
            flagId,
            flag.UpdatedAt.UtcDateTime.Ticks.ToString(),
            $"Feature flag {flag.Name} was deactivated.",
            ct);
        return new { id = flagId, enabled = false };
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
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Cancelled Review", "ReviewRequest", reviewRequestId,
            $"Cancelled: {request.Reason}", ct);
        return new { id = reviewRequestId, status = "cancelled" };
    }

    public async Task<object> ReopenReviewAsync(string adminId, string adminName,
        string reviewRequestId, AdminReviewReopenRequest request, CancellationToken ct)
    {
        var review = await db.ReviewRequests.FirstOrDefaultAsync(r => r.Id == reviewRequestId, ct)
                     ?? throw ApiException.NotFound("review_not_found", "Review request not found.");

        if (review.State is not (ReviewRequestState.Cancelled or ReviewRequestState.Failed))
            throw ApiException.Conflict("review_not_reopenable",
                $"Only cancelled or failed reviews can be reopened. Current: {review.State}.");

        review.State = ReviewRequestState.Queued;
        review.CompletedAt = null;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Reopened Review", "ReviewRequest", reviewRequestId,
            request.Reason ?? "Reopened by admin", ct);
        return new { id = reviewRequestId, status = "queued" };
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
    //  Audit Log Detail  (B9)
    // ════════════════════════════════════════════

    public async Task<object> GetAuditEventDetailAsync(string eventId, CancellationToken ct)
    {
        var evt = await db.AuditEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct)
                  ?? throw ApiException.NotFound("audit_event_not_found", "Audit event not found.");

        return new
        {
            evt.Id,
            timestamp = evt.OccurredAt,
            actorId = evt.ActorId,
            actorName = evt.ActorName,
            action = evt.Action,
            resourceType = evt.ResourceType,
            resourceId = evt.ResourceId,
            details = evt.Details
        };
    }

    // ════════════════════════════════════════════
    //  Optimistic Concurrency on Content (B18)
    // ════════════════════════════════════════════

    public async Task<object> UpdateContentWithVersionCheckAsync(string adminId, string adminName,
        string contentId, AdminContentUpdateRequest request, int? expectedRevisionCount, CancellationToken ct)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        if (expectedRevisionCount.HasValue)
        {
            var currentRevCount = await db.ContentRevisions.CountAsync(r => r.ContentItemId == contentId, ct);
            if (currentRevCount != expectedRevisionCount.Value)
                throw ApiException.Conflict("version_conflict",
                    $"Content has been modified. Expected revision {expectedRevisionCount.Value}, current is {currentRevCount}. Please refresh and retry.");
        }

        return await UpdateContentAsync(adminId, adminName, contentId, request, ct);
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

    // ════════════════════════════════════════════
    //  Admin Permissions (RBAC)
    // ════════════════════════════════════════════

    public async Task<object> GetAdminPermissionsAsync(string userId, CancellationToken ct)
    {
        var user = await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw ApiException.NotFound("user_not_found", "User not found.");

        if (user.Role != ApplicationUserRoles.Admin)
            throw ApiException.Validation("not_admin", "User is not an admin.");

        var grants = await db.AdminPermissionGrants
            .AsNoTracking()
            .Where(g => g.AdminUserId == userId)
            .OrderBy(g => g.Permission)
            .ToListAsync(ct);

        return new
        {
            userId,
            permissions = grants.Select(g => new
            {
                permission = g.Permission,
                grantedBy = g.GrantedBy,
                grantedAt = g.GrantedAt
            }),
            allPermissions = AdminPermissions.All
        };
    }

    public async Task<object> UpdateAdminPermissionsAsync(
        string actorId, string actorName, string userId,
        AdminPermissionUpdateRequest request, CancellationToken ct)
    {
        var user = await db.ApplicationUserAccounts.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw ApiException.NotFound("user_not_found", "User not found.");

        if (user.Role != ApplicationUserRoles.Admin)
            throw ApiException.Validation("not_admin", "User is not an admin.");

        var invalid = request.Permissions.Except(AdminPermissions.All).ToArray();
        if (invalid.Length > 0)
            throw ApiException.Validation("invalid_permissions", $"Invalid permissions: {string.Join(", ", invalid)}");

        var tx = await BeginTransactionIfNeededAsync(ct);
        try
        {
            var existing = await db.AdminPermissionGrants
                .Where(g => g.AdminUserId == userId)
                .ToListAsync(ct);

            db.AdminPermissionGrants.RemoveRange(existing);

            var now = timeProvider.GetUtcNow();
            foreach (var perm in request.Permissions.Distinct())
            {
                db.AdminPermissionGrants.Add(new AdminPermissionGrant
                {
                    Id = $"APG-{Guid.NewGuid():N}",
                    AdminUserId = userId,
                    Permission = perm,
                    GrantedBy = actorId,
                    GrantedAt = now
                });
            }

            await db.SaveChangesAsync(ct);
            await CommitIfOwnedAsync(tx, ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }

        await LogAuditAsync(actorId, actorName, "UpdatePermissions", "AdminPermission", userId,
            $"Set permissions: [{string.Join(", ", request.Permissions)}]", ct);

        return new { userId, permissions = request.Permissions, updated = true };
    }

    // ════════════════════════════════════════════
    //  Permission Templates
    // ════════════════════════════════════════════

    public object GetAllPermissions()
    {
        return new
        {
            permissions = AdminPermissions.All.Select(p => new { key = p }).ToArray()
        };
    }

    public async Task<object> GetPermissionTemplatesAsync(CancellationToken ct)
    {
        var templates = await db.PermissionTemplates
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return new
        {
            templates = templates.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                description = t.Description,
                permissions = System.Text.Json.JsonSerializer.Deserialize<string[]>(t.Permissions) ?? [],
                createdBy = t.CreatedBy,
                createdAt = t.CreatedAt
            })
        };
    }

    public async Task<object> CreatePermissionTemplateAsync(
        string actorId, string actorName,
        CreatePermissionTemplateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw ApiException.Validation("name_required", "Template name is required.");

        var invalid = request.Permissions.Except(AdminPermissions.All).ToArray();
        if (invalid.Length > 0)
            throw ApiException.Validation("invalid_permissions", $"Invalid permissions: {string.Join(", ", invalid)}");

        var exists = await db.PermissionTemplates.AnyAsync(t => t.Name == request.Name, ct);
        if (exists)
            throw ApiException.Validation("duplicate_name", "A template with this name already exists.");

        var template = new PermissionTemplate
        {
            Id = $"PT-{Guid.NewGuid():N}",
            Name = request.Name,
            Description = request.Description,
            Permissions = System.Text.Json.JsonSerializer.Serialize(request.Permissions.Distinct().ToArray()),
            CreatedBy = actorId,
            CreatedAt = timeProvider.GetUtcNow()
        };

        db.PermissionTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "CreatePermissionTemplate", "PermissionTemplate", template.Id,
            $"Created template '{request.Name}' with [{string.Join(", ", request.Permissions)}]", ct);

        return new
        {
            id = template.Id,
            name = template.Name,
            description = template.Description,
            permissions = request.Permissions,
            createdBy = template.CreatedBy,
            createdAt = template.CreatedAt
        };
    }

    public async Task<object> DeletePermissionTemplateAsync(
        string actorId, string actorName, string templateId, CancellationToken ct)
    {
        var template = await db.PermissionTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
                       ?? throw ApiException.NotFound("template_not_found", "Permission template not found.");

        db.PermissionTemplates.Remove(template);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "DeletePermissionTemplate", "PermissionTemplate", templateId,
            $"Deleted template '{template.Name}'", ct);

        return new { deleted = true, id = templateId };
    }

    public async Task<object> ApplyPermissionTemplateAsync(
        string actorId, string actorName, string userId, string templateId, CancellationToken ct)
    {
        var user = await db.ApplicationUserAccounts.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw ApiException.NotFound("user_not_found", "User not found.");

        if (user.Role != ApplicationUserRoles.Admin)
            throw ApiException.Validation("not_admin", "User is not an admin.");

        var template = await db.PermissionTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct)
                       ?? throw ApiException.NotFound("template_not_found", "Permission template not found.");

        var permissions = System.Text.Json.JsonSerializer.Deserialize<string[]>(template.Permissions) ?? [];

        var tx = await BeginTransactionIfNeededAsync(ct);
        try
        {
            var existing = await db.AdminPermissionGrants
                .Where(g => g.AdminUserId == userId)
                .ToListAsync(ct);

            db.AdminPermissionGrants.RemoveRange(existing);

            var now = timeProvider.GetUtcNow();
            foreach (var perm in permissions.Distinct())
            {
                db.AdminPermissionGrants.Add(new AdminPermissionGrant
                {
                    Id = $"APG-{Guid.NewGuid():N}",
                    AdminUserId = userId,
                    Permission = perm,
                    GrantedBy = actorId,
                    GrantedAt = now
                });
            }

            await db.SaveChangesAsync(ct);
            await CommitIfOwnedAsync(tx, ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }

        await LogAuditAsync(actorId, actorName, "ApplyPermissionTemplate", "AdminPermission", userId,
            $"Applied template '{template.Name}' → [{string.Join(", ", permissions)}]", ct);

        return new { userId, templateId, templateName = template.Name, permissions, applied = true };
    }

    // ════════════════════════════════════════════
    //  Content Publishing Workflow
    // ════════════════════════════════════════════

    public async Task<object> RequestContentPublishAsync(
        string actorId, string actorName, string contentId,
        AdminPublishRequestPayload request, CancellationToken ct)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        if (item.Status != ContentStatus.Draft && item.Status != ContentStatus.InReview && item.Status != ContentStatus.Rejected)
            throw ApiException.Validation("invalid_status", "Content must be in Draft, InReview, or Rejected status to request publishing.");

        var pending = await db.ContentPublishRequests
            .AnyAsync(r => r.ContentItemId == contentId && (r.Status == "pending" || r.Status == "editor_review" || r.Status == "publisher_approval"), ct);
        if (pending)
            throw ApiException.Conflict("already_pending", "A publish request is already pending for this content.");

        item.Status = ContentStatus.EditorReview;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        var pr = new ContentPublishRequest
        {
            Id = $"CPR-{Guid.NewGuid():N}",
            ContentItemId = contentId,
            RequestedBy = actorId,
            RequestedByName = actorName,
            RequestNote = request.Note,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = "editor_review",
            Stage = "editor_review"
        };

        db.ContentPublishRequests.Add(pr);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "RequestPublish", "Content", contentId,
            $"Publish requested (multi-stage) for: {item.Title}", ct);

        return new { requestId = pr.Id, contentId, status = "editor_review", stage = "editor_review" };
    }

    public async Task<object> GetPublishRequestsAsync(
        string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentPublishRequests.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = items.Select(r => new
            {
                id = r.Id,
                contentItemId = r.ContentItemId,
                requestedBy = r.RequestedBy,
                requestedByName = r.RequestedByName,
                reviewedBy = r.ReviewedBy,
                reviewedByName = r.ReviewedByName,
                status = r.Status,
                stage = r.Stage,
                requestNote = r.RequestNote,
                reviewNote = r.ReviewNote,
                requestedAt = r.RequestedAt,
                reviewedAt = r.ReviewedAt,
                editorReviewedBy = r.EditorReviewedBy,
                editorReviewedByName = r.EditorReviewedByName,
                editorReviewedAt = r.EditorReviewedAt,
                editorNotes = r.EditorNotes,
                publisherApprovedBy = r.PublisherApprovedBy,
                publisherApprovedByName = r.PublisherApprovedByName,
                publisherApprovedAt = r.PublisherApprovedAt,
                publisherNotes = r.PublisherNotes,
                rejectedBy = r.RejectedBy,
                rejectedByName = r.RejectedByName,
                rejectedAt = r.RejectedAt,
                rejectionReason = r.RejectionReason,
                rejectionStage = r.RejectionStage
            }),
            total,
            page,
            pageSize
        };
    }

    public async Task<object> ApprovePublishRequestAsync(
        string actorId, string actorName, string requestId,
        AdminPublishReviewPayload request, CancellationToken ct)
    {
        var pr = await db.ContentPublishRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                 ?? throw ApiException.NotFound("request_not_found", "Publish request not found.");

        if (pr.Status != "pending")
            throw ApiException.Validation("not_pending", "Publish request is not pending.");

        if (pr.RequestedBy == actorId)
            throw ApiException.Validation("self_approve", "Cannot approve your own publish request.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == pr.ContentItemId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        pr.Status = "approved";
        pr.ReviewedBy = actorId;
        pr.ReviewedByName = actorName;
        pr.ReviewNote = request.Note;
        pr.ReviewedAt = DateTimeOffset.UtcNow;

        item.Status = ContentStatus.Published;
        item.PublishedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "ApprovePublish", "Content", pr.ContentItemId,
            $"Approved publish for: {item.Title}", ct);

        return new { requestId, contentId = pr.ContentItemId, status = "approved" };
    }

    public async Task<object> RejectPublishRequestAsync(
        string actorId, string actorName, string requestId,
        AdminPublishReviewPayload request, CancellationToken ct)
    {
        var pr = await db.ContentPublishRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                 ?? throw ApiException.NotFound("request_not_found", "Publish request not found.");

        if (pr.Status != "pending")
            throw ApiException.Validation("not_pending", "Publish request is not pending.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == pr.ContentItemId, ct);

        pr.Status = "rejected";
        pr.ReviewedBy = actorId;
        pr.ReviewedByName = actorName;
        pr.ReviewNote = request.Note;
        pr.ReviewedAt = DateTimeOffset.UtcNow;

        if (item is not null)
        {
            item.Status = ContentStatus.Draft;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "RejectPublish", "Content", pr.ContentItemId,
            $"Rejected publish{(request.Note is not null ? $": {request.Note}" : "")}", ct);

        return new { requestId, contentId = pr.ContentItemId, status = "rejected" };
    }

    // ── Multi-Stage Approval Workflow ──

    public async Task<object> SubmitContentForReviewAsync(
        string actorId, string actorName, string contentId,
        AdminPublishRequestPayload request, CancellationToken ct)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        if (item.Status != ContentStatus.Draft && item.Status != ContentStatus.Rejected)
            throw ApiException.Validation("invalid_status", "Content must be in Draft or Rejected status to submit for review.");

        var pending = await db.ContentPublishRequests
            .AnyAsync(r => r.ContentItemId == contentId && (r.Status == "pending" || r.Status == "editor_review" || r.Status == "publisher_approval"), ct);
        if (pending)
            throw ApiException.Conflict("already_pending", "An active publish request already exists for this content.");

        item.Status = ContentStatus.EditorReview;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        var pr = new ContentPublishRequest
        {
            Id = $"CPR-{Guid.NewGuid():N}",
            ContentItemId = contentId,
            RequestedBy = actorId,
            RequestedByName = actorName,
            RequestNote = request.Note,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = "editor_review",
            Stage = "editor_review"
        };

        db.ContentPublishRequests.Add(pr);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "SubmitForReview", "Content", contentId,
            $"Submitted for editor review: {item.Title}", ct);

        return new { requestId = pr.Id, contentId, status = "editor_review", stage = "editor_review" };
    }

    public async Task<object> EditorApproveContentAsync(
        string actorId, string actorName, string contentId,
        AdminEditorReviewPayload request, CancellationToken ct)
    {
        var perms = await GetEffectivePermissionsAsync(actorId, ct);
        if (!perms.Contains(AdminPermissions.ContentEditorReview) && !perms.Contains(AdminPermissions.ContentPublish) && !perms.Contains(AdminPermissions.SystemAdmin))
            throw ApiException.Forbidden("insufficient_permission", "Editor review permission required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        if (item.Status != ContentStatus.EditorReview)
            throw ApiException.Validation("invalid_status", "Content must be in EditorReview status.");

        var pr = await db.ContentPublishRequests
            .Where(r => r.ContentItemId == contentId && r.Status == "editor_review")
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("request_not_found", "No active editor review request found.");

        if (pr.RequestedBy == actorId)
            throw ApiException.Validation("self_approve", "Cannot approve your own content submission.");

        pr.Status = "publisher_approval";
        pr.Stage = "publisher_approval";
        pr.EditorReviewedBy = actorId;
        pr.EditorReviewedByName = actorName;
        pr.EditorReviewedAt = DateTimeOffset.UtcNow;
        pr.EditorNotes = request.Notes;

        item.Status = ContentStatus.PublisherApproval;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "EditorApprove", "Content", contentId,
            $"Editor approved, moved to publisher approval: {item.Title}", ct);

        return new { requestId = pr.Id, contentId, status = "publisher_approval", stage = "publisher_approval" };
    }

    public async Task<object> EditorRejectContentAsync(
        string actorId, string actorName, string contentId,
        AdminEditorRejectPayload request, CancellationToken ct)
    {
        var perms = await GetEffectivePermissionsAsync(actorId, ct);
        if (!perms.Contains(AdminPermissions.ContentEditorReview) && !perms.Contains(AdminPermissions.ContentPublish) && !perms.Contains(AdminPermissions.SystemAdmin))
            throw ApiException.Forbidden("insufficient_permission", "Editor review permission required.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw ApiException.Validation("reason_required", "Rejection reason is required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        if (item.Status != ContentStatus.EditorReview)
            throw ApiException.Validation("invalid_status", "Content must be in EditorReview status.");

        var pr = await db.ContentPublishRequests
            .Where(r => r.ContentItemId == contentId && r.Status == "editor_review")
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("request_not_found", "No active editor review request found.");

        pr.Status = "rejected";
        pr.RejectedBy = actorId;
        pr.RejectedByName = actorName;
        pr.RejectedAt = DateTimeOffset.UtcNow;
        pr.RejectionReason = request.Reason;
        pr.RejectionStage = "editor_review";
        pr.ReviewedAt = DateTimeOffset.UtcNow;

        item.Status = ContentStatus.Draft;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "EditorReject", "Content", contentId,
            $"Editor rejected: {request.Reason}", ct);

        return new { requestId = pr.Id, contentId, status = "rejected", rejectionStage = "editor_review" };
    }

    public async Task<object> PublisherApproveContentAsync(
        string actorId, string actorName, string contentId,
        AdminPublisherApprovePayload request, CancellationToken ct)
    {
        var perms = await GetEffectivePermissionsAsync(actorId, ct);
        if (!perms.Contains(AdminPermissions.ContentPublisherApproval) && !perms.Contains(AdminPermissions.ContentPublish) && !perms.Contains(AdminPermissions.SystemAdmin))
            throw ApiException.Forbidden("insufficient_permission", "Publisher approval permission required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        if (item.Status != ContentStatus.PublisherApproval)
            throw ApiException.Validation("invalid_status", "Content must be in PublisherApproval status.");

        var pr = await db.ContentPublishRequests
            .Where(r => r.ContentItemId == contentId && r.Status == "publisher_approval")
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("request_not_found", "No active publisher approval request found.");

        pr.Status = "approved";
        pr.PublisherApprovedBy = actorId;
        pr.PublisherApprovedByName = actorName;
        pr.PublisherApprovedAt = DateTimeOffset.UtcNow;
        pr.PublisherNotes = request.Notes;
        pr.ReviewedBy = actorId;
        pr.ReviewedByName = actorName;
        pr.ReviewedAt = DateTimeOffset.UtcNow;

        item.Status = ContentStatus.Published;
        item.PublishedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "PublisherApprove", "Content", contentId,
            $"Publisher approved and published: {item.Title}", ct);

        return new { requestId = pr.Id, contentId, status = "approved" };
    }

    public async Task<object> PublisherRejectContentAsync(
        string actorId, string actorName, string contentId,
        AdminPublisherRejectPayload request, CancellationToken ct)
    {
        var perms = await GetEffectivePermissionsAsync(actorId, ct);
        if (!perms.Contains(AdminPermissions.ContentPublisherApproval) && !perms.Contains(AdminPermissions.ContentPublish) && !perms.Contains(AdminPermissions.SystemAdmin))
            throw ApiException.Forbidden("insufficient_permission", "Publisher approval permission required.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw ApiException.Validation("reason_required", "Rejection reason is required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        if (item.Status != ContentStatus.PublisherApproval)
            throw ApiException.Validation("invalid_status", "Content must be in PublisherApproval status.");

        var pr = await db.ContentPublishRequests
            .Where(r => r.ContentItemId == contentId && r.Status == "publisher_approval")
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("request_not_found", "No active publisher approval request found.");

        // Publisher rejection returns to EditorReview (not Draft)
        pr.Status = "editor_review";
        pr.Stage = "editor_review";
        pr.RejectedBy = actorId;
        pr.RejectedByName = actorName;
        pr.RejectedAt = DateTimeOffset.UtcNow;
        pr.RejectionReason = request.Reason;
        pr.RejectionStage = "publisher_approval";
        // Clear prior publisher fields for re-review
        pr.PublisherApprovedBy = null;
        pr.PublisherApprovedByName = null;
        pr.PublisherApprovedAt = null;
        pr.PublisherNotes = null;

        item.Status = ContentStatus.EditorReview;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "PublisherReject", "Content", contentId,
            $"Publisher rejected, returned to editor review: {request.Reason}", ct);

        return new { requestId = pr.Id, contentId, status = "editor_review", rejectionStage = "publisher_approval" };
    }

    public async Task<object> GetPendingReviewContentAsync(
        string? stage, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentPublishRequests.AsNoTracking()
            .Where(r => r.Status == "editor_review" || r.Status == "publisher_approval");

        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(r => r.Stage == stage);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = items.Select(r => new
            {
                id = r.Id,
                contentItemId = r.ContentItemId,
                requestedBy = r.RequestedBy,
                requestedByName = r.RequestedByName,
                status = r.Status,
                stage = r.Stage,
                requestNote = r.RequestNote,
                requestedAt = r.RequestedAt,
                editorReviewedBy = r.EditorReviewedBy,
                editorReviewedByName = r.EditorReviewedByName,
                editorReviewedAt = r.EditorReviewedAt,
                editorNotes = r.EditorNotes,
                publisherApprovedBy = r.PublisherApprovedBy,
                publisherApprovedByName = r.PublisherApprovedByName,
                publisherApprovedAt = r.PublisherApprovedAt,
                publisherNotes = r.PublisherNotes,
                rejectedBy = r.RejectedBy,
                rejectedByName = r.RejectedByName,
                rejectedAt = r.RejectedAt,
                rejectionReason = r.RejectionReason,
                rejectionStage = r.RejectionStage
            }),
            total,
            page,
            pageSize
        };
    }

    // ════════════════════════════════════════════
    //  Webhook Monitoring
    // ════════════════════════════════════════════

    public async Task<object> GetWebhookEventsAsync(
        string? gateway, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PaymentWebhookEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(gateway))
            query = query.Where(e => e.Gateway == gateway);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(e => e.ProcessingStatus == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                id = e.Id,
                gateway = e.Gateway,
                eventType = e.EventType,
                gatewayEventId = e.GatewayEventId,
                processingStatus = e.ProcessingStatus,
                errorMessage = e.ErrorMessage,
                receivedAt = e.ReceivedAt,
                processedAt = e.ProcessedAt
            })
            .ToListAsync(ct);

        return new { items, total, page, pageSize };
    }

    public async Task<object> GetWebhookSummaryAsync(CancellationToken ct)
    {
        var events = db.PaymentWebhookEvents.AsNoTracking();
        var now = DateTimeOffset.UtcNow;
        var last24h = now.AddHours(-24);
        var last7d = now.AddDays(-7);

        var total = await events.CountAsync(ct);
        var recent24h = await events.CountAsync(e => e.ReceivedAt >= last24h, ct);
        var failed = await events.CountAsync(e => e.ProcessingStatus == "failed", ct);
        var failed24h = await events.CountAsync(e => e.ProcessingStatus == "failed" && e.ReceivedAt >= last24h, ct);

        var byStatus = await events
            .GroupBy(e => e.ProcessingStatus)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var byGateway = await events
            .GroupBy(e => e.Gateway)
            .Select(g => new { gateway = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var recentFailures = await events
            .Where(e => e.ProcessingStatus == "failed")
            .OrderByDescending(e => e.ReceivedAt)
            .Take(5)
            .Select(e => new
            {
                id = e.Id,
                eventType = e.EventType,
                errorMessage = e.ErrorMessage,
                receivedAt = e.ReceivedAt
            })
            .ToListAsync(ct);

        return new
        {
            total,
            recent24h,
            failed,
            failed24h,
            byStatus,
            byGateway,
            recentFailures
        };
    }

    public async Task<object> RetryWebhookAsync(
        string actorId, string actorName, string eventId, CancellationToken ct)
    {
        var evt = await db.PaymentWebhookEvents.FirstOrDefaultAsync(e => e.Id.ToString() == eventId, ct)
                  ?? throw ApiException.NotFound("webhook_not_found", "Webhook event not found.");

        if (evt.ProcessingStatus != "failed")
            throw ApiException.Validation("not_failed", "Only failed webhooks can be retried.");

        evt.ProcessingStatus = "received";
        evt.ErrorMessage = null;
        evt.ProcessedAt = null;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "RetryWebhook", "PaymentWebhookEvent", eventId,
            $"Retried webhook: {evt.EventType} ({evt.GatewayEventId})", ct);

        return new { eventId, status = "queued_for_retry" };
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
            ?? throw ApiException.NotFound("reviewer_not_found", "Expert reviewer not found.");

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

    // ══════════════════════════════════════════════════════
    // A4 · Content Quality Scoring
    // ══════════════════════════════════════════════════════

    public async Task<object> GetContentQualityOverviewAsync(int page, int pageSize, CancellationToken ct)
    {
        var content = await db.ContentItems
            .OrderByDescending(c => c.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var total = await db.ContentItems.CountAsync(ct);

        return new
        {
            items = content.Select(c => new
            {
                id = c.Id,
                title = c.Title,
                subtestCode = c.SubtestCode,
                contentType = c.ContentType,
                qaStatus = c.QaStatus,
                qaReviewedBy = c.QaReviewedBy,
                qaReviewedAt = c.QaReviewedAt,
                sourceType = c.SourceType,
                performanceMetrics = c.PerformanceMetricsJson,
                difficultyRating = c.DifficultyRating,
                status = c.Status.ToString().ToLower(),
                updatedAt = c.UpdatedAt
            }).ToList(),
            total,
            page,
            pageSize
        };
    }

    public async Task<object> ScoreContentQualityAsync(string actorId, string actorName, string contentId, CancellationToken ct)
    {
        var content = await db.ContentItems.FindAsync([contentId], ct)
            ?? throw ApiException.NotFound("CONTENT_NOT_FOUND", "Content item not found.");

        // Compute quality score based on completeness metrics
        var score = 0;
        var factors = new List<string>();

        if (!string.IsNullOrWhiteSpace(content.Title) && content.Title.Length >= 10) { score += 15; factors.Add("title_quality"); }
        if (!string.IsNullOrWhiteSpace(content.CaseNotes)) { score += 15; factors.Add("case_notes_present"); }
        if (content.DetailJson != "{}") { score += 20; factors.Add("detail_populated"); }
        if (content.ModelAnswerJson != "{}") { score += 20; factors.Add("model_answer_present"); }
        if (content.CriteriaFocusJson != "[]") { score += 10; factors.Add("criteria_focus_set"); }
        if (content.EstimatedDurationMinutes > 0) { score += 10; factors.Add("duration_set"); }
        if (!string.IsNullOrWhiteSpace(content.ScenarioType)) { score += 10; factors.Add("scenario_typed"); }

        content.QaStatus = score >= 80 ? "approved" : score >= 50 ? "needs_review" : "rejected";
        content.QaReviewedBy = actorId;
        content.QaReviewedAt = DateTimeOffset.UtcNow;
        content.PerformanceMetricsJson = JsonSupport.Serialize(new { qualityScore = score, factors, scoredAt = DateTimeOffset.UtcNow });
        content.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "ContentQualityScore", "ContentItem", contentId, $"Score: {score}/100 → {content.QaStatus}", ct);

        return new { contentId, qualityScore = score, qaStatus = content.QaStatus, factors };
    }

    // ══════════════════════════════════════════════════════
    // A6 · Bulk Learner Operations
    // ══════════════════════════════════════════════════════

    public async Task<object> BulkCreditAdjustmentAsync(string actorId, string actorName, string[] userIds, int creditAmount, string reason, CancellationToken ct)
    {
        if (userIds.Length == 0 || userIds.Length > 500)
            throw ApiException.Validation("INVALID_BATCH", "Provide between 1 and 500 user IDs.");

        var wallets = await db.Wallets
            .Where(w => userIds.Contains(w.UserId))
            .ToListAsync(ct);

        var results = new List<object>();
        foreach (var wallet in wallets)
        {
            wallet.CreditBalance += creditAmount;
            wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                TransactionType = creditAmount >= 0 ? "bulk_credit" : "bulk_debit",
                Amount = Math.Abs(creditAmount),
                BalanceAfter = wallet.CreditBalance,
                ReferenceType = "manual",
                ReferenceId = "bulk",
                Description = reason,
                CreatedBy = actorId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            results.Add(new { userId = wallet.UserId, newBalance = wallet.CreditBalance });
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "BulkCreditAdjustment", "Wallet", "bulk",
            $"Adjusted {wallets.Count} wallets by {creditAmount} credits. Reason: {reason}", ct);

        return new { processed = wallets.Count, skipped = userIds.Length - wallets.Count, results };
    }

    public async Task<object> BulkNotificationAsync(string actorId, string actorName, string[] userIds, string title, string message, string? category, CancellationToken ct)
    {
        if (userIds.Length == 0 || userIds.Length > 1000)
            throw ApiException.Validation("INVALID_BATCH", "Provide between 1 and 1000 user IDs.");

        var sent = 0;
        foreach (var userId in userIds)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerAccountStatusChanged,
                userId,
                category ?? "admin_broadcast",
                $"bulk-{Guid.NewGuid():N}",
                DateTimeOffset.UtcNow.Ticks.ToString(),
                new Dictionary<string, object?> { ["title"] = title, ["message"] = message },
                ct);
            sent++;
        }

        await LogAuditAsync(actorId, actorName, "BulkNotification", "Notification", "bulk",
            $"Sent to {sent} users. Title: {title}", ct);

        return new { sent, total = userIds.Length };
    }

    public async Task<object> BulkStatusChangeAsync(string actorId, string actorName, string[] userIds, string newStatus, string reason, CancellationToken ct)
    {
        if (userIds.Length == 0 || userIds.Length > 200)
            throw ApiException.Validation("INVALID_BATCH", "Provide between 1 and 200 user IDs.");

        var accounts = await db.Users
            .Where(a => userIds.Contains(a.Id))
            .ToListAsync(ct);

        foreach (var acct in accounts)
        {
            acct.AccountStatus = newStatus;
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "BulkStatusChange", "LearnerUser", "bulk",
            $"Changed {accounts.Count} accounts to '{newStatus}'. Reason: {reason}", ct);

        return new { processed = accounts.Count, skipped = userIds.Length - accounts.Count, newStatus };
    }

    // ══════════════════════════════════════════════════════
    // B3 · Enterprise / Sponsor Channel
    // ══════════════════════════════════════════════════════

    public async Task<object> GetSponsorsAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.SponsorAccounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync(ct);
        var sponsors = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = sponsors.Select(s => new
            {
                id = s.Id, name = s.Name, type = s.Type, contactEmail = s.ContactEmail,
                organizationName = s.OrganizationName, status = s.Status, createdAt = s.CreatedAt
            }).ToList(),
            total, page, pageSize
        };
    }

    public async Task<object> CreateSponsorAsync(string actorId, string actorName, SponsorCreateRequest req, CancellationToken ct)
    {
        var sponsor = new SponsorAccount
        {
            Id = $"spon-{Guid.NewGuid():N}",
            AuthAccountId = $"auth-{Guid.NewGuid():N}",
            Name = req.Name,
            Type = req.Type,
            ContactEmail = req.ContactEmail,
            OrganizationName = req.OrganizationName,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.SponsorAccounts.Add(sponsor);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "CreateSponsor", "SponsorAccount", sponsor.Id, $"Created sponsor: {req.Name}", ct);
        return new { id = sponsor.Id, name = sponsor.Name, status = sponsor.Status };
    }

    public async Task<object> UpdateSponsorAsync(string actorId, string actorName, string sponsorId, SponsorUpdateRequest req, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts.FindAsync([sponsorId], ct)
            ?? throw ApiException.NotFound("SPONSOR_NOT_FOUND", "Sponsor not found.");

        if (req.Name is not null) sponsor.Name = req.Name;
        if (req.ContactEmail is not null) sponsor.ContactEmail = req.ContactEmail;
        if (req.OrganizationName is not null) sponsor.OrganizationName = req.OrganizationName;
        if (req.Status is not null) sponsor.Status = req.Status;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "UpdateSponsor", "SponsorAccount", sponsorId, "Updated sponsor", ct);
        return new { id = sponsor.Id, name = sponsor.Name, status = sponsor.Status };
    }

    public async Task<object> GetCohortsAsync(string? sponsorId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Cohorts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(sponsorId))
            query = query.Where(c => c.SponsorId == sponsorId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(c => c.Status == status);

        var total = await query.CountAsync(ct);
        var cohorts = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = cohorts.Select(c => new
            {
                id = c.Id, sponsorId = c.SponsorId, name = c.Name, examTypeCode = c.ExamTypeCode,
                startDate = c.StartDate, endDate = c.EndDate, maxSeats = c.MaxSeats,
                enrolledCount = c.EnrolledCount, status = c.Status, createdAt = c.CreatedAt
            }).ToList(),
            total, page, pageSize
        };
    }

    public async Task<object> CreateCohortAsync(string actorId, string actorName, CohortCreateRequest req, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts.FindAsync([req.SponsorId], ct)
            ?? throw ApiException.NotFound("SPONSOR_NOT_FOUND", "Sponsor not found.");

        var cohort = new Cohort
        {
            Id = $"coh-{Guid.NewGuid():N}",
            SponsorId = req.SponsorId,
            Name = req.Name,
            ExamTypeCode = req.ExamTypeCode,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            MaxSeats = req.MaxSeats,
            EnrolledCount = 0,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Cohorts.Add(cohort);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "CreateCohort", "Cohort", cohort.Id, $"Created cohort: {req.Name}", ct);
        return new { id = cohort.Id, name = cohort.Name, status = cohort.Status };
    }

    public async Task<object> UpdateCohortAsync(string actorId, string actorName, string cohortId, CohortUpdateRequest req, CancellationToken ct)
    {
        var cohort = await db.Cohorts.FindAsync([cohortId], ct)
            ?? throw ApiException.NotFound("COHORT_NOT_FOUND", "Cohort not found.");

        if (req.Name is not null) cohort.Name = req.Name;
        if (req.StartDate is not null) cohort.StartDate = req.StartDate;
        if (req.EndDate is not null) cohort.EndDate = req.EndDate;
        if (req.MaxSeats is not null) cohort.MaxSeats = req.MaxSeats.Value;
        if (req.Status is not null) cohort.Status = req.Status;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "UpdateCohort", "Cohort", cohortId, "Updated cohort", ct);
        return new { id = cohort.Id, name = cohort.Name, status = cohort.Status };
    }

    public async Task<object> GetCohortMembersAsync(string cohortId, int page, int pageSize, CancellationToken ct)
    {
        var members = await db.CohortMembers
            .Where(m => m.CohortId == cohortId)
            .OrderByDescending(m => m.EnrolledAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var total = await db.CohortMembers.CountAsync(m => m.CohortId == cohortId, ct);

        return new
        {
            items = members.Select(m => new
            {
                id = m.Id, cohortId = m.CohortId, learnerId = m.LearnerId,
                status = m.Status, enrolledAt = m.EnrolledAt
            }).ToList(),
            total, page, pageSize
        };
    }

    public async Task<object> AddCohortMemberAsync(string actorId, string actorName, string cohortId, string learnerId, CancellationToken ct)
    {
        var cohort = await db.Cohorts.FindAsync([cohortId], ct)
            ?? throw ApiException.NotFound("COHORT_NOT_FOUND", "Cohort not found.");

        if (cohort.EnrolledCount >= cohort.MaxSeats)
            throw ApiException.Validation("COHORT_FULL", "Cohort is at maximum capacity.");

        var existing = await db.CohortMembers
            .AnyAsync(m => m.CohortId == cohortId && m.LearnerId == learnerId, ct);
        if (existing) throw ApiException.Validation("ALREADY_ENROLLED", "Learner is already enrolled.");

        var member = new CohortMember
        {
            Id = Guid.NewGuid(),
            CohortId = cohortId,
            LearnerId = learnerId,
            Status = "active",
            EnrolledAt = DateTimeOffset.UtcNow
        };
        db.CohortMembers.Add(member);
        cohort.EnrolledCount++;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "AddCohortMember", "CohortMember", member.Id.ToString(),
            $"Added learner {learnerId} to cohort {cohortId}", ct);

        return new { id = member.Id, cohortId, learnerId, status = member.Status };
    }

    public async Task<object> GetSponsorLearnersAsync(string sponsorId, int page, int pageSize, CancellationToken ct)
    {
        var links = await db.SponsorLearnerLinks
            .Where(l => l.SponsorId == sponsorId)
            .OrderByDescending(l => l.LinkedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var total = await db.SponsorLearnerLinks.CountAsync(l => l.SponsorId == sponsorId, ct);

        return new
        {
            items = links.Select(l => new
            {
                id = l.Id, sponsorId = l.SponsorId, learnerId = l.LearnerId,
                learnerConsented = l.LearnerConsented, linkedAt = l.LinkedAt, consentedAt = l.ConsentedAt
            }).ToList(),
            total, page, pageSize
        };
    }

    public async Task<object> LinkSponsorLearnerAsync(string actorId, string actorName, string sponsorId, string learnerId, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts.FindAsync([sponsorId], ct)
            ?? throw ApiException.NotFound("SPONSOR_NOT_FOUND", "Sponsor not found.");

        var existing = await db.SponsorLearnerLinks
            .AnyAsync(l => l.SponsorId == sponsorId && l.LearnerId == learnerId, ct);
        if (existing)
            throw ApiException.Validation("ALREADY_LINKED", "Learner is already linked to this sponsor.");

        var link = new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            LearnerId = learnerId,
            LearnerConsented = false,
            LinkedAt = DateTimeOffset.UtcNow
        };
        db.SponsorLearnerLinks.Add(link);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "LinkSponsorLearner", "SponsorLearnerLink", link.Id.ToString(),
            $"Linked learner {learnerId} to sponsor {sponsorId}", ct);

        return new { id = link.Id, sponsorId, learnerId, learnerConsented = false };
    }

    // ═══════════════════════════════════════════════════════════════
    // AE1: Content Usage Analytics Per Item
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetContentItemAnalyticsAsync(string contentId, CancellationToken ct)
    {
        var content = await db.ContentItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contentId, ct)
            ?? throw ApiException.NotFound("CONTENT_NOT_FOUND", "Content item not found.");

        // Slim projection (UserId/State/StartedAt/ElapsedSeconds + Id) avoids loading wide Attempt rows.
        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.ContentId == contentId)
            .Select(a => new { a.Id, a.UserId, a.State, a.StartedAt, a.ElapsedSeconds })
            .ToListAsync(ct);
        var completedAttempts = attempts.Where(a => a.State == AttemptState.Completed).ToList();
        var attemptIds = attempts.Select(a => a.Id).ToList();
        var evaluations = attemptIds.Count == 0
            ? new List<string?>()
            : await db.Evaluations.AsNoTracking()
                .Where(e => attemptIds.Contains(e.AttemptId))
                .Select(e => e.ScoreRange)
                .ToListAsync(ct);

        // Score distribution
        var scores = evaluations
            .Select(scoreRange =>
            {
                var parts = scoreRange?.Split('-');
                return parts?.Length == 2 && int.TryParse(parts[0], out var lo) && int.TryParse(parts[1], out var hi)
                    ? (lo + hi) / 2.0 : (double?)null;
            })
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
        var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

        // Aggregate users to (UserId, IsActiveLastMonth, TotalUsers) without hydrating full rows.
        var totalUsers = await db.Users.AsNoTracking().CountAsync(ct);
        var activeUserIds = await db.Users.AsNoTracking()
            .Where(u => u.LastActiveAt >= thirtyDaysAgo)
            .Select(u => u.Id)
            .ToListAsync(ct);
        var activeUserSet = new HashSet<string>(activeUserIds, StringComparer.Ordinal);

        var cohorts = new List<object>();

        if (groupKey == "profession")
        {
            // 1 query: goals (UserId, ProfessionId)
            var goals = await db.Goals.AsNoTracking()
                .Select(g => new { g.UserId, g.ProfessionId })
                .ToListAsync(ct);
            if (goals.Count == 0)
            {
                return new { groupBy = groupKey, cohorts, totalLearners = totalUsers, generatedAt = DateTimeOffset.UtcNow };
            }
            // 1 query: professions
            var professions = await db.Professions.AsNoTracking().ToListAsync(ct);
            // 1 query: all eval rows joined to attempt -> (UserId, ScoreRange)
            var relevantUserIds = goals.Select(g => g.UserId).Distinct().ToList();
            var evalRows = await (
                from e in db.Evaluations.AsNoTracking()
                join a in db.Attempts.AsNoTracking() on e.AttemptId equals a.Id
                where relevantUserIds.Contains(a.UserId)
                select new { a.UserId, e.ScoreRange }
            ).ToListAsync(ct);
            var evalsByUser = evalRows.ToLookup(r => r.UserId);

            foreach (var prof in professions)
            {
                var learnerIds = goals.Where(g => g.ProfessionId == prof.Id).Select(g => g.UserId).ToList();
                if (learnerIds.Count == 0) continue;
                var cohortEvals = learnerIds.SelectMany(uid => evalsByUser[uid]).ToList();
                var avgScores = cohortEvals
                    .Select(e => { var p = e.ScoreRange?.Split('-'); return p?.Length == 2 && int.TryParse(p[0], out var lo) ? lo : (int?)null; })
                    .Where(s => s.HasValue).Select(s => (double)s!.Value).ToList();

                cohorts.Add(new
                {
                    cohortKey = prof.Id,
                    cohortName = prof.Label,
                    learnerCount = learnerIds.Count,
                    averageScore = avgScores.Count > 0 ? Math.Round(avgScores.Average(), 1) : (double?)null,
                    evaluationCount = cohortEvals.Count,
                    activeLastMonth = learnerIds.Count(uid => activeUserSet.Contains(uid))
                });
            }
        }
        else
        {
            // 1 query: active subscriptions (UserId, PlanId)
            var subs = await db.Subscriptions.AsNoTracking()
                .Where(s => s.Status == SubscriptionStatus.Active)
                .Select(s => new { s.UserId, s.PlanId })
                .ToListAsync(ct);
            if (subs.Count == 0)
            {
                return new { groupBy = groupKey, cohorts, totalLearners = totalUsers, generatedAt = DateTimeOffset.UtcNow };
            }
            var plans = await referenceCache.GetBillingPlansAsync(ct);
            var relevantUserIds = subs.Select(s => s.UserId).Distinct().ToList();
            var evalRows = await (
                from e in db.Evaluations.AsNoTracking()
                join a in db.Attempts.AsNoTracking() on e.AttemptId equals a.Id
                where relevantUserIds.Contains(a.UserId)
                select new { a.UserId, e.ScoreRange }
            ).ToListAsync(ct);
            var evalsByUser = evalRows.ToLookup(r => r.UserId);

            foreach (var plan in plans)
            {
                var learnerIds = subs.Where(s => s.PlanId == plan.Id).Select(s => s.UserId).ToList();
                if (learnerIds.Count == 0) continue;
                var cohortEvals = learnerIds.SelectMany(uid => evalsByUser[uid]).ToList();
                var avgScores = cohortEvals
                    .Select(e => { var p = e.ScoreRange?.Split('-'); return p?.Length == 2 && int.TryParse(p[0], out var lo) ? lo : (int?)null; })
                    .Where(s => s.HasValue).Select(s => (double)s!.Value).ToList();

                cohorts.Add(new
                {
                    cohortKey = plan.Id,
                    cohortName = plan.Name,
                    learnerCount = learnerIds.Count,
                    averageScore = avgScores.Count > 0 ? Math.Round(avgScores.Average(), 1) : (double?)null,
                    evaluationCount = cohortEvals.Count,
                    activeLastMonth = learnerIds.Count(uid => activeUserSet.Contains(uid))
                });
            }
        }

        return new
        {
            groupBy = groupKey,
            cohorts = cohorts.OrderByDescending(c => ((dynamic)c).learnerCount).ToList(),
            totalLearners = totalUsers,
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // R2: Content Effectiveness Metrics
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetContentEffectivenessAsync(string? subtestCode, int top, CancellationToken ct)
    {
        var query = db.ContentItems.AsNoTracking().Where(c => c.Status == ContentStatus.Published);
        if (!string.IsNullOrEmpty(subtestCode)) query = query.Where(c => c.SubtestCode == subtestCode);

        var items = await query.Take(top > 0 ? top : 50).ToListAsync(ct);
        if (items.Count == 0)
        {
            return new { subtestFilter = subtestCode, items = new List<object>(), generatedAt = DateTimeOffset.UtcNow };
        }

        var contentIds = items.Select(i => i.Id).ToList();

        // 1 query: all attempts for these papers, projected to lightweight rows
        var attemptRows = await db.Attempts.AsNoTracking()
            .Where(a => contentIds.Contains(a.ContentId))
            .Select(a => new { a.Id, a.ContentId, a.State, a.ElapsedSeconds })
            .ToListAsync(ct);
        var attemptsByContent = attemptRows.ToLookup(a => a.ContentId);

        // 1 query: all evaluations across those attempts, joined to content id
        var attemptIds = attemptRows.Select(a => a.Id).ToList();
        var evalRows = attemptIds.Count == 0
            ? new List<EvalProjection>()
            : await (
                from e in db.Evaluations.AsNoTracking()
                join a in db.Attempts.AsNoTracking() on e.AttemptId equals a.Id
                where contentIds.Contains(a.ContentId)
                select new EvalProjection { ContentId = a.ContentId, ScoreRange = e.ScoreRange }
            ).ToListAsync(ct);
        var evalsByContent = evalRows.ToLookup(e => e.ContentId);

        var results = new List<object>();
        foreach (var item in items)
        {
            var attempts = attemptsByContent[item.Id].ToList();
            var completedCount = attempts.Count(a => a.State == AttemptState.Completed);
            var evals = evalsByContent[item.Id].ToList();

            var scores = evals
                .Select(e => { var p = e.ScoreRange?.Split('-'); return p?.Length == 2 && int.TryParse(p[0], out var lo) ? lo : (int?)null; })
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

    private sealed class EvalProjection
    {
        public string ContentId { get; set; } = string.Empty;
        public string? ScoreRange { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    // R3: Expert Efficiency Report
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetExpertEfficiencyReportAsync(int days, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var experts = await db.ExpertUsers.AsNoTracking().ToListAsync(ct);
        if (experts.Count == 0)
        {
            return new
            {
                period = days,
                experts = new List<object>(),
                summary = new { totalExperts = 0, activeExperts = 0, totalReviewsCompleted = 0, averageReviewsPerExpertPerDay = 0.0 }
            };
        }

        var expertIds = experts.Select(e => e.Id).ToList();

        // 1 query: all assignments in the period for these experts
        var assignments = await db.ExpertReviewAssignments.AsNoTracking()
            .Where(a => expertIds.Contains(a.AssignedReviewerId) && a.AssignedAt >= since)
            .ToListAsync(ct);
        var assignmentsByExpert = assignments.ToLookup(a => a.AssignedReviewerId);

        // 1 query: all drafts in the period for these experts
        var drafts = await db.ExpertReviewDrafts.AsNoTracking()
            .Where(d => expertIds.Contains(d.ReviewerId) && d.DraftSavedAt >= since)
            .ToListAsync(ct);
        var draftsByExpert = drafts.ToLookup(d => d.ReviewerId);

        // For AI alignment, we sample up to 20 drafts per expert (preserve original semantics).
        // Batch-load all needed ReviewRequests + Evaluations across the sampled draft set.
        var sampledDrafts = experts
            .SelectMany(e => draftsByExpert[e.Id].Take(20))
            .ToList();
        var sampledReviewIds = sampledDrafts.Select(d => d.ReviewRequestId).Distinct().ToList();
        var requestLookup = sampledReviewIds.Count == 0
            ? new Dictionary<string, ReviewRequest>()
            : await db.ReviewRequests.AsNoTracking()
                .Where(r => sampledReviewIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);
        var sampledAttemptIds = requestLookup.Values.Select(r => r.AttemptId).Distinct().ToList();
        var evalByAttempt = sampledAttemptIds.Count == 0
            ? new Dictionary<string, Evaluation>()
            : (await db.Evaluations.AsNoTracking()
                    .Where(e => sampledAttemptIds.Contains(e.AttemptId))
                    .ToListAsync(ct))
                .GroupBy(e => e.AttemptId)
                .ToDictionary(g => g.Key, g => g.First());

        var results = new List<object>();
        foreach (var expert in experts)
        {
            var expertAssignments = assignmentsByExpert[expert.Id].ToList();
            var expertDrafts = draftsByExpert[expert.Id].ToList();
            var completedCount = expertDrafts.Count;

            // Quality alignment vs AI — sampled to 20 drafts (preserved semantics).
            var aiDiffs = new List<double>();
            foreach (var draft in expertDrafts.Take(20))
            {
                if (!requestLookup.TryGetValue(draft.ReviewRequestId, out var request)) continue;
                if (!evalByAttempt.TryGetValue(request.AttemptId, out var aiEval)) continue;

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
                assignmentsReceived = expertAssignments.Count,
                reviewsCompleted = completedCount,
                averageReviewTimeMinutes = (double?)null, // No TimeSpentSeconds field — populate when telemetry is added.
                reviewsPerDay = Math.Round(completedCount / (double)Math.Max(days, 1), 1),
                aiAlignmentScore = aiDiffs.Count > 0 ? Math.Round(100 - aiDiffs.Average() * 10, 1) : (double?)null,
                efficiency = completedCount > 0 ? "no-data" : "no-data"
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
        // Slim projection (Id/Name/Price/TrialDays/PlanId/Status/StartedAt/ChangedAt) keeps memory + bandwidth bounded.
        var plans = await db.BillingPlans.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.Price, p.TrialDays })
            .ToDictionaryAsync(p => p.Id, ct);
        var allSubs = await db.Subscriptions.AsNoTracking()
            .Select(s => new { s.PlanId, s.Status, s.StartedAt, s.ChangedAt })
            .ToListAsync(ct);
        var activeSubs = allSubs.Where(s => s.Status == SubscriptionStatus.Active).ToList();

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
