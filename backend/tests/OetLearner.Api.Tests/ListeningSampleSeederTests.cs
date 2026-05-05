using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

public class ListeningSampleSeederTests
{
    private sealed class FakeHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static (LearnerDbContext db, InMemoryFileStorage storage) BuildDeps()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new InMemoryFileStorage());
    }

    private static ListeningSampleSeeder BuildSeeder(
        LearnerDbContext db,
        InMemoryFileStorage storage,
        ListeningSampleSeederOptions opts,
        string? contentRoot = null)
    {
        return new ListeningSampleSeeder(
            db,
            storage,
            new FakeHostEnvironment(contentRoot ?? Path.GetTempPath()),
            Options.Create(opts),
            NullLogger<ListeningSampleSeeder>.Instance);
    }

    [Fact]
    public async Task Seeder_no_ops_when_disabled()
    {
        var (db, storage) = BuildDeps();
        var seeder = BuildSeeder(db, storage, new ListeningSampleSeederOptions { Enabled = false });

        var created = await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(0, created);
        Assert.Equal(0, await db.ContentPapers.CountAsync());
        Assert.Equal(0, await db.MediaAssets.CountAsync());
    }

    [Fact]
    public async Task Seeder_no_ops_when_source_missing()
    {
        var (db, storage) = BuildDeps();
        var seeder = BuildSeeder(db, storage, new ListeningSampleSeederOptions
        {
            Enabled = true,
            SourceRoot = Path.Combine(Path.GetTempPath(), "absolutely-nonexistent-" + Guid.NewGuid().ToString("N")),
        });

        var created = await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(0, created);
        Assert.Equal(0, await db.ContentPapers.CountAsync());
        Assert.Equal(0, await db.MediaAssets.CountAsync());
    }

    [Fact]
    public async Task Seeder_is_idempotent_on_repeat_run()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(),
            "oet-listening-seeder-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Build a single fake "Listening Sample 1" folder with the four
            // expected files, all tiny dummy payloads.
            var sampleDir = Path.Combine(sourceRoot, "Listening Sample 1");
            var audioDir = Path.Combine(sampleDir, "Audio 1");
            Directory.CreateDirectory(audioDir);

            await File.WriteAllBytesAsync(
                Path.Combine(sampleDir, "Listening Sample 1 Question-Paper.pdf"),
                System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 fake question"));
            await File.WriteAllBytesAsync(
                Path.Combine(sampleDir, "Listening Sample 1 Audio-Script.pdf"),
                System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 fake script"));
            await File.WriteAllBytesAsync(
                Path.Combine(sampleDir, "Listening Sample 1 Answer-Key.pdf"),
                System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 fake answers"));
            await File.WriteAllBytesAsync(
                Path.Combine(audioDir, "Audio 1.mp3"),
                new byte[] { 0xFF, 0xFB, 0x00, 0x00, 0x01, 0x02, 0x03 });

            var (db, storage) = BuildDeps();
            var seeder = BuildSeeder(db, storage, new ListeningSampleSeederOptions
            {
                Enabled = true,
                SourceRoot = sourceRoot,
            });

            var firstRun = await seeder.SeedAsync(CancellationToken.None);
            Assert.Equal(1, firstRun);
            Assert.Equal(1, await db.ContentPapers.CountAsync());
            Assert.Equal(4, await db.MediaAssets.CountAsync());
            Assert.Equal(4, await db.ContentPaperAssets.CountAsync());

            var paper = await db.ContentPapers.FirstAsync();
            Assert.Equal("listening-sample-1-medicine", paper.Slug);
            Assert.Equal(ContentStatus.Draft, paper.Status);
            Assert.False(string.IsNullOrWhiteSpace(paper.SourceProvenance));

            var secondRun = await seeder.SeedAsync(CancellationToken.None);
            Assert.Equal(0, secondRun);
            Assert.Equal(1, await db.ContentPapers.CountAsync());
            Assert.Equal(4, await db.MediaAssets.CountAsync());
            Assert.Equal(4, await db.ContentPaperAssets.CountAsync());
        }
        finally
        {
            try { Directory.Delete(sourceRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
