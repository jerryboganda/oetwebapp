using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Speaking;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The approved Speaking card taxonomy has to be retrievable, and it has to stay
/// in step with the classifier that actually labels the catalogue.
///
/// <para>
/// Four acceptance scenarios ride on this — Testing Pack 1 scenarios 12, 21, 22
/// and 23 — and all four fail the same way if it drifts: the companion answers
/// confidently from a taxonomy the platform no longer uses, which scores zero as
/// an invented Dr Hesham rule rather than as a near miss.
/// </para>
/// </summary>
public sealed class CompanionSpeakingTaxonomyIndexerTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionSpeakingTaxonomyIndexerTests()
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

    // ── anti-drift ───────────────────────────────────────────────────────────

    [Fact]
    public void Every_classifier_category_has_candidate_guidance()
    {
        // This is the test that makes generating-from-the-classifier worth doing.
        // Add a tenth category to SpeakingCardClassifier and this fails here,
        // rather than silently shipping a companion that cannot explain it.
        var chunkText = string.Concat(
            CompanionSpeakingTaxonomyIndexer.BuildChunks().Select(c => c.Heading + "\n" + c.Text));

        foreach (var category in SpeakingCardClassifier.PrimaryCategories)
        {
            Assert.Contains(category, chunkText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_priority_order_is_taught_not_just_the_list()
    {
        // Pack 1 scenario 21: an angry patient on a ward is an Already Known
        // Patient tagged Angry. Knowing the nine names is not enough to get that
        // right — the candidate needs the tie-break rule.
        var chunks = CompanionSpeakingTaxonomyIndexer.BuildChunks().ToList();

        var priority = Assert.Single(chunks, c => c.Heading.Contains("priority order", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("encounter type wins over the patient's behaviour", priority.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_examination_wording_nuance_is_explicit()
    {
        // Pack 1 scenario 23 tests exactly this and nothing else: the card says
        // "after examination you find…", which must NOT force an Examination Card.
        var chunks = CompanionSpeakingTaxonomyIndexer.BuildChunks().ToList();

        var rule = Assert.Single(chunks, c => c.Heading.Contains("wording rule", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("after examination you find", rule.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is NOT enough", rule.Text, StringComparison.Ordinal);
    }

    // ── indexing behaviour ───────────────────────────────────────────────────

    [Fact]
    public async Task Indexing_publishes_an_approved_retrievable_source()
    {
        var result = await IndexAsync();

        Assert.Equal(1, result.SourcesWritten);
        Assert.True(result.ChunksWritten > SpeakingCardClassifier.PrimaryCategories.Length);
        Assert.Empty(result.Warnings);

        await using var db = new LearnerDbContext(_options);
        var source = await db.CompanionSources.SingleAsync();

        Assert.Equal(CompanionSourceState.Approved, source.State);
        // Teaching method, not an official exam fact — the distinction the packs
        // repeatedly ask the companion to keep straight.
        Assert.Equal(CompanionAuthorityClass.DrHeshamApprovedMethod, source.AuthorityClass);
        Assert.Equal("speaking", source.SubtestCode);
        // Every profession shares the encounter categories.
        Assert.Null(source.ProfessionId);
        // Candidate-facing reference: already published in-app, so it is not
        // rationed by the proprietary extraction budget.
        Assert.False(source.IsProprietary);
        Assert.Equal(SpeakingCardClassifier.ClassifierVersion, source.Version);
    }

    [Fact]
    public async Task Reindexing_is_idempotent()
    {
        await IndexAsync();
        var second = await IndexAsync();

        Assert.Equal(0, second.SourcesWritten);
        Assert.Equal(0, second.ChunksWritten);
        Assert.True(second.ChunksUnchanged > 0);
    }

    [Fact]
    public async Task A_learner_can_actually_retrieve_the_examination_rule()
    {
        // The end-to-end point of the whole exercise: indexed is not the same as
        // retrievable, and only retrievable answers a candidate.
        await IndexAsync();

        var result = await RetrieveAsync("should I use the examination card opening");

        Assert.NotEmpty(result.Evidence);
        Assert.Contains(
            result.Evidence,
            e => e.Text.Contains("after examination you find", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_taxonomy_reaches_a_learner_of_any_profession()
    {
        await IndexAsync();

        foreach (var profession in new[] { "medicine", "nursing", "pharmacy" })
        {
            var result = await RetrieveAsync("what type of speaking card is this", profession);
            Assert.NotEmpty(result.Evidence);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<CompanionIndexResult> IndexAsync()
    {
        await using var db = new LearnerDbContext(_options);
        var indexer = new CompanionSpeakingTaxonomyIndexer(
            db,
            new UnusedEmbeddings(),
            NullLogger<CompanionSpeakingTaxonomyIndexer>.Instance);

        // embed: false — SQLite has no pgvector, and the keyword path is the
        // stricter test anyway.
        return await indexer.IndexAsync(embed: false, CancellationToken.None);
    }

    private async Task<CompanionRetrievalResult> RetrieveAsync(
        string query,
        string profession = "medicine")
    {
        await using var db = new LearnerDbContext(_options);
        var retriever = new CompanionRetriever(
            db,
            new UnusedEmbeddings(),
            new CompanionExtractionBudget(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<CompanionRetriever>.Instance);

        var context = new CompanionTurnContext
        {
            UserId = "learner-1",
            ProfessionId = profession,
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
