using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingContentAuditEntry(
    string ResourceType,
    string ResourceId,
    string Action,
    string ActorId,
    DateTimeOffset OccurredAt,
    string? Details);

public interface IWritingContentAuditService
{
    Task LogAsync(string actorId, string resourceType, string resourceId, string action, string? details, CancellationToken ct);
    Task<IReadOnlyList<WritingContentAuditEntry>> RecentAsync(string resourceType, int take, CancellationToken ct);
    bool ValidateStateTransition(string currentStatus, string nextStatus);

    Task<WritingContentAuditListResponse> ListAuditEntriesAsync(string adminUserId, string? entityType, string? action, int page, int pageSize, CancellationToken ct);
}

public sealed class WritingContentAuditService(LearnerDbContext db, TimeProvider clock) : IWritingContentAuditService
{
    private static readonly HashSet<(string From, string To)> AllowedTransitions = new()
    {
        ("draft", "review"),
        ("review", "published"),
        ("review", "draft"),
        ("published", "deprecated"),
        ("deprecated", "draft"),
    };

    public async Task LogAsync(string actorId, string resourceType, string resourceId, string action, string? details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = actorId ?? "system",
            ActorName = actorId ?? "system",
            Action = string.IsNullOrWhiteSpace(action) ? "writing.content.unknown" : action,
            ResourceType = string.IsNullOrWhiteSpace(resourceType) ? "Writing" : resourceType,
            ResourceId = resourceId,
            Details = details,
            OccurredAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WritingContentAuditEntry>> RecentAsync(string resourceType, int take, CancellationToken ct)
    {
        var rows = await db.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceType == resourceType && a.Action.StartsWith("writing."))
            .OrderByDescending(a => a.OccurredAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        return rows.Select(r => new WritingContentAuditEntry(r.ResourceType, r.ResourceId, r.Action, r.ActorId, r.OccurredAt, r.Details)).ToList();
    }

    public bool ValidateStateTransition(string currentStatus, string nextStatus)
    {
        var from = (currentStatus ?? string.Empty).Trim().ToLowerInvariant();
        var to = (nextStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (from == to) return true;
        return AllowedTransitions.Contains((from, to));
    }

    public async Task<WritingContentAuditListResponse> ListAuditEntriesAsync(string adminUserId, string? entityType, string? action, int page, int pageSize, CancellationToken ct)
    {
        _ = adminUserId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.AuditEvents.AsNoTracking().Where(a => a.Action.StartsWith("writing."));
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.ResourceType == entityType);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        var rows = await query.OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(a => new WritingContentAuditEntryResponse(
            Id: Guid.TryParse(a.Id, out var g) ? g : Guid.Empty,
            EntityType: a.ResourceType,
            EntityId: a.ResourceId,
            Action: a.Action,
            ActorUserId: a.ActorId,
            Note: a.Details,
            OccurredAt: a.OccurredAt)).ToList();
        return new WritingContentAuditListResponse(items);
    }
}
