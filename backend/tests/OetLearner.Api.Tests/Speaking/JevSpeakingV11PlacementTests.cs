namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Structural pins for where the Jev calls sit in the v1.1 grader. The v1.1 pipeline has no
/// in-memory harness (release gate, audio assessment, persona runtime, usage ledger), so the
/// guarantee that Jev can never trip <c>latency_sla_exceeded</c>, run inside the grade chain, or
/// reach the corpus harness is pinned on the source order itself.
/// </summary>
public sealed class JevSpeakingV11PlacementTests
{
    private static readonly string ServicesRoot =
        Path.Combine(FindRepoRoot(), "backend", "src", "OetLearner.Api", "Services", "Speaking");

    private static string Source(string file) => File.ReadAllText(Path.Combine(ServicesRoot, file));

    [Fact]
    public void V11_ReadinessRunsBeforeTheGradeStopwatch_AndTheCrosscheckAfterTheSlaChecks()
    {
        var source = Source("SpeakingSimulationV11AssessmentService.cs");
        var readiness = source.IndexOf("JevSpeakingAdvisor.CheckReadinessAsync(", StringComparison.Ordinal);
        var watchStart = source.IndexOf("Stopwatch.StartNew()", StringComparison.Ordinal);
        var chain = source.IndexOf("SpeakingGradeChain.CompleteAsync(", StringComparison.Ordinal);
        var watchStop = source.IndexOf("watch.Stop()", StringComparison.Ordinal);
        var sla = source.IndexOf("watch.ElapsedMilliseconds > budget.LatencySlaMs", StringComparison.Ordinal);
        var crosscheck = source.IndexOf("JevSpeakingAdvisor.CrosscheckAsync(", StringComparison.Ordinal);

        Assert.True(readiness > 0, "readiness call missing");
        Assert.True(crosscheck > 0, "cross-check call missing");
        // readiness < stopwatch start < grade chain < stopwatch stop < SLA check < cross-check
        Assert.True(
            readiness < watchStart && watchStart < chain && chain < watchStop && watchStop < sla && sla < crosscheck,
            $"order was readiness={readiness} start={watchStart} chain={chain} stop={watchStop} sla={sla} crosscheck={crosscheck}");
        Assert.Equal(readiness, source.LastIndexOf("JevSpeakingAdvisor.CheckReadinessAsync(", StringComparison.Ordinal));
        Assert.Equal(crosscheck, source.LastIndexOf("JevSpeakingAdvisor.CrosscheckAsync(", StringComparison.Ordinal));
    }

    [Fact]
    public void Classic_ReadinessRunsBeforeTheGradeChain_AndTheCrosscheckAfterTheGradeIsScaled()
    {
        // The grade itself lives in GradeCoreAsync (shared by the card grade, the combined Full Mock and the calibration
        // harness): the chain, then the scaled score. RunAssessmentAsync wraps it: readiness, the core, then the cross-check.
        var source = Source("SpeakingAiAssessmentService.cs");
        var readiness = source.IndexOf("JevSpeakingAdvisor.CheckReadinessAsync(", StringComparison.Ordinal);
        var coreCall = source.IndexOf("await GradeCoreAsync(", StringComparison.Ordinal);
        var crosscheck = source.IndexOf("JevSpeakingAdvisor.CrosscheckAsync(", StringComparison.Ordinal);
        var coreStart = source.IndexOf("Task<SpeakingGradeOutcome> GradeCoreAsync(", StringComparison.Ordinal);
        var chain = source.IndexOf("SpeakingGradeChain.CompleteAsync(", StringComparison.Ordinal);
        var scaled = source.IndexOf("OetScoring.SpeakingReportedScaled(rubricScores)", StringComparison.Ordinal);

        Assert.True(readiness > 0 && crosscheck > 0 && coreStart > 0);
        Assert.True(
            readiness < coreCall && coreCall < crosscheck,
            $"order was readiness={readiness} core={coreCall} crosscheck={crosscheck}");
        Assert.True(
            coreStart < chain && chain < scaled,
            $"order was core={coreStart} chain={chain} scaled={scaled}");
        Assert.DoesNotContain("JevSpeakingAdvisor", source[coreStart..], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SpeakingGradeChain.cs")]
    [InlineData("SpeakingCorpusCompatibilityService.cs")]
    [InlineData("SpeakingCanonicalAssessmentService.cs")]
    public void TheGradeChainAndTheCorpusHarness_NeverCallJev(string file)
    {
        var source = Source(file);

        Assert.DoesNotContain("JevSpeakingAdvisor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ITypeSafeJudgmentService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeSafeOptions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGradeChain_HasNoJevAtAll()
    {
        Assert.DoesNotContain("Jev", Source("SpeakingGradeChain.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void EachGradingPath_MakesExactlyOneGradeChainCall_NoJevHopIsAddedToIt()
    {
        Assert.Equal(1, CountOf(Source("SpeakingAiAssessmentService.cs"), "SpeakingGradeChain.CompleteAsync("));
        var v11 = Source("SpeakingSimulationV11AssessmentService.cs");
        var combinedStart = v11.IndexOf("RunCombinedAssessmentAsync(", StringComparison.Ordinal);
        Assert.True(combinedStart > 0, "combined assessment method missing");
        Assert.Equal(1, CountOf(v11[..combinedStart], "SpeakingGradeChain.CompleteAsync("));
        Assert.Equal(1, CountOf(v11[combinedStart..], "SpeakingGradeChain.CompleteAsync("));
        Assert.DoesNotContain("JevSpeakingAdvisor", v11[combinedStart..], StringComparison.Ordinal);
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the OET repository root.");
    }
}
