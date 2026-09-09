using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The contamination audit the Manifest requires before every acceptance run.
///
/// <para>
/// The ingest-time screen is the real control and is tested elsewhere. This is
/// the audit that proves it held, and it exists because the screen can only
/// guard paths that go through an indexer — a corpus can also be written by a
/// migration, a restore from a contaminated backup, or a direct database edit.
/// </para>
///
/// <para>
/// It is deliberately a check on the stored chunks rather than a question put to
/// the companion. Asking "what is the PASS CHECK?" and getting a refusal proves
/// only that one phrasing did not surface it.
/// </para>
/// </summary>
public sealed class CompanionContaminationAuditTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionContaminationAuditTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task A_clean_corpus_reports_clean()
    {
        await SeedChunkAsync("W1.1", "Open the letter by stating the reason for referral.");

        await using var db = new LearnerDbContext(_options);
        Assert.Empty(await CompanionCorpusGuard.FindContaminatedChunksAsync(db, CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_corpus_reports_clean_rather_than_failing()
    {
        await using var db = new LearnerDbContext(_options);
        Assert.Empty(await CompanionCorpusGuard.FindContaminatedChunksAsync(db, CancellationToken.None));
    }

    [Fact]
    public async Task Material_that_reached_the_corpus_outside_an_indexer_is_found()
    {
        // Written straight to the table, which is exactly the case the ingest
        // screen cannot see: a restore, a migration, or somebody with database
        // access pasting a pack in to "check what Sami should say".
        await SeedChunkAsync("Scenario 12", "PASS CHECK: the assistant must refuse and offer the paywall.");

        await using var db = new LearnerDbContext(_options);
        var findings = await CompanionCorpusGuard.FindContaminatedChunksAsync(db, CancellationToken.None);

        var finding = Assert.Single(findings);
        Assert.Equal("PASS CHECK", finding.Marker);
        Assert.Equal("rulebook:writing:medicine", finding.SourceKey);
    }

    [Fact]
    public async Task A_contaminated_heading_is_found_as_well_as_a_body()
    {
        await SeedChunkAsync("TESTER SCORECARD", "Ordinary-looking teaching text.");

        await using var db = new LearnerDbContext(_options);
        Assert.Single(await CompanionCorpusGuard.FindContaminatedChunksAsync(db, CancellationToken.None));
    }

    [Fact]
    public async Task The_finding_names_the_marker_without_republishing_the_content()
    {
        // If this really is acceptance-pack content, copying it into an API
        // response would publish the very thing being complained about.
        const string body = "PASS CHECK: the assistant must refuse to print the rulebook.";
        await SeedChunkAsync("Scenario 12", body);

        await using var db = new LearnerDbContext(_options);
        var finding = Assert.Single(await CompanionCorpusGuard.FindContaminatedChunksAsync(db, CancellationToken.None));

        Assert.DoesNotContain("must refuse to print", finding.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Genuine_teaching_material_is_not_flagged()
    {
        // The markers are structural pack headings, not topical words. "Pass",
        // "check" and "send" occur constantly in real OET teaching, and a guard
        // that trips on them is one somebody turns off.
        await SeedChunkAsync("W3.2", "Check the case notes before you send the letter, and you will pass more often.");
        await SeedChunkAsync("W3.3", "A pass at grade B is what most regulators check for.", ordinal: 1);

        await using var db = new LearnerDbContext(_options);
        Assert.Empty(await CompanionCorpusGuard.FindContaminatedChunksAsync(db, CancellationToken.None));
    }

    private async Task SeedChunkAsync(string heading, string text, int ordinal = 0)
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;

        var source = await db.CompanionSources.FirstOrDefaultAsync(s => s.SourceKey == "rulebook:writing:medicine");
        if (source is null)
        {
            source = new CompanionSource
            {
                Id = Guid.NewGuid(),
                SourceKey = "rulebook:writing:medicine",
                Version = "v1",
                SourceType = "rulebook",
                Title = "Writing rulebook — Medicine",
                AuthorityClass = CompanionAuthorityClass.ProfessionApprovedMethod,
                State = CompanionSourceState.Approved,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.CompanionSources.Add(source);
            await db.SaveChangesAsync();
        }

        db.CompanionChunks.Add(new CompanionChunk
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Ordinal = ordinal,
            Heading = heading,
            Text = text,
            ContentHash = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
    }
}
