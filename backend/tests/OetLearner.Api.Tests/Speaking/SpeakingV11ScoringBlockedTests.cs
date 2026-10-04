using System.Text.RegularExpressions;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Owner spec 4 Oct 2026: the nine official OET criteria are the only Speaking scoring model.
/// The ten weighted v1.1 criteria (one of them invented) never score new work, whatever the
/// release-gate approvals say; a report that already exists stays readable. The routing itself
/// is covered by <c>SpeakingFinalizationRaceTests</c>; these source scans stop a new caller
/// from quietly starting a v1.1 grade again.
/// </summary>
public sealed class SpeakingV11ScoringBlockedTests
{
    [Fact]
    public void Source_NothingButTheCanonicalRouterAndTheV11ServiceItselfCanStartAV11Grade()
    {
        var apiRoot = Path.Combine(FindRepoRoot(), "backend", "src", "OetLearner.Api");

        foreach (var file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            var source = File.ReadAllText(file);

            // The combined v1.1 grade has no caller at all any more (the endpoint only reads an existing report).
            if (name != "SpeakingSimulationV11AssessmentService.cs")
            {
                Assert.DoesNotContain("RunCombinedAssessmentAsync(", source, StringComparison.Ordinal);
            }

            // A v1.1 card grade is started only by the canonical router, which gates it on an existing report.
            if (name is not ("SpeakingSimulationV11AssessmentService.cs" or "SpeakingCanonicalAssessmentService.cs"))
            {
                Assert.False(
                    Regex.IsMatch(source, @"\b(v11|v11Assessor|simulationAssessor)\.RunAssessmentAsync\("),
                    $"{name} starts a v1.1 card grade directly");
            }
        }
    }

    [Fact]
    public void Source_TheRouterNoLongerConsultsTheReleaseGate()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "backend", "src", "OetLearner.Api", "Services", "Speaking", "SpeakingCanonicalAssessmentService.cs"));

        Assert.DoesNotContain("ReleaseGate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_TheCombinedEndpointOnlyReadsAnExistingReport()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "backend", "src", "OetLearner.Api", "Endpoints", "SpeakingSimulationV11Endpoints.cs"));

        Assert.Contains("speaking_v11_retired", source, StringComparison.Ordinal);
        Assert.Contains("GetLatestCombinedAsync(", source, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Repository root (AGENTS.md + backend/) not found.");
    }
}
