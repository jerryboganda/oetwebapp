using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Entitlements;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The Manifest source classes that had no writer at all until now: the platform
/// map (1.E), the support rules (1.E) and the vocabulary recall sets (1.C).
///
/// <para>
/// Each of these is tested the same way, and it is deliberately not "did the
/// indexer run". Indexed is not the same as retrievable, and retrievable is the
/// only property a learner can observe — so every case ends with a real query
/// through <see cref="CompanionRetriever"/>, including the entitlement prefilter.
/// </para>
/// </summary>
public sealed class CompanionCorpusCoverageTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionCorpusCoverageTests()
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

    // ── Platform map ─────────────────────────────────────────────────────────

    [Fact]
    public void The_platform_map_covers_every_live_destination()
    {
        // The point of generating from the registry is that the two cannot drift.
        // If a destination is added and this fails, the map is stale.
        var text = string.Concat(CompanionPlatformMapIndexer.BuildChunks().Select(c => c.Text));

        foreach (var destination in CompanionDestinationRegistry.CatalogForIndexing)
        {
            if (destination.SupersededById is not null) continue; // aliases are never surfaced
            Assert.Contains(destination.Id, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Locked_areas_are_described_rather_than_hidden()
    {
        // Describing a product a learner has not bought is marketing; opening it
        // is entitlement. Hiding it entirely would leave the companion unable to
        // answer "what is the video library?" for exactly the people considering
        // buying it.
        var chunks = CompanionPlatformMapIndexer.BuildChunks().ToList();
        var text = string.Concat(chunks.Select(c => c.Text));

        Assert.Contains("videos", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ModuleKeys.VideoLibrary, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_learner_can_ask_where_something_lives()
    {
        await IndexPlatformMapAsync();

        var result = await RetrieveAsync("where do I see my progress and past attempts");

        Assert.NotEmpty(result.Evidence);
        Assert.Contains(result.Evidence, e => e.Authority == CompanionAuthorityClass.PlatformSupport);
    }

    // ── Support knowledge ────────────────────────────────────────────────────

    [Fact]
    public void Device_and_credit_figures_come_from_the_enforcing_code()
    {
        // The whole reason this indexer exists: a companion that states a device
        // limit or a credit price from memory will eventually state the wrong
        // one, and a learner told the wrong number acts on it.
        var text = string.Concat(CompanionSupportKnowledgeIndexer.BuildChunks(7, 3).Select(c => c.Text));

        Assert.Contains($"{TrustedDeviceService.DefaultMaxDevices} trusted devices", text, StringComparison.Ordinal);
        Assert.Contains($"at most {TrustedDeviceService.MaxAllowedDevicesOverride}", text, StringComparison.Ordinal);
        Assert.Contains($"{AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity} credits", text, StringComparison.Ordinal);

        // Two cards at two credits each. The packs test this exact number.
        Assert.Contains("4 credits", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_device_change_window_reflects_runtime_settings()
    {
        // Freezing these would make an admin's settings change silently untrue.
        var text = string.Concat(CompanionSupportKnowledgeIndexer.BuildChunks(14, 5).Select(c => c.Text));

        Assert.Contains("5 approved changes in a rolling 14-day window", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Support_knowledge_never_states_the_learners_own_position()
    {
        // A shared corpus must carry shared rules only. "You have two devices
        // registered" in a shared source would be another learner's fact for
        // everyone who retrieved it.
        var text = string.Concat(CompanionSupportKnowledgeIndexer.BuildChunks(7, 3).Select(c => c.Text));

        Assert.Contains("Never tell a learner how many devices they currently have", text, StringComparison.Ordinal);
        Assert.Contains("Never state whether a specific learner's access is active", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_in_codes_are_never_solicited()
    {
        var text = string.Concat(CompanionSupportKnowledgeIndexer.BuildChunks(7, 3).Select(c => c.Text));

        Assert.Contains("Never ask a learner to tell you their one-time code", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pack4_s13_a_learner_can_troubleshoot_a_sign_in_code()
    {
        await IndexSupportAsync();

        var result = await RetrieveAsync("I did not receive my verification code on my new phone");

        Assert.NotEmpty(result.Evidence);
        Assert.Contains(result.Evidence, e => e.Text.Contains("spam", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pack4_s14_a_learner_can_ask_how_many_devices_are_allowed()
    {
        await IndexSupportAsync();

        var result = await RetrieveAsync("how many devices can I use on my account");

        Assert.NotEmpty(result.Evidence);
        Assert.Contains(
            result.Evidence,
            e => e.Text.Contains($"{TrustedDeviceService.DefaultMaxDevices} trusted devices", StringComparison.Ordinal));
    }

    // ── Vocabulary ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Paid_recall_sets_need_the_recalls_module_and_the_preview_does_not()
    {
        await SeedVocabularyAsync();
        await IndexVocabularyAsync();

        await using var db = new LearnerDbContext(_options);

        var free = await db.CompanionSources.SingleAsync(s => s.SourceKey == CompanionVocabularyIndexer.FreeSourceKey);
        var paid = await db.CompanionSources.SingleAsync(s => s.SourceKey == CompanionVocabularyIndexer.PaidSourceKey);

        Assert.Null(free.RequiredEntitlementScope);
        Assert.False(free.IsProprietary);
        Assert.Equal(ModuleKeys.Recalls, paid.RequiredEntitlementScope);
        Assert.True(paid.IsProprietary);
    }

    [Fact]
    public async Task A_learner_without_the_recalls_module_reaches_only_the_preview()
    {
        // The prefilter drops the paid source before any search runs, so this
        // cannot be talked around — which is the F-154 ordering requirement.
        await SeedVocabularyAsync();
        await IndexVocabularyAsync();

        var result = await RetrieveAsync("what does bradycardia mean", scopes: []);

        Assert.DoesNotContain(result.Evidence, e => e.SourceKey == CompanionVocabularyIndexer.PaidSourceKey);
    }

    [Fact]
    public async Task A_learner_with_the_recalls_module_reaches_the_full_set()
    {
        await SeedVocabularyAsync();
        await IndexVocabularyAsync();

        var result = await RetrieveAsync("what does bradycardia mean", scopes: [ModuleKeys.Recalls]);

        Assert.Contains(result.Evidence, e => e.SourceKey == CompanionVocabularyIndexer.PaidSourceKey);
    }

    [Fact]
    public void A_recall_label_is_never_presented_as_a_prediction()
    {
        // "This word came up in 2026" is a practice grouping. A candidate who
        // hears it as "this will be on your test" has been misled by us.
        var terms = new List<CompanionVocabularyIndexer.TermRow>
        {
            new("bradycardia", "A slow heart rate.", null, "cardiology", null, false, "[\"2026\"]"),
        };

        var text = string.Concat(CompanionVocabularyIndexer.BuildChunks(terms, isPaid: true).Select(c => c.Text));

        Assert.Contains("Never tell a learner a word will be on their test", text, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task IndexPlatformMapAsync()
    {
        await using var db = new LearnerDbContext(_options);
        await new CompanionPlatformMapIndexer(db, new UnusedEmbeddings(),
            NullLogger<CompanionPlatformMapIndexer>.Instance)
            .IndexAsync(embed: false, CancellationToken.None);
    }

    private async Task IndexSupportAsync()
    {
        await using var db = new LearnerDbContext(_options);
        // No settings provider: exercises the enforced-defaults path, which is
        // what an unconfigured install actually applies.
        await new CompanionSupportKnowledgeIndexer(db, new UnusedEmbeddings(),
            NullLogger<CompanionSupportKnowledgeIndexer>.Instance)
            .IndexAsync(embed: false, CancellationToken.None);
    }

    private async Task IndexVocabularyAsync()
    {
        await using var db = new LearnerDbContext(_options);
        await new CompanionVocabularyIndexer(db, new UnusedEmbeddings(),
            NullLogger<CompanionVocabularyIndexer>.Instance)
            .IndexAsync(embed: false, CancellationToken.None);
    }

    private async Task SeedVocabularyAsync()
    {
        await using var db = new LearnerDbContext(_options);

        db.VocabularyTerms.AddRange(
            new VocabularyTerm
            {
                Id = "v1", Term = "bradycardia", Definition = "A slower than normal heart rate.",
                ExamTypeCode = "OET", Category = "cardiology", Status = "active", IsFreePreview = false,
                RecallSetCodesJson = "[\"2026\"]",
            },
            new VocabularyTerm
            {
                Id = "v2", Term = "oedema", Definition = "Swelling caused by fluid in the tissues.",
                ExamTypeCode = "OET", Category = "general", Status = "active", IsFreePreview = true,
                RecallSetCodesJson = "[]",
            });

        await db.SaveChangesAsync();
    }

    private async Task<CompanionRetrievalResult> RetrieveAsync(string query, string[]? scopes = null)
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
            EntitlementScopes = scopes ?? [ModuleKeys.Recalls, ModuleKeys.MaterialsLibrary],
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
