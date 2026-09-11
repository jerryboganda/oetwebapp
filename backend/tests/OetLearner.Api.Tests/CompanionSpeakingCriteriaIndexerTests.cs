using System.Text.Json;
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
/// Testing Pack 1 scenario 24 — "Show me the approved Speaking Intro Questions
/// and explain the Speaking Assessment Criteria used in our course. If you
/// cannot retrieve the approved set, do not invent one."
///
/// <para>
/// The scenario is really two assertions in one: the approved set must be
/// retrievable, AND it must be the same set the product shows on
/// /speaking/assessment-criteria. These tests read the real shipped JSON rather
/// than a fixture, because a fixture would pass happily while the actual file
/// was malformed or unlinked from the build.
/// </para>
/// </summary>
public sealed class CompanionSpeakingCriteriaIndexerTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionSpeakingCriteriaIndexerTests()
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

    // ── the shipped data ─────────────────────────────────────────────────────

    [Fact]
    public void The_shipped_resource_file_is_present_and_complete()
    {
        // Guards the csproj link as much as the content: drop the <None Include>
        // and the file silently stops reaching the build output, at which point
        // scenario 24 fails with a refusal that looks like correct behaviour.
        var resources = LoadShipped();

        Assert.Equal(4, resources.LinguisticCriteria.Count);
        Assert.Equal(5, resources.ClinicalCriteria.Count);
        Assert.Equal(12, resources.IntroQuestions.Count);
    }

    [Fact]
    public void Criteria_carry_the_scales_the_product_advertises()
    {
        var resources = LoadShipped();

        Assert.All(resources.LinguisticCriteria, c => Assert.Equal(6, c.MaxBand));
        Assert.All(resources.ClinicalCriteria, c => Assert.Equal(3, c.MaxScore));

        // A–E, in order: the letters are how the criteria are referred to in
        // feedback, so a gap here would make Sami's explanations unmatchable to
        // the learner's own report.
        Assert.Equal(
            new[] { "A", "B", "C", "D", "E" },
            resources.ClinicalCriteria.Select(c => c.Letter).ToArray());
    }

    // ── indexing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Indexing_publishes_one_chunk_per_criterion_plus_overview_and_intro()
    {
        var resources = LoadShipped();
        var expected = 1 + resources.LinguisticCriteria.Count + resources.ClinicalCriteria.Count + 1;

        var result = await IndexAsync();

        Assert.Equal(1, result.SourcesWritten);
        Assert.Equal(expected, result.ChunksWritten);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task The_source_is_approved_teaching_method_not_an_official_fact()
    {
        // The packs repeatedly test that Sami keeps official OET facts separate
        // from Dr Hesham's course material. The course's own criteria page is
        // the latter, and mislabelling it would make the distinction meaningless.
        await IndexAsync();

        await using var db = new LearnerDbContext(_options);
        var source = await db.CompanionSources.SingleAsync();

        Assert.Equal(CompanionAuthorityClass.DrHeshamApprovedMethod, source.AuthorityClass);
        Assert.Equal(CompanionSourceState.Approved, source.State);
        Assert.Equal("speaking", source.SubtestCode);
        Assert.Null(source.ProfessionId);
        Assert.False(source.IsProprietary);
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

    // ── retrieval, which is the only thing scenario 24 actually observes ─────

    [Fact]
    public async Task A_learner_can_retrieve_the_intro_questions()
    {
        await IndexAsync();

        var result = await RetrieveAsync("show me the speaking intro questions");

        Assert.NotEmpty(result.Evidence);
        Assert.Contains(
            result.Evidence,
            e => e.Text.Contains("What is your name?", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_learner_can_retrieve_an_individual_criterion()
    {
        await IndexAsync();

        var result = await RetrieveAsync("explain the intelligibility criterion");

        Assert.NotEmpty(result.Evidence);
        Assert.Contains(
            result.Evidence,
            e => e.Heading is not null && e.Heading.Contains("Intelligibility", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_intro_answers_are_presented_as_material_to_adapt()
    {
        // The page itself is emphatic that these are "sample answers to
        // personalise, not memorise". A companion that hands them over as a
        // script would coach candidates into an obviously rehearsed warm-up.
        await IndexAsync();

        var result = await RetrieveAsync("speaking intro questions sample answers");

        Assert.Contains(
            result.Evidence,
            e => e.Text.Contains("PERSONALISE", StringComparison.OrdinalIgnoreCase)
                 || e.Text.Contains("not to be memorised", StringComparison.OrdinalIgnoreCase));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static SpeakingCandidateResources LoadShipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, CompanionSpeakingCriteriaIndexer.RelativePath);
        Assert.True(File.Exists(path), $"shipped resource file missing from build output: {path}");

        var parsed = JsonSerializer.Deserialize<SpeakingCandidateResources>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(parsed);
        return parsed!;
    }

    private async Task<CompanionIndexResult> IndexAsync()
    {
        await using var db = new LearnerDbContext(_options);
        var indexer = new CompanionSpeakingCriteriaIndexer(
            db,
            new UnusedEmbeddings(),
            NullLogger<CompanionSpeakingCriteriaIndexer>.Instance);

        return await indexer.IndexAsync(embed: false, CancellationToken.None);
    }

    private async Task<CompanionRetrievalResult> RetrieveAsync(string query)
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
