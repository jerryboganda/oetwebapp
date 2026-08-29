using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

public interface IAiBudgetOverrideService
{
    /// <summary>Sum of unrevoked, unexpired override amounts for
    /// <paramref name="scope"/>. Zero when none apply.</summary>
    Task<decimal> TryGetActiveAsync(string scope, DateTimeOffset now, CancellationToken ct);

    Task<AiBudgetOverride> CreateAsync(
        string scope,
        decimal amountUsd,
        string reason,
        string actorAdminId,
        DateTimeOffset expiresAt,
        CancellationToken ct);

    Task<IReadOnlyList<AiBudgetOverride>> ListActiveAsync(CancellationToken ct);
}

public sealed class AiBudgetOverrideService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiBudgetOverrideService> logger) : IAiBudgetOverrideService
{
    public async Task<decimal> TryGetActiveAsync(string scope, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scope)) return 0m;

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var total = await db.AiBudgetOverrides.AsNoTracking()
                .Where(o => o.Scope == scope && o.RevokedAt == null && o.ExpiresAt > now)
                .SumAsync(o => o.AmountUsd, ct);
            return total;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AiBudgetOverrideService.TryGetActiveAsync failed for scope {Scope}; treating as zero extra headroom.", scope);
            return 0m;
        }
    }

    public async Task<AiBudgetOverride> CreateAsync(
        string scope,
        decimal amountUsd,
        string reason,
        string actorAdminId,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("scope is required.", nameof(scope));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("reason is required.", nameof(reason));
        if (amountUsd <= 0m)
            throw new ArgumentException("amountUsd must be positive.", nameof(amountUsd));

        var now = DateTimeOffset.UtcNow;
        if (expiresAt <= now)
            throw new ArgumentException("expiresAt must be in the future.", nameof(expiresAt));
        if (string.IsNullOrWhiteSpace(actorAdminId))
            throw new ArgumentException("actorAdminId is required.", nameof(actorAdminId));

        await using var dbScope = scopeFactory.CreateAsyncScope();
        var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var row = new AiBudgetOverride
        {
            Id = Guid.NewGuid().ToString("N"),
            Scope = scope.Trim(),
            AmountUsd = amountUsd,
            Reason = reason.Trim(),
            ActorAdminId = actorAdminId.Trim(),
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
        db.AiBudgetOverrides.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<IReadOnlyList<AiBudgetOverride>> ListActiveAsync(CancellationToken ct)
    {
        await using var dbScope = scopeFactory.CreateAsyncScope();
        var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        return await db.AiBudgetOverrides.AsNoTracking()
            .Where(o => o.RevokedAt == null && o.ExpiresAt > now)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);
    }
}
