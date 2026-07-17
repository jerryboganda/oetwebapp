using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiAssistant;
using OetWithDrHesham.Api.Services.AiTools;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Services.Writing.Configuration;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Wave 2 of the env→DB config migration: AiAssistant, AiGateway (AiProvider +
/// AiTool non-credential knobs), Writing, and Platform. Verifies the merge
/// semantics (DB override wins; null DB field falls back to env/appsettings),
/// a secret round-trip (Writing GCV key) via the Data-Protection protector, and
/// env-fallback when no overrides exist.
/// </summary>
public sealed class RuntimeSettingsProviderWave2Tests
{
    [Fact]
    public async Task GetAsync_MergesWave2DatabaseOverridesOverConfiguredDefaults()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddDataProtection();
        await using var sp = services.BuildServiceProvider();

        var protector = sp.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("RuntimeSettings.Secret.v1");

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.RuntimeSettings.Add(new RuntimeSettingsRow
            {
                Id = "default",

                // AI Assistant
                AiAssistantGlobalEnabled = true,
                AiAssistantMaxIterations = 25,
                AiAssistantMaxContextMessages = 100,
                AiAssistantMaxWriteFileSizeBytes = 4_194_304,
                AiAssistantEmbeddingModel = "text-embedding-3-large",

                // AI gateway / tooling knobs
                AiProviderProviderId = "openai-platform",
                AiProviderBaseUrl = "https://api.openai.com/v1",
                AiProviderDefaultModel = "gpt-4o-mini",
                AiProviderReasoningEffort = "high",
                AiProviderDefaultMaxTokens = 8192,
                AiProviderDefaultTemperature = 0.7,
                AiToolMaxToolCallsPerCompletion = 9,
                AiToolAllowedExternalHostsCsv = "api.example.com, API.Example.com ,api.other.test",
                AiToolExternalNetworkPerUserDailyCalls = 0,

                // Writing
                WritingCronsEnabled = false,
                WritingCoachEnabled = false,
                WritingCoachDailyCostCapPerLearnerUsd = 1.25m,
                WritingCoachMaxHintsPerSession = 12,
                WritingGcvApiKeyEncrypted = protector.Protect("gcv-secret-key"),
                WritingOcrEnabled = true,
                WritingTutorReviewQueueMaxDepth = 200,
                WritingGradeIdempotencyTtlHours = 6,

                // Platform
                PublicApiBaseUrl = "https://api.db-override.example",
                PublicWebBaseUrl = "https://app.db-override.example",
                FallbackEmailDomain = "db-override.invalid",
            });
            await db.SaveChangesAsync();
        }

        var provider = BuildProvider(sp,
            aiAssistant: new AiAssistantOptions(),
            aiProvider: new AiProviderOptions(),
            aiTool: new AiToolOptions(),
            writing: new WritingV2Options(),
            platform: new PlatformOptions());

        var settings = await provider.GetAsync();

        // ── AI Assistant: DB overrides win; unset fields fall back to defaults ──
        Assert.True(settings.AiAssistant.GlobalEnabled);
        Assert.Equal(25, settings.AiAssistant.MaxIterations);
        Assert.Equal(100, settings.AiAssistant.MaxContextMessages);
        Assert.Equal(4_194_304, settings.AiAssistant.MaxWriteFileSizeBytes);
        Assert.Equal("text-embedding-3-large", settings.AiAssistant.EmbeddingModel);
        // No DB override → env default (true) for RequireApprovalAlways.
        Assert.True(settings.AiAssistant.RequireApprovalAlways);
        Assert.Equal(300, settings.AiAssistant.CommandTimeoutSeconds);

        // ── AI gateway / tooling ──
        Assert.Equal("openai-platform", settings.AiGateway.ProviderId);
        Assert.Equal("https://api.openai.com/v1", settings.AiGateway.BaseUrl);
        Assert.Equal("gpt-4o-mini", settings.AiGateway.DefaultModel);
        Assert.Equal("high", settings.AiGateway.ReasoningEffort);
        Assert.Equal(8192, settings.AiGateway.DefaultMaxTokens);
        Assert.Equal(0.7, settings.AiGateway.DefaultTemperature);
        Assert.Equal(9, settings.AiGateway.MaxToolCallsPerCompletion);
        // CSV parsed: trimmed, lowercased, deduped.
        Assert.Equal(new[] { "api.example.com", "api.other.test" }, settings.AiGateway.AllowedExternalHosts);
        // 0 is a valid (disabled) value for the daily call budget.
        Assert.Equal(0, settings.AiGateway.ExternalNetworkPerUserDailyCalls);
        // No DB override → env default (30) for the grant cache TTL.
        Assert.Equal(30, settings.AiGateway.FeatureGrantCacheSeconds);

        // ── Writing: DB overrides win; secret round-trips through the protector ──
        Assert.False(settings.Writing.CronsEnabled);
        Assert.False(settings.Writing.CoachEnabled);
        Assert.Equal(1.25m, settings.Writing.CoachDailyCostCapPerLearnerUsd);
        Assert.Equal(12, settings.Writing.CoachMaxHintsPerSession);
        Assert.Equal("gcv-secret-key", settings.Writing.GcvApiKey);
        Assert.True(settings.Writing.OcrEnabled);
        Assert.Equal(200, settings.Writing.TutorReviewQueueMaxDepth);
        Assert.Equal(6, settings.Writing.GradeIdempotencyTtlHours);
        // No DB override → env default (true) for AppealsEnabled.
        Assert.True(settings.Writing.AppealsEnabled);

        // ── Platform ──
        Assert.Equal("https://api.db-override.example", settings.Platform.PublicApiBaseUrl);
        Assert.Equal("https://app.db-override.example", settings.Platform.PublicWebBaseUrl);
        Assert.Equal("db-override.invalid", settings.Platform.FallbackEmailDomain);
    }

    [Fact]
    public async Task GetAsync_FallsBackToConfiguredDefaultsWhenNoOverrides()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddDataProtection();
        await using var sp = services.BuildServiceProvider();

        var provider = BuildProvider(sp,
            aiAssistant: new AiAssistantOptions { MaxIterations = 7, EmbeddingModel = "env-embed-model" },
            aiProvider: new AiProviderOptions { DefaultModel = "env-model", BaseUrl = "https://env.example/v1", DefaultTemperature = 0.3 },
            aiTool: new AiToolOptions { MaxToolCallsPerCompletion = 6, AllowedExternalHosts = ["env.host.test"] },
            writing: new WritingV2Options { CoachMaxHintsPerSession = 42, GcvApiKey = "env-gcv-key" },
            platform: new PlatformOptions { PublicWebBaseUrl = "https://web.env.example", FallbackEmailDomain = "env.invalid" });

        var settings = await provider.GetAsync();

        // AI Assistant env fallback.
        Assert.Equal(7, settings.AiAssistant.MaxIterations);
        Assert.Equal("env-embed-model", settings.AiAssistant.EmbeddingModel);
        Assert.False(settings.AiAssistant.GlobalEnabled); // options default

        // AI gateway env fallback.
        Assert.Equal("env-model", settings.AiGateway.DefaultModel);
        Assert.Equal("https://env.example/v1", settings.AiGateway.BaseUrl);
        Assert.Equal(0.3, settings.AiGateway.DefaultTemperature);
        Assert.Equal(6, settings.AiGateway.MaxToolCallsPerCompletion);
        Assert.Equal(new[] { "env.host.test" }, settings.AiGateway.AllowedExternalHosts);

        // Writing env fallback (incl. plaintext GCV key from env when no DB cipher).
        Assert.Equal(42, settings.Writing.CoachMaxHintsPerSession);
        Assert.Equal("env-gcv-key", settings.Writing.GcvApiKey);
        Assert.True(settings.Writing.CronsEnabled); // options default

        // Platform env fallback.
        Assert.Equal("https://web.env.example", settings.Platform.PublicWebBaseUrl);
        Assert.Equal("env.invalid", settings.Platform.FallbackEmailDomain);
        Assert.Null(settings.Platform.PublicApiBaseUrl); // unset everywhere
    }

    private static RuntimeSettingsProvider BuildProvider(
        IServiceProvider sp,
        AiAssistantOptions aiAssistant,
        AiProviderOptions aiProvider,
        AiToolOptions aiTool,
        WritingV2Options writing,
        PlatformOptions platform)
        => new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            sp.GetRequiredService<IDataProtectionProvider>(),
            Options.Create(new BrevoOptions()),
            Options.Create(new BillingOptions()),
            Options.Create(new ExternalAuthOptions()),
            Options.Create(new UploadScannerOptions()),
            Options.Create(new WebPushOptions()),
            Options.Create(new ZoomOptions()),
            Options.Create(new SoketiOptions()),
            Options.Create(new DataRetentionOptions()),
            Options.Create(new ExpertAutoAssignmentOptions()),
            Options.Create(new PasswordPolicyOptions()),
            Options.Create(aiAssistant),
            Options.Create(aiProvider),
            Options.Create(aiTool),
            Options.Create(writing),
            Options.Create(platform),
            Options.Create(new TwilioOptions()),
            Options.Create(new WhatsAppOptions()),
            Options.Create(new FxOptions()),
            Options.Create(new StorageOptions()),
            Options.Create(new PdfExtractionOptions()),
            Options.Create(new PronunciationOptions()),
            Options.Create(new AuthTokenOptions()),
            new StaticOptionsMonitor<SmtpOptions>(new SmtpOptions()),
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment("Development"));

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OetWithDrHesham.Api.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
