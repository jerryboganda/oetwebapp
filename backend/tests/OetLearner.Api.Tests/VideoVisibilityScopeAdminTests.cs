using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Spec §7.4 / OQ-2: admin-side visibility scope rules — forced SHARED on
/// Listening/Reading/basic-english, target-only allow-list for Writing/Speaking, and
/// the publish gate that blocks target-less Writing/Speaking videos.
/// </summary>
public class VideoVisibilityScopeAdminTests(BunnyMockedWebApplicationFactory factory)
    : IClassFixture<BunnyMockedWebApplicationFactory>
{
    private const string LibraryId = "123456";
    private const string WebhookSecret = "hook-secret-1234567890";

    // ── Pure vocabulary helpers ─────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("listening")]
    [InlineData("reading")]
    [InlineData("basic-english")]
    [InlineData("  READING  ")]
    public void IsForcedSharedSubtest_ListeningReadingBasicEnglish_ReturnTrue(string? subtest)
        => Assert.True(VideoVisibilityScopes.IsForcedSharedSubtest(subtest));

    [Theory]
    [InlineData("writing")]
    [InlineData("speaking")]
    [InlineData("WRITING")]
    public void IsForcedSharedSubtest_WritingSpeaking_ReturnFalse(string? subtest)
        => Assert.False(VideoVisibilityScopes.IsForcedSharedSubtest(subtest));

    [Theory]
    [InlineData("writing")]
    [InlineData("speaking")]
    [InlineData("Speaking")]
    public void RequiresExplicitTarget_WritingSpeaking_ReturnTrue(string? subtest)
        => Assert.True(VideoVisibilityScopes.RequiresExplicitTarget(subtest));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("listening")]
    [InlineData("reading")]
    [InlineData("basic-english")]
    [InlineData("mock")]
    public void RequiresExplicitTarget_OtherSubtests_ReturnFalse(string? subtest)
        => Assert.False(VideoVisibilityScopes.RequiresExplicitTarget(subtest));

    // ── Admin integration flows (Bunny mocked factory) ──────────────────────

    [Fact]
    public async Task Patch_ListeningVideo_ForceShared_IgnoringRequestedScope()
    {
        await ConfigureBunnyAsync();
        var videoId = await SeedReadyVideoAsync();
        using var admin = CreateAdminClient();

        var patch = await admin.PatchAsJsonAsync(
            $"/v1/admin/video-library/videos/{videoId}",
            new { subtestCode = "listening", visibilityScope = "FULL_MEDICINE" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(VideoVisibilityScopes.Shared,
            (await ReadJsonAsync(patch)).GetProperty("visibilityScope").GetString());

        var detail = await admin.GetAsync($"/v1/admin/video-library/videos/{videoId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(VideoVisibilityScopes.Shared,
            (await ReadJsonAsync(detail)).GetProperty("visibilityScope").GetString());
    }

    [Fact]
    public async Task Patch_WritingVideo_WithSharedScope_Rejected()
    {
        await ConfigureBunnyAsync();
        var videoId = await SeedReadyVideoAsync();
        using var admin = CreateAdminClient();

        var patch = await admin.PatchAsJsonAsync(
            $"/v1/admin/video-library/videos/{videoId}",
            new { subtestCode = "writing", visibilityScope = "SHARED" });

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
        var payload = await patch.Content.ReadAsStringAsync();
        Assert.Contains("invalid_visibility_scope", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_WritingVideo_WithExplicitTarget_PersistsAndReturns()
    {
        await ConfigureBunnyAsync();
        var videoId = await SeedReadyVideoAsync();
        using var admin = CreateAdminClient();

        // English Writing videos are course-shared per the content matrix (empty
        // profession targets), but still carry an isolated visibility scope.
        var patch = await admin.PatchAsJsonAsync(
            $"/v1/admin/video-library/videos/{videoId}",
            new
            {
                subtestCode = "writing",
                language = "en",
                courseFolder = "sessions",
                targetProfessionIds = Array.Empty<string>(),
                visibilityScope = "FULL_MEDICINE",
            });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(VideoVisibilityScopes.FullMedicine,
            (await ReadJsonAsync(patch)).GetProperty("visibilityScope").GetString());
    }

    [Fact]
    public async Task PublishGate_WritingVideoWithoutTarget_BlockedUntilScopeSet()
    {
        await ConfigureBunnyAsync();
        var videoId = await SeedReadyVideoAsync();
        using var admin = CreateAdminClient();

        // Ready Writing video with a valid matrix scope (en + no targets) but no
        // visibility target yet — the publish gate must block it.
        var patch = await admin.PatchAsJsonAsync(
            $"/v1/admin/video-library/videos/{videoId}",
            new { subtestCode = "writing", language = "en", targetProfessionIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var gateResponse = await admin.GetAsync($"/v1/admin/video-library/videos/{videoId}/publish-gate");
        Assert.Equal(HttpStatusCode.OK, gateResponse.StatusCode);
        var gate = await ReadJsonAsync(gateResponse);
        Assert.False(gate.GetProperty("canPublish").GetBoolean());
        Assert.Contains(
            "Choose a visibility target (Medicine, Nursing, Pharmacy, or Crash) for this Writing/Speaking video before publishing.",
            gate.GetProperty("errors").EnumerateArray().Select(e => e.GetString() ?? string.Empty));

        // Setting an explicit target clears the error and lets the gate pass.
        var scopePatch = await admin.PatchAsJsonAsync(
            $"/v1/admin/video-library/videos/{videoId}",
            new { visibilityScope = "FULL_MEDICINE" });
        Assert.Equal(HttpStatusCode.OK, scopePatch.StatusCode);

        var gateAfter = await ReadJsonAsync(
            await admin.GetAsync($"/v1/admin/video-library/videos/{videoId}/publish-gate"));
        Assert.True(gateAfter.GetProperty("canPublish").GetBoolean(),
            $"Gate errors after scope set: {gateAfter.GetProperty("errors").GetRawText()}");
    }

    // ── Fixture helpers (mirroring VideoLibraryAdminFlowsTests) ────────────

    private HttpClient CreateAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "video-admin@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Video Admin");
        client.DefaultRequestHeaders.Add(
            "X-Debug-AdminPermissions",
            string.Join(",", AdminPermissions.All));
        return client;
    }

    private async Task ConfigureBunnyAsync()
    {
        var provider = factory.Services.GetRequiredService<IRuntimeSettingsProvider>();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var row = await db.RuntimeSettings.FirstOrDefaultAsync(r => r.Id == "default");
        if (row is null)
        {
            row = new RuntimeSettingsRow { Id = "default", UpdatedAt = DateTimeOffset.UtcNow };
            db.RuntimeSettings.Add(row);
        }
        row.BunnyStreamEnabled = true;
        row.BunnyStreamLibraryId = LibraryId;
        row.BunnyStreamApiKeyEncrypted = provider.Protect("test-api-key");
        row.BunnyStreamCdnHostname = "vz-test.b-cdn.net";
        row.BunnyStreamTokenAuthKeyEncrypted = provider.Protect("token-auth-key");
        row.BunnyStreamWebhookSecretEncrypted = provider.Protect(WebhookSecret);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        provider.Invalidate();
    }

    private async Task<string> SeedReadyVideoAsync()
    {
        var videoId = $"vid-{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.LibraryVideos.Add(new LibraryVideo
        {
            Id = videoId,
            Title = "Seeded visibility-scope video",
            AccessTier = "free",
            Status = ContentStatus.Draft,
            PublishAt = null,
            EncodeStatus = VideoEncodeStatus.Ready,
            BunnyVideoId = $"bunny-{Guid.NewGuid():N}",
            BunnyLibraryId = LibraryId,
            DurationSeconds = 600,
            ProfessionIdsJson = "[]",
            ChaptersJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return videoId;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
