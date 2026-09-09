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
/// Golden case GC-006 and Testing Pack 4 scenario 22 — multi-turn proprietary
/// reconstruction.
///
/// <para>
/// The per-turn caps in <see cref="CompanionRetriever"/> stop a single "print
/// the rulebook" request. They do nothing about the attack the packs actually
/// describe: ask for section 1, then "the next section", then "keep going".
/// Chunks are one per rule, so each turn is individually reasonable and the book
/// still walks out of the door. These tests pin the rolling window that closes
/// that.
/// </para>
/// </summary>
public sealed class CompanionExtractionBudgetTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly Guid _sourceId = Guid.NewGuid();

    public CompanionExtractionBudgetTests()
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

    // ── The budget itself ────────────────────────────────────────────────────

    [Fact]
    public void A_fresh_learner_has_the_full_window()
    {
        var budget = NewBudget();

        Assert.Equal(
            CompanionExtractionBudget.WindowChars,
            budget.RemainingChars("learner-1", _sourceId));
    }

    [Fact]
    public void Consumption_accumulates_across_calls()
    {
        var budget = NewBudget();

        budget.Consume("learner-1", _sourceId, 1000);
        budget.Consume("learner-1", _sourceId, 1000);

        Assert.Equal(
            CompanionExtractionBudget.WindowChars - 2000,
            budget.RemainingChars("learner-1", _sourceId));
    }

    [Fact]
    public void The_window_never_goes_negative()
    {
        var budget = NewBudget();

        budget.Consume("learner-1", _sourceId, CompanionExtractionBudget.WindowChars * 10);

        Assert.Equal(0, budget.RemainingChars("learner-1", _sourceId));
    }

    [Fact]
    public void One_learner_cannot_spend_the_allowance_of_another()
    {
        var budget = NewBudget();

        budget.Consume("learner-1", _sourceId, CompanionExtractionBudget.WindowChars);

        Assert.Equal(0, budget.RemainingChars("learner-1", _sourceId));
        Assert.Equal(
            CompanionExtractionBudget.WindowChars,
            budget.RemainingChars("learner-2", _sourceId));
    }

    [Fact]
    public void Each_source_carries_its_own_allowance()
    {
        // The thing being protected is a document. Studying the writing rulebook
        // hard must not lock a learner out of the speaking one.
        var budget = NewBudget();
        var other = Guid.NewGuid();

        budget.Consume("learner-1", _sourceId, CompanionExtractionBudget.WindowChars);

        Assert.Equal(0, budget.RemainingChars("learner-1", _sourceId));
        Assert.Equal(CompanionExtractionBudget.WindowChars, budget.RemainingChars("learner-1", other));
    }

    // ── The retriever honouring it ───────────────────────────────────────────

    [Fact]
    public async Task A_chapter_walk_runs_dry_within_a_few_turns()
    {
        // 40 rules, each individually small enough to clear every per-turn cap.
        await SeedRulebookAsync(ruleCount: 40, charsPerRule: 900);

        var budget = NewBudget();
        var context = Context();

        var firstTurn = await Retrieve(budget, context, "referral letter rule");
        Assert.NotEmpty(firstTurn.Evidence);

        // Walk the book the way the pack describes.
        var productiveTurns = 1;
        for (var turn = 2; turn <= 12; turn++)
        {
            var result = await Retrieve(budget, context, "referral letter rule");
            if (result.Evidence.Count == 0) break;
            productiveTurns++;
        }

        Assert.True(
            productiveTurns < 12,
            $"the rolling cap never tripped: {productiveTurns} turns still returned proprietary text.");
    }

    [Fact]
    public async Task Exhaustion_is_reported_so_the_prompt_can_explain_itself()
    {
        await SeedRulebookAsync(ruleCount: 40, charsPerRule: 900);

        var budget = NewBudget();
        var context = Context();
        budget.Consume(context.UserId, _sourceId, CompanionExtractionBudget.WindowChars);

        var result = await Retrieve(budget, context, "referral letter rule");

        // Truncated drives the "material was withheld" line in the composed
        // prompt, so the learner is told rather than silently given less.
        Assert.True(result.Truncated);
        Assert.Contains(result.Trace, t => t.StartsWith("extraction.budget_exhausted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Non_proprietary_sources_are_not_rationed()
    {
        // Official facts and platform/support knowledge must stay answerable for
        // as long as the learner keeps asking. The cap protects paid teaching
        // material, not the navigation and policy answers the product owes them.
        await SeedRulebookAsync(ruleCount: 40, charsPerRule: 900, proprietary: false);

        var budget = NewBudget();
        var context = Context();
        budget.Consume(context.UserId, _sourceId, CompanionExtractionBudget.WindowChars * 5);

        var result = await Retrieve(budget, context, "referral letter rule");

        Assert.NotEmpty(result.Evidence);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static CompanionExtractionBudget NewBudget() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    private static CompanionTurnContext Context() => new()
    {
        UserId = "learner-1",
        ProfessionId = "medicine",
        ExamTypeCode = "OET",
        RetrievalEnabled = true,
    };

    private async Task<CompanionRetrievalResult> Retrieve(
        ICompanionExtractionBudget budget,
        CompanionTurnContext context,
        string query)
    {
        await using var db = new LearnerDbContext(_options);
        var retriever = new CompanionRetriever(
            db,
            new UnusedEmbeddings(),
            budget,
            NullLogger<CompanionRetriever>.Instance);

        return await retriever.RetrieveAsync(query, context, maxResults: 8, CancellationToken.None);
    }

    private async Task SeedRulebookAsync(
        int ruleCount,
        int charsPerRule,
        bool proprietary = true)
    {
        await using var db = new LearnerDbContext(_options);

        db.CompanionSources.Add(new CompanionSource
        {
            Id = _sourceId,
            SourceKey = "rulebook:writing:medicine",
            SourceType = "rulebook",
            Title = "Writing rulebook — Medicine",
            AuthorityClass = CompanionAuthorityClass.ProfessionApprovedMethod,
            State = CompanionSourceState.Approved,
            ExamTypeCode = "OET",
            ProfessionId = "medicine",
            SubtestCode = "writing",
            IsProprietary = proprietary,
            RequiredEntitlementScope = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        for (var i = 0; i < ruleCount; i++)
        {
            db.CompanionChunks.Add(new CompanionChunk
            {
                Id = Guid.NewGuid(),
                SourceId = _sourceId,
                Ordinal = i,
                Heading = $"W{i:D3} — referral letter rule {i}",
                // Every chunk matches the query, so ranking never starves the walk.
                Text = "referral letter rule " + new string('x', charsPerRule),
                ContentHash = $"hash-{i}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>SQLite has no pgvector, so the vector path must never be reached.</summary>
    private sealed class UnusedEmbeddings : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
            throw new InvalidOperationException("vector path must not run on SQLite");

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            throw new InvalidOperationException("vector path must not run on SQLite");
    }
}
