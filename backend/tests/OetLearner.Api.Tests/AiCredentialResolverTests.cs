using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

public class AiCredentialResolverTests
{
    private static async Task<(LearnerDbContext db, AiCredentialResolver resolver, AiCredentialVault vault)> BuildAsync(
        bool allowByokOnScoring = false,
        bool allowByokOnNonScoring = true,
        AiCredentialMode mode = AiCredentialMode.Auto,
        bool hasByokKey = false,
        string byokProvider = "openai-platform")
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);

        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            AllowByokOnScoringFeatures = allowByokOnScoring,
            AllowByokOnNonScoringFeatures = allowByokOnNonScoring,
            DefaultPlatformProviderId = "digitalocean-serverless",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        db.UserAiPreferences.Add(new UserAiPreferences
        {
            UserId = "u1",
            Mode = mode,
            AllowPlatformFallback = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var dpProvider = DataProtectionProvider.Create(nameof(AiCredentialResolverTests));
        var httpFactory = new StubHttpClientFactory();
        var vault = new AiCredentialVault(db, dpProvider, httpFactory, NullLogger<AiCredentialVault>.Instance);
        var quota = new AiQuotaService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<AiQuotaService>.Instance, new EffectiveEntitlementResolver(db));
        var resolver = new AiCredentialResolver(db, quota, vault);

        if (hasByokKey)
        {
            await vault.UpsertAsync("u1", null, byokProvider, "sk-thisisasupersecrettestkey-abcd", null, true, default);
        }
        return (db, resolver, vault);
    }

    [Fact]
    public async Task Resolves_PlatformOnly_ForScoringCritical_WhenGlobalSwitchOff()
    {
        var (db, resolver, _) = await BuildAsync(allowByokOnScoring: false, hasByokKey: true);
        var r = await resolver.ResolveAsync("u1", AiFeatureCodes.WritingGrade, null, default);
        Assert.Equal(AiKeySource.Platform, r.KeySource);
        Assert.Contains("scoring_critical", r.PolicyTrace);
        Assert.Null(r.ApiKeyPlaintext);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Resolves_Byok_ForScoringCritical_WhenGlobalSwitchOn()
    {
        var (db, resolver, _) = await BuildAsync(allowByokOnScoring: true, hasByokKey: true);
        var r = await resolver.ResolveAsync("u1", AiFeatureCodes.WritingGrade, null, default);
        Assert.Equal(AiKeySource.Byok, r.KeySource);
        Assert.Equal("openai-platform", r.ProviderCode);
        Assert.False(string.IsNullOrEmpty(r.ApiKeyPlaintext));
        Assert.Contains("byok", r.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Resolves_Byok_ForNonScoring_ByDefault()
    {
        var (db, resolver, _) = await BuildAsync(hasByokKey: true);
        var r = await resolver.ResolveAsync("u1", AiFeatureCodes.ConversationReply, null, default);
        Assert.Equal(AiKeySource.Byok, r.KeySource);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Resolves_Platform_ForNonScoring_WhenNoKeyStored()
    {
        var (db, resolver, _) = await BuildAsync(hasByokKey: false);
        var r = await resolver.ResolveAsync("u1", AiFeatureCodes.ConversationReply, null, default);
        Assert.Equal(AiKeySource.Platform, r.KeySource);
        Assert.Contains("auto.platform_fallback", r.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Resolves_None_ForByokOnlyUser_WithoutKey()
    {
        var (db, resolver, _) = await BuildAsync(mode: AiCredentialMode.ByokOnly, hasByokKey: false);
        var r = await resolver.ResolveAsync("u1", AiFeatureCodes.ConversationReply, null, default);
        Assert.Equal(AiKeySource.None, r.KeySource);
        Assert.Contains("byok_only.no_key_available", r.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Resolves_Platform_ForPlatformOnlyMode_EvenWithKey()
    {
        var (db, resolver, _) = await BuildAsync(mode: AiCredentialMode.PlatformOnly, hasByokKey: true);
        var r = await resolver.ResolveAsync("u1", AiFeatureCodes.ConversationReply, null, default);
        Assert.Equal(AiKeySource.Platform, r.KeySource);
        Assert.Contains("platform_only", r.PolicyTrace);
        Assert.Null(r.ApiKeyPlaintext);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Resolves_Platform_ForAdminFeature_AlwaysRegardlessOfKey()
    {
        var (db, resolver, _) = await BuildAsync(mode: AiCredentialMode.ByokOnly, hasByokKey: true);
        var r = await resolver.ResolveAsync("u1", AiFeatureCodes.AdminContentGeneration, null, default);
        Assert.Equal(AiKeySource.Platform, r.KeySource);
        Assert.Contains("platform_only", r.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Resolves_Platform_ForAnonymousCaller()
    {
        var (db, resolver, _) = await BuildAsync();
        var r = await resolver.ResolveAsync(null, AiFeatureCodes.ConversationReply, null, default);
        Assert.Equal(AiKeySource.Platform, r.KeySource);
        Assert.Contains("no_user", r.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SetsBaseUrlOverride_ForKnownByokProviders()
    {
        var (db, resolver, _) = await BuildAsync(hasByokKey: true, byokProvider: "openrouter");
        var r = await resolver.ResolveAsync("u1", AiFeatureCodes.ConversationReply, null, default);
        Assert.Equal(AiKeySource.Byok, r.KeySource);
        Assert.Equal("https://openrouter.ai/api/v1", r.BaseUrlOverride);
        await db.DisposeAsync();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler());
    }
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
