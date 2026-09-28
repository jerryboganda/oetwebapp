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
}
