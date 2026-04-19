using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.StudyPlanner;

/// <summary>
/// Admin CRUD surface for the Study Planner v2. Handles all admin-authored
/// content: task templates, plan templates (incl. item list), assignment
/// rules, and the drift policy singleton. Every mutation writes an AuditEvent.
/// </summary>
public interface IStudyPlannerAdminService
{
    // Task Templates
    Task<IReadOnlyList<StudyPlanTaskTemplate>> ListTaskTemplatesAsync(TaskTemplateListQuery q, CancellationToken ct);
    Task<StudyPlanTaskTemplate?> GetTaskTemplateAsync(string id, CancellationToken ct);
    Task<StudyPlanTaskTemplate> CreateTaskTemplateAsync(TaskTemplateCreate dto, string adminId, CancellationToken ct);
    Task<StudyPlanTaskTemplate> UpdateTaskTemplateAsync(string id, TaskTemplateUpdate dto, string adminId, CancellationToken ct);
    Task ArchiveTaskTemplateAsync(string id, string adminId, CancellationToken ct);

    // Plan Templates
    Task<IReadOnlyList<StudyPlanTemplate>> ListPlanTemplatesAsync(bool includeArchived, CancellationToken ct);
    Task<PlanTemplateDetail?> GetPlanTemplateDetailAsync(string id, CancellationToken ct);
    Task<StudyPlanTemplate> CreatePlanTemplateAsync(PlanTemplateCreate dto, string adminId, CancellationToken ct);
    Task<StudyPlanTemplate> UpdatePlanTemplateAsync(string id, PlanTemplateUpdate dto, string adminId, CancellationToken ct);
    Task ArchivePlanTemplateAsync(string id, string adminId, CancellationToken ct);
    Task ReplacePlanTemplateItemsAsync(string planTemplateId, IReadOnlyList<PlanTemplateItemUpsert> items, string adminId, CancellationToken ct);

    // Assignment Rules
    Task<IReadOnlyList<StudyPlanAssignmentRule>> ListRulesAsync(bool includeInactive, CancellationToken ct);
    Task<StudyPlanAssignmentRule?> GetRuleAsync(string id, CancellationToken ct);
    Task<StudyPlanAssignmentRule> CreateRuleAsync(AssignmentRuleCreate dto, string adminId, CancellationToken ct);
    Task<StudyPlanAssignmentRule> UpdateRuleAsync(string id, AssignmentRuleUpdate dto, string adminId, CancellationToken ct);
    Task DeleteRuleAsync(string id, string adminId, CancellationToken ct);

    // Drift Policy
    Task<StudyPlanDriftPolicy> GetDriftPolicyAsync(string examFamilyCode, CancellationToken ct);
    Task<StudyPlanDriftPolicy> UpdateDriftPolicyAsync(string examFamilyCode, DriftPolicyUpdate dto, string adminId, CancellationToken ct);
}

public sealed record PlanTemplateDetail(
    StudyPlanTemplate Template,
    IReadOnlyList<StudyPlanTemplateItem> Items);

public sealed class StudyPlannerAdminService(LearnerDbContext db) : IStudyPlannerAdminService
{
    private static readonly string[] ValidSubtests = { "writing", "speaking", "reading", "listening", "mock", "diagnostic" };
    private static readonly string[] ValidSections = { "today", "thisWeek", "nextCheckpoint", "weakSkillFocus" };
    private static readonly string[] ValidItemTypes = { "practice", "roleplay", "drill", "mock", "diagnostic", "lesson", "review" };

    // ── Task Templates ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StudyPlanTaskTemplate>> ListTaskTemplatesAsync(TaskTemplateListQuery q, CancellationToken ct)
    {
        IQueryable<StudyPlanTaskTemplate> qry = db.StudyPlanTaskTemplates.AsNoTracking();
        if (!string.IsNullOrEmpty(q.SubtestCode)) qry = qry.Where(x => x.SubtestCode == q.SubtestCode);
        if (!string.IsNullOrEmpty(q.ExamFamilyCode)) qry = qry.Where(x => x.ExamFamilyCode == q.ExamFamilyCode);
        if (q.IncludeArchived != true) qry = qry.Where(x => !x.IsArchived);
        if (!string.IsNullOrEmpty(q.Search))
        {
            var needle = q.Search.ToLower();
            qry = qry.Where(x => x.Title.ToLower().Contains(needle) || x.Slug.ToLower().Contains(needle));
        }
        var page = Math.Max(q.Page, 1);
        var pageSize = Math.Clamp(q.PageSize, 1, 200);
        return await qry.OrderBy(x => x.Title).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    }

    public async Task<StudyPlanTaskTemplate?> GetTaskTemplateAsync(string id, CancellationToken ct)
        => await db.StudyPlanTaskTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<StudyPlanTaskTemplate> CreateTaskTemplateAsync(TaskTemplateCreate dto, string adminId, CancellationToken ct)
    {
        ValidateSubtest(dto.SubtestCode);
        ValidateItemType(dto.ItemType);
        if (!string.IsNullOrEmpty(dto.DefaultSection)) ValidateSection(dto.DefaultSection);
        if (dto.DurationMinutes <= 0 || dto.DurationMinutes > 600)
            throw new InvalidOperationException("DurationMinutes must be 1–600");
        if (await db.StudyPlanTaskTemplates.AnyAsync(x => x.Slug == dto.Slug, ct))
            throw new InvalidOperationException($"task template slug '{dto.Slug}' already exists");

        var entity = new StudyPlanTaskTemplate
        {
            Id = $"spt-task-{Guid.NewGuid():N}",
            Slug = dto.Slug,
            Title = dto.Title,
            SubtestCode = dto.SubtestCode,
            ItemType = dto.ItemType,
            DurationMinutes = dto.DurationMinutes,
            RationaleMarkdown = dto.RationaleMarkdown ?? "",
            ProfessionScopeJson = JsonSerializer.Serialize(dto.ProfessionScope ?? Array.Empty<string>()),
            ExamFamilyCode = dto.ExamFamilyCode ?? "oet",
            TargetCountriesJson = JsonSerializer.Serialize(dto.TargetCountries ?? Array.Empty<string>()),
            DifficultyMin = Math.Clamp(dto.DifficultyMin ?? 1, 1, 5),
            DifficultyMax = Math.Clamp(dto.DifficultyMax ?? 5, 1, 5),
            DefaultSection = dto.DefaultSection ?? "thisWeek",
            DefaultContentPaperId = dto.DefaultContentPaperId,
            TagsCsv = dto.TagsCsv ?? "",
            IsArchived = false,
            CreatedByAdminId = adminId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.StudyPlanTaskTemplates.Add(entity);
        await LogAuditAsync(adminId, "Created", "StudyPlanTaskTemplate", entity.Id, $"Created task template '{entity.Title}' ({entity.Slug})", ct);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<StudyPlanTaskTemplate> UpdateTaskTemplateAsync(string id, TaskTemplateUpdate dto, string adminId, CancellationToken ct)
    {
        var entity = await db.StudyPlanTaskTemplates.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("task template not found");
        if (dto.Title is not null) entity.Title = dto.Title;
        if (dto.ItemType is not null) { ValidateItemType(dto.ItemType); entity.ItemType = dto.ItemType; }
        if (dto.DurationMinutes is int d) { if (d <= 0 || d > 600) throw new InvalidOperationException("DurationMinutes must be 1–600"); entity.DurationMinutes = d; }
        if (dto.RationaleMarkdown is not null) entity.RationaleMarkdown = dto.RationaleMarkdown;
        if (dto.ProfessionScope is not null) entity.ProfessionScopeJson = JsonSerializer.Serialize(dto.ProfessionScope);
        if (dto.TargetCountries is not null) entity.TargetCountriesJson = JsonSerializer.Serialize(dto.TargetCountries);
        if (dto.DifficultyMin is int dmin) entity.DifficultyMin = Math.Clamp(dmin, 1, 5);
        if (dto.DifficultyMax is int dmax) entity.DifficultyMax = Math.Clamp(dmax, 1, 5);
        if (dto.DefaultSection is not null) { ValidateSection(dto.DefaultSection); entity.DefaultSection = dto.DefaultSection; }
        if (dto.DefaultContentPaperId is not null) entity.DefaultContentPaperId = dto.DefaultContentPaperId.Length == 0 ? null : dto.DefaultContentPaperId;
        if (dto.TagsCsv is not null) entity.TagsCsv = dto.TagsCsv;
        if (dto.IsArchived is bool ar) entity.IsArchived = ar;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await LogAuditAsync(adminId, "Updated", "StudyPlanTaskTemplate", entity.Id, $"Updated task template '{entity.Title}'", ct);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task ArchiveTaskTemplateAsync(string id, string adminId, CancellationToken ct)
    {
        var entity = await db.StudyPlanTaskTemplates.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("task template not found");
        entity.IsArchived = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await LogAuditAsync(adminId, "Archived", "StudyPlanTaskTemplate", entity.Id, $"Archived task template '{entity.Title}'", ct);
        await db.SaveChangesAsync(ct);
    }

    // ── Plan Templates ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<StudyPlanTemplate>> ListPlanTemplatesAsync(bool includeArchived, CancellationToken ct)
    {
        IQueryable<StudyPlanTemplate> qry = db.StudyPlanTemplates.AsNoTracking();
        if (!includeArchived) qry = qry.Where(x => !x.IsArchived);
        return await qry.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<PlanTemplateDetail?> GetPlanTemplateDetailAsync(string id, CancellationToken ct)
    {
        var t = await db.StudyPlanTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return null;
        var items = await db.StudyPlanTemplateItems.AsNoTracking()
            .Where(x => x.PlanTemplateId == id)
            .OrderBy(x => x.Ordering).ToListAsync(ct);
        return new PlanTemplateDetail(t, items);
    }

    public async Task<StudyPlanTemplate> CreatePlanTemplateAsync(PlanTemplateCreate dto, string adminId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Slug) || string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("slug and name are required");
        if (await db.StudyPlanTemplates.AnyAsync(x => x.Slug == dto.Slug, ct))
            throw new InvalidOperationException($"plan template slug '{dto.Slug}' already exists");
        var entity = new StudyPlanTemplate
        {
            Id = $"spt-plan-{Guid.NewGuid():N}",
            Slug = dto.Slug,
            Name = dto.Name,
            Description = dto.Description ?? "",
            DurationWeeks = dto.DurationWeeks ?? 8,
            DefaultHoursPerWeek = dto.DefaultHoursPerWeek ?? 10,
            ExamFamilyCode = dto.ExamFamilyCode ?? "oet",
            IsArchived = false,
            CreatedByAdminId = adminId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.StudyPlanTemplates.Add(entity);
        await LogAuditAsync(adminId, "Created", "StudyPlanTemplate", entity.Id, $"Created plan template '{entity.Name}' ({entity.Slug})", ct);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<StudyPlanTemplate> UpdatePlanTemplateAsync(string id, PlanTemplateUpdate dto, string adminId, CancellationToken ct)
    {
        var entity = await db.StudyPlanTemplates.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("plan template not found");
        if (dto.Name is not null) entity.Name = dto.Name;
        if (dto.Description is not null) entity.Description = dto.Description;
        if (dto.DurationWeeks is int w) entity.DurationWeeks = Math.Clamp(w, 1, 52);
        if (dto.DefaultHoursPerWeek is int h) entity.DefaultHoursPerWeek = Math.Clamp(h, 1, 80);
        if (dto.IsArchived is bool ar) entity.IsArchived = ar;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await LogAuditAsync(adminId, "Updated", "StudyPlanTemplate", entity.Id, $"Updated plan template '{entity.Name}'", ct);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task ArchivePlanTemplateAsync(string id, string adminId, CancellationToken ct)
    {
        var entity = await db.StudyPlanTemplates.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("plan template not found");
        entity.IsArchived = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await LogAuditAsync(adminId, "Archived", "StudyPlanTemplate", entity.Id, $"Archived plan template '{entity.Name}'", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReplacePlanTemplateItemsAsync(string planTemplateId, IReadOnlyList<PlanTemplateItemUpsert> items, string adminId, CancellationToken ct)
    {
        var plan = await db.StudyPlanTemplates.FirstOrDefaultAsync(x => x.Id == planTemplateId, ct)
            ?? throw new InvalidOperationException("plan template not found");
        foreach (var it in items)
        {
            ValidateSection(it.Section);
            if (!await db.StudyPlanTaskTemplates.AnyAsync(x => x.Id == it.TaskTemplateId, ct))
                throw new InvalidOperationException($"task template {it.TaskTemplateId} not found");
        }
        var existing = await db.StudyPlanTemplateItems.Where(x => x.PlanTemplateId == planTemplateId).ToListAsync(ct);
        db.StudyPlanTemplateItems.RemoveRange(existing);
        var ord = 0;
        foreach (var it in items)
        {
            db.StudyPlanTemplateItems.Add(new StudyPlanTemplateItem
            {
                Id = $"spti-{Guid.NewGuid():N}",
                PlanTemplateId = planTemplateId,
                TaskTemplateId = it.TaskTemplateId,
                WeekOffset = Math.Max(0, it.WeekOffset),
                DayOffsetWithinWeek = Math.Clamp(it.DayOffsetWithinWeek, 0, 6),
                Section = it.Section,
                Priority = Math.Clamp(it.Priority, 0, 100),
                IsMandatory = it.IsMandatory,
                PrerequisiteItemTemplateId = it.PrerequisiteItemTemplateId,
                Ordering = it.Ordering == 0 ? ord : it.Ordering,
            });
            ord++;
        }
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await LogAuditAsync(adminId, "Updated", "StudyPlanTemplateItems", planTemplateId, $"Replaced {items.Count} items on plan template '{plan.Name}'", ct);
        await db.SaveChangesAsync(ct);
    }

    // ── Assignment Rules ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<StudyPlanAssignmentRule>> ListRulesAsync(bool includeInactive, CancellationToken ct)
    {
        IQueryable<StudyPlanAssignmentRule> qry = db.StudyPlanAssignmentRules.AsNoTracking();
        if (!includeInactive) qry = qry.Where(x => x.IsActive);
        return await qry.OrderBy(x => x.Priority).ThenByDescending(x => x.Weight).ToListAsync(ct);
    }

    public async Task<StudyPlanAssignmentRule?> GetRuleAsync(string id, CancellationToken ct)
        => await db.StudyPlanAssignmentRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<StudyPlanAssignmentRule> CreateRuleAsync(AssignmentRuleCreate dto, string adminId, CancellationToken ct)
    {
        ValidateConditionJson(dto.ConditionJson);
        if (!await db.StudyPlanTemplates.AnyAsync(x => x.Id == dto.TargetTemplateId && !x.IsArchived, ct))
            throw new InvalidOperationException($"target template {dto.TargetTemplateId} not found or archived");
        var entity = new StudyPlanAssignmentRule
        {
            Id = $"spar-{Guid.NewGuid():N}",
            Name = dto.Name,
            ExamFamilyCode = dto.ExamFamilyCode ?? "oet",
            Priority = dto.Priority ?? 100,
            Weight = dto.Weight ?? 50,
            ConditionJson = dto.ConditionJson,
            TargetTemplateId = dto.TargetTemplateId,
            IsActive = dto.IsActive ?? true,
            CreatedByAdminId = adminId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.StudyPlanAssignmentRules.Add(entity);
        await LogAuditAsync(adminId, "Created", "StudyPlanAssignmentRule", entity.Id, $"Created rule '{entity.Name}' → {entity.TargetTemplateId}", ct);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<StudyPlanAssignmentRule> UpdateRuleAsync(string id, AssignmentRuleUpdate dto, string adminId, CancellationToken ct)
    {
        var entity = await db.StudyPlanAssignmentRules.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("rule not found");
        if (dto.Name is not null) entity.Name = dto.Name;
        if (dto.Priority is int p) entity.Priority = p;
        if (dto.Weight is int w) entity.Weight = w;
        if (dto.ConditionJson is not null) { ValidateConditionJson(dto.ConditionJson); entity.ConditionJson = dto.ConditionJson; }
        if (dto.TargetTemplateId is not null)
        {
            if (!await db.StudyPlanTemplates.AnyAsync(x => x.Id == dto.TargetTemplateId && !x.IsArchived, ct))
                throw new InvalidOperationException($"target template {dto.TargetTemplateId} not found or archived");
            entity.TargetTemplateId = dto.TargetTemplateId;
        }
        if (dto.IsActive is bool active) entity.IsActive = active;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await LogAuditAsync(adminId, "Updated", "StudyPlanAssignmentRule", entity.Id, $"Updated rule '{entity.Name}'", ct);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteRuleAsync(string id, string adminId, CancellationToken ct)
    {
        var entity = await db.StudyPlanAssignmentRules.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("rule not found");
        db.StudyPlanAssignmentRules.Remove(entity);
        await LogAuditAsync(adminId, "Deleted", "StudyPlanAssignmentRule", entity.Id, $"Deleted rule '{entity.Name}'", ct);
        await db.SaveChangesAsync(ct);
    }

    // ── Drift Policy ───────────────────────────────────────────────────────

    public async Task<StudyPlanDriftPolicy> GetDriftPolicyAsync(string examFamilyCode, CancellationToken ct)
    {
        var p = await db.StudyPlanDriftPolicies.FirstOrDefaultAsync(x => x.ExamFamilyCode == examFamilyCode, ct);
        if (p is null)
        {
            p = new StudyPlanDriftPolicy
            {
                Id = $"spdp-{examFamilyCode}",
                ExamFamilyCode = examFamilyCode,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.StudyPlanDriftPolicies.Add(p);
            await db.SaveChangesAsync(ct);
        }
        return p;
    }

    public async Task<StudyPlanDriftPolicy> UpdateDriftPolicyAsync(string examFamilyCode, DriftPolicyUpdate dto, string adminId, CancellationToken ct)
    {
        var p = await GetDriftPolicyAsync(examFamilyCode, ct);
        if (dto.MildDays is int md) p.MildDays = Math.Clamp(md, 1, 365);
        if (dto.ModerateDays is int mod) p.ModerateDays = Math.Clamp(mod, 1, 365);
        if (dto.SevereDays is int sev) p.SevereDays = Math.Clamp(sev, 1, 365);
        if (p.MildDays >= p.ModerateDays || p.ModerateDays >= p.SevereDays)
            throw new InvalidOperationException("Thresholds must be strictly increasing: MildDays < ModerateDays < SevereDays");
        if (dto.MildCopy is not null) p.MildCopy = dto.MildCopy;
        if (dto.ModerateCopy is not null) p.ModerateCopy = dto.ModerateCopy;
        if (dto.SevereCopy is not null) p.SevereCopy = dto.SevereCopy;
        if (dto.OnTrackCopy is not null) p.OnTrackCopy = dto.OnTrackCopy;
        if (dto.AutoRegenerateOnModerate is bool m) p.AutoRegenerateOnModerate = m;
        if (dto.AutoRegenerateOnSevere is bool s) p.AutoRegenerateOnSevere = s;
        p.UpdatedByAdminId = adminId;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await LogAuditAsync(adminId, "Updated", "StudyPlanDriftPolicy", p.Id, $"Updated drift policy for '{examFamilyCode}'", ct);
        await db.SaveChangesAsync(ct);
        return p;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task LogAuditAsync(string adminId, string action, string resourceType, string? resourceId, string details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = "Admin",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
        await Task.CompletedTask;
    }

    private static void ValidateSubtest(string s)
    {
        if (!ValidSubtests.Contains(s))
            throw new InvalidOperationException($"invalid subtest '{s}'");
    }

    private static void ValidateSection(string s)
    {
        if (!ValidSections.Contains(s))
            throw new InvalidOperationException($"invalid section '{s}'");
    }

    private static void ValidateItemType(string s)
    {
        if (!ValidItemTypes.Contains(s))
            throw new InvalidOperationException($"invalid item type '{s}'");
    }

    private static void ValidateConditionJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"invalid condition JSON: {ex.Message}");
        }
    }
}
