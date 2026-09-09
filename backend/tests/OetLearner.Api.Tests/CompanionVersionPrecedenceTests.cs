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
/// Manifest §4 — "newer approved versions override obsolete versions; old
/// versions remain auditable but not current", and the effective date is
/// resolved against the learner's exam.
///
/// <para>
/// Both of these fail silently rather than loudly, which is why they are pinned
/// here. A duplicated version does not throw: the retriever simply hands the
/// model two contradictory rules and the model reconciles them itself, inventing
/// exactly the compromise answer Testing Pack 1 scenario 19 says it must never
/// produce. Nothing in a chunk count would show it.
/// </para>
/// </summary>
public sealed class CompanionVersionPrecedenceTests : IAsyncDisposable
{
    private const string SourceKey = "rulebook:writing:medicine";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionVersionPrecedenceTests()
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
    public async Task A_superseded_version_is_not_retrievable_but_is_still_on_record()
    {
        var v1 = await SeedVersionAsync("v1", "Sign off with your full name only.");
        var v2 = await SeedVersionAsync("v2", "Sign off with your full name and role.");
        await SupersedeAsync(older: v1, newer: v2);

        var result = await RetrieveAsync("sign off");

        // The learner sees one rule, not two contradictory ones.
        Assert.Single(result.Evidence);
        Assert.Contains("full name and role", result.Evidence[0].Text, StringComparison.Ordinal);

        // ...and the old version is retained for audit rather than deleted.
        await using var db = new LearnerDbContext(_options);
        var stored = await db.CompanionSources.SingleAsync(s => s.Id == v1);
        Assert.Equal(CompanionSourceState.Superseded, stored.State);
        Assert.Equal(v2, stored.SupersededBySourceId);
    }

    [Fact]
    public async Task Two_live_versions_of_one_book_would_contradict_each_other()
    {
        // The regression this guards: before supersede was wired in, a version
        // bump left both rows Approved and unsuperseded, and both reached the
        // prompt together.
        await SeedVersionAsync("v1", "Sign off with your full name only.");
        await SeedVersionAsync("v2", "Sign off with your full name and role.");

        var result = await RetrieveAsync("sign off");

        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public async Task Effectivity_follows_the_learners_exam_date_not_today()
    {
        // A rule that takes effect in two months is the correct answer for a
        // candidate sitting in three, and the wrong one for a candidate sitting
        // next week. Same corpus, same question, two different right answers.
        var soon = DateTimeOffset.UtcNow.AddMonths(2);
        await SeedVersionAsync("v1", "Current sign-off rule.", effectiveTo: soon);
        await SeedVersionAsync("v2", "Future sign-off rule.", effectiveFrom: soon);

        var beforeChange = await RetrieveAsync(
            "sign off",
            examDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));
        Assert.Single(beforeChange.Evidence);
        Assert.Contains("Current sign-off rule", beforeChange.Evidence[0].Text, StringComparison.Ordinal);

        var afterChange = await RetrieveAsync(
            "sign off",
            examDate: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)));
        Assert.Single(afterChange.Evidence);
        Assert.Contains("Future sign-off rule", afterChange.Evidence[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_exam_date_the_current_rule_is_used()
    {
        var soon = DateTimeOffset.UtcNow.AddMonths(2);
        await SeedVersionAsync("v1", "Current sign-off rule.", effectiveTo: soon);
        await SeedVersionAsync("v2", "Future sign-off rule.", effectiveFrom: soon);

        var result = await RetrieveAsync("sign off", examDate: null);

        Assert.Single(result.Evidence);
        Assert.Contains("Current sign-off rule", result.Evidence[0].Text, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedVersionAsync(
        string version,
        string ruleText,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null)
    {
        await using var db = new LearnerDbContext(_options);
        var id = Guid.NewGuid();

        db.CompanionSources.Add(new CompanionSource
        {
            Id = id,
            SourceKey = SourceKey,
            Version = version,
            SourceType = "rulebook",
            Title = $"Writing rulebook — Medicine ({version})",
            AuthorityClass = CompanionAuthorityClass.ProfessionApprovedMethod,
            State = CompanionSourceState.Approved,
            ExamTypeCode = "OET",
            ProfessionId = "medicine",
            SubtestCode = "writing",
            IsProprietary = true,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        db.CompanionChunks.Add(new CompanionChunk
        {
            Id = Guid.NewGuid(),
            SourceId = id,
            Ordinal = 0,
            Heading = "W001 — sign off",
            Text = ruleText,
            ContentHash = $"hash-{version}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
        return id;
    }

    private async Task SupersedeAsync(Guid older, Guid newer)
    {
        await using var db = new LearnerDbContext(_options);
        var source = await db.CompanionSources.SingleAsync(s => s.Id == older);
        source.SupersededBySourceId = newer;
        source.State = CompanionSourceState.Superseded;
        await db.SaveChangesAsync();
    }

    private async Task<CompanionRetrievalResult> RetrieveAsync(
        string query,
        DateOnly? examDate = null)
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
            ExamDate = examDate,
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
