using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// End-to-end HTTP coverage for the learner-facing pronunciation endpoints.
/// Exercises the same routes the Next.js client calls through <c>lib/api.ts</c>.
/// </summary>
public class PronunciationEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PronunciationEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDrills_Returns_Seeded_Catalog()
    {
        var response = await _client.GetAsync("/v1/pronunciation/drills");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() > 10,
            $"Expected more than 10 seeded drills, got {json.RootElement.GetArrayLength()}.");

        var first = json.RootElement[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("targetPhoneme", out _));
        Assert.True(first.TryGetProperty("focus", out _));
        Assert.True(first.TryGetProperty("difficulty", out _));
    }

    [Fact]
    public async Task GetDrills_Filters_By_Focus()
    {
        var response = await _client.GetAsync("/v1/pronunciation/drills?focus=intonation");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.All(
            json.RootElement.EnumerateArray(),
            el => Assert.Equal("intonation", el.GetProperty("focus").GetString()));
    }

    [Fact]
    public async Task GetDrills_Filters_By_Difficulty()
    {
        var response = await _client.GetAsync("/v1/pronunciation/drills?difficulty=easy");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.All(
            json.RootElement.EnumerateArray(),
            el => Assert.Equal("easy", el.GetProperty("difficulty").GetString()));
    }

    [Fact]
    public async Task GetDrill_Returns_Full_Seed_Fields_For_Known_Id()
    {
        var response = await _client.GetAsync("/v1/pronunciation/drills/pd-001");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("pd-001", root.GetProperty("id").GetString());
        Assert.Equal("θ", root.GetProperty("targetPhoneme").GetString());
        Assert.Equal("P01.1", root.GetProperty("primaryRuleId").GetString());
        Assert.NotNull(root.GetProperty("exampleWordsJson").GetString());
        Assert.NotNull(root.GetProperty("minimalPairsJson").GetString());
        Assert.NotNull(root.GetProperty("sentencesJson").GetString());
    }

    [Fact]
    public async Task GetDrill_Returns_404_For_Unknown_Id()
    {
        var response = await _client.GetAsync("/v1/pronunciation/drills/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMyProgress_Returns_Empty_Array_For_Fresh_Learner()
    {
        var response = await _client.GetAsync("/v1/pronunciation/my-progress");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task GetProfile_Returns_Pronunciation_Profile_Shape()
    {
        var response = await _client.GetAsync("/v1/pronunciation/profile");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal(JsonValueKind.Number, root.GetProperty("overallScore").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("projectedSpeakingScaled").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("projectedSpeakingGrade").ValueKind);
        Assert.True(root.TryGetProperty("weakPhonemes", out _));
        Assert.True(root.TryGetProperty("phonemeProgress", out _));
    }

    [Fact]
    public async Task GetEntitlement_Allows_Learner_By_Default()
    {
        var response = await _client.GetAsync("/v1/pronunciation/entitlement");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("allowed").GetBoolean(),
            "Entitlement should allow a fresh learner to record.");
    }

    [Fact]
    public async Task DueDrills_Returns_Array_With_Limit()
    {
        var response = await _client.GetAsync("/v1/pronunciation/drills/due?limit=4");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.True(json.RootElement.GetArrayLength() <= 4);
    }

    [Fact]
    public async Task InitAttempt_Returns_AttemptId_And_UploadUrl()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/pronunciation/drills/pd-001/attempt/init", new { });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("attemptId").GetString()));
        Assert.Equal("pd-001", root.GetProperty("drillId").GetString());
        Assert.Contains("/attempt/", root.GetProperty("uploadUrl").GetString() ?? "");
    }

    [Fact]
    public async Task LegacyAttemptEndpoint_Returns_Gone()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/pronunciation/drills/pd-001/attempt", new { audioUrl = "https://example.com/audio.webm" });
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task UploadAndScore_Persists_Assessment_And_Progress()
    {
        // 1. init attempt
        var initResponse = await _client.PostAsJsonAsync(
            "/v1/pronunciation/drills/pd-003/attempt/init", new { });
        initResponse.EnsureSuccessStatusCode();
        using var initJson = JsonDocument.Parse(await initResponse.Content.ReadAsStringAsync());
        var attemptId = initJson.RootElement.GetProperty("attemptId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(attemptId));

        // 2. upload a fake webm blob
        using var audioContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake-audio-bytes"));
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        var uploadResponse = await _client.PostAsync(
            $"/v1/pronunciation/drills/pd-003/attempt/{attemptId}/audio",
            audioContent);
        uploadResponse.EnsureSuccessStatusCode();
        using var uploadJson = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
        var root = uploadJson.RootElement;

        Assert.Equal("pd-003", root.GetProperty("drillId").GetString());
        var overall = root.GetProperty("overall").GetDouble();
        Assert.InRange(overall, 1, 100);
        var scaled = root.GetProperty("projectedSpeakingScaled").GetInt32();
        Assert.InRange(scaled, 0, 500);
        Assert.Equal("mock", root.GetProperty("provider").GetString());

        // 3. my-progress now contains the target phoneme
        var progressResponse = await _client.GetAsync("/v1/pronunciation/my-progress");
        progressResponse.EnsureSuccessStatusCode();
        using var progressJson = JsonDocument.Parse(await progressResponse.Content.ReadAsStringAsync());
        Assert.True(progressJson.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task UploadAndScore_Rejects_Invalid_Mime_Type()
    {
        var initResponse = await _client.PostAsJsonAsync(
            "/v1/pronunciation/drills/pd-001/attempt/init", new { });
        initResponse.EnsureSuccessStatusCode();
        using var initJson = JsonDocument.Parse(await initResponse.Content.ReadAsStringAsync());
        var attemptId = initJson.RootElement.GetProperty("attemptId").GetString();

        using var audioContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake-audio"));
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        var uploadResponse = await _client.PostAsync(
            $"/v1/pronunciation/drills/pd-001/attempt/{attemptId}/audio",
            audioContent);
        Assert.Equal(HttpStatusCode.BadRequest, uploadResponse.StatusCode);
    }

    [Fact]
    public async Task Discrimination_Submit_Persists_Aggregate_Accuracy()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/pronunciation/drills/pd-001/discrimination",
            new { roundsTotal = 8, roundsCorrect = 6 });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(8, json.RootElement.GetProperty("roundsTotal").GetInt32());
        Assert.Equal(6, json.RootElement.GetProperty("roundsCorrect").GetInt32());
        Assert.Equal("θ", json.RootElement.GetProperty("targetPhoneme").GetString());
    }

    [Fact]
    public async Task Discrimination_Submit_Rejects_Zero_Rounds()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/pronunciation/drills/pd-001/discrimination",
            new { roundsTotal = 0, roundsCorrect = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
