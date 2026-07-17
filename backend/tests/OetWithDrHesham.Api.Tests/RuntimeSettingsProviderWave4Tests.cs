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
/// Wave 4 (final backend wave) of the env→DB config migration. Covers the new
/// FX, Storage, PdfExtraction, Pronunciation, and Auth-token groups plus the
/// extensions to the existing Billing (core, non-gateway), OAuth (LinkedIn +
/// per-provider toggles), and Push (WebPush enablement) records. Verifies the
/// merge semantics (DB override wins; null DB field falls back to env defaults),
/// secret round-trips through the Data-Protection protector (FX ApiKey, Storage
/// AccessKeyId/SecretAccessKey, PdfExtraction AzureApiKey, LinkedIn ClientId/
/// ClientSecret), and env fallback when no overrides exist.
/// </summary>
public sealed class RuntimeSettingsProviderWave4Tests
{
    [Fact]
    public async Task GetAsync_MergesWave4DatabaseOverridesOverConfiguredDefaults()
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

                // FX
                FxBaseCurrency = "gbp",
                FxApiKeyEncrypted = protector.Protect("fx-app-id"),
                FxApiBaseUrl = "https://fx.db-override.test/api",
                FxDynamicPricingEnabled = true,

                // Billing core (non-gateway)
                BillingCheckoutBaseUrl = "https://checkout.db-override.test",
                BillingWebhookMaxAgeSeconds = 120,
                BillingWebhookMaxAttempts = 9,
                BillingDefaultCurrency = "usd",
                BillingDefaultRegion = "gulf",
                WalletCurrency = "eur",
                WalletTopUpTiersJson = "[{\"Amount\":20,\"Credits\":7,\"Bonus\":1,\"Label\":\"DB tier\",\"IsPopular\":true}]",
                PayPalUseSandbox = false,
                PayPalApiBaseUrl = "https://api-m.paypal.db-override.test",

                // Storage (S3)
                StorageProvider = "s3",
                StorageBucketName = "oet-media-db",
                StorageEndpointUrl = "https://ams3.db-override.test",
                StorageAccessKeyIdEncrypted = protector.Protect("AKIA_DB_OVERRIDE"),
                StorageSecretAccessKeyEncrypted = protector.Protect("s3-secret-db"),
                StorageAwsRegion = "eu-west-2",
                StorageSignedReadTtlSeconds = 7200,
                StorageContentUploadMaxAudioBytes = 200L * 1024 * 1024,
                StorageContentUploadMaxPdfBytes = 30L * 1024 * 1024,
                StorageContentUploadMaxImageBytes = 6L * 1024 * 1024,
                StorageContentUploadMaxZipBytes = 600L * 1024 * 1024,
                StorageContentUploadMaxZipEntries = 7000,
                StorageContentUploadMaxZipEntryBytes = 160L * 1024 * 1024,
                StorageContentUploadMaxZipUncompressedBytes = 3L * 1024 * 1024 * 1024,
                StorageContentUploadMaxZipCompressionRatio = 120.0,
                StorageContentUploadChunkSizeBytes = 16L * 1024 * 1024,
                StorageContentUploadStagingTtlHours = 48,

                // PdfExtraction
                PdfExtractionProvider = "azure",
                PdfExtractionAzureEndpoint = "https://pdf.db-override.test/",
                PdfExtractionAzureApiKeyEncrypted = protector.Protect("azure-docintel-db"),
                PdfExtractionMinTextLengthForSuccess = 80,

                // Pronunciation (non-credential)
                PronunciationProvider = "whisper",
                PronunciationAzureSpeechRegion = "uksouth",
                PronunciationAzureLocale = "en-US",
                PronunciationWhisperBaseUrl = "https://whisper.db-override.test/v1",
                PronunciationWhisperModel = "whisper-large-v3",
                PronunciationGeminiBaseUrl = "https://gemini.db-override.test/v1beta",
                PronunciationGeminiModel = "gemini-3.5-pro",
                PronunciationMaxAudioBytes = 20L * 1024 * 1024,
                PronunciationAudioRetentionDays = 60,
                PronunciationFreeTierWeeklyAttemptLimit = 30,
                PronunciationFreeTierWindowDays = 14,

                // Auth — LinkedIn + per-provider toggles
                LinkedInClientIdEncrypted = protector.Protect("linkedin-client-id-db"),
                LinkedInClientSecretEncrypted = protector.Protect("linkedin-client-secret-db"),
                LinkedInEnabled = true,
                GoogleAuthEnabled = true,
                FacebookAuthEnabled = false,

                // Auth tokens (safe subset)
                AuthTokenAccessTokenLifetimeSeconds = 1800,
                AuthTokenRefreshTokenLifetimeSeconds = 1_209_600,
                AuthTokenOtpLifetimeSeconds = 600,
                AuthTokenAuthenticatorIssuer = "OET DB Issuer",

                // Web push
                WebPushEnabled = true,
            });
            await db.SaveChangesAsync();
        }

        var provider = BuildProvider(sp,
            fx: new FxOptions(),
            storage: new StorageOptions(),
            pdfExtraction: new PdfExtractionOptions(),
            pronunciation: new PronunciationOptions(),
            authTokens: new AuthTokenOptions(),
            billing: new BillingOptions(),
            oauth: new ExternalAuthOptions(),
            webPush: new WebPushOptions());

        var settings = await provider.GetAsync();

        // ── FX: DB overrides win; ApiKey secret round-trips; base currency uppercased ──
        Assert.Equal("GBP", settings.Fx.BaseCurrency);
        Assert.Equal("fx-app-id", settings.Fx.ApiKey);
        Assert.Equal("https://fx.db-override.test/api", settings.Fx.ApiBaseUrl);
        Assert.True(settings.Fx.DynamicPricingEnabled);

        // ── Billing core: DB overrides win; currencies uppercased; tiers parsed ──
        Assert.Equal("https://checkout.db-override.test", settings.Billing.CheckoutBaseUrl);
        Assert.Equal(120, settings.Billing.WebhookMaxAgeSeconds);
        Assert.Equal(9, settings.Billing.WebhookMaxAttempts);
        Assert.Equal("USD", settings.Billing.DefaultCurrency);
        Assert.Equal("GULF", settings.Billing.DefaultRegion);
        Assert.Equal("EUR", settings.Billing.WalletCurrency);
        Assert.Single(settings.Billing.WalletTopUpTiers);
        Assert.Equal(20, settings.Billing.WalletTopUpTiers[0].Amount);
        Assert.Equal("DB tier", settings.Billing.WalletTopUpTiers[0].Label);
        Assert.False(settings.Billing.PayPalUseSandbox);
        Assert.Equal("https://api-m.paypal.db-override.test", settings.Billing.PayPalApiBaseUrl);

        // ── Storage: DB overrides win; both secrets round-trip; IsConfigured true ──
        Assert.Equal("s3", settings.Storage.Provider);
        Assert.Equal("oet-media-db", settings.Storage.BucketName);
        Assert.Equal("https://ams3.db-override.test", settings.Storage.EndpointUrl);
        Assert.Equal("AKIA_DB_OVERRIDE", settings.Storage.AccessKeyId);
        Assert.Equal("s3-secret-db", settings.Storage.SecretAccessKey);
        Assert.Equal("eu-west-2", settings.Storage.AwsRegion);
        Assert.Equal(7200, settings.Storage.SignedReadTtlSeconds);
        Assert.Equal(200L * 1024 * 1024, settings.Storage.MaxAudioBytes);
        Assert.Equal(7000, settings.Storage.MaxZipEntries);
        Assert.Equal(120.0, settings.Storage.MaxZipCompressionRatio);
        Assert.Equal(48, settings.Storage.StagingTtlHours);
        Assert.True(settings.Storage.IsConfigured);

        // ── PdfExtraction: DB overrides win; Azure key secret round-trips ──
        Assert.Equal("azure", settings.PdfExtraction.Provider);
        Assert.Equal("https://pdf.db-override.test/", settings.PdfExtraction.AzureEndpoint);
        Assert.Equal("azure-docintel-db", settings.PdfExtraction.AzureApiKey);
        Assert.Equal(80, settings.PdfExtraction.MinTextLengthForSuccess);

        // ── Pronunciation (non-credential): DB overrides win ──
        Assert.Equal("whisper", settings.Pronunciation.Provider);
        Assert.Equal("uksouth", settings.Pronunciation.AzureSpeechRegion);
        Assert.Equal("en-US", settings.Pronunciation.AzureLocale);
        Assert.Equal("https://whisper.db-override.test/v1", settings.Pronunciation.WhisperBaseUrl);
        Assert.Equal("whisper-large-v3", settings.Pronunciation.WhisperModel);
        Assert.Equal("https://gemini.db-override.test/v1beta", settings.Pronunciation.GeminiBaseUrl);
        Assert.Equal("gemini-3.5-pro", settings.Pronunciation.GeminiModel);
        Assert.Equal(20L * 1024 * 1024, settings.Pronunciation.MaxAudioBytes);
        Assert.Equal(60, settings.Pronunciation.AudioRetentionDays);
        Assert.Equal(30, settings.Pronunciation.FreeTierWeeklyAttemptLimit);
        Assert.Equal(14, settings.Pronunciation.FreeTierWindowDays);

        // ── OAuth — LinkedIn (secret id + secret round-trip) + toggles ──
        Assert.Equal("linkedin-client-id-db", settings.OAuth.LinkedInClientId);
        Assert.Equal("linkedin-client-secret-db", settings.OAuth.LinkedInClientSecret);
        Assert.True(settings.OAuth.LinkedInEnabled);
        Assert.True(settings.OAuth.GoogleAuthEnabled);
        Assert.False(settings.OAuth.FacebookAuthEnabled);

        // ── Auth tokens (safe subset): seconds → TimeSpan; issuer override ──
        Assert.Equal(TimeSpan.FromSeconds(1800), settings.AuthTokens.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromSeconds(1_209_600), settings.AuthTokens.RefreshTokenLifetime);
        Assert.Equal(TimeSpan.FromSeconds(600), settings.AuthTokens.OtpLifetime);
        Assert.Equal("OET DB Issuer", settings.AuthTokens.AuthenticatorIssuer);

        // ── Web push enablement ──
        Assert.True(settings.Push.WebPushEnabled);
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
            fx: new FxOptions
            {
                BaseCurrency = "EUR",
                ApiKey = "env-fx-key",
                ApiBaseUrl = "https://fx.env.test/api",
                DynamicPricingEnabled = true,
            },
            storage: new StorageOptions
            {
                Provider = "s3",
                BucketName = "env-bucket",
                AccessKeyId = "env-access-key",
                SecretAccessKey = "env-secret-key",
                AwsRegion = "ap-southeast-2",
            },
            pdfExtraction: new PdfExtractionOptions
            {
                Provider = "pdfpig",
                AzureEndpoint = "https://pdf.env.test/",
                AzureApiKey = "env-azure-key",
                MinTextLengthForSuccess = 40,
            },
            pronunciation: new PronunciationOptions
            {
                Provider = "gemini",
                FreeTierWeeklyAttemptLimit = 5,
                FreeTierWindowDays = 3,
            },
            authTokens: new AuthTokenOptions
            {
                AccessTokenLifetime = TimeSpan.FromMinutes(15),
                RefreshTokenLifetime = TimeSpan.FromDays(30),
                OtpLifetime = TimeSpan.FromMinutes(10),
                AuthenticatorIssuer = "OET Env Issuer",
            },
            billing: new BillingOptions
            {
                CheckoutBaseUrl = "https://checkout.env.test",
                WebhookMaxAgeSeconds = 250,
                WebhookMaxAttempts = 4,
                DefaultCurrency = "AED",
                DefaultRegion = "UK",
                Wallet = new WalletBillingOptions { Currency = "USD" },
                PayPal = new PayPalBillingOptions { UseSandbox = false, ApiBaseUrl = "https://api-m.env.test" },
            },
            oauth: new ExternalAuthOptions
            {
                LinkedIn = new ExternalAuthProviderOptions { Enabled = true, ClientId = "env-linkedin-id", ClientSecret = "env-linkedin-secret" },
                Google = new ExternalAuthProviderOptions { Enabled = true },
                Facebook = new ExternalAuthProviderOptions { Enabled = false },
            },
            webPush: new WebPushOptions { Enabled = true });

        var settings = await provider.GetAsync();

        // FX env fallback (incl. plaintext key from env when no cipher).
        Assert.Equal("EUR", settings.Fx.BaseCurrency);
        Assert.Equal("env-fx-key", settings.Fx.ApiKey);
        Assert.Equal("https://fx.env.test/api", settings.Fx.ApiBaseUrl);
        Assert.True(settings.Fx.DynamicPricingEnabled);

        // Billing core env fallback (currencies uppercased).
        Assert.Equal("https://checkout.env.test", settings.Billing.CheckoutBaseUrl);
        Assert.Equal(250, settings.Billing.WebhookMaxAgeSeconds);
        Assert.Equal(4, settings.Billing.WebhookMaxAttempts);
        Assert.Equal("AED", settings.Billing.DefaultCurrency);
        Assert.Equal("UK", settings.Billing.DefaultRegion);
        Assert.Equal("USD", settings.Billing.WalletCurrency);
        Assert.False(settings.Billing.PayPalUseSandbox);
        Assert.Equal("https://api-m.env.test", settings.Billing.PayPalApiBaseUrl);

        // Storage env fallback (plaintext credentials from env when no cipher).
        Assert.Equal("s3", settings.Storage.Provider);
        Assert.Equal("env-bucket", settings.Storage.BucketName);
        Assert.Equal("env-access-key", settings.Storage.AccessKeyId);
        Assert.Equal("env-secret-key", settings.Storage.SecretAccessKey);
        Assert.Equal("ap-southeast-2", settings.Storage.AwsRegion);
        Assert.True(settings.Storage.IsConfigured);

        // PdfExtraction env fallback.
        Assert.Equal("pdfpig", settings.PdfExtraction.Provider);
        Assert.Equal("https://pdf.env.test/", settings.PdfExtraction.AzureEndpoint);
        Assert.Equal("env-azure-key", settings.PdfExtraction.AzureApiKey);
        Assert.Equal(40, settings.PdfExtraction.MinTextLengthForSuccess);

        // Pronunciation env fallback.
        Assert.Equal("gemini", settings.Pronunciation.Provider);
        Assert.Equal(5, settings.Pronunciation.FreeTierWeeklyAttemptLimit);
        Assert.Equal(3, settings.Pronunciation.FreeTierWindowDays);
        // Defaults applied when neither DB nor env set them.
        Assert.Equal("en-GB", settings.Pronunciation.AzureLocale);
        Assert.Equal("whisper-1", settings.Pronunciation.WhisperModel);

        // OAuth LinkedIn env fallback (plaintext from env when no cipher) + toggles.
        Assert.Equal("env-linkedin-id", settings.OAuth.LinkedInClientId);
        Assert.Equal("env-linkedin-secret", settings.OAuth.LinkedInClientSecret);
        Assert.True(settings.OAuth.LinkedInEnabled);
        Assert.True(settings.OAuth.GoogleAuthEnabled);
        Assert.False(settings.OAuth.FacebookAuthEnabled);

        // Auth tokens env fallback.
        Assert.Equal(TimeSpan.FromMinutes(15), settings.AuthTokens.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(30), settings.AuthTokens.RefreshTokenLifetime);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.AuthTokens.OtpLifetime);
        Assert.Equal("OET Env Issuer", settings.AuthTokens.AuthenticatorIssuer);

        // Web push enablement env fallback.
        Assert.True(settings.Push.WebPushEnabled);
    }

    [Fact]
    public async Task GetAsync_AppliesHardDefaultsWhenNeitherDbNorEnvSet()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddDataProtection();
        await using var sp = services.BuildServiceProvider();

        // FxOptions defaults BaseCurrency=USD; StorageOptions defaults Provider=local;
        // everything else uses the resolver's hard defaults.
        var provider = BuildProvider(sp,
            fx: new FxOptions { ApiBaseUrl = null },
            storage: new StorageOptions(),
            pdfExtraction: new PdfExtractionOptions(),
            pronunciation: new PronunciationOptions(),
            authTokens: new AuthTokenOptions(),
            billing: new BillingOptions(),
            oauth: new ExternalAuthOptions(),
            webPush: new WebPushOptions());

        var settings = await provider.GetAsync();

        Assert.Equal("USD", settings.Fx.BaseCurrency);
        Assert.Null(settings.Fx.ApiKey);
        Assert.False(settings.Fx.DynamicPricingEnabled);

        // Storage defaults to local; no S3 credentials → not configured.
        Assert.Equal("local", settings.Storage.Provider);
        Assert.False(settings.Storage.IsConfigured);
        Assert.Equal("us-east-1", settings.Storage.AwsRegion);
        Assert.Equal(3600, settings.Storage.SignedReadTtlSeconds);

        // Billing core hard defaults.
        Assert.Equal("GBP", settings.Billing.DefaultCurrency);
        Assert.Equal("ROW", settings.Billing.DefaultRegion);
        Assert.Equal("AUD", settings.Billing.WalletCurrency);
        Assert.True(settings.Billing.PayPalUseSandbox);
        // Env defaults from BillingOptions are inherited (4 historic wallet tiers).
        Assert.Equal(4, settings.Billing.WalletTopUpTiers.Count);

        // Pronunciation hard defaults.
        Assert.Equal("auto", settings.Pronunciation.Provider);
        Assert.Equal("en-GB", settings.Pronunciation.AzureLocale);
        Assert.Equal(45, settings.Pronunciation.AudioRetentionDays);

        // Web push disabled by default.
        Assert.False(settings.Push.WebPushEnabled);
        // LinkedIn disabled by default; no credentials.
        Assert.False(settings.OAuth.LinkedInEnabled);
        Assert.Null(settings.OAuth.LinkedInClientId);
    }

    private static RuntimeSettingsProvider BuildProvider(
        IServiceProvider sp,
        FxOptions fx,
        StorageOptions storage,
        PdfExtractionOptions pdfExtraction,
        PronunciationOptions pronunciation,
        AuthTokenOptions authTokens,
        BillingOptions billing,
        ExternalAuthOptions oauth,
        WebPushOptions webPush)
        => new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            sp.GetRequiredService<IDataProtectionProvider>(),
            Options.Create(new BrevoOptions()),
            Options.Create(billing),
            Options.Create(oauth),
            Options.Create(new UploadScannerOptions()),
            Options.Create(webPush),
            Options.Create(new ZoomOptions()),
            Options.Create(new SoketiOptions()),
            Options.Create(new DataRetentionOptions()),
            Options.Create(new ExpertAutoAssignmentOptions()),
            Options.Create(new PasswordPolicyOptions()),
            Options.Create(new AiAssistantOptions()),
            Options.Create(new AiProviderOptions()),
            Options.Create(new AiToolOptions()),
            Options.Create(new WritingV2Options()),
            Options.Create(new PlatformOptions()),
            Options.Create(new TwilioOptions()),
            Options.Create(new WhatsAppOptions()),
            Options.Create(fx),
            Options.Create(storage),
            Options.Create(pdfExtraction),
            Options.Create(pronunciation),
            Options.Create(authTokens),
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
