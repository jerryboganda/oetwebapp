using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Owns the operational guard around a v1.1 AI turn. The limiter is process
/// local because SignalR turns are process-local work; the persisted rows are
/// the cross-instance audit/analytics source. A turn that exceeds the
/// approved concurrency, latency, or cost budget is never silently scored.
/// </summary>
public sealed class SpeakingSimulationV11TurnTelemetryService(
    LearnerDbContext db,
    SpeakingSimulationV11ReleaseGate releaseGate,
    ILogger<SpeakingSimulationV11TurnTelemetryService> logger)
{
    internal static int ActiveTurnCount;

    public async Task<SpeakingSimulationV11TurnLease?> TryAcquireAsync(
        string speakingSessionId,
        string professionId,
        string phase,
        CancellationToken ct)
    {
        var gate = await releaseGate.EvaluateAsync(professionId, ct);
        if (!gate.IsReleased || string.IsNullOrWhiteSpace(gate.AudioAssessmentProvider))
        {
            return null;
        }

        var budget = await releaseGate.GetOperationalBudgetAsync(
            gate.SpecVersion, gate.RubricVersion, ct);
        if (budget is null)
        {
            return null;
        }

        var active = Interlocked.Increment(ref ActiveTurnCount);
        var bucket = string.Concat(active, "/", budget.ConcurrencyLimit);
        if (active > budget.ConcurrencyLimit)
        {
            Interlocked.Decrement(ref ActiveTurnCount);
            await RecordAsync(
                speakingSessionId,
                new SpeakingSimulationV11TurnTelemetryInput(
                    TurnNumber: 0,
                    Role: "system",
                    Phase: phase,
                    SourceTranscriptId: null,
                    AsrProvider: null,
                    AsrModel: null,
                    AsrLatencyMs: 0,
                    ActorProvider: null,
                    ActorModel: null,
                    ActorUsageRecordId: null,
                    ActorLatencyMs: 0,
                    TtsProvider: null,
                    TtsModel: null,
                    TtsLatencyMs: 0,
                    TotalLatencyMs: 0,
                    InputTokens: 0,
                    OutputTokens: 0,
                    RetryCount: 0,
                    EstimatedCostUsd: 0m,
                    ConcurrencyBucket: bucket,
                    DegradationState: "concurrency_limited",
                    BudgetBreachCode: "concurrency_limit_exceeded",
                    TechnicalReviewCode: "concurrency_limit_exceeded",
                    TechnicalReviewRequired: true,
                    CostComponentsJson: "{}",
                    StartedAt: DateTimeOffset.UtcNow,
                    CompletedAt: DateTimeOffset.UtcNow),
                gate,
                ct);
            return null;
        }

        return new SpeakingSimulationV11TurnLease(
            speakingSessionId,
            phase,
            gate,
            budget,
            active,
            bucket,
            DateTimeOffset.UtcNow);
    }

    public async Task RecordAsync(
        SpeakingSimulationV11TurnLease lease,
        SpeakingSimulationV11TurnTelemetryInput input,
        CancellationToken ct)
    {
        try
        {
            await RecordAsync(lease.SpeakingSessionId, input, lease.Gate, ct);
        }
        finally
        {
            lease.Release();
        }
    }

    public async Task<bool> HasTechnicalReviewBreachAsync(
        string speakingSessionId,
        CancellationToken ct)
        => await db.SpeakingSimulationV11TurnTelemetryRows
            .AsNoTracking()
            .AnyAsync(x => x.SpeakingSessionId == speakingSessionId
                && x.TechnicalReviewRequired, ct);

    private async Task RecordAsync(
        string speakingSessionId,
        SpeakingSimulationV11TurnTelemetryInput input,
        SpeakingSimulationV11GateResult gate,
        CancellationToken ct)
    {
        var budget = await releaseGate.GetOperationalBudgetAsync(
            gate.SpecVersion, gate.RubricVersion, ct);
        var totalLatencyMs = Math.Max(0, input.TotalLatencyMs);
        var breach = input.BudgetBreachCode;
        if (budget is not null)
        {
            if (totalLatencyMs > budget.LatencySlaMs)
            {
                breach ??= "latency_sla_exceeded";
            }
            if (input.EstimatedCostUsd > budget.CostCeilingUsd)
            {
                breach ??= "cost_ceiling_exceeded";
            }
            var priorCost = await db.SpeakingSimulationV11TurnTelemetryRows
                .AsNoTracking()
                .Where(x => x.SpeakingSessionId == speakingSessionId)
                .Select(x => (decimal?)x.EstimatedCostUsd)
                .SumAsync(ct) ?? 0m;
            if (priorCost + input.EstimatedCostUsd > budget.CostCeilingUsd)
            {
                breach ??= "cost_ceiling_exceeded";
            }
        }

        var reviewRequired = input.TechnicalReviewRequired || breach is not null;
        var degradation = reviewRequired && string.Equals(input.DegradationState, "normal", StringComparison.Ordinal)
            ? "budget_breach"
            : input.DegradationState;
        if (reviewRequired)
        {
            logger.LogWarning(
                "Speaking v1.1 operational review required for session {SessionId}: code={Code}, degradation={Degradation}, costUsd={CostUsd}, latencyMs={LatencyMs}.",
                speakingSessionId,
                input.TechnicalReviewCode ?? breach ?? "technical_review",
                degradation,
                input.EstimatedCostUsd,
                totalLatencyMs);
        }
        var now = DateTimeOffset.UtcNow;
        db.SpeakingSimulationV11TurnTelemetryRows.Add(new SpeakingSimulationV11TurnTelemetry
        {
            Id = "spv11_telemetry_" + Guid.NewGuid().ToString("N"),
            SpeakingSessionId = speakingSessionId,
            SourceTranscriptId = input.SourceTranscriptId,
            TurnNumber = input.TurnNumber,
            Role = input.Role,
            Phase = input.Phase,
            AsrProvider = input.AsrProvider,
            AsrModel = input.AsrModel,
            AsrLatencyMs = Math.Max(0, input.AsrLatencyMs),
            ActorProvider = input.ActorProvider,
            ActorModel = input.ActorModel,
            ActorUsageRecordId = input.ActorUsageRecordId,
            ActorLatencyMs = Math.Max(0, input.ActorLatencyMs),
            TtsProvider = input.TtsProvider,
            TtsModel = input.TtsModel,
            TtsLatencyMs = Math.Max(0, input.TtsLatencyMs),
            TotalLatencyMs = totalLatencyMs,
            InputTokens = Math.Max(0, input.InputTokens),
            OutputTokens = Math.Max(0, input.OutputTokens),
            RetryCount = Math.Max(0, input.RetryCount),
            EstimatedCostUsd = Math.Max(0m, input.EstimatedCostUsd),
            ConcurrencyBucket = input.ConcurrencyBucket,
            DegradationState = degradation,
            SpecVersion = gate.SpecVersion,
            RubricVersion = gate.RubricVersion,
            CalibrationVersion = gate.CalibrationVersion,
            BudgetBreachCode = breach,
            TechnicalReviewCode = input.TechnicalReviewCode ?? breach,
            TechnicalReviewRequired = reviewRequired,
            CostComponentsJson = input.CostComponentsJson,
            StartedAt = input.StartedAt,
            CompletedAt = input.CompletedAt,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not persist Speaking v1.1 turn telemetry for session {SessionId}.",
                speakingSessionId);
            throw;
        }
    }
}

public sealed class SpeakingSimulationV11TurnLease(
    string speakingSessionId,
    string phase,
    SpeakingSimulationV11GateResult gate,
    SpeakingSimulationV11OperationalBudget budget,
    int activeCount,
    string concurrencyBucket,
    DateTimeOffset startedAt) : IDisposable
{
    private int _released;

    public string SpeakingSessionId { get; } = speakingSessionId;
    public string Phase { get; } = phase;
    public SpeakingSimulationV11GateResult Gate { get; } = gate;
    public SpeakingSimulationV11OperationalBudget Budget { get; } = budget;
    public int ActiveCount { get; } = activeCount;
    public string ConcurrencyBucket { get; } = concurrencyBucket;
    public DateTimeOffset StartedAt { get; } = startedAt;

    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            Interlocked.Decrement(ref SpeakingSimulationV11TurnTelemetryService.ActiveTurnCount);
        }
    }

    public void Dispose() => Release();
}

public sealed record SpeakingSimulationV11TurnTelemetryInput(
    int TurnNumber,
    string Role,
    string Phase,
    string? SourceTranscriptId,
    string? AsrProvider,
    string? AsrModel,
    int AsrLatencyMs,
    string? ActorProvider,
    string? ActorModel,
    string? ActorUsageRecordId,
    int ActorLatencyMs,
    string? TtsProvider,
    string? TtsModel,
    int TtsLatencyMs,
    int TotalLatencyMs,
    int InputTokens,
    int OutputTokens,
    int RetryCount,
    decimal EstimatedCostUsd,
    string ConcurrencyBucket,
    string DegradationState,
    string? BudgetBreachCode,
    string? TechnicalReviewCode,
    bool TechnicalReviewRequired,
    string CostComponentsJson,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
