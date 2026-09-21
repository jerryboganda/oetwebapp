using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

public sealed class SpeakingSimulationV11ReleaseGate(LearnerDbContext db)
{
    public async Task<SpeakingSimulationV11OperationalBudget?> GetOperationalBudgetAsync(
        string specVersion,
        string rubricVersion,
        CancellationToken ct)
    {
        var concurrency = await GetPositiveApprovedValueAsync(
            "concurrency_budget", "global", specVersion, rubricVersion, ct);
        var cost = await GetPositiveApprovedValueAsync(
            "cost_ceiling", "global", specVersion, rubricVersion, ct);
        var latency = await GetPositiveApprovedValueAsync(
            "latency_sla_ms", "global", specVersion, rubricVersion, ct);
        var retention = await GetPositiveApprovedValueAsync(
            "retention_days", "global", specVersion, rubricVersion, ct);
        var sttCostPerMinute = await GetPositiveApprovedValueAsync(
            "stt_cost_per_minute", "global", specVersion, rubricVersion, ct);
        var ttsCostPerThousandCharacters = await GetPositiveApprovedValueAsync(
            "tts_cost_per_1000_characters", "global", specVersion, rubricVersion, ct);

        if (concurrency is not > 0 || cost is not > 0 || latency is not > 0 || retention is not > 0
            || sttCostPerMinute is not > 0 || ttsCostPerThousandCharacters is not > 0)
        {
            return null;
        }

        return new SpeakingSimulationV11OperationalBudget(
            ConcurrencyLimit: (int)Math.Clamp(concurrency.Value, 1m, int.MaxValue),
            CostCeilingUsd: cost.Value,
            LatencySlaMs: (int)Math.Clamp(latency.Value, 1m, int.MaxValue),
            RetentionDays: (int)Math.Clamp(retention.Value, 1m, int.MaxValue),
            SttCostPerMinuteUsd: sttCostPerMinute.Value,
            TtsCostPerThousandCharactersUsd: ttsCostPerThousandCharacters.Value);
    }

    public async Task<int> GetSilencePromptThresholdMsAsync(CancellationToken ct)
    {
        var value = await db.SpeakingSimulationV11OwnerApprovals
            .AsNoTracking()
            .Where(x => x.ApprovalKey == "silence_prompt_threshold_ms"
                && x.ScopeKey == "global"
                && x.SpecVersion == SpeakingSimulationV11Contracts.SpecVersion
                && x.RubricVersion == SpeakingSimulationV11Contracts.RubricVersion
                && x.Status == SpeakingSimulationV11ApprovalStatus.Approved
                && x.NumericValue.HasValue
                && x.NumericValue.Value > 0)
            .OrderByDescending(x => x.ApprovedAt ?? x.UpdatedAt)
            .Select(x => x.NumericValue)
            .FirstOrDefaultAsync(ct);

        return value is > 0
            ? (int)Math.Min((decimal)int.MaxValue, value.Value)
            : SpeakingSimulationV11Contracts.DefaultSilencePromptThresholdMs;
    }

    public async Task<SpeakingSimulationV11GateResult> EvaluateAsync(string professionId, CancellationToken ct)
    {
        var blockingReasons = new List<string>();

        var rubricRelease = await db.SpeakingSimulationV11RubricReleases
            .AsNoTracking()
            .Where(x => x.Status == SpeakingSimulationV11ReleaseStatus.Approved
                && x.RubricVersion == SpeakingSimulationV11Contracts.RubricVersion)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var specVersion = SpeakingSimulationV11Contracts.SpecVersion;
        var rubricVersion = SpeakingSimulationV11Contracts.RubricVersion;
        IReadOnlyList<SpeakingSimulationV11RubricCriterion> rubricCriteria =
            SpeakingSimulationV11Contracts.RubricCriteria.Criteria;
        var calibrationVersion = SpeakingSimulationV11Contracts.CalibrationVersion;
        if (rubricRelease is not null
            && string.Equals(
                rubricRelease.CalibrationVersion,
                SpeakingSimulationV11Contracts.CalibrationVersion,
                StringComparison.Ordinal))
        {
            try
            {
                var configured = JsonSerializer.Deserialize<SpeakingSimulationV11RubricCriterion[]>(
                    rubricRelease.CriteriaJson);
                if (SpeakingSimulationV11Contracts.IsValidRubric(configured))
                {
                    rubricCriteria = configured!;
                    calibrationVersion = rubricRelease.CalibrationVersion;
                }
            }
            catch (JsonException)
            {
                // Governance validation owns malformed release definitions.
                // Candidate visibility remains opt-out and falls back to the
                // shipped contract until a valid configured rubric exists.
            }
        }

        var normalizedProfessionId = string.IsNullOrWhiteSpace(professionId)
            ? string.Empty
            : professionId.Trim().ToLowerInvariant();
        var candidateReleaseBlocked = await db.SpeakingSimulationV11OwnerApprovals
            .AsNoTracking()
            .AnyAsync(x => x.ApprovalKey == "candidate_release_block"
                && (x.ScopeKey == "global" || x.ScopeKey == normalizedProfessionId)
                && x.SpecVersion == specVersion
                && x.RubricVersion == rubricVersion
                && x.Status == SpeakingSimulationV11ApprovalStatus.Approved,
                ct);
        if (candidateReleaseBlocked)
            blockingReasons.Add("candidate_release_blocked");

        // Operational controls are intentionally resolved independently from
        // candidate visibility. Missing values fall back or are handled by the
        // runtime budget/provider guards at the point where they are needed.
        var silencePromptThreshold = await GetPositiveApprovedValueAsync(
            "silence_prompt_threshold_ms", "global", specVersion, rubricVersion, ct);
        var audioAssessmentProvider = await GetApprovedAudioAssessmentProviderAsync(
            specVersion, rubricVersion, ct);

        var enabledRuleIds = rubricCriteria
            .SelectMany(x => x.EnabledRuleIds)
            .Where(ruleId => !string.Equals(ruleId, "rule55", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ruleId, "R55", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SpeakingSimulationV11GateResult(
            IsReleased: blockingReasons.Count == 0,
            BlockingReasons: blockingReasons,
            EnabledRuleIds: enabledRuleIds,
            SpecVersion: specVersion,
            RubricVersion: rubricVersion,
            RubricCriteria: rubricCriteria,
            SilencePromptThresholdMs: silencePromptThreshold is > 0
                ? (int)Math.Min((decimal)int.MaxValue, silencePromptThreshold.Value)
                : SpeakingSimulationV11Contracts.DefaultSilencePromptThresholdMs,
            AudioAssessmentProvider: audioAssessmentProvider,
            CalibrationVersion: calibrationVersion);
    }

    private Task<bool> HasApprovedFlagAsync(
        string approvalKey,
        string scopeKey,
        string specVersion,
        string rubricVersion,
        CancellationToken ct)
        => db.SpeakingSimulationV11OwnerApprovals
            .AsNoTracking()
            .AnyAsync(x =>
                x.ApprovalKey == approvalKey
                && x.ScopeKey == scopeKey
                && x.SpecVersion == specVersion
                && x.RubricVersion == rubricVersion
                && x.Status == SpeakingSimulationV11ApprovalStatus.Approved,
                ct);

    private async Task<bool> HasPositiveApprovedValueAsync(
        string approvalKey,
        string scopeKey,
        string specVersion,
        string rubricVersion,
        CancellationToken ct)
    {
        var value = await GetPositiveApprovedValueAsync(
            approvalKey, scopeKey, specVersion, rubricVersion, ct);
        return value is > 0;
    }

    private Task<decimal?> GetPositiveApprovedValueAsync(
        string approvalKey,
        string scopeKey,
        string specVersion,
        string rubricVersion,
        CancellationToken ct)
        => db.SpeakingSimulationV11OwnerApprovals
            .AsNoTracking()
            .Where(x =>
                x.ApprovalKey == approvalKey
                && x.ScopeKey == scopeKey
                && x.SpecVersion == specVersion
                && x.RubricVersion == rubricVersion
                && x.Status == SpeakingSimulationV11ApprovalStatus.Approved
                && x.NumericValue.HasValue
                && x.NumericValue.Value > 0)
            .OrderByDescending(x => x.ApprovedAt ?? x.UpdatedAt)
            .Select(x => x.NumericValue)
            .FirstOrDefaultAsync(ct);

    private async Task<string?> GetApprovedAudioAssessmentProviderAsync(
        string specVersion,
        string rubricVersion,
        CancellationToken ct)
    {
        var evidenceJson = await db.SpeakingSimulationV11OwnerApprovals
            .AsNoTracking()
            .Where(x => x.ApprovalKey == "audio_assessment_approval"
                && x.ScopeKey == "global"
                && x.SpecVersion == specVersion
                && x.RubricVersion == rubricVersion
                && x.Status == SpeakingSimulationV11ApprovalStatus.Approved
                && x.EvidenceJson != null)
            .OrderByDescending(x => x.ApprovedAt ?? x.UpdatedAt)
            .Select(x => x.EvidenceJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(evidenceJson)) return null;

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (document.RootElement.TryGetProperty("provider", out var provider)
                && provider.ValueKind == JsonValueKind.String)
            {
                var value = provider.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            if (document.RootElement.TryGetProperty("approvedProvider", out var approvedProvider)
                && approvedProvider.ValueKind == JsonValueKind.String)
            {
                var value = approvedProvider.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            // The governance endpoint rejects malformed JSON before an
            // approval can be created; this keeps direct/test inserts closed.
        }

        return null;
    }
}

public sealed record SpeakingSimulationV11OperationalBudget(
    int ConcurrencyLimit,
    decimal CostCeilingUsd,
    int LatencySlaMs,
    int RetentionDays,
    decimal SttCostPerMinuteUsd,
    decimal TtsCostPerThousandCharactersUsd);
