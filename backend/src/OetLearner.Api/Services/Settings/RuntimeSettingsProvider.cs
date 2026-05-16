using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Settings;

/// <summary>
/// Default <see cref="IRuntimeSettingsProvider"/>. Mirrors the
/// <c>ConversationOptionsProvider</c> pattern: registered as a singleton,
/// reads the DB row through a short-lived service scope, merges over the
/// IOptions-bound env defaults, and caches the merged result in
/// <see cref="IMemoryCache"/> for 30 seconds.
/// </summary>
public sealed class RuntimeSettingsProvider : IRuntimeSettingsProvider
{
    private const string CacheKey = "runtime-settings:effective:v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _protector;
    private readonly IOptions<BrevoOptions> _brevo;
    private readonly IOptions<BillingOptions> _billing;
    private readonly IOptions<ExternalAuthOptions> _oauth;
    private readonly IOptionsMonitor<SmtpOptions> _smtp;
    private readonly IConfiguration _config;

    public RuntimeSettingsProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IDataProtectionProvider dp,
        IOptions<BrevoOptions> brevo,
        IOptions<BillingOptions> billing,
        IOptions<ExternalAuthOptions> oauth,
        IOptionsMonitor<SmtpOptions> smtp,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _protector = dp.CreateProtector("RuntimeSettings.Secret.v1");
        _brevo = brevo;
        _billing = billing;
        _oauth = oauth;
        _smtp = smtp;
        _config = config;
    }

    public async Task<EffectiveSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue<EffectiveSettings>(CacheKey, out var cached) && cached is not null)
            return cached;

        var row = await LoadRowAsync(ct) ?? new RuntimeSettingsRow { Id = "default" };
        var merged = Merge(row);
        _cache.Set(CacheKey, merged, CacheTtl);
        return merged;
    }

    public async Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default)
        => await LoadRowAsync(ct) ?? new RuntimeSettingsRow { Id = "default" };

    public void Invalidate() => _cache.Remove(CacheKey);

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch { return null; }
    }

    private async Task<RuntimeSettingsRow?> LoadRowAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await db.RuntimeSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == "default", ct);
    }

    private EffectiveSettings Merge(RuntimeSettingsRow r)
    {
        var brevo = _brevo.Value;
        var billing = _billing.Value;
        var stripe = billing.Stripe;
        var oauth = _oauth.Value;
        var smtp = _smtp.CurrentValue;

        var email = new EmailSettings(
            BrevoApiKey: Unprotect(r.BrevoApiKeyEncrypted) ?? NullIfEmpty(brevo.ApiKey),
            BrevoEmailVerificationTemplateId: r.BrevoEmailVerificationTemplateId ?? brevo.EmailVerificationTemplateId,
            BrevoPasswordResetTemplateId: r.BrevoPasswordResetTemplateId ?? brevo.PasswordResetTemplateId,
            SmtpHost: Coalesce(r.SmtpHost, smtp.Host),
            SmtpPort: r.SmtpPort ?? (smtp.Port == 0 ? null : smtp.Port),
            SmtpUsername: Coalesce(r.SmtpUsername, smtp.Username),
            SmtpPassword: Unprotect(r.SmtpPasswordEncrypted) ?? NullIfEmpty(smtp.Password),
            SmtpFromAddress: Coalesce(r.SmtpFromAddress, smtp.FromEmail),
            SmtpFromName: Coalesce(r.SmtpFromName, smtp.FromName));

        var bill = new BillingSettings(
            StripeSecretKey: Unprotect(r.StripeSecretKeyEncrypted) ?? NullIfEmpty(stripe.SecretKey),
            StripePublishableKey: Coalesce(r.StripePublishableKey, stripe.PublishableKey),
            StripeWebhookSecret: Unprotect(r.StripeWebhookSecretEncrypted) ?? NullIfEmpty(stripe.WebhookSecret),
            StripeSuccessUrl: Coalesce(r.StripeSuccessUrl, stripe.SuccessUrl),
            StripeCancelUrl: Coalesce(r.StripeCancelUrl, stripe.CancelUrl));

        // Sentry is wired from raw configuration (Sentry:Dsn) rather than an
        // IOptions binding, so fall back to IConfiguration for env defaults.
        var sentry = new SentrySettings(
            Dsn: Coalesce(r.SentryDsn, _config["Sentry:Dsn"]),
            Environment: Coalesce(r.SentryEnvironment, _config["Sentry:Environment"]),
            SampleRate: r.SentrySampleRate ?? ParseDouble(_config["Sentry:TracesSampleRate"]));

        // Backup secrets are operations-only (read by the Linux backup
        // job, not the .NET host). The DB row is the single source of
        // truth here so admins can rotate keys without re-deploying.
        var backup = new BackupSettings(
            S3Url: r.BackupS3Url,
            AwsAccessKeyId: r.BackupAwsAccessKeyId,
            AwsSecretAccessKey: Unprotect(r.BackupAwsSecretAccessKeyEncrypted),
            GpgPassphrase: Unprotect(r.BackupGpgPassphraseEncrypted),
            AlertWebhook: r.BackupAlertWebhook);

        var google = oauth.Google;
        var facebook = oauth.Facebook;
        var oa = new OAuthSettings(
            GoogleClientId: Coalesce(r.GoogleClientId, google.ClientId),
            GoogleClientSecret: Unprotect(r.GoogleClientSecretEncrypted) ?? NullIfEmpty(google.ClientSecret),
            AppleClientId: r.AppleClientId,
            AppleTeamId: r.AppleTeamId,
            AppleKeyId: r.AppleKeyId,
            ApplePrivateKey: Unprotect(r.ApplePrivateKeyEncrypted),
            FacebookAppId: Coalesce(r.FacebookAppId, facebook.ClientId),
            FacebookAppSecret: Unprotect(r.FacebookAppSecretEncrypted) ?? NullIfEmpty(facebook.ClientSecret));

        var push = new PushSettings(
            ApnsKeyId: r.ApnsKeyId,
            ApnsTeamId: r.ApnsTeamId,
            ApnsBundleId: r.ApnsBundleId,
            ApnsAuthKey: Unprotect(r.ApnsAuthKeyEncrypted),
            FcmServerKey: Unprotect(r.FcmServerKeyEncrypted),
            FcmProjectId: r.FcmProjectId);

        return new EffectiveSettings(
            Email: email,
            Billing: bill,
            Sentry: sentry,
            Backup: backup,
            OAuth: oa,
            Push: push,
            UpdatedByUserId: r.UpdatedByUserId,
            UpdatedByUserName: r.UpdatedByUserName,
            UpdatedAt: r.UpdatedAt == default ? null : r.UpdatedAt);
    }

    private static string? Coalesce(string? @override, string? fallback)
        => !string.IsNullOrWhiteSpace(@override) ? @override
         : !string.IsNullOrWhiteSpace(fallback) ? fallback
         : null;

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private static double? ParseDouble(string? s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
}
