using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The companion serves many students from one deployment. There is no tenancy
/// boundary to hide behind — every learner shares the same process, the same
/// knowledge corpus and the same service instances — so isolation is entirely a
/// matter of every lookup being keyed by the authenticated user id.
///
/// <para>
/// These tests exercise several learners with deliberately different
/// professions, entitlements, preferences, exam states and saved data, and
/// assert nothing crosses between them. Some run <b>concurrently</b> on purpose:
/// a per-user bug that only appears under interleaving is exactly the kind that
/// survives sequential testing and then shows up on a busy evening.
/// </para>
/// </summary>
public sealed class CompanionMultiLearnerIsolationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionMultiLearnerIsolationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    // ── retrieval ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneRetrieverInstance_ServesEachLearnerOnlyTheirOwnSources()
    {
        await SeedSourceAsync("course:pharmacy", "pkg_pharmacy", "Anticoagulant counselling guidance.", "pharmacy");
        await SeedSourceAsync("course:nursing", "pkg_nursing", "Anticoagulant counselling guidance.", "nursing");
        await SeedSourceAsync("rulebook:shared", null, "Anticoagulant counselling guidance.", null);

        // The SAME retriever answers both learners, back to back, as it would
        // within a single request scope serving a shared corpus.
        var retriever = BuildRetriever();

        var pharmacist = await retriever.RetrieveAsync(
            "anticoagulant counselling",
            Context("s1", ExamProfession.Pharmacy, "pharmacy", ["pkg_pharmacy"]),
            10, CancellationToken.None);

        var nurse = await retriever.RetrieveAsync(
            "anticoagulant counselling",
            Context("s2", ExamProfession.Nursing, "nursing", ["pkg_nursing"]),
            10, CancellationToken.None);

        Assert.Contains(pharmacist.Evidence, e => e.SourceKey == "course:pharmacy");
        Assert.DoesNotContain(pharmacist.Evidence, e => e.SourceKey == "course:nursing");

        Assert.Contains(nurse.Evidence, e => e.SourceKey == "course:nursing");
        Assert.DoesNotContain(nurse.Evidence, e => e.SourceKey == "course:pharmacy");

        // Profession-neutral material reaches both.
        Assert.Contains(pharmacist.Evidence, e => e.SourceKey == "rulebook:shared");
        Assert.Contains(nurse.Evidence, e => e.SourceKey == "rulebook:shared");
    }

    [Fact]
    public async Task ConcurrentLearners_NeverSeeEachOthersEntitledContent()
    {
        await SeedSourceAsync("course:premium", "pkg_premium", "Premium referral letter depth.", null);
        await SeedSourceAsync("rulebook:free", null, "Referral letter basics.", null);

        var retriever = BuildRetriever();

        // Twenty interleaved turns: half entitled, half not.
        var work = Enumerable.Range(0, 20).Select(async i =>
        {
            var entitled = i % 2 == 0;
            var scopes = entitled ? new[] { "pkg_premium" } : [];
            var result = await retriever.RetrieveAsync(
                "referral letter",
                Context($"s{i}", ExamProfession.Medicine, "medicine", scopes),
                10, CancellationToken.None);

            return (Entitled: entitled, SawPremium: result.Evidence.Any(e => e.SourceKey == "course:premium"));
        });

        var outcomes = await Task.WhenAll(work);

        Assert.All(outcomes, o => Assert.Equal(o.Entitled, o.SawPremium));
    }

    // ── resolved context ─────────────────────────────────────────────────────

    [Fact]
    public async Task OneEntitlementResolver_DoesNotLeakTheFirstLearnersSnapshot()
    {
        // EffectiveEntitlementResolver memoizes per instance. If that cache were
        // keyed by anything other than user id, the second learner would inherit
        // the first learner's entitlements — silently, and in their favour.
        await SeedLearnerAsync("s1", "pharmacy", locale: "en");
        await SeedLearnerAsync("s2", "nursing", locale: "ar");

        await using var db = new LearnerDbContext(_options);
        var entitlements = new EffectiveEntitlementResolver(db, NullLogger<EffectiveEntitlementResolver>.Instance);
        var resolver = BuildResolver(db, entitlements);

        var first = await resolver.ResolveAsync("s1", null, CancellationToken.None);
        var second = await resolver.ResolveAsync("s2", null, CancellationToken.None);

        Assert.Equal("s1", first.UserId);
        Assert.Equal("s2", second.UserId);
        Assert.Equal("pharmacy", first.ProfessionId);
        Assert.Equal("nursing", second.ProfessionId);
        Assert.Equal("en", first.Locale);
        Assert.Equal("ar", second.Locale);
    }

    [Fact]
    public async Task OneLearnerInAnExam_DoesNotPutEveryoneElseIntoExamMode()
    {
        await SeedLearnerAsync("sitting", "medicine");
        await SeedLearnerAsync("studying", "medicine");
        await SeedAttemptAsync("sitting", AttemptState.InProgress);

        await using var db = new LearnerDbContext(_options);
        var resolver = BuildResolver(db);

        var sitting = await resolver.ResolveAsync("sitting", null, CancellationToken.None);
        var studying = await resolver.ResolveAsync("studying", null, CancellationToken.None);

        Assert.True(sitting.ExamMode);
        Assert.False(studying.ExamMode);
    }

    [Fact]
    public async Task PreferencesArePerLearner_AndReachTheirOwnPromptOnly()
    {
        await SeedLearnerAsync("socratic-learner", "medicine");
        await SeedLearnerAsync("direct-learner", "medicine");
        await SeedPreferenceAsync("socratic-learner", CompanionTeachingStyle.Socratic, englishOnly: true);

        await using var db = new LearnerDbContext(_options);
        var resolver = BuildResolver(db);
        var composer = new CompanionPromptComposer(new ConfigurationBuilder().Build());
        var empty = new CompanionRetrievalResult([], false, false, false, []);

        var socraticPrompt = await composer.ComposeAsync(
            await resolver.ResolveAsync("socratic-learner", null, CancellationToken.None),
            empty, CancellationToken.None);

        var directPrompt = await composer.ComposeAsync(
            await resolver.ResolveAsync("direct-learner", null, CancellationToken.None),
            empty, CancellationToken.None);

        Assert.Contains("SOCRATIC MODE", socraticPrompt, StringComparison.Ordinal);
        Assert.Contains("ENGLISH-ONLY MODE", socraticPrompt, StringComparison.Ordinal);

        Assert.DoesNotContain("SOCRATIC MODE", directPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ENGLISH-ONLY MODE", directPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APromptNeverCarriesAnotherLearnersIdentifiers()
    {
        await SeedLearnerAsync("s1", "pharmacy");
        await SeedLearnerAsync("s2", "nursing");

        await using var db = new LearnerDbContext(_options);
        var resolver = BuildResolver(db);
        var composer = new CompanionPromptComposer(new ConfigurationBuilder().Build());
        var empty = new CompanionRetrievalResult([], false, false, false, []);

        var prompt = await composer.ComposeAsync(
            await resolver.ResolveAsync("s1", null, CancellationToken.None),
            empty, CancellationToken.None);

        Assert.Contains("pharmacy", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nursing", prompt, StringComparison.OrdinalIgnoreCase);
        // The user id is internal plumbing and has no business in a prompt.
        Assert.DoesNotContain("s2", prompt, StringComparison.Ordinal);
    }

    // ── saved data ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ManyLearnersSavedNotes_StayWithTheirOwner()
    {
        for (var i = 0; i < 10; i++)
        {
            await SeedNoteAsync($"note-{i}", $"s{i}", AiFeatureCodes.AiAssistantLearner);
        }

        await using var db = new LearnerDbContext(_options);

        for (var i = 0; i < 10; i++)
        {
            var owned = await db.UserNotes.AsNoTracking()
                .Where(n => n.UserId == $"s{i}")
                .Select(n => n.Id)
                .ToListAsync();

            Assert.Equal([$"note-{i}"], owned);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static CompanionTurnContext Context(
        string userId,
        ExamProfession profession,
        string professionId,
        IReadOnlyList<string> scopes) => new()
    {
        UserId = userId,
        Profession = profession,
        ProfessionId = professionId,
        ExamTypeCode = "OET",
        Tier = "paid",
        EntitlementScopes = scopes,
        RetrievalEnabled = true,
    };

    private CompanionRetriever BuildRetriever() =>
        new(new LearnerDbContext(_options),
            new UnusedEmbeddingService(),
            NullLogger<CompanionRetriever>.Instance);

    private CompanionContextResolver BuildResolver(
        LearnerDbContext db,
        IEffectiveEntitlementResolver? entitlements = null) =>
        new(db,
            entitlements ?? new EffectiveEntitlementResolver(db, NullLogger<EffectiveEntitlementResolver>.Instance),
            new AllOnFlags(),
            NullLogger<CompanionContextResolver>.Instance,
            credits: null);

    private async Task SeedLearnerAsync(string userId, string professionId, string locale = "en")
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = userId,
            Email = $"{userId}@example.test",
            ActiveProfessionId = professionId,
            Locale = locale,
            CreatedAt = now,
            LastActiveAt = now,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedPreferenceAsync(string userId, CompanionTeachingStyle style, bool englishOnly)
    {
        await using var db = new LearnerDbContext(_options);
        db.CompanionPreferences.Add(new CompanionPreference
        {
            UserId = userId,
            TeachingStyle = style,
            EnglishOnly = englishOnly,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAttemptAsync(string userId, AttemptState state)
    {
        await using var db = new LearnerDbContext(_options);
        db.Attempts.Add(new Attempt
        {
            Id = $"attempt-{userId}",
            UserId = userId,
            ContentId = "c1",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "standard",
            State = state,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedNoteAsync(string id, string userId, string featureCode)
    {
        await using var db = new LearnerDbContext(_options);
        db.UserNotes.Add(new UserNote
        {
            Id = id,
            UserId = userId,
            Title = $"note for {userId}",
            BodyMarkdown = "body",
            Source = "ai_tool",
            CreatedByFeatureCode = featureCode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedSourceAsync(
        string key,
        string? requiredScope,
        string text,
        string? professionId)
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
            State = CompanionSourceState.Approved,
            ExamTypeCode = "OET",
            ProfessionId = professionId,
            RequiredEntitlementScope = requiredScope,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.CompanionChunks.Add(new CompanionChunk
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            Ordinal = 0,
            Heading = key,
            Text = text,
            ContentHash = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>SQLite has no pgvector, so the keyword path answers and the
    /// embedding provider must never be reached.</summary>
    private sealed class UnusedEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
            throw new InvalidOperationException("Vector search must not run without pgvector.");

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            throw new InvalidOperationException("Vector search must not run without pgvector.");
    }

    private sealed class AllOnFlags : ICompanionFeatureFlags
    {
        public Task<bool> IsEnabledAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsRetrievalEnabledAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> AreActionsEnabledAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsCreditConsumptionEnabledAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsScoreDisplayEnabledAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<bool> IsPlatformFlagOnAsync(string key, CancellationToken ct) => Task.FromResult(true);
    }
}
