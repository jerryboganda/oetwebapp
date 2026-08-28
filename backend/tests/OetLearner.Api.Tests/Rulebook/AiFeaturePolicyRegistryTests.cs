using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Rulebook;

/// <summary>
/// W2 of the AI cost/reliability remediation — proves
/// <see cref="AiFeaturePolicyRegistry"/> covers every existing feature code
/// (via <see cref="AiFeaturePolicyDefaults"/>), resolves DB overrides over
/// static defaults, picks the highest active+effective version
/// deterministically, and refuses blank/Unclassified/unregistered codes.
/// </summary>
public sealed class AiFeaturePolicyRegistryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiFeaturePolicyRegistryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task LookupAsync_BlankOrUnclassified_ReturnsUnknown()
    {
        await using var db = new LearnerDbContext(_options);
        var registry = new AiFeaturePolicyRegistry(db);

        foreach (var code in new string?[] { null, "", "   ", AiFeatureCodes.Unclassified })
        {
            var lookup = await registry.LookupAsync(code, default);
            Assert.Equal(AiFeaturePolicyStatus.Unknown, lookup.Status);
            Assert.False(lookup.IsUsable);
            Assert.Null(lookup.Policy);
            Assert.Equal("policy_unknown", lookup.Reason);
        }
    }

    [Fact]
    public async Task LookupAsync_UnregisteredCode_ReturnsUnknown()
    {
        await using var db = new LearnerDbContext(_options);
        var registry = new AiFeaturePolicyRegistry(db);

        var lookup = await registry.LookupAsync("not.a.real.feature", default);

        Assert.Equal(AiFeaturePolicyStatus.Unknown, lookup.Status);
        Assert.False(lookup.IsUsable);
        Assert.Null(lookup.Policy);
    }

    [Theory]
    [InlineData(AiFeatureCodes.WritingGrade, "writing", AiOperationClass.ScoringCritical, true)]
    [InlineData(AiFeatureCodes.WritingCoachSuggest, "writing", AiOperationClass.InteractiveLearning, true)]
    [InlineData(AiFeatureCodes.AdminContentGeneration, "admin", AiOperationClass.AdminBatch, true)]
    [InlineData(AiFeatureCodes.ConversationEvaluation, "conversation", AiOperationClass.ScoringCritical, true)]
    [InlineData(AiFeatureCodes.TutorRecommendation, "tutor", AiOperationClass.AdminBatch, true)]
    [InlineData(AiFeatureCodes.OcrListeningPartA, "ocr", AiOperationClass.InteractiveLearning, false)]
    [InlineData(AiFeatureCodes.ListeningPartAScore, "listening", AiOperationClass.ScoringCritical, false)]
    [InlineData(SpeakingAiFeatureCodes.SpeakingScoreV2, "speaking", AiOperationClass.ScoringCritical, true)]
    [InlineData(SpeakingAiFeatureCodes.CardDraftV1, "speaking", AiOperationClass.AdminBatch, true)]
    public async Task LookupAsync_StaticDefault_CoversFeature(
        string featureCode, string expectedModule, AiOperationClass expectedClass, bool expectedGrounding)
    {
        await using var db = new LearnerDbContext(_options);
        var registry = new AiFeaturePolicyRegistry(db);

        var lookup = await registry.LookupAsync(featureCode, default);

        Assert.Equal(AiFeaturePolicyStatus.StaticDefault, lookup.Status);
        Assert.True(lookup.IsUsable);
        var resolution = lookup.Policy;
        Assert.NotNull(resolution);
        Assert.Equal(expectedModule, resolution!.Module);
        Assert.Equal(expectedClass, resolution.OperationClass);
        Assert.Equal(expectedGrounding, resolution.RequiresGrounding);
    }

    [Fact]
    public void StaticDefaults_CoverEveryAiFeatureCodeExceptUnclassified()
    {
        var codeFields = typeof(AiFeatureCodes)
            .GetFields()
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .Where(code => !string.Equals(code, AiFeatureCodes.Unclassified, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(codeFields);
        foreach (var code in codeFields)
        {
            Assert.True(
                AiFeaturePolicyDefaults.All.ContainsKey(code),
                $"AiFeaturePolicyDefaults is missing a static default for '{code}'.");
        }
    }

    [Fact]
    public void StaticDefaults_CoverEverySpeakingAiFeatureCode()
    {
        foreach (var code in SpeakingAiFeatureCodes.All)
        {
            Assert.True(
                AiFeaturePolicyDefaults.All.ContainsKey(code),
                $"AiFeaturePolicyDefaults is missing a static default for Speaking code '{code}'.");
        }
    }

    [Fact]
    public async Task LookupAsync_DbOverride_WinsOverStaticDefault()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedPolicyAsync(db, AiFeatureCodes.WritingCoachSuggest, version: 1, module: "custom-writing", operationClass: AiOperationClass.AdminBatch);
        var registry = new AiFeaturePolicyRegistry(db);

        var lookup = await registry.LookupAsync(AiFeatureCodes.WritingCoachSuggest, default);

        Assert.Equal(AiFeaturePolicyStatus.DbActive, lookup.Status);
        Assert.True(lookup.IsUsable);
        Assert.NotNull(lookup.Policy);
        Assert.Equal("custom-writing", lookup.Policy!.Module);
        Assert.Equal(AiOperationClass.AdminBatch, lookup.Policy.OperationClass);
        Assert.Equal(1, lookup.Policy.PolicyVersion);
    }

    [Fact]
    public async Task LookupAsync_MultipleActiveVersions_ResolvesHighestVersion()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedPolicyAsync(db, AiFeatureCodes.WritingCoachSuggest, version: 1, module: "v1-module", operationClass: AiOperationClass.InteractiveLearning);
        await SeedPolicyAsync(db, AiFeatureCodes.WritingCoachSuggest, version: 2, module: "v2-module", operationClass: AiOperationClass.InteractiveLearning);
        var registry = new AiFeaturePolicyRegistry(db);

        var lookup = await registry.LookupAsync(AiFeatureCodes.WritingCoachSuggest, default);

        Assert.Equal(AiFeaturePolicyStatus.DbActive, lookup.Status);
        Assert.NotNull(lookup.Policy);
        Assert.Equal("v2-module", lookup.Policy!.Module);
        Assert.Equal(2, lookup.Policy.PolicyVersion);
    }

    // ── E-1: an EXPLICIT row suppresses the static default ──────────────────
    // The pre-correction behaviour silently fell back to the compiled-in
    // default whenever the DB row was inactive/not-yet-effective/expired, so an
    // admin switching a feature OFF had no effect on spend. These three tests
    // are the load-bearing proof that a deliberate "off" now wins.

    [Fact]
    public async Task LookupAsync_InactiveRow_SuppressesStaticDefault()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedPolicyAsync(db, AiFeatureCodes.WritingCoachSuggest, version: 1, module: "should-be-ignored", operationClass: AiOperationClass.AdminBatch, isActive: false);
        var registry = new AiFeaturePolicyRegistry(db);

        var lookup = await registry.LookupAsync(AiFeatureCodes.WritingCoachSuggest, default);

        Assert.Equal(AiFeaturePolicyStatus.DbDisabled, lookup.Status);
        Assert.False(lookup.IsUsable);
        Assert.Equal("policy_disabled", lookup.Reason);
    }

    [Fact]
    public async Task LookupAsync_NotYetEffectiveRow_SuppressesStaticDefault()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedPolicyAsync(
            db,
            AiFeatureCodes.WritingCoachSuggest,
            version: 1,
            module: "future-module",
            operationClass: AiOperationClass.AdminBatch,
            effectiveFrom: DateTimeOffset.UtcNow.AddDays(30));
        var registry = new AiFeaturePolicyRegistry(db);

        var lookup = await registry.LookupAsync(AiFeatureCodes.WritingCoachSuggest, default);

        Assert.Equal(AiFeaturePolicyStatus.DbNotYetEffective, lookup.Status);
        Assert.False(lookup.IsUsable);
        Assert.Equal("policy_not_yet_effective", lookup.Reason);
    }

    [Fact]
    public async Task LookupAsync_ExpiredRow_SuppressesStaticDefault()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedPolicyAsync(
            db,
            AiFeatureCodes.WritingCoachSuggest,
            version: 1,
            module: "expired-module",
            operationClass: AiOperationClass.AdminBatch,
            effectiveFrom: DateTimeOffset.UtcNow.AddDays(-60),
            effectiveTo: DateTimeOffset.UtcNow.AddDays(-1));
        var registry = new AiFeaturePolicyRegistry(db);

        var lookup = await registry.LookupAsync(AiFeatureCodes.WritingCoachSuggest, default);

        Assert.Equal(AiFeaturePolicyStatus.DbExpired, lookup.Status);
        Assert.False(lookup.IsUsable);
        Assert.Equal("policy_expired", lookup.Reason);
    }

    /// <summary>An inactive row for feature A must not affect feature B, and an
    /// ACTIVE newer version must beat an inactive older one for the same
    /// feature — "suppression" is per-feature and version-aware, not a blanket
    /// kill of anything with a row.</summary>
    [Fact]
    public async Task LookupAsync_ActiveVersionBeatsInactiveVersion_ForSameFeature()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedPolicyAsync(db, AiFeatureCodes.WritingCoachSuggest, version: 1, module: "old-inactive", operationClass: AiOperationClass.AdminBatch, isActive: false);
        await SeedPolicyAsync(db, AiFeatureCodes.WritingCoachSuggest, version: 2, module: "new-active", operationClass: AiOperationClass.InteractiveLearning);
        var registry = new AiFeaturePolicyRegistry(db);

        var lookup = await registry.LookupAsync(AiFeatureCodes.WritingCoachSuggest, default);
        Assert.Equal(AiFeaturePolicyStatus.DbActive, lookup.Status);
        Assert.Equal("new-active", lookup.Policy!.Module);

        // An untouched neighbour still resolves from its static default.
        var neighbour = await registry.LookupAsync(AiFeatureCodes.WritingGrade, default);
        Assert.Equal(AiFeaturePolicyStatus.StaticDefault, neighbour.Status);
        Assert.True(neighbour.IsUsable);
    }

    private static async Task SeedPolicyAsync(
        LearnerDbContext db,
        string featureCode,
        int version,
        string module,
        AiOperationClass operationClass,
        bool isActive = true,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null)
    {
        db.AiFeaturePolicies.Add(new AiFeaturePolicy
        {
            Id = Guid.NewGuid().ToString("N"),
            FeatureCode = featureCode,
            Module = module,
            OperationClass = operationClass,
            IsActive = isActive,
            PolicyVersion = version,
            RequiresGrounding = true,
            EffectiveFrom = effectiveFrom ?? DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo = effectiveTo,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
