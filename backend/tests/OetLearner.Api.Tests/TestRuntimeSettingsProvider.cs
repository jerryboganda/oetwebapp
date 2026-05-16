using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

internal sealed class TestRuntimeSettingsProvider(EffectiveSettings settings) : IRuntimeSettingsProvider
{
    public static TestRuntimeSettingsProvider FromBillingOptions(BillingOptions options)
        => new(new EffectiveSettings(
            Email: new EmailSettings(null, null, null, null, null, null, null, null, null),
            Billing: new BillingSettings(
                options.Stripe.SecretKey,
                options.Stripe.PublishableKey,
                options.Stripe.WebhookSecret,
                options.Stripe.SuccessUrl,
                options.Stripe.CancelUrl),
            Sentry: new SentrySettings(null, null, null),
            Backup: new BackupSettings(null, null, null, null, null),
            OAuth: new OAuthSettings(null, null, null, null, null, null, null, null),
            Push: new PushSettings(null, null, null, null, null, null),
            UpdatedByUserId: null,
            UpdatedByUserName: null,
            UpdatedAt: null));

    public Task<EffectiveSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);

    public Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default) => Task.FromResult(new RuntimeSettingsRow());

    public void Invalidate() { }

    public string Protect(string plain) => plain;

    public string? Unprotect(string? cipher) => cipher;
}
