using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.AiPipeline;

/// <summary>A refused pipeline write. Status/code map straight to the HTTP answer.</summary>
public sealed class AiPipelineException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed record AiPipelineHopInput(
    string Provider,
    string? Model,
    bool Enabled,
    int Attempts,
    int BudgetSeconds,
    string? BenchmarkRunId);

public sealed record AiPipelineSaveRequest(
    string StageKey,
    bool StageEnabled,
    IReadOnlyList<AiPipelineHopInput> Hops,
    int ExpectedVersion,
    string? Reason,
    string? Confirmation);

public sealed record AiPipelineActor(string Id, string Name);

public sealed record AiPipelineRevisionInfo(
    int Version,
    string Kind,
    bool StageEnabled,
    IReadOnlyList<AiPipelineHop> Hops,
    string? Reason,
    string? ChangedBy,
    DateTimeOffset At);

/// <summary>
/// Reads and writes the saved provider order of each AI pipeline stage (owner directive 2026-10-09).
///
/// Persistence rules: the saved row is authoritative; a read is ONE uncached indexed lookup per run, so every
/// API slot and the worker see a change on their next run; if the database cannot be read the last version this
/// process read successfully is used; the built-in default only creates a missing row (insert-only) or serves the
/// very first read of a process that has never seen the row. No environment variable, boot task or health probe
/// writes to these rows; the only writer is <see cref="SaveAsync"/> / <see cref="RollbackAsync"/>, reached from the
/// audited admin endpoint.
/// </summary>
public interface IAiPipelineStore
{
    /// <summary>Creates any missing stage row from the built-in default (insert-only). <paramref name="liveVoiceOrder"/> is the
    /// environment order in force at first setup, so creating the live voice row never flips today’s order.</summary>
    Task EnsureSeededAsync(IReadOnlyList<string>? liveVoiceOrder, CancellationToken ct);
    Task<AiPipelineStageConfig> ReadAsync(string stageKey, CancellationToken ct);
    Task<AiPipelinePlan> ResolvePlanAsync(string stageKey, CancellationToken ct);
    Task<AiPipelineStageConfig> SaveAsync(AiPipelineSaveRequest request, AiPipelineActor actor, CancellationToken ct);
    Task<AiPipelineStageConfig> RollbackAsync(string stageKey, int toVersion, int expectedVersion, string? reason, AiPipelineActor actor, CancellationToken ct);
    /// <summary>Owner action "Restore built-in order" (for example Max first). Saved as a new version; the previous order stays in history.</summary>
    Task<AiPipelineStageConfig> RestoreDefaultAsync(string stageKey, int expectedVersion, string? reason, AiPipelineActor actor, CancellationToken ct);
    Task<IReadOnlyList<AiPipelineRevisionInfo>> HistoryAsync(string stageKey, int take, CancellationToken ct);
}

public sealed class AiPipelineStore(
    LearnerDbContext db,
    IAiProviderRegistry registry,
    IAiProviderRouteApprovalService routeApproval,
    ILogger<AiPipelineStore> logger) : IAiPipelineStore
{
    public const string DisableMaxConfirmation = "DISABLE CLAUDE MAX";

    // Last version this process read successfully, per stage. Used only when the database cannot be read.
    private static readonly ConcurrentDictionary<string, AiPipelineStageConfig> LastKnownGood = new();

    public async Task EnsureSeededAsync(IReadOnlyList<string>? liveVoiceOrder, CancellationToken ct)
    {
        var existing = await db.AiPipelineStages.AsNoTracking().Select(s => s.StageKey).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var added = false;
        foreach (var key in AiPipelineStageKeys.All.Where(k => !existing.Contains(k)))
        {
            var hops = key == AiPipelineStageKeys.LiveVoice && liveVoiceOrder is { Count: > 0 }
                ? liveVoiceOrder.Select(p => new AiPipelineHop(p, null, true, 1, 0)).ToList()
                : AiPipelineDefaults.For(key);
            db.AiPipelineStages.Add(new AiPipelineStage
            {
                Id = Guid.NewGuid().ToString("N"),
                StageKey = key,
                ChainJson = AiPipelineJson.Serialize(hops),
                StageEnabled = true,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AiPipelineStageRevisions.Add(new AiPipelineStageRevision
            {
                Id = Guid.NewGuid().ToString("N"),
                StageKey = key,
                Version = 1,
                ChainJson = AiPipelineJson.Serialize(hops),
                StageEnabled = true,
                Kind = "create",
                Reason = "Initial setup from the built-in default order.",
                ChangedByName = "system",
                CreatedAt = now,
            });
            added = true;
        }

        if (!added) return;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Another process seeded first (unique StageKey): nothing to do, and never an overwrite.
            logger.LogInformation("AI pipeline seeding raced with another process ({Type}).", ex.GetType().Name);
            db.ChangeTracker.Clear();
        }
    }

    public async Task<AiPipelineStageConfig> ReadAsync(string stageKey, CancellationToken ct)
    {
        try
        {
            var row = await db.AiPipelineStages.AsNoTracking().FirstOrDefaultAsync(s => s.StageKey == stageKey, ct);
            if (row is null)
            {
                return LastKnownGood.TryGetValue(stageKey, out var known)
                    ? known with { Source = AiPipelineSource.LastKnownGood }
                    : new AiPipelineStageConfig(stageKey, true, 0, AiPipelineDefaults.For(stageKey), AiPipelineSource.InitialDefault);
            }

            var config = new AiPipelineStageConfig(stageKey, row.StageEnabled, row.Version, AiPipelineJson.Parse(row.ChainJson), AiPipelineSource.Saved);
            LastKnownGood[stageKey] = config;
            return config;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("AI pipeline stage {Stage} could not be read ({Type}); using the last known good version.", stageKey, ex.GetType().Name);
            return LastKnownGood.TryGetValue(stageKey, out var known)
                ? known with { Source = AiPipelineSource.LastKnownGood }
                : new AiPipelineStageConfig(stageKey, true, 0, AiPipelineDefaults.For(stageKey), AiPipelineSource.InitialDefault);
        }
    }

    public async Task<AiPipelinePlan> ResolvePlanAsync(string stageKey, CancellationToken ct)
    {
        var config = await ReadAsync(stageKey, ct);
        var resolved = new List<AiPipelineResolvedHop>();
        var skipped = new List<string>();
        for (var i = 0; i < config.Hops.Count; i++)
        {
            var hop = config.Hops[i];
            if (!hop.Enabled)
            {
                skipped.Add($"{hop.Provider}: disabled");
                continue;
            }

            if (!AiPipelineStageKeys.UsesProviderRegistry(stageKey))
            {
                resolved.Add(new AiPipelineResolvedHop(i, hop.Provider, hop.Model ?? string.Empty, hop.Attempts, hop.BudgetSeconds));
                continue;
            }

            AiProvider? row;
            string? key;
            try
            {
                row = await registry.FindByCodeAsync(hop.Provider, ct);
                key = row is null ? null : await registry.GetPlatformKeyAsync(hop.Provider, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The registry could not be read: keep the hop and let the gateway decide, rather than silently dropping a provider.
                logger.LogWarning("AI pipeline provider check for {Provider} failed ({Type}); keeping the hop.", hop.Provider, ex.GetType().Name);
                resolved.Add(new AiPipelineResolvedHop(i, hop.Provider, hop.Model ?? string.Empty, hop.Attempts, hop.BudgetSeconds));
                continue;
            }

            if (row is null) { skipped.Add($"{hop.Provider}: provider record inactive or missing"); continue; }
            if (string.IsNullOrWhiteSpace(key)) { skipped.Add($"{hop.Provider}: no credential"); continue; }

            var model = string.IsNullOrWhiteSpace(hop.Model) ? row.DefaultModel : hop.Model!;
            resolved.Add(new AiPipelineResolvedHop(i, hop.Provider, model, hop.Attempts, hop.BudgetSeconds));
        }

        return new AiPipelinePlan(stageKey, config.StageEnabled, config.Version, config.Source, resolved, skipped);
    }

    public async Task<AiPipelineStageConfig> SaveAsync(AiPipelineSaveRequest request, AiPipelineActor actor, CancellationToken ct)
    {
        var key = request.StageKey;
        if (!AiPipelineStageKeys.IsKnown(key))
            throw new AiPipelineException(404, "ai_pipeline_stage_unknown", "Unknown pipeline stage.");

        var row = await db.AiPipelineStages.FirstOrDefaultAsync(s => s.StageKey == key, ct)
                  ?? throw new AiPipelineException(409, "ai_pipeline_not_initialised", "The stage has not been set up yet. Reload and try again.");
        if (request.ExpectedVersion != row.Version)
            throw new AiPipelineException(409, "ai_pipeline_version_conflict", "Someone else changed this stage. Reload to see the latest order.");

        var before = AiPipelineJson.Parse(row.ChainJson);
        var after = Normalise(request.Hops);
        await ValidateAsync(key, request.StageEnabled, after, ct);
        EnforceMaxConfirmation(key, before, row.StageEnabled, after, request.StageEnabled, request.Confirmation);
        await EnforceBenchmarkGateAsync(key, before, row.StageEnabled, request, after, ct);
        return await WriteAsync(row, after, request.StageEnabled, "update", request.Reason, actor, ct);
    }

    public async Task<AiPipelineStageConfig> RollbackAsync(
        string stageKey, int toVersion, int expectedVersion, string? reason, AiPipelineActor actor, CancellationToken ct)
    {
        if (!AiPipelineStageKeys.IsKnown(stageKey))
            throw new AiPipelineException(404, "ai_pipeline_stage_unknown", "Unknown pipeline stage.");
        var row = await db.AiPipelineStages.FirstOrDefaultAsync(s => s.StageKey == stageKey, ct)
                  ?? throw new AiPipelineException(409, "ai_pipeline_not_initialised", "The stage has not been set up yet.");
        if (expectedVersion != row.Version)
            throw new AiPipelineException(409, "ai_pipeline_version_conflict", "Someone else changed this stage. Reload to see the latest order.");
        var target = await db.AiPipelineStageRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.StageKey == stageKey && r.Version == toVersion, ct)
            ?? throw new AiPipelineException(404, "ai_pipeline_revision_unknown", "That version does not exist.");

        var hops = AiPipelineJson.Parse(target.ChainJson);
        await ValidateAsync(stageKey, target.StageEnabled, hops, ct);
        return await WriteAsync(row, hops, target.StageEnabled, "rollback",
            string.IsNullOrWhiteSpace(reason) ? $"Restored version {toVersion}." : reason, actor, ct);
    }

    public async Task<AiPipelineStageConfig> RestoreDefaultAsync(
        string stageKey, int expectedVersion, string? reason, AiPipelineActor actor, CancellationToken ct)
    {
        if (!AiPipelineStageKeys.IsKnown(stageKey))
            throw new AiPipelineException(404, "ai_pipeline_stage_unknown", "Unknown pipeline stage.");
        var row = await db.AiPipelineStages.FirstOrDefaultAsync(s => s.StageKey == stageKey, ct)
                  ?? throw new AiPipelineException(409, "ai_pipeline_not_initialised", "The stage has not been set up yet.");
        if (expectedVersion != row.Version)
            throw new AiPipelineException(409, "ai_pipeline_version_conflict", "Someone else changed this stage. Reload to see the latest order.");
        var hops = AiPipelineDefaults.For(stageKey);
        await ValidateAsync(stageKey, true, hops, ct);
        return await WriteAsync(row, hops, true, "restore",
            string.IsNullOrWhiteSpace(reason) ? "Restored the built-in order." : reason, actor, ct);
    }

    public async Task<IReadOnlyList<AiPipelineRevisionInfo>> HistoryAsync(string stageKey, int take, CancellationToken ct)
    {
        var rows = await db.AiPipelineStageRevisions.AsNoTracking()
            .Where(r => r.StageKey == stageKey)
            .OrderByDescending(r => r.Version)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);
        return rows.Select(r => new AiPipelineRevisionInfo(
            r.Version, r.Kind, r.StageEnabled, AiPipelineJson.Parse(r.ChainJson), r.Reason, r.ChangedByName, r.CreatedAt)).ToList();
    }

    private static IReadOnlyList<AiPipelineHop> Normalise(IReadOnlyList<AiPipelineHopInput> input)
        => input.Select(h => new AiPipelineHop(
            (h.Provider ?? string.Empty).Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(h.Model) ? null : h.Model.Trim(),
            h.Enabled,
            h.Attempts,
            h.BudgetSeconds)).ToList();

    private async Task ValidateAsync(string key, bool stageEnabled, IReadOnlyList<AiPipelineHop> hops, CancellationToken ct)
    {
        if (hops.Count is < 1 or > 8)
            throw new AiPipelineException(400, "ai_pipeline_hops_invalid", "A stage needs between 1 and 8 steps.");
        if (hops.Any(h => string.IsNullOrWhiteSpace(h.Provider)))
            throw new AiPipelineException(400, "ai_pipeline_provider_required", "Every step needs a provider.");
        if (hops.Select(h => h.Provider).Distinct().Count() != hops.Count)
            throw new AiPipelineException(400, "ai_pipeline_duplicate_provider", "A provider can appear only once in a stage.");

        var live = key == AiPipelineStageKeys.LiveVoice;
        foreach (var h in hops)
        {
            if (live)
            {
                if (h.Provider is not ("openai" or "gemini"))
                    throw new AiPipelineException(400, "ai_pipeline_live_provider_invalid", "Live voice supports openai and gemini only.");
                continue;
            }

            if (h.Attempts is < 1 or > 4)
                throw new AiPipelineException(400, "ai_pipeline_attempts_invalid", "Attempts must be between 1 and 4.");
            if (h.BudgetSeconds is < 10 or > 1500)
                throw new AiPipelineException(400, "ai_pipeline_budget_invalid", "The time limit per attempt must be between 10 and 1500 seconds.");
        }

        if (!live)
        {
            var codes = hops.Select(h => h.Provider).ToList();
            var rows = await db.AiProviders.AsNoTracking().Where(p => codes.Contains(p.Code)).ToListAsync(ct);
            foreach (var h in hops)
            {
                var p = rows.FirstOrDefault(r => r.Code == h.Provider);
                if (p is null)
                    throw new AiPipelineException(400, "ai_pipeline_provider_unknown", $"Provider '{h.Provider}' is not set up. Add it under Providers and keys first.");
                if (p.Category != AiProviderCategory.TextChat)
                    throw new AiPipelineException(400, "ai_pipeline_provider_category", $"Provider '{h.Provider}' is not a text model provider.");
            }
        }

        var anyEnabled = hops.Any(h => h.Enabled);
        if (AiPipelineStageKeys.IsGrading(key) && !stageEnabled)
            throw new AiPipelineException(400, "ai_pipeline_grading_required", "A grading stage cannot be switched off.");
        if ((AiPipelineStageKeys.IsGrading(key) || live || stageEnabled) && !anyEnabled)
            throw new AiPipelineException(400, "ai_pipeline_one_enabled", live
                ? "At least one live voice provider must stay enabled."
                : "At least one working alternative must stay enabled.");
    }

    // Choice B (owner, 2026-10-09): Claude Max may be disabled in a grading stage, with a typed confirmation.
    private static void EnforceMaxConfirmation(
        string key, IReadOnlyList<AiPipelineHop> before, bool beforeEnabled,
        IReadOnlyList<AiPipelineHop> after, bool afterEnabled, string? confirmation)
    {
        if (!AiPipelineStageKeys.IsGrading(key)) return;
        var wasOn = before.Any(h => h.Provider == AiPipelineDefaults.MaxProvider && h.Enabled);
        var isOn = after.Any(h => h.Provider == AiPipelineDefaults.MaxProvider && h.Enabled);
        if (wasOn && !isOn && !string.Equals(confirmation?.Trim(), DisableMaxConfirmation, StringComparison.Ordinal))
            throw new AiPipelineException(400, "ai_pipeline_disable_max_confirmation",
                $"Type \"{DisableMaxConfirmation}\" to confirm switching Claude Max off in this stage.");
    }

    // Owner decision D2: a model that leaves Claude must pass the benchmark before it can be enabled in a scoring stage.
    private async Task EnforceBenchmarkGateAsync(
        string key, IReadOnlyList<AiPipelineHop> before, bool beforeEnabled,
        AiPipelineSaveRequest request, IReadOnlyList<AiPipelineHop> after, CancellationToken ct)
    {
        if (key == AiPipelineStageKeys.LiveVoice) return;
        for (var i = 0; i < after.Count; i++)
        {
            var h = after[i];
            if (!h.Enabled) continue;
            var already = before.Any(b => b.Enabled && b.Provider == h.Provider && string.Equals(b.Model, h.Model, StringComparison.OrdinalIgnoreCase));
            if (already) continue;
            try
            {
                await routeApproval.EnsureSwitchAllowedAsync(
                    new AiRouteSwitchRequest(key, h.Provider, h.Model, request.Hops[i].BenchmarkRunId, null, null, null), ct);
            }
            catch (AiProviderRouteRefusedException ex)
            {
                throw new AiPipelineException(409, "ai_route_switch_refused", ex.Message);
            }
        }
    }

    private async Task<AiPipelineStageConfig> WriteAsync(
        AiPipelineStage row, IReadOnlyList<AiPipelineHop> after, bool stageEnabled,
        string kind, string? reason, AiPipelineActor actor, CancellationToken ct)
    {
        var before = AiPipelineJson.Parse(row.ChainJson);
        var fromVersion = row.Version;
        var now = DateTimeOffset.UtcNow;
        row.ChainJson = AiPipelineJson.Serialize(after);
        row.StageEnabled = stageEnabled;
        row.Version = fromVersion + 1;
        row.UpdatedAt = now;
        row.UpdatedByAdminId = actor.Id;

        db.AiPipelineStageRevisions.Add(new AiPipelineStageRevision
        {
            Id = Guid.NewGuid().ToString("N"),
            StageKey = row.StageKey,
            Version = row.Version,
            ChainJson = row.ChainJson,
            StageEnabled = stageEnabled,
            Kind = kind,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : (reason.Length > 512 ? reason[..512] : reason),
            ChangedByAdminId = actor.Id,
            ChangedByName = actor.Name,
            CreatedAt = now,
        });

        // Provider codes, models and flags only: never key material or key hints.
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = actor.Id,
            ActorName = actor.Name,
            Action = kind == "rollback" ? "AiPipelineStageRolledBack" : "AiPipelineStageUpdated",
            ResourceType = "AiConfig",
            ResourceId = row.StageKey,
            Details = JsonSerializer.Serialize(new
            {
                stage = row.StageKey,
                fromVersion,
                toVersion = row.Version,
                before = before.Select(h => new { h.Provider, h.Model, h.Enabled }),
                after = after.Select(h => new { h.Provider, h.Model, h.Enabled }),
                reason,
            }, AiPipelineJson.Options),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AiPipelineException(409, "ai_pipeline_version_conflict", "Someone else changed this stage. Reload to see the latest order.");
        }

        var config = new AiPipelineStageConfig(row.StageKey, stageEnabled, row.Version, after, AiPipelineSource.Saved);
        LastKnownGood[row.StageKey] = config;
        return config;
    }
}
