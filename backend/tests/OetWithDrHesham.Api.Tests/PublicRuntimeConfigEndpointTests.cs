using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Wave 5 — GET /v1/public/runtime-config must be reachable ANONYMOUSLY and
/// expose ONLY secret-free boot values (Sentry DSN, Soketi public AppKey,
/// VAPID public key, platform URLs). It must never leak a secret: not by field
/// name (secret/private/apiKey/token/password) and not by value (the configured
/// Soketi AppSecret or VAPID private key).
/// </summary>
public class PublicRuntimeConfigEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ConfiguredSentryDsn = "https://publicpart@o123.ingest.sentry.io/456";
    private const string ConfiguredSoketiAppKey = "soketi-public-key-abc";
    private const string ConfiguredSoketiAppSecret = "soketi-SECRET-must-not-leak-zzz";
    private const string ConfiguredVapidPublicKey = "BVapidPublicKey0000000000000000000000000000public";
    private const string ConfiguredVapidPrivateKey = "vapid-PRIVATE-must-not-leak-yyy";
    private const string ConfiguredWebBaseUrl = "https://app.example.test";
    private const string ConfiguredApiBaseUrl = "https://api.example.test";

    private readonly TestWebApplicationFactory _factory;

    public PublicRuntimeConfigEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> ConfiguredFactory()
        => _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sentry:Dsn"] = ConfiguredSentryDsn,
                    ["Sentry:Environment"] = "test",
                    ["Sentry:TracesSampleRate"] = "0.25",
                    ["Soketi:AppKey"] = ConfiguredSoketiAppKey,
                    ["Soketi:AppSecret"] = ConfiguredSoketiAppSecret,
                    ["Soketi:Host"] = "soketi.example.test",
                    ["Soketi:Port"] = "6001",
                    ["Soketi:Enabled"] = "true",
                    ["WebPush:Enabled"] = "true",
                    ["WebPush:PublicKey"] = ConfiguredVapidPublicKey,
                    ["WebPush:PrivateKey"] = ConfiguredVapidPrivateKey,
                    ["WebPush:Subject"] = "mailto:ops@example.test",
                    ["Platform:PublicWebBaseUrl"] = ConfiguredWebBaseUrl,
                    ["Platform:PublicApiBaseUrl"] = ConfiguredApiBaseUrl,
                });
            });
        });

    [Fact]
    public async Task RuntimeConfig_IsReachableAnonymously_AndReturnsOk()
    {
        using var factory = ConfiguredFactory();
        // No X-Debug-* / Authorization headers — anonymous request.
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/public/runtime-config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RuntimeConfig_ExposesPublicBootValues()
    {
        using var factory = ConfiguredFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/public/runtime-config");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal(ConfiguredSentryDsn, root.GetProperty("sentry").GetProperty("dsn").GetString());
        Assert.Equal(ConfiguredSoketiAppKey, root.GetProperty("soketi").GetProperty("appKey").GetString());
        Assert.Equal(ConfiguredVapidPublicKey, root.GetProperty("webPush").GetProperty("vapidPublicKey").GetString());
        Assert.Equal(ConfiguredWebBaseUrl, root.GetProperty("platform").GetProperty("publicWebBaseUrl").GetString());
        Assert.Equal(ConfiguredApiBaseUrl, root.GetProperty("platform").GetProperty("publicApiBaseUrl").GetString());
    }

    [Fact]
    public async Task RuntimeConfig_DoesNotLeakSecrets_ByValue()
    {
        using var factory = ConfiguredFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/public/runtime-config");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        // The configured Soketi AppSecret and VAPID private key are secrets and
        // must never appear anywhere in the public payload.
        Assert.DoesNotContain(ConfiguredSoketiAppSecret, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ConfiguredVapidPrivateKey, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeConfig_DoesNotLeakSecrets_ByFieldName()
    {
        using var factory = ConfiguredFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/public/runtime-config");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        string[] forbiddenMarkers = ["secret", "private", "apikey", "token", "password"];
        var offending = new List<string>();
        WalkFieldNames(json.RootElement, forbiddenMarkers, offending);

        Assert.True(
            offending.Count == 0,
            $"Public runtime-config exposed field name(s) containing secret markers: {string.Join(", ", offending)}");
    }

    private static void WalkFieldNames(JsonElement element, string[] forbidden, List<string> offending)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var lower = property.Name.ToLowerInvariant();
                    if (forbidden.Any(marker => lower.Contains(marker, StringComparison.Ordinal)))
                    {
                        offending.Add(property.Name);
                    }

                    WalkFieldNames(property.Value, forbidden, offending);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    WalkFieldNames(item, forbidden, offending);
                }

                break;
        }
    }
}
