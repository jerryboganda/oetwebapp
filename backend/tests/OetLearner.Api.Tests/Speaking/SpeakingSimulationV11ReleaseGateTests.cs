using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingSimulationV11ReleaseGateTests
{
    [Fact]
    public async Task EvaluateAsync_blocks_release_until_all_owner_gates_are_approved()
    {
        await using var fixture = await SimulationV11Fixture.CreateAsync();

        var result = await fixture.Gate.EvaluateAsync("medicine", CancellationToken.None);

        Assert.False(result.IsReleased);
        Assert.Contains("calibration_approval_required", result.BlockingReasons);
        Assert.Contains("concurrency_budget_required", result.BlockingReasons);
        Assert.Contains("cost_ceiling_required", result.BlockingReasons);
        Assert.Contains("retention_approval_required", result.BlockingReasons);
        Assert.Contains("graph_approval_required", result.BlockingReasons);
        Assert.Contains("profession_pack_approval_required", result.BlockingReasons);
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
                CriteriaJson = "[]",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            if (approveAll)
            {
                db.SpeakingSimulationV11OwnerApprovals.AddRange(
                    BuildApproval("calibration_approval", "global"),
                    BuildApproval("concurrency_budget", "global", numericValue: 8m),
                    BuildApproval("cost_ceiling", "global", numericValue: 25m),
                    BuildApproval("retention_approval", "global"),
                    BuildApproval("graph_approval", "global"),
                    BuildApproval("profession_pack_approval", "medicine"));
            }

            await db.SaveChangesAsync();
            return new SimulationV11Fixture(db);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static SpeakingSimulationV11OwnerApproval BuildApproval(
            string approvalKey,
            string scopeKey,
            decimal? numericValue = null)
            => new()
            {
                Id = $"{approvalKey}-{scopeKey}",
                ApprovalKey = approvalKey,
                ScopeKey = scopeKey,
                Status = SpeakingSimulationV11ApprovalStatus.Approved,
                NumericValue = numericValue,
                EvidenceJson = "{}",
                ApprovedByUserId = "system-admin",
                ApprovedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
    }
}
