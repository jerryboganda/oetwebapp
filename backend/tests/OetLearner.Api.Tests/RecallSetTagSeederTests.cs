using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Tests;

public class RecallSetTagSeederTests
{
    [Fact]
    public async Task EnsureAsync_creates_missing_terms_as_draft_with_generated_provenance()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"recall-set-seed-{Guid.NewGuid():N}");
        var seedFolder = Path.Combine(tempRoot, "Data", "SeedData", "recall-sets");
        Directory.CreateDirectory(seedFolder);
        await File.WriteAllTextAsync(
            Path.Combine(seedFolder, "sample.json"),
            """
            {
              "schemaVersion": 1,
              "recallSetCode": "2026",
              "examTypeCode": "OET",
              "sourceProvenance": "generated:platform-authored:recall-set-label-pack-v1:2026;not-source-backed",
              "terms": ["dizziness"]
            }
            """);

        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new LearnerDbContext(options);
        try
        {
            await RecallSetTagSeeder.EnsureAsync(
                db,
                new TestWebHostEnvironment(tempRoot),
                NullLogger.Instance,
                CancellationToken.None);

            var term = await db.VocabularyTerms.SingleAsync();
            Assert.Equal("draft", term.Status);
            Assert.Equal("""["2026"]""", term.RecallSetCodesJson);
            Assert.NotNull(term.SourceProvenance);
            Assert.StartsWith("generated:platform-authored:recall-set-label-pack-v1", term.SourceProvenance);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureAsync_tags_existing_lowercase_oet_term_without_duplicate_placeholder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"recall-set-seed-{Guid.NewGuid():N}");
        var seedFolder = Path.Combine(tempRoot, "Data", "SeedData", "recall-sets");
        Directory.CreateDirectory(seedFolder);
        await File.WriteAllTextAsync(
            Path.Combine(seedFolder, "sample.json"),
            """
            {
              "schemaVersion": 1,
              "recallSetCode": "2026",
              "examTypeCode": "OET",
              "sourceProvenance": "generated:platform-authored:recall-set-label-pack-v1:2026;not-source-backed",
              "terms": ["dizziness"]
            }
            """);

        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new LearnerDbContext(options);
        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "vt-existing",
            Term = "dizziness",
            Definition = "A sensation of light-headedness.",
            ExampleSentence = "The patient reported dizziness.",
            ExamTypeCode = "oet",
            Category = "symptom",
            Status = "active",
            RecallSetCodesJson = "[]",
        });
        await db.SaveChangesAsync();

        try
        {
            await RecallSetTagSeeder.EnsureAsync(
                db,
                new TestWebHostEnvironment(tempRoot),
                NullLogger.Instance,
                CancellationToken.None);

            Assert.Equal(1, await db.VocabularyTerms.CountAsync());
            var term = await db.VocabularyTerms.SingleAsync();
            Assert.Equal("active", term.Status);
            Assert.Equal("""["2026"]""", term.RecallSetCodesJson);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Repository_recall_set_packs_do_not_claim_source_backed_provenance()
    {
        var seedFolder = Path.Combine(FindApiProjectRoot(), "Data", "SeedData", "recall-sets");
        var banned = new[] { "pdf", "verbatim", "extracted", "dr hesham" };

        foreach (var file in Directory.GetFiles(seedFolder, "*.json", SearchOption.TopDirectoryOnly))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var provenance = root.TryGetProperty("sourceProvenance", out var provenanceElement)
                ? provenanceElement.GetString()
                : null;
            var notes = root.TryGetProperty("_notes", out var notesElement) ? notesElement.GetString() : null;

            Assert.NotNull(provenance);
            Assert.StartsWith("generated:platform-authored:recall-set-label-pack-v1", provenance);
            var combined = $"{provenance} {notes}".ToLowerInvariant();
            foreach (var bannedTerm in banned)
            {
                Assert.DoesNotContain(bannedTerm, combined);
            }
        }
    }

    private static string FindApiProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var direct = Path.Combine(current.FullName, "Data", "SeedData", "recall-sets");
            if (Directory.Exists(direct))
            {
                return current.FullName;
            }

            var repoRelative = Path.Combine(current.FullName, "backend", "src", "OetLearner.Api", "Data", "SeedData", "recall-sets");
            if (Directory.Exists(repoRelative))
            {
                return Path.Combine(current.FullName, "backend", "src", "OetLearner.Api");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate OetLearner.Api recall-set seed folder.");
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
