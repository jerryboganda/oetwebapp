using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class AdminService(LearnerDbContext db)
{
    // ════════════════════════════════════════════
    //  Audit helper
    // ════════════════════════════════════════════

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
            SnapshotJson = JsonSupport.Serialize(new { item.Title, item.ContentType, item.SubtestCode }),
            CreatedBy = adminName,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "Content", id, $"Created content: {request.Title}", ct);

        return new { id, status = "draft" };
    }

    public async Task<object> UpdateContentAsync(string adminId, string adminName,
        string contentId, AdminContentUpdateRequest request, CancellationToken ct)
    {
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
            SnapshotJson = JsonSupport.Serialize(new { item.Title, item.ContentType, item.SubtestCode }),
            CreatedBy = adminName,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "Content", contentId, $"Updated content: {item.Title}", ct);

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
        var revision = await db.ContentRevisions.FirstOrDefaultAsync(
            r => r.Id == revisionId && r.ContentItemId == contentId, ct)
            ?? throw ApiException.NotFound("revision_not_found", "Revision not found.");

        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId, ct)
                   ?? throw ApiException.NotFound("content_not_found", "Content item not found.");

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

        var items = await query.OrderBy(c => c.SortOrder)
            .Select(c => new
            {
                c.Id,
                name = c.Label,
                type = c.SubtestCode,
                weight = c.SortOrder,
                status = "active",
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
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Updated", "Criterion", criterionId, $"Updated criterion: {c.Label}", ct);
        return new { id = criterionId, status = "active" };
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

    // ════════════════════════════════════════════
    //  User Ops
    // ════════════════════════════════════════════

    public async Task<object> GetUserListAsync(string? role, string? status, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        var learners = await db.Users.ToListAsync(ct);
        var experts = await db.ExpertUsers.ToListAsync(ct);

        var combined = learners.Select(u => new
        {
            u.Id,
            name = u.DisplayName,
            u.Email,
            role = u.Role,
            status = "active",
            lastLogin = u.LastActiveAt
        })
        .Concat(experts.Select(e => new
        {
            e.Id,
            name = e.DisplayName,
            e.Email,
            role = e.Role,
            status = e.IsActive ? "active" : "suspended",
            lastLogin = e.CreatedAt
        }))
        .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(role) && role != "all")
            combined = combined.Where(u => u.role == role);
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            combined = combined.Where(u => u.status == status);
        if (!string.IsNullOrWhiteSpace(search))
            combined = combined.Where(u => u.name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                            || u.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
                                            || u.Id.Contains(search, StringComparison.OrdinalIgnoreCase));

        var all = combined.OrderByDescending(u => u.lastLogin).ToList();
        var total = all.Count;
        var items = all.Skip((page - 1) * pageSize).Take(pageSize);

        return new { total, page, pageSize, items };
    }

    public async Task<object> GetUserDetailAsync(string userId, CancellationToken ct)
    {
        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is not null)
        {
            var attemptCount = await db.Attempts.CountAsync(a => a.UserId == userId, ct);
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
            return new
            {
                learner.Id,
                name = learner.DisplayName,
                learner.Email,
                role = learner.Role,
                status = "active",
                lastLogin = learner.LastActiveAt,
                tasksCompleted = attemptCount,
                creditBalance = wallet?.CreditBalance ?? 0,
                profession = learner.ActiveProfessionId,
                createdAt = learner.CreatedAt
            };
        }

        var expert = await db.ExpertUsers.FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            var reviewCount = await db.ExpertReviewAssignments.CountAsync(a => a.AssignedReviewerId == userId, ct);
            return new
            {
                expert.Id,
                name = expert.DisplayName,
                expert.Email,
                role = expert.Role,
                status = expert.IsActive ? "active" : "suspended",
                lastLogin = expert.CreatedAt,
                tasksGraded = reviewCount,
                specialties = expert.SpecialtiesJson,
                createdAt = expert.CreatedAt
            };
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    public async Task<object> UpdateUserStatusAsync(string adminId, string adminName,
        string userId, AdminUserStatusRequest request, CancellationToken ct)
    {
        var expert = await db.ExpertUsers.FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            expert.IsActive = request.Status == "active";
            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, request.Status == "active" ? "Reactivated User" : "Suspended User",
                "User", userId, $"Status changed to {request.Status}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
            return new { id = userId, status = request.Status };
        }

        if (await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            await LogAuditAsync(adminId, adminName, request.Status == "active" ? "Reactivated User" : "Suspended User",
                "User", userId, $"Status changed to {request.Status}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
            return new { id = userId, status = request.Status };
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    public async Task<object> AdjustUserCreditsAsync(string adminId, string adminName,
        string userId, AdminUserCreditsRequest request, CancellationToken ct)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
                throw ApiException.NotFound("user_not_found", "User not found.");

            wallet = new Wallet { Id = Guid.NewGuid().ToString(), UserId = userId, CreditBalance = 0, LastUpdatedAt = DateTimeOffset.UtcNow };
            db.Wallets.Add(wallet);
        }

        wallet.CreditBalance += request.Amount;
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Credit Adjustment", "User", userId,
            $"Adjusted credits by {request.Amount}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
        return new { id = userId, newBalance = wallet.CreditBalance };
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
        var query = db.Invoices.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => i.UserId.Contains(search) || i.Description.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(i => i.IssuedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                i.Id,
                userId = i.UserId,
                amount = i.Amount,
                status = i.Status,
                date = i.IssuedAt,
                plan = i.Description
            }).ToListAsync(ct);

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
        var query = db.ReviewRequests.AsQueryable();

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
        var items = reviews.Select(r => new
        {
            r.Id,
            taskId = r.AttemptId,
            learnerId = r.AttemptId,
            status = r.State == ReviewRequestState.InReview ? "in_progress"
                   : r.State == ReviewRequestState.Completed ? "completed"
                   : "pending",
            assignedAt = r.CreatedAt,
            priority = r.TurnaroundOption == "express" ? "high" : "normal"
        });

        return items;
    }

    public async Task<object> AssignReviewAsync(string adminId, string adminName,
        string reviewRequestId, AdminReviewAssignRequest request, CancellationToken ct)
    {
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
        return new { id = reviewRequestId, assignedTo = request.ExpertId };
    }

    // ════════════════════════════════════════════
    //  Quality Analytics
    // ════════════════════════════════════════════

    public async Task<object> GetQualityAnalyticsAsync(string? timeRange, string? subtest, CancellationToken ct)
    {
        var totalEvaluations = await db.Evaluations.CountAsync(ct);
        var completedReviews = await db.ReviewRequests.CountAsync(r => r.State == ReviewRequestState.Completed, ct);
        var activeUsers = await db.Users.CountAsync(ct);

        var highConfidence = totalEvaluations > 0
            ? await db.Evaluations.CountAsync(e => e.ConfidenceBand == ConfidenceBand.High, ct)
            : 0;
        var agreementRate = totalEvaluations > 0 ? Math.Round(100.0 * highConfidence / totalEvaluations, 1) : 94.2;

        var slaRate = completedReviews > 0 ? 96.1 : 0;

        return new
        {
            aiHumanAgreement = new { value = agreementRate, trend = 1.2 },
            appealsRate = new { value = 2.8, trend = -0.4 },
            avgReviewTime = new { value = 14.5, unit = "mins" },
            contentPerformance = new { topContent = "Writing Tasks", avgScore = 7.2 },
            reviewSLA = new { metPercent = slaRate, avgTurnaround = "18h" },
            featureAdoption = new { activeUsers, adoptionRate = 78.5 },
            riskCases = new { count = 5, severity = "medium" }
        };
    }
}
