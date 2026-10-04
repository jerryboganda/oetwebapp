using System.Text.RegularExpressions;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The candidate-facing Speaking score is computed twice — <c>OetScoring.SpeakingRawToReported</c> here
/// and <c>SPEAKING_RAW_TO_REPORTED</c> in <c>lib/scoring.ts</c> — and the two must never drift
/// (AGENTS.md: "the two implementations must remain behaviourally identical"). A calibration fit
/// changes both literals and the mapping version in the same commit; this test fails if only one moves.
/// </summary>
public sealed class SpeakingScoreMappingParityTests
{
    [Fact]
    public void TypeScriptTable_EqualsTheCSharpTable_EntryByEntry()
    {
        var ts = File.ReadAllText(Path.Combine(FindRepoRoot(), "lib", "scoring.ts"));

        var match = Regex.Match(
            ts,
            @"SPEAKING_RAW_TO_REPORTED\s*:\s*readonly\s+number\[\]\s*=\s*\[(?<body>[^\]]*)\]");
        Assert.True(match.Success, "SPEAKING_RAW_TO_REPORTED literal not found in lib/scoring.ts");

        var tsTable = match.Groups["body"].Value
            .Split(new[] { ',', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();

        Assert.Equal(OetScoring.SpeakingRawToReported.ToArray(), tsTable);
    }

    [Fact]
    public void TypeScriptMappingVersion_EqualsTheCSharpMappingVersion()
    {
        var ts = File.ReadAllText(Path.Combine(FindRepoRoot(), "lib", "scoring.ts"));

        var match = Regex.Match(ts, @"SPEAKING_MAPPING_VERSION\s*=\s*'(?<version>[^']+)'");
        Assert.True(match.Success, "SPEAKING_MAPPING_VERSION not found in lib/scoring.ts");
        Assert.Equal(OetScoring.SpeakingMappingVersion, match.Groups["version"].Value);
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
