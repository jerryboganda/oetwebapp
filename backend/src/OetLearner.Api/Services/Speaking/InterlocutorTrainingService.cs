using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

// Phase 6 of the OET Speaking module roadmap.
//
// CRUD + completion tracking for interlocutor training modules. Admins
// author + publish the markdown content; tutors mark each module
// complete before they can be added to the live calibration pool.
//
// Modules with `RequiredForCalibration = true` AND `Status = Published`
// gate live-room eligibility — see <see cref="IsTutorEligibleForLiveRoomsAsync"/>.
public sealed class InterlocutorTrainingService(LearnerDbContext db)
{
    private readonly TimeProvider _clock = TimeProvider.System;

    // ── Admin ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<object>> ListModulesAsync(
        string? stage,
        string? status,
        CancellationToken ct)
    {
        IQueryable<InterlocutorTrainingModule> q = db.InterlocutorTrainingModules.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(stage)
            && Enum.TryParse<InterlocutorTrainingStage>(stage, ignoreCase: true, out var stageEnum))
        {
            q = q.Where(m => m.Stage == stageEnum);
        }

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<ContentStatus>(status, ignoreCase: true, out var statusEnum))
        {
            q = q.Where(m => m.Status == statusEnum);
        }

        var rows = await q
            .OrderBy(m => m.Stage)
            .ThenBy(m => m.OrderIndex)
            .ThenByDescending(m => m.UpdatedAt)
            .ToListAsync(ct);

        return rows.Select(Project).ToArray();
    }

    public async Task<object?> GetModuleAsync(string id, CancellationToken ct)
    {
        var row = await db.InterlocutorTrainingModules
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        return row is null ? null : Project(row);
    }

    public async Task<object> CreateModuleAsync(
        string adminId,
        string adminName,
        string title,
        string contentMarkdown,
        string stage,
        int orderIndex,
        bool requiredForCalibration,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw ApiException.Validation("title_required", "Title is required.");
        }
        if (!Enum.TryParse<InterlocutorTrainingStage>(stage, ignoreCase: true, out var stageEnum))
        {
            throw ApiException.Validation("invalid_stage", $"Unknown stage '{stage}'.");
        }

        var now = _clock.GetUtcNow();
        var row = new InterlocutorTrainingModule
        {
            Id = $"itm-{Guid.NewGuid():N}",
            Title = title.Trim(),
            ContentMarkdown = contentMarkdown ?? string.Empty,
            Stage = stageEnum,
            OrderIndex = orderIndex,
            RequiredForCalibration = requiredForCalibration,
            Status = ContentStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.InterlocutorTrainingModules.Add(row);
        AddAuditEvent(adminId, adminName, "InterlocutorTrainingModuleCreated", row.Id, row.Title);
        await db.SaveChangesAsync(ct);
        return Project(row);
    }

    public async Task<object> UpdateModuleAsync(
        string adminId,
        string adminName,
        string id,
        string? title,
        string? contentMarkdown,
        string? stage,
        int? orderIndex,
        bool? requiredForCalibration,
        CancellationToken ct)
    {
        var row = await db.InterlocutorTrainingModules.FirstOrDefaultAsync(m => m.Id == id, ct)
                  ?? throw ApiException.NotFound("module_not_found", "Module not found.");

        if (!string.IsNullOrWhiteSpace(title)) row.Title = title.Trim();
        if (contentMarkdown is not null) row.ContentMarkdown = contentMarkdown;
        if (!string.IsNullOrWhiteSpace(stage))
        {
            if (!Enum.TryParse<InterlocutorTrainingStage>(stage, ignoreCase: true, out var stageEnum))
            {
                throw ApiException.Validation("invalid_stage", $"Unknown stage '{stage}'.");
            }
            row.Stage = stageEnum;
        }
        if (orderIndex.HasValue) row.OrderIndex = orderIndex.Value;
        if (requiredForCalibration.HasValue) row.RequiredForCalibration = requiredForCalibration.Value;
        row.UpdatedAt = _clock.GetUtcNow();
        AddAuditEvent(adminId, adminName, "InterlocutorTrainingModuleUpdated", row.Id, row.Title);
        await db.SaveChangesAsync(ct);
        return Project(row);
    }

    public async Task<object> PublishModuleAsync(
        string adminId,
        string adminName,
        string id,
        CancellationToken ct)
    {
        var row = await db.InterlocutorTrainingModules.FirstOrDefaultAsync(m => m.Id == id, ct)
                  ?? throw ApiException.NotFound("module_not_found", "Module not found.");
        var now = _clock.GetUtcNow();
        row.Status = ContentStatus.Published;
        row.PublishedAt ??= now;
        row.UpdatedAt = now;
        AddAuditEvent(adminId, adminName, "InterlocutorTrainingModulePublished", row.Id, row.Title);
        await db.SaveChangesAsync(ct);
        return Project(row);
    }

    public async Task<object> ArchiveModuleAsync(
        string adminId,
        string adminName,
        string id,
        CancellationToken ct)
    {
        var row = await db.InterlocutorTrainingModules.FirstOrDefaultAsync(m => m.Id == id, ct)
                  ?? throw ApiException.NotFound("module_not_found", "Module not found.");
        row.Status = ContentStatus.Archived;
        row.UpdatedAt = _clock.GetUtcNow();
        AddAuditEvent(adminId, adminName, "InterlocutorTrainingModuleArchived", row.Id, row.Title);
        await db.SaveChangesAsync(ct);
        return Project(row);
    }

    // ── Tutor ──────────────────────────────────────────────────────────────

    public async Task<object> ListForTutorAsync(string tutorId, CancellationToken ct)
    {
        var modules = await db.InterlocutorTrainingModules
            .AsNoTracking()
            .Where(m => m.Status == ContentStatus.Published)
            .OrderBy(m => m.Stage)
            .ThenBy(m => m.OrderIndex)
            .ToListAsync(ct);

        var moduleIds = modules.Select(m => m.Id).ToList();
        var progress = await db.InterlocutorTrainingProgress
            .AsNoTracking()
            .Where(p => p.TutorId == tutorId && moduleIds.Contains(p.ModuleId))
            .ToListAsync(ct);

        var byId = progress.ToDictionary(p => p.ModuleId, p => p);

        var items = modules.Select(m => new
        {
            id = m.Id,
            title = m.Title,
            stage = m.Stage.ToString(),
            orderIndex = m.OrderIndex,
            requiredForCalibration = m.RequiredForCalibration,
            contentMarkdown = m.ContentMarkdown,
            status = m.Status.ToString(),
            publishedAt = m.PublishedAt,
            completedAt = byId.TryGetValue(m.Id, out var p) ? p.CompletedAt : null,
            quizScore = byId.TryGetValue(m.Id, out var p2) ? p2.QuizScore : null,
            isCompleted = byId.TryGetValue(m.Id, out var p3) && p3.CompletedAt.HasValue,
        }).ToArray();

        var eligible = await IsTutorEligibleForLiveRoomsAsync(tutorId, ct);
        return new { modules = items, isEligibleForLiveRooms = eligible };
    }

    public async Task<object> MarkCompleteAsync(
        string tutorId,
        string moduleId,
        int? quizScore,
        CancellationToken ct)
    {
        var module = await db.InterlocutorTrainingModules
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == moduleId, ct)
            ?? throw ApiException.NotFound("module_not_found", "Module not found.");
        if (module.Status != ContentStatus.Published)
        {
            throw ApiException.Validation("module_not_published", "Module is not published.");
        }

        var now = _clock.GetUtcNow();
        var existing = await db.InterlocutorTrainingProgress
            .FirstOrDefaultAsync(p => p.TutorId == tutorId && p.ModuleId == moduleId, ct);
        if (existing is null)
        {
            existing = new InterlocutorTrainingProgress
            {
                Id = $"itp-{Guid.NewGuid():N}",
                TutorId = tutorId,
                ModuleId = moduleId,
                CompletedAt = now,
                QuizScore = quizScore,
                CreatedAt = now,
            };
            db.InterlocutorTrainingProgress.Add(existing);
        }
        else
        {
            existing.CompletedAt ??= now;
            if (quizScore.HasValue) existing.QuizScore = quizScore;
        }
        await db.SaveChangesAsync(ct);

        return new
        {
            moduleId,
            completedAt = existing.CompletedAt,
            quizScore = existing.QuizScore,
        };
    }

    // ── Eligibility ────────────────────────────────────────────────────────

    public async Task<bool> IsTutorEligibleForLiveRoomsAsync(string tutorId, CancellationToken ct)
    {
        var required = await db.InterlocutorTrainingModules
            .AsNoTracking()
            .Where(m => m.Status == ContentStatus.Published && m.RequiredForCalibration)
            .Select(m => m.Id)
            .ToListAsync(ct);
        if (required.Count == 0) return true;

        var completed = await db.InterlocutorTrainingProgress
            .AsNoTracking()
            .Where(p => p.TutorId == tutorId && required.Contains(p.ModuleId) && p.CompletedAt.HasValue)
            .Select(p => p.ModuleId)
            .ToListAsync(ct);

        return required.All(id => completed.Contains(id));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static object Project(InterlocutorTrainingModule m) => new
    {
        id = m.Id,
        title = m.Title,
        orderIndex = m.OrderIndex,
        contentMarkdown = m.ContentMarkdown,
        requiredForCalibration = m.RequiredForCalibration,
        stage = m.Stage.ToString(),
        status = m.Status.ToString(),
        createdAt = m.CreatedAt,
        updatedAt = m.UpdatedAt,
        publishedAt = m.PublishedAt,
    };

    private void AddAuditEvent(string actorId, string actorName, string action, string resourceId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = _clock.GetUtcNow(),
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = "InterlocutorTrainingModule",
            ResourceId = resourceId,
            Details = details,
        });
    }
}
