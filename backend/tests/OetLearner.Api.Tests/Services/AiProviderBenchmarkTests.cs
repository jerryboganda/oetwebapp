using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

public sealed class AiProviderBenchmarkTests
{
    [Fact]
    public void Scoring_critical_fails_on_pass_fail_flip()
    {
        var service = NewService(out _);
        var result = service.Evaluate(AiBenchmarkClass.ScoringCritical, PerfectScoring() with { PassFailFlips = 1 });
        Assert.False(result.Passed);
        Assert.Contains("pass_fail_flips", result.Failures);
    }

    [Fact]
    public void Scoring_critical_requires_full_schema_and_citations()
    {
        var service = NewService(out _);
        var schema = service.Evaluate(AiBenchmarkClass.ScoringCritical, PerfectScoring() with { SchemaValidityPct = 99.9m });
        var cites = service.Evaluate(AiBenchmarkClass.ScoringCritical, PerfectScoring() with { CitationCompliancePct = 99m });
        Assert.False(schema.Passed);
        Assert.False(cites.Passed);
    }

    [Fact]
    public void Non_scoring_fails_on_fabricated_claims_or_weak_savings()
    {
        var service = NewService(out _);
        var fabricated = service.Evaluate(AiBenchmarkClass.NonScoring, PerfectNonScoring() with { FabricatedSourceClaims = 1 });
        var cheap = service.Evaluate(AiBenchmarkClass.NonScoring, PerfectNonScoring() with { CostReductionPct = 29m });
        Assert.False(fabricated.Passed);
        Assert.False(cheap.Passed);
    }

    [Fact]
    public async Task Switch_away_from_claude_is_refused_without_a_passing_run()
    {
        var service = NewService(out _);
        await Assert.ThrowsAsync<AiProviderRouteRefusedException>(() =>
            service.EnsureSwitchAllowedAsync(new AiRouteSwitchRequest(
                AiFeatureCodes.WritingGrade, "openai", "gpt-4o", null, null, "anthropic", "claude-sonnet-5"), default));
    }

    [Fact]
    public async Task Failed_run_cannot_authorize_a_switch()
    {
        var service = NewService(out var db);
        var run = await service.RecordRunAsync(
            AiFeatureCodes.ReadingExplanation, "openai", "gpt-4o-mini", "explanations.v1",
            PerfectNonScoring() with { CostReductionPct = 10m }, default);

        Assert.False(run.Passed);
        await Assert.ThrowsAsync<AiProviderRouteRefusedException>(() =>
            service.EnsureSwitchAllowedAsync(new AiRouteSwitchRequest(
                AiFeatureCodes.ReadingExplanation, "openai", "gpt-4o-mini", run.Id, null, "anthropic", null), default));
        Assert.Equal(0, await db.AiFeatureRoutes.CountAsync());
    }

    [Fact]
    public async Task Passing_run_authorizes_matching_switch_and_claude_always_allowed()
    {
        var service = NewService(out _);
        var run = await service.RecordRunAsync(
            AiFeatureCodes.ReadingExplanation, "openai", "gpt-4o-mini", "explanations.v1",
            PerfectNonScoring(), default);

        Assert.True(run.Passed);
        await service.EnsureSwitchAllowedAsync(new AiRouteSwitchRequest(
            AiFeatureCodes.ReadingExplanation, "openai", "gpt-4o-mini", run.Id, "route-1", "anthropic", "claude-sonnet-5"), default);

        await service.EnsureSwitchAllowedAsync(new AiRouteSwitchRequest(
            AiFeatureCodes.WritingGrade, "anthropic", "claude-sonnet-5", null, null, "openai", "gpt-4o"), default);
    }

    [Fact]
    public void Frozen_corpora_are_versioned_and_checked_in()
    {
        var root = FindRepoRoot();
        var dir = Path.Combine(root, "docs", "benchmarks");
        foreach (var name in new[]
                 {
                     "writing-grading.v1.json",
                     "speaking-assessment.v1.json",
                     "explanations.v1.json",
                     "patient-turns.v1.json",
                     "admin-extraction.v1.json",
                     "admin-drafting.v1.json",
                 })
        {
            var path = Path.Combine(dir, name);
            Assert.True(File.Exists(path), path);
            Assert.Contains("\"corpusVersion\"", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    private static AiBenchmarkMetrics PerfectScoring() => new(
        100m, 100m, 0, 0, 96m, 12m, 100m, 0, 0m);

    private static AiBenchmarkMetrics PerfectNonScoring() => new(
        99m, 100m, 0, 0, 100m, 0m, 97m, 0, 35m);

    private static AiProviderRouteApprovalService NewService(out LearnerDbContext db)
    {
        db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        return new AiProviderRouteApprovalService(db);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "benchmarks", "writing-grading.v1.json")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("docs/benchmarks/writing-grading.v1.json not found from " + AppContext.BaseDirectory);
    }
}
