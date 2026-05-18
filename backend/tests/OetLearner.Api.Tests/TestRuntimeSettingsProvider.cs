using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

internal sealed class TestRuntimeSettingsProvider(EffectiveSettings settings) : IRuntimeSettingsProvider
{
    public static TestRuntimeSettingsProvider FromBillingOptions(BillingOptions options)
        => new(new EffectiveSettings(
            Email: new EmailSettings(
                BrevoEnabled: false,
                BrevoApiKey: null,
                BrevoEmailVerificationTemplateId: null,
                BrevoPasswordResetTemplateId: null,
                BrevoWelcomeTemplateId: null,
                BrevoPasswordChangedTemplateId: null,
                BrevoMfaEnabledTemplateId: null,
                BrevoAdminInviteTemplateId: null,
                BrevoSecurityAlertTemplateId: null,
                BrevoReviewCompletedTemplateId: null,
                SmtpEnabled: false,
                SmtpHost: null,
                SmtpPort: null,
                SmtpUsername: null,
                SmtpPassword: null,
                SmtpEnableSsl: true,
                SmtpFromAddress: null,
                SmtpFromName: null),
            Billing: new BillingSettings(
                options.Stripe.SecretKey,
                options.Stripe.PublishableKey,
                options.Stripe.WebhookSecret,
                options.Stripe.SuccessUrl,
                options.Stripe.CancelUrl),
            Sentry: new SentrySettings(null, null, null),
            Backup: new BackupSettings(null, null, null, null, null),
            OAuth: new OAuthSettings(
                GoogleEnabled: false,
                GoogleClientId: null,
                GoogleClientSecret: null,
                AppleClientId: null,
                AppleTeamId: null,
                AppleKeyId: null,
                ApplePrivateKey: null,
                FacebookEnabled: false,
                FacebookAppId: null,
                FacebookAppSecret: null,
                LinkedInEnabled: false,
                LinkedInClientId: null,
                LinkedInClientSecret: null),
            Push: new PushSettings(
                WebPushEnabled: false,
                WebPushSubject: null,
                WebPushPublicKey: null,
                WebPushPrivateKey: null,
                ApnsKeyId: null,
                ApnsTeamId: null,
                ApnsBundleId: null,
                ApnsAuthKey: null,
                FcmServerKey: null,
                FcmProjectId: null),
            UpdatedByUserId: null,
            UpdatedByUserName: null,
            UpdatedAt: null));

    public static TestRuntimeSettingsProvider ForBrevoEmail(
        string apiKey,
        string fromEmail,
        string? fromName = null)
        => new(new EffectiveSettings(
            Email: new EmailSettings(
                BrevoEnabled: true,
                BrevoApiKey: apiKey,
                BrevoEmailVerificationTemplateId: null,
                BrevoPasswordResetTemplateId: null,
                BrevoWelcomeTemplateId: null,
                BrevoPasswordChangedTemplateId: null,
                BrevoMfaEnabledTemplateId: null,
                BrevoAdminInviteTemplateId: null,
                BrevoSecurityAlertTemplateId: null,
                BrevoReviewCompletedTemplateId: null,
                SmtpEnabled: false,
                SmtpHost: null,
                SmtpPort: null,
                SmtpUsername: null,
                SmtpPassword: null,
                SmtpEnableSsl: true,
                SmtpFromAddress: fromEmail,
                SmtpFromName: fromName),
            Billing: new BillingSettings(null, null, null, null, null),
            Sentry: new SentrySettings(null, null, null),
            Backup: new BackupSettings(null, null, null, null, null),
            OAuth: new OAuthSettings(
                GoogleEnabled: false,
                GoogleClientId: null,
                GoogleClientSecret: null,
                AppleClientId: null,
                AppleTeamId: null,
                AppleKeyId: null,
                ApplePrivateKey: null,
                FacebookEnabled: false,
                FacebookAppId: null,
                FacebookAppSecret: null,
                LinkedInEnabled: false,
                LinkedInClientId: null,
                LinkedInClientSecret: null),
            Push: new PushSettings(
                WebPushEnabled: false,
                WebPushSubject: null,
                WebPushPublicKey: null,
                WebPushPrivateKey: null,
                ApnsKeyId: null,
                ApnsTeamId: null,
                ApnsBundleId: null,
                ApnsAuthKey: null,
                FcmServerKey: null,
                FcmProjectId: null),
            UpdatedByUserId: null,
            UpdatedByUserName: null,
            UpdatedAt: null));

    public Task<EffectiveSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);

    public Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default) => Task.FromResult(new RuntimeSettingsRow());

    public void Invalidate() { }

    public string Protect(string plain) => plain;

    public string? Unprotect(string? cipher) => cipher;
}
