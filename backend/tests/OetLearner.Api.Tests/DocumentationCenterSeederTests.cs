using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Seeding;
using OetLearner.Api.Services.Seeding.DocumentationContent;

namespace OetLearner.Api.Tests;

/// <summary>
/// Verifies <see cref="DocumentationCenterSeeder"/> revision handling: a seed whose
/// <see cref="DocumentationModuleSeed.Revision"/> is above the stored version is
/// published as a new current version (old one Superseded, evidence synced), and
/// re-runs / higher stored versions are left alone.
/// </summary>
public sealed class DocumentationCenterSeederTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public DocumentationCenterSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task FreshDatabase_InsertsEveryModuleAtItsSeedRevision()
    {
        await RunSeederAsync();

        await using var db = new LearnerDbContext(_options);
        var seeds = DocumentationContentCatalog.GetAll();
        Assert.Equal(seeds.Count, await db.DocumentationModules.CountAsync());
        foreach (var seed in seeds)
        {
            var current = await db.DocumentationVersions.SingleAsync(v => v.ModuleId == seed.Code && v.IsCurrent);
            Assert.Equal(seed.Revision, current.VersionNumber);
        }
        Assert.Equal(seeds.Sum(s => s.Evidence.Count), await db.DocumentationEvidenceItems.CountAsync());
    }

    [Fact]
    public async Task StaleModule_IsSupersededAndEvidenceSynced_ThenIdempotent()
    {
        var doc10 = DocumentationContentCatalog.GetAll().Single(s => s.Code == "DOC-10");
        Assert.True(doc10.Revision > 1);

        await using (var seed = new LearnerDbContext(_options))
        {
            seed.DocumentationModules.Add(new DocumentationModule { Id = "DOC-10", Title = "old", Description = "old", SortOrder = 10 });
            seed.DocumentationVersions.Add(new DocumentationVersion
            {
                Id = "DOCVER-old", ModuleId = "DOC-10", VersionNumber = 1, Status = DocumentationVersionStatus.Published,
                ContentJson = "[]", GeneratedAt = DateTimeOffset.UtcNow, IsCurrent = true,
            });
            seed.DocumentationEvidenceItems.Add(new DocumentationEvidenceItem
            {
                Id = "DOCEV-019", EvidenceId = "EV-PLATFORM-019", ModuleId = "DOC-10", EvidenceType = DocumentationEvidenceType.Deployment,
                Description = "stale", SourceReference = "stale", CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.DocumentationEvidenceItems.Add(new DocumentationEvidenceItem
            {
                Id = "DOCEV-006", EvidenceId = "EV-PLATFORM-006", ModuleId = "DOC-10", EvidenceType = DocumentationEvidenceType.DataKnowledge,
                Description = "stale", SourceReference = "stale", IsInternalOnly = false, CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.DocumentationEvidenceItems.Add(new DocumentationEvidenceItem
            {
                Id = "DOCEV-999", EvidenceId = "EV-PLATFORM-999", ModuleId = "DOC-10", EvidenceType = DocumentationEvidenceType.Code,
                Description = "retired", SourceReference = "retired", CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await RunSeederAsync();
        await RunSeederAsync(); // second run must be a no-op

        await using var db = new LearnerDbContext(_options);
        var versions = await db.DocumentationVersions.Where(v => v.ModuleId == "DOC-10").OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.False(versions[0].IsCurrent);
        Assert.Equal(DocumentationVersionStatus.Superseded, versions[0].Status);
        Assert.True(versions[1].IsCurrent);
        Assert.Equal(doc10.Revision, versions[1].VersionNumber);
        Assert.Contains("docker-compose.agent-console.yml", versions[1].ContentJson);
        Assert.DoesNotContain("prebuilt-web", versions[1].ContentJson);
        Assert.Equal(doc10.Title, (await db.DocumentationModules.SingleAsync(m => m.Id == "DOC-10")).Title);

        var evidence = await db.DocumentationEvidenceItems.Where(e => e.ModuleId == "DOC-10").ToListAsync();
        Assert.Equal(doc10.Evidence.Count, evidence.Count);
        Assert.DoesNotContain(evidence, e => e.EvidenceId == "EV-PLATFORM-999");
        var ev19 = evidence.Single(e => e.EvidenceId == "EV-PLATFORM-019");
        Assert.Equal("DOCEV-019", ev19.Id); // updated in place, not re-inserted
        Assert.Equal(doc10.Evidence.Single(e => e.EvidenceId == "EV-PLATFORM-019").SourceReference, ev19.SourceReference);
        // Evidence pointing at the local-only agent-state file must drop out of External exports.
        Assert.True(evidence.Single(e => e.EvidenceId == "EV-PLATFORM-006").IsInternalOnly);
    }

    [Fact]
    public async Task HigherStoredVersion_IsNeverTouched()
    {
        await using (var seed = new LearnerDbContext(_options))
        {
            seed.DocumentationModules.Add(new DocumentationModule { Id = "DOC-10", Title = "edited", Description = "edited", SortOrder = 10 });
            seed.DocumentationVersions.Add(new DocumentationVersion
            {
                Id = "DOCVER-edited", ModuleId = "DOC-10", VersionNumber = 99, Status = DocumentationVersionStatus.Published,
                ContentJson = "[]", GeneratedAt = DateTimeOffset.UtcNow, IsCurrent = true,
            });
            await seed.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using var db = new LearnerDbContext(_options);
        var current = await db.DocumentationVersions.SingleAsync(v => v.ModuleId == "DOC-10");
        Assert.Equal("DOCVER-edited", current.Id);
        Assert.True(current.IsCurrent);
        Assert.Equal("edited", (await db.DocumentationModules.SingleAsync(m => m.Id == "DOC-10")).Title);
    }

    private async Task RunSeederAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped(sp => new LearnerDbContext(sp.GetRequiredService<DbContextOptions<LearnerDbContext>>()));
        var seeder = new DocumentationCenterSeeder(services.BuildServiceProvider(), NullLogger<DocumentationCenterSeeder>.Instance);
        await seeder.StartAsync(default);
    }
}
