using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin operations / budget / circuit surface for the AI control plane.
/// Same auth as <see cref="AiUsageAdminEndpoints"/>: <c>AdminAiConfig</c> +
/// <c>PerUser</c> rate limit. Parent wires
/// <c>app.MapAiOperationsAdminEndpoints()</c> from Program.cs.
///
/// Candidate-facing payloads never include tokens, dollars, or provider
/// secrets; the budget list is an admin-only ceiling view (scope/limit/
/// reserved/committed only).
/// </summary>
public static class AiOperationsAdminEndpoints
{
    public static IEndpointRouteBuilder MapAiOperationsAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/ai")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        group.MapGet("/operations", ListOperationsAsync);
        group.MapGet("/benchmark-runs", ListBenchmarkRunsAsync);
        group.MapGet("/budgets", ListBudgetsAsync);
        group.MapPost("/budgets/override", CreateBudgetOverrideAsync);
        group.MapGet("/circuits", ListCircuitsAsync);
        group.MapPost("/circuits/{key}/reset", ResetCircuitAsync);

        return app;
    }

    private static async Task<IResult> ListOperationsAsync(
        LearnerDbContext db,
        CancellationToken ct,
        string? state,
        string? featureCode,
        int? page,
        int? pageSize)
    {
        var pageNum = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 50, 1, 200);

        var query = db.AiOperations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(state)
            && Enum.TryParse<AiOperationState>(state, ignoreCase: true, out var stateEnum))
        {
            query = query.Where(o => o.State == stateEnum);
        }

        if (!string.IsNullOrWhiteSpace(featureCode))
        {
            query = query.Where(o => o.FeatureCode == featureCode);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .Select(o => new
            {
                id = o.Id,
                module = o.Module,
                featureCode = o.FeatureCode,
                userId = o.UserId,
                tenantId = o.TenantId,
                resourceId = o.ResourceId,
                resourceType = o.ResourceType,
                state = o.State.ToString(),
                operationClass = o.OperationClass.ToString(),
                createdAt = o.CreatedAt,
                updatedAt = o.UpdatedAt,
                resultRef = o.ResultRef,
            })
            .ToListAsync(ct);

        return Results.Ok(new { page = pageNum, pageSize = size, total, rows });
    }

    private static async Task<IResult> ListBenchmarkRunsAsync(
        LearnerDbContext db,
        CancellationToken ct,
        string? featureCode)
    {
        var query = db.AiProviderBenchmarkRuns.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(featureCode))
            query = query.Where(r => r.FeatureCode == featureCode);

        var rows = await query
            .OrderByDescending(r => r.RecordedAt)
            .Take(200)
            .Select(r => new
            {
                id = r.Id,
                featureCode = r.FeatureCode,
                providerCode = r.ProviderCode,
                model = r.Model,
                corpusVersion = r.CorpusVersion,
                passed = r.Passed,
                rollbackTarget = r.RollbackTargetRouteId,
                rollbackProviderCode = r.RollbackProviderCode,
                rollbackModel = r.RollbackModel,
                recordedAt = r.RecordedAt,
                reportJson = r.ReportJson,
            })
            .ToListAsync(ct);

        return Results.Ok(new { rows });
    }

    private static async Task<IResult> ListBudgetsAsync(LearnerDbContext db, CancellationToken ct)
    {
        var rows = await db.AiBudgetPeriods.AsNoTracking()
            .OrderBy(p => p.Scope)
            .ThenBy(p => p.PeriodKey)
            .Select(p => new
            {
                scope = p.Scope,
                periodKey = p.PeriodKey,
                limit = p.LimitUsd,
                reserved = p.ReservedUsd,
                committed = p.CommittedUsd,
            })
            .ToListAsync(ct);

        return Results.Ok(new { rows });
    }

    private static async Task<IResult> CreateBudgetOverrideAsync(
        AiBudgetOverrideRequest request,
        IAiBudgetOverrideService overrides,
        HttpContext http,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Scope)
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.AmountUsd <= 0m)
        {
            return Results.BadRequest(new { error = "scope, amountUsd, and reason are required." });
        }

        var actorAdminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        try
        {
            var row = await overrides.CreateAsync(
                request.Scope,
                request.AmountUsd,
                request.Reason,
                actorAdminId,
                request.ExpiresAt,
                ct);
            return Results.Ok(new
            {
                id = row.Id,
                scope = row.Scope,
                amountUsd = row.AmountUsd,
                reason = row.Reason,
                expiresAt = row.ExpiresAt,
                createdAt = row.CreatedAt,
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListCircuitsAsync(IAiCircuitBreakerStore circuits, CancellationToken ct)
    {
        var rows = await circuits.ListAsync(ct);
        return Results.Ok(new
        {
            rows = rows.Select(r => new
            {
                id = r.Id,
                kind = r.Kind,
                key = r.Key,
                state = r.State,
                failureCount = r.FailureCount,
                openedAt = r.OpenedAt,
                openUntil = r.OpenUntil,
                lastFailureAt = r.LastFailureAt,
                lastFailureCode = r.LastFailureCode,
                probeInFlight = r.ProbeInFlight,
                updatedAt = r.UpdatedAt,
            }),
        });
    }

    private static async Task<IResult> ResetCircuitAsync(
        string key,
        IAiCircuitBreakerStore circuits,
        CancellationToken ct,
        string? kind)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.BadRequest(new { error = "key is required." });
        }

        var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedKind is not (AiCircuitBreakerStore.KindProvider or AiCircuitBreakerStore.KindCredential))
        {
            return Results.BadRequest(new { error = "kind must be provider or credential." });
        }

        await circuits.ResetAsync(normalizedKind, key, ct);
        return Results.Ok(new { kind = normalizedKind, key, state = AiCircuitBreakerStore.StateClosed });
    }
}
