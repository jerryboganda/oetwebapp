using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiPipeline;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// AI Pipeline Control Center admin surface (owner directive 2026-10-09): the saved provider order, per-step on/off and
/// history of the five pipeline stages, plus the on-demand self-check. Same permission as the other AI admin pages
/// (<c>ai_config</c>). The ONLY writers of the saved order are <see cref="AiPipelineStore.SaveAsync"/>,
/// <see cref="AiPipelineStore.RollbackAsync"/> and <see cref="AiPipelineStore.RestoreDefaultAsync"/>, reached from here.
/// </summary>
public static class AiPipelineAdminEndpoints
{
    // Usage rows can lag a run that resolved its plan just before a change; a call this long after a change is a violation.
    private static readonly TimeSpan InFlightGrace = TimeSpan.FromMinutes(25);

    public static IEndpointRouteBuilder MapAiPipelineAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/ai/pipelines")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        group.MapGet("", async (IAiPipelineStore store, LearnerDbContext db, CancellationToken ct) =>
        {
            await store.EnsureSeededAsync(null, ct);
            var providerRows = await db.AiProviders.AsNoTracking()
                .Where(p => p.Category == AiProviderCategory.TextChat)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Code, p.Name, p.Dialect, p.DefaultModel, p.IsActive, p.EncryptedApiKey, p.LastTestStatus, p.LastTestedAt })
                .ToListAsync(ct);
            var providers = providerRows.Select(p => new ProviderOption(
                p.Code, p.Name, p.Dialect.ToString(), p.DefaultModel, p.IsActive,
                !string.IsNullOrEmpty(p.EncryptedApiKey), p.LastTestStatus, p.LastTestedAt,
                p.EncryptedApiKey == OetLearner.Api.Services.Seeding.WritingSubscriptionProviderDefaults.MarkerKey)).ToList();

            var stages = new List<object>();
            foreach (var key in AiPipelineStageKeys.All)
            {
                var plan = await store.ResolvePlanAsync(key, ct);
                var config = await store.ReadAsync(key, ct);
                var latest = (await store.HistoryAsync(key, 1, ct)).FirstOrDefault();
                var usableIndexes = plan.Hops.Select(h => h.Index).ToHashSet();
                var hops = config.Hops.Select((h, i) => new
                {
                    h.Provider,
                    h.Model,
                    h.Enabled,
                    h.Attempts,
                    h.BudgetSeconds,
                    status = !h.Enabled ? "disabled"
                        : usableIndexes.Contains(i) ? "ready"
                        : (plan.Skipped.FirstOrDefault(s => s.StartsWith(h.Provider + ":", StringComparison.Ordinal)) ?? "unavailable"),
                }).ToList();

                object? lastServed = null;
                if (key != AiPipelineStageKeys.LiveVoice)
                {
                    lastServed = await db.AiUsageRecords.AsNoTracking()
                        .Where(u => u.FeatureCode == key && u.Outcome == AiCallOutcome.Success)
                        .OrderByDescending(u => u.CreatedAt)
                        .Select(u => new { u.ProviderId, u.Model, u.CreatedAt })
                        .FirstOrDefaultAsync(ct);
                }

                var firstEnabled = config.Hops.FirstOrDefault(h => h.Enabled);
                stages.Add(new
                {
                    stageKey = key,
                    label = AiPipelineStageKeys.Label(key),
                    kind = AiPipelineStageKeys.IsGrading(key) ? "grading" : AiPipelineStageKeys.IsReviewer(key) ? "reviewer" : "voice",
                    stageEnabled = config.StageEnabled,
                    version = config.Version,
                    source = config.Source.ToString(),
                    hops,
                    nextRunStartsOn = plan.Hops.FirstOrDefault()?.Provider,
                    lastServed,
                    maxNotFirst = AiPipelineStageKeys.IsGrading(key) && firstEnabled is not null && firstEnabled.Provider != AiPipelineDefaults.MaxProvider,
                    maxDisabled = AiPipelineStageKeys.IsGrading(key) && !config.Hops.Any(h => h.Provider == AiPipelineDefaults.MaxProvider && h.Enabled),
                    lastChange = latest is null ? null : new { latest.Version, latest.Kind, latest.ChangedBy, latest.Reason, latest.At },
                });
            }

            return Results.Ok(new { stages, providers, disableMaxConfirmation = AiPipelineStore.DisableMaxConfirmation });
        });

        group.MapPut("/{stageKey}", async (
            string stageKey,
            SaveStageDto dto,
            IAiPipelineStore store,
            HttpContext http,
            CancellationToken ct) =>
        {
            try
            {
                var saved = await store.SaveAsync(
                    new AiPipelineSaveRequest(
                        stageKey, dto.StageEnabled, dto.Hops, dto.ExpectedVersion, dto.Reason, dto.Confirmation),
                    Actor(http), ct);
                return Results.Ok(new { saved.StageKey, saved.Version });
            }
            catch (AiPipelineException ex)
            {
                return new ApiErrorResult(ex.Status, ex.Code, ex.Message);
            }
        }).RequireRateLimiting("PerUserWrite");

        group.MapGet("/{stageKey}/history", async (string stageKey, IAiPipelineStore store, CancellationToken ct) =>
            AiPipelineStageKeys.IsKnown(stageKey)
                ? Results.Ok(await store.HistoryAsync(stageKey, 50, ct))
                : Results.NotFound());

        group.MapPost("/{stageKey}/rollback", async (
            string stageKey,
            RollbackDto dto,
            IAiPipelineStore store,
            HttpContext http,
            CancellationToken ct) =>
        {
            try
            {
                var saved = await store.RollbackAsync(stageKey, dto.ToVersion, dto.ExpectedVersion, dto.Reason, Actor(http), ct);
                return Results.Ok(new { saved.StageKey, saved.Version });
            }
            catch (AiPipelineException ex)
            {
                return new ApiErrorResult(ex.Status, ex.Code, ex.Message);
            }
        }).RequireRateLimiting("PerUserWrite");

        group.MapPost("/{stageKey}/restore-default", async (
            string stageKey,
            RestoreDto dto,
            IAiPipelineStore store,
            HttpContext http,
            CancellationToken ct) =>
        {
            try
            {
                var saved = await store.RestoreDefaultAsync(stageKey, dto.ExpectedVersion, dto.Reason, Actor(http), ct);
                return Results.Ok(new { saved.StageKey, saved.Version });
            }
            catch (AiPipelineException ex)
            {
                return new ApiErrorResult(ex.Status, ex.Code, ex.Message);
            }
        }).RequireRateLimiting("PerUserWrite");

        // On-demand production evidence (owner-confirmed carve-out, 2026-10-09): never part of CI. Costs nothing unless
        // live=true, which runs the cheap connection probe once per enabled step (the same probe as the provider Test button).
        group.MapPost("/self-check", async (
            IAiPipelineStore store,
            LearnerDbContext db,
            IAiProviderConnectionTester tester,
            bool? live,
            CancellationToken ct) =>
        {
            var results = new List<object>();
            foreach (var key in AiPipelineStageKeys.All)
            {
                var config = await store.ReadAsync(key, ct);
                var plan = await store.ResolvePlanAsync(key, ct);
                var checks = new List<object>();

                // 1. The plan contains no disabled step and follows the saved order.
                var planProviders = plan.Hops.Select(h => h.Provider).ToList();
                var expected = config.Hops.Where(h => h.Enabled).Select(h => h.Provider).Where(planProviders.Contains).ToList();
                checks.Add(new
                {
                    name = "plan_follows_saved_order",
                    ok = planProviders.SequenceEqual(expected) && !config.Hops.Any(h => !h.Enabled && planProviders.Contains(h.Provider)),
                    detail = $"saved v{config.Version} ({config.Source}); next run: {string.Join(" > ", planProviders)}",
                });

                // 2. Disabled steps were never called after the change (the real production evidence).
                var revisions = await store.HistoryAsync(key, 100, ct);
                var violations = new List<Violation>();
                if (key != AiPipelineStageKeys.LiveVoice)
                {
                    foreach (var hop in config.Hops.Where(h => !h.Enabled))
                    {
                        // The moment the step last became (and stayed) disabled.
                        DateTimeOffset? since = null;
                        foreach (var r in revisions)
                        {
                            var inRev = r.Hops.FirstOrDefault(h => h.Provider == hop.Provider);
                            if (inRev is { Enabled: false }) since = r.At; else break;
                        }

                        if (since is null) continue;
                        var cutoff = since.Value + InFlightGrace;
                        var late = await db.AiUsageRecords.AsNoTracking()
                            .CountAsync(u => u.FeatureCode == key && u.ProviderId == hop.Provider && u.CreatedAt > cutoff, ct);
                        var early = await db.AiUsageRecords.AsNoTracking()
                            .CountAsync(u => u.FeatureCode == key && u.ProviderId == hop.Provider && u.CreatedAt > since.Value && u.CreatedAt <= cutoff, ct);
                        violations.Add(new Violation(hop.Provider, since, late, early));
                    }
                }

                checks.Add(new
                {
                    name = "disabled_steps_never_called",
                    ok = violations.All(v => v.CallsAfterGrace == 0),
                    detail = violations.Count == 0 ? "no disabled step with history" : null,
                    violations,
                });

                // 3. What actually served since the last change (proves API-first / Max-first as selected).
                if (key != AiPipelineStageKeys.LiveVoice && revisions.Count > 0)
                {
                    var changedAt = revisions[0].At;
                    var served = await db.AiUsageRecords.AsNoTracking()
                        .Where(u => u.FeatureCode == key && u.CreatedAt > changedAt)
                        .GroupBy(u => new { u.ProviderId, u.Outcome })
                        .Select(g => new { g.Key.ProviderId, g.Key.Outcome, calls = g.Count() })
                        .ToListAsync(ct);
                    var servedView = served.Select(x => new { x.ProviderId, outcome = x.Outcome.ToString(), x.calls }).ToList();
                    checks.Add(new { name = "served_since_last_change", ok = true, since = changedAt, served = servedView });
                }

                // 4. Optional live connection probe of every enabled step.
                if (live == true && key != AiPipelineStageKeys.LiveVoice)
                {
                    var probes = new List<object>();
                    foreach (var hop in plan.Hops)
                    {
                        var t = await tester.TestProviderAsync(hop.Provider, ct);
                        probes.Add(new { hop.Provider, t.Status, t.LatencyMs, t.ErrorMessage });
                    }

                    checks.Add(new { name = "live_probe", ok = probes.Count > 0, probes });
                }

                results.Add(new { stageKey = key, label = AiPipelineStageKeys.Label(key), version = config.Version, checks });
            }

            return Results.Ok(new { ranAt = DateTimeOffset.UtcNow, live = live == true, results });
        }).RequireRateLimiting("PerUserWrite");

        // Phase 2 (owner directive 2026-10-09): usage/cost + subscription + credit payload behind the
        // dashboard sections of the same page. Internally tracked figures only — see
        // AiPipelineOverviewService. Cached server-side for 60s per window.
        group.MapGet("/overview", async (IAiPipelineOverviewService overview, string? window, CancellationToken ct) =>
            Results.Ok(await overview.BuildAsync(window, ct)));

        // Subscription-account rotation (owner directive 2026-10-10): per-account state + the audited
        // controls (switch threshold, per-account drain). Neither control may write the saved order.
        group.MapGet("/accounts", (ISubscriptionAccountPool pool) =>
        {
            var policy = pool.Policy;
            return Results.Ok(new
            {
                policy = new
                {
                    switchPercent = policy.SwitchPercent,
                    cooldownFloorMinutes = (int)policy.CooldownFloor.TotalMinutes,
                    refreshSeconds = (int)policy.RefreshTtl.TotalSeconds,
                },
                lastRefreshedAt = pool.LastRefreshedAt,
                accounts = pool.Accounts.Select(a => new
                {
                    a.ProviderCode,
                    a.Group,
                    a.Name,
                    standing = a.Standing.ToString().ToLowerInvariant(),
                    a.StandingReason,
                    windows = a.Windows.Select(w => new
                    {
                        w.Label,
                        w.UsedTokens,
                        w.Cap,
                        usedPct = w.UsedPct is { } pct ? Math.Round(pct * 100.0, 1) : (double?)null,
                        w.ResetsAt,
                        w.WindowStartedAt,
                    }),
                    a.LastQuotaErrorAt,
                    a.LastQuotaErrorKind,
                    a.CooldownUntil,
                    a.SampledAt,
                    a.Reachable,
                }),
            });
        });

        group.MapPut("/accounts/threshold", async (
            ThresholdDto dto,
            LearnerDbContext db,
            HttpContext http,
            ISubscriptionAccountPool pool,
            OetLearner.Api.Services.AiPipeline.ISubscriptionAccountStateProvider accountState,
            CancellationToken ct) =>
        {
            var percent = Math.Clamp(dto.SwitchPercent, 50, 100);
            var now = DateTimeOffset.UtcNow;
            var actor = Actor(http);
            // Stored on the switch-percent flag's RolloutPercentage: an absent or disabled row keeps 95.
            var flag = await db.FeatureFlags.FirstOrDefaultAsync(
                f => f.Key == OetLearner.Api.Services.AiPipeline.SubscriptionAccountStateProvider.SwitchPercentFlagKey, ct);
            if (flag is null)
            {
                flag = new FeatureFlag
                {
                    Id = $"FLG-{Guid.NewGuid():N}"[..12],
                    Name = "Subscription account rotation switch threshold",
                    Key = OetLearner.Api.Services.AiPipeline.SubscriptionAccountStateProvider.SwitchPercentFlagKey,
                    FlagType = FeatureFlagType.Operational,
                    Owner = "Owner",
                    CreatedAt = now,
                };
                db.FeatureFlags.Add(flag);
            }

            flag.Enabled = true;
            flag.RolloutPercentage = (int)percent;
            flag.Description = "Percent of a subscription account's rolling 5-hour or weekly cap at which the runtime prefers another account of the same engine group.";
            flag.UpdatedAt = now;

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = actor.Id,
                ActorName = actor.Name,
                Action = "SubscriptionPoolThresholdUpdated",
                ResourceType = "AiConfig",
                ResourceId = "subscription-pool",
                Details = System.Text.Json.JsonSerializer.Serialize(new
                {
                    switchPercent = percent,
                    reason = dto.Reason,
                }, AiPipelineJson.Options),
            });
            await db.SaveChangesAsync(ct);
            await pool.RefreshAsync(accountState, ct);
            return Results.Ok(new { policy = new { switchPercent = pool.Policy.SwitchPercent } });
        }).RequireRateLimiting("PerUserWrite");

        group.MapPut("/accounts/drain", async (
            DrainAccountDto dto,
            LearnerDbContext db,
            HttpContext http,
            ISubscriptionAccountPool pool,
            CancellationToken ct) =>
        {
            var code = (dto.ProviderCode ?? string.Empty).Trim().ToLowerInvariant();
            if (OetLearner.Api.Services.AiPipeline.SubscriptionAccountGroups.GroupOf(code) is null)
                return new ApiErrorResult(400, "subscription_account_unknown", $"'{code}' is not a subscription account of a known engine group.");

            var now = DateTimeOffset.UtcNow;
            var actor = Actor(http);
            var key = OetLearner.Api.Services.AiPipeline.SubscriptionAccountStateProvider.DrainFlagPrefix + code;
            var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, ct);
            if (flag is null)
            {
                flag = new FeatureFlag
                {
                    Id = $"FLG-{Guid.NewGuid():N}"[..12],
                    Name = $"Drain subscription account {code}",
                    Key = key,
                    FlagType = FeatureFlagType.Operational,
                    Owner = "Owner",
                    CreatedAt = now,
                };
                db.FeatureFlags.Add(flag);
            }

            flag.Enabled = dto.Drain;
            flag.RolloutPercentage = 0;
            flag.Description = "Drained accounts are parked at the end of their engine group: grading and reviews prefer the healthy accounts first, and a drained account is only used once the others fail.";
            flag.UpdatedAt = now;

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = actor.Id,
                ActorName = actor.Name,
                Action = dto.Drain ? "SubscriptionAccountDrained" : "SubscriptionAccountUndrained",
                ResourceType = "AiConfig",
                ResourceId = code,
                Details = System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = code,
                    drained = dto.Drain,
                    reason = dto.Reason,
                }, AiPipelineJson.Options),
            });
            await db.SaveChangesAsync(ct);
            var state = http.RequestServices.GetRequiredService<OetLearner.Api.Services.AiPipeline.ISubscriptionAccountStateProvider>();
            await pool.RefreshAsync(state, ct);
            return Results.Ok(new { providerCode = code, drained = dto.Drain, standing = pool.Accounts.FirstOrDefault(a => a.ProviderCode == code)?.Standing });
        }).RequireRateLimiting("PerUserWrite");

        group.MapGet("/credit-grants", async (IAiPipelineOverviewService overview, CancellationToken ct) =>
            Results.Ok(new { credits = (await overview.BuildAsync(null, ct)).Credits }));

        group.MapPost("/credit-grants", async (
            CreditGrantDto dto,
            LearnerDbContext db,
            HttpContext http,
            IAiPipelineOverviewService overview,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ProviderCode))
                return new ApiErrorResult(400, "ai_credit_provider_required", "Provider is required.");
            if (dto.GrantUsd <= 0)
                return new ApiErrorResult(400, "ai_credit_amount_invalid", "The credit amount must be positive.");

            var providerCode = dto.ProviderCode.Trim().ToLowerInvariant();
            if (!await db.AiProviders.AsNoTracking().AnyAsync(p => p.Code == providerCode, ct))
                return new ApiErrorResult(400, "ai_credit_provider_unknown", $"Provider '{providerCode}' does not exist. Add it under Providers and keys first.");

            var now = DateTimeOffset.UtcNow;
            var actor = Actor(http);
            var grant = new AiCreditGrant
            {
                Id = Guid.NewGuid().ToString("N"),
                ProviderCode = providerCode,
                GrantUsd = dto.GrantUsd,
                StartsAt = dto.StartsAt ?? now,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note!.Trim()[..Math.Min(512, dto.Note!.Trim().Length)],
                CreatedByAdminId = actor.Id,
                CreatedByAdminName = actor.Name,
                CreatedAt = now,
            };
            db.AiCreditGrants.Add(grant);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = actor.Id,
                ActorName = actor.Name,
                Action = "AiCreditGrantCreated",
                ResourceType = "AiConfig",
                ResourceId = grant.Id,
                Details = System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = grant.ProviderCode,
                    grantUsd = grant.GrantUsd,
                    startsAt = grant.StartsAt,
                    note = grant.Note,
                }, AiPipelineJson.Options),
            });
            await db.SaveChangesAsync(ct);

            // Drop the 60s cache so the panel shows the new grant immediately.
            overview.InvalidateCache();
            return Results.Ok(new { grant.Id, grant.ProviderCode, grant.GrantUsd, grant.StartsAt });
        }).RequireRateLimiting("PerUserWrite");

        group.MapDelete("/credit-grants/{id}", async (
            string id,
            LearnerDbContext db,
            HttpContext http,
            IAiPipelineOverviewService overview,
            CancellationToken ct) =>
        {
            var grant = await db.AiCreditGrants.FirstOrDefaultAsync(g => g.Id == id, ct);
            if (grant is null) return Results.NotFound();
            var now = DateTimeOffset.UtcNow;
            var actor = Actor(http);
            db.AiCreditGrants.Remove(grant);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = actor.Id,
                ActorName = actor.Name,
                Action = "AiCreditGrantDeleted",
                ResourceType = "AiConfig",
                ResourceId = grant.Id,
                Details = System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = grant.ProviderCode,
                    grantUsd = grant.GrantUsd,
                    startsAt = grant.StartsAt,
                }, AiPipelineJson.Options),
            });
            await db.SaveChangesAsync(ct);
            overview.InvalidateCache();
            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        return app;
    }

    private static AiPipelineActor Actor(HttpContext http)
    {
        var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        return new AiPipelineActor(id, http.User.FindFirstValue(ClaimTypes.Name) ?? id);
    }

    private sealed record Violation(string Provider, DateTimeOffset? DisabledAt, int CallsAfterGrace, int CallsInsideGrace);

    public sealed record ProviderOption(
        string Code, string Name, string Dialect, string DefaultModel, bool IsActive,
        bool HasKey, string? LastTestStatus, DateTimeOffset? LastTestedAt, bool IsSubscriptionBridge);

    public sealed record SaveStageDto(
        bool StageEnabled,
        IReadOnlyList<AiPipelineHopInput> Hops,
        int ExpectedVersion,
        string? Reason,
        string? Confirmation);

    public sealed record RollbackDto(int ToVersion, int ExpectedVersion, string? Reason);

    public sealed record RestoreDto(int ExpectedVersion, string? Reason);

    public sealed record CreditGrantDto(string ProviderCode, decimal GrantUsd, DateTimeOffset? StartsAt, string? Note);

    public sealed record DrainAccountDto(string ProviderCode, bool Drain, string? Reason);

    public sealed record ThresholdDto(double SwitchPercent, string? Reason);
}
