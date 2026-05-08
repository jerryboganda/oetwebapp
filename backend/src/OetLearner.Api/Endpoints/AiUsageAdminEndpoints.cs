using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Read + write admin surface for the AI Usage Management subsystem.
/// Usage log is read-only; plans, policy, and user overrides are CRUD.
///
/// Authorisation: requires the <c>ai_config</c> permission (or
/// <c>system_admin</c>), matching the sidebar mapping in
/// <c>lib/admin-permissions.ts</c>.
/// </summary>
public static class AiUsageAdminEndpoints
{
    public static IEndpointRouteBuilder MapAiUsageAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/ai")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        // ── Paginated usage log ─────────────────────────────────────────────
        group.MapGet("/usage", async (
            LearnerDbContext db,
            CancellationToken ct,
            string? userId,
            string? featureCode,
            string? providerId,
            string? accountId,
            string? outcome,
            string? periodMonthKey,
            string? periodDayKey,
            int? page,
            int? pageSize) =>
        {
            var pageNum = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 500);

            var query = db.AiUsageRecords.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(userId))
                query = query.Where(r => r.UserId == userId);
            if (!string.IsNullOrWhiteSpace(featureCode))
                query = query.Where(r => r.FeatureCode == featureCode);
            if (!string.IsNullOrWhiteSpace(providerId))
                query = query.Where(r => r.ProviderId == providerId);
            if (!string.IsNullOrWhiteSpace(accountId))
                query = query.Where(r => r.AccountId == accountId);
            if (!string.IsNullOrWhiteSpace(periodMonthKey))
                query = query.Where(r => r.PeriodMonthKey == periodMonthKey);
            if (!string.IsNullOrWhiteSpace(periodDayKey))
                query = query.Where(r => r.PeriodDayKey == periodDayKey);
            if (!string.IsNullOrWhiteSpace(outcome)
                && Enum.TryParse<AiCallOutcome>(outcome, ignoreCase: true, out var outcomeEnum))
            {
                query = query.Where(r => r.Outcome == outcomeEnum);
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .Select(r => new
                {
                    id = r.Id,
                    userId = r.UserId,
                    authAccountId = r.AuthAccountId,
                    tenantId = r.TenantId,
                    featureCode = r.FeatureCode,
                    providerId = r.ProviderId,
                    accountId = r.AccountId,
                    failoverTrace = r.FailoverTrace,
                    model = r.Model,
                    keySource = r.KeySource.ToString(),
                    rulebookVersion = r.RulebookVersion,
                    promptTemplateId = r.PromptTemplateId,
                    promptTokens = r.PromptTokens,
                    completionTokens = r.CompletionTokens,
                    totalTokens = r.PromptTokens + r.CompletionTokens,
                    costEstimateUsd = r.CostEstimateUsd,
                    outcome = r.Outcome.ToString(),
                    errorCode = r.ErrorCode,
                    errorMessage = r.ErrorMessage,
                    latencyMs = r.LatencyMs,
                    retryCount = r.RetryCount,
                    policyTrace = r.PolicyTrace,
                    createdAt = r.CreatedAt,
                    periodMonthKey = r.PeriodMonthKey,
                    periodDayKey = r.PeriodDayKey,
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                page = pageNum,
                pageSize = size,
                total,
                rows,
            });
        });

        // ── Summary by feature ──────────────────────────────────────────────
        group.MapGet("/usage/summary", async (
            LearnerDbContext db,
            CancellationToken ct,
            string? periodMonthKey,
            string? groupBy) =>
        {
            // periodMonthKey defaults to current month (UTC)
            var monthKey = string.IsNullOrWhiteSpace(periodMonthKey)
                ? DateTimeOffset.UtcNow.ToString("yyyy-MM")
                : periodMonthKey;

            var query = db.AiUsageRecords.AsNoTracking()
                .Where(r => r.PeriodMonthKey == monthKey);

            var key = (groupBy ?? "feature").ToLowerInvariant();
            object result = key switch
            {
                "provider" => await query
                    .GroupBy(r => r.ProviderId ?? "(none)")
                    .Select(g => new
                    {
                        key = g.Key,
                        calls = g.Count(),
                        promptTokens = g.Sum(x => x.PromptTokens),
                        completionTokens = g.Sum(x => x.CompletionTokens),
                        totalTokens = g.Sum(x => x.PromptTokens + x.CompletionTokens),
                        costEstimateUsd = g.Sum(x => x.CostEstimateUsd),
                        successes = g.Count(x => x.Outcome == AiCallOutcome.Success),
                        failures = g.Count(x => x.Outcome != AiCallOutcome.Success),
                    })
                    .OrderByDescending(r => r.totalTokens)
                    .ToListAsync(ct),

                "account" => await query
                    .Where(r => r.AccountId != null)
                    .GroupBy(r => new { ProviderId = r.ProviderId ?? "(none)", AccountId = r.AccountId! })
                    .Select(g => new
                    {
                        key = g.Key.AccountId,
                        providerId = g.Key.ProviderId,
                        calls = g.Count(),
                        promptTokens = g.Sum(x => x.PromptTokens),
                        completionTokens = g.Sum(x => x.CompletionTokens),
                        totalTokens = g.Sum(x => x.PromptTokens + x.CompletionTokens),
                        costEstimateUsd = g.Sum(x => x.CostEstimateUsd),
                        successes = g.Count(x => x.Outcome == AiCallOutcome.Success),
                        failures = g.Count(x => x.Outcome != AiCallOutcome.Success),
                        failovers = g.Count(x => x.FailoverTrace != null),
                    })
                    .OrderByDescending(r => r.totalTokens)
                    .ToListAsync(ct),

                "outcome" => await query
                    .GroupBy(r => r.Outcome)
                    .Select(g => new
                    {
                        key = g.Key.ToString(),
                        calls = g.Count(),
                        totalTokens = g.Sum(x => x.PromptTokens + x.CompletionTokens),
                    })
                    .OrderByDescending(r => r.calls)
                    .ToListAsync(ct),

                "user" => await query
                    .Where(r => r.UserId != null)
                    .GroupBy(r => r.UserId!)
                    .Select(g => new
                    {
                        key = g.Key,
                        calls = g.Count(),
                        totalTokens = g.Sum(x => x.PromptTokens + x.CompletionTokens),
                        costEstimateUsd = g.Sum(x => x.CostEstimateUsd),
                    })
                    .OrderByDescending(r => r.totalTokens)
                    .Take(100)
                    .ToListAsync(ct),

                _ /* feature */ => await query
                    .GroupBy(r => r.FeatureCode)
                    .Select(g => new
                    {
                        key = g.Key,
                        calls = g.Count(),
                        promptTokens = g.Sum(x => x.PromptTokens),
                        completionTokens = g.Sum(x => x.CompletionTokens),
                        totalTokens = g.Sum(x => x.PromptTokens + x.CompletionTokens),
                        costEstimateUsd = g.Sum(x => x.CostEstimateUsd),
                        successes = g.Count(x => x.Outcome == AiCallOutcome.Success),
                        failures = g.Count(x => x.Outcome != AiCallOutcome.Success),
                    })
                    .OrderByDescending(r => r.totalTokens)
                    .ToListAsync(ct),
            };

            return Results.Ok(new
            {
                periodMonthKey = monthKey,
                groupBy = key,
                rows = result,
            });
        });

        // ═══ Quota plans ═══════════════════════════════════════════════════
        group.MapGet("/plans", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var plans = await db.AiQuotaPlans.AsNoTracking()
                .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Code)
                .ToListAsync(ct);
            return Results.Ok(plans);
        });

        group.MapPost("/plans", async (AiQuotaPlanUpsertDto dto, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            {
                return Results.BadRequest(new { error = "Code and Name are required." });
            }
            var now = DateTimeOffset.UtcNow;
            var plan = new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = dto.Code.Trim().ToLowerInvariant(),
                Name = dto.Name.Trim(),
                Description = dto.Description ?? string.Empty,
                Period = dto.Period,
                MonthlyTokenCap = dto.MonthlyTokenCap,
                DailyTokenCap = dto.DailyTokenCap,
                MaxConcurrentRequests = dto.MaxConcurrentRequests,
                RolloverPolicy = dto.RolloverPolicy,
                RolloverCapPct = dto.RolloverCapPct,
                OveragePolicy = dto.OveragePolicy,
                OverageRatePer1kTokens = dto.OverageRatePer1kTokens,
                AutoUpgradeTargetPlanCode = dto.AutoUpgradeTargetPlanCode,
                DegradeModel = dto.DegradeModel,
                AllowedFeaturesCsv = dto.AllowedFeaturesCsv ?? string.Empty,
                AllowedModelsCsv = dto.AllowedModelsCsv ?? string.Empty,
                IsActive = dto.IsActive,
                DisplayOrder = dto.DisplayOrder,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AiQuotaPlans.Add(plan);
            await SaveWithAuditAsync(db, http, "AiQuotaPlanCreated", plan.Id, plan.Code, ct);
            return Results.Created($"/v1/admin/ai/plans/{plan.Id}", plan);
        }).RequireRateLimiting("PerUserWrite");

        group.MapPut("/plans/{id}", async (string id, AiQuotaPlanUpsertDto dto, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var plan = await db.AiQuotaPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (plan is null) return Results.NotFound();
            plan.Name = dto.Name?.Trim() ?? plan.Name;
            plan.Description = dto.Description ?? plan.Description;
            plan.Period = dto.Period;
            plan.MonthlyTokenCap = dto.MonthlyTokenCap;
            plan.DailyTokenCap = dto.DailyTokenCap;
            plan.MaxConcurrentRequests = dto.MaxConcurrentRequests;
            plan.RolloverPolicy = dto.RolloverPolicy;
            plan.RolloverCapPct = dto.RolloverCapPct;
            plan.OveragePolicy = dto.OveragePolicy;
            plan.OverageRatePer1kTokens = dto.OverageRatePer1kTokens;
            plan.AutoUpgradeTargetPlanCode = dto.AutoUpgradeTargetPlanCode;
            plan.DegradeModel = dto.DegradeModel;
            plan.AllowedFeaturesCsv = dto.AllowedFeaturesCsv ?? string.Empty;
            plan.AllowedModelsCsv = dto.AllowedModelsCsv ?? string.Empty;
            plan.IsActive = dto.IsActive;
            plan.DisplayOrder = dto.DisplayOrder;
            plan.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveWithAuditAsync(db, http, "AiQuotaPlanUpdated", plan.Id, plan.Code, ct);
            return Results.Ok(plan);
        }).RequireRateLimiting("PerUserWrite");

        group.MapDelete("/plans/{id}", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var plan = await db.AiQuotaPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (plan is null) return Results.NotFound();
            plan.IsActive = false;
            plan.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveWithAuditAsync(db, http, "AiQuotaPlanDeactivated", plan.Id, plan.Code, ct);
            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        // ═══ Global policy / kill-switch / budget ══════════════════════════
        group.MapGet("/global-policy", async (IAiQuotaService quota, CancellationToken ct) =>
        {
            var policy = await quota.GetGlobalPolicyAsync(ct);
            return Results.Ok(policy);
        });

        group.MapPut("/global-policy", async (
            AiGlobalPolicyUpsertDto dto,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var row = await db.AiGlobalPolicies.FirstOrDefaultAsync(p => p.Id == "global", ct);
            if (row is null)
            {
                row = new AiGlobalPolicy { Id = "global" };
                db.AiGlobalPolicies.Add(row);
            }
            row.KillSwitchEnabled = dto.KillSwitchEnabled;
            row.KillSwitchScope = dto.KillSwitchScope;
            row.KillSwitchReason = dto.KillSwitchReason;
            row.DisabledFeaturesCsv = NormaliseCsv(dto.DisabledFeaturesCsv);
            row.MonthlyBudgetUsd = dto.MonthlyBudgetUsd;
            row.SoftWarnPct = Math.Clamp(dto.SoftWarnPct, 0, 100);
            row.HardKillPct = Math.Clamp(dto.HardKillPct, 0, 150);
            row.AllowByokOnScoringFeatures = dto.AllowByokOnScoringFeatures;
            row.AllowByokOnNonScoringFeatures = dto.AllowByokOnNonScoringFeatures;
            row.DefaultPlatformProviderId = string.IsNullOrWhiteSpace(dto.DefaultPlatformProviderId)
                ? "digitalocean-serverless" : dto.DefaultPlatformProviderId.Trim();
            row.ByokErrorCooldownHours = Math.Max(0, dto.ByokErrorCooldownHours);
            row.ByokTransientRetryCount = Math.Clamp(dto.ByokTransientRetryCount, 0, 5);
            row.AnomalyDetectionEnabled = dto.AnomalyDetectionEnabled;
            row.AnomalyMultiplierX = Math.Max(2m, dto.AnomalyMultiplierX);
            row.RowVersion += 1;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            await SaveWithAuditAsync(db, http, "AiGlobalPolicyUpdated", "global",
                row.KillSwitchEnabled ? "kill_switch=on" : "kill_switch=off", ct);
            return Results.Ok(row);
        }).RequireRateLimiting("PerUserWrite");

        group.MapPost("/kill-switch", async (
            AiKillSwitchDto dto,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var row = await db.AiGlobalPolicies.FirstOrDefaultAsync(p => p.Id == "global", ct);
            if (row is null)
            {
                row = new AiGlobalPolicy { Id = "global" };
                db.AiGlobalPolicies.Add(row);
            }
            row.KillSwitchEnabled = dto.Enabled;
            row.KillSwitchScope = dto.Scope;
            row.KillSwitchReason = dto.Reason;
            row.RowVersion += 1;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            await SaveWithAuditAsync(db, http, "AiKillSwitchToggled", "global",
                $"enabled={dto.Enabled} scope={dto.Scope}", ct);
            return Results.Ok(row);
        }).RequireRateLimiting("PerUserWrite");

        // ═══ Per-user quota overrides ══════════════════════════════════════
        group.MapGet("/users/{userId}/override", async (string userId, LearnerDbContext db, CancellationToken ct) =>
        {
            var row = await db.AiUserQuotaOverrides.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return Results.Ok(row);
        });

        group.MapPut("/users/{userId}/override", async (
            string userId,
            AiUserOverrideUpsertDto dto,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var row = await db.AiUserQuotaOverrides.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (row is null)
            {
                row = new AiUserQuotaOverride { UserId = userId, CreatedAt = DateTimeOffset.UtcNow };
                db.AiUserQuotaOverrides.Add(row);
            }
            row.MonthlyTokenCapOverride = dto.MonthlyTokenCapOverride;
            row.DailyTokenCapOverride = dto.DailyTokenCapOverride;
            row.ForcePlanCode = dto.ForcePlanCode;
            row.AiDisabled = dto.AiDisabled;
            row.Reason = dto.Reason;
            row.GrantedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            row.ExpiresAt = dto.ExpiresAt;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveWithAuditAsync(db, http, "AiUserOverrideUpserted", userId, dto.Reason, ct);
            return Results.Ok(row);
        }).RequireRateLimiting("PerUserWrite");

        group.MapDelete("/users/{userId}/override", async (string userId, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.AiUserQuotaOverrides.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (row is null) return Results.NotFound();
            db.AiUserQuotaOverrides.Remove(row);
            await SaveWithAuditAsync(db, http, "AiUserOverrideRemoved", userId, null, ct);
            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        // ═══ Provider registry (Slice 5) ════════════════════════════════════
        group.MapGet("/providers", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var rows = await db.AiProviders.AsNoTracking()
                .OrderBy(p => p.FailoverPriority).ThenBy(p => p.Code)
                .Select(p => new
                {
                    p.Id, p.Code, p.Name, p.Dialect, p.Category, p.BaseUrl, p.ApiKeyHint,
                    p.DefaultModel, p.ReasoningEffort, p.AllowedModelsCsv,
                    p.PricePer1kPromptTokens, p.PricePer1kCompletionTokens,
                    p.RetryCount, p.CircuitBreakerThreshold, p.CircuitBreakerWindowSeconds,
                    p.FailoverPriority, p.IsActive,
                    p.LastTestedAt, p.LastTestStatus, p.LastTestError,
                    p.CreatedAt, p.UpdatedAt, p.UpdatedByAdminId,
                })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapPost("/providers", async (
            AiProviderUpsertDto dto,
            LearnerDbContext db,
            IDataProtectionProvider dpProvider,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
                return Results.BadRequest(new { error = "Code and Name are required." });
            if (string.IsNullOrWhiteSpace(dto.BaseUrl))
                return Results.BadRequest(new { error = "BaseUrl is required." });
            if (string.IsNullOrWhiteSpace(dto.ApiKey) || dto.ApiKey.Length < 16)
                return Results.BadRequest(new { error = "ApiKey is required and must be at least 16 chars." });

            var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
            var now = DateTimeOffset.UtcNow;
            var row = new AiProvider
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = dto.Code.Trim().ToLowerInvariant(),
                Name = dto.Name.Trim(),
                Dialect = dto.Dialect,
                Category = dto.Category,
                BaseUrl = dto.BaseUrl.Trim(),
                EncryptedApiKey = protector.Protect(dto.ApiKey),
                ApiKeyHint = $"…{dto.ApiKey[^4..]}",
                DefaultModel = dto.DefaultModel ?? string.Empty,
                ReasoningEffort = string.IsNullOrWhiteSpace(dto.ReasoningEffort) ? null : dto.ReasoningEffort!.Trim().ToLowerInvariant(),
                AllowedModelsCsv = dto.AllowedModelsCsv ?? string.Empty,
                PricePer1kPromptTokens = dto.PricePer1kPromptTokens,
                PricePer1kCompletionTokens = dto.PricePer1kCompletionTokens,
                RetryCount = dto.RetryCount,
                CircuitBreakerThreshold = dto.CircuitBreakerThreshold,
                CircuitBreakerWindowSeconds = dto.CircuitBreakerWindowSeconds,
                FailoverPriority = dto.FailoverPriority,
                IsActive = dto.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier),
            };
            db.AiProviders.Add(row);
            await SaveWithAuditAsync(db, http, "AiProviderCreated", row.Id, row.Code, ct);
            return Results.Created($"/v1/admin/ai/providers/{row.Id}",
                new { row.Id, row.Code, row.ApiKeyHint });
        }).RequireRateLimiting("PerUserWrite");

        group.MapPut("/providers/{id}", async (
            string id,
            AiProviderUpsertDto dto,
            LearnerDbContext db,
            IDataProtectionProvider dpProvider,
            HttpContext http,
            CancellationToken ct) =>
        {
            var row = await db.AiProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (row is null) return Results.NotFound();
            row.Name = dto.Name?.Trim() ?? row.Name;
            row.Dialect = dto.Dialect;
            row.Category = dto.Category;
            if (!string.IsNullOrWhiteSpace(dto.BaseUrl)) row.BaseUrl = dto.BaseUrl.Trim();
            if (!string.IsNullOrWhiteSpace(dto.ApiKey) && dto.ApiKey.Length >= 16)
            {
                var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
                row.EncryptedApiKey = protector.Protect(dto.ApiKey);
                row.ApiKeyHint = $"…{dto.ApiKey[^4..]}";
            }
            row.DefaultModel = dto.DefaultModel ?? row.DefaultModel;
            if (dto.ReasoningEffort is not null)
            {
                row.ReasoningEffort = string.IsNullOrWhiteSpace(dto.ReasoningEffort) ? null : dto.ReasoningEffort.Trim().ToLowerInvariant();
            }
            row.AllowedModelsCsv = dto.AllowedModelsCsv ?? row.AllowedModelsCsv;
            row.PricePer1kPromptTokens = dto.PricePer1kPromptTokens;
            row.PricePer1kCompletionTokens = dto.PricePer1kCompletionTokens;
            row.RetryCount = dto.RetryCount;
            row.CircuitBreakerThreshold = dto.CircuitBreakerThreshold;
            row.CircuitBreakerWindowSeconds = dto.CircuitBreakerWindowSeconds;
            row.FailoverPriority = dto.FailoverPriority;
            row.IsActive = dto.IsActive;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            await SaveWithAuditAsync(db, http, "AiProviderUpdated", row.Id, row.Code, ct);
            return Results.Ok(new { row.Id, row.Code, row.ApiKeyHint });
        }).RequireRateLimiting("PerUserWrite");

        group.MapDelete("/providers/{id}", async (string id, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.AiProviders.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (row is null) return Results.NotFound();
            row.IsActive = false;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveWithAuditAsync(db, http, "AiProviderDeactivated", row.Id, row.Code, ct);
            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        // Phase 4: admin "Test connection" probe. Sends a 1-token chat
        // completion to the provider and persists the classifier outcome
        // (ok / auth / rate_limited / network / unknown) plus a timestamp.
        // Bypasses gateway grounding + quota — see
        // AiProviderConnectionTester XML doc.
        group.MapPost("/providers/{code}/test", async (
            string code,
            IAiProviderConnectionTester tester,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var providerRow = await db.AiProviders.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == code, ct);
            if (providerRow is null) return Results.NotFound();
            var result = await tester.TestProviderAsync(code, ct);
            // Audit AFTER tester.SaveChanges so the audit row reflects
            // the persisted status. Reload to attach to context for the
            // audit save (tester used its own scope-shared db).
            var tracked = await db.AiProviders.FirstAsync(p => p.Code == code, ct);
            await SaveWithAuditAsync(db, http,
                result.Status == AiProviderTestStatuses.Ok ? "AiProviderTested" : "AiProviderTestFailed",
                tracked.Id,
                $"code={code} status={result.Status} latency={result.LatencyMs}ms", ct);
            return Results.Ok(new
            {
                status = result.Status,
                errorMessage = result.ErrorMessage,
                latencyMs = result.LatencyMs,
                testedAt = result.TestedAt,
            });
        }).RequireRateLimiting("PerUserWrite");

        // ═══ Multi-account pool (Phase 2 Slice 2b) ═══════════════════════════
        // Admin CRUD over AiProviderAccount rows. Used today by the Copilot
        // dialect to spread requests across multiple PATs with auto-failover
        // (see AiProviderAccountRegistry + CopilotAiModelProvider). Other
        // dialects can use it too — the pool is generic.
        group.MapGet("/providers/{providerId}/accounts", async (
            string providerId,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var providerExists = await db.AiProviders.AsNoTracking()
                .AnyAsync(p => p.Id == providerId, ct);
            if (!providerExists) return Results.NotFound();

            var rows = await db.AiProviderAccounts.AsNoTracking()
                .Where(a => a.ProviderId == providerId)
                .OrderBy(a => a.Priority).ThenBy(a => a.Label)
                .Select(a => new
                {
                    a.Id, a.ProviderId, a.Label, a.ApiKeyHint,
                    a.MonthlyRequestCap, a.RequestsUsedThisMonth,
                    a.Priority, a.ExhaustedUntil, a.IsActive,
                    a.PeriodMonthKey,
                    a.LastTestedAt, a.LastTestStatus, a.LastTestError,
                    a.CreatedAt, a.UpdatedAt, a.UpdatedByAdminId,
                })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapPost("/providers/{providerId}/accounts", async (
            string providerId,
            AiProviderAccountUpsertDto dto,
            LearnerDbContext db,
            IDataProtectionProvider dpProvider,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Label))
                return Results.BadRequest(new { error = "Label is required." });
            if (string.IsNullOrWhiteSpace(dto.ApiKey) || dto.ApiKey.Length < 16)
                return Results.BadRequest(new { error = "ApiKey is required and must be at least 16 chars." });
            if (dto.MonthlyRequestCap is < 1)
                return Results.BadRequest(new { error = "MonthlyRequestCap must be null or >= 1." });

            var providerExists = await db.AiProviders.AsNoTracking()
                .AnyAsync(p => p.Id == providerId, ct);
            if (!providerExists) return Results.NotFound();

            var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
            var now = DateTimeOffset.UtcNow;
            var row = new AiProviderAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                ProviderId = providerId,
                Label = dto.Label.Trim(),
                EncryptedApiKey = protector.Protect(dto.ApiKey),
                ApiKeyHint = $"…{dto.ApiKey[^4..]}",
                MonthlyRequestCap = dto.MonthlyRequestCap,
                RequestsUsedThisMonth = 0,
                Priority = dto.Priority,
                ExhaustedUntil = null,
                IsActive = dto.IsActive,
                PeriodMonthKey = now.ToString("yyyy-MM"),
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier),
            };
            db.AiProviderAccounts.Add(row);
            await SaveWithAuditAsync(db, http, "AiProviderAccountCreated", row.Id,
                $"provider={providerId} label={row.Label}", ct);
            return Results.Created(
                $"/v1/admin/ai/providers/{providerId}/accounts/{row.Id}",
                new { row.Id, row.Label, row.ApiKeyHint });
        }).RequireRateLimiting("PerUserWrite");

        group.MapPut("/providers/{providerId}/accounts/{accountId}", async (
            string providerId,
            string accountId,
            AiProviderAccountUpsertDto dto,
            LearnerDbContext db,
            IDataProtectionProvider dpProvider,
            HttpContext http,
            CancellationToken ct) =>
        {
            var row = await db.AiProviderAccounts
                .FirstOrDefaultAsync(a => a.Id == accountId && a.ProviderId == providerId, ct);
            if (row is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(dto.Label)) row.Label = dto.Label.Trim();
            if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            {
                if (dto.ApiKey.Length < 16)
                    return Results.BadRequest(new { error = "ApiKey must be at least 16 chars." });
                var protector = dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
                row.EncryptedApiKey = protector.Protect(dto.ApiKey);
                row.ApiKeyHint = $"…{dto.ApiKey[^4..]}";
            }
            if (dto.MonthlyRequestCap is < 1)
                return Results.BadRequest(new { error = "MonthlyRequestCap must be null or >= 1." });
            row.MonthlyRequestCap = dto.MonthlyRequestCap;
            row.Priority = dto.Priority;
            row.IsActive = dto.IsActive;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

            await SaveWithAuditAsync(db, http, "AiProviderAccountUpdated", row.Id,
                $"provider={providerId} label={row.Label}", ct);
            return Results.Ok(new { row.Id, row.Label, row.ApiKeyHint });
        }).RequireRateLimiting("PerUserWrite");

        group.MapDelete("/providers/{providerId}/accounts/{accountId}", async (
            string providerId,
            string accountId,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var row = await db.AiProviderAccounts
                .FirstOrDefaultAsync(a => a.Id == accountId && a.ProviderId == providerId, ct);
            if (row is null) return Results.NotFound();
            row.IsActive = false;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            await SaveWithAuditAsync(db, http, "AiProviderAccountDeactivated", row.Id,
                $"provider={providerId} label={row.Label}", ct);
            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        // Manual "release from quarantine + reset counter" — mostly for ops
        // when an account was wrongly 401'd or to test failover. Audited.
        group.MapPost("/providers/{providerId}/accounts/{accountId}/reset", async (
            string providerId,
            string accountId,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var row = await db.AiProviderAccounts
                .FirstOrDefaultAsync(a => a.Id == accountId && a.ProviderId == providerId, ct);
            if (row is null) return Results.NotFound();
            var now = DateTimeOffset.UtcNow;
            row.RequestsUsedThisMonth = 0;
            row.ExhaustedUntil = null;
            row.PeriodMonthKey = now.ToString("yyyy-MM");
            row.UpdatedAt = now;
            row.UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            await SaveWithAuditAsync(db, http, "AiProviderAccountReset", row.Id,
                $"provider={providerId} label={row.Label}", ct);
            return Results.Ok(new { row.Id, row.RequestsUsedThisMonth, row.ExhaustedUntil });
        }).RequireRateLimiting("PerUserWrite");

        // Phase 4: per-account connectivity probe. Same classifier as the
        // provider-level probe but uses the account's PAT.
        group.MapPost("/providers/{providerId}/accounts/{accountId}/test", async (
            string providerId,
            string accountId,
            IAiProviderConnectionTester tester,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var exists = await db.AiProviderAccounts.AsNoTracking()
                .AnyAsync(a => a.Id == accountId && a.ProviderId == providerId, ct);
            if (!exists) return Results.NotFound();
            var result = await tester.TestAccountAsync(providerId, accountId, ct);
            await SaveWithAuditAsync(db, http,
                result.Status == AiProviderTestStatuses.Ok ? "AiProviderAccountTested" : "AiProviderAccountTestFailed",
                accountId,
                $"provider={providerId} status={result.Status} latency={result.LatencyMs}ms", ct);
            return Results.Ok(new
            {
                status = result.Status,
                errorMessage = result.ErrorMessage,
                latencyMs = result.LatencyMs,
                testedAt = result.TestedAt,
            });
        }).RequireRateLimiting("PerUserWrite");

        // ═══ Per-feature routing overrides (Phase 7) ═══════════════════════
        // GET   /v1/admin/ai/feature-routes              → list all rows + known feature codes
        // POST  /v1/admin/ai/feature-routes              → upsert a single route
        // POST  /v1/admin/ai/feature-routes/bulk-copilot → flip the canonical bulk-route set to copilot
        // DELETE /v1/admin/ai/feature-routes/{featureCode}
        group.MapGet("/feature-routes", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var rows = await db.AiFeatureRoutes.AsNoTracking()
                .OrderBy(r => r.FeatureCode)
                .Select(r => new
                {
                    r.Id, r.FeatureCode, r.ProviderCode, r.Model, r.IsActive,
                    r.CreatedAt, r.UpdatedAt, r.UpdatedByAdminId,
                })
                .ToListAsync(ct);
            return Results.Ok(new
            {
                rows,
                knownFeatureCodes = AiFeatureRouteResolver.KnownFeatureCodes,
                copilotBulkRouteTargets = AiFeatureRouteResolver.CopilotBulkRouteTargets,
            });
        });

        group.MapPost("/feature-routes", async (
            AiFeatureRouteUpsertDto dto,
            LearnerDbContext db,
            IAiFeatureRouteResolver resolver,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.FeatureCode) || !resolver.IsKnownFeatureCode(dto.FeatureCode))
                return Results.BadRequest(new { error = "Unknown feature code." });
            if (string.IsNullOrWhiteSpace(dto.ProviderCode))
                return Results.BadRequest(new { error = "ProviderCode is required." });

            var providerExists = await db.AiProviders.AsNoTracking()
                .AnyAsync(p => p.Code == dto.ProviderCode && p.IsActive, ct);
            if (!providerExists)
                return Results.BadRequest(new { error = $"Provider '{dto.ProviderCode}' is not registered or not active." });

            var now = DateTimeOffset.UtcNow;
            var row = await db.AiFeatureRoutes.FirstOrDefaultAsync(r => r.FeatureCode == dto.FeatureCode, ct);
            var auditEvent = row is null ? "AiFeatureRouteCreated" : "AiFeatureRouteUpdated";
            if (row is null)
            {
                row = new AiFeatureRoute
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FeatureCode = dto.FeatureCode,
                    CreatedAt = now,
                };
                db.AiFeatureRoutes.Add(row);
            }
            row.ProviderCode = dto.ProviderCode;
            row.Model = string.IsNullOrWhiteSpace(dto.Model) ? null : dto.Model.Trim();
            row.IsActive = dto.IsActive;
            row.UpdatedAt = now;
            row.UpdatedByAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            await SaveWithAuditAsync(db, http, auditEvent, row.Id,
                $"feature={dto.FeatureCode} provider={dto.ProviderCode} active={dto.IsActive}", ct);
            return Results.Ok(new { row.Id, row.FeatureCode, row.ProviderCode, row.Model, row.IsActive });
        }).RequireRateLimiting("PerUserWrite");

        group.MapDelete("/feature-routes/{featureCode}", async (
            string featureCode, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var row = await db.AiFeatureRoutes.FirstOrDefaultAsync(r => r.FeatureCode == featureCode, ct);
            if (row is null) return Results.NotFound();
            db.AiFeatureRoutes.Remove(row);
            await SaveWithAuditAsync(db, http, "AiFeatureRouteDeleted", row.Id,
                $"feature={featureCode}", ct);
            return Results.NoContent();
        }).RequireRateLimiting("PerUserWrite");

        // Bulk-route action — flips the canonical set of feature codes to
        // route through the Copilot provider. Refuses when no Copilot row
        // is registered + active so the UI can disable the button.
        group.MapPost("/feature-routes/bulk-copilot", async (
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            const string copilot = "copilot";
            var copilotActive = await db.AiProviders.AsNoTracking()
                .AnyAsync(p => p.Code == copilot && p.IsActive, ct);
            if (!copilotActive)
                return Results.BadRequest(new { error = "Copilot provider is not registered or not active." });

            var now = DateTimeOffset.UtcNow;
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var existing = await db.AiFeatureRoutes
                .Where(r => AiFeatureRouteResolver.CopilotBulkRouteTargets.Contains(r.FeatureCode))
                .ToListAsync(ct);
            var existingByCode = existing.ToDictionary(r => r.FeatureCode, StringComparer.OrdinalIgnoreCase);

            var changed = new List<string>();
            foreach (var featureCode in AiFeatureRouteResolver.CopilotBulkRouteTargets)
            {
                if (existingByCode.TryGetValue(featureCode, out var row))
                {
                    if (string.Equals(row.ProviderCode, copilot, StringComparison.OrdinalIgnoreCase)
                        && row.IsActive)
                    {
                        continue;
                    }
                    row.ProviderCode = copilot;
                    row.IsActive = true;
                    row.UpdatedAt = now;
                    row.UpdatedByAdminId = adminId;
                }
                else
                {
                    db.AiFeatureRoutes.Add(new AiFeatureRoute
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        FeatureCode = featureCode,
                        ProviderCode = copilot,
                        Model = null,
                        IsActive = true,
                        CreatedAt = now,
                        UpdatedAt = now,
                        UpdatedByAdminId = adminId,
                    });
                }
                changed.Add(featureCode);
            }

            await SaveWithAuditAsync(db, http, "AiFeatureRoutesBulkCopilot", "*",
                $"changed={string.Join(',', changed)}", ct);
            return Results.Ok(new { changed });
        }).RequireRateLimiting("PerUserWrite");

        // ═══ Credit ledger (Slice 6) ═══════════════════════════════════════
        group.MapGet("/users/{userId}/credits", async (
            string userId, IAiCreditService credits, CancellationToken ct, int? page, int? pageSize) =>
        {
            var balance = await credits.GetBalanceAsync(userId, ct);
            var entries = await credits.ListAsync(userId, page ?? 1, pageSize ?? 50, ct);
            return Results.Ok(new { balance, entries });
        });

        group.MapPost("/users/{userId}/credits/grant", async (
            string userId,
            AiCreditGrantDto dto,
            IAiCreditService credits,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (dto.Tokens <= 0) return Results.BadRequest(new { error = "Tokens must be positive." });
            var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var source = dto.Source switch
            {
                "promo" => AiCreditSource.Promo,
                "purchase" => AiCreditSource.Purchase,
                _ => AiCreditSource.AdminAdjustment,
            };
            var entry = await credits.GrantAsync(
                userId, dto.Tokens, dto.CostUsd,
                source, dto.Description, dto.ReferenceId, dto.ExpiresAt, actorId, ct);
            await SaveWithAuditAsync(db, http, "AiCreditGranted", entry.Id,
                $"user={userId} tokens={dto.Tokens}", ct);
            return Results.Ok(entry);
        }).RequireRateLimiting("PerUserWrite");

        group.MapPost("/credits/sweep-expired", async (
            IAiCreditService credits, HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var count = await credits.SweepExpiredAsync(DateTimeOffset.UtcNow, ct);
            await SaveWithAuditAsync(db, http, "AiCreditsSweptExpired", "global",
                $"count={count}", ct);
            return Results.Ok(new { expired = count });
        }).RequireRateLimiting("PerUserWrite");

        // ═══ Analytics — daily trend, anomalies (Slice 7) ══════════════════
        group.MapGet("/usage/trend", async (
            LearnerDbContext db, CancellationToken ct,
            string? fromMonth, string? toMonth) =>
        {
            var now = DateTimeOffset.UtcNow;
            var from = fromMonth ?? now.AddMonths(-2).ToString("yyyy-MM");
            var to = toMonth ?? now.ToString("yyyy-MM");

            var rows = await db.AiUsageRecords.AsNoTracking()
                .Where(r => string.Compare(r.PeriodMonthKey, from) >= 0
                         && string.Compare(r.PeriodMonthKey, to) <= 0)
                .GroupBy(r => r.PeriodDayKey)
                .Select(g => new
                {
                    day = g.Key,
                    calls = g.Count(),
                    promptTokens = g.Sum(x => x.PromptTokens),
                    completionTokens = g.Sum(x => x.CompletionTokens),
                    totalTokens = g.Sum(x => x.PromptTokens + x.CompletionTokens),
                    costUsd = g.Sum(x => x.CostEstimateUsd),
                    successes = g.Count(x => x.Outcome == AiCallOutcome.Success),
                    failures = g.Count(x => x.Outcome != AiCallOutcome.Success),
                })
                .OrderBy(r => r.day)
                .ToListAsync(ct);

            return Results.Ok(new { fromMonth = from, toMonth = to, rows });
        });

        group.MapGet("/usage/anomalies", async (
            LearnerDbContext db,
            IAiQuotaService quota,
            CancellationToken ct) =>
        {
            var global = await quota.GetGlobalPolicyAsync(ct);
            if (!global.AnomalyDetectionEnabled)
            {
                return Results.Ok(new { enabled = false, rows = Array.Empty<object>() });
            }

            var now = DateTimeOffset.UtcNow;
            var today = now.ToString("yyyy-MM-dd");
            var windowStart = now.AddDays(-8);

            // Per-user daily totals for the last 8 days.
            var byUserDay = await db.AiUsageRecords.AsNoTracking()
                .Where(r => r.UserId != null && r.CreatedAt >= windowStart)
                .GroupBy(r => new { r.UserId, r.PeriodDayKey })
                .Select(g => new
                {
                    g.Key.UserId,
                    g.Key.PeriodDayKey,
                    Tokens = g.Sum(x => x.PromptTokens + x.CompletionTokens),
                })
                .ToListAsync(ct);

            var multiplier = (double)global.AnomalyMultiplierX;
            var flagged = byUserDay
                .GroupBy(x => x.UserId!)
                .Select(g =>
                {
                    var todayTokens = g.FirstOrDefault(x => x.PeriodDayKey == today)?.Tokens ?? 0;
                    var past = g.Where(x => x.PeriodDayKey != today).Select(x => (double)x.Tokens).OrderBy(v => v).ToList();
                    double median = past.Count == 0 ? 0 : past[past.Count / 2];
                    return new { UserId = g.Key, TokensToday = todayTokens, Median7d = median };
                })
                .Where(r => r.Median7d > 0 && r.TokensToday >= r.Median7d * multiplier)
                .OrderByDescending(r => r.TokensToday)
                .Take(50)
                .ToList();

            return Results.Ok(new
            {
                enabled = true,
                multiplier = global.AnomalyMultiplierX,
                rows = flagged,
            });
        });

        return app;
    }

    private static async Task SaveWithAuditAsync(
        LearnerDbContext db,
        HttpContext http,
        string action,
        string resourceId,
        string? details,
        CancellationToken ct)
    {
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var actorName = http.User.FindFirstValue(ClaimTypes.Name) ?? actorId;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = "AiConfig",
            ResourceId = resourceId,
            Details = details,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Trims, lower-cases and de-duplicates a CSV feature list so the
    /// stored value is canonical. Empty / null → empty string.</summary>
    private static string NormaliseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return string.Empty;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return string.Join(',', parts);
    }
}

public sealed record AiQuotaPlanUpsertDto(
    string Code,
    string Name,
    string? Description,
    AiQuotaPeriod Period,
    int MonthlyTokenCap,
    int DailyTokenCap,
    int MaxConcurrentRequests,
    AiQuotaRolloverPolicy RolloverPolicy,
    int RolloverCapPct,
    AiOveragePolicy OveragePolicy,
    decimal? OverageRatePer1kTokens,
    string? AutoUpgradeTargetPlanCode,
    string? DegradeModel,
    string? AllowedFeaturesCsv,
    string? AllowedModelsCsv,
    bool IsActive,
    int DisplayOrder);

public sealed record AiGlobalPolicyUpsertDto(
    bool KillSwitchEnabled,
    AiKillSwitchScope KillSwitchScope,
    string? KillSwitchReason,
    string? DisabledFeaturesCsv,
    decimal MonthlyBudgetUsd,
    int SoftWarnPct,
    int HardKillPct,
    bool AllowByokOnScoringFeatures,
    bool AllowByokOnNonScoringFeatures,
    string DefaultPlatformProviderId,
    int ByokErrorCooldownHours,
    int ByokTransientRetryCount,
    bool AnomalyDetectionEnabled,
    decimal AnomalyMultiplierX);

public sealed record AiKillSwitchDto(bool Enabled, AiKillSwitchScope Scope, string? Reason);

public sealed record AiUserOverrideUpsertDto(
    int? MonthlyTokenCapOverride,
    int? DailyTokenCapOverride,
    string? ForcePlanCode,
    bool AiDisabled,
    string? Reason,
    DateTimeOffset? ExpiresAt);

public sealed record AiProviderUpsertDto(
    string Code,
    string Name,
    AiProviderDialect Dialect,
    AiProviderCategory Category,
    string BaseUrl,
    string? ApiKey,
    string? DefaultModel,
    string? ReasoningEffort,
    string? AllowedModelsCsv,
    decimal PricePer1kPromptTokens,
    decimal PricePer1kCompletionTokens,
    int RetryCount,
    int CircuitBreakerThreshold,
    int CircuitBreakerWindowSeconds,
    int FailoverPriority,
    bool IsActive);

public sealed record AiProviderAccountUpsertDto(
    string Label,
    string? ApiKey,
    int? MonthlyRequestCap,
    int Priority,
    bool IsActive);

public sealed record AiCreditGrantDto(
    int Tokens,
    decimal CostUsd,
    string Source, // "promo" | "purchase" | "admin"
    string? Description,
    string? ReferenceId,
    DateTimeOffset? ExpiresAt);

public sealed record AiFeatureRouteUpsertDto(
    string FeatureCode,
    string ProviderCode,
    string? Model,
    bool IsActive);
