using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// F-154 — entitlement-safe retrieval. The source specification gives this
/// <b>zero tolerance</b>: a learner must never retrieve content they do not own,
/// and the filter must run <i>before</i> search rather than after ranking.
///
/// <para>
/// These tests run on SQLite, so the pgvector path is inactive and the keyword
/// path answers — which is the stricter test: if the prefilter were implemented
/// as a post-search score penalty rather than a candidate-set restriction, the
/// keyword path would happily return the forbidden chunk.
/// </para>
/// </summary>
public sealed class CompanionRetrievalSecurityTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionRetrievalSecurityTests()
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
    public async Task UnentitledSource_IsNeverRetrieved_EvenAsTheOnlyMatch()
    {
        await SeedSourceAsync(
            key: "course:pharmacy-premium",
            requiredScope: "pkg_pharmacy_pro",
            text: "Pharmacy premium referral letter guidance about anticoagulant counselling.");

        var retriever = BuildRetriever();
        var context = Context(scopes: []); // owns nothing

        var result = await retriever.RetrieveAsync(
            "anticoagulant counselling referral", context, 10, CancellationToken.None);

        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task EntitledSource_IsRetrieved()
    {
        await SeedSourceAsync(
            key: "course:pharmacy-premium",
            requiredScope: "pkg_pharmacy_pro",
            text: "Pharmacy premium referral letter guidance about anticoagulant counselling.");

        var retriever = BuildRetriever();
        var context = Context(scopes: ["pkg_pharmacy_pro"]);

        var result = await retriever.RetrieveAsync(
            "anticoagulant counselling referral", context, 10, CancellationToken.None);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("course:pharmacy-premium", evidence.SourceKey);
    }

    [Fact]
    public async Task FreelyRetrievableSource_NeedsNoScope()
    {
        await SeedSourceAsync(
            key: "rulebook:writing:medicine",
            requiredScope: null,
            text: "Select only the case notes relevant to the reader's purpose.");

        var retriever = BuildRetriever();
        var result = await retriever.RetrieveAsync(
            "which case notes relevant", Context(scopes: []), 10, CancellationToken.None);

        Assert.Single(result.Evidence);
    }

    [Fact]
    public async Task DraftSource_IsNeverRetrieved()
    {
        await SeedSourceAsync(
            key: "rulebook:draft",
            requiredScope: null,
            text: "Unapproved draft guidance about referral letters.",
            state: CompanionSourceState.Draft);

        var retriever = BuildRetriever();
        var result = await retriever.RetrieveAsync(
            "referral letters guidance", Context(scopes: []), 10, CancellationToken.None);

        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task SupersededSource_IsNeverRetrieved()
    {
        await SeedSourceAsync(
            key: "rulebook:old",
            requiredScope: null,
            text: "Obsolete guidance about referral letters.",
            supersededBy: Guid.NewGuid());

        var retriever = BuildRetriever();
        var result = await retriever.RetrieveAsync(
            "referral letters guidance", Context(scopes: []), 10, CancellationToken.None);

        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task OtherProfessionSource_IsNeverRetrieved()
    {
        await SeedSourceAsync(
            key: "rulebook:writing:nursing",
            requiredScope: null,
            text: "Nursing specific referral letter guidance.",
            professionId: "nursing");

        var retriever = BuildRetriever();
        var context = Context(scopes: [], professionId: "medicine");

        var result = await retriever.RetrieveAsync(
            "referral letter guidance", context, 10, CancellationToken.None);

        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task ExpiredSource_IsNeverRetrieved()
    {
        await SeedSourceAsync(
            key: "official:old-fee",
            requiredScope: null,
            text: "Historic referral fee guidance no longer in effect.",
            effectiveTo: DateTimeOffset.UtcNow.AddDays(-1));

        var retriever = BuildRetriever();
        var result = await retriever.RetrieveAsync(
            "referral fee guidance", Context(scopes: []), 10, CancellationToken.None);

        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task RetrievalDisabledByKillSwitch_ReturnsNothing()
    {
        await SeedSourceAsync("rulebook:writing:medicine", null, "Relevant case note selection guidance.");

        var retriever = BuildRetriever();
        var context = Context(scopes: []) with { RetrievalEnabled = false };

        var result = await retriever.RetrieveAsync(
            "case note selection", context, 10, CancellationToken.None);

        Assert.Empty(result.Evidence);
        Assert.Contains("retrieval.disabled_by_flag", result.Trace);
    }

    [Fact]
    public async Task ProprietarySource_IsCappedAndFlaggedTruncated()
    {
        // One long proprietary chunk: the companion must teach from it, not reprint it.
        var longText = "Referral letter guidance. " + new string('x', 4000);
        await SeedSourceAsync(
            key: "rulebook:writing:medicine",
            requiredScope: null,
            text: longText,
            isProprietary: true);

        var retriever = BuildRetriever();
        var result = await retriever.RetrieveAsync(
            "referral letter guidance", Context(scopes: []), 10, CancellationToken.None);

        var evidence = Assert.Single(result.Evidence);
        Assert.True(
            evidence.Text.Length <= 1200,
            $"Proprietary evidence was {evidence.Text.Length} chars; the per-source verbatim cap is 1200.");
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task MixedEntitlement_ReturnsOnlyTheOwnedSource()
    {
        await SeedSourceAsync("owned", null, "Referral letter structure and purpose statement.");
        await SeedSourceAsync("locked", "pkg_not_owned", "Referral letter structure and purpose statement, premium depth.");

        var retriever = BuildRetriever();
        var result = await retriever.RetrieveAsync(
            "referral letter purpose statement", Context(scopes: []), 10, CancellationToken.None);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("owned", evidence.SourceKey);
    }

    // ---------------------------------------------------------------- helpers

    private static CompanionTurnContext Context(
        string[] scopes,
        string professionId = "medicine") => new()
    {
        UserId = "learner-1",
        ProfessionId = professionId,
        ExamTypeCode = "OET",
        Tier = "paid",
        EntitlementScopes = scopes,
        RetrievalEnabled = true,
    };

    private async Task SeedSourceAsync(
        string key,
        string? requiredScope,
        string text,
        CompanionSourceState state = CompanionSourceState.Approved,
        string? professionId = "medicine",
        bool isProprietary = false,
        Guid? supersededBy = null,
        DateTimeOffset? effectiveTo = null)
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;
        var sourceId = Guid.NewGuid();

        db.CompanionSources.Add(new CompanionSource
        {
            Id = sourceId,
            SourceKey = key,
            Version = "v1",
            SourceType = "test",
            Title = key,
            AuthorityClass = CompanionAuthorityClass.ProfessionApprovedMethod,
            State = state,
            ExamTypeCode = "OET",
            ProfessionId = professionId,
            RequiredEntitlementScope = requiredScope,
            IsProprietary = isProprietary,
            SupersededBySourceId = supersededBy,
            EffectiveTo = effectiveTo,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.CompanionChunks.Add(new CompanionChunk
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            Ordinal = 0,
            Heading = $"{key} rule",
            Text = text,
            ContentHash = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
    }

    private CompanionRetriever BuildRetriever() =>
        new(new LearnerDbContext(_options),
            new NeverCalledEmbeddingService(),
            NullLogger<CompanionRetriever>.Instance);

    /// <summary>
    /// SQLite has no pgvector, so the retriever must never reach the embedding
    /// provider. Throwing here proves the vector path is correctly skipped rather
    /// than silently failing and masking a prefilter bug.
    /// </summary>
    private sealed class NeverCalledEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
            throw new InvalidOperationException("Vector search must not run without pgvector.");

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            throw new InvalidOperationException("Vector search must not run without pgvector.");
    }
}
