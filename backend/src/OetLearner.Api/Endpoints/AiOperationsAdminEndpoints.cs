using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Speaking;

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
        group.MapGet("/credit-costs", async (OetLearner.Api.Services.Billing.IAiCreditCostService costs, CancellationToken ct)
            => Results.Ok(new { rows = await costs.GetAllAsync(ct) }));
        group.MapPut("/credit-costs/{actionCode}", async (
            string actionCode,
            AiCreditCostUpsertDto dto,
            OetLearner.Api.Services.Billing.IAiCreditCostService costs,
            HttpContext http,
            CancellationToken ct) =>
        {
            try
            {
                var row = await costs.UpsertAsync(actionCode, dto.Credits, dto.Enabled, dto.Description,
                    http.User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
                return Results.Ok(row);
            }
            catch (ArgumentException ex)
            {
                return new ApiErrorResult(400, "credit_cost_invalid", ex.Message);
            }
        }).RequireRateLimiting("PerUserWrite");
        group.MapGet("/benchmark-runs", ListBenchmarkRunsAsync);
        group.MapPost("/benchmark-runs/run", RunBenchmarkAsync)
            .RequireRateLimiting("PerUserWrite");
        group.MapGet("/ledger-reconciliation", ListLedgerReconciliationAsync);
        group.MapGet("/vocabulary-duplicates", ListVocabularyDuplicatesAsync);
        group.MapPost("/speaking-duplicates/mark", MarkSpeakingDuplicatesAsync);
        group.MapGet("/budgets", ListBudgetsAsync);
        group.MapPost("/budgets/override", CreateBudgetOverrideAsync);
        group.MapGet("/circuits", ListCircuitsAsync);
        group.MapPost("/circuits/{key}/reset", ResetCircuitAsync);
        group.MapGet("/live-voice/health", GetLiveVoiceHealthAsync);
        group.MapGet("/live-voice/admission", GetLiveVoiceAdmissionAsync);
        group.MapPut("/live-voice/admission", UpdateLiveVoiceAdmissionAsync)
            .RequireRateLimiting("PerUserWrite");
        group.MapPost("/live-voice/{provider}/reset", ResetLiveVoiceProviderAsync);
        group.MapGet("/live-voice/voices", (LiveVoicePreviewService service) => Results.Ok(service.List()));
        group.MapPost("/live-voice/voices/preview-offer", CreateLiveVoicePreviewOfferAsync)
            .RequireRateLimiting("PerUserWrite");

        return app;
    }

    /// <summary>
    /// Live voice provider health: per provider the catalog probe, the breaker fed by real session
    /// creations, the last failure class and counters, plus the live-session admission gate (cap, admitted
    /// and queued, derived from the database so it covers every API slot and the worker). Never keys, URLs,
    /// tokens or provider messages. The provider view never depends on the admission tables: if they cannot
    /// be read, <c>admission</c> is null.
    /// </summary>
    private static async Task<IResult> GetLiveVoiceHealthAsync(
        LiveVoiceProviderProbeState state,
        IOptions<LiveVoiceOptions> options,
        SpeakingLiveAdmissionService admission,
        CancellationToken ct)
    {
        var snapshot = state.Snapshot(options.Value);
        SpeakingLiveAdmissionCounts? counts = null;
        try
        {
            counts = await admission.GetCountsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Left null on purpose (see above).
        }
        return Results.Ok(snapshot with { Admission = counts });
    }

    /// <summary>The live-session admission gate: effective cap, kill switch, and what is in it now.</summary>
    private static async Task<IResult> GetLiveVoiceAdmissionAsync(
        SpeakingLiveAdmissionService admission,
        CancellationToken ct)
        => Results.Ok(await admission.GetCountsAsync(ct));

    /// <summary>Sets the live-session cap (1..10000) and/or the kill switch (<c>enabled=false</c> lets everyone
    /// through, today's behaviour). Takes effect on the next admission decision, no restart. Audited.</summary>
    private static async Task<IResult> UpdateLiveVoiceAdmissionAsync(
        UpdateSpeakingLiveAdmissionRequest request,
        SpeakingLiveAdmissionService admission,
        LearnerDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        if (request is null || (request.Enabled is null && request.MaxConcurrent is null))
        {
            return new ApiErrorResult(400, "speaking_live_admission_empty", "Send enabled and/or maxConcurrent.");
        }

        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        // Saved together with the setting by UpdateSettingsAsync: a rejected value leaves no audit row.
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = http.User.FindFirstValue(ClaimTypes.Name) ?? actorId,
            Action = "SpeakingLiveAdmissionSettingsUpdated",
            ResourceType = "SpeakingLiveAdmission",
            ResourceId = SpeakingLiveAdmissionService.SettingsId,
            Details = JsonSerializer.Serialize(new { enabled = request.Enabled, maxConcurrent = request.MaxConcurrent }),
        });
        return Results.Ok(await admission.UpdateSettingsAsync(request.Enabled, request.MaxConcurrent, actorId, ct));
    }

    /// <summary>Relays an admin browser's WebRTC offer to a short GPT-Live preview session for one of the four voices.
    /// The provider key never leaves the server; the session is audited, not billed against any learner.</summary>
    private static async Task<IResult> CreateLiveVoicePreviewOfferAsync(
        LiveVoicePreviewOfferRequest request,
        LiveVoicePreviewService service,
        HttpContext http,
        CancellationToken ct)
    {
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var actorName = http.User.FindFirstValue(ClaimTypes.Name) ?? actorId;
        return Results.Ok(await service.CreateOfferAsync(actorId, actorName, request, ct));
    }

    private static async Task<IResult> ResetLiveVoiceProviderAsync(
        string provider,
        LiveVoiceProviderProbeState state,
        LearnerDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        var normalized = LiveVoiceOptions.NormalizeProvider(provider);
        if (normalized.Length == 0)
        {
            return new ApiErrorResult(400, "live_voice_provider_invalid", "provider must be openai or gemini.");
        }

        var wasOpen = state.Reset(normalized);
        var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = http.User.FindFirstValue(ClaimTypes.Name) ?? actorId,
            Action = "LiveVoiceProviderCircuitReset",
            ResourceType = "LiveVoiceProvider",
            ResourceId = normalized,
            Details = JsonSerializer.Serialize(new { wasOpen }),
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { provider = normalized, breaker = state.BreakerState(normalized), wasOpen });
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

    /// <summary>
    /// Executes the route benchmark corpus against a candidate provider/model through the real
    /// dispatch path and RECORDS the run (passed or failed) for the route-approval gate. Every
    /// metric is computed from the observed completions and the admin-configured token pricing of
    /// pricing; nothing is hand-entered. A refused or failed run is recorded as not-passed, so a
    /// route switch attempt citing it is refused by <c>AiProviderRouteApprovalService</c>.
    /// Audited: the run row itself plus an explicit audit event.
    /// </summary>
    private static async Task<IResult> RunBenchmarkAsync(
        RunRouteBenchmarkRequest request,
        IAiRouteBenchmarkRunner runner,
        LearnerDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.FeatureCode)
            || string.IsNullOrWhiteSpace(request.ProviderCode)
            || string.IsNullOrWhiteSpace(request.Model))
        {
            return new ApiErrorResult(400, "benchmark_request_incomplete", "featureCode, providerCode and model are required.");
        }

        AiRouteBenchmarkResult result;
        try
        {
            result = await runner.RunAsync(request.FeatureCode.Trim(), request.ProviderCode.Trim().ToLowerInvariant(), request.Model.Trim(), ct);
        }
        catch (InvalidOperationException ex)
        {
            return new ApiErrorResult(400, "benchmark_not_executable", ex.Message);
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system",
            ActorName = http.User.FindFirstValue(ClaimTypes.Name) ?? "admin",
            Action = "AiRouteBenchmarkExecuted",
            ResourceType = "AiProviderBenchmarkRun",
            ResourceId = result.Run.Id,
            Details = JsonSerializer.Serialize(new
            {
                featureCode = result.FeatureCode,
                providerCode = result.ProviderCode,
                model = result.Model,
                passed = result.Evaluation.Passed,
                failures = result.Evaluation.Failures,
            }),
        });
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            runId = result.Run.Id,
            passed = result.Evaluation.Passed,
            failures = result.Evaluation.Failures,
            metrics = result.Metrics,
            incumbent = new { provider = result.IncumbentProviderCode, model = result.IncumbentModel },
            cases = result.Cases,
        });
    }

    public sealed record RunRouteBenchmarkRequest(string FeatureCode, string ProviderCode, string Model);

    public sealed record AiCreditCostUpsertDto(int Credits, bool Enabled, string Description);

    private static async Task<IResult> ListLedgerReconciliationAsync(
        IAiLedgerReconciliationService recon,
        CancellationToken ct)
        => Results.Ok(await recon.BuildReportAsync(ct));

    private static async Task<IResult> ListVocabularyDuplicatesAsync(
        OetLearner.Api.Services.Reading.IVocabularyMergeReportService report,
        CancellationToken ct)
        => Results.Ok(await report.BuildReportAsync(ct));

    private static async Task<IResult> MarkSpeakingDuplicatesAsync(
        OetLearner.Api.Services.Speaking.ISpeakingDuplicateMarker marker,
        CancellationToken ct)
        => Results.Ok(new { marked = await marker.MarkDuplicatesAsync(ct) });

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
            return new ApiErrorResult(400, "ai_budget_override_invalid", "scope, amountUsd, and reason are required.");
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
            return new ApiErrorResult(400, "ai_budget_override_invalid", ApiErrorResult.SafeMessage(ex, "The budget override is invalid.")) { Exception = ex };
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
            return new ApiErrorResult(400, "ai_circuit_key_required", "key is required.");
        }

        var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedKind is not (AiCircuitBreakerStore.KindProvider or AiCircuitBreakerStore.KindCredential))
        {
            return new ApiErrorResult(400, "ai_circuit_kind_invalid", "kind must be provider or credential.");
        }

        await circuits.ResetAsync(normalizedKind, key, ct);
        return Results.Ok(new { kind = normalizedKind, key, state = AiCircuitBreakerStore.StateClosed });
    }
}
