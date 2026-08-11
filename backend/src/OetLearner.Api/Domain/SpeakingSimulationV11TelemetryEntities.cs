using System.ComponentModel.DataAnnotations;
using OetLearner.Api.Contracts;

namespace OetLearner.Api.Domain;

/// <summary>
/// Operational telemetry for one v1.1 conversation turn. This table contains
/// no learner audio or transcript text; the authoritative recording and
/// transcript remain in the existing SpeakingRecording/SpeakingTranscript
/// rows. It exists so p95 latency, provider provenance, cost, degradation,
/// and release identifiers remain queryable even before an assessment report
/// is generated.
/// </summary>
public sealed class SpeakingSimulationV11TurnTelemetry
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    [MaxLength(64)]
    public string? SourceTranscriptId { get; set; }

    public int TurnNumber { get; set; }

    [MaxLength(32)]
    public string Role { get; set; } = "candidate";

    [MaxLength(16)]
    public string Phase { get; set; } = "roleplay";

    [MaxLength(64)]
    public string? AsrProvider { get; set; }

    [MaxLength(128)]
    public string? AsrModel { get; set; }

    public int AsrLatencyMs { get; set; }

    [MaxLength(128)]
    public string? ActorProvider { get; set; }

    [MaxLength(128)]
    public string? ActorModel { get; set; }

    [MaxLength(64)]
    public string? ActorUsageRecordId { get; set; }

    public int ActorLatencyMs { get; set; }

    [MaxLength(128)]
    public string? TtsProvider { get; set; }

    [MaxLength(128)]
    public string? TtsModel { get; set; }

    public int TtsLatencyMs { get; set; }
    public int TotalLatencyMs { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int RetryCount { get; set; }
    public decimal EstimatedCostUsd { get; set; }

    [MaxLength(32)]
    public string ConcurrencyBucket { get; set; } = "unknown";

    [MaxLength(32)]
    public string DegradationState { get; set; } = "normal";

    [MaxLength(64)]
    public string SpecVersion { get; set; } = SpeakingSimulationV11Contracts.SpecVersion;

    [MaxLength(64)]
    public string RubricVersion { get; set; } = SpeakingSimulationV11Contracts.RubricVersion;

    [MaxLength(64)]
    public string CalibrationVersion { get; set; } = SpeakingSimulationV11Contracts.CalibrationVersion;

    [MaxLength(64)]
    public string? BudgetBreachCode { get; set; }

    [MaxLength(64)]
    public string? TechnicalReviewCode { get; set; }

    public bool TechnicalReviewRequired { get; set; }

    public string CostComponentsJson { get; set; } = "{}";

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
