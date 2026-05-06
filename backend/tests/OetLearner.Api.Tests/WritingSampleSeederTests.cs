using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class WritingSampleSeederTests
{
    [Fact]
    public async Task Seeder_creates_published_papers_with_content_items_and_is_idempotent()
    {
        await using var db = CreateDb();
        var seedFile = WriteSeedFile(samples: 2);
        var seeder = BuildSeeder(db, seedFile, enabled: true);

        var firstRun = await seeder.SeedAsync(CancellationToken.None);
        Assert.Equal(2, firstRun);

        var first = await db.ContentPapers.AsNoTracking().ToListAsync();
        Assert.Equal(2, first.Count);
        var paper = first.Single(p => p.Slug == "writing-1-routine-referral");
        Assert.Equal(ContentStatus.Published, paper.Status);
        Assert.NotNull(paper.PublishedAt);
        Assert.Equal("writing", paper.SubtestCode);
        Assert.Equal("routine_referral", paper.LetterType);
        Assert.Equal("seed:writing:v1:writing-1-routine-referral", paper.SourceProvenance);

        var structure = WritingContentStructure.ExtractStructure(paper.ExtractedTextJson);
        Assert.Contains("Acne", WritingContentStructure.BuildCaseNotesText(structure));
        Assert.Contains("Dr Smith", WritingContentStructure.BuildModelAnswerText(structure));

        // Learner-facing ContentItem must be created and Published.
        var item = await db.ContentItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == paper.Id);
        Assert.NotNull(item);
        Assert.Equal(ContentStatus.Published, item!.Status);
        Assert.Equal("writing_task", item.ContentType);
        Assert.Equal("writing", item.SubtestCode);
        Assert.Contains("Acne", item.CaseNotes);

        var audits = await db.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceId == paper.Id)
            .OrderBy(a => a.Action)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Equal("ContentPaperPublished", audits[0].Action);
        Assert.Equal("ContentPaperSeeded", audits[1].Action);

        var secondRun = await seeder.SeedAsync(CancellationToken.None);
        Assert.Equal(0, secondRun);
        Assert.Equal(2, await db.ContentPapers.CountAsync());
        Assert.Equal(2, await db.ContentItems.CountAsync());
    }

    [Fact]
    public async Task Seeder_skips_when_disabled()
    {
        await using var db = CreateDb();
        var seedFile = WriteSeedFile(samples: 1);
        var seeder = BuildSeeder(db, seedFile, enabled: false);

        var created = await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(0, created);
        Assert.Equal(0, await db.ContentPapers.CountAsync());
    }

    [Fact]
    public async Task Seeder_rejects_non_canonical_letter_type()
    {
        await using var db = CreateDb();
        var dir = Path.Combine(Path.GetTempPath(), $"writing-seed-{Guid.NewGuid():N}");
        var seedFile = Path.Combine(dir, "writing-samples.v1.json");
        Directory.CreateDirectory(dir);
        var doc = new
        {
            schemaVersion = 1,
            samples = new[]
            {
                new
                {
                    seedId = "seed:writing:v1:bogus",
                    slug = "bogus",
                    title = "Bogus",
                    profession = "medicine",
                    letterType = "not_a_real_type",
                    caseNotesText = "x",
                    modelAnswerText = "y",
                },
            },
        };
        File.WriteAllText(seedFile, JsonSerializer.Serialize(doc));

        var seeder = BuildSeeder(db, seedFile, enabled: true);
        var created = await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(0, created);
        Assert.Equal(0, await db.ContentPapers.CountAsync());
    }

    [Fact]
    public async Task Seeder_skips_when_seed_file_missing()
    {
        await using var db = CreateDb();
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        var seeder = BuildSeeder(db, missing, enabled: true);

        var created = await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(0, created);
    }

    private static string WriteSeedFile(int samples)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"writing-seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "writing-samples.v1.json");
        var sampleData = new[]
        {
            new
            {
                seedId = "seed:writing:v1:writing-1-routine-referral",
                slug = "writing-1-routine-referral",
                title = "Routine Referral",
                profession = "medicine",
                letterType = "routine_referral",
                caseNotesText = "Patient has Acne and Rosacea.",
                modelAnswerText = "Dear Dr Smith,\n\nI am writing to refer Ms Sarah Miller.",
            },
            new
            {
                seedId = "seed:writing:v1:writing-3-urgent-referral",
                slug = "writing-3-urgent-referral",
                title = "Urgent Referral",
                profession = "medicine",
                letterType = "urgent_referral",
                caseNotesText = "Patient with chest pain.",
                modelAnswerText = "Dear Dr Brown,\n\nUrgent referral for Leo Bennett.",
            },
        };
        var doc = new { schemaVersion = 1, samples = sampleData.Take(samples).ToArray() };
        File.WriteAllText(file, JsonSerializer.Serialize(doc));
        return file;
    }

    private static WritingSampleSeeder BuildSeeder(LearnerDbContext db, string seedFile, bool enabled)
    {
        var scopeFactory = new SingleDbScopeFactory(db);
        var options = Options.Create(new WritingSeedOptions
        {
            Enabled = enabled,
            SeedFilePath = seedFile,
        });
        var env = new FakeHostEnvironment(Path.GetTempPath());
        return new WritingSampleSeeder(scopeFactory, options, env, NullLogger<WritingSampleSeeder>.Instance);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private sealed class FakeHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class SingleDbScopeFactory(LearnerDbContext db) : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType)
            => serviceType == typeof(LearnerDbContext) ? db : null;
        public void Dispose() { /* db owned by test */ }
    }
}
