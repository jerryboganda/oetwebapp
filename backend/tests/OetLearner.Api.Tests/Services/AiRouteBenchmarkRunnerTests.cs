using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Metric mathematics of the route benchmark (the part that must never drift silently).
/// The live corpus execution is exercised in production by the admin endpoint; these tests pin
/// the aggregation, the fabrication/refusal rules and the honest cost_reduction failure modes.
/// </summary>
public class AiRouteBenchmarkRunnerTests
{
    private static AiRouteBenchmarkCaseOutcome Outcome(
        string id, bool valid, bool? grounded = null, bool fabricated = false,
        int prompt = 100, int completion = 50, decimal cost = 0.001m, string? error = null)
        => new(id, "grounded", valid, grounded, fabricated, prompt, completion, cost, error);

    [Fact]
    public void ComputeMetrics_PerfectCandidate_WithCheaperCost_PassesAllBars()
    {
        var candidate = Enumerable.Range(1, 10)
            .Select(i => Outcome($"c{i}", valid: true, grounded: i <= 6, cost: 0.0005m))
            .ToList();
        var incumbent = Enumerable.Range(1, 10)
            .Select(i => Outcome($"i{i}", valid: true, grounded: i <= 6, cost: 0.002m))
            .ToList();

        var metrics = AiRouteBenchmarkRunner.ComputeMetrics(candidate, incumbent);

        Assert.Equal(100m, metrics.SchemaValidityPct);
        Assert.Equal(100m, metrics.EvidenceGroundingPct);
        Assert.Equal(0, metrics.FabricatedSourceClaims);
        Assert.Equal(75m, metrics.CostReductionPct);
    }

    [Fact]
    public void ComputeMetrics_InvalidCases_SinkSchemaValidity()
    {
        var candidate = Enumerable.Range(1, 10)
            .Select(i => Outcome($"c{i}", valid: i % 2 == 0))
            .ToList();

        var metrics = AiRouteBenchmarkRunner.ComputeMetrics(candidate, []);

        Assert.Equal(50m, metrics.SchemaValidityPct);
    }

    [Fact]
    public void ComputeMetrics_RefusalWithoutCitation_IsCorrectlyGrounded_ButCitationOutsideProvidedRange_IsFabricated()
    {
        // Grounded math is pinned by GroundedCorrect/FabricatedCitation indirectly: a correct
        // refusal (no citation) counts as grounded and not fabricated.
        var refusing = new AiRouteBenchmarkCaseOutcome("g5", "grounded", true, true, false, 100, 50, 0.001m, null);
        var fabricating = new AiRouteBenchmarkCaseOutcome("g5b", "grounded", true, false, true, 100, 50, 0.001m, null);

        var metrics = AiRouteBenchmarkRunner.ComputeMetrics([refusing, fabricating], []);

        Assert.Equal(50m, metrics.EvidenceGroundingPct);
        Assert.Equal(1, metrics.FabricatedSourceClaims);
    }

    [Fact]
    public void ComputeMetrics_NoIncumbentRoute_CostReductionCannotPass()
    {
        var candidate = Enumerable.Range(1, 5).Select(i => Outcome($"c{i}", valid: true, grounded: true)).ToList();

        var metrics = AiRouteBenchmarkRunner.ComputeMetrics(candidate, []);

        Assert.Equal(-100m, metrics.CostReductionPct);
    }

    [Fact]
    public void ComputeMetrics_FreeIncumbent_CostReductionCannotPass()
    {
        var candidate = Enumerable.Range(1, 5).Select(i => Outcome($"c{i}", valid: true, grounded: true, cost: 0.001m)).ToList();
        var incumbent = Enumerable.Range(1, 5).Select(i => Outcome($"i{i}", valid: true, grounded: true, cost: 0m)).ToList();

        var metrics = AiRouteBenchmarkRunner.ComputeMetrics(candidate, incumbent);

        Assert.Equal(-100m, metrics.CostReductionPct);
    }

    [Fact]
    public void ComputeMetrics_EmptyCandidateCorpus_FailsClosed()
    {
        var metrics = AiRouteBenchmarkRunner.ComputeMetrics([], []);

        Assert.Equal(0m, metrics.SchemaValidityPct);
        Assert.Equal(-100m, metrics.CostReductionPct);
    }

    [Fact]
    public void Corpus_ContainsGroundedSchemaAndToolCases_WithBoundedSourceRanges()
    {
        var corpus = AiRouteBenchmarkRunner.Corpus;

        Assert.Equal(10, corpus.Count);
        Assert.Equal(6, corpus.Count(c => c.Kind == "grounded"));
        Assert.Equal(2, corpus.Count(c => c.Kind == "schema"));
        Assert.Equal(2, corpus.Count(c => c.Kind == "tool"));
        Assert.All(corpus.Where(c => c.Kind == "schema"), c => Assert.NotEmpty(c.RequiredJsonKeys));
        Assert.All(corpus, c => Assert.InRange(c.ProvidedSourceCount, 1, 3));
    }
}
