using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Tests;

public sealed class RecallSetTagSeederTests
{
    [Fact]
    public async Task Production_DoesNotCreatePlaceholderVocabularyRowsForMissingTerms()
    {
        await using var db = NewDb();
        using var tempRoot = new TempSeedRoot("2023-2025", "Unseeded clinical term");

        await RecallSetTagSeeder.EnsureAsync(
            db,
            new FakeWebHostEnvironment(Environments.Production, tempRoot.Path),
            NullLogger.Instance);

        Assert.Empty(db.VocabularyTerms);
    }

    [Fact]
    public async Task Production_TagsExistingTermsWithoutCreatingPlaceholderRows()
    {
        await using var db = NewDb();
        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "vt-existing",
            Term = "Existing clinical term",
            Definition = "A curated definition.",
            ExampleSentence = "A curated example sentence.",
            ExamTypeCode = "OET",
            ProfessionId = null,
            Category = "clinical",
            Difficulty = "medium",
            SourceProvenance = "curated:test",
            Status = "active",
        });
        await db.SaveChangesAsync();
        using var tempRoot = new TempSeedRoot("2023-2025", "Existing clinical term", "Missing clinical term");

        await RecallSetTagSeeder.EnsureAsync(
            db,
            new FakeWebHostEnvironment(Environments.Production, tempRoot.Path),
            NullLogger.Instance);

        var terms = await db.VocabularyTerms.OrderBy(t => t.Term).ToListAsync();
        Assert.Single(terms);
        Assert.Equal("Existing clinical term", terms[0].Term);
        Assert.Contains("2023-2025", terms[0].RecallSetCodesJson, StringComparison.OrdinalIgnoreCase);
    }

    private static LearnerDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class TempSeedRoot : IDisposable
    {
        public TempSeedRoot(string recallSetCode, params string[] terms)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"oet-recall-tags-{Guid.NewGuid():N}");
            var seedFolder = System.IO.Path.Combine(Path, "Data", "SeedData", "recall-sets");
            Directory.CreateDirectory(seedFolder);
            var termsJson = string.Join(",", terms.Select(term => $"\"{term}\""));
            File.WriteAllText(System.IO.Path.Combine(seedFolder, "pack.json"), $$"""
                {
                  "schemaVersion": 1,
                  "recallSetCode": "{{recallSetCode}}",
                  "examTypeCode": "OET",
                  "sourceProvenance": "test-pack",
                  "terms": [{{termsJson}}]
                }
                """);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class FakeWebHostEnvironment(string environmentName, string contentRootPath) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string WebRootPath { get; set; } = contentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}