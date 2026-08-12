using Xunit;

namespace OetLearner.Api.Tests.Reading;

/// <summary>
/// Reading v1.1 scoring-path audit. Candidate Reading scaled scores must come
/// only from the immutable owner-approved conversion service; the historical
/// raw-to-scaled helper is not a permitted production fallback.
/// </summary>
public sealed class ReadingScoringPathAuditTest
{
    [Fact]
    public void Reading_services_use_versioned_conversion_only()
    {
        var readingServicesDir = LocateReadingServicesDir();
        var gradingSource = File.ReadAllText(Path.Combine(readingServicesDir, "ReadingGradingService.cs"));
        var serviceSource = string.Join(
            "\n",
            Directory.GetFiles(readingServicesDir, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("OetRawToScaled", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GradeListeningReading", serviceSource, StringComparison.Ordinal);
        Assert.Contains("IAssessmentScoreConversionService", gradingSource, StringComparison.Ordinal);
    }

    private static string LocateReadingServicesDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "backend", "src", "OetLearner.Api", "Services", "Reading");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src/OetLearner.Api/Services/Reading from test bin dir.");
    }
}
