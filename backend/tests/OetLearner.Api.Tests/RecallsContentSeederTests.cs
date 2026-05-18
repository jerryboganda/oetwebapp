using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Tests;

public class RecallsContentSeederTests
{
    [Fact]
    public async Task EnsureAsync_accepts_all_repository_recall_seed_terms()
    {
        var seedRoot = FindApiProjectRoot();
        var expectedTerms = ReadExpectedSeedTerms(seedRoot);
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new LearnerDbContext(options);

        await RecallsContentSeeder.EnsureAsync(
            db,
            new TestWebHostEnvironment(seedRoot),
            NullLogger.Instance,
            CancellationToken.None);

        var actualTerms = await db.VocabularyTerms.ToListAsync();
        Assert.Equal(expectedTerms.Count, actualTerms.Count);
        foreach (var term in actualTerms)
        {
            Assert.Equal(expectedTerms[SeedKey(term.ExamTypeCode, term.ProfessionId, term.Term)], term.Status);
            Assert.NotNull(term.SourceProvenance);
            Assert.StartsWith("generated:platform-authored:recalls-content-pack-v1", term.SourceProvenance);
        }
    }

    [Fact]
    public async Task EnsureAsync_uses_generated_provenance_fallback_when_pack_omits_source()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"recalls-seed-{Guid.NewGuid():N}");
        var seedFolder = Path.Combine(tempRoot, "Data", "SeedData", "recalls");
        Directory.CreateDirectory(seedFolder);
        await File.WriteAllTextAsync(
            Path.Combine(seedFolder, "medicine.json"),
            """
            {
              "schemaVersion": 1,
              "professionId": "medicine",
              "examTypeCode": "OET",
              "defaultStatus": "draft",
              "terms": [
                {
                  "term": "triage",
                  "definition": "Sorting patients by clinical urgency.",
                  "exampleSentence": "The nurse performed triage at reception.",
                  "category": "procedure",
                  "oetSubtestTags": ["listening_a"],
                  "difficulty": "medium"
                }
              ]
            }
            """);

        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new LearnerDbContext(options);
        try
        {
            await RecallsContentSeeder.EnsureAsync(
                db,
                new TestWebHostEnvironment(tempRoot),
                NullLogger.Instance,
                CancellationToken.None);

            var term = await db.VocabularyTerms.SingleAsync();
            Assert.NotNull(term.SourceProvenance);
            Assert.StartsWith("generated:platform-authored:recalls-content-pack-v1", term.SourceProvenance);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static Dictionary<string, string> ReadExpectedSeedTerms(string apiRoot)
    {
        var seedFolder = Path.Combine(apiRoot, "Data", "SeedData", "recalls");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(seedFolder, "*.json", SearchOption.TopDirectoryOnly))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var profession = root.TryGetProperty("professionId", out var professionElement)
                ? professionElement.GetString()
                : Path.GetFileNameWithoutExtension(file);
            var examType = root.TryGetProperty("examTypeCode", out var examTypeElement)
                ? examTypeElement.GetString()
                : "oet";
            var defaultStatus = root.TryGetProperty("defaultStatus", out var statusElement)
                ? statusElement.GetString()
                : "draft";
            foreach (var term in root.GetProperty("terms").EnumerateArray())
            {
                var text = term.GetProperty("term").GetString();
                expected[SeedKey(examType, profession, text)] = defaultStatus?.Trim() ?? "draft";
            }
        }

        return expected;
    }

    private static string SeedKey(string? examType, string? profession, string? term)
        => $"{examType?.Trim().ToLowerInvariant()}:{profession?.Trim().ToLowerInvariant()}:{term?.Trim().ToLowerInvariant()}";

    private static string FindApiProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var direct = Path.Combine(current.FullName, "Data", "SeedData", "recalls");
            if (Directory.Exists(direct))
            {
                return current.FullName;
            }

            var repoRelative = Path.Combine(current.FullName, "backend", "src", "OetLearner.Api", "Data", "SeedData", "recalls");
            if (Directory.Exists(repoRelative))
            {
                return Path.Combine(current.FullName, "backend", "src", "OetLearner.Api");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate OetLearner.Api recall seed folder.");
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
