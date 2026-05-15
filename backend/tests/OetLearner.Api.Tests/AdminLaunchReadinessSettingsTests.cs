using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public sealed class AdminLaunchReadinessSettingsTests
{
    [Fact]
    public async Task SystemAdmin_CanUpdateLaunchReadiness_AndPublicPolicyReadsIt()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateSystemAdminClient(factory);

        var update = await client.PutAsJsonAsync("/v1/admin/launch-readiness/settings", new
        {
            mobileMinSupportedVersion = "1.2.0",
            mobileLatestVersion = "1.3.0",
            mobileForceUpdate = true,
            iosAppStoreUrl = "https://apps.apple.com/app/oet-prep/id1234567890",
            androidPlayStoreUrl = "https://play.google.com/store/apps/details?id=com.oetprep.learner",
            desktopMinSupportedVersion = "2.0.0",
            desktopLatestVersion = "2.1.0",
            desktopForceUpdate = false,
            desktopUpdateFeedUrl = "https://updates.example.test/oet/latest.yml",
            realtimeLegalApprovalStatus = "approved",
            realtimePrivacyApprovalStatus = "approved",
            realtimeProtectedSmokeStatus = "complete",
            realtimeEvidenceUrl = "https://evidence.example.test/realtime-stt",
            realtimeSpendCapApproved = true,
            realtimeTopologyApproved = true,
            releaseOwnerApprovalStatus = "approved",
        });
        update.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal("1.2.0", body.RootElement.GetProperty("mobileMinSupportedVersion").GetString());
        Assert.True(body.RootElement.GetProperty("mobileForceUpdate").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("apiKey", out _));
        Assert.False(body.RootElement.TryGetProperty("signingKey", out _));

        using var anonymousClient = factory.CreateClient();
        var publicPolicy = await anonymousClient.GetAsync("/v1/app-release?platform=ios");
        publicPolicy.EnsureSuccessStatusCode();
        using var publicBody = JsonDocument.Parse(await publicPolicy.Content.ReadAsStringAsync());
        Assert.Equal("ios", publicBody.RootElement.GetProperty("platform").GetString());
        Assert.Equal("1.2.0", publicBody.RootElement.GetProperty("minVersion").GetString());
        Assert.Equal("1.3.0", publicBody.RootElement.GetProperty("latestVersion").GetString());
        Assert.True(publicBody.RootElement.GetProperty("forceUpdate").GetBoolean());
        Assert.Equal("https://apps.apple.com/app/oet-prep/id1234567890", publicBody.RootElement.GetProperty("storeUrl").GetString());
    }

    [Fact]
    public async Task UpdateLaunchReadiness_RejectsLoopbackUrls()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = CreateSystemAdminClient(factory);

        var update = await client.PutAsJsonAsync("/v1/admin/launch-readiness/settings", new
        {
            iosAppStoreUrl = "https://localhost/app",
        });

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Contains("external https:// URL", await update.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonSystemAdmin_CannotReadLaunchReadinessSettings()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.AiConfig);

        var response = await client.GetAsync("/v1/admin/launch-readiness/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class DevAuthEnv : IDisposable
    {
        private const string Key = "Auth__UseDevelopmentAuth";
        private readonly string? _previous;

        private DevAuthEnv()
        {
            _previous = Environment.GetEnvironmentVariable(Key);
            Environment.SetEnvironmentVariable(Key, "true");
        }

        public static DevAuthEnv Enable() => new();
        public void Dispose() => Environment.SetEnvironmentVariable(Key, _previous);
    }

    private static HttpClient CreateSystemAdminClient(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.SystemAdmin);
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "launch-readiness@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Launch Readiness Admin");
        return client;
    }
}
