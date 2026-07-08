using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Covers the server-side forced-update gate (ClientVersionGateMiddleware):
/// out-of-date shell clients get 426, while the website, exempt endpoints, and
/// the disabled state are never affected.
/// </summary>
[Collection("AuthFlows")]
public sealed class ClientVersionGateTests
{
    [Fact]
    public async Task BelowMinVersion_WithGateEnabled_Returns426()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        await EnableGateAsync(factory, desktopMin: "9.9.9");

        using var client = CreateSystemAdminClient(factory);
        client.DefaultRequestHeaders.Add("X-Client-Platform", "desktop");
        client.DefaultRequestHeaders.Add("X-App-Version", "0.0.1");

        var response = await client.GetAsync("/v1/admin/launch-readiness/settings");

        Assert.Equal((HttpStatusCode)426, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UPGRADE_REQUIRED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("9.9.9", body.RootElement.GetProperty("minVersion").GetString());
    }

    [Fact]
    public async Task AtOrAboveMinVersion_WithGateEnabled_Passes()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        await EnableGateAsync(factory, desktopMin: "1.0.0");

        using var client = CreateSystemAdminClient(factory);
        client.DefaultRequestHeaders.Add("X-Client-Platform", "desktop");
        client.DefaultRequestHeaders.Add("X-App-Version", "1.0.0");

        var response = await client.GetAsync("/v1/admin/launch-readiness/settings");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task BelowMinVersion_WithGateDisabled_Passes()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        // Set a high minimum but leave the master flag OFF.
        using (var admin = CreateSystemAdminClient(factory))
        {
            var seed = await admin.PutAsJsonAsync("/v1/admin/launch-readiness/settings", new
            {
                enforceClientVersionGate = false,
                desktopMinSupportedVersion = "9.9.9",
            });
            seed.EnsureSuccessStatusCode();
        }

        using var client = CreateSystemAdminClient(factory);
        client.DefaultRequestHeaders.Add("X-Client-Platform", "desktop");
        client.DefaultRequestHeaders.Add("X-App-Version", "0.0.1");

        var response = await client.GetAsync("/v1/admin/launch-readiness/settings");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ForceUpdate_BlocksEvenWhenVersionMeetsMinimum()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        using (var admin = CreateSystemAdminClient(factory))
        {
            var seed = await admin.PutAsJsonAsync("/v1/admin/launch-readiness/settings", new
            {
                enforceClientVersionGate = true,
                mobileMinSupportedVersion = "1.0.0",
                mobileForceUpdate = true,
            });
            seed.EnsureSuccessStatusCode();
        }

        using var client = CreateSystemAdminClient(factory);
        client.DefaultRequestHeaders.Add("X-Client-Platform", "android");
        client.DefaultRequestHeaders.Add("X-App-Version", "5.0.0");

        var response = await client.GetAsync("/v1/admin/launch-readiness/settings");

        Assert.Equal((HttpStatusCode)426, response.StatusCode);
    }

    [Fact]
    public async Task NoShellHeaders_NeverBlocked()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        await EnableGateAsync(factory, desktopMin: "9.9.9");

        // Anonymous browser request with no version headers — the website.
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/v1/public/runtime-config");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task VersionEndpoint_IsExempt_EvenForOutdatedShell()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        await EnableGateAsync(factory, desktopMin: "9.9.9");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Platform", "desktop");
        client.DefaultRequestHeaders.Add("X-App-Version", "0.0.1");

        var response = await client.GetAsync("/v1/app-release?platform=desktop");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task HealthEndpoint_IsExempt_EvenForOutdatedShell()
    {
        using var env = DevAuthEnv.Enable();
        using var factory = new TestWebApplicationFactory();
        await EnableGateAsync(factory, desktopMin: "9.9.9");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Platform", "desktop");
        client.DefaultRequestHeaders.Add("X-App-Version", "0.0.1");

        var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
    }

    private static async Task EnableGateAsync(TestWebApplicationFactory factory, string desktopMin)
    {
        using var admin = CreateSystemAdminClient(factory);
        var seed = await admin.PutAsJsonAsync("/v1/admin/launch-readiness/settings", new
        {
            enforceClientVersionGate = true,
            desktopMinSupportedVersion = desktopMin,
            desktopForceUpdate = false,
        });
        seed.EnsureSuccessStatusCode();
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
        client.DefaultRequestHeaders.Add("X-Debug-Email", "version-gate@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Version Gate Admin");
        return client;
    }
}
