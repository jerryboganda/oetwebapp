using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// GET /v1/speaking/realtime/sessions/{id}/preflight?provider=... through the real pipeline: the
/// signed-in learner's identity (never the page's query string) decides whether a requested provider
/// is honoured. The service-level rules are in <see cref="LiveVoiceFailoverTests"/>; this proves the
/// route hands the service the verified user id and the raw query value.
/// </summary>
public sealed class LiveVoiceProviderPinEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LiveVoiceProviderPinEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Preflight_APinRequest_IsHonouredOnlyForTheFlaggedLearner_AndNeverRefusedForAnyoneElse()
    {
        using var host = CreateHost();
        var ordinary = await SeedSessionAsync(host);
        var flagged = await SeedSessionAsync(host);
        await AuthoriseAsync(host, flagged.UserId);
        using var ordinaryClient = CreateLearnerClient(host, ordinary.UserId);
        using var flaggedClient = CreateLearnerClient(host, flagged.UserId);

        var ignored = await ordinaryClient.GetAsync($"/v1/speaking/realtime/sessions/{ordinary.SessionId}/preflight?provider=gemini");
        var honoured = await flaggedClient.GetAsync($"/v1/speaking/realtime/sessions/{flagged.SessionId}/preflight?provider=gemini");

        // 200 either way: a candidate who follows a link carrying the parameter is never turned away.
        Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);
        using (var json = JsonDocument.Parse(await ignored.Content.ReadAsStringAsync()))
        {
            Assert.False(json.RootElement.GetProperty("pinned").GetBoolean());
            Assert.Equal("openai", json.RootElement.GetProperty("provider").GetString());
            Assert.Equal(new[] { "openai", "gemini" }, CandidatesOf(json.RootElement));
        }
        Assert.Equal(HttpStatusCode.OK, honoured.StatusCode);
        using (var json = JsonDocument.Parse(await honoured.Content.ReadAsStringAsync()))
        {
            Assert.True(json.RootElement.GetProperty("pinned").GetBoolean());
            Assert.Equal("gemini", json.RootElement.GetProperty("provider").GetString());
            Assert.Equal(new[] { "gemini" }, CandidatesOf(json.RootElement));
        }
    }

    [Fact]
    public async Task Preflight_APinRequest_OnSomeoneElsesSession_Is404_EvenForAFlaggedLearner()
    {
        using var host = CreateHost();
        var owner = await SeedSessionAsync(host);
        var flaggedStranger = await SeedSessionAsync(host);
        await AuthoriseAsync(host, flaggedStranger.UserId);
        using var client = CreateLearnerClient(host, flaggedStranger.UserId);

        var response = await client.GetAsync($"/v1/speaking/realtime/sessions/{owner.SessionId}/preflight?provider=gemini");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string[] CandidatesOf(JsonElement root)
        => root.GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetString()!).ToArray();

    /// <summary>A host with both providers configured, as the live voice tests configure them.</summary>
    private WebApplicationFactory<Program> CreateHost()
    {
        var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<LiveVoiceOptions>(options =>
            {
                options.PrimaryProvider = LiveVoiceProviders.OpenAi;
                options.OpenAiApiKey = LiveVoiceTestKit.OpenAiKey;
                options.GeminiApiKey = LiveVoiceTestKit.GeminiKey;
            })));
        // The catalog probe is a hosted service, which the test host strips: verify both by hand.
        var state = host.Services.GetRequiredService<LiveVoiceProviderProbeState>();
        state.Set(LiveVoiceProviders.OpenAi, true, null);
        state.Set(LiveVoiceProviders.Gemini, true, null);
        return host;
    }

    private static async Task<SeededLiveVoiceSession> SeedSessionAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await LiveVoiceTestKit.SeedReadySessionAsync(db, DateTimeOffset.UtcNow);
    }

    private static async Task AuthoriseAsync(WebApplicationFactory<Program> host, string userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await LiveVoiceTestKit.AuthoriseQaPinAsync(db, userId, DateTimeOffset.UtcNow);
    }

    private static HttpClient CreateLearnerClient(WebApplicationFactory<Program> host, string userId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Learner);
        return client;
    }
}
