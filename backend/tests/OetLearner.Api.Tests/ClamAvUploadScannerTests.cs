using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

public sealed class ClamAvUploadScannerTests
{
    [Fact]
    public async Task ScanAsync_WhenProviderIsNoopInProduction_RejectsFailClosed()
    {
        var scanner = new ClamAvUploadScanner(
            RuntimeSettingsWithScannerProvider("noop"),
            new TestHostEnvironment("Production"),
            Options.Create(new UploadScannerOptions { Provider = "clamav", Host = "clamav", Port = 3310 }),
            NullLogger<ClamAvUploadScanner>.Instance);

        var result = await scanner.ScanAsync(new MemoryStream([1, 2, 3]), "sample.pdf", CancellationToken.None);

        Assert.False(result.clean);
        Assert.Equal("scan_provider_not_clamav", result.reason);
    }

    [Fact]
    public async Task ScanAsync_WhenProviderIsNoopInDevelopment_AllowsLocalBypass()
    {
        var scanner = new ClamAvUploadScanner(
            RuntimeSettingsWithScannerProvider("noop"),
            new TestHostEnvironment("Development"),
            Options.Create(new UploadScannerOptions()),
            NullLogger<ClamAvUploadScanner>.Instance);

        var result = await scanner.ScanAsync(new MemoryStream([1, 2, 3]), "sample.pdf", CancellationToken.None);

        Assert.True(result.clean);
        Assert.Null(result.reason);
    }

    [Fact]
    public async Task ScanAsync_WhenProductionClamAvEndpointDiffersFromDeploymentConfig_RejectsFailClosed()
    {
        var scanner = new ClamAvUploadScanner(
            RuntimeSettingsWithScannerProvider("clamav", host: "attacker.example", port: 3310),
            new TestHostEnvironment("Production"),
            Options.Create(new UploadScannerOptions { Provider = "clamav", Host = "clamav", Port = 3310 }),
            NullLogger<ClamAvUploadScanner>.Instance);

        var result = await scanner.ScanAsync(new MemoryStream([1, 2, 3]), "sample.pdf", CancellationToken.None);

        Assert.False(result.clean);
        Assert.Equal("scan_endpoint_not_allowed", result.reason);
    }

    private static TestRuntimeSettingsProvider RuntimeSettingsWithScannerProvider(
        string provider,
        string host = "127.0.0.1",
        int port = 3310)
        => new(new EffectiveSettings(
            Email: new EmailSettings(null, null, null, null, null, null, null, null, null),
            Billing: new BillingSettings(null, null, null, null, null, null, null, null, null, null),
            Sentry: new SentrySettings(null, null, null),
            Backup: new BackupSettings(null, null, null, null, null),
            OAuth: new OAuthSettings(null, null, null, null, null, null, null, null),
            Push: new PushSettings(null, null, null, null, null, null),
            UploadScanner: new UploadScannerSettings(provider, host, port, 30, true),
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
            DataRetention: TestRuntimeSettingsProvider.DefaultDataRetention(),
            ExpertAutoAssignment: TestRuntimeSettingsProvider.DefaultExpertAutoAssignment(),
            PasswordPolicy: TestRuntimeSettingsProvider.DefaultPasswordPolicy(),
            AiAssistant: TestRuntimeSettingsProvider.DefaultAiAssistant(),
            AiGateway: TestRuntimeSettingsProvider.DefaultAiGateway(),
            Writing: TestRuntimeSettingsProvider.DefaultWriting(),
            Platform: TestRuntimeSettingsProvider.DefaultPlatform(),
            UpdatedByUserId: null,
            UpdatedByUserName: null,
            UpdatedAt: null));

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
