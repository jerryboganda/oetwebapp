using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingSimulationV11ReleaseGateTests
{
    [Fact]
    public void Rubric_contract_requires_exact_order_weights_anchors_and_excludes_rule55()
    {
        var exact = SpeakingSimulationV11Contracts.RubricCriteria.Criteria;
        Assert.True(SpeakingSimulationV11Contracts.IsValidRubric(exact));

        var wrongWeight = exact
            .Select((item, index) => index == 0 ? item with { Weight = item.Weight + 1 } : item)
            .ToArray();
        Assert.False(SpeakingSimulationV11Contracts.IsValidRubric(wrongWeight));

        var wrongOrder = exact.Reverse().ToArray();
        Assert.False(SpeakingSimulationV11Contracts.IsValidRubric(wrongOrder));

        var rule55 = exact
            .Select((item, index) => index == 0 ? item with { EnabledRuleIds = ["R55"] } : item)
            .ToArray();
        Assert.False(SpeakingSimulationV11Contracts.IsValidRubric(rule55));
    }

    [Fact]
    public async Task EvaluateAsync_blocks_release_until_all_owner_gates_are_approved()
    {
        await using var fixture = await SimulationV11Fixture.CreateAsync();

        var result = await fixture.Gate.EvaluateAsync("medicine", CancellationToken.None);

        Assert.False(result.IsReleased);
        Assert.Contains("calibration_approval_required", result.BlockingReasons);
        Assert.Contains("concurrency_budget_required", result.BlockingReasons);
        Assert.Contains("cost_ceiling_required", result.BlockingReasons);
        Assert.Contains("latency_sla_required", result.BlockingReasons);
        Assert.Contains("retention_days_required", result.BlockingReasons);
        Assert.Contains("stt_cost_per_minute_required", result.BlockingReasons);
        Assert.Contains("tts_cost_per_1000_characters_required", result.BlockingReasons);
        Assert.Contains("retention_approval_required", result.BlockingReasons);
        Assert.Contains("silence_prompt_threshold_required", result.BlockingReasons);
        Assert.Contains("silence_prompt_approval_required", result.BlockingReasons);
        Assert.Contains("graph_approval_required", result.BlockingReasons);
        Assert.Contains("profession_pack_approval_required", result.BlockingReasons);
        Assert.Contains("audio_assessment_approval_required", result.BlockingReasons);
        Assert.Contains("audio_assessment_provider_required", result.BlockingReasons);
        Assert.DoesNotContain("rule55", result.EnabledRuleIds);
    }

    [Fact]
    public async Task EvaluateAsync_releases_when_all_owner_gates_are_approved_with_positive_values()
    {
        await using var fixture = await SimulationV11Fixture.CreateAsync(approveAll: true);

        var result = await fixture.Gate.EvaluateAsync("medicine", CancellationToken.None);

        Assert.True(result.IsReleased);
        Assert.Empty(result.BlockingReasons);
        Assert.Equal(SpeakingSimulationV11Contracts.SpecVersion, result.SpecVersion);
        Assert.Equal(SpeakingSimulationV11Contracts.RubricVersion, result.RubricVersion);
        Assert.NotEmpty(result.EnabledRuleIds);
        Assert.DoesNotContain("rule55", result.EnabledRuleIds);

        var budget = await fixture.Gate.GetOperationalBudgetAsync(
            result.SpecVersion, result.RubricVersion, CancellationToken.None);
        Assert.NotNull(budget);
        Assert.Equal(0.01m, budget!.SttCostPerMinuteUsd);
        Assert.Equal(0.03m, budget.TtsCostPerThousandCharactersUsd);
    }

    private sealed class SimulationV11Fixture(LearnerDbContext db) : IAsyncDisposable
    {
        public LearnerDbContext Db { get; } = db;
        public SpeakingSimulationV11ReleaseGate Gate { get; } = new(db);

        public static async Task<SimulationV11Fixture> CreateAsync(bool approveAll = false)
        {
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"speaking-simulation-v11-{Guid.NewGuid():N}")
                .Options;

            var db = new LearnerDbContext(options);

            db.SpeakingSimulationV11SpecReleases.Add(new SpeakingSimulationV11SpecRelease
            {
                Id = "spec-v1-1",
                SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
                ReleaseVersion = "2026-08-11",
                Status = SpeakingSimulationV11ReleaseStatus.Approved,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            db.SpeakingSimulationV11RubricReleases.Add(new SpeakingSimulationV11RubricRelease
            {
                Id = "rubric-v1-1",
                RubricVersion = SpeakingSimulationV11Contracts.RubricVersion,
                CalibrationVersion = SpeakingSimulationV11Contracts.CalibrationVersion,
                Status = SpeakingSimulationV11ReleaseStatus.Approved,
                CriteriaJson = JsonSerializer.Serialize(SpeakingSimulationV11Contracts.RubricCriteria.Criteria),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            if (approveAll)
            {
                db.SpeakingSimulationV11OwnerApprovals.AddRange(
                    BuildApproval("calibration_approval", "global"),
                    BuildApproval("concurrency_budget", "global", numericValue: 8m),
                    BuildApproval("cost_ceiling", "global", numericValue: 25m),
                    BuildApproval("latency_sla_ms", "global", numericValue: 2500m),
                    BuildApproval("retention_days", "global", numericValue: 30m),
                    BuildApproval("stt_cost_per_minute", "global", numericValue: 0.01m),
                    BuildApproval("tts_cost_per_1000_characters", "global", numericValue: 0.03m),
                    BuildApproval("silence_prompt_threshold_ms", "global", numericValue: 12000m),
                    BuildApproval("retention_approval", "global"),
                    BuildApproval("silence_prompt_approval", "global"),
                    BuildApproval("graph_approval", "global"),
                    BuildApproval("profession_pack_approval", "medicine"),
                    BuildApproval("audio_assessment_approval", "global",
                        evidenceJson: "{\"provider\":\"azure-phoneme\"}"));
            }

            await db.SaveChangesAsync();
            return new SimulationV11Fixture(db);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static SpeakingSimulationV11OwnerApproval BuildApproval(
            string approvalKey,
            string scopeKey,
            decimal? numericValue = null,
            string? evidenceJson = null)
            => new()
            {
                Id = $"{approvalKey}-{scopeKey}",
                ApprovalKey = approvalKey,
                ScopeKey = scopeKey,
                SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
                RubricVersion = SpeakingSimulationV11Contracts.RubricVersion,
                Status = SpeakingSimulationV11ApprovalStatus.Approved,
                NumericValue = numericValue,
                EvidenceJson = evidenceJson ?? "{}",
                ApprovedByUserId = "system-admin",
                ApprovedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
    }
}
