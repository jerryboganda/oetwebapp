using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase 7 — verify that <see cref="AiFeatureRouteResolver"/> returns the
/// correct override (or null) for every state of a route row, and that the
/// IsKnownFeatureCode allowlist matches the canonical feature-code set.
/// </summary>
public sealed class AiFeatureRouteResolverTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiFeatureRouteResolverTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ResolveAsync_NoRow_ReturnsNull()
    {
        await using var db = new LearnerDbContext(_options);
        var resolver = new AiFeatureRouteResolver(db);
        var result = await resolver.ResolveAsync(AiFeatureCodes.VocabularyGloss, default);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(AiFeatureCodes.ConversationOpening, "anthropic", "claude-sonnet-4-6")]
    [InlineData(AiFeatureCodes.ConversationReply, "anthropic", "claude-sonnet-4-6")]
    [InlineData(AiFeatureCodes.ConversationEvaluation, "anthropic", "claude-sonnet-4-6")]
    [InlineData(AiFeatureCodes.WritingGrade, "anthropic", "claude-sonnet-4-6")]
    [InlineData(AiFeatureCodes.ReadingExplanation, "anthropic", "claude-sonnet-4-6")]
    [InlineData(AiFeatureCodes.PronunciationLinguisticScore, "gemini-pronunciation-audio", "gemini-3.5-flash")]
    public async Task ResolveAsync_NoRow_ReturnsStaticDefault_WhenProviderKeyed(string featureCode, string provider, string model)
    {
        await using var db = new LearnerDbContext(_options);
        // Key-guard: static defaults only resolve when the provider is usable.
        await SeedProviderAsync(db, "anthropic", keyed: true);
        await SeedProviderAsync(db, "gemini-pronunciation-audio", keyed: true);
        var resolver = new AiFeatureRouteResolver(db);

        var result = await resolver.ResolveAsync(featureCode, default);

        Assert.NotNull(result);
        Assert.Equal(provider, result!.ProviderCode);
        Assert.Equal(model, result.Model);
    }

    [Fact]
    public async Task ResolveAsync_StaticDefault_KeylessPrimary_FallsThroughToNull()
    {
        await using var db = new LearnerDbContext(_options);
        // anthropic row exists but has NO key, and no openai fallback row →
        // resolver returns null so the gateway falls through to its keyed top
        // provider (the exact pre-seeder behaviour).
        await SeedProviderAsync(db, "anthropic", keyed: false);
        var resolver = new AiFeatureRouteResolver(db);

        var result = await resolver.ResolveAsync(AiFeatureCodes.WritingGrade, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_StaticDefault_KeylessPrimary_UsesKeyedFallback()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "anthropic", keyed: false);
        await SeedProviderAsync(db, "openai", keyed: true);
        var resolver = new AiFeatureRouteResolver(db);

        var result = await resolver.ResolveAsync(AiFeatureCodes.WritingGrade, default);

        Assert.NotNull(result);
        Assert.Equal("openai", result!.ProviderCode);
        Assert.Equal("gpt-4o", result.Model);
    }

    [Fact]
    public async Task ResolveAsync_StaticDefault_KeyOnAccountPool_IsUsable()
    {
        await using var db = new LearnerDbContext(_options);
        var providerId = await SeedProviderAsync(db, "anthropic", keyed: false);
        db.AiProviderAccounts.Add(new AiProviderAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            ProviderId = providerId,
            Label = "pool-1",
            EncryptedApiKey = "cipher",
            ApiKeyHint = "…key",
            Priority = 0,
            IsActive = true,
            PeriodMonthKey = "2026-05",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var resolver = new AiFeatureRouteResolver(db);

        var result = await resolver.ResolveAsync(AiFeatureCodes.WritingGrade, default);

        Assert.NotNull(result);
        Assert.Equal("anthropic", result!.ProviderCode);
    }

    private static async Task<string> SeedProviderAsync(LearnerDbContext db, string code, bool keyed)
    {
        var id = Guid.NewGuid().ToString("N");
        db.AiProviders.Add(new AiProvider
        {
            Id = id,
            Code = code,
            Name = code,
            Dialect = AiProviderDialect.OpenAiCompatible,
            Category = AiProviderCategory.TextChat,
            BaseUrl = "https://example.com/v1",
            EncryptedApiKey = keyed ? "cipher" : string.Empty,
            ApiKeyHint = string.Empty,
            DefaultModel = "x",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task ResolveAsync_InactiveRow_ReturnsNull()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedRouteAsync(db, AiFeatureCodes.VocabularyGloss, "copilot", isActive: false);
        var resolver = new AiFeatureRouteResolver(db);
        var result = await resolver.ResolveAsync(AiFeatureCodes.VocabularyGloss, default);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ActiveRow_ReturnsProviderCodeAndModel()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedRouteAsync(db, AiFeatureCodes.VocabularyGloss, "copilot",
            model: "openai/gpt-4o-mini", isActive: true);
        var resolver = new AiFeatureRouteResolver(db);
        var result = await resolver.ResolveAsync(AiFeatureCodes.VocabularyGloss, default);
        Assert.NotNull(result);
        Assert.Equal("copilot", result!.ProviderCode);
        Assert.Equal("openai/gpt-4o-mini", result.Model);
    }

    [Fact]
    public async Task ResolveAsync_BlankFeatureCode_ReturnsNull()
    {
        await using var db = new LearnerDbContext(_options);
        var resolver = new AiFeatureRouteResolver(db);
        Assert.Null(await resolver.ResolveAsync("", default));
        Assert.Null(await resolver.ResolveAsync("   ", default));
    }

    [Fact]
    public void IsKnownFeatureCode_AcceptsKnownCodes_AndRejectsUnknown()
    {
        using var db = new LearnerDbContext(_options);
        var resolver = new AiFeatureRouteResolver(db);
        Assert.True(resolver.IsKnownFeatureCode(AiFeatureCodes.VocabularyGloss));
        Assert.True(resolver.IsKnownFeatureCode(AiFeatureCodes.WritingCoachSuggest));
        Assert.True(resolver.IsKnownFeatureCode(AiFeatureCodes.AdminGrammarDraft));
        Assert.True(resolver.IsKnownFeatureCode(AiFeatureCodes.PronunciationLinguisticScore));
        // Case-insensitive on input.
        Assert.True(resolver.IsKnownFeatureCode("VOCABULARY.GLOSS"));
        // Unclassified is intentionally NOT routable.
        Assert.False(resolver.IsKnownFeatureCode(AiFeatureCodes.Unclassified));
        Assert.False(resolver.IsKnownFeatureCode("not.a.real.code"));
        Assert.False(resolver.IsKnownFeatureCode(""));
    }

    [Fact]
    public void CopilotBulkRouteTargets_MatchPRDList()
    {
        // Lock the canonical set against the PRD so accidental edits to
        // AiFeatureRouteResolver.CopilotBulkRouteTargets break this test.
        var expected = new[]
        {
            AiFeatureCodes.VocabularyGloss,
            AiFeatureCodes.RecallsMistakeExplain,
            AiFeatureCodes.RecallsRevisionPlan,
            AiFeatureCodes.ConversationOpening,
            AiFeatureCodes.ConversationReply,
            AiFeatureCodes.WritingCoachSuggest,
            AiFeatureCodes.WritingCoachExplain,
            AiFeatureCodes.SummarisePassage,
        };
        Assert.Equal(expected, AiFeatureRouteResolver.CopilotBulkRouteTargets);
    }

    private static async Task SeedRouteAsync(LearnerDbContext db,
        string featureCode, string providerCode,
        string? model = null, bool isActive = true)
    {
        db.AiFeatureRoutes.Add(new AiFeatureRoute
        {
            Id = Guid.NewGuid().ToString("N"),
            FeatureCode = featureCode,
            ProviderCode = providerCode,
            Model = model,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
