using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class LearnerSurfaceContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LearnerSurfaceContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureLearnerProfileAsync("mock-user-001", "mock-user-001@example.test", "mock-user-001").GetAwaiter().GetResult();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task WritingHome_ExposesRecommendationDrillsHistoryCreditsAndMockEntry()
    {
        var response = await _client.GetAsync("/v1/writing/home");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("recommendedTask", out _));
        Assert.True(json.RootElement.TryGetProperty("practiceLibrary", out _));
        Assert.True(json.RootElement.TryGetProperty("criterionDrillLibrary", out _));
        Assert.True(json.RootElement.TryGetProperty("pastSubmissions", out _));
        Assert.True(json.RootElement.TryGetProperty("reviewCredits", out _));
        Assert.True(json.RootElement.TryGetProperty("fullMockEntry", out _));
    }

    [Fact]
    public async Task SpeakingHome_ExposesRecommendationIssuesDrillsAttemptsAndCredits()
    {
        var response = await _client.GetAsync("/v1/speaking/home");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("recommendedRolePlay", out _));
        Assert.True(json.RootElement.TryGetProperty("commonIssuesToImprove", out var issues));
        Assert.NotEqual(0, issues.GetArrayLength());
        Assert.True(json.RootElement.TryGetProperty("drillGroups", out _));
        Assert.True(json.RootElement.TryGetProperty("pastAttempts", out _));
        Assert.True(json.RootElement.TryGetProperty("reviewCredits", out _));
    }

    [Fact]
    public async Task ReadingAndListeningHomes_ExposeGroupedCollections()
    {
        var readingResponse = await _client.GetAsync("/v1/reading/home");
        readingResponse.EnsureSuccessStatusCode();
        using var readingJson = JsonDocument.Parse(await readingResponse.Content.ReadAsStringAsync());
        Assert.True(readingJson.RootElement.TryGetProperty("papers", out _));
        Assert.True(readingJson.RootElement.TryGetProperty("activeAttempts", out _));
        Assert.True(readingJson.RootElement.TryGetProperty("recentResults", out _));
        Assert.True(readingJson.RootElement.TryGetProperty("policy", out _));

        var listeningResponse = await _client.GetAsync("/v1/listening/home");
        listeningResponse.EnsureSuccessStatusCode();
        using var listeningJson = JsonDocument.Parse(await listeningResponse.Content.ReadAsStringAsync());
        Assert.True(listeningJson.RootElement.TryGetProperty("papers", out _));
        Assert.True(listeningJson.RootElement.TryGetProperty("activeAttempts", out _));
        Assert.True(listeningJson.RootElement.TryGetProperty("recentResults", out _));
        Assert.True(listeningJson.RootElement.TryGetProperty("emptyStates", out _));
        Assert.True(listeningJson.RootElement.TryGetProperty("partCollections", out _));
        Assert.True(listeningJson.RootElement.TryGetProperty("transcriptBackedReview", out _));
        Assert.True(listeningJson.RootElement.TryGetProperty("distractorDrills", out _));
        Assert.True(listeningJson.RootElement.TryGetProperty("accessPolicyHints", out _));
    }

    [Fact]
    public async Task ListeningPaperSession_HidesAnswerKeyBeforeSubmit()
    {
        var response = await _client.GetAsync("/v1/listening-papers/papers/lt-001/session?mode=practice");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);

        Assert.True(json.RootElement.TryGetProperty("questions", out var questions));
        Assert.NotEqual(0, questions.GetArrayLength());
        Assert.DoesNotContain("correctAnswer", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptedAnswers", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcriptExcerpt", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListeningPaperAttempt_SubmitsCanonicalScoreAndPolicySafeReview()
    {
        var start = await _client.PostAsJsonAsync("/v1/listening-papers/papers/lt-001/attempts", new { mode = "practice" });
        start.EnsureSuccessStatusCode();
        using var startJson = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        var attemptId = startJson.RootElement.GetProperty("attemptId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(attemptId));

        await SaveListeningAnswerAsync(attemptId!, "lq-1", "Increasing breathlessness at night");
        await SaveListeningAnswerAsync(attemptId!, "lq-2", "3-4 times per week");
        await SaveListeningAnswerAsync(attemptId!, "lq-3", "Combination inhaler");

        var submit = await _client.PostAsync($"/v1/listening-papers/attempts/{attemptId}/submit", null);
        submit.EnsureSuccessStatusCode();
        using var review = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());

        Assert.Equal(3, review.RootElement.GetProperty("rawScore").GetInt32());
        Assert.Equal(42, review.RootElement.GetProperty("maxRawScore").GetInt32());
        Assert.Equal(OetScoring.OetRawToScaled(3), review.RootElement.GetProperty("scaledScore").GetInt32());
        Assert.False(review.RootElement.GetProperty("passed").GetBoolean());
        Assert.True(review.RootElement.TryGetProperty("itemReview", out var items));
        Assert.Equal(3, items.GetArrayLength());
        Assert.True(review.RootElement.TryGetProperty("transcriptAccess", out var access));
        Assert.Equal("partial", access.GetProperty("state").GetString());
    }

    [Fact]
    public async Task ObjectiveEvaluations_ExposeItemReviewClustersAndTranscriptAccess()
    {
        var readingResponse = await _client.GetAsync("/v1/reading/evaluations/re-001");
        readingResponse.EnsureSuccessStatusCode();
        using var readingJson = JsonDocument.Parse(await readingResponse.Content.ReadAsStringAsync());
        Assert.True(readingJson.RootElement.TryGetProperty("itemReview", out var readingReview));
        Assert.NotEqual(0, readingReview.GetArrayLength());
        Assert.True(readingJson.RootElement.TryGetProperty("errorClusters", out _));
        Assert.True(readingJson.RootElement.TryGetProperty("recommendedNextDrill", out _));

        var listeningResponse = await _client.GetAsync("/v1/listening/evaluations/le-001");
        listeningResponse.EnsureSuccessStatusCode();
        using var listeningJson = JsonDocument.Parse(await listeningResponse.Content.ReadAsStringAsync());
        Assert.True(listeningJson.RootElement.TryGetProperty("itemReview", out var listeningReview));
        Assert.NotEqual(0, listeningReview.GetArrayLength());
        Assert.True(listeningJson.RootElement.TryGetProperty("transcriptAccess", out _));
        Assert.True(listeningJson.RootElement.TryGetProperty("recommendedNextDrill", out _));
    }

    [Fact]
    public async Task MockCenter_ExposesEntitlementsReportsAndRecommendation()
    {
        var response = await _client.GetAsync("/v1/mocks");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("collections", out _));
        Assert.True(json.RootElement.TryGetProperty("purchasedMockReviews", out _));
        Assert.True(json.RootElement.TryGetProperty("recommendedNextMock", out _));
    }

    [Fact]
    public async Task ProgressAndSubmissionHistory_ExposeAnalyticsAndActions()
    {
        var progressResponse = await _client.GetAsync("/v1/progress");
        progressResponse.EnsureSuccessStatusCode();
        using var progressJson = JsonDocument.Parse(await progressResponse.Content.ReadAsStringAsync());
        Assert.True(progressJson.RootElement.TryGetProperty("subtestTrend", out _));
        Assert.True(progressJson.RootElement.TryGetProperty("criterionTrend", out _));
        Assert.True(progressJson.RootElement.TryGetProperty("reviewUsage", out _));

        var submissionsResponse = await _client.GetAsync("/v1/submissions");
        submissionsResponse.EnsureSuccessStatusCode();
        using var submissionsJson = JsonDocument.Parse(await submissionsResponse.Content.ReadAsStringAsync());
        var firstItem = submissionsJson.RootElement.GetProperty("items")[0];
        Assert.True(firstItem.TryGetProperty("actions", out _));
        Assert.True(firstItem.TryGetProperty("canRequestReview", out _));
    }

    [Fact]
    public async Task SignupCatalog_DoesNotExposeBillingOrSessionChoicesForRegistration()
    {
        var response = await _client.GetAsync("/v1/auth/catalog/signup");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("examTypes", out _));
        Assert.True(json.RootElement.TryGetProperty("professions", out _));
        Assert.False(json.RootElement.TryGetProperty("sessions", out _));
        Assert.False(json.RootElement.TryGetProperty("billingPlans", out _));
    }

    [Fact]
    public async Task NewLearner_MockAndBillingSurfacesHydrateWithoutExistingData()
    {
        using var client = await CreateClientForUserAsync("surface-new-user");

        var mocksResponse = await client.GetAsync("/v1/mocks");
        mocksResponse.EnsureSuccessStatusCode();
        using var mocksJson = JsonDocument.Parse(await mocksResponse.Content.ReadAsStringAsync());
        Assert.True(mocksJson.RootElement.TryGetProperty("recommendedNextMock", out _));

        var billingResponse = await client.GetAsync("/v1/billing/summary");
        billingResponse.EnsureSuccessStatusCode();
        using var billingJson = JsonDocument.Parse(await billingResponse.Content.ReadAsStringAsync());
        Assert.True(billingJson.RootElement.TryGetProperty("wallet", out var wallet));
        Assert.Equal(0, wallet.GetProperty("creditBalance").GetInt32());
    }

    private async Task<HttpClient> CreateClientForUserAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private async Task SaveListeningAnswerAsync(string attemptId, string questionId, string answer)
    {
        var response = await _client.PutAsJsonAsync(
            $"/v1/listening-papers/attempts/{attemptId}/answers/{questionId}",
            new { userAnswer = answer });
        response.EnsureSuccessStatusCode();
    }
}
