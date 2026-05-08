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
