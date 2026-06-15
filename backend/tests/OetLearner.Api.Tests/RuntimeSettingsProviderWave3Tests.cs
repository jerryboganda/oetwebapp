using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Services.Writing.Configuration;

namespace OetLearner.Api.Tests;

/// <summary>
/// Wave 3 of the env→DB config migration: the Email partial-coverage gap (the
/// remaining Brevo template IDs, Brevo webhook secret + master flag, and the
/// SMTP enable/SSL flags) and the new Messaging group (Twilio SMS + WhatsApp
/// Business Cloud). Verifies the merge semantics (DB override wins; null DB
/// field falls back to env/appsettings), secret round-trips (Brevo webhook
/// secret, Twilio auth token, WhatsApp access token) via the Data-Protection
/// protector, and env-fallback when no overrides exist.
/// </summary>
public sealed class RuntimeSettingsProviderWave3Tests
{
    [Fact]
    public async Task GetAsync_MergesWave3DatabaseOverridesOverConfiguredDefaults()
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

                // Email partial-coverage gap
                BrevoWelcomeTemplateId = 101,
                BrevoPasswordChangedTemplateId = 102,
                BrevoMfaEnabledTemplateId = 103,
                BrevoAdminInviteTemplateId = 104,
                BrevoSecurityAlertTemplateId = 105,
                BrevoReviewCompletedTemplateId = 106,
                BrevoWebhookSecretEncrypted = protector.Protect("brevo-webhook-secret"),
                BrevoEnabled = true,
                SmtpEnabled = true,
                SmtpEnableSsl = false,

                // Messaging — Twilio
                TwilioEnabled = true,
                TwilioApiBaseUrl = "https://api.twilio.db-override.test",
                TwilioAccountSid = "AC_db_override",
                TwilioAuthTokenEncrypted = protector.Protect("twilio-auth-token"),
                TwilioFromNumber = "+15551234567",
                TwilioMessagingServiceSid = "MG_db_override",

                // Messaging — WhatsApp
                WhatsAppEnabled = true,
                WhatsAppApiBaseUrl = "https://graph.db-override.test/v21.0",
                WhatsAppAccessTokenEncrypted = protector.Protect("whatsapp-access-token"),
                WhatsAppPhoneNumberId = "PN_db_override",
                WhatsAppFallbackTemplateName = "billing_reminder",
            });
            await db.SaveChangesAsync();
        }

        var provider = BuildProvider(sp,
            brevo: new BrevoOptions(),
            smtp: new SmtpOptions(),
            twilio: new TwilioOptions(),
            whatsApp: new WhatsAppOptions());

        var settings = await provider.GetAsync();

        // ── Email partial-coverage gap: DB overrides win; secret round-trips ──
        Assert.Equal(101, settings.Email.BrevoWelcomeTemplateId);
        Assert.Equal(102, settings.Email.BrevoPasswordChangedTemplateId);
        Assert.Equal(103, settings.Email.BrevoMfaEnabledTemplateId);
        Assert.Equal(104, settings.Email.BrevoAdminInviteTemplateId);
        Assert.Equal(105, settings.Email.BrevoSecurityAlertTemplateId);
        Assert.Equal(106, settings.Email.BrevoReviewCompletedTemplateId);
        Assert.Equal("brevo-webhook-secret", settings.Email.BrevoWebhookSecret);
        Assert.True(settings.Email.BrevoEnabled);
        Assert.True(settings.Email.SmtpEnabled);
        Assert.False(settings.Email.SmtpEnableSsl);

        // ── Messaging — Twilio: DB overrides win; auth token decrypts ──
        Assert.True(settings.Messaging.TwilioEnabled);
        Assert.Equal("https://api.twilio.db-override.test", settings.Messaging.TwilioApiBaseUrl);
        Assert.Equal("AC_db_override", settings.Messaging.TwilioAccountSid);
        Assert.Equal("twilio-auth-token", settings.Messaging.TwilioAuthToken);
        Assert.Equal("+15551234567", settings.Messaging.TwilioFromNumber);
        Assert.Equal("MG_db_override", settings.Messaging.TwilioMessagingServiceSid);
        Assert.True(settings.Messaging.IsTwilioConfigured);

        // ── Messaging — WhatsApp: DB overrides win; access token decrypts ──
        Assert.True(settings.Messaging.WhatsAppEnabled);
        Assert.Equal("https://graph.db-override.test/v21.0", settings.Messaging.WhatsAppApiBaseUrl);
        Assert.Equal("whatsapp-access-token", settings.Messaging.WhatsAppAccessToken);
        Assert.Equal("PN_db_override", settings.Messaging.WhatsAppPhoneNumberId);
        Assert.Equal("billing_reminder", settings.Messaging.WhatsAppFallbackTemplateName);
        Assert.True(settings.Messaging.IsWhatsAppConfigured);
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
            brevo: new BrevoOptions
            {
                Enabled = true,
                WelcomeTemplateId = 11,
                ReviewCompletedTemplateId = 12,
                WebhookSecret = "env-brevo-webhook-secret",
            },
            smtp: new SmtpOptions { Enabled = true, EnableSsl = false },
            twilio: new TwilioOptions
            {
                Enabled = true,
                AccountSid = "AC_env",
                AuthToken = "env-twilio-token",
                FromNumber = "+15557654321",
            },
            whatsApp: new WhatsAppOptions
            {
                Enabled = true,
                AccessToken = "env-whatsapp-token",
                PhoneNumberId = "PN_env",
            });

        var settings = await provider.GetAsync();

        // Email gap env fallback.
        Assert.Equal(11, settings.Email.BrevoWelcomeTemplateId);
        Assert.Equal(12, settings.Email.BrevoReviewCompletedTemplateId);
        Assert.Equal("env-brevo-webhook-secret", settings.Email.BrevoWebhookSecret);
        Assert.True(settings.Email.BrevoEnabled);
        Assert.True(settings.Email.SmtpEnabled);
        Assert.False(settings.Email.SmtpEnableSsl);
        // No DB override and no env value → null template id.
        Assert.Null(settings.Email.BrevoMfaEnabledTemplateId);

        // Messaging env fallback (incl. plaintext secrets from env when no cipher).
        Assert.True(settings.Messaging.TwilioEnabled);
        Assert.Equal("AC_env", settings.Messaging.TwilioAccountSid);
        Assert.Equal("env-twilio-token", settings.Messaging.TwilioAuthToken);
        Assert.Equal("+15557654321", settings.Messaging.TwilioFromNumber);
        // Default API base URL applied when neither DB nor env set it.
        Assert.Equal("https://api.twilio.com", settings.Messaging.TwilioApiBaseUrl);
        Assert.True(settings.Messaging.IsTwilioConfigured);

        Assert.True(settings.Messaging.WhatsAppEnabled);
        Assert.Equal("env-whatsapp-token", settings.Messaging.WhatsAppAccessToken);
        Assert.Equal("PN_env", settings.Messaging.WhatsAppPhoneNumberId);
        Assert.Equal("https://graph.facebook.com/v20.0", settings.Messaging.WhatsAppApiBaseUrl);
        Assert.True(settings.Messaging.IsWhatsAppConfigured);
    }

    [Fact]
    public async Task GetAsync_MessagingNotConfiguredWhenDisabledOrCredentialsMissing()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddDataProtection();
        await using var sp = services.BuildServiceProvider();

        var provider = BuildProvider(sp,
            brevo: new BrevoOptions(),
            smtp: new SmtpOptions(),
            // Twilio enabled but no credentials; WhatsApp has credentials but disabled.
            twilio: new TwilioOptions { Enabled = true },
            whatsApp: new WhatsAppOptions { Enabled = false, AccessToken = "tok", PhoneNumberId = "pn" });

        var settings = await provider.GetAsync();

        Assert.False(settings.Messaging.IsTwilioConfigured);
        Assert.False(settings.Messaging.IsWhatsAppConfigured);
    }

    private static RuntimeSettingsProvider BuildProvider(
        IServiceProvider sp,
        BrevoOptions brevo,
        SmtpOptions smtp,
        TwilioOptions twilio,
        WhatsAppOptions whatsApp)
        => new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            sp.GetRequiredService<IDataProtectionProvider>(),
            Options.Create(brevo),
            Options.Create(new BillingOptions()),
            Options.Create(new ExternalAuthOptions()),
            Options.Create(new UploadScannerOptions()),
            Options.Create(new WebPushOptions()),
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
            Options.Create(twilio),
            Options.Create(whatsApp),
            new StaticOptionsMonitor<SmtpOptions>(smtp),
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
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
