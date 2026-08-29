using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public enum AiBenchmarkClass
{
    ScoringCritical = 0,
    NonScoring = 1,
}

/// <summary>
/// W10 — frozen-corpus benchmark run that gates a provider route switch.
/// A route may leave Claude only when a matching row has <see cref="Passed"/>.
/// </summary>
public sealed class AiProviderBenchmarkRun
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    [MaxLength(64)]
    public string ProviderCode { get; set; } = default!;

    [MaxLength(128)]
    public string Model { get; set; } = default!;

    [MaxLength(64)]
    public string CorpusVersion { get; set; } = default!;

    public AiBenchmarkClass Class { get; set; }

    public decimal SchemaValidityPct { get; set; }
    public decimal CitationCompliancePct { get; set; }
    public int ScoringGovernanceViolations { get; set; }
    public int PassFailFlips { get; set; }
    public decimal CriterionWithinOnePct { get; set; }
    public decimal MeanScaledAbsError { get; set; }
    public decimal EvidenceGroundingPct { get; set; }
    public int FabricatedSourceClaims { get; set; }
    public decimal CostReductionPct { get; set; }

    public bool Passed { get; set; }

    [MaxLength(64)]
    public string? RollbackTargetRouteId { get; set; }

    [MaxLength(64)]
    public string? RollbackProviderCode { get; set; }

    [MaxLength(128)]
    public string? RollbackModel { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public string ReportJson { get; set; } = "{}";
}
