using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Phase 6c — Resolves effective pronunciation credentials with
/// registry-first / options-fallback semantics.
/// <para>
/// Why singleton: every pronunciation attempt does an Azure / Whisper
/// call. Hitting the DB inside <see cref="IPronunciationAsrProvider.IsConfigured"/>
/// on the hot path would either be sync-over-async (forbidden) or force
/// the entire interface async. Instead we cache the resolved snapshot for
/// 30 seconds — same TTL as <see cref="OetLearner.Api.Services.Conversation.ConversationOptionsProvider"/>
/// — so the synchronous <see cref="IsRegistryConfigured"/> check below is
/// a hot dictionary lookup.
/// </para>
/// <para>
/// Registry rows used:
/// <list type="bullet">
///   <item><c>azure-phoneme</c> — Azure Pronunciation Assessment.</item>
///   <item><c>whisper-asr</c> — Whisper ASR (key + base URL + model).</item>
///   <item><c>gemini-pronunciation-audio</c> — Gemini native-audio scoring.</item>
/// </list>
/// When a row is active and has a non-empty <c>EncryptedApiKey</c>, the
/// decrypted key wins; the row's <c>BaseUrl</c> /<c>DefaultModel</c>
/// override <c>WhisperBaseUrl</c> / <c>WhisperModel</c> respectively. For
/// Azure, the region is parsed out of the row's BaseUrl host.
/// </para>
/// </summary>
public interface IPronunciationCredentialResolver
{
    /// <summary>Returns merged credentials for a single provider code, or
    /// <c>null</c> if no registry row exists. Preferred read path. Will
    /// hit the DB at most once per <c>CacheTtl</c>.</summary>
    Task<PronunciationCredentials?> ResolveAsync(string providerCode, CancellationToken ct);

    /// <summary>Sync hot-path used by provider <c>IsConfigured</c>. Reports
    /// whether the cached registry snapshot has a usable row for the
    /// given provider code. Returns false when the cache is cold; the
    /// first request after cold-start will populate it. Callers MUST OR
    /// this with their options-derived configured check so a missing
    /// cache entry never blocks a deployment whose credentials live in
    /// <see cref="PronunciationOptions"/>.</summary>
    bool IsRegistryConfigured(string providerCode);

    /// <summary>Drop the cached snapshot — admin endpoints call this after
    /// a row mutation so the next attempt picks up the new credentials.</summary>
    void Invalidate();
}

public sealed record PronunciationCredentials(
    string ApiKey,
    string? BaseUrl,
    string? DefaultModel,
    string? AzureRegion);

public sealed class PronunciationCredentialResolver(
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache) : IPronunciationCredentialResolver
{
    private const string CacheKey = "pronunciation:registry-snapshot:v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly string[] KnownCodes = ["azure-phoneme", "whisper-asr", "gemini-pronunciation-audio"];

    public async Task<PronunciationCredentials?> ResolveAsync(string providerCode, CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(ct);
        return snapshot.TryGetValue(providerCode, out var creds) ? creds : null;
    }

    public bool IsRegistryConfigured(string providerCode)
    {
        if (cache.TryGetValue<Dictionary<string, PronunciationCredentials>>(CacheKey, out var snap) && snap is not null)
            return snap.ContainsKey(providerCode);

        // 2026-09-09 bug fix — this method never warmed its own cache, only
        // read it. That's fine for a provider whose real IsConfiguredAsync/
        // ResolveAsync path runs regardless (Pronunciation), but Speaking's
        // Whisper-vs-Mock DI factory (Program.cs) selects the real provider
        // ONLY when this sync check is already true — and the one call that
        // would warm the cache (ResolveAsync, inside the real provider) never
        // fires while Mock keeps getting selected instead. Net effect: once
        // cold (e.g. after any container restart), a correctly-configured
        // registry row could never be observed here again, silently
        // defaulting every Speaking submission to the honest-failure path
        // forever. Kick off a background warm so the TTL window promised by
        // this method's own doc comment ("the first request after cold-start
        // will populate it") actually happens.
        _ = Task.Run(() => GetSnapshotAsync(CancellationToken.None));
        return false;
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<Dictionary<string, PronunciationCredentials>> GetSnapshotAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<Dictionary<string, PronunciationCredentials>>(CacheKey, out var cached) && cached is not null)
            return cached;

        var snapshot = new Dictionary<string, PronunciationCredentials>(StringComparer.OrdinalIgnoreCase);
        // Singleton consumes scoped IAiProviderRegistry — open a scope per
        // refresh so EF Core / DbContext stays scoped (same pattern used
        // by ConversationOptionsProvider).
        await using var scope = scopeFactory.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAiProviderRegistry>();

        foreach (var code in KnownCodes)
        {
            var row = await registry.FindByCodeAsync(code, ct);
            if (row is null || !row.IsActive || string.IsNullOrEmpty(row.EncryptedApiKey)) continue;
            var key = await registry.GetPlatformKeyAsync(row.Code, ct);
            if (string.IsNullOrEmpty(key)) continue;

            string? region = null;
            if (string.Equals(code, "azure-phoneme", StringComparison.OrdinalIgnoreCase))
                region = ExtractAzureRegion(row.BaseUrl);

            snapshot[code] = new PronunciationCredentials(
                ApiKey: key,
                BaseUrl: string.IsNullOrWhiteSpace(row.BaseUrl) ? null : row.BaseUrl,
                DefaultModel: string.IsNullOrWhiteSpace(row.DefaultModel) ? null : row.DefaultModel,
                AzureRegion: region);
        }

        cache.Set(CacheKey, snapshot, CacheTtl);
        return snapshot;
    }

    private static string? ExtractAzureRegion(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host;
        var dot = host.IndexOf('.');
        if (dot <= 0) return null;
        return host[..dot];
    }
}
