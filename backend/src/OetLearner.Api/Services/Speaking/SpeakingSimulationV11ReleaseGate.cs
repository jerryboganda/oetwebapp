using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

public sealed class SpeakingSimulationV11ReleaseGate(LearnerDbContext db)
{
    public async Task<SpeakingSimulationV11GateResult> EvaluateAsync(string professionId, CancellationToken ct)
    {
        var blockingReasons = new List<string>();

        var specRelease = await db.SpeakingSimulationV11SpecReleases
            .AsNoTracking()
            .Where(x => x.Status == SpeakingSimulationV11ReleaseStatus.Approved)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var rubricRelease = await db.SpeakingSimulationV11RubricReleases
            .AsNoTracking()
            .Where(x => x.Status == SpeakingSimulationV11ReleaseStatus.Approved)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (specRelease is null)
        {
            blockingReasons.Add("spec_release_required");
        }

        if (rubricRelease is null)
        {
            blockingReasons.Add("rubric_release_required");
        }

        var specVersion = specRelease?.SpecVersion ?? SpeakingSimulationV11Contracts.SpecVersion;
        var rubricVersion = rubricRelease?.RubricVersion ?? SpeakingSimulationV11Contracts.RubricVersion;

        if (!await HasApprovedFlagAsync("calibration_approval", "global", specVersion, rubricVersion, ct))
        {
            blockingReasons.Add("calibration_approval_required");
        }

        if (!await HasPositiveApprovedValueAsync("concurrency_budget", "global", specVersion, rubricVersion, ct))
        {
            blockingReasons.Add("concurrency_budget_required");
        }

        if (!await HasPositiveApprovedValueAsync("cost_ceiling", "global", specVersion, rubricVersion, ct))
        {
            blockingReasons.Add("cost_ceiling_required");
        }

        if (!await HasApprovedFlagAsync("retention_approval", "global", specVersion, rubricVersion, ct))
        {
            blockingReasons.Add("retention_approval_required");
        }

        if (!await HasApprovedFlagAsync("graph_approval", "global", specVersion, rubricVersion, ct))
        {
            blockingReasons.Add("graph_approval_required");
        }

        if (!await HasApprovedFlagAsync("profession_pack_approval", professionId, specVersion, rubricVersion, ct))
        {
            blockingReasons.Add("profession_pack_approval_required");
        }

        var enabledRuleIds = SpeakingSimulationV11Contracts.RubricCriteria.Criteria
            .SelectMany(x => x.EnabledRuleIds)
            .Where(ruleId => !string.Equals(ruleId, "rule55", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SpeakingSimulationV11GateResult(
            IsReleased: blockingReasons.Count == 0,
            BlockingReasons: blockingReasons,
            EnabledRuleIds: enabledRuleIds,
            SpecVersion: specVersion,
            RubricVersion: rubricVersion);
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

    private Task<bool> HasPositiveApprovedValueAsync(
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
                && x.Status == SpeakingSimulationV11ApprovalStatus.Approved
                && x.NumericValue.HasValue
                && x.NumericValue.Value > 0,
                ct);
}
