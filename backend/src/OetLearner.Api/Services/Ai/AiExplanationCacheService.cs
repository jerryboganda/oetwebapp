using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W3 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// reusable, cross-learner cache for AI-generated post-submit advisory
/// explanations. See the class doc comment on
/// <see cref="AiExplanationCacheEntry"/> for why this is safe: the cached
/// value is deterministic, author-evidence-grounded, learner-facing content
/// meant to be reused, not per-user state.
///
/// <para>
/// Deliberately fail-soft in both directions: a cache-store failure never
/// fails the caller's already-generated explanation, and a cache-read failure
/// degrades to "no cache hit" (fall through to a fresh provider call) rather
/// than blocking the request. A missed cache opportunity costs money; a
/// broken explanation feature costs trust — this class always chooses the
/// former failure mode.
/// </para>
/// </summary>
public interface IAiExplanationCacheService
{
    /// <summary>Deterministic cache key for one explanation. Every input that
    /// could change the generated text must be included — see the class doc
    /// comment on <see cref="AiExplanationCacheEntry"/>.</summary>
    string BuildCacheKey(
        string module,
        string questionId,
        int? questionVersion,
        string normalizedSelectedAnswer,
        string language,
        string approvedRationale,
        string sourceSentence,
        string? extraEvidence);

    /// <summary>Returns the cached explanation JSON, or null on a genuine miss
    /// or a control-plane failure (both mean "call the provider").</summary>
    Task<string?> TryGetAsync(string cacheKey, CancellationToken ct);

    /// <summary>Stores a freshly-generated explanation for future reuse.
    /// Fire-and-forget safe: failures are logged, never thrown.</summary>
    Task StoreAsync(string module, string questionId, string language, string cacheKey, string explanationJson, CancellationToken ct);
}

public sealed class AiExplanationCacheService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiExplanationCacheService> logger) : IAiExplanationCacheService
{
    public string BuildCacheKey(
        string module,
        string questionId,
        int? questionVersion,
        string normalizedSelectedAnswer,
        string language,
        string approvedRationale,
        string sourceSentence,
        string? extraEvidence)
    {
        var canonical = new StringBuilder()
            .Append(Norm(module)).Append('|')
            .Append(Norm(questionId)).Append('|')
            .Append(questionVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append('|')
            .Append(Norm(normalizedSelectedAnswer)).Append('|')
            .Append(Norm(language)).Append('|')
            .Append(Digest(approvedRationale)).Append('|')
            .Append(Digest(sourceSentence)).Append('|')
            .Append(Digest(extraEvidence))
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

            var hit = await db.AiExplanationCacheEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.CacheKey == cacheKey, ct);
            if (hit is null) return null;

            // Best-effort hit-tracking — never blocks the cached response, and
            // a failure here must not turn a cache hit into a miss.
            try
            {
                await db.AiExplanationCacheEntries
                    .Where(e => e.Id == hit.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(e => e.ServedCount, e => e.ServedCount + 1)
                        .SetProperty(e => e.LastServedAt, DateTimeOffset.UtcNow), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AiExplanationCacheService: hit-count update failed for key {CacheKey}; returning the cached value anyway.", cacheKey);
            }

            return hit.ExplanationJson;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AiExplanationCacheService.TryGetAsync failed for key {CacheKey}; falling through to a fresh call.", cacheKey);
            return null;
        }
    }

    public async Task StoreAsync(string module, string questionId, string language, string cacheKey, string explanationJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(explanationJson)) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            var now = DateTimeOffset.UtcNow;
            db.AiExplanationCacheEntries.Add(new AiExplanationCacheEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Module = module,
                QuestionId = questionId,
                Language = language,
                CacheKey = cacheKey,
                ExplanationJson = explanationJson,
                CreatedAt = now,
                LastServedAt = now,
                ServedCount = 0,
            });

            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the store race to a concurrent identical generation — fine,
            // the other caller's value is now cached and will serve future
            // requests; this caller's own (already-generated, already
            // returned) result is unaffected.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AiExplanationCacheService.StoreAsync failed for key {CacheKey}; the explanation was still returned to the caller, just not cached for reuse.", cacheKey);
        }
    }

    private static string Norm(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string Digest(string? value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
