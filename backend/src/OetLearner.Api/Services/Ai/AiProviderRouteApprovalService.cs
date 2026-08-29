using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

public sealed class AiProviderRouteRefusedException(string message) : InvalidOperationException(message);

public sealed record AiBenchmarkMetrics(
    decimal SchemaValidityPct,
    decimal CitationCompliancePct,
    int ScoringGovernanceViolations,
    int PassFailFlips,
    decimal CriterionWithinOnePct,
    decimal MeanScaledAbsError,
    decimal EvidenceGroundingPct,
    int FabricatedSourceClaims,
    decimal CostReductionPct);

public sealed record AiBenchmarkEvaluation(bool Passed, IReadOnlyList<string> Failures);

public sealed record AiRouteSwitchRequest(
    string FeatureCode,
    string ProviderCode,
    string? Model,
    string? BenchmarkRunId,
    string? ExistingRouteId,
    string? ExistingProviderCode,
    string? ExistingModel);

public interface IAiProviderRouteApprovalService
{
    AiBenchmarkClass Classify(string featureCode);
    AiBenchmarkEvaluation Evaluate(AiBenchmarkClass @class, AiBenchmarkMetrics metrics);
    bool IsClaudeRoute(string providerCode, string? model);
    Task<AiProviderBenchmarkRun> RecordRunAsync(
        string featureCode,
        string providerCode,
        string model,
        string corpusVersion,
        AiBenchmarkMetrics metrics,
        CancellationToken ct);
    Task EnsureSwitchAllowedAsync(AiRouteSwitchRequest request, CancellationToken ct);
}

public sealed class AiProviderRouteApprovalService(LearnerDbContext db) : IAiProviderRouteApprovalService
{
    private static readonly HashSet<string> ScoringCritical = new(StringComparer.OrdinalIgnoreCase)
    {
        AiFeatureCodes.WritingGrade,
        AiFeatureCodes.WritingSampleScore,
        AiFeatureCodes.SpeakingGrade,
        AiFeatureCodes.MockFullGrade,
        AiFeatureCodes.WritingDrillGradeV1,
        AiFeatureCodes.ListeningPartAScore,
        AiFeatureCodes.PronunciationScore,
        AiFeatureCodes.PronunciationLinguisticScore,
        AiFeatureCodes.ConversationEvaluation,
        SpeakingAiFeatureCodes.SpeakingScoreV2,
    };

    public AiBenchmarkClass Classify(string featureCode)
        => ScoringCritical.Contains(featureCode)
            ? AiBenchmarkClass.ScoringCritical
            : AiBenchmarkClass.NonScoring;

    public bool IsClaudeRoute(string providerCode, string? model)
    {
        if (string.Equals(providerCode, "anthropic", StringComparison.OrdinalIgnoreCase))
            return true;
        if (providerCode.Contains("claude", StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrWhiteSpace(model)
            && model.Contains("claude", StringComparison.OrdinalIgnoreCase);
    }

    public AiBenchmarkEvaluation Evaluate(AiBenchmarkClass @class, AiBenchmarkMetrics metrics)
    {
        var failures = new List<string>();
        if (@class == AiBenchmarkClass.ScoringCritical)
        {
            if (metrics.SchemaValidityPct < 100m) failures.Add("schema_validity");
            if (metrics.CitationCompliancePct < 100m) failures.Add("rulebook_citations");
            if (metrics.ScoringGovernanceViolations != 0) failures.Add("scoring_governance");
            if (metrics.PassFailFlips != 0) failures.Add("pass_fail_flips");
            if (metrics.CriterionWithinOnePct < 95m) failures.Add("criterion_within_one");
            if (metrics.MeanScaledAbsError > 20m) failures.Add("mean_scaled_abs_error");
        }
        else
        {
            if (metrics.SchemaValidityPct < 98m) failures.Add("schema_validity");
            if (metrics.EvidenceGroundingPct < 95m) failures.Add("evidence_grounding");
            if (metrics.FabricatedSourceClaims != 0) failures.Add("fabricated_source_claims");
            if (metrics.CostReductionPct < 30m) failures.Add("cost_reduction");
        }

        return new AiBenchmarkEvaluation(failures.Count == 0, failures);
    }

    public async Task<AiProviderBenchmarkRun> RecordRunAsync(
        string featureCode,
        string providerCode,
        string model,
        string corpusVersion,
        AiBenchmarkMetrics metrics,
        CancellationToken ct)
    {
        var @class = Classify(featureCode);
        var evaluation = Evaluate(@class, metrics);
        var run = new AiProviderBenchmarkRun
        {
            Id = Guid.NewGuid().ToString("N"),
            FeatureCode = featureCode,
            ProviderCode = providerCode.Trim().ToLowerInvariant(),
            Model = model,
            CorpusVersion = corpusVersion,
            Class = @class,
            SchemaValidityPct = metrics.SchemaValidityPct,
            CitationCompliancePct = metrics.CitationCompliancePct,
            ScoringGovernanceViolations = metrics.ScoringGovernanceViolations,
            PassFailFlips = metrics.PassFailFlips,
            CriterionWithinOnePct = metrics.CriterionWithinOnePct,
            MeanScaledAbsError = metrics.MeanScaledAbsError,
            EvidenceGroundingPct = metrics.EvidenceGroundingPct,
            FabricatedSourceClaims = metrics.FabricatedSourceClaims,
            CostReductionPct = metrics.CostReductionPct,
            Passed = evaluation.Passed,
            RecordedAt = DateTimeOffset.UtcNow,
            ReportJson = JsonSerializer.Serialize(new { passed = evaluation.Passed, failures = evaluation.Failures }),
        };
        db.AiProviderBenchmarkRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task EnsureSwitchAllowedAsync(AiRouteSwitchRequest request, CancellationToken ct)
    {
        if (IsClaudeRoute(request.ProviderCode, request.Model))
            return;

        if (string.IsNullOrWhiteSpace(request.BenchmarkRunId))
        {
            throw new AiProviderRouteRefusedException(
                "No route may leave Claude without a recorded passing benchmark run.");
        }

        var run = await db.AiProviderBenchmarkRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.BenchmarkRunId, ct);
        if (run is null || !run.Passed)
        {
            throw new AiProviderRouteRefusedException(
                "If nothing passes, Claude remains the route.");
        }

        if (!string.Equals(run.FeatureCode, request.FeatureCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(run.ProviderCode, request.ProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new AiProviderRouteRefusedException(
                "The benchmark run does not match this feature and provider.");
        }

        if (!string.IsNullOrWhiteSpace(request.Model)
            && !string.Equals(run.Model, request.Model, StringComparison.OrdinalIgnoreCase))
        {
            throw new AiProviderRouteRefusedException(
                "The benchmark run does not match this model.");
        }
    }
}
