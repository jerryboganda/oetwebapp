using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

public sealed record WritingCommonMistakeView(
    Guid Id,
    string Category,
    string Summary,
    string ExampleWrong,
    string ExampleRight,
    string? CanonRuleId,
    string? RelatedSubSkill);

public sealed record WritingLearnerMistakeStatView(
    Guid MistakeId,
    int OccurrenceCount,
    DateTimeOffset LastOccurredAt,
    DateTimeOffset FirstOccurredAt);

public interface IWritingMistakeService
{
    Task<IReadOnlyList<WritingCommonMistakeView>> ListAsync(string userId, string? category, string? subSkill, CancellationToken ct);
    Task<WritingCommonMistakeView?> GetAsync(string userId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<WritingLearnerMistakeStatView>> ListMineAsync(string userId, CancellationToken ct);
    Task IncrementForCanonViolationsAsync(string userId, IEnumerable<WritingCanonViolation> violations, CancellationToken ct);
    Task<WritingCommonMistakeView> UpsertAsync(string adminId, WritingCommonMistakeView mistake, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingCommonMistakeListResponse> ListCommonMistakesAsync(string userId, string? category, string? subSkill, CancellationToken ct);
    Task<WritingCommonMistakeResponse?> GetCommonMistakeAsync(string userId, Guid id, CancellationToken ct);
    Task<WritingLearnerMistakeListResponse> ListMyMistakesAsync(string userId, CancellationToken ct);
    Task<WritingCommonMistakeListResponse> AdminListMistakesAsync(string adminUserId, string? category, string? subSkill, CancellationToken ct);
    Task<WritingCommonMistakeResponse> AdminCreateMistakeAsync(string adminUserId, WritingMistakeUpsertRequest request, CancellationToken ct);
    Task<WritingCommonMistakeResponse?> AdminGetMistakeAsync(string adminUserId, Guid id, CancellationToken ct);
    Task<WritingCommonMistakeResponse?> AdminUpdateMistakeAsync(string adminUserId, Guid id, WritingMistakeUpsertRequest request, CancellationToken ct);
    Task<bool> AdminDeleteMistakeAsync(string adminUserId, Guid id, CancellationToken ct);
}

public sealed class WritingMistakeService(LearnerDbContext db, TimeProvider clock) : IWritingMistakeService
{
    public async Task<IReadOnlyList<WritingCommonMistakeView>> ListAsync(string userId, string? category, string? subSkill, CancellationToken ct)
    {
        _ = userId;
        var query = db.WritingCommonMistakes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(m => m.Category == category);
        if (!string.IsNullOrWhiteSpace(subSkill)) query = query.Where(m => m.RelatedSubSkill == subSkill);
        var rows = await query.OrderBy(m => m.Category).ThenBy(m => m.Summary).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    public async Task<WritingCommonMistakeView?> GetAsync(string userId, Guid id, CancellationToken ct)
    {
        _ = userId;
        var row = await db.WritingCommonMistakes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return row is null ? null : ToView(row);
    }

    public async Task<IReadOnlyList<WritingLearnerMistakeStatView>> ListMineAsync(string userId, CancellationToken ct)
    {
        var persistedRows = await db.WritingLearnerMistakeStats.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var merged = persistedRows.ToDictionary(
            row => row.MistakeId,
            row => new WritingLearnerMistakeStatView(row.MistakeId, row.OccurrenceCount, row.LastOccurredAt, row.FirstOccurredAt));

        foreach (var ruleStat in await BuildRuleViolationMistakeStatsAsync(userId, ct))
        {
            if (merged.TryGetValue(ruleStat.MistakeId, out var existing))
            {
                merged[ruleStat.MistakeId] = existing with
                {
                    OccurrenceCount = existing.OccurrenceCount + ruleStat.OccurrenceCount,
                    FirstOccurredAt = existing.FirstOccurredAt <= ruleStat.FirstOccurredAt ? existing.FirstOccurredAt : ruleStat.FirstOccurredAt,
                    LastOccurredAt = existing.LastOccurredAt >= ruleStat.LastOccurredAt ? existing.LastOccurredAt : ruleStat.LastOccurredAt,
                };
            }
            else
            {
                merged[ruleStat.MistakeId] = ruleStat;
            }
        }

        return merged.Values
            .OrderByDescending(s => s.OccurrenceCount)
            .ThenByDescending(s => s.LastOccurredAt)
            .Take(50)
            .ToList();
    }

    private async Task<IReadOnlyList<WritingLearnerMistakeStatView>> BuildRuleViolationMistakeStatsAsync(string userId, CancellationToken ct)
    {
        var mistakes = await db.WritingCommonMistakes.AsNoTracking()
            .Where(m => m.CanonRuleId != null)
            .Select(m => new { m.Id, m.CanonRuleId })
            .ToListAsync(ct);
        if (mistakes.Count == 0) return Array.Empty<WritingLearnerMistakeStatView>();

        var byRule = mistakes
            .Where(m => !string.IsNullOrWhiteSpace(m.CanonRuleId))
            .GroupBy(m => m.CanonRuleId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToList(), StringComparer.OrdinalIgnoreCase);
        if (byRule.Count == 0) return Array.Empty<WritingLearnerMistakeStatView>();

        var ruleIds = byRule.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var violations = await db.WritingRuleViolations.AsNoTracking()
            .Where(v => v.UserId == userId)
            .Select(v => new { v.RuleId, v.GeneratedAt })
            .ToListAsync(ct);

        var grouped = violations
            .Where(v => ruleIds.Contains(v.RuleId))
            .GroupBy(v => v.RuleId, StringComparer.OrdinalIgnoreCase);

        var results = new List<WritingLearnerMistakeStatView>();
        foreach (var group in grouped)
        {
            if (!byRule.TryGetValue(group.Key, out var mistakeIds)) continue;
            var count = group.Count();
            var first = group.Min(v => v.GeneratedAt);
            var last = group.Max(v => v.GeneratedAt);
            results.AddRange(mistakeIds.Select(id => new WritingLearnerMistakeStatView(id, count, last, first)));
        }

        return results;
    }

    public async Task IncrementForCanonViolationsAsync(string userId, IEnumerable<WritingCanonViolation> violations, CancellationToken ct)
    {
        var byRule = violations.GroupBy(v => v.RuleId).ToDictionary(g => g.Key, g => g.Count());
        if (byRule.Count == 0) return;
        var ruleIds = byRule.Keys.ToList();
        var mistakes = await db.WritingCommonMistakes.AsNoTracking()
            .Where(m => m.CanonRuleId != null && ruleIds.Contains(m.CanonRuleId))
            .ToListAsync(ct);
        if (mistakes.Count == 0) return;
        var now = clock.GetUtcNow();
        foreach (var mistake in mistakes)
        {
            var occurrences = byRule.GetValueOrDefault(mistake.CanonRuleId!);
            if (occurrences <= 0) continue;
            var existing = await db.WritingLearnerMistakeStats.FirstOrDefaultAsync(s => s.UserId == userId && s.MistakeId == mistake.Id, ct);
            if (existing is null)
            {
                db.WritingLearnerMistakeStats.Add(new WritingLearnerMistakeStat
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    MistakeId = mistake.Id,
                    OccurrenceCount = occurrences,
                    FirstOccurredAt = now,
                    LastOccurredAt = now,
                });
            }
            else
            {
                existing.OccurrenceCount += occurrences;
                existing.LastOccurredAt = now;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<WritingCommonMistakeView> UpsertAsync(string adminId, WritingCommonMistakeView mistake, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mistake);
        var now = clock.GetUtcNow();
        var entity = mistake.Id != Guid.Empty
            ? await db.WritingCommonMistakes.FirstOrDefaultAsync(m => m.Id == mistake.Id, ct)
            : null;
        var created = entity is null;
        if (entity is null)
        {
            entity = new WritingCommonMistake
            {
                Id = mistake.Id == Guid.Empty ? Guid.NewGuid() : mistake.Id,
                CreatedAt = now,
            };
            db.WritingCommonMistakes.Add(entity);
        }
        entity.Category = mistake.Category;
        entity.Summary = mistake.Summary;
        entity.ExampleWrong = mistake.ExampleWrong;
        entity.ExampleRight = mistake.ExampleRight;
        entity.CanonRuleId = mistake.CanonRuleId;
        entity.RelatedSubSkill = mistake.RelatedSubSkill;
        AddAuditEvent(adminId, "WritingCommonMistake", entity.Id.ToString("D"), created ? "writing.mistake.created" : "writing.mistake.updated", entity.Summary);
        await db.SaveChangesAsync(ct);
        return ToView(entity);
    }

    private static WritingCommonMistakeView ToView(WritingCommonMistake row)
        => new(row.Id, row.Category, row.Summary, row.ExampleWrong, row.ExampleRight, row.CanonRuleId, row.RelatedSubSkill);

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingCommonMistakeListResponse> ListCommonMistakesAsync(string userId, string? category, string? subSkill, CancellationToken ct)
    {
        var rows = await ListAsync(userId, category, subSkill, ct);
        return new WritingCommonMistakeListResponse(rows.Select(WritingV2ResponseMapper.ToResponse).ToList());
    }

    public async Task<WritingCommonMistakeResponse?> GetCommonMistakeAsync(string userId, Guid id, CancellationToken ct)
    {
        var view = await GetAsync(userId, id, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingLearnerMistakeListResponse> ListMyMistakesAsync(string userId, CancellationToken ct)
    {
        var stats = await ListMineAsync(userId, ct);
        if (stats.Count == 0) return new WritingLearnerMistakeListResponse(Array.Empty<WritingLearnerMistakeRowResponse>());
        var ids = stats.Select(s => s.MistakeId).ToList();
        var mistakes = await db.WritingCommonMistakes.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m, ct);
        var rows = new List<WritingLearnerMistakeRowResponse>();
        foreach (var stat in stats)
        {
            if (!mistakes.TryGetValue(stat.MistakeId, out var m)) continue;
            rows.Add(new WritingLearnerMistakeRowResponse(
                Id: m.Id,
                Category: m.Category,
                Summary: m.Summary,
                ExampleWrong: m.ExampleWrong,
                ExampleRight: m.ExampleRight,
                CanonRuleId: m.CanonRuleId,
                RelatedSubSkill: m.RelatedSubSkill,
                Stat: new WritingLearnerMistakeStatResponse(stat.MistakeId, stat.OccurrenceCount, stat.LastOccurredAt)));
        }
        return new WritingLearnerMistakeListResponse(rows);
    }

    public async Task<WritingCommonMistakeListResponse> AdminListMistakesAsync(string adminUserId, string? category, string? subSkill, CancellationToken ct)
    {
        var rows = await ListAsync(adminUserId, category, subSkill, ct);
        return new WritingCommonMistakeListResponse(rows.Select(WritingV2ResponseMapper.ToResponse).ToList());
    }

    public async Task<WritingCommonMistakeResponse> AdminCreateMistakeAsync(string adminUserId, WritingMistakeUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var view = new WritingCommonMistakeView(
            Id: Guid.Empty,
            Category: request.Category,
            Summary: request.Summary,
            ExampleWrong: request.ExampleWrong,
            ExampleRight: request.ExampleRight,
            CanonRuleId: request.CanonRuleId,
            RelatedSubSkill: request.RelatedSubSkill);
        var saved = await UpsertAsync(adminUserId, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<WritingCommonMistakeResponse?> AdminGetMistakeAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        var view = await GetAsync(adminUserId, id, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingCommonMistakeResponse?> AdminUpdateMistakeAsync(string adminUserId, Guid id, WritingMistakeUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exists = await db.WritingCommonMistakes.AsNoTracking().AnyAsync(m => m.Id == id, ct);
        if (!exists) return null;
        var view = new WritingCommonMistakeView(
            Id: id,
            Category: request.Category,
            Summary: request.Summary,
            ExampleWrong: request.ExampleWrong,
            ExampleRight: request.ExampleRight,
            CanonRuleId: request.CanonRuleId,
            RelatedSubSkill: request.RelatedSubSkill);
        var saved = await UpsertAsync(adminUserId, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<bool> AdminDeleteMistakeAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        var entity = await db.WritingCommonMistakes.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (entity is null) return false;
        db.WritingCommonMistakes.Remove(entity);
        AddAuditEvent(adminUserId, "WritingCommonMistake", id.ToString("D"), "writing.mistake.deleted", entity.Summary);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private void AddAuditEvent(string actorId, string resourceType, string resourceId, string action, string? details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            ActorName = string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
            OccurredAt = clock.GetUtcNow(),
        });
    }
}
