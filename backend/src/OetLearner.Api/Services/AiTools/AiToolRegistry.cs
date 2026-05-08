using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiTools;

/// <summary>
/// Resolves the set of tools a feature is allowed to call. Deny-by-default:
/// the absence of an active <see cref="AiFeatureToolGrant"/> row + active
/// <see cref="AiTool"/> row means the feature gets an empty list, and the
/// model is never informed the tool exists.
///
/// The catalog is also seeded by the registry on first read (idempotent —
/// inserts only missing rows from the static <c>BuiltInToolDefinitions</c>
/// produced by the registered <c>IAiToolExecutor</c> instances).
/// </summary>
public interface IAiToolRegistry
{
    Task<IReadOnlyList<AiToolDefinition>> ResolveForFeatureAsync(string featureCode, CancellationToken ct);

    bool IsKnownToolCode(string toolCode);

    /// <summary>Drops the cached grant lookup for a feature. Called by the
    /// admin endpoints after a grant mutation.</summary>
    void InvalidateFeature(string featureCode);

    /// <summary>Idempotent seed: inserts a row for every registered
    /// <see cref="IAiToolExecutor"/> if not already present, and updates
    /// the description/schema/category of existing rows.</summary>
    Task SeedCatalogAsync(CancellationToken ct);
}

public sealed class AiToolRegistry : IAiToolRegistry
{
    private readonly IServiceProvider _sp;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<AiToolOptions> _options;
    private readonly ILogger<AiToolRegistry> _logger;
    private readonly IReadOnlyDictionary<string, IAiToolExecutor> _executors;

    public AiToolRegistry(
        IServiceProvider sp,
        IMemoryCache cache,
        IOptionsMonitor<AiToolOptions> options,
        ILogger<AiToolRegistry> logger,
        IEnumerable<IAiToolExecutor> executors)
    {
        _sp = sp;
        _cache = cache;
        _options = options;
        _logger = logger;
        _executors = executors.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsKnownToolCode(string toolCode) =>
        _executors.ContainsKey(toolCode);

    public void InvalidateFeature(string featureCode) =>
        _cache.Remove(GrantCacheKey(featureCode));

    public async Task<IReadOnlyList<AiToolDefinition>> ResolveForFeatureAsync(string featureCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(featureCode)) return Array.Empty<AiToolDefinition>();

        var key = GrantCacheKey(featureCode);
        if (_cache.TryGetValue<IReadOnlyList<AiToolDefinition>>(key, out var cached) && cached is not null)
            return cached;

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var rows = await (from g in db.AiFeatureToolGrants
                          join t in db.AiTools on g.ToolCode equals t.Code
                          where g.FeatureCode == featureCode
                                && g.IsActive
                                && t.IsActive
                          select new { t.Code, t.Name, t.Description, t.Category, t.JsonSchemaArgs })
                  .AsNoTracking()
                  .ToListAsync(ct);

        var defs = rows
            .Where(r => _executors.ContainsKey(r.Code)) // tool must still be in the assembly
            .Select(r => new AiToolDefinition(r.Code, r.Name, r.Description, r.Category, r.JsonSchemaArgs))
            .ToList()
            .AsReadOnly();

        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.FeatureGrantCacheSeconds));
        _cache.Set(key, (IReadOnlyList<AiToolDefinition>)defs, ttl);
        return defs;
    }

    public async Task SeedCatalogAsync(CancellationToken ct)
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var existing = await db.AiTools.AsNoTracking().ToDictionaryAsync(t => t.Code, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var exec in _executors.Values)
        {
            if (!existing.TryGetValue(exec.Code, out var prior))
            {
                db.AiTools.Add(new AiTool
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Code = exec.Code,
                    Name = HumanizeCode(exec.Code),
                    Description = "",
                    Category = exec.Category,
                    JsonSchemaArgs = exec.JsonSchemaArgs,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                // Keep schema + category in sync with code-defined source of truth.
                var dirty = false;
                var tracked = await db.AiTools.FirstAsync(t => t.Code == exec.Code, ct);
                if (tracked.JsonSchemaArgs != exec.JsonSchemaArgs)
                {
                    tracked.JsonSchemaArgs = exec.JsonSchemaArgs;
                    dirty = true;
                }
                if (tracked.Category != exec.Category)
                {
                    tracked.Category = exec.Category;
                    dirty = true;
                }
                if (dirty)
                {
                    tracked.UpdatedAt = now;
                }
            }
        }
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("AiToolRegistry seeded {Count} tool catalog rows.", _executors.Count);
        }
    }

    private static string GrantCacheKey(string featureCode) =>
        $"AiTool.GrantsForFeature::{featureCode}";

    private static string HumanizeCode(string code) =>
        string.Join(' ', code.Split('_', '.', '-')
            .Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]));
}
