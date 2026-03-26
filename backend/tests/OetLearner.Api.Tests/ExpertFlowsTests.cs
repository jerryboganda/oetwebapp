using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class ExpertFlowsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExpertFlowsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Debug-UserId", "expert-001");
        _client.DefaultRequestHeaders.Add("X-Debug-Role", "expert");
    }

    [Fact]
    public async Task ExpertMe_ReturnsExpertProfile()
    {
        var response = await _client.GetAsync("/v1/expert/me");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("expert-001", json.RootElement.GetProperty("userId").GetString());
        Assert.Equal("expert", json.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task ExpertQueue_ReturnsSeededReviewRequests()
    {
        var response = await _client.GetAsync("/v1/expert/queue");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ExpertCalibrationCases_ReturnsSeededCases()
    {
        var response = await _client.GetAsync("/v1/expert/calibration/cases");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() >= 2);

        var first = json.RootElement[0];
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task ExpertCalibrationNotes_ReturnsSeededNotes()
    {
        var response = await _client.GetAsync("/v1/expert/calibration/notes");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ExpertSchedule_GetAndSave()
    {
        var getResponse = await _client.GetAsync("/v1/expert/schedule");
        getResponse.EnsureSuccessStatusCode();

        using var getJson = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(getJson.RootElement.GetProperty("timezone").GetString()));

        var saveResponse = await _client.PutAsJsonAsync("/v1/expert/schedule", new
        {
            timezone = "Europe/London",
            days = new Dictionary<string, object>
            {
                ["monday"] = new { active = true, start = "08:00", end = "16:00" },
                ["tuesday"] = new { active = true, start = "08:00", end = "16:00" },
                ["wednesday"] = new { active = false, start = "09:00", end = "12:00" },
                ["thursday"] = new { active = true, start = "08:00", end = "16:00" },
                ["friday"] = new { active = true, start = "08:00", end = "15:00" },
                ["saturday"] = new { active = false, start = "09:00", end = "12:00" },
                ["sunday"] = new { active = false, start = "09:00", end = "12:00" }
            }
        });
        saveResponse.EnsureSuccessStatusCode();

        using var saveJson = JsonDocument.Parse(await saveResponse.Content.ReadAsStringAsync());
        Assert.Equal("Europe/London", saveJson.RootElement.GetProperty("timezone").GetString());
    }

    [Fact]
    public async Task ExpertMetrics_ReturnsMetricsObject()
    {
        var response = await _client.GetAsync("/v1/expert/metrics");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metrics = json.RootElement.GetProperty("metrics");
        Assert.True(metrics.GetProperty("totalReviewsCompleted").GetInt32() >= 0);
        Assert.True(json.RootElement.GetProperty("completionData").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ExpertLearnerProfile_ReturnsProfile()
    {
        var response = await _client.GetAsync("/v1/expert/learners/mock-user-001");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mock-user-001", json.RootElement.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("name").GetString()));
        Assert.True(json.RootElement.GetProperty("attemptsCount").GetInt32() >= 0);
    }

    [Fact]
    public async Task ExpertWritingReviewBundle_ReturnsDetail()
    {
        var response = await _client.GetAsync("/v1/expert/reviews/review-001/writing");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("review-001", json.RootElement.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("learnerResponse").GetString()));
    }

    [Fact]
    public async Task ExpertDraftSaveAndSubmit_WritingFlow()
    {
        // Save draft
        var draftResponse = await _client.PutAsJsonAsync("/v1/expert/reviews/review-001/draft", new
        {
            scores = new Dictionary<string, int> { ["purpose"] = 5, ["content"] = 5, ["conciseness"] = 4, ["genre"] = 4, ["organization"] = 5, ["language"] = 4 },
            criterionComments = new Dictionary<string, string> { ["purpose"] = "Excellent clarity." },
            finalComment = "Strong submission overall.",
            version = 1
        });
        draftResponse.EnsureSuccessStatusCode();

        using var draftJson = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(draftJson.RootElement.GetProperty("savedAt").GetString()));
    }

    [Fact]
    public async Task LearnerCannotAccessExpertEndpoints()
    {
        var learnerClient = new TestWebApplicationFactory().CreateClient();
        // Default dev auth sends role=learner
        var response = await learnerClient.GetAsync("/v1/expert/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
