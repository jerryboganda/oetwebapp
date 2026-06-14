using System.Data.Common;
using System.Text.Json;
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
    private readonly IOptions<UploadScannerOptions> _uploadScanner;
    private readonly IOptions<WebPushOptions> _webPush;
    private readonly IOptions<ZoomOptions> _zoom;
    private readonly IOptions<SoketiOptions> _soketi;
    private readonly IOptions<DataRetentionOptions> _dataRetention;
    private readonly IOptions<ExpertAutoAssignmentOptions> _expertAutoAssignment;
    private readonly IOptions<PasswordPolicyOptions> _passwordPolicy;
    private readonly IOptionsMonitor<SmtpOptions> _smtp;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _environment;

    public RuntimeSettingsProvider(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IDataProtectionProvider dp,
        IOptions<BrevoOptions> brevo,
        IOptions<BillingOptions> billing,
        IOptions<ExternalAuthOptions> oauth,
        IOptions<UploadScannerOptions> uploadScanner,
        IOptions<WebPushOptions> webPush,
        IOptions<ZoomOptions> zoom,
        IOptions<SoketiOptions> soketi,
        IOptions<DataRetentionOptions> dataRetention,
        IOptions<ExpertAutoAssignmentOptions> expertAutoAssignment,
        IOptions<PasswordPolicyOptions> passwordPolicy,
        IOptionsMonitor<SmtpOptions> smtp,
        IConfiguration config,
        IHostEnvironment environment)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _protector = dp.CreateProtector("RuntimeSettings.Secret.v1");
        _brevo = brevo;
        _billing = billing;
        _oauth = oauth;
        _uploadScanner = uploadScanner;
        _webPush = webPush;
        _zoom = zoom;
        _soketi = soketi;
        _dataRetention = dataRetention;
        _expertAutoAssignment = expertAutoAssignment;
        _passwordPolicy = passwordPolicy;
        _smtp = smtp;
        _config = config;
        _environment = environment;
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
        try
        {
            return await db.RuntimeSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == "default", ct);
        }
        catch (DbException ex) when (!_environment.IsProduction() && IsMissingRuntimeSettingsTable(ex))
        {
            return null;
        }
        catch (DbException ex) when (IsMissingRuntimeSettingsColumn(ex))
        {
            // A newly added override column is not present on this slot yet
            // (migration lag during a rolling deploy). Degrade gracefully to
            // env/appsettings defaults for ALL settings rather than failing
            // startup — the column is backfilled once its migration applies,
            // and every secret/override already falls back to its env value.
            return null;
        }
    }

    /// <summary>PostgreSQL <c>undefined_column</c> (42703) — a RuntimeSettings
    /// override column the code expects is not in the table yet.</summary>
    private static bool IsMissingRuntimeSettingsColumn(DbException ex)
        => ex.Message.Contains("42703", StringComparison.Ordinal)
           || (ex.Message.Contains("column", StringComparison.OrdinalIgnoreCase)
               && ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase));

    private static bool IsMissingRuntimeSettingsTable(DbException ex)
        => ex.Message.Contains("RuntimeSettings", StringComparison.OrdinalIgnoreCase)
           && (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase));

    private EffectiveSettings Merge(RuntimeSettingsRow r)
    {
        var brevo = _brevo.Value;
        var billing = _billing.Value;
        var stripeOptions = billing.Stripe;
        var paypal = billing.PayPal;
        var oauth = _oauth.Value;
        var smtp = _smtp.CurrentValue;
        var scanner = _uploadScanner.Value;
        var zoomOptions = _zoom.Value;

        var email = new EmailSettings(
            BrevoApiKey: Unprotect(r.BrevoApiKeyEncrypted) ?? NullIfEmpty(brevo.ApiKey),
            BrevoEmailVerificationTemplateId: r.BrevoEmailVerificationTemplateId ?? brevo.EmailVerificationTemplateId,
            BrevoPasswordResetTemplateId: r.BrevoPasswordResetTemplateId ?? brevo.PasswordResetTemplateId,
            SmtpHost: Coalesce(r.SmtpHost, smtp.Host),
            SmtpPort: r.SmtpPort ?? (smtp.Port == 0 ? null : smtp.Port),
            SmtpUsername: Coalesce(r.SmtpUsername, smtp.Username),
            SmtpPassword: Unprotect(r.SmtpPasswordEncrypted) ?? NullIfEmpty(smtp.Password),
            SmtpFromAddress: Coalesce(r.SmtpFromAddress, brevo.FromEmail, smtp.FromEmail),
            SmtpFromName: Coalesce(r.SmtpFromName, brevo.FromName, smtp.FromName));

        var bill = new BillingSettings(
            StripeSecretKey: Unprotect(r.StripeSecretKeyEncrypted) ?? NullIfEmpty(stripeOptions.SecretKey),
            StripePublishableKey: Coalesce(r.StripePublishableKey, stripeOptions.PublishableKey),
            StripeWebhookSecret: Unprotect(r.StripeWebhookSecretEncrypted) ?? NullIfEmpty(stripeOptions.WebhookSecret),
            StripeSuccessUrl: Coalesce(r.StripeSuccessUrl, stripeOptions.SuccessUrl),
            StripeCancelUrl: Coalesce(r.StripeCancelUrl, stripeOptions.CancelUrl),
            PayPalClientId: Coalesce(r.PayPalClientId, paypal.ClientId),
            PayPalClientSecret: Unprotect(r.PayPalClientSecretEncrypted) ?? NullIfEmpty(paypal.ClientSecret),
            PayPalWebhookId: Unprotect(r.PayPalWebhookIdEncrypted) ?? NullIfEmpty(paypal.WebhookId),
            PayPalSuccessUrl: Coalesce(r.PayPalSuccessUrl, paypal.SuccessUrl),
            PayPalCancelUrl: Coalesce(r.PayPalCancelUrl, paypal.CancelUrl),
            PayPalAdvancedCardsEnabled: r.PayPalAdvancedCardsEnabled ?? paypal.AdvancedCardsEnabled,
            PublicAppBaseUrl: NullIfEmpty(r.BillingPublicAppBaseUrl));

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
        var webPush = _webPush.Value;
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
            FcmProjectId: r.FcmProjectId,
            VapidSubject: Coalesce(r.VapidSubject, webPush.Subject),
            VapidPublicKey: Coalesce(r.VapidPublicKey, webPush.PublicKey),
            VapidPrivateKey: Unprotect(r.VapidPrivateKeyEncrypted) ?? NullIfEmpty(webPush.PrivateKey));

        var uploadScanner = new UploadScannerSettings(
            Provider: Coalesce(r.UploadScannerProvider, scanner.Provider) ?? "noop",
            Host: Coalesce(r.UploadScannerHost, scanner.Host) ?? "127.0.0.1",
            Port: r.UploadScannerPort ?? (scanner.Port <= 0 ? 3310 : scanner.Port),
            TimeoutSeconds: r.UploadScannerTimeoutSeconds ?? Math.Max(1, (int)Math.Ceiling(scanner.Timeout.TotalSeconds)),
            FailClosedOnError: r.UploadScannerFailClosedOnError ?? scanner.FailClosedOnError);

        var zoom = new ZoomSettings(
            Enabled: r.ZoomEnabled ?? zoomOptions.Enabled,
            AccountId: Coalesce(r.ZoomAccountId, zoomOptions.AccountId),
            ClientId: Coalesce(r.ZoomClientId, zoomOptions.ClientId),
            ClientSecret: ResolveStoredSecretOrFallback(r.ZoomClientSecretEncrypted, zoomOptions.ClientSecret, "zoom.clientSecret"),
            ApiBaseUrl: Coalesce(r.ZoomApiBaseUrl, zoomOptions.ApiBaseUrl, "https://api.zoom.us/v2")!,
            TokenUrl: Coalesce(r.ZoomTokenUrl, zoomOptions.TokenUrl, "https://zoom.us/oauth/token")!,
            HostUserId: Coalesce(r.ZoomHostUserId, zoomOptions.HostUserId),
            MeetingSdkKey: Coalesce(r.ZoomMeetingSdkKey, zoomOptions.MeetingSdkKey),
            MeetingSdkSecret: ResolveStoredSecretOrFallback(r.ZoomMeetingSdkSecretEncrypted, zoomOptions.MeetingSdkSecret, "zoom.meetingSdkSecret"),
            WebhookSecretToken: ResolveStoredSecretOrFallback(r.ZoomWebhookSecretTokenEncrypted, zoomOptions.WebhookSecretToken, "zoom.webhookSecretToken"),
            WebhookRetryToleranceSeconds: Math.Clamp(r.ZoomWebhookRetryToleranceSeconds ?? (zoomOptions.WebhookRetryToleranceSeconds <= 0 ? 300 : zoomOptions.WebhookRetryToleranceSeconds), 60, 3600),
            AllowSandboxFallback: r.ZoomAllowSandboxFallback ?? zoomOptions.AllowSandboxFallback);
        ValidateEffectiveZoomSettings(zoom);

        // Stripe — Tax/Portal/Radar runtime tunables. SecretKey/WebhookSecret
        // mirror the existing BillingSettings fields so callers can opt for
        // either source; new Wave A5 consumers (renewal/dunning/tax) use
        // EffectiveSettings.Stripe directly to avoid coupling to the legacy
        // BillingSettings shape.
        var stripe = new StripeSettings(
            SecretKey: bill.StripeSecretKey,
            PublishableKey: bill.StripePublishableKey,
            WebhookSecret: bill.StripeWebhookSecret,
            TaxAutomaticEnabled: r.StripeTaxAutomaticEnabled ?? true,
            TaxRegistrations: ParseCsvList(r.StripeTaxRegistrationsCsv),
            CustomerPortalConfigurationId: NullIfEmpty(r.StripeCustomerPortalConfigurationId),
            RadarHighRiskCountryAllowReview: r.StripeRadarHighRiskCountryAllowReview ?? false,
            RadarBlockEmailDomainsCsv: NullIfEmpty(r.StripeRadarBlockEmailDomainsCsv));

        var liveClasses = ResolveLiveClassSettings(r);
        var speakingWhisper = ResolveSpeakingWhisper(r);
        var speakingLiveKit = ResolveSpeakingLiveKit(r);
        var speakingAi = ResolveSpeakingAi(r);
        var speakingStorage = ResolveSpeakingStorage(r);
        var speakingCompliance = ResolveSpeakingCompliance(r);
        var speakingFeatures = ResolveSpeakingFeatures(r);
        var checkoutCom = ResolveCheckoutCom(r, billing.CheckoutCom);
        var paymob = ResolvePaymob(r, billing.Paymob);
        var payTabs = ResolvePayTabs(r, billing.PayTabs);
        var soketi = ResolveSoketi(r, _soketi.Value);
        var dataRetention = ResolveDataRetention(r, _dataRetention.Value);
        var expertAutoAssignment = ResolveExpertAutoAssignment(r, _expertAutoAssignment.Value);
        var passwordPolicy = ResolvePasswordPolicy(r, _passwordPolicy.Value);

        return new EffectiveSettings(
            Email: email,
            Billing: bill,
            Sentry: sentry,
            Backup: backup,
            OAuth: oa,
            Push: push,
            UploadScanner: uploadScanner,
            Zoom: zoom,
            Stripe: stripe,
            LiveClasses: liveClasses,
            SpeakingWhisper: speakingWhisper,
            SpeakingLiveKit: speakingLiveKit,
            SpeakingAi: speakingAi,
            SpeakingStorage: speakingStorage,
            SpeakingCompliance: speakingCompliance,
            SpeakingFeatures: speakingFeatures,
            CheckoutCom: checkoutCom,
            Paymob: paymob,
            PayTabs: payTabs,
            Soketi: soketi,
            DataRetention: dataRetention,
            ExpertAutoAssignment: expertAutoAssignment,
            PasswordPolicy: passwordPolicy,
            UpdatedByUserId: r.UpdatedByUserId,
            UpdatedByUserName: r.UpdatedByUserName,
            UpdatedAt: r.UpdatedAt == default ? null : r.UpdatedAt);
    }

    // ── Payment gateways + Soketi (DB-over-env, null DB field → env value) ──
    private CheckoutComSettings ResolveCheckoutCom(RuntimeSettingsRow r, CheckoutComOptions env)
        => new(
            ApiBaseUrl: Coalesce(r.CheckoutComApiBaseUrl, env.ApiBaseUrl, "https://api.checkout.com")!,
            SecretKey: Unprotect(r.CheckoutComSecretKeyEncrypted) ?? NullIfEmpty(env.SecretKey),
            PublicKey: Coalesce(r.CheckoutComPublicKey, env.PublicKey),
            ProcessingChannelId: Coalesce(r.CheckoutComProcessingChannelId, env.ProcessingChannelId),
            WebhookSecret: Unprotect(r.CheckoutComWebhookSecretEncrypted) ?? NullIfEmpty(env.WebhookSecret),
            SuccessUrl: Coalesce(r.CheckoutComSuccessUrl, env.SuccessUrl),
            CancelUrl: Coalesce(r.CheckoutComCancelUrl, env.CancelUrl));

    private PaymobSettings ResolvePaymob(RuntimeSettingsRow r, PaymobOptions env)
    {
        IReadOnlyDictionary<string, int> integrationIds = env.IntegrationIds;
        if (!string.IsNullOrWhiteSpace(r.PaymobIntegrationIdsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(r.PaymobIntegrationIdsJson!);
                if (parsed is { Count: > 0 }) integrationIds = parsed;
            }
            catch (JsonException) { /* keep env defaults on malformed JSON */ }
        }
        return new PaymobSettings(
            ApiBaseUrl: Coalesce(r.PaymobApiBaseUrl, env.ApiBaseUrl, "https://accept.paymob.com")!,
            ApiKey: Unprotect(r.PaymobApiKeyEncrypted) ?? NullIfEmpty(env.ApiKey),
            MerchantId: Coalesce(r.PaymobMerchantId, env.MerchantId),
            HmacSecret: Unprotect(r.PaymobHmacSecretEncrypted) ?? NullIfEmpty(env.HmacSecret),
            IntegrationIds: integrationIds,
            IframeId: r.PaymobIframeId ?? env.IframeId,
            SuccessUrl: Coalesce(r.PaymobSuccessUrl, env.SuccessUrl),
            CancelUrl: Coalesce(r.PaymobCancelUrl, env.CancelUrl));
    }

    private PayTabsSettings ResolvePayTabs(RuntimeSettingsRow r, PayTabsOptions env)
        => new(
            ApiBaseUrl: Coalesce(r.PayTabsApiBaseUrl, env.ApiBaseUrl, "https://secure.paytabs.com")!,
            ServerKey: Unprotect(r.PayTabsServerKeyEncrypted) ?? NullIfEmpty(env.ServerKey),
            ProfileId: Coalesce(r.PayTabsProfileId, env.ProfileId),
            WebhookSecret: Unprotect(r.PayTabsWebhookSecretEncrypted) ?? NullIfEmpty(env.WebhookSecret),
            SuccessUrl: Coalesce(r.PayTabsSuccessUrl, env.SuccessUrl),
            CancelUrl: Coalesce(r.PayTabsCancelUrl, env.CancelUrl));

    private SoketiSettings ResolveSoketi(RuntimeSettingsRow r, SoketiOptions env)
        => new(
            Host: Coalesce(r.SoketiHost, env.Host, "localhost")!,
            Port: r.SoketiPort ?? (env.Port <= 0 ? 6001 : env.Port),
            AppId: Coalesce(r.SoketiAppId, env.AppId, "oet-app")!,
            AppKey: Coalesce(r.SoketiAppKey, env.AppKey, "oet-key")!,
            AppSecret: Unprotect(r.SoketiAppSecretEncrypted) ?? NullIfEmpty(env.AppSecret),
            UseTls: r.SoketiUseTls ?? env.UseTls,
            Enabled: r.SoketiEnabled ?? env.Enabled);

    // ── Data retention / Expert auto-assign / Password policy ──────
    // (DB-over-env: null DB field → env/appsettings default)
    private static DataRetentionSettings ResolveDataRetention(RuntimeSettingsRow r, DataRetentionOptions env)
        => new(
            AnalyticsEvents: DaysOrDefault(r.DataRetentionAnalyticsEventsDays, env.AnalyticsEvents),
            AuditEvents: DaysOrDefault(r.DataRetentionAuditEventsDays, env.AuditEvents),
            PaymentWebhookEvents: DaysOrDefault(r.DataRetentionPaymentWebhookEventsDays, env.PaymentWebhookEvents),
            PaymentWebhookPiiNullOutAge: DaysOrDefault(r.DataRetentionPaymentWebhookPiiNullOutAgeDays, env.PaymentWebhookPiiNullOutAge),
            NotificationDeliveryAttempts: DaysOrDefault(r.DataRetentionNotificationDeliveryAttemptsDays, env.NotificationDeliveryAttempts),
            SweepInterval: HoursOrDefault(r.DataRetentionSweepIntervalHours, env.SweepInterval),
            BatchSize: r.DataRetentionBatchSize is > 0 ? r.DataRetentionBatchSize.Value : (env.BatchSize <= 0 ? 5000 : env.BatchSize));

    private static ExpertAutoAssignmentSettings ResolveExpertAutoAssignment(RuntimeSettingsRow r, ExpertAutoAssignmentOptions env)
        => new(
            Enabled: r.ExpertAutoAssignmentEnabled ?? env.Enabled,
            PollingIntervalSeconds: PositiveOrDefault(r.ExpertAutoAssignmentPollingIntervalSeconds, env.PollingIntervalSeconds, 30),
            SlaEscalationIntervalSeconds: PositiveOrDefault(r.ExpertAutoAssignmentSlaEscalationIntervalSeconds, env.SlaEscalationIntervalSeconds, 60),
            SlaHoursStandard: PositiveOrDefault(r.ExpertAutoAssignmentSlaHoursStandard, env.SlaHoursStandard, 48),
            SlaHoursExpress: PositiveOrDefault(r.ExpertAutoAssignmentSlaHoursExpress, env.SlaHoursExpress, 12),
            MaxActiveAssignmentsPerExpert: PositiveOrDefault(r.ExpertAutoAssignmentMaxActiveAssignmentsPerExpert, env.MaxActiveAssignmentsPerExpert, 8),
            LookbackHoursForLoad: PositiveOrDefault(r.ExpertAutoAssignmentLookbackHoursForLoad, env.LookbackHoursForLoad, 24),
            BatchSize: PositiveOrDefault(r.ExpertAutoAssignmentBatchSize, env.BatchSize, 50));

    private PasswordPolicySettings ResolvePasswordPolicy(RuntimeSettingsRow r, PasswordPolicyOptions env)
    {
        var baseUrl = Coalesce(r.PasswordPolicyBreachApiBaseUrl, env.BreachApiBaseUrl, "https://api.pwnedpasswords.com/")!;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        var timeoutSeconds = r.PasswordPolicyBreachApiTimeoutSeconds is > 0
            ? r.PasswordPolicyBreachApiTimeoutSeconds.Value
            : (env.BreachApiTimeout > TimeSpan.Zero ? (int)Math.Ceiling(env.BreachApiTimeout.TotalSeconds) : 3);
        return new PasswordPolicySettings(
            MinimumLength: r.PasswordPolicyMinimumLength is > 0 ? r.PasswordPolicyMinimumLength.Value : (env.MinimumLength > 0 ? env.MinimumLength : 10),
            RequireMixedCase: r.PasswordPolicyRequireMixedCase ?? env.RequireMixedCase,
            RequireDigit: r.PasswordPolicyRequireDigit ?? env.RequireDigit,
            RequireSymbol: r.PasswordPolicyRequireSymbol ?? env.RequireSymbol,
            BreachCheckEnabled: r.PasswordPolicyBreachCheckEnabled ?? env.BreachCheckEnabled,
            BreachApiBaseUrl: baseUrl,
            BreachApiTimeout: TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 60)));
    }

    private static TimeSpan DaysOrDefault(int? days, TimeSpan fallback)
        => days.HasValue ? TimeSpan.FromDays(Math.Max(0, days.Value)) : fallback;

    private static TimeSpan HoursOrDefault(int? hours, TimeSpan fallback)
        => hours.HasValue ? TimeSpan.FromHours(Math.Max(0, hours.Value)) : fallback;

    private static int PositiveOrDefault(int? dbValue, int envValue, int hardDefault)
        => dbValue is > 0 ? dbValue.Value : (envValue > 0 ? envValue : hardDefault);

    private static LiveClassSettings ResolveLiveClassSettings(RuntimeSettingsRow r)
        => new(AiRecordingProcessingEnabled: r.LiveClassesAiRecordingProcessingEnabled ?? false);

    private SpeakingWhisperSettings ResolveSpeakingWhisper(RuntimeSettingsRow r)
    {
        var dbKey = Unprotect(r.SpeakingWhisperApiKeyEncrypted);
        var fallbackKey = NullIfEmpty(_config["Speaking:Whisper:ApiKey"]);
        var key = !string.IsNullOrWhiteSpace(dbKey) ? dbKey : fallbackKey;
        var baseUrl = Coalesce(r.SpeakingWhisperBaseUrl, _config["Speaking:Whisper:BaseUrl"], "https://api.openai.com/v1") ?? "https://api.openai.com/v1";
        var model = Coalesce(r.SpeakingWhisperModel, _config["Speaking:Whisper:Model"], "whisper-1") ?? "whisper-1";
        return new SpeakingWhisperSettings(
            ApiKey: key,
            BaseUrl: baseUrl,
            Model: model,
            IsConfigured: !string.IsNullOrWhiteSpace(key));
    }

    private SpeakingLiveKitSettings ResolveSpeakingLiveKit(RuntimeSettingsRow r)
    {
        var provider = Coalesce(r.SpeakingLiveKitProvider, _config["LiveKit:Provider"], "disabled") ?? "disabled";
        var apiKey = Unprotect(r.SpeakingLiveKitApiKeyEncrypted) ?? NullIfEmpty(_config["LiveKit:ApiKey"]);
        var apiSecret = Unprotect(r.SpeakingLiveKitApiSecretEncrypted) ?? NullIfEmpty(_config["LiveKit:ApiSecret"]);
        var wssUrl = Coalesce(r.SpeakingLiveKitWssUrl, _config["LiveKit:WssUrl"]);
        var webhookSecret = Unprotect(r.SpeakingLiveKitWebhookSigningSecretEncrypted) ?? NullIfEmpty(_config["LiveKit:WebhookSigningSecret"]);
        var egressBucket = Coalesce(r.SpeakingLiveKitEgressBucket, _config["LiveKit:EgressBucket"]);
        var maxDuration = r.SpeakingLiveKitDefaultMaxDurationSeconds ?? ParseInt(_config["LiveKit:DefaultMaxDurationSeconds"]) ?? 1800;
        var egressEnabled = r.SpeakingLiveKitEgressEnabled ?? ParseBool(_config["LiveKit:EgressEnabled"]) ?? false;
        var isEnabled = !string.Equals(provider, "disabled", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(apiKey);
        return new SpeakingLiveKitSettings(
            Provider: provider,
            ApiKey: apiKey,
            ApiSecret: apiSecret,
            WssUrl: wssUrl,
            WebhookSigningSecret: webhookSecret,
            EgressBucket: egressBucket,
            DefaultMaxDurationSeconds: Math.Clamp(maxDuration, 60, 7200),
            EgressEnabled: egressEnabled,
            IsEnabled: isEnabled);
    }

    private SpeakingAiSettings ResolveSpeakingAi(RuntimeSettingsRow r)
    {
        var anthropicKey = Unprotect(r.SpeakingAnthropicApiKeyEncrypted) ?? NullIfEmpty(_config["Anthropic:ApiKey"]);
        var elevenLabsKey = Unprotect(r.SpeakingElevenLabsApiKeyEncrypted) ?? NullIfEmpty(_config["ElevenLabs:ApiKey"]);
        return new SpeakingAiSettings(
            AnthropicApiKey: anthropicKey,
            ElevenLabsApiKey: elevenLabsKey,
            IsAnthropicConfigured: !string.IsNullOrWhiteSpace(anthropicKey),
            IsElevenLabsConfigured: !string.IsNullOrWhiteSpace(elevenLabsKey));
    }

    private SpeakingStorageSettings ResolveSpeakingStorage(RuntimeSettingsRow r)
    {
        var accessKeyId = Coalesce(r.SpeakingAwsAccessKeyId, _config["Aws:AccessKeyId"]);
        var secretKey = Unprotect(r.SpeakingAwsSecretAccessKeyEncrypted) ?? NullIfEmpty(_config["Aws:SecretAccessKey"]);
        var region = Coalesce(r.SpeakingAwsRegion, _config["Aws:Region"], "eu-west-2") ?? "eu-west-2";
        var bucket = Coalesce(r.SpeakingAwsBucket, _config["Aws:Bucket"]);
        return new SpeakingStorageSettings(
            AwsAccessKeyId: accessKeyId,
            AwsSecretAccessKey: secretKey,
            Region: region,
            Bucket: bucket,
            IsConfigured: !string.IsNullOrWhiteSpace(accessKeyId) && !string.IsNullOrWhiteSpace(secretKey) && !string.IsNullOrWhiteSpace(bucket));
    }

    private SpeakingComplianceSettings ResolveSpeakingCompliance(RuntimeSettingsRow r)
    {
        var consentVersion = Coalesce(r.SpeakingComplianceCurrentConsentVersion, _config["SpeakingCompliance:CurrentConsentVersion"], "recording.v1") ?? "recording.v1";
        var liveVideoVersion = Coalesce(r.SpeakingComplianceCurrentLiveVideoConsentVersion, _config["SpeakingCompliance:CurrentLiveVideoConsentVersion"], "live_video_with_tutor.v1") ?? "live_video_with_tutor.v1";
        var retentionDefault = r.SpeakingComplianceRetentionDaysDefault ?? ParseInt(_config["SpeakingCompliance:RetentionDaysDefault"]) ?? 90;
        var retentionTutor = r.SpeakingComplianceRetentionDaysWhenTutorReviewed ?? ParseInt(_config["SpeakingCompliance:RetentionDaysWhenTutorReviewed"]) ?? 365;
        var auditRetention = r.SpeakingComplianceAuditLogRetentionDays ?? ParseInt(_config["SpeakingCompliance:AuditLogRetentionDays"]) ?? 2555;
        return new SpeakingComplianceSettings(
            CurrentConsentVersion: consentVersion,
            CurrentLiveVideoConsentVersion: liveVideoVersion,
            RetentionDaysDefault: Math.Max(1, retentionDefault),
            RetentionDaysWhenTutorReviewed: Math.Max(1, retentionTutor),
            AuditLogRetentionDays: Math.Max(1, auditRetention));
    }

    private SpeakingFeatureSettings ResolveSpeakingFeatures(RuntimeSettingsRow r)
    {
        var v2 = r.SpeakingV2Enabled ?? ParseBool(_config["Features:SpeakingV2"]) ?? false;
        return new SpeakingFeatureSettings(SpeakingV2Enabled: v2);
    }

    private static int? ParseInt(string? s)
        => int.TryParse(s, out var v) ? v : null;

    private static bool? ParseBool(string? s)
        => bool.TryParse(s, out var v) ? v : null;

    private static IReadOnlyList<string> ParseCsvList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<string>();
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => value.Length > 0)
            .ToArray();
    }

    private static string? Coalesce(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private string? ResolveStoredSecretOrFallback(string? cipher, string? fallback, string key)
    {
        if (string.IsNullOrEmpty(cipher))
        {
            return NullIfEmpty(fallback);
        }

        try
        {
            return _protector.Unprotect(cipher);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Runtime setting {key} could not be decrypted.", ex);
        }
    }

    private void ValidateEffectiveZoomSettings(ZoomSettings zoom)
    {
        if (_environment.IsProduction() && zoom.AllowSandboxFallback)
        {
            throw new InvalidOperationException("Zoom sandbox fallback cannot be enabled in production.");
        }

        ValidateZoomEndpoint(zoom.ApiBaseUrl, "Zoom ApiBaseUrl", ["api.zoom.us", "api.zoom.com"]);
        ValidateZoomEndpoint(zoom.TokenUrl, "Zoom TokenUrl", ["zoom.us", "zoom.com"]);
    }

    private static void ValidateZoomEndpoint(string value, string name, IReadOnlyCollection<string> allowedHosts)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{name} must be an https:// URL.");
        }

        if (!allowedHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{name} must use an official Zoom host.");
        }
    }

    private static double? ParseDouble(string? s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
}
