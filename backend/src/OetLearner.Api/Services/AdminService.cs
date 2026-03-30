using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class AdminService(
    LearnerDbContext db,
    EmailOtpService emailOtpService,
    IPasswordHasher<ApplicationUserAccount> passwordHasher,
    TimeProvider timeProvider)
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

    public async Task<object> GetDashboardSummaryAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var staleDraftThreshold = now.AddDays(-14);

        var draftCount = await db.ContentItems.CountAsync(c => c.Status == ContentStatus.Draft, ct);
        var publishedCount = await db.ContentItems.CountAsync(c => c.Status == ContentStatus.Published, ct);
        var archivedCount = await db.ContentItems.CountAsync(c => c.Status == ContentStatus.Archived, ct);
        var staleDrafts = await db.ContentItems.CountAsync(c => c.Status == ContentStatus.Draft && c.UpdatedAt < staleDraftThreshold, ct);

        var queuedReviews = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.Queued, ct);
        var inReviewCount = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.InReview, ct);
        var reviewFailures = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.Failed, ct);
        var failedJobs = await db.BackgroundJobs.CountAsync(j => j.State == AsyncState.Failed, ct);
        var overdueThreshold = now.AddHours(-48);
        var overdueReviews = await db.ReviewRequests.CountAsync(r =>
            (r.State == ReviewRequestState.Queued || r.State == ReviewRequestState.InReview) && r.CreatedAt < overdueThreshold, ct);

        var pendingInvoices = await db.Invoices.CountAsync(i => i.Status == "Pending", ct);
        var failedInvoices = await db.Invoices.CountAsync(i => i.Status == "Failed", ct);
        var legacyPlans = await db.BillingPlans.CountAsync(p => p.Status == BillingPlanStatus.Legacy, ct);
        var activeSubscribers = await db.BillingPlans.SumAsync(p => p.ActiveSubscribers, ct);

        var totalFlags = await db.FeatureFlags.CountAsync(ct);
        var enabledFlags = await db.FeatureFlags.CountAsync(f => f.Enabled, ct);
        var liveExperiments = await db.FeatureFlags.CountAsync(f => f.FlagType == FeatureFlagType.Experiment && f.Enabled, ct);
        var recentFlagChanges = await db.FeatureFlags.CountAsync(f => f.UpdatedAt >= now.AddDays(-7), ct);

        var recentEvaluations = db.Evaluations.AsNoTracking().Where(e => e.GeneratedAt >= now.AddDays(-30));
        var evaluationCount = await recentEvaluations.CountAsync(ct);
        var agreementCount = evaluationCount > 0
            ? await recentEvaluations.CountAsync(e => e.ConfidenceBand == ConfidenceBand.High, ct)
            : 0;
        var agreementRate = evaluationCount > 0 ? Math.Round(100.0 * agreementCount / evaluationCount, 1) : 0;

        var completedReviews = await db.ReviewRequests.AsNoTracking()
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
                contentUpdatedAt = await db.ContentItems.MaxAsync(c => (DateTimeOffset?)c.UpdatedAt, ct),
                auditUpdatedAt = await db.AuditEvents.MaxAsync(a => (DateTimeOffset?)a.OccurredAt, ct),
                reviewUpdatedAt = await db.ReviewRequests
                    .Select(r => (DateTimeOffset?)(r.CompletedAt ?? r.CreatedAt))
                    .MaxAsync(ct),
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
        var items = await query.OrderByDescending(c => c.UpdatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var result = items.Select(c => new
        {
            c.Id,
            c.Title,
            type = c.ContentType,
            profession = c.ProfessionId,
            status = c.Status.ToString().ToLowerInvariant(),
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
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

        item.Status = ContentStatus.Published;
        item.PublishedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Published", "Content", contentId, $"Published: {item.Title}", ct);
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

        db.AIConfigVersions.Add(new AIConfigVersion
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
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "AIConfig", id, $"Created AI config: {request.Model}", ct);
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
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Updated", "AIConfig", configId, $"Updated AI config: {a.Model}", ct);
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

        var flags = await query.OrderByDescending(f => f.UpdatedAt).ToListAsync(ct);
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

        db.FeatureFlags.Add(new FeatureFlag
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
        });
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "Flag", id, $"Created flag: {request.Name}", ct);
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
        var items = await query.OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new
            {
                e.Id,
                timestamp = e.OccurredAt,
                actor = e.ActorName,
                action = e.Action,
                resource = e.ResourceId,
                details = e.Details
            }).ToListAsync(ct);

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

        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .Take(5000)
            .ToListAsync(ct);

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
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Deleted accounts cannot be adjusted.");
        }

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
                throw ApiException.NotFound("user_not_found", "User not found.");

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
        var query = db.BillingPlans.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = Enum.Parse<BillingPlanStatus>(status, true);
            query = query.Where(p => p.Status == parsedStatus);
        }

        var plans = await query.OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        return plans.Select(p => new
        {
            p.Id,
            p.Name,
            p.Price,
            p.Interval,
            activeSubscribers = p.ActiveSubscribers,
            status = p.Status.ToString().ToLowerInvariant()
        });
    }

    public async Task<object> CreateBillingPlanAsync(string adminId, string adminName,
        AdminBillingPlanCreateRequest request, CancellationToken ct)
    {
        var id = $"PLAN-{Guid.NewGuid():N}"[..12];
        var now = DateTimeOffset.UtcNow;

        db.BillingPlans.Add(new BillingPlan
        {
            Id = id,
            Name = request.Name,
            Price = request.Price,
            Interval = request.Interval,
            ActiveSubscribers = 0,
            Status = BillingPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "BillingPlan", id, $"Created plan: {request.Name}", ct);
        return new { id, status = "active" };
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
        var invoices = await query.OrderByDescending(i => i.IssuedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

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
        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var review = await db.ReviewRequests.FirstOrDefaultAsync(r => r.Id == reviewRequestId, ct)
                     ?? throw ApiException.NotFound("review_not_found", "Review request not found.");

        var assignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId, ct);

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
}
