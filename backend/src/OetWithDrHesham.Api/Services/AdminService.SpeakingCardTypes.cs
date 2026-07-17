using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services;

// Speaking module rebuild (2026-06-11 spec).
//
// Admin CRUD for the fully-configurable `SpeakingCardType` taxonomy. The owner
// adds ~6 types later (e.g. "Card 4 – Examination Card"). The type is HIDDEN
// from students at all times — it is surfaced only on admin/tutor paths and
// passed to the AI scorer as marking guidance.
//
// Wired into the route surface from `AdminSpeakingContentEndpoints.cs`.
public partial class AdminService
{
    public async Task<IReadOnlyList<AdminSpeakingCardTypeDetail>> ListSpeakingCardTypesAsync(
        bool includeInactive,
        CancellationToken ct)
    {
        var q = db.SpeakingCardTypes.AsNoTracking();
        if (!includeInactive)
        {
            q = q.Where(t => t.IsActive);
        }

        var rows = await q
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToArray();
        var counts = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.CardTypeId != null && ids.Contains(c.CardTypeId!))
            .GroupBy(c => c.CardTypeId!)
            .Select(g => new { TypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TypeId, x => x.Count, ct);

        return rows
            .Select(t => Project(t, counts.TryGetValue(t.Id, out var c) ? c : 0))
            .ToList();
    }

    public async Task<AdminSpeakingCardTypeDetail> GetSpeakingCardTypeAsync(string id, CancellationToken ct)
    {
        var row = await db.SpeakingCardTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ApiException.NotFound("speaking_card_type_not_found", "That card type does not exist.");
        var count = await db.RolePlayCards.AsNoTracking().CountAsync(c => c.CardTypeId == id, ct);
        return Project(row, count);
    }

    public async Task<AdminSpeakingCardTypeDetail> CreateSpeakingCardTypeAsync(
        string adminId, string adminName, AdminSpeakingCardTypeUpsertRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Name))
        {
            throw ApiException.Validation("SPEAKING_CARD_TYPE_NAME_REQUIRED", "Card type name is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var row = new SpeakingCardType
        {
            Id = $"sct-{Guid.NewGuid():N}",
            Name = req.Name.Trim(),
            Description = req.Description?.Trim() ?? string.Empty,
            SortOrder = req.SortOrder ?? 0,
            IsActive = req.IsActive ?? true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.SpeakingCardTypes.Add(row);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "SpeakingCardType", row.Id,
            $"Created speaking card type: {row.Name}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return Project(row, 0);
    }

    public async Task<AdminSpeakingCardTypeDetail> UpdateSpeakingCardTypeAsync(
        string adminId, string adminName, string id, AdminSpeakingCardTypeUpsertRequest req, CancellationToken ct)
    {
        var row = await db.SpeakingCardTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ApiException.NotFound("speaking_card_type_not_found", "That card type does not exist.");
        if (req is null)
        {
            throw ApiException.Validation("SPEAKING_CARD_TYPE_BODY_REQUIRED", "A request body is required.");
        }

        if (!string.IsNullOrWhiteSpace(req.Name)) row.Name = req.Name.Trim();
        if (req.Description is not null) row.Description = req.Description.Trim();
        if (req.SortOrder.HasValue) row.SortOrder = req.SortOrder.Value;
        if (req.IsActive.HasValue) row.IsActive = req.IsActive.Value;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "SpeakingCardType", row.Id,
            $"Updated speaking card type: {row.Name}", ct);
        await CommitIfOwnedAsync(tx, ct);

        var count = await db.RolePlayCards.AsNoTracking().CountAsync(c => c.CardTypeId == id, ct);
        return Project(row, count);
    }

    /// <summary>Soft-deletes (deactivates) a card type when it is referenced by
    /// cards so historical cards keep their label; hard-deletes when unused.</summary>
    public async Task<object> DeleteSpeakingCardTypeAsync(
        string adminId, string adminName, string id, CancellationToken ct)
    {
        var row = await db.SpeakingCardTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ApiException.NotFound("speaking_card_type_not_found", "That card type does not exist.");

        var referenced = await db.RolePlayCards.AsNoTracking().AnyAsync(c => c.CardTypeId == id, ct);

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        string action;
        if (referenced)
        {
            row.IsActive = false;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            action = "Deactivated";
        }
        else
        {
            db.SpeakingCardTypes.Remove(row);
            action = "Deleted";
        }
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, action, "SpeakingCardType", id,
            $"{action} speaking card type: {row.Name}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id, action = action.ToLowerInvariant(), softDeleted = referenced };
    }

    private static AdminSpeakingCardTypeDetail Project(SpeakingCardType row, int cardCount)
        => new(
            Id: row.Id,
            Name: row.Name,
            Description: row.Description,
            SortOrder: row.SortOrder,
            IsActive: row.IsActive,
            CardCount: cardCount,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt);
}
