using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Caching;

/// <summary>
/// Process-local cache for reference / catalog tables that are mutated
/// rarely (admin CRUD only) and read on most learner-facing requests.
///
/// Returns the full row set; callers apply their own predicates with
/// LINQ-to-objects on the cached list. Cardinality is small (tens of
/// rows) so this is materially cheaper than a per-request round-trip.
///
/// Admin write paths MUST call the matching <c>Invalidate*</c> after
/// committing changes so other instances re-load on next read. With the
/// current single-VPS deployment this is sufficient; once we scale
/// horizontally, swap the backing store for <c>IDistributedCache</c>
/// (Redis) without changing this contract.
/// </summary>
public interface IReferenceDataCache
{
    Task<IReadOnlyList<BillingPlan>> GetBillingPlansAsync(CancellationToken ct);
    Task<IReadOnlyList<BillingAddOn>> GetBillingAddOnsAsync(CancellationToken ct);
    Task<IReadOnlyList<ProfessionReference>> GetProfessionsAsync(CancellationToken ct);
    Task<IReadOnlyList<ExamFamily>> GetExamFamiliesAsync(CancellationToken ct);

    void InvalidateBillingPlans();
    void InvalidateBillingAddOns();
    void InvalidateProfessions();
    void InvalidateExamFamilies();
}

public sealed class ReferenceDataCache(LearnerDbContext db, IMemoryCache cache) : IReferenceDataCache
{
    private const string KeyBillingPlans = "ref:billing-plans";
    private const string KeyBillingAddOns = "ref:billing-addons";
    private const string KeyProfessions = "ref:professions";
    private const string KeyExamFamilies = "ref:exam-families";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<BillingPlan>> GetBillingPlansAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(KeyBillingPlans, out IReadOnlyList<BillingPlan>? hit) && hit is not null)
            return hit;

        var rows = (IReadOnlyList<BillingPlan>)await db.BillingPlans.AsNoTracking().ToListAsync(ct);
        cache.Set(KeyBillingPlans, rows, Ttl);
        return rows;
    }

    public async Task<IReadOnlyList<BillingAddOn>> GetBillingAddOnsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(KeyBillingAddOns, out IReadOnlyList<BillingAddOn>? hit) && hit is not null)
            return hit;

        var rows = (IReadOnlyList<BillingAddOn>)await db.BillingAddOns.AsNoTracking().ToListAsync(ct);
        cache.Set(KeyBillingAddOns, rows, Ttl);
        return rows;
    }

    public async Task<IReadOnlyList<ProfessionReference>> GetProfessionsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(KeyProfessions, out IReadOnlyList<ProfessionReference>? hit) && hit is not null)
            return hit;

        var rows = (IReadOnlyList<ProfessionReference>)await db.Professions.AsNoTracking().ToListAsync(ct);
        cache.Set(KeyProfessions, rows, Ttl);
        return rows;
    }

    public async Task<IReadOnlyList<ExamFamily>> GetExamFamiliesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(KeyExamFamilies, out IReadOnlyList<ExamFamily>? hit) && hit is not null)
            return hit;

        var rows = (IReadOnlyList<ExamFamily>)await db.ExamFamilies.AsNoTracking().ToListAsync(ct);
        cache.Set(KeyExamFamilies, rows, Ttl);
        return rows;
    }

    public void InvalidateBillingPlans() => cache.Remove(KeyBillingPlans);
    public void InvalidateBillingAddOns() => cache.Remove(KeyBillingAddOns);
    public void InvalidateProfessions() => cache.Remove(KeyProfessions);
    public void InvalidateExamFamilies() => cache.Remove(KeyExamFamilies);
}
