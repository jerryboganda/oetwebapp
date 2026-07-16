using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Professions;

// ═════════════════════════════════════════════════════════════════════════════
// Canonical profession taxonomy.
//
// SignupProfessionCatalog is the platform-wide source of truth: registration,
// billing plans, content tagging and the discipline filters must all agree on
// the same ids. The legacy Professions reference table (ProfessionReference) is
// a mirror kept in sync from it — never author into it directly.
//
// Read-through cached for 5 minutes; the catalog changes only on admin CRUD.
// ═════════════════════════════════════════════════════════════════════════════

/// <param name="Id">Canonical profession id, e.g. "other-allied-health".</param>
public sealed record ProfessionCatalogEntry(
    string Id,
    string Label,
    string Description,
    int SortOrder,
    bool IsActive);

public interface IProfessionCatalogService
{
    /// <summary>Active catalog entries in signup-UI order (SortOrder, then Id).</summary>
    Task<IReadOnlyList<ProfessionCatalogEntry>> GetActiveAsync(CancellationToken ct);

    /// <summary>Every catalog entry, active or archived, in signup-UI order.</summary>
    Task<IReadOnlyList<ProfessionCatalogEntry>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// True when the id names an ACTIVE profession. Use for anything a user or an
    /// admin is choosing right now (profile writes, plan validation); an archived
    /// profession stays resolvable via <see cref="GetAllAsync"/> for display of
    /// records that already carry it.
    /// </summary>
    Task<bool> IsValidProfessionIdAsync(string? professionId, CancellationToken ct);

    /// <summary>Resolve one entry (active or archived), or null when unknown.</summary>
    Task<ProfessionCatalogEntry?> FindAsync(string? professionId, CancellationToken ct);

    /// <summary>Drop the cached catalog; next read re-fetches from the DB.</summary>
    void Invalidate();
}

public sealed class ProfessionCatalogService(LearnerDbContext db, IMemoryCache cache)
    : IProfessionCatalogService
{
    private const string CacheKey = "profession-catalog:all:v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<ProfessionCatalogEntry>> GetAllAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<ProfessionCatalogEntry>? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await db.SignupProfessionCatalog
            .AsNoTracking()
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new ProfessionCatalogEntry(
                item.Id,
                item.Label,
                item.Description,
                item.SortOrder,
                item.IsActive))
            .ToListAsync(ct);

        cache.Set(CacheKey, (IReadOnlyList<ProfessionCatalogEntry>)rows, CacheTtl);
        return rows;
    }

    public async Task<IReadOnlyList<ProfessionCatalogEntry>> GetActiveAsync(CancellationToken ct)
        => (await GetAllAsync(ct)).Where(item => item.IsActive).ToList();

    public async Task<bool> IsValidProfessionIdAsync(string? professionId, CancellationToken ct)
    {
        var entry = await FindAsync(professionId, ct);
        return entry is { IsActive: true };
    }

    public async Task<ProfessionCatalogEntry?> FindAsync(string? professionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(professionId)) return null;
        var normalized = professionId.Trim();
        return (await GetAllAsync(ct))
            .FirstOrDefault(item => string.Equals(item.Id, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Invalidate() => cache.Remove(CacheKey);
}

/// <summary>
/// Reconciles the legacy <c>Professions</c> reference table against the canonical
/// SignupProfessionCatalog on every boot.
///
/// <para>
/// AdminService.SyncProfessionTaxonomyAsync only mirrors a row when an admin
/// creates or edits it, so any profession that arrived through the seeder — as
/// <c>other-allied-health</c> did — never propagated, and the discipline filters
/// that join on the reference id fell through for those learners. Reconciling at
/// startup closes that gap for every environment without asking each read path to
/// remember. Strictly additive: rows are inserted or refreshed, never deleted,
/// because content tags and mock plans reference them by id.
/// </para>
/// </summary>
public sealed class ProfessionTaxonomySyncStartupTask(
    IServiceScopeFactory scopeFactory,
    ILogger<ProfessionTaxonomySyncStartupTask> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            var catalog = await db.SignupProfessionCatalog.AsNoTracking().ToListAsync(ct);
            if (catalog.Count == 0)
            {
                logger.LogInformation("Profession taxonomy sync skipped; signup catalog is empty.");
                return;
            }

            var existing = await db.Professions.ToDictionaryAsync(p => p.Id, StringComparer.Ordinal, ct);
            var inserted = 0;
            var updated = 0;

            foreach (var item in catalog)
            {
                var status = item.IsActive ? "active" : "archived";
                if (!existing.TryGetValue(item.Id, out var reference))
                {
                    db.Professions.Add(new ProfessionReference
                    {
                        Id = item.Id,
                        Code = item.Id,
                        Label = item.Label,
                        Status = status,
                        SortOrder = item.SortOrder,
                    });
                    inserted++;
                    continue;
                }

                if (reference.Label == item.Label && reference.Status == status && reference.SortOrder == item.SortOrder)
                {
                    continue;
                }

                reference.Label = item.Label;
                reference.Status = status;
                reference.SortOrder = item.SortOrder;
                updated++;
            }

            if (inserted == 0 && updated == 0) return;

            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Profession taxonomy synced from signup catalog ({Inserted} inserted, {Updated} updated).",
                inserted, updated);
        }
        catch (Exception ex)
        {
            // Never block boot on a taxonomy refresh; the previous mirror stays live.
            logger.LogWarning(ex, "Profession taxonomy sync skipped (non-fatal).");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
