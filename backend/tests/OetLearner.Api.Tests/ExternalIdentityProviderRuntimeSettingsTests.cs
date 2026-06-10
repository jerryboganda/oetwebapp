using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

public sealed class ExternalIdentityProviderRuntimeSettingsTests
{
    [Fact]
    public void BuildAuthorizationUri_UsesRuntimeGoogleCredentialsWhenEnvProviderIsDisabled()
    {
        var client = new ExternalIdentityProviderClient(
            new HttpClient(),
            Options.Create(new ExternalAuthOptions()),
            RuntimeOAuthProvider());

        var uri = client.BuildAuthorizationUri(
            ExternalAuthProviders.Google,
            "state-1",
            "https://app.example/auth/callback");

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("runtime-google-client", query["client_id"]);
        Assert.Equal("state-1", query["state"]);
    }

    private static TestRuntimeSettingsProvider RuntimeOAuthProvider()
        => new(
            new EffectiveSettings(
                Email: new EmailSettings(null, null, null, null, null, null, null, null, null),
                Billing: new BillingSettings(null, null, null, null, null, null, null, null, null, null),
                Sentry: new SentrySettings(null, null, null),
                Backup: new BackupSettings(null, null, null, null, null),
                OAuth: new OAuthSettings(
                    GoogleClientId: "runtime-google-client",
                    GoogleClientSecret: "runtime-google-secret",
                    AppleClientId: null,
                    AppleTeamId: null,
                    AppleKeyId: null,
                    ApplePrivateKey: null,
                    FacebookAppId: null,
                    FacebookAppSecret: null),
                Push: new PushSettings(null, null, null, null, null, null),
                UploadScanner: new UploadScannerSettings("noop", "127.0.0.1", 3310, 30, true),
                Zoom: new ZoomSettings(false, null, null, null, "https://api.zoom.us/v2", "https://zoom.us/oauth/token", null, null, null, null, 300, false),
                Stripe: new StripeSettings(null, null, null, true, Array.Empty<string>(), null, false, null),
                LiveClasses: new LiveClassSettings(AiRecordingProcessingEnabled: false),
                SpeakingWhisper: SpeakingSettingsTestDefaults.Whisper(),
                SpeakingLiveKit: SpeakingSettingsTestDefaults.LiveKit(),
                SpeakingAi: SpeakingSettingsTestDefaults.Ai(),
                SpeakingStorage: SpeakingSettingsTestDefaults.Storage(),
                SpeakingCompliance: SpeakingSettingsTestDefaults.Compliance(),
                SpeakingFeatures: SpeakingSettingsTestDefaults.Features(),
                CheckoutCom: TestRuntimeSettingsProvider.DefaultCheckoutCom(),
                Paymob: TestRuntimeSettingsProvider.DefaultPaymob(),
                PayTabs: TestRuntimeSettingsProvider.DefaultPayTabs(),
                Soketi: TestRuntimeSettingsProvider.DefaultSoketi(),
                UpdatedByUserId: null,
                UpdatedByUserName: null,
                UpdatedAt: null),
            new RuntimeSettingsRow
            {
                GoogleClientId = "runtime-google-client",
                GoogleClientSecretEncrypted = "protected-google-secret",
            });
}
