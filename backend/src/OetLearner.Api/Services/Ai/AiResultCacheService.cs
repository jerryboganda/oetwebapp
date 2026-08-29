using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W5 — versioned AI result cache. Fail-soft in both directions: a miss or
/// store failure never fails the caller.
/// </summary>
public interface IAiResultCacheService
{
    string BuildCacheKey(
        string featureCode,
        string module,
        string? attemptId,
        string? questionRevisionId,
        string? storedAnswerHash,
        string? language,
        string? promptVersion,
        string? rulebookVersion);

    Task<string?> TryGetAsync(string cacheKey, CancellationToken ct);

    Task StoreAsync(
        string cacheKey,
        string featureCode,
        string module,
        string payloadJson,
        string? promptVersion,
        string? rulebookVersion,
        string? resourceVersion,
        TimeSpan? ttl,
        CancellationToken ct);
}

public sealed class AiResultCacheService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiResultCacheService> logger) : IAiResultCacheService
{
    public string BuildCacheKey(
        string featureCode,
        string module,
        string? attemptId,
        string? questionRevisionId,
        string? storedAnswerHash,
        string? language,
        string? promptVersion,
        string? rulebookVersion)
    {
        var canonical = new StringBuilder()
            .Append(Norm(featureCode)).Append('|')
            .Append(Norm(module)).Append('|')
            .Append(Norm(attemptId)).Append('|')
            .Append(Norm(questionRevisionId)).Append('|')
            .Append(Norm(storedAnswerHash)).Append('|')
            .Append(Norm(language)).Append('|')
            .Append(Norm(promptVersion)).Append('|')
            .Append(Norm(rulebookVersion))
            .ToString();
        return Digest(canonical);
    }

    public async Task<string?> TryGetAsync(string cacheKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cacheKey)) return null;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            var hit = await db.AiResultCaches.AsNoTracking()
                .FirstOrDefaultAsync(e => e.CacheKey == cacheKey
                    && (e.ExpiresAt == null || e.ExpiresAt > now), ct);
            if (hit is null) return null;

            try
            {
                await db.AiResultCaches.Where(e => e.Id == hit.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(e => e.ServedCount, e => e.ServedCount + 1)
                        .SetProperty(e => e.LastServedAt, now), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AiResultCacheService hit-count update failed for {CacheKey}.", cacheKey);
            }

            return hit.PayloadJson;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AiResultCacheService.TryGetAsync failed for {CacheKey}.", cacheKey);
            return null;
        }
    }

    public async Task StoreAsync(
        string cacheKey,
        string featureCode,
        string module,
        string payloadJson,
        string? promptVersion,
        string? rulebookVersion,
        string? resourceVersion,
        TimeSpan? ttl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(payloadJson)) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.AiResultCaches.Add(new AiResultCacheEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                CacheKey = cacheKey,
                FeatureCode = featureCode,
                Module = module,
                PromptVersion = promptVersion,
                RulebookVersion = rulebookVersion,
                ResourceVersion = resourceVersion,
                PayloadJson = payloadJson,
                CreatedAt = now,
                LastServedAt = now,
                ExpiresAt = ttl is { } t ? now.Add(t) : null,
                ServedCount = 0,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent identical store — the other row will serve future hits.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AiResultCacheService.StoreAsync failed for {CacheKey}.", cacheKey);
        }
    }

    private static string Norm(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string Digest(string? value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
