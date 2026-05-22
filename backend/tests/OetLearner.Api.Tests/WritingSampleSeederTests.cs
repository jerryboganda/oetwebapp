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
        Assert.Equal("wrt-v1-1-routine-referral", paper.SourceProvenance);

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
                    seedId = "wrt-v1-bogus",
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

    [Fact]
    public async Task Seeder_loads_multiple_files_from_csv()
    {
        await using var db = CreateDb();
        var fileA = WriteSeedFile(samples: 2);
        var fileB = WriteSecondSeedFile();
        var opts = new WritingSeedOptions
        {
            Enabled = true,
            SeedFilePathsCsv = $"{fileA}, {fileB}",
        };
        var seeder = BuildSeederWithOptions(db, opts);

        var created = await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(3, created);
        var papers = await db.ContentPapers.AsNoTracking().ToListAsync();
        Assert.Equal(3, papers.Count);
        Assert.Contains(papers, p => p.SourceProvenance == "wrt-v1-1-routine-referral");
        Assert.Contains(papers, p => p.SourceProvenance == "wrt-v1-3-urgent-referral");
        Assert.Contains(papers, p => p.SourceProvenance == "wrt-v2-extra-nursing");
    }

    [Fact]
    public async Task Seeder_respects_per_file_auto_publish_override()
    {
        await using var db = CreateDb();
        var fileA = WriteSeedFile(samples: 1);
        var fileB = WriteSecondSeedFile();
        var opts = new WritingSeedOptions
        {
            Enabled = true,
            SeedFilePathsCsv = $"{fileA},{fileB}",
            AutoPublish = true,
            AutoPublishByFile = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.GetFileName(fileB)] = false,
            },
        };
        var seeder = BuildSeederWithOptions(db, opts);

        var created = await seeder.SeedAsync(CancellationToken.None);
        Assert.Equal(2, created);

        var papers = await db.ContentPapers.AsNoTracking().ToListAsync();
        var v1 = papers.Single(p => p.SourceProvenance == "wrt-v1-1-routine-referral");
        var v2 = papers.Single(p => p.SourceProvenance == "wrt-v2-extra-nursing");
        Assert.Equal(ContentStatus.Published, v1.Status);
        Assert.NotNull(v1.PublishedAt);
        Assert.Equal(ContentStatus.Draft, v2.Status);
        Assert.Null(v2.PublishedAt);
        // PublishedRevisionId is always set (required by ContentItem FK), but
        // PublishedAt staying null is the actual signal that the paper is unpublished.
        Assert.NotNull(v2.PublishedRevisionId);

        // Audit trail: published file logs both Seeded + Published; draft logs only Seeded.
        var v1Audits = await db.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceId == v1.Id)
            .Select(a => a.Action)
            .OrderBy(s => s)
            .ToListAsync();
        Assert.Equal(new[] { "ContentPaperPublished", "ContentPaperSeeded" }, v1Audits);

        var v2Audits = await db.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceId == v2.Id)
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Equal(new[] { "ContentPaperSeeded" }, v2Audits);

        // ContentItem status mirrors paper status.
        var v2Item = await db.ContentItems.AsNoTracking().FirstAsync(c => c.Id == v2.Id);
        Assert.Equal(ContentStatus.Draft, v2Item.Status);
    }

    [Fact]
    public async Task Seeder_skips_letter_type_not_allowed_for_profession()
    {
        await using var db = CreateDb();
        var dir = Path.Combine(Path.GetTempPath(), $"writing-seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var seedFile = Path.Combine(dir, "writing-samples-vet.json");
        var doc = new
        {
            schemaVersion = 1,
            samples = new[]
            {
                new
                {
                    seedId = "wrt-v2-vet-bad",
                    slug = "writing-v2-vet-bad",
                    title = "Veterinary — should-be-blocked",
                    profession = "veterinary",
                    // non_medical_referral is NOT allowed for veterinary per the per-profession allow-list.
                    letterType = "non_medical_referral",
                    caseNotesText = "x",
                    modelAnswerText = "y",
                },
                new
                {
                    seedId = "wrt-v2-vet-ok",
                    slug = "writing-v2-vet-ok",
                    title = "Veterinary — urgent",
                    profession = "veterinary",
                    letterType = "urgent_referral",
                    caseNotesText = "x",
                    modelAnswerText = "y",
                },
            },
        };
        File.WriteAllText(seedFile, JsonSerializer.Serialize(doc));

        var seeder = BuildSeeder(db, seedFile, enabled: true);
        var created = await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(1, created);
        var papers = await db.ContentPapers.AsNoTracking().ToListAsync();
        Assert.Single(papers);
        Assert.Equal("wrt-v2-vet-ok", papers[0].SourceProvenance);
    }

    private static string WriteSecondSeedFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"writing-seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "writing-samples.v2.json");
        var doc = new
        {
            schemaVersion = 1,
            samples = new[]
            {
                new
                {
                    seedId = "wrt-v2-extra-nursing",
                    slug = "writing-v2-extra-nursing",
                    title = "Nursing — Community Nurse Referral",
                    profession = "nursing",
                    letterType = "routine_referral",
                    caseNotesText = "",
                    modelAnswerText = "",
                },
            },
        };
        File.WriteAllText(file, JsonSerializer.Serialize(doc));
        return file;
    }

    private static WritingSampleSeeder BuildSeederWithOptions(LearnerDbContext db, WritingSeedOptions opts)
    {
        var scopeFactory = new SingleDbScopeFactory(db);
        var env = new FakeHostEnvironment(Path.GetTempPath());
        return new WritingSampleSeeder(scopeFactory, Options.Create(opts), env, NullLogger<WritingSampleSeeder>.Instance);
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
                seedId = "wrt-v1-1-routine-referral",
                slug = "writing-1-routine-referral",
                title = "Routine Referral",
                profession = "medicine",
                letterType = "routine_referral",
                caseNotesText = "Patient has Acne and Rosacea.",
                modelAnswerText = "Dear Dr Smith,\n\nI am writing to refer Ms Sarah Miller.",
            },
            new
            {
                seedId = "wrt-v1-3-urgent-referral",
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
