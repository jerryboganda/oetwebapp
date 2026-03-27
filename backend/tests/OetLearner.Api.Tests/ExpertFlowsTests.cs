using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class ExpertFlowsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ExpertFlowsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExpertMe_ReturnsExpertProfile()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/me");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("expert-001", json.RootElement.GetProperty("userId").GetString());
        Assert.Equal("expert", json.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task ExpertQueue_ReturnsSeededReviewRequests()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/queue");
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);

        using var json = JsonDocument.Parse(payload);
        var items = json.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        Assert.True(json.RootElement.GetProperty("totalCount").GetInt32() >= 1);
        Assert.True(items[0].GetProperty("availableActions").GetProperty("canClaim").ValueKind is JsonValueKind.True or JsonValueKind.False);
    }

    [Fact]
    public async Task ExpertCanClaimAndReleaseReview()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var claimResponse = await client.PostAsync("/v1/expert/queue/review-queue-001/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var releaseResponse = await client.PostAsync("/v1/expert/queue/review-queue-001/release", content: null);
        releaseResponse.EnsureSuccessStatusCode();

        var queueResponse = await client.GetAsync("/v1/expert/queue");
        var queuePayload = await queueResponse.Content.ReadAsStringAsync();
        Assert.True(queueResponse.IsSuccessStatusCode, queuePayload);
        using var queueJson = JsonDocument.Parse(queuePayload);
        var queueItems = queueJson.RootElement.GetProperty("items");
        Assert.Contains(queueItems.EnumerateArray(), item => item.GetProperty("id").GetString() == "review-queue-001");
    }

    [Fact]
    public async Task ExpertCalibrationCases_ReturnsSeededCases()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/calibration/cases");
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
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/calibration/notes");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ExpertSchedule_GetAndSave()
    {
        using var client = CreateExpertClient(_factory);

        var getResponse = await client.GetAsync("/v1/expert/schedule");
        getResponse.EnsureSuccessStatusCode();

        using var getJson = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(getJson.RootElement.GetProperty("timezone").GetString()));

        var saveResponse = await client.PutAsJsonAsync("/v1/expert/schedule", new
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
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/metrics");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metrics = json.RootElement.GetProperty("metrics");
        Assert.True(metrics.GetProperty("totalReviewsCompleted").GetInt32() >= 0);
        Assert.True(metrics.GetProperty("draftReviews").GetInt32() >= 0);
        Assert.True(json.RootElement.GetProperty("completionData").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ExpertLearnerProfile_ReturnsProfile()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/learners/mock-user-001");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mock-user-001", json.RootElement.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("name").GetString()));
        Assert.True(json.RootElement.GetProperty("attemptsCount").GetInt32() >= 0);
        Assert.Equal("review_context_only", json.RootElement.GetProperty("visibilityScope").GetString());
    }

    [Fact]
    public async Task ExpertWritingReviewBundle_RequiresClaimAndReturnsDetail()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var forbiddenResponse = await client.GetAsync("/v1/expert/reviews/review-queue-002/writing");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        var claimResponse = await client.PostAsync("/v1/expert/queue/review-queue-002/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/v1/expert/reviews/review-queue-002/writing");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("review-queue-002", json.RootElement.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("learnerResponse").GetString()));
        Assert.True(json.RootElement.GetProperty("permissions").GetProperty("canSubmit").GetBoolean());
    }

    [Fact]
    public async Task ExpertSpeakingAudio_RequiresClaim()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/reviews/review-queue-001/speaking/audio");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExpertDraftSaveAndSubmit_WritingFlow()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var claimResponse = await client.PostAsync("/v1/expert/queue/review-queue-002/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var draftResponse = await client.PutAsJsonAsync("/v1/expert/reviews/review-queue-002/draft", new
        {
            scores = new Dictionary<string, int> { ["purpose"] = 5, ["content"] = 5, ["conciseness"] = 4, ["genre"] = 4, ["organization"] = 5, ["language"] = 4 },
            criterionComments = new Dictionary<string, string> { ["purpose"] = "Excellent clarity." },
            finalComment = "Strong submission overall.",
            anchoredComments = new[] { new { text = "Tighten this point.", startOffset = 5, endOffset = 18 } }
        });
        draftResponse.EnsureSuccessStatusCode();

        using var draftJson = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        var version = draftJson.RootElement.GetProperty("version").GetInt32();
        Assert.False(string.IsNullOrWhiteSpace(draftJson.RootElement.GetProperty("savedAt").GetString()));

        var submitResponse = await client.PostAsJsonAsync("/v1/expert/reviews/review-queue-002/writing/submit", new
        {
            scores = new Dictionary<string, int> { ["purpose"] = 5, ["content"] = 5, ["conciseness"] = 4, ["genre"] = 4, ["organization"] = 5, ["language"] = 4 },
            criterionComments = new Dictionary<string, string> { ["purpose"] = "Excellent clarity." },
            finalComment = "Strong submission overall.",
            version
        });
        submitResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ExpertSubmitRejectsIncompleteRubric()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var claimResponse = await client.PostAsync("/v1/expert/queue/review-queue-002/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var submitResponse = await client.PostAsJsonAsync("/v1/expert/reviews/review-queue-002/writing/submit", new
        {
            scores = new Dictionary<string, int> { ["purpose"] = 5 },
            criterionComments = new Dictionary<string, string>(),
            finalComment = "Too short to submit."
        });

        Assert.Equal(HttpStatusCode.BadRequest, submitResponse.StatusCode);
    }

    [Fact]
    public async Task ExpertCannotSaveDraftWithoutAssignment()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.PutAsJsonAsync("/v1/expert/reviews/review-queue-002/draft", new
        {
            scores = new Dictionary<string, int> { ["purpose"] = 4 },
            criterionComments = new Dictionary<string, string>(),
            finalComment = string.Empty
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LearnerCannotAccessExpertEndpoints()
    {
        using var learnerClient = new TestWebApplicationFactory().CreateClient();
        var response = await learnerClient.GetAsync("/v1/expert/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnrelatedExpertCannotViewLearnerProfile()
    {
        using var client = CreateExpertClient(_factory, "expert-unauthorised");
        var response = await client.GetAsync("/v1/expert/learners/mock-user-001");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpClient CreateExpertClient(TestWebApplicationFactory factory, string expertId = "expert-001")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", expertId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "expert");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{expertId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Expert Reviewer");
        return client;
    }
}
