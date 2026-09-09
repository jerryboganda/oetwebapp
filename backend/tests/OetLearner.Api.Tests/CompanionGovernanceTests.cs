using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Manifest 1.F — approval, release and rollback, and the official-fact layer's
/// staging rule.
///
/// <para>
/// The behaviour under test is mostly about what must <b>not</b> happen: an
/// unverified official fact must not be retrievable, a reindex must not undo a
/// human's sign-off, and a rollback must not destroy the evidence of what went
/// wrong. Each of those fails silently in the wrong direction — the corpus keeps
/// working and simply starts telling learners something nobody approved.
/// </para>
/// </summary>
public sealed class CompanionGovernanceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionGovernanceTests()
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

    // ── official facts are staged, not published ─────────────────────────────

    [Fact]
    public async Task Seeded_official_facts_are_pending_and_unretrievable()
    {
        // GC-002: with nothing verified, the correct answer to "what is the OET
        // fee in Egypt next month?" is "I don't have verified information",
        // not a plausible number. Staging is what makes that the honest answer
        // rather than an accident.
        await IndexOfficialFactsAsync();

        await using var db = new LearnerDbContext(_options);
        var source = await db.CompanionSources.SingleAsync();

        Assert.Equal(CompanionSourceState.PendingApproval, source.State);
        Assert.Equal(CompanionAuthorityClass.OfficialCurrentFact, source.AuthorityClass);
        Assert.Equal(CompanionOfficialFactsIndexer.VerificationSource, source.SourceUrl);
        Assert.Null(source.VerifiedByUserId);

        var result = await RetrieveAsync("how many sub-tests does OET have");
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task Indexing_warns_that_the_official_layer_is_not_live()
    {
        // An operator reading "6 chunks written" would reasonably assume the
        // facts are answering. They are not, and the warning has to say so.
        var result = await IndexOfficialFactsAsync();

        Assert.Contains(result.Warnings, w => w.Contains("PendingApproval", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Approval_makes_them_retrievable_and_records_who_verified_them()
    {
        await IndexOfficialFactsAsync();
        await ApproveAsync(CompanionOfficialFactsIndexer.SourceKey, "dr-hesham");

        await using var db = new LearnerDbContext(_options);
        var source = await db.CompanionSources.SingleAsync();

        Assert.Equal(CompanionSourceState.Approved, source.State);
        Assert.Equal("dr-hesham", source.ApprovedByUserId);

        // Approving an official fact IS the verification act. Recording it
        // separately is what answers "when was this last checked?" later.
        Assert.Equal("dr-hesham", source.VerifiedByUserId);
        Assert.NotNull(source.VerifiedAt);

        var result = await RetrieveAsync("how many sub-tests does OET have");
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public async Task A_later_reindex_does_not_undo_an_approval()
    {
        // Reindexing is routine. Silently reverting a sign-off because the seed
        // says "pending" would make approval meaningless and would pull verified
        // facts back out of the corpus with nobody asking for it.
        await IndexOfficialFactsAsync();
        await ApproveAsync(CompanionOfficialFactsIndexer.SourceKey, "dr-hesham");
        await IndexOfficialFactsAsync();

        await using var db = new LearnerDbContext(_options);
        var source = await db.CompanionSources.SingleAsync();

        Assert.Equal(CompanionSourceState.Approved, source.State);
    }

    [Fact]
    public async Task Official_facts_defer_to_the_regulator_rather_than_inventing_a_requirement()
    {
        // The single most consequential wrong answer this product could give is
        // "you need a B" to someone whose council wants something else.
        var text = string.Concat(CompanionOfficialFactsIndexer.BuildChunks().Select(c => c.Text));

        Assert.Contains("Never tell a candidate what score their regulator requires", text, StringComparison.Ordinal);
        Assert.Contains("official OET site", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Volatile_facts_are_deliberately_absent()
    {
        // Fees and dates change quarterly. Staging one would produce a
        // confidently stale answer, and "check the official site" is the better
        // one for the whole life of the source.
        var text = string.Concat(CompanionOfficialFactsIndexer.BuildChunks().Select(c => c.Text));

        Assert.DoesNotContain("USD", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("change over time", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── approval and retirement ──────────────────────────────────────────────

    [Fact]
    public async Task Approving_never_revives_a_superseded_version()
    {
        // Two live versions of one source is the silent-conflict failure the
        // supersede logic exists to prevent; approval must not reopen it.
        var v1 = await SeedSourceAsync("rulebook:writing:medicine", "v1", CompanionSourceState.Superseded);
        var v2 = await SeedSourceAsync("rulebook:writing:medicine", "v2", CompanionSourceState.Approved);

        await using (var setup = new LearnerDbContext(_options))
        {
            var old = await setup.CompanionSources.SingleAsync(s => s.Id == v1);
            old.SupersededBySourceId = v2;
            await setup.SaveChangesAsync();
        }

        await ApproveAsync("rulebook:writing:medicine", "admin-1");

        await using var db = new LearnerDbContext(_options);
        var superseded = await db.CompanionSources.SingleAsync(s => s.Id == v1);

        Assert.Equal(CompanionSourceState.Superseded, superseded.State);
    }

    [Fact]
    public async Task Retiring_a_source_takes_it_out_of_retrieval()
    {
        await IndexOfficialFactsAsync();
        await ApproveAsync(CompanionOfficialFactsIndexer.SourceKey, "dr-hesham");
        Assert.NotEmpty((await RetrieveAsync("how many sub-tests does OET have")).Evidence);

        await using (var db = new LearnerDbContext(_options))
        {
            await Governance(db).RetireAsync(CompanionOfficialFactsIndexer.SourceKey, null, "admin-1", CancellationToken.None);
        }

        Assert.Empty((await RetrieveAsync("how many sub-tests does OET have")).Evidence);
    }

    [Fact]
    public async Task Approving_an_unknown_source_is_reported_not_swallowed()
    {
        await using var db = new LearnerDbContext(_options);
        var result = await Governance(db).ApproveAsync("nope", null, "admin-1", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("not_found", result.Reason);
    }

    // ── releases and rollback ────────────────────────────────────────────────

    [Fact]
    public async Task Publishing_stamps_the_live_chunks_with_the_release()
    {
        await IndexOfficialFactsAsync();
        await ApproveAsync(CompanionOfficialFactsIndexer.SourceKey, "dr-hesham");

        await using var db = new LearnerDbContext(_options);
        var release = await Governance(db).PublishReleaseAsync("2026.09.1", "dr-hesham", "first", CancellationToken.None);

        Assert.Equal("published", release.Status);
        Assert.True(release.ChunkCount > 0);
        Assert.NotNull(release.IdOrNull());

        await using var verify = new LearnerDbContext(_options);
        Assert.True(await verify.CompanionChunks.AllAsync(c => c.ReleaseId == release.Id));
    }

    [Fact]
    public async Task The_first_release_has_nothing_to_roll_back_to()
    {
        await IndexOfficialFactsAsync();
        await ApproveAsync(CompanionOfficialFactsIndexer.SourceKey, "dr-hesham");

        await using var db = new LearnerDbContext(_options);
        await Governance(db).PublishReleaseAsync("2026.09.1", "dr-hesham", null, CancellationToken.None);

        await using var second = new LearnerDbContext(_options);
        Assert.Null(await Governance(second).RollbackAsync("admin-1", CancellationToken.None));
    }

    [Fact]
    public async Task Rollback_restores_the_previous_release_and_stages_what_the_bad_one_added()
    {
        await IndexOfficialFactsAsync();
        await ApproveAsync(CompanionOfficialFactsIndexer.SourceKey, "dr-hesham");

        await using (var db = new LearnerDbContext(_options))
        {
            await Governance(db).PublishReleaseAsync("2026.09.1", "dr-hesham", "good", CancellationToken.None);
        }

        // A second source arrives and is published — this is the bad release.
        await SeedSourceAsync("rulebook:writing:medicine", "v1", CompanionSourceState.Approved, withChunk: true);

        await using (var db = new LearnerDbContext(_options))
        {
            await Governance(db).PublishReleaseAsync("2026.09.2", "dr-hesham", "regrettable", CancellationToken.None);
        }

        await using (var db = new LearnerDbContext(_options))
        {
            var restored = await Governance(db).RollbackAsync("admin-1", CancellationToken.None);
            Assert.NotNull(restored);
            Assert.Equal("2026.09.1", restored!.ReleaseVersion);
        }

        await using var verify = new LearnerDbContext(_options);

        // What the bad release introduced is staged, not deleted: whatever was
        // wrong with it still needs looking at.
        var introduced = await verify.CompanionSources.SingleAsync(s => s.SourceKey == "rulebook:writing:medicine");
        Assert.Equal(CompanionSourceState.PendingApproval, introduced.State);

        // ...and the bad release itself stays on record as rolled back.
        var bad = await verify.CompanionKnowledgeReleases.SingleAsync(r => r.ReleaseVersion == "2026.09.2");
        Assert.Equal("rolled_back", bad.Status);
    }

    // ── asset register ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_register_lists_what_is_present_and_what_is_deliberately_absent()
    {
        await IndexOfficialFactsAsync();

        await using var db = new LearnerDbContext(_options);
        var register = await Governance(db).BuildAssetRegisterAsync(CancellationToken.None);

        Assert.Contains(register.Ingested, e => e.SourceKey == CompanionOfficialFactsIndexer.SourceKey);

        // The half that makes the register checkable against the Manifest: an
        // exclusion with a reason can be disagreed with; a silent omission
        // cannot even be found.
        Assert.Contains(register.NotIngested, e => e.Asset.Contains("Testing PDF", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(register.NotIngested, e => e.Asset.Contains("Tutor Book", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(register.NotIngested, e => e.Asset.Contains("video", StringComparison.OrdinalIgnoreCase));
        Assert.All(register.NotIngested, e => Assert.False(string.IsNullOrWhiteSpace(e.Reason)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static CompanionKnowledgeGovernance Governance(LearnerDbContext db) =>
        new(db, NullLogger<CompanionKnowledgeGovernance>.Instance);

    private async Task<CompanionIndexResult> IndexOfficialFactsAsync()
    {
        await using var db = new LearnerDbContext(_options);
        return await new CompanionOfficialFactsIndexer(
                db, new UnusedEmbeddings(), NullLogger<CompanionOfficialFactsIndexer>.Instance)
            .IndexAsync(embed: false, CancellationToken.None);
    }

    private async Task ApproveAsync(string sourceKey, string approver)
    {
        await using var db = new LearnerDbContext(_options);
        await Governance(db).ApproveAsync(sourceKey, null, approver, CancellationToken.None);
    }

    private async Task<Guid> SeedSourceAsync(
        string sourceKey,
        string version,
        CompanionSourceState state,
        bool withChunk = false)
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;

        var source = new CompanionSource
        {
            Id = Guid.NewGuid(),
            SourceKey = sourceKey,
            Version = version,
            SourceType = "rulebook",
            Title = "Writing rulebook — Medicine",
            AuthorityClass = CompanionAuthorityClass.ProfessionApprovedMethod,
            State = state,
            ExamTypeCode = "OET",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.CompanionSources.Add(source);

        if (withChunk)
        {
            db.CompanionChunks.Add(new CompanionChunk
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                Ordinal = 0,
                Heading = "W1.1",
                Text = "Sign off with your full name and role.",
                ContentHash = "hash",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task<CompanionRetrievalResult> RetrieveAsync(string query)
    {
        await using var db = new LearnerDbContext(_options);
        var retriever = new CompanionRetriever(
            db, new UnusedEmbeddings(),
            new CompanionExtractionBudget(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<CompanionRetriever>.Instance);

        var context = new CompanionTurnContext
        {
            UserId = "learner-1",
            ProfessionId = "medicine",
            ExamTypeCode = "OET",
            RetrievalEnabled = true,
        };

        return await retriever.RetrieveAsync(query, context, maxResults: 8, CancellationToken.None);
    }

    private sealed class UnusedEmbeddings : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
            throw new InvalidOperationException("vector path must not run on SQLite");

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            throw new InvalidOperationException("vector path must not run on SQLite");
    }
}

internal static class CompanionReleaseTestExtensions
{
    /// <summary>Readability shim so the assertion reads as an intent, not a cast.</summary>
    public static Guid? IdOrNull(this CompanionReleaseSummary summary) =>
        summary.Id == Guid.Empty ? null : summary.Id;
}
