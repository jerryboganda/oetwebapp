using System.Net;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Learner;

/// <summary>
/// <c>GET /v1/features?keys=a,b,c</c> returns several learner-visible release gates in one
/// request (the learner shell used to issue one request per flag on every cold load). It must
/// keep the single-flag route's allow-list and per-flag semantics exactly.
/// </summary>
public class LearnerFeatureFlagBatchTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly string[] ExposedKeys = ["video_library", "strategy_guides", "ai_learning_companion"];

    private readonly TestWebApplicationFactory _factory;

    public LearnerFeatureFlagBatchTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Batch_ReturnsEveryExposedFlag_AndOmitsKeysNotExposedToLearners()
    {
        using var client = CreateClient("learner");

        var response = await client.GetAsync("/v1/features?keys=video_library,strategy-guides,ai_learning_companion,gamification,internal_ops_switch");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var flags = await ReadFlagsAsync(response);
        // Aliases round-trip under the key the caller sent; unknown keys are simply absent.
        Assert.Equal(
            new[] { "ai_learning_companion", "strategy-guides", "video_library" },
            flags.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Batch_AgreesWithTheSingleFlagRoute_ForEveryExposedKey()
    {
        using var client = CreateClient("learner");

        var batch = await ReadFlagsAsync(await client.GetAsync($"/v1/features?keys={string.Join(',', ExposedKeys)}"));

        foreach (var key in ExposedKeys)
        {
            var single = await client.GetAsync($"/v1/features/{key}");
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            using var json = JsonDocument.Parse(await single.Content.ReadAsStringAsync());
            Assert.Equal(json.RootElement.GetProperty("enabled").GetBoolean(), batch[key]);
        }
    }

    [Fact]
    public async Task SingleFlagRoute_StillAnswers404ForAKeyNotExposedToLearners()
    {
        using var client = CreateClient("learner");

        var response = await client.GetAsync("/v1/features/internal_ops_switch");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Batch_WithoutKeys_ReturnsAnEmptyList()
    {
        using var client = CreateClient("learner");

        var response = await client.GetAsync("/v1/features");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ReadFlagsAsync(response));
    }

    [Fact]
    public async Task Batch_DeduplicatesRepeatedKeys()
    {
        using var client = CreateClient("learner");

        var response = await client.GetAsync("/v1/features?keys=video_library,VIDEO_LIBRARY,video_library");

        var flags = await ReadFlagsAsync(response);
        Assert.Single(flags);
        Assert.True(flags.ContainsKey("video_library"));
    }

    [Fact]
    public async Task Batch_ResolvesAtMostSixteenDistinctKeys()
    {
        using var client = CreateClient("learner");
        var filler = string.Join(',', Enumerable.Range(0, 16).Select(index => $"not_exposed_{index}"));

        // The exposed key is the 17th distinct key, so it falls outside the cap.
        var response = await client.GetAsync($"/v1/features?keys={filler},video_library");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ReadFlagsAsync(response));
    }

    [Fact]
    public async Task Batch_IsLearnerOnly()
    {
        using var client = CreateClient("admin");

        var response = await client.GetAsync("/v1/features?keys=video_library");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"feature-flag-batch-{role}");
        client.DefaultRequestHeaders.Add("X-Debug-Role", role);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"feature-flag-batch-{role}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Feature Flag Batch");
        return client;
    }

    private static async Task<Dictionary<string, bool>> ReadFlagsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("flags")
            .EnumerateArray()
            .ToDictionary(
                flag => flag.GetProperty("key").GetString()!,
                flag => flag.GetProperty("enabled").GetBoolean(),
                StringComparer.Ordinal);
    }
}
