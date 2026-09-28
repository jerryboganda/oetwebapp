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
    //  Content Management
    // ════════════════════════════════════════════

    public async Task<object> GetContentListAsync(string? contentType, string? subtestCode, string? profession,
        string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ContentItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(contentType))
            query = query.Where(c => c.ContentType == contentType);
        if (!string.IsNullOrWhiteSpace(subtestCode))
            query = query.Where(c => c.SubtestCode == subtestCode);
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
            subtestCode = c.SubtestCode,
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
            SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "manual" : request.SourceType!.Trim(),
            QaStatus = string.IsNullOrWhiteSpace(request.QaStatus) ? "pending" : request.QaStatus!.Trim(),
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
        if (!string.IsNullOrWhiteSpace(request.SourceType)) item.SourceType = request.SourceType!.Trim();
        if (!string.IsNullOrWhiteSpace(request.QaStatus))
        {
            var nextQa = request.QaStatus!.Trim();
            if (!string.Equals(item.QaStatus, nextQa, StringComparison.Ordinal))
            {
                item.QaStatus = nextQa;
                item.QaReviewedBy = adminName;
                item.QaReviewedAt = DateTimeOffset.UtcNow;
            }
        }
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

    /// <summary>
    /// Permanently deletes an archived profession/taxonomy node. No FK dependents
    /// reference it (profession references elsewhere are denormalised string tags),
    /// so this is a plain row delete. Archive-first gated; irreversible.
    /// </summary>
    public async Task<object> ForceDeleteTaxonomyNodeAsync(string adminId, string adminName,
        string professionId, CancellationToken ct)
    {
        var p = await db.Professions.FirstOrDefaultAsync(x => x.Id == professionId, ct)
                ?? throw ApiException.NotFound("profession_not_found", "Profession not found.");
        if (p.Status != "archived")
            throw ApiException.Validation("taxonomy_force_delete_not_archived",
                "Only archived professions can be permanently deleted. Archive it first.");

        db.Professions.Remove(p);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "ForceDeleted", "Taxonomy", professionId, $"Force-deleted profession: {p.Label}", ct);
        return new { id = professionId, deleted = true };
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
}
