using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Services.VideoLibrary;

namespace OetWithDrHesham.Api.Tests;

public class BunnyStreamClientTests
{
    // ── Dormant behaviour ───────────────────────────────────────────────────

    [Fact]
    public async Task Client_ThrowsBunnyNotConfigured_WhileDormant()
    {
        var client = new BunnyStreamClient(
            new ThrowingHttpClientFactory(),
            new FakeSettingsProvider(BunnyStreamSettings.Unconfigured),
            NullLogger<BunnyStreamClient>.Instance);

        await Assert.ThrowsAsync<BunnyNotConfiguredException>(
            () => client.CreateVideoAsync("Title", null, default));
        await Assert.ThrowsAsync<BunnyNotConfiguredException>(
            () => client.GetVideoAsync("video-guid-1", default));
        await Assert.ThrowsAsync<BunnyNotConfiguredException>(
            () => client.SignPlaybackUrlAsync("video-guid-1", 1700000000, default));
        await Assert.ThrowsAsync<BunnyNotConfiguredException>(
            () => client.CreateTusUploadAuthorizationAsync("video-guid-1", 1700000000, default));
    }

    [Fact]
    public async Task Client_TreatsEnabledFalseAsDormant_EvenWithKeys()
    {
        var settings = new BunnyStreamSettings(
            Enabled: false,
            LibraryId: "123456",
            ApiKey: "test-api-key",
            CdnHostname: "vz-test.b-cdn.net",
            TokenAuthKey: "token-auth-key",
            WebhookSecret: null,
            CollectionId: null,
            PlaybackTokenTtlSeconds: 14400);
        var client = new BunnyStreamClient(
            new ThrowingHttpClientFactory(),
            new FakeSettingsProvider(settings),
            NullLogger<BunnyStreamClient>.Instance);

        await Assert.ThrowsAsync<BunnyNotConfiguredException>(
            () => client.SignPlaybackUrlAsync("video-guid-1", 1700000000, default));
    }

    // ── Pinned signature vectors ────────────────────────────────────────────
    // Independently computed (SHA-256 / HMAC-SHA256 over the documented
    // concatenations). If either implementation drifts, these fail loudly.

    [Fact]
    public void TusSignature_MatchesPinnedVector()
    {
        // sha256("123456" + "test-api-key" + "1700000000" + "video-guid-1")
        var signature = BunnyStreamClient.ComputeTusSignature(
            "123456", "test-api-key", 1700000000, "video-guid-1");

        Assert.Equal("065629b7c38ea4d2928d623894d93227a22640aba3053b240aed28e71736463f", signature);
    }

    [Fact]
    public void CdnToken_MatchesPinnedVector()
    {
        // base64url-nopad(raw sha256("token-auth-key" + "/video-guid-1/" + "1700000000"
        //   + "token_path=/video-guid-1/")) — the trailing token_path parameter-data
        // suffix is required; verified against the live Bunny CDN (2026-07-03).
        var token = BunnyStreamClient.ComputeCdnToken("token-auth-key", "/video-guid-1/", 1700000000);

        Assert.Equal("TzIgH5lOXdzgaFl7T-fdR-ZHj4nTU3QCZoPIpKvbTWI", token);
    }

    [Fact]
    public void SignedPlaybackUrl_ComposesTokenExpiresAndTokenPath()
    {
        var url = BunnyStreamClient.BuildSignedPlaybackUrl(
            "vz-test.b-cdn.net", "video-guid-1",
            BunnyStreamClient.ComputeCdnToken("token-auth-key", "/video-guid-1/", 1700000000),
            1700000000, "/video-guid-1/");

        Assert.StartsWith("https://vz-test.b-cdn.net/video-guid-1/playlist.m3u8?token=", url, StringComparison.Ordinal);
        Assert.Contains("token=TzIgH5lOXdzgaFl7T-fdR-ZHj4nTU3QCZoPIpKvbTWI", url, StringComparison.Ordinal);
        Assert.Contains("expires=1700000000", url, StringComparison.Ordinal);
        Assert.Contains("token_path=%2Fvideo-guid-1%2F", url, StringComparison.Ordinal);
    }

    [Fact]
    public void ThumbnailToken_MatchesPinnedVector()
    {
        // base64url-nopad(raw sha256("token-auth-key" + "/video-guid-1/thumbnail.jpg"
        //   + "1700000000")) — Bunny's basic (exact-path) URL token: file-scoped,
        //   NO token_path parameter. Computed independently via node:crypto.
        var token = BunnyStreamClient.ComputeFileToken(
            "token-auth-key", "/video-guid-1/thumbnail.jpg", 1700000000);

        Assert.Equal("CqWtgOdmU3o2siHbMAtLjIWbAQ5sO3pcXAn2PhJRTJY", token);
    }

    [Fact]
    public void SignThumbnailUrl_AppendsFileScopedTokenAndExpires()
    {
        var url = BunnyStreamClient.SignThumbnailUrl(
            "https://vz-test.b-cdn.net/video-guid-1/thumbnail.jpg", 1700000000, "token-auth-key");

        Assert.StartsWith(
            "https://vz-test.b-cdn.net/video-guid-1/thumbnail.jpg?token=", url, StringComparison.Ordinal);
        Assert.Contains("token=CqWtgOdmU3o2siHbMAtLjIWbAQ5sO3pcXAn2PhJRTJY", url, StringComparison.Ordinal);
        Assert.Contains("expires=1700000000", url, StringComparison.Ordinal);
        // File-scoped: must NOT carry token_path (that would authorize the whole
        // /{videoId}/ directory incl. playlist.m3u8 → playback-attestation bypass).
        Assert.DoesNotContain("token_path", url, StringComparison.Ordinal);
    }

    [Fact]
    public void ThumbnailToken_DiffersFromDirectoryPlaybackToken()
    {
        // Guards the security property: the thumbnail (file) token and the playback
        // (directory) token for the same video must NOT be interchangeable.
        var fileToken = BunnyStreamClient.ComputeFileToken(
            "token-auth-key", "/video-guid-1/thumbnail.jpg", 1700000000);
        var dirToken = BunnyStreamClient.ComputeCdnToken(
            "token-auth-key", "/video-guid-1/", 1700000000);

        Assert.NotEqual(dirToken, fileToken);
    }

    [Fact]
    public void VideoThumbnailUrl_SignsBunnyUrl_WhenTokenAuthConfigured()
    {
        var bunny = new BunnyStreamSettings(
            Enabled: true, LibraryId: "123456", ApiKey: "k", CdnHostname: "vz-test.b-cdn.net",
            TokenAuthKey: "token-auth-key", WebhookSecret: null, CollectionId: null,
            PlaybackTokenTtlSeconds: 14400);
        var video = new LibraryVideo
        {
            Id = "v1",
            Title = "T",
            BunnyVideoId = "video-guid-1",
            BunnyThumbnailUrl = "https://vz-test.b-cdn.net/video-guid-1/thumbnail.jpg",
        };

        var url = VideoThumbnailUrl.Resolve(video, bunny);

        Assert.NotNull(url);
        Assert.Contains("token=", url, StringComparison.Ordinal);
        Assert.Contains("expires=", url, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoThumbnailUrl_ReturnsRaw_WhenTokenAuthMissing()
    {
        // Feature dormant / open pull zone → no key to sign with; serve unchanged.
        var video = new LibraryVideo
        {
            Id = "v1",
            Title = "T",
            BunnyVideoId = "video-guid-1",
            BunnyThumbnailUrl = "https://vz-test.b-cdn.net/video-guid-1/thumbnail.jpg",
        };

        var url = VideoThumbnailUrl.Resolve(video, BunnyStreamSettings.Unconfigured);

        Assert.Equal("https://vz-test.b-cdn.net/video-guid-1/thumbnail.jpg", url);
    }

    [Fact]
    public void VideoThumbnailUrl_PrefersCustomAsset_OverBunny()
    {
        var bunny = new BunnyStreamSettings(
            Enabled: true, LibraryId: "123456", ApiKey: "k", CdnHostname: "vz-test.b-cdn.net",
            TokenAuthKey: "token-auth-key", WebhookSecret: null, CollectionId: null,
            PlaybackTokenTtlSeconds: 14400);
        var video = new LibraryVideo
        {
            Id = "v1",
            Title = "T",
            BunnyVideoId = "video-guid-1",
            BunnyThumbnailUrl = "https://vz-test.b-cdn.net/video-guid-1/thumbnail.jpg",
            CustomThumbnailMediaAssetId = "asset-9",
        };

        var url = VideoThumbnailUrl.Resolve(video, bunny);

        Assert.Equal("/v1/media/asset-9/content", url);
    }

    [Fact]
    public void AttestationSignature_MatchesPinnedVector()
    {
        // HMAC-SHA256 keyed with the LITERAL UTF-8 bytes of "aabbccdd" (NOT
        // hex-decoded) over "nonce-1|vid-1|user-1|tauri|v1" — matching how the
        // native shells key HMAC with the raw secret string. Vector computed
        // independently via node:crypto createHmac('sha256','aabbccdd').
        var signature = OetWithDrHesham.Api.Services.VideoLibrary.VideoAttestationService.ComputeSignature(
            "aabbccdd", "nonce-1", "vid-1", "user-1", "tauri", "v1");

        Assert.Equal("91b3f75483c7fa96794fd7df92e4951f30dddf693cdf8b804e07387fc8d8a4d9", signature);
    }

    [Fact]
    public void BunnyStatusMapping_Maps3And4ToReady()
    {
        // 0 = Created (object exists, bytes not yet received) → Uploading so an
        // interrupted upload stays recoverable, not a phantom encode.
        Assert.Equal(VideoEncodeStatus.Uploading, VideoLibraryAdminService.MapBunnyStatus(0));
        Assert.Equal(VideoEncodeStatus.Processing, VideoLibraryAdminService.MapBunnyStatus(1));
        Assert.Equal(VideoEncodeStatus.Encoding, VideoLibraryAdminService.MapBunnyStatus(2));
        Assert.Equal(VideoEncodeStatus.Ready, VideoLibraryAdminService.MapBunnyStatus(3));
        Assert.Equal(VideoEncodeStatus.Ready, VideoLibraryAdminService.MapBunnyStatus(4));
        Assert.Equal(VideoEncodeStatus.Failed, VideoLibraryAdminService.MapBunnyStatus(5));
        Assert.Equal(VideoEncodeStatus.Failed, VideoLibraryAdminService.MapBunnyStatus(6));
    }

    // ── Doubles ─────────────────────────────────────────────────────────────

    private sealed class FakeSettingsProvider(BunnyStreamSettings bunny) : IRuntimeSettingsProvider
    {
        public Task<EffectiveSettings> GetAsync(CancellationToken ct = default)
            => Task.FromResult(TestEffectiveSettingsFactory.Create(bunny));

        public Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default)
            => Task.FromResult(new RuntimeSettingsRow { Id = "default" });

        public void Invalidate() { }
        public string Protect(string plain) => plain;
        public string? Unprotect(string? cipher) => cipher;
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("HTTP must not be reached while Bunny is dormant.");
    }
}

/// <summary>
/// Builds a minimal EffectiveSettings for unit tests that only consume the
/// Video Library sections. Every other section is a blank default.
/// </summary>
internal static class TestEffectiveSettingsFactory
{
    public static EffectiveSettings Create(
        BunnyStreamSettings? bunny = null,
        VideoAttestationSettings? attestation = null)
        => new(
            Email: new EmailSettings(null, null, null, null, null, null, null, null, null),
            Billing: new BillingSettings(null, null, null, null, null, null, null, null, null, null),
            Sentry: new SentrySettings(null, null, null),
            Backup: new BackupSettings(null, null, null, null, null),
            OAuth: new OAuthSettings(null, null, null, null, null, null, null, null),
            Push: new PushSettings(null, null, null, null, null, null),
            UploadScanner: new UploadScannerSettings("noop", "127.0.0.1", 3310, 5, false),
            Zoom: new ZoomSettings(false, null, null, null, "https://api.zoom.us/v2", "https://zoom.us/oauth/token", null, null, null, null, 300, false),
            Stripe: new StripeSettings(null, null, null, true, Array.Empty<string>(), null, false, null),
            LiveClasses: new LiveClassSettings(false),
            SpeakingWhisper: new SpeakingWhisperSettings(null, "https://api.openai.com/v1", "whisper-1", false),
            SpeakingLiveKit: new SpeakingLiveKitSettings("disabled", null, null, null, null, null, 1800, false, false),
            SpeakingAi: new SpeakingAiSettings(null, null, false, false),
            SpeakingStorage: new SpeakingStorageSettings(null, null, "eu-west-2", null, false),
            SpeakingCompliance: new SpeakingComplianceSettings("recording.v1", "live_video_with_tutor.v1", 90, 365, 2555),
            SpeakingFeatures: new SpeakingFeatureSettings(false),
            CheckoutCom: new CheckoutComSettings("https://api.checkout.com", null, null, null, null, null, null),
            Paymob: new PaymobSettings("https://accept.paymob.com", null, null, null, new Dictionary<string, int>(), 0, null, null),
            PayTabs: new PayTabsSettings("https://secure.paytabs.com", null, null, null, null, null),
            Soketi: new SoketiSettings("localhost", 6001, "oet-app", "oet-key", null, false, false),
            DataRetention: new DataRetentionSettings(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromHours(6), 5000),
            ExpertAutoAssignment: new ExpertAutoAssignmentSettings(false, 30, 60, 48, 12, 8, 24, 50),
            PasswordPolicy: new PasswordPolicySettings(10, false, false, false, false, "https://api.pwnedpasswords.com/", TimeSpan.FromSeconds(3)),
            AiAssistant: new AiAssistantSettings(false, true, 10, 50, 30, 1_048_576, 300, 3, 60, 10, 300, "text-embedding-3-small", 512),
            AiGateway: new AiGatewaySettings("digitalocean-serverless", "https://inference.do-ai.run/v1", "glm-5", string.Empty, 4096, 0.2, 4, 30, "api.dictionaryapi.dev", ["api.dictionaryapi.dev"], 200, 4000, 65536),
            Writing: new WritingSettings(false, false, 0m, 80, 30, null, false, false, 50, 36, 1, 24),
            Platform: new PlatformSettings(null, null, "example.invalid"),
            Messaging: new MessagingSettings(false, "https://api.twilio.com", null, null, null, null, false, "https://graph.facebook.com/v20.0", null, null, null, false, false),
            Fx: new FxSettings("USD", null, null, false),
            Storage: new StorageSettings("local", null, null, null, null, "us-east-1", 3600, 1, 1, 1, 1, 1, 1, 1, 1.0, 1, 1),
            PdfExtraction: new PdfExtractionSettings("auto", string.Empty, null, 50),
            Pronunciation: new PronunciationSettings("auto", string.Empty, "en-GB", string.Empty, "whisper-1", "https://generativelanguage.googleapis.com/v1beta", "gemini-3.5-flash", 1, 45, -1, 7),
            AuthTokens: new AuthTokenSettings(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(10), null),
            UpdatedByUserId: null,
            UpdatedByUserName: null,
            UpdatedAt: null)
        {
            BunnyStream = bunny ?? BunnyStreamSettings.Unconfigured,
            VideoAttestation = attestation ?? VideoAttestationSettings.Unconfigured,
        };
}
