using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Services.VideoLibrary;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// MISSION-CRITICAL: playback sessions must only ever be issued to callers
/// that pass the native-client HMAC attestation. Anything else — missing
/// body, wrong signature, replayed/expired/foreign nonce, unknown platform,
/// unconfigured keys — is a 403, and the signed URL never leaks.
/// </summary>
public class VideoPlaybackAttestationTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string TauriSecretHex = "a3b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1";
    private const string KeysJson = "{\"tauri:v1\":\"" + TauriSecretHex + "\"}";

    // ── Failure paths ───────────────────────────────────────────────────────

    [Fact]
    public async Task PlaybackSession_WithoutAttestationBody_Returns403()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: true, configureBunny: true);
        using var client = CreateLearnerClient(userId);

        var response = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("attestation_invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaybackSession_BadSignature_Returns403AndAudits()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: true, configureBunny: true);
        using var client = CreateLearnerClient(userId);
        var nonce = await IssueChallengeAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce, platform = "tauri", keyId = "v1", signature = new string('a', 64) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("attestation_invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var audited = await db.AuditEvents.AsNoTracking().AnyAsync(a =>
            a.Action == "video.playback.attestation_failed" && a.ResourceId == videoId);
        Assert.True(audited, "Expected an attestation-failure audit event.");
    }

    [Fact]
    public async Task PlaybackSession_ReusedNonce_Returns403()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: true, configureBunny: true);
        using var client = CreateLearnerClient(userId);
        var nonce = await IssueChallengeAsync(client);
        var signature = VideoAttestationService.ComputeSignature(
            TauriSecretHex, nonce, videoId, userId, "tauri", "v1");

        var first = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce, platform = "tauri", keyId = "v1", signature });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce, platform = "tauri", keyId = "v1", signature });
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Contains("attestation_invalid", await replay.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaybackSession_ExpiredNonce_Returns403()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: true, configureBunny: true);
        var nonce = $"expired-{Guid.NewGuid():N}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.VideoAttestationChallenges.Add(new VideoAttestationChallenge
            {
                Id = nonce,
                UserId = userId,
                IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateLearnerClient(userId);
        var signature = VideoAttestationService.ComputeSignature(
            TauriSecretHex, nonce, videoId, userId, "tauri", "v1");
        var response = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce, platform = "tauri", keyId = "v1", signature });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("attestation_invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaybackSession_OtherUsersNonce_Returns403()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: true, configureBunny: true);
        var otherUserId = $"learner-{Guid.NewGuid():N}";
        using var otherClient = CreateLearnerClient(otherUserId);
        var foreignNonce = await IssueChallengeAsync(otherClient);

        using var client = CreateLearnerClient(userId);
        var signature = VideoAttestationService.ComputeSignature(
            TauriSecretHex, foreignNonce, videoId, userId, "tauri", "v1");
        var response = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce = foreignNonce, platform = "tauri", keyId = "v1", signature });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("attestation_invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaybackSession_UnknownPlatformOrKeyId_Returns403Unavailable()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: true, configureBunny: true);
        using var client = CreateLearnerClient(userId);
        var nonce = await IssueChallengeAsync(client);

        var signature = VideoAttestationService.ComputeSignature(
            TauriSecretHex, nonce, videoId, userId, "windows", "v9");
        var response = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce, platform = "windows", keyId = "v9", signature });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("attestation_unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaybackSession_UnconfiguredKeys_Returns403Unavailable()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: false, configureBunny: true);
        using var client = CreateLearnerClient(userId);
        var nonce = await IssueChallengeAsync(client);

        var signature = VideoAttestationService.ComputeSignature(
            TauriSecretHex, nonce, videoId, userId, "tauri", "v1");
        var response = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce, platform = "tauri", keyId = "v1", signature });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("attestation_unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ── Success + concurrency paths ─────────────────────────────────────────

    [Fact]
    public async Task PlaybackSession_ValidHmac_EndToEnd_ReturnsSignedUrl()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: true, configureBunny: true);
        using var client = CreateLearnerClient(userId);
        var nonce = await IssueChallengeAsync(client);
        var signature = VideoAttestationService.ComputeSignature(
            TauriSecretHex, nonce, videoId, userId, "tauri", "v1");

        var response = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce, platform = "tauri", keyId = "v1", signature });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;
        var sessionId = root.GetProperty("sessionId").GetString();
        var playbackUrl = root.GetProperty("playbackUrl").GetString();
        var watermark = root.GetProperty("watermarkText").GetString();

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
        Assert.NotNull(playbackUrl);
        Assert.Contains("playlist.m3u8", playbackUrl, StringComparison.Ordinal);
        Assert.Contains("token=", playbackUrl, StringComparison.Ordinal);
        Assert.Contains("expires=", playbackUrl, StringComparison.Ordinal);
        Assert.Contains("token_path=", playbackUrl, StringComparison.Ordinal);
        Assert.NotNull(watermark);
        Assert.Contains(sessionId![..8], watermark, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("captions").ValueKind);
    }

    [Fact]
    public async Task PlaybackSession_FourthDistinctVideo_Returns409()
    {
        var (videoId, userId) = await SeedAsync(configureKeys: true, configureBunny: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 3; i++)
            {
                db.VideoPlaybackSessions.Add(new VideoPlaybackSession
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = userId,
                    VideoId = $"other-video-{i}-{Guid.NewGuid():N}",
                    Platform = "tauri",
                    AttestationKeyId = "v1",
                    IssuedAt = now,
                    ExpiresAt = now.AddHours(1),
                });
            }
            await db.SaveChangesAsync();
        }

        using var client = CreateLearnerClient(userId);
        var nonce = await IssueChallengeAsync(client);
        var signature = VideoAttestationService.ComputeSignature(
            TauriSecretHex, nonce, videoId, userId, "tauri", "v1");
        var response = await client.PostAsJsonAsync(
            $"/v1/video-library/videos/{videoId}/playback-session",
            new { nonce, platform = "tauri", keyId = "v1", signature });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("concurrent_session_limit", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ── Fixture helpers ─────────────────────────────────────────────────────

    private HttpClient CreateLearnerClient(string learnerId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{learnerId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", learnerId);
        return client;
    }

    private static async Task<string> IssueChallengeAsync(HttpClient client)
    {
        var response = await client.PostAsync("/v1/video-library/attestation/challenge", content: null);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var nonce = payload.RootElement.GetProperty("nonce").GetString();
        Assert.False(string.IsNullOrWhiteSpace(nonce));
        return nonce!;
    }

    /// <summary>
    /// Seeds a published FREE-tier video (so entitlement always passes and the
    /// attestation layer is isolated), the learner row, and the runtime
    /// settings state for this test (attestation keys + Bunny credentials via
    /// the provider's own encryption helper).
    /// </summary>
    private async Task<(string VideoId, string UserId)> SeedAsync(bool configureKeys, bool configureBunny)
    {
        var videoId = $"vid-{Guid.NewGuid():N}";
        var userId = $"learner-{Guid.NewGuid():N}";
        var provider = factory.Services.GetRequiredService<IRuntimeSettingsProvider>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;

        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = userId,
            Email = $"{userId}@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            Timezone = "UTC",
            Locale = "en-AU",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.LibraryVideos.Add(new LibraryVideo
        {
            Id = videoId,
            Title = "Attestation test video",
            AccessTier = "free",
            Status = ContentStatus.Published,
            EncodeStatus = VideoEncodeStatus.Ready,
            BunnyVideoId = $"bunny-{Guid.NewGuid():N}",
            DurationSeconds = 600,
            ProfessionIdsJson = "[]",
            ChaptersJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });

        var row = await db.RuntimeSettings.FirstOrDefaultAsync(r => r.Id == "default");
        if (row is null)
        {
            row = new RuntimeSettingsRow { Id = "default", UpdatedAt = now };
            db.RuntimeSettings.Add(row);
        }
        row.VideoAttestationKeysEncrypted = configureKeys ? provider.Protect(KeysJson) : null;
        if (configureBunny)
        {
            row.BunnyStreamEnabled = true;
            row.BunnyStreamLibraryId = "123456";
            row.BunnyStreamApiKeyEncrypted = provider.Protect("test-api-key");
            row.BunnyStreamCdnHostname = "vz-test.b-cdn.net";
            row.BunnyStreamTokenAuthKeyEncrypted = provider.Protect("token-auth-key");
        }
        else
        {
            row.BunnyStreamEnabled = null;
            row.BunnyStreamLibraryId = null;
            row.BunnyStreamApiKeyEncrypted = null;
            row.BunnyStreamCdnHostname = null;
            row.BunnyStreamTokenAuthKeyEncrypted = null;
        }
        row.UpdatedAt = now;
        await db.SaveChangesAsync();
        provider.Invalidate();

        return (videoId, userId);
    }
}
