using Xunit;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Listening V2 — mission-critical scoring-path audit. Source-scans the
/// Listening service tree for legacy raw→scaled formula paths. Listening
/// scoring must resolve an owner-approved versioned table; no formula helper
/// is an acceptable production fallback.
///
/// Why a meta-test: the v1.1 invariant is that a later or missing table cannot
/// silently change a result and no copied formula can bypass governance. A
/// reviewer-only check is not sufficient because copy-paste of an old ad-hoc
/// formula is the most likely regression vector.
/// </summary>
public class ListeningScoringPathAuditTest
{
    [Fact]
    public void Listening_services_use_versioned_conversion_only()
    {
        var listeningServicesDir = LocateListeningServicesDir();
        var gradingSource = File.ReadAllText(Path.Combine(listeningServicesDir, "ListeningGradingService.cs"));
        var serviceSource = string.Join(
            "\n",
            Directory.GetFiles(listeningServicesDir, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("OetRawToScaled", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GradeListeningReading", serviceSource, StringComparison.Ordinal);
        Assert.Contains("IAssessmentScoreConversionService", gradingSource, StringComparison.Ordinal);
    }

    private static string LocateListeningServicesDir()
    {
        // Walk up from the test bin dir to the repo root.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "backend", "src", "OetLearner.Api", "Services", "Listening");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        throw new DirectoryNotFoundException(
            "Could not locate backend/src/OetLearner.Api/Services/Listening from test bin dir.");
    }

    /// <summary>Strip everything after the first <c>//</c> that is not
    /// inside a string literal. Crude but sufficient for our line-by-line
    /// pattern audit (we do NOT need to parse C# precisely).</summary>
    private static string StripInlineComment(string line)
    {
        bool inString = false;
        bool inChar = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && !inChar) inString = !inString;
            else if (c == '\'' && !inString) inChar = !inChar;
            else if (!inString && !inChar && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                return line.Substring(0, i);
        }
        return line;
    }
}
