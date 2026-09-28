using System.Security.Claims;
using System.Text.Json;
using System.Net.Sockets;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Pronunciation;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Endpoints;

public static partial class AdminRuntimeSettingsEndpoints
{
    private static string? NormalizeSectionId(string? sectionId)
    {
        var normalized = sectionId?.Trim().ToLowerInvariant();
        return normalized is "email" or "billing" or "paypal" or "sentry" or "backup" or "oauth" or "push" or "uploadscanner" or "zoom" or "stripe" or "speakinglivekit" or "speakingai" or "speakingstorage" or "speakingcompliance" or "speakingfeatures" or "speakingwhisper" or "checkoutcom" or "bunnystream" or "paymob" or "paytabs" or "easykash" or "soketi" or "dataretention" or "expertautoassignment" or "passwordpolicy" or "aiassistant" or "aigateway" or "writing" or "platform" or "messaging" or "fx" or "billingcore" or "storage" or "pdfextraction" or "pronunciation" or "authtokens" or "webpush" or "security" or "videoprotection" or "support" or "firebaseotp"
            ? normalized
            : normalized == "upload-scanner" ? "uploadscanner"
            : normalized == "bunny-stream" ? "bunnystream" : null;
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestSectionAsync(
        string sectionId,
        IRuntimeSettingsProvider provider,
        IWebHostEnvironment env,
        UploadScannerOptions scannerOptions,
        IPronunciationCredentialResolver whisperRegistry,
        IHttpClientFactory httpClientFactory,
        DateTimeOffset testedAt,
        CancellationToken ct)
    {
        var settings = await provider.GetAsync(ct);
        return sectionId switch
        {
            "email" => HasAny(settings.Email.BrevoApiKey, settings.Email.SmtpHost)
                ? Ok(sectionId, "Email configuration is present. No live email was sent.", testedAt)
                : Failed(sectionId, "Configure Brevo or SMTP before enabling email delivery.", testedAt),
            "billing" => HasAll(settings.Billing.StripeSecretKey, settings.Billing.StripePublishableKey, settings.Billing.StripeWebhookSecret)
                ? Ok(sectionId, "Stripe keys and webhook secret are configured. No live charge was created.", testedAt)
                : Failed(sectionId, "Configure Stripe secret, publishable key, and webhook secret.", testedAt),
            "paypal" => await TestPayPalAsync(settings.Billing, httpClientFactory, sectionId, testedAt, ct),
            "sentry" => Uri.TryCreate(settings.Sentry.Dsn, UriKind.Absolute, out var sentryUri)
                        && sentryUri.Scheme == Uri.UriSchemeHttps
                ? Ok(sectionId, "Sentry DSN format is valid. No event was sent.", testedAt)
                : Failed(sectionId, "Configure a valid https:// Sentry DSN.", testedAt),
            "backup" => HasAll(settings.Backup.S3Url, settings.Backup.AwsAccessKeyId, settings.Backup.AwsSecretAccessKey, settings.Backup.GpgPassphrase)
                ? Ok(sectionId, "Backup destination and encryption settings are present. No backup was run.", testedAt)
                : Failed(sectionId, "Configure S3/R2 destination, credentials, and GPG passphrase.", testedAt),
            "oauth" => HasAll(settings.OAuth.GoogleClientId, settings.OAuth.GoogleClientSecret)
                       || HasAll(settings.OAuth.FacebookAppId, settings.OAuth.FacebookAppSecret)
                ? Ok(sectionId, "At least one OAuth provider is configured. No sign-in was attempted.", testedAt)
                : Failed(sectionId, "Configure Google or Facebook OAuth credentials. Apple fields are stored for future use but are not an active sign-in provider yet.", testedAt),
            "push" => await TestPushAsync(settings.Push, sectionId, testedAt, ct),
            "uploadscanner" => await TestUploadScannerAsync(settings.UploadScanner, env, scannerOptions, sectionId, testedAt, ct),
            "zoom" => settings.Zoom.Enabled
                      && HasAll(settings.Zoom.AccountId, settings.Zoom.ClientId, settings.Zoom.ClientSecret, settings.Zoom.HostUserId)
                ? Ok(sectionId, "Zoom server-to-server OAuth settings are configured. No meeting was created.", testedAt)
                : Failed(sectionId, "Configure Zoom account, client, client secret, and host user before enabling live classes.", testedAt),
            "stripe" => HasAll(settings.Stripe.SecretKey, settings.Stripe.PublishableKey, settings.Stripe.WebhookSecret)
                ? Ok(sectionId, "Stripe Tax/Portal/Radar runtime settings appear configured. No live API calls were made.", testedAt)
                : Failed(sectionId, "Configure Stripe secret key, publishable key, and webhook secret before enabling Tax/Radar.", testedAt),
            "speakingwhisper" => whisperRegistry.IsRegistryConfigured("whisper-asr")
                ? Ok(sectionId, "Whisper is active via the whisper-asr row in Admin → AI Providers (covers Speaking, Pronunciation, and Conversation). This legacy field is unused while that row has a key.", testedAt)
                : settings.SpeakingWhisper.IsConfigured
                    ? Ok(sectionId, "Whisper is active via this legacy field. No transcription was performed. Tip: move the key to the whisper-asr row in AI Providers to cover all speech-to-text with one key.", testedAt)
                    : Failed(sectionId, "Configure Whisper in the whisper-asr row (Admin → AI Providers) to cover all speech-to-text, or set a key here.", testedAt),
            "speakinglivekit" => settings.SpeakingLiveKit.IsEnabled
                ? Ok(sectionId, "LiveKit is configured and enabled. No room was created.", testedAt)
                : Failed(sectionId, "Configure LiveKit provider, API key, and API secret to enable live tutor rooms.", testedAt),
            "speakingai" => settings.SpeakingAi.IsAnthropicConfigured
                ? Ok(sectionId, "Anthropic API key is configured for Speaking AI scoring. No AI call was made.", testedAt)
                : Failed(sectionId, "Configure the Anthropic API key for Speaking AI scoring and patient turns.", testedAt),
            "speakingstorage" => settings.SpeakingStorage.IsConfigured
                ? Ok(sectionId, "AWS S3 storage is configured for speaking recordings. No upload was performed.", testedAt)
                : Failed(sectionId, "Configure AWS access key, secret, and bucket for speaking recording storage.", testedAt),
            "speakingcompliance" => Ok(sectionId, "Speaking compliance settings are configured via defaults or admin overrides.", testedAt),
            "speakingfeatures" => Ok(sectionId, $"Speaking V2 feature flag is {(settings.SpeakingFeatures.SpeakingV2Enabled ? "enabled" : "disabled")}.", testedAt),
            "placement" => Ok(sectionId, settings.Placement.PlacementEnabled
                ? $"Placement test is enabled{(settings.Placement.BetaOnly ? $" (beta allowlist, {settings.Placement.BetaEmails?.Split(',', ';').Count(e => !string.IsNullOrWhiteSpace(e))} account(s))" : " for everyone")}."
                : "Placement test is disabled.",
                testedAt),
            "checkoutcom" => await TestCheckoutComAsync(settings.CheckoutCom, httpClientFactory, sectionId, testedAt, ct),
            "bunnystream" => await TestBunnyStreamAsync(settings.BunnyStream, settings.VideoAttestation, httpClientFactory, sectionId, testedAt, ct),
            "paymob" => await TestPaymobAsync(settings.Paymob, httpClientFactory, sectionId, testedAt, ct),
            "paytabs" => await TestPayTabsAsync(settings.PayTabs, httpClientFactory, sectionId, testedAt, ct),
            "easykash" => TestEasyKash(settings.EasyKash, sectionId, testedAt),
            "soketi" => await TestSoketiAsync(settings.Soketi, httpClientFactory, sectionId, testedAt, ct),
            "dataretention" => Ok(sectionId, $"Retention windows resolved (audit {settings.DataRetention.AuditEvents.TotalDays:0}d, webhooks {settings.DataRetention.PaymentWebhookEvents.TotalDays:0}d). Sweep every {settings.DataRetention.SweepInterval.TotalHours:0}h, batch {settings.DataRetention.BatchSize}.", testedAt),
            "expertautoassignment" => settings.ExpertAutoAssignment.Enabled
                ? Ok(sectionId, $"Auto-assignment enabled. SLA {settings.ExpertAutoAssignment.SlaHoursStandard}h standard / {settings.ExpertAutoAssignment.SlaHoursExpress}h express; max {settings.ExpertAutoAssignment.MaxActiveAssignmentsPerExpert} per expert.", testedAt)
                : Ok(sectionId, "Auto-assignment is disabled. Writing reviews stay in the manual queue.", testedAt),
            "passwordpolicy" => Uri.TryCreate(settings.PasswordPolicy.BreachApiBaseUrl, UriKind.Absolute, out var hibpUri) && hibpUri.Scheme == Uri.UriSchemeHttps
                ? Ok(sectionId, $"Policy: min {settings.PasswordPolicy.MinimumLength} chars; breach check {(settings.PasswordPolicy.BreachCheckEnabled ? "on" : "off")}. Breach API URL is valid https.", testedAt)
                : Failed(sectionId, "Breach API base URL must be a valid https:// URL.", testedAt),
            "aiassistant" => settings.AiAssistant.GlobalEnabled
                ? Ok(sectionId, $"AI Assistant enabled. Max {settings.AiAssistant.MaxIterations} iterations, {settings.AiAssistant.MaxContextMessages} context messages; approval-always {(settings.AiAssistant.RequireApprovalAlways ? "on" : "off")}.", testedAt)
                : Ok(sectionId, "AI Assistant is disabled (master kill switch off).", testedAt),
            "aigateway" => Uri.TryCreate(settings.AiGateway.BaseUrl, UriKind.Absolute, out var aiUri) && (aiUri.Scheme == Uri.UriSchemeHttps || aiUri.Scheme == Uri.UriSchemeHttp)
                ? Ok(sectionId, $"Gateway base URL is valid. Provider {settings.AiGateway.ProviderId}, model {settings.AiGateway.DefaultModel}, max {settings.AiGateway.MaxToolCallsPerCompletion} tool calls/completion. Provider API key is managed in Admin → AI Providers.", testedAt)
                : Failed(sectionId, "AI gateway base URL must be a valid http(s):// URL.", testedAt),
            "writing" => Ok(sectionId, $"Writing: crons {(settings.Writing.CronsEnabled ? "on" : "off")}, coach {(settings.Writing.CoachEnabled ? "on" : "off")}, OCR {(settings.Writing.OcrEnabled ? "on" : "off")}, appeals {(settings.Writing.AppealsEnabled ? "on" : "off")}. GCV OCR fallback {(string.IsNullOrWhiteSpace(settings.Writing.GcvApiKey) ? "not configured (jobs mark manual_required)" : "configured")}.", testedAt),
            "platform" => string.IsNullOrWhiteSpace(settings.Platform.PublicWebBaseUrl) || string.IsNullOrWhiteSpace(settings.Platform.PublicApiBaseUrl)
                ? Failed(sectionId, "Configure both Public API Base URL and Public Web Base URL for external auth callbacks.", testedAt)
                : Uri.TryCreate(settings.Platform.PublicApiBaseUrl, UriKind.Absolute, out _) && Uri.TryCreate(settings.Platform.PublicWebBaseUrl, UriKind.Absolute, out _)
                    ? Ok(sectionId, $"Public host URLs are valid. Fallback email domain: {settings.Platform.FallbackEmailDomain}.", testedAt)
                    : Failed(sectionId, "Public API/Web base URLs must be valid absolute URLs.", testedAt),
            // Non-destructive config-presence probe — no live SMS/WhatsApp is sent.
            "messaging" => (!settings.Messaging.TwilioEnabled && !settings.Messaging.WhatsAppEnabled)
                ? Ok(sectionId, "Messaging channels are disabled. No SMS/WhatsApp notifications will be sent.", testedAt)
                : (settings.Messaging.TwilioEnabled && !settings.Messaging.IsTwilioConfigured)
                    ? Failed(sectionId, "Twilio is enabled but not fully configured. Set Account SID and Auth Token.", testedAt)
                    : (settings.Messaging.WhatsAppEnabled && !settings.Messaging.IsWhatsAppConfigured)
                        ? Failed(sectionId, "WhatsApp is enabled but not fully configured. Set Access Token and Phone Number ID.", testedAt)
                        : Ok(sectionId, $"Messaging configured: Twilio SMS {(settings.Messaging.IsTwilioConfigured ? "ready" : "off")}, WhatsApp {(settings.Messaging.IsWhatsAppConfigured ? "ready" : "off")}. No message was sent.", testedAt),
            "support" => TestSupport(settings.Support, sectionId, testedAt),
            "firebaseotp" => TestFirebaseOtp(settings.FirebaseOtp, sectionId, testedAt),
            "fx" => string.IsNullOrWhiteSpace(settings.Fx.ApiKey) || string.IsNullOrWhiteSpace(settings.Fx.ApiBaseUrl)
                ? Ok(sectionId, $"No FX provider configured — offline seed rates are used (base {settings.Fx.BaseCurrency}). Dynamic pricing {(settings.Fx.DynamicPricingEnabled ? "on" : "off")}.", testedAt)
                : Uri.TryCreate(settings.Fx.ApiBaseUrl, UriKind.Absolute, out _)
                    ? Ok(sectionId, $"FX provider key + base URL configured (base {settings.Fx.BaseCurrency}). No live rate fetch was performed.", testedAt)
                    : Failed(sectionId, "FX provider base URL must be a valid absolute URL.", testedAt),
            "billingcore" => Ok(sectionId, $"Billing core: default {settings.Billing.DefaultCurrency}/{settings.Billing.DefaultRegion}, wallet {settings.Billing.WalletCurrency} ({settings.Billing.WalletTopUpTiers.Count} tiers); webhook max-age {settings.Billing.WebhookMaxAgeSeconds}s, max {settings.Billing.WebhookMaxAttempts} attempts; PayPal {(settings.Billing.PayPalUseSandbox ? "sandbox" : "production")}.", testedAt),
            "storage" => settings.Storage.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase)
                ? (settings.Storage.IsConfigured
                    ? Ok(sectionId, $"S3 storage configured (bucket {settings.Storage.BucketName}, region {settings.Storage.AwsRegion}). No object was read or written.", testedAt)
                    : Failed(sectionId, "Storage provider is 's3' but bucket, access key, or secret key is missing.", testedAt))
                : Ok(sectionId, "Storage provider is 'local'. S3 credentials are not required.", testedAt),
            "pdfextraction" => settings.PdfExtraction.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase)
                    || settings.PdfExtraction.Provider.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(settings.PdfExtraction.AzureEndpoint) || string.IsNullOrWhiteSpace(settings.PdfExtraction.AzureApiKey)
                    ? Ok(sectionId, $"PDF extraction provider '{settings.PdfExtraction.Provider}' — Azure OCR not configured; PdfPig-only extraction will be used.", testedAt)
                    : Ok(sectionId, $"PDF extraction provider '{settings.PdfExtraction.Provider}' with Azure OCR configured. No document was processed.", testedAt))
                : Ok(sectionId, $"PDF extraction provider is '{settings.PdfExtraction.Provider}'.", testedAt),
            "pronunciation" => Ok(sectionId, $"Pronunciation provider '{settings.Pronunciation.Provider}' (locale {settings.Pronunciation.AzureLocale}). API keys are managed in Admin → AI Providers. Free tier: {settings.Pronunciation.FreeTierWeeklyAttemptLimit} attempts / {settings.Pronunciation.FreeTierWindowDays}d.", testedAt),
            "authtokens" => Ok(sectionId, $"Access token {settings.AuthTokens.AccessTokenLifetime.TotalMinutes:0}m, refresh {settings.AuthTokens.RefreshTokenLifetime.TotalDays:0.##}d, OTP {settings.AuthTokens.OtpLifetime.TotalMinutes:0}m. Issuer: {settings.AuthTokens.AuthenticatorIssuer ?? "(env default)"}.", testedAt),
            "webpush" => settings.Push.WebPushEnabled
                ? (HasAll(settings.Push.VapidSubject, settings.Push.VapidPublicKey, settings.Push.VapidPrivateKey)
                    ? Ok(sectionId, "Web push is enabled and VAPID keys are configured (Push section).", testedAt)
                    : Failed(sectionId, "Web push is enabled but VAPID keys are missing. Configure them in the Push section.", testedAt))
                : Ok(sectionId, "Web push is disabled. Enable it (with VAPID keys) to allow browser notifications.", testedAt),
            "security" => Ok(sectionId, $"Single active session {(settings.Security.SingleActiveSessionEnabled ? "enforced" : "off")}; risk mode '{settings.Security.RiskMode}'; device verification {(settings.Security.TrustedDeviceRequired ? "required" : "off")}; inactive session timeout {settings.Security.InactiveSessionTimeoutDays}d; learner verified-email gate {(settings.Security.RequireVerifiedEmailForLearners ? "ON" : "off")}; country allow-list mode '{settings.Security.CountryAllowListMode}'. No live sign-in was attempted.", testedAt),
            "videoprotection" => Ok(sectionId, $"Revoke-on-capture {(settings.VideoProtection.RevokeOnCaptureDetected ? "on" : "off")}; block rooted devices {(settings.VideoProtection.BlockRootedDevices ? "on" : "off")}; block emulators {(settings.VideoProtection.BlockEmulators ? "on" : "off")}. No playback session was probed.", testedAt),
            _ => Failed(sectionId, "Unknown integration section.", testedAt),
        };
    }

    /// <summary>
    /// Live, non-destructive push-config probe. For FCM, actually mints an OAuth2
    /// access token from the service account JSON against Google's token endpoint —
    /// this proves the JSON is well-formed AND the service account authenticates,
    /// not just that fields are non-empty. No push notification is sent.
    /// </summary>
    private static async Task<RuntimeSettingsIntegrationTestResponse> TestPushAsync(
        PushSettings push, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        var channels = new List<string>();

        if (HasAll(push.VapidSubject, push.VapidPublicKey, push.VapidPrivateKey))
        {
            channels.Add("browser (VAPID)");
        }

        if (push.IsApnsConfigured)
        {
            channels.Add("iOS (APNs)");
        }

        if (push.IsFcmConfigured)
        {
            try
            {
                var credential = GoogleCredential.FromJson(push.FcmServiceAccountJson)
                    .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
                // GoogleCredential itself doesn't expose ITokenAccess — the underlying
                // wrapped credential (ServiceAccountCredential, for a service-account
                // JSON key) does.
                if (credential.UnderlyingCredential is not ITokenAccess tokenAccess)
                {
                    return Failed(sectionId, "FCM service account JSON did not produce a token-capable credential.", testedAt);
                }
                await tokenAccess.GetAccessTokenForRequestAsync(cancellationToken: ct);
                channels.Add("Android (FCM)");
            }
            catch (Exception ex)
            {
                // Never echo ex.Message: credential/token errors can carry service-account JSON fragments.
                return Failed(sectionId, $"FCM service account did not authenticate ({ex.GetType().Name}). Check the service account JSON and Firebase project.", testedAt);
            }
        }

        return channels.Count > 0
            ? Ok(sectionId, $"Configured push channels: {string.Join(", ", channels)}. No notification was sent.", testedAt)
            : Failed(sectionId, "Configure browser VAPID keys, an FCM service account JSON + project id, or APNs credentials before enabling push.", testedAt);
    }

    // ── Live, non-destructive payment/Soketi probes (no money moves) ───────
    /// <summary>
    /// Live PayPal connection probe: requests an OAuth token from the EFFECTIVE host
    /// (the same host selection PayPalGateway uses), so "green" means the credentials
    /// actually authenticate against the sandbox/live host they're paired with — not just
    /// "keys are present". A 401 here is the exact failure learners would hit, and the
    /// message calls out a sandbox/live mismatch (the most common cause).
    /// </summary>
    private static async Task<RuntimeSettingsIntegrationTestResponse> TestPayPalAsync(
        BillingSettings b, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(b.PayPalClientId) || string.IsNullOrWhiteSpace(b.PayPalClientSecret))
            return Failed(sectionId, "Configure PayPal Client ID and Secret (and the Webhook ID) to enable PayPal checkout.", testedAt);

        // Mirror PayPalGateway.GetPayPalApiBaseUrl: a custom (non-default) base URL wins,
        // otherwise the sandbox flag selects the host.
        var host = !string.IsNullOrWhiteSpace(b.PayPalApiBaseUrl)
                   && !string.Equals(b.PayPalApiBaseUrl, "https://api-m.paypal.com", StringComparison.OrdinalIgnoreCase)
            ? b.PayPalApiBaseUrl
            : (b.PayPalUseSandbox ? "https://api-m.sandbox.paypal.com" : "https://api-m.paypal.com");
        var isSandbox = host.Contains("sandbox", StringComparison.OrdinalIgnoreCase);
        var hostLabel = isSandbox ? "sandbox" : "LIVE";

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            var client = httpClientFactory.CreateClient("RuntimeSettingsTest");
            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureSlash(host)), "v1/oauth2/token"));
            var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{b.PayPalClientId}:{b.PayPalClientSecret}"));
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            if ((int)resp.StatusCode is >= 200 and < 300)
            {
                var cards = b.PayPalAdvancedCardsEnabled ? "embedded card fields on" : "buttons only";
                var webhookWarn = string.IsNullOrWhiteSpace(b.PayPalWebhookId)
                    ? " ⚠ No Webhook ID set — refunds/disputes won't be recorded until you add it."
                    : string.Empty;
                return Ok(sectionId, $"Verified — PayPal authenticated against the {hostLabel} host ({cards}). Learners will be offered PayPal at checkout. No order was created.{webhookWarn}", testedAt);
            }

            if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                var fix = isSandbox
                    ? "These keys were rejected by the SANDBOX host. If they are LIVE keys, turn OFF \"Use PayPal Sandbox\" (the keys and the host must match)."
                    : "These keys were rejected by the LIVE host. If they are SANDBOX keys, turn ON \"Use PayPal Sandbox\" (the keys and the host must match).";
                return Failed(sectionId, $"PayPal authentication failed (HTTP {(int)resp.StatusCode}). {fix}", testedAt);
            }

            return Failed(sectionId, $"PayPal returned HTTP {(int)resp.StatusCode} from the {hostLabel} host.", testedAt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var msg = AiProviderConnectionTester.RedactSecrets(ex.Message, b.PayPalClientSecret) ?? "PayPal endpoint unreachable.";
            return Failed(sectionId, msg.Length > 200 ? msg[..200] : msg, testedAt);
        }
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestCheckoutComAsync(
        CheckoutComSettings s, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.SecretKey))
            return Failed(sectionId, "Configure the Checkout.com secret key.", testedAt);
        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(s.ApiBaseUrl);
        if (unsafeReason is not null) return Failed(sectionId, unsafeReason, testedAt);
        return await ProbeAsync(httpClientFactory, sectionId, testedAt, s.SecretKey, ct, () =>
        {
            // Read-only list endpoint — validates the secret without moving money.
            var req = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(EnsureSlash(s.ApiBaseUrl)), "workflows"));
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", s.SecretKey);
            return req;
        });
    }

    /// <summary>Non-destructive Bunny probe: GET the video library resource with
    /// the API key. 2xx proves the key + library id pair; nothing is mutated.</summary>
    private static async Task<RuntimeSettingsIntegrationTestResponse> TestBunnyStreamAsync(
        BunnyStreamSettings s, VideoAttestationSettings attestation, IHttpClientFactory httpClientFactory,
        string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (!s.Enabled)
            return Ok(sectionId, "Bunny Stream is disabled. The Video Library stays dormant (uploads/playback return 503 bunny_not_configured).", testedAt);
        if (string.IsNullOrWhiteSpace(s.LibraryId) || string.IsNullOrWhiteSpace(s.ApiKey))
            return Failed(sectionId, "Configure the Bunny Stream library id and API key.", testedAt);
        if (string.IsNullOrWhiteSpace(s.CdnHostname) || string.IsNullOrWhiteSpace(s.TokenAuthKey))
            return Failed(sectionId, "Configure the CDN hostname and token authentication key for signed playback URLs.", testedAt);

        var attestationNote = attestation.IsConfigured
            ? $" Attestation keys configured: {attestation.Keys.Count}."
            : " ⚠ No video attestation keys set — native playback sessions will return 403 attestation_unavailable.";
        var result = await ProbeAsync(httpClientFactory, sectionId, testedAt, s.ApiKey!, ct, () =>
        {
            var req = new HttpRequestMessage(
                HttpMethod.Get, $"https://video.bunnycdn.com/library/{Uri.EscapeDataString(s.LibraryId!)}");
            req.Headers.TryAddWithoutValidation("AccessKey", s.ApiKey);
            return req;
        });
        return result.Status == "ok"
            ? Ok(sectionId, $"Bunny Stream credentials accepted for library {s.LibraryId}. No video was created.{attestationNote}", testedAt)
            : result;
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestPaymobAsync(
        PaymobSettings s, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            return Failed(sectionId, "Configure the Paymob API key.", testedAt);
        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(s.ApiBaseUrl);
        if (unsafeReason is not null) return Failed(sectionId, unsafeReason, testedAt);
        return await ProbeAsync(httpClientFactory, sectionId, testedAt, s.ApiKey, ct, () =>
        {
            // Token issuance only — no order/payment is created.
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureSlash(s.ApiBaseUrl)), "api/auth/tokens"));
            req.Content = System.Net.Http.Json.JsonContent.Create(new { api_key = s.ApiKey });
            return req;
        });
    }

    // EasyKash's only API creates a real hosted payment, so "test" validates
    // configuration completeness rather than issuing a live request.
    private static RuntimeSettingsIntegrationTestResponse TestEasyKash(
        EasyKashSettings s, string sectionId, DateTimeOffset testedAt)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            return Failed(sectionId, "Configure the EasyKash API key.", testedAt);
        if (string.IsNullOrWhiteSpace(s.HmacSecret))
            return Failed(sectionId, "Add the EasyKash HMAC secret. EasyKash generates it after you save the Callback URL + payment methods in their dashboard.", testedAt);
        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(s.ApiBaseUrl);
        if (unsafeReason is not null) return Failed(sectionId, unsafeReason, testedAt);
        var mode = s.ConvertToEgp ? "convert to EGP" : "charge quote currency";
        return Ok(sectionId, $"EasyKash is configured ({mode}). No live payment was created — run a real checkout to fully verify.", testedAt);
    }

    // The support number is only ever rendered into a wa.me/<number> deep link the
    // learner taps — there is no outbound connection to probe, so this validates the
    // effective number is present and wa.me-dialable (digits only) rather than faking
    // a connection test. An unconfigured or malformed number means a dead proof button
    // on every package, which is precisely what this catches.
    private static RuntimeSettingsIntegrationTestResponse TestSupport(
        SupportSettings s, string sectionId, DateTimeOffset testedAt)
    {
        if (!s.IsWhatsAppConfigured)
            return Failed(sectionId, "Configure the support WhatsApp number. Without it the \"Send proof on WhatsApp\" button is hidden on every package and checkout surface.", testedAt);
        var number = NormalizeWhatsAppNumber(s.WhatsAppNumber)!;
        if (number.Length is < 6 or > 20 || !number.All(char.IsAsciiDigit))
            return Failed(sectionId, $"\"{s.WhatsAppNumber}\" is not wa.me-dialable. Use 6-20 digits in international format without '+' (e.g. 447961725989).", testedAt);
        var template = string.IsNullOrWhiteSpace(s.WhatsAppProofTemplate) ? "built-in wording" : "custom wording";
        return Ok(sectionId, $"Support WhatsApp number is configured and wa.me-dialable (https://wa.me/{number}), proof message uses {template}. This is a deep link the learner taps — no message is sent from the server, so nothing was contacted.", testedAt);
    }

    private static RuntimeSettingsIntegrationTestResponse TestFirebaseOtp(
        FirebaseOtpSettings s, string sectionId, DateTimeOffset testedAt)
    {
        if (!s.Enabled)
            return Ok(sectionId, "Firebase SMS OTP is disabled. Password reset and device-trust codes stay on Brevo/SMTP email.", testedAt);
        if (!s.SmsEnabled)
            return Ok(sectionId, "Firebase OTP is enabled but SMS is off. Email remains the only OTP channel.", testedAt);
        if (string.IsNullOrWhiteSpace(s.WebApiKey) || string.IsNullOrWhiteSpace(s.ProjectId))
            return Failed(sectionId, "Enable Firebase SMS OTP only after the web key and project id are set. No live SMS was sent.", testedAt);
        var fallback = s.FallbackToBrevo ? "Brevo email fallback ON" : "Brevo email fallback OFF";
        return Ok(sectionId, $"Firebase SMS OTP is configured ({fallback}). No live SMS was sent.", testedAt);
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestPayTabsAsync(
        PayTabsSettings s, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.ServerKey) || string.IsNullOrWhiteSpace(s.ProfileId))
            return Failed(sectionId, "Configure the PayTabs server key and profile id.", testedAt);
        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(s.ApiBaseUrl);
        if (unsafeReason is not null) return Failed(sectionId, unsafeReason, testedAt);
        // Querying a nonexistent transaction is read-only: a valid key yields a
        // "not found"-style 2xx/4xx with a body, a bad key yields 401/403.
        return await ProbeAsync(httpClientFactory, sectionId, testedAt, s.ServerKey, ct, () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureSlash(s.ApiBaseUrl)), "payment/query"));
            req.Headers.TryAddWithoutValidation("Authorization", s.ServerKey);
            req.Content = System.Net.Http.Json.JsonContent.Create(new { profile_id = s.ProfileId, tran_ref = "TEST-0" });
            return req;
        });
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestSoketiAsync(
        SoketiSettings s, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (!s.Enabled) return Ok(sectionId, "Soketi is disabled. Enable it to dispatch realtime push.", testedAt);
        if (string.IsNullOrWhiteSpace(s.AppSecret))
            return Failed(sectionId, "Configure the Soketi app secret.", testedAt);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            var path = $"/apps/{s.AppId}/channels";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var query = SoketiPusherSigner.BuildSignedQueryString("GET", path, timestamp, s.AppKey, s.AppSecret!, string.Empty);
            var scheme = s.UseTls ? "https" : "http";
            var url = $"{scheme}://{s.Host}:{s.Port}{path}?{query}";
            var client = httpClientFactory.CreateClient("Soketi");
            using var resp = await client.GetAsync(url, timeoutCts.Token);
            return (int)resp.StatusCode is >= 200 and < 300
                ? Ok(sectionId, "Soketi accepted the signed request.", testedAt)
                : Failed(sectionId, $"Soketi returned HTTP {(int)resp.StatusCode}.", testedAt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return Failed(sectionId, "Soketi endpoint is unreachable.", testedAt);
        }
    }

    /// <summary>Send a probe request with a 10s timeout and classify the result
    /// (2xx = ok; 401/403 = auth; else failed). Error text is secret-redacted.</summary>
    private static async Task<RuntimeSettingsIntegrationTestResponse> ProbeAsync(
        IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt,
        string secretToRedact, CancellationToken ct, Func<HttpRequestMessage> buildRequest)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            var client = httpClientFactory.CreateClient("RuntimeSettingsTest");
            using var req = buildRequest();
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            if ((int)resp.StatusCode is >= 200 and < 300)
                return Ok(sectionId, "Credentials accepted. No money was moved.", testedAt);
            if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return Failed(sectionId, $"Authentication rejected (HTTP {(int)resp.StatusCode}). Check the key.", testedAt);
            // A 4xx that is not auth (e.g. "transaction not found") still proves
            // the credential was accepted by the gateway.
            if ((int)resp.StatusCode is >= 400 and < 500)
                return Ok(sectionId, $"Gateway reachable; credential accepted (HTTP {(int)resp.StatusCode}).", testedAt);
            return Failed(sectionId, $"Gateway returned HTTP {(int)resp.StatusCode}.", testedAt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var msg = AiProviderConnectionTester.RedactSecrets(ex.Message, secretToRedact) ?? "Endpoint unreachable.";
            return Failed(sectionId, msg.Length > 200 ? msg[..200] : msg, testedAt);
        }
    }

    private static string EnsureSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestUploadScannerAsync(
        UploadScannerSettings settings,
        IWebHostEnvironment env,
        UploadScannerOptions scannerOptions,
        string sectionId,
        DateTimeOffset testedAt,
        CancellationToken ct)
    {
        if (!settings.Provider.Equals("clamav", StringComparison.OrdinalIgnoreCase))
            return Failed(sectionId, "Upload scanner provider is not set to clamav.", testedAt);
        if (env.IsProduction() && !settings.FailClosedOnError)
            return Failed(sectionId, "ClamAV scan failures must remain fail-closed in production.", testedAt);
        var endpointReason = UploadScannerEndpointGuard.GetUnsafeEndpointReason(
            settings.Host,
            settings.Port,
            scannerOptions.Host,
            scannerOptions.Port,
            requireDeploymentEndpoint: env.IsProduction());
        if (endpointReason is not null)
            return Failed(sectionId, endpointReason, testedAt);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 10)));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(settings.Host, settings.Port, timeoutCts.Token);
            return Ok(sectionId, "ClamAV TCP endpoint accepted a connection.", testedAt);
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException)
        {
            return Failed(sectionId, "ClamAV endpoint is unreachable.", testedAt);
        }
    }

    private static RuntimeSettingsIntegrationTestResponse Ok(string section, string message, DateTimeOffset testedAt)
        => new(section, "ok", message, testedAt);

    private static RuntimeSettingsIntegrationTestResponse Failed(string section, string message, DateTimeOffset testedAt)
        => new(section, "failed", message, testedAt);

    private static bool HasAny(params string?[] values)
        => values.Any(v => !string.IsNullOrWhiteSpace(v));

    private static bool HasAll(params string?[] values)
        => values.All(v => !string.IsNullOrWhiteSpace(v));
}
