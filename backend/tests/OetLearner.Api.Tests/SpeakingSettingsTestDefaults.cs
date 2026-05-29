using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

/// <summary>
/// Shared "disabled / safe-default" Speaking runtime settings used when building
/// <see cref="EffectiveSettings"/> in tests. The values mirror the env-fallback
/// defaults produced by <c>RuntimeSettingsProvider</c> for a fresh, unconfigured
/// environment (Speaking features off, no provider credentials) so test fixtures
/// match real boot behaviour. Tests that care about Speaking settings should
/// construct their own values instead of using these.
/// </summary>
internal static class SpeakingSettingsTestDefaults
{
    public static SpeakingWhisperSettings Whisper() => new(
        ApiKey: null,
        BaseUrl: "https://api.openai.com/v1",
        Model: "whisper-1",
        IsConfigured: false);

    public static SpeakingLiveKitSettings LiveKit() => new(
        Provider: "disabled",
        ApiKey: null,
        ApiSecret: null,
        WssUrl: null,
        WebhookSigningSecret: null,
        EgressBucket: null,
        DefaultMaxDurationSeconds: 1800,
        EgressEnabled: false,
        IsEnabled: false);

    public static SpeakingAiSettings Ai() => new(
        AnthropicApiKey: null,
        ElevenLabsApiKey: null,
        IsAnthropicConfigured: false,
        IsElevenLabsConfigured: false);

    public static SpeakingStorageSettings Storage() => new(
        AwsAccessKeyId: null,
        AwsSecretAccessKey: null,
        Region: "eu-west-2",
        Bucket: null,
        IsConfigured: false);

    public static SpeakingComplianceSettings Compliance() => new(
        CurrentConsentVersion: "recording.v1",
        CurrentLiveVideoConsentVersion: "live_video_with_tutor.v1",
        RetentionDaysDefault: 90,
        RetentionDaysWhenTutorReviewed: 365,
        AuditLogRetentionDays: 2555);

    public static SpeakingFeatureSettings Features() => new(
        SpeakingV2Enabled: false);
}
