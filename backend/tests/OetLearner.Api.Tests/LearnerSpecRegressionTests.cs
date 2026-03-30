using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class LearnerSpecRegressionTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LearnerSpecRegressionTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SettingsAggregate_ExposesGoalsSection_ForInitialHydrate()
    {
        using var client = CreateClientForUser("settings-user");

        var response = await client.GetAsync("/v1/settings");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("goals", out var goals));
        Assert.True(goals.TryGetProperty("targetExamDate", out _));
        Assert.True(goals.TryGetProperty("studyHoursPerWeek", out _));
    }

    [Fact]
    public async Task SettingsSectionEndpoints_ExposeConcreteLearnerSections()
    {
        using var client = CreateClientForUser("settings-section-user");

        var profileResponse = await client.GetAsync("/v1/settings/profile");
        profileResponse.EnsureSuccessStatusCode();
        using var profileJson = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync());
        Assert.Equal("profile", profileJson.RootElement.GetProperty("section").GetString());
        Assert.True(profileJson.RootElement.TryGetProperty("values", out _));

        var studyResponse = await client.GetAsync("/v1/settings/study");
        studyResponse.EnsureSuccessStatusCode();
        using var studyJson = JsonDocument.Parse(await studyResponse.Content.ReadAsStringAsync());
        Assert.Equal("study", studyJson.RootElement.GetProperty("section").GetString());
        Assert.True(studyJson.RootElement.TryGetProperty("values", out _));
    }

    [Fact]
    public async Task MockAttemptCreation_PersistsReviewSelectionAndLaunchRoutes()
    {
        using var client = CreateClientForUser("mock-route-user");

        var response = await client.PostAsJsonAsync("/v1/mock-attempts", new
        {
            mockType = "full",
            subType = (string?)null,
            mode = "exam",
            profession = "Medicine",
            includeReview = true,
            strictTimer = true,
            reviewSelection = "writing_and_speaking"
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_and_speaking", json.RootElement.GetProperty("config").GetProperty("reviewSelection").GetString());
        var sectionStates = json.RootElement.GetProperty("sectionStates");
        Assert.Equal(4, sectionStates.GetArrayLength());
        Assert.All(sectionStates.EnumerateArray().ToArray(), section =>
        {
            Assert.True(section.TryGetProperty("launchRoute", out var launchRoute));
            Assert.Contains("/app/mocks/player/", launchRoute.GetString());
        });
    }

    [Fact]
    public async Task BillingPlansAndListeningDrills_ExposeLearnerContracts()
    {
        using var client = CreateClientForUser("billing-contract-user");

        var plansResponse = await client.GetAsync("/v1/billing/plans");
        plansResponse.EnsureSuccessStatusCode();
        using var plansJson = JsonDocument.Parse(await plansResponse.Content.ReadAsStringAsync());
        Assert.True(plansJson.RootElement.GetProperty("items").GetArrayLength() >= 2);

        var changePreviewResponse = await client.GetAsync("/v1/billing/change-preview?targetPlanId=premium-monthly");
        changePreviewResponse.EnsureSuccessStatusCode();
        using var changePreviewJson = JsonDocument.Parse(await changePreviewResponse.Content.ReadAsStringAsync());
        Assert.Equal("premium-monthly", changePreviewJson.RootElement.GetProperty("targetPlanId").GetString());

        var drillResponse = await client.GetAsync("/v1/listening/drills/listening-drill-distractor_confusion");
        drillResponse.EnsureSuccessStatusCode();
        using var drillJson = JsonDocument.Parse(await drillResponse.Content.ReadAsStringAsync());
        Assert.Equal("listening-drill-distractor_confusion", drillJson.RootElement.GetProperty("drillId").GetString());
        Assert.Contains("/app/listening/player/", drillJson.RootElement.GetProperty("launchRoute").GetString());
        Assert.Contains("/app/listening/review/", drillJson.RootElement.GetProperty("reviewRoute").GetString());
    }

    [Fact]
    public async Task LearnerOwnedAttempt_CannotBeFetchedByAnotherUser()
    {
        using var ownerClient = CreateClientForUser("attempt-owner");
        var attemptId = await CreateWritingAttemptAsync(ownerClient, "practice");

        using var otherClient = CreateClientForUser("attempt-intruder");
        var response = await otherClient.GetAsync($"/v1/writing/attempts/{attemptId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WritingDraftPatch_RejectsStaleDraftVersion_WithStructuredConflict()
    {
        using var client = CreateClientForUser("draft-version-user");
        var attemptId = await CreateWritingAttemptAsync(client, "practice");

        var firstPatch = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = "First version of the draft.",
            scratchpad = "Keep the letter concise.",
            checklist = new Dictionary<string, bool> { ["Addressed the purpose clearly"] = true },
            draftVersion = 1
        });
        firstPatch.EnsureSuccessStatusCode();

        var stalePatch = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = "Second version sent from an out-of-date browser tab.",
            scratchpad = "This should conflict.",
            checklist = new Dictionary<string, bool> { ["Addressed the purpose clearly"] = true },
            draftVersion = 1
        });

        Assert.Equal(HttpStatusCode.Conflict, stalePatch.StatusCode);

        using var json = JsonDocument.Parse(await stalePatch.Content.ReadAsStringAsync());
        Assert.Equal("draft_version_conflict", json.RootElement.GetProperty("code").GetString());
        Assert.Contains(
            json.RootElement.GetProperty("fieldErrors").EnumerateArray(),
            item => item.GetProperty("field").GetString() == "draftVersion");
    }

    [Fact]
    public async Task DiagnosticFlow_CompletesAcrossAllSubtests_AndProducesScopedResults()
    {
        using var client = CreateClientForUser("diagnostic-user");

        var createDiagnosticResponse = await client.PostAsync("/v1/diagnostic/attempts", content: null);
        createDiagnosticResponse.EnsureSuccessStatusCode();

        using var diagnosticJson = JsonDocument.Parse(await createDiagnosticResponse.Content.ReadAsStringAsync());
        var diagnosticId = diagnosticJson.RootElement.GetProperty("diagnosticId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(diagnosticId));

        var writingAttemptId = await CreateWritingAttemptAsync(client, "diagnostic");
        var writingSubmit = await client.PostAsJsonAsync($"/v1/writing/attempts/{writingAttemptId}/submit", new
        {
            content = "Dear Dr Patterson, I am writing to update you regarding Mrs Vance after her knee replacement. She recovered well and requires staple removal in 14 days.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        writingSubmit.EnsureSuccessStatusCode();
        using var writingSubmitJson = JsonDocument.Parse(await writingSubmit.Content.ReadAsStringAsync());
        var writingEvaluationId = writingSubmitJson.RootElement.GetProperty("evaluationId").GetString();
        await WaitForAsync(
            async () =>
            {
                var response = await client.GetAsync($"/v1/writing/evaluations/{writingEvaluationId}/summary");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("state").GetString() == "completed";
            },
            "writing evaluation to complete");

        var speakingAttemptId = await CreateSpeakingAttemptAsync(client, "diagnostic");
        var uploadSession = await client.PostAsync($"/v1/speaking/attempts/{speakingAttemptId}/audio/upload-session", content: null);
        uploadSession.EnsureSuccessStatusCode();
        using var uploadSessionJson = JsonDocument.Parse(await uploadSession.Content.ReadAsStringAsync());
        var uploadSessionId = uploadSessionJson.RootElement.GetProperty("uploadSessionId").GetString();
        var uploadUrl = uploadSessionJson.RootElement.GetProperty("uploadUrl").GetString();
        var storageKey = uploadSessionJson.RootElement.GetProperty("storageKey").GetString();

        var uploadBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(uploadBytes)
        };
        uploadRequest.Headers.Add("X-Debug-UserId", "diagnostic-user");
        uploadRequest.Headers.Add("X-Debug-Email", "diagnostic-user@example.test");
        uploadRequest.Headers.Add("X-Debug-Name", "diagnostic-user");
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        var binaryUpload = await client.SendAsync(uploadRequest);
        binaryUpload.EnsureSuccessStatusCode();

        var uploadComplete = await client.PostAsJsonAsync($"/v1/speaking/attempts/{speakingAttemptId}/audio/complete", new
        {
            uploadSessionId,
            storageKey,
            fileName = "diagnostic-speaking.webm",
            sizeBytes = uploadBytes.Length,
            durationSeconds = 75,
            captureMethod = "browser-recording",
            contentType = "audio/webm"
        });
        uploadComplete.EnsureSuccessStatusCode();

        var speakingSubmit = await client.PostAsync($"/v1/speaking/attempts/{speakingAttemptId}/submit", content: null);
        speakingSubmit.EnsureSuccessStatusCode();
        using var speakingSubmitJson = JsonDocument.Parse(await speakingSubmit.Content.ReadAsStringAsync());
        var speakingEvaluationId = speakingSubmitJson.RootElement.GetProperty("evaluationId").GetString();
        await WaitForAsync(
            async () =>
            {
                var response = await client.GetAsync($"/v1/speaking/evaluations/{speakingEvaluationId}/summary");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("state").GetString() == "completed";
            },
            "speaking evaluation to complete");

        var readingAttemptId = await CreateObjectiveAttemptAsync(client, "reading", "rt-001", "diagnostic");
        var readingSubmit = await client.PostAsync($"/v1/reading/attempts/{readingAttemptId}/submit", content: null);
        readingSubmit.EnsureSuccessStatusCode();

        var listeningAttemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "diagnostic");
        var listeningSubmit = await client.PostAsync($"/v1/listening/attempts/{listeningAttemptId}/submit", content: null);
        listeningSubmit.EnsureSuccessStatusCode();

        await WaitForAsync(
            async () =>
            {
                var response = await client.GetAsync($"/v1/diagnostic/attempts/{diagnosticId}/hub");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("completedCount").GetInt32() == 4
                    && json.RootElement.GetProperty("state").GetString() == "completed";
            },
            "diagnostic hub to reach the completed state");

        var resultsResponse = await client.GetAsync($"/v1/diagnostic/attempts/{diagnosticId}/results");
        resultsResponse.EnsureSuccessStatusCode();
        using var resultsJson = JsonDocument.Parse(await resultsResponse.Content.ReadAsStringAsync());

        Assert.Equal(4, resultsJson.RootElement.GetProperty("results").GetArrayLength());
        Assert.Equal("Writing", resultsJson.RootElement.GetProperty("results")[0].GetProperty("subTest").GetString());
    }

    [Fact]
    public async Task CompletedEvaluation_RegeneratesTheStudyPlan()
    {
        using var client = CreateClientForUser("study-plan-user");

        var initialPlanResponse = await client.GetAsync("/v1/study-plan");
        initialPlanResponse.EnsureSuccessStatusCode();
        using var initialPlanJson = JsonDocument.Parse(await initialPlanResponse.Content.ReadAsStringAsync());
        var initialVersion = initialPlanJson.RootElement.GetProperty("version").GetInt32();
        var initialGeneratedAt = initialPlanJson.RootElement.GetProperty("generatedAt").GetDateTimeOffset();

        var attemptId = await CreateWritingAttemptAsync(client, "practice");
        var submitResponse = await client.PostAsJsonAsync($"/v1/writing/attempts/{attemptId}/submit", new
        {
            content = "Dear Dr Patterson, I am writing to update you regarding Mrs Vance after her knee replacement. She recovered well and requires staple removal in 14 days.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        submitResponse.EnsureSuccessStatusCode();
        using var submitJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        var evaluationId = submitJson.RootElement.GetProperty("evaluationId").GetString();

        await WaitForAsync(
            async () =>
            {
                var response = await client.GetAsync($"/v1/writing/evaluations/{evaluationId}/summary");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("state").GetString() == "completed";
            },
            "writing evaluation to complete");

        await WaitForAsync(
            async () =>
            {
                var response = await client.GetAsync("/v1/study-plan");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var version = json.RootElement.GetProperty("version").GetInt32();
                var generatedAt = json.RootElement.GetProperty("generatedAt").GetDateTimeOffset();
                return version > initialVersion || generatedAt > initialGeneratedAt;
            },
            "study plan regeneration to complete");
    }

    private HttpClient CreateClientForUser(string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private static async Task<string> CreateWritingAttemptAsync(HttpClient client, string context)
    {
        var response = await client.PostAsJsonAsync("/v1/writing/attempts", new
        {
            contentId = "wt-001",
            context,
            mode = "timed",
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("attemptId").GetString()
            ?? throw new InvalidOperationException("Writing attempt id was missing.");
    }

    private static async Task<string> CreateSpeakingAttemptAsync(HttpClient client, string context)
    {
        var response = await client.PostAsJsonAsync("/v1/speaking/attempts", new
        {
            contentId = "st-001",
            context,
            mode = "exam",
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("attemptId").GetString()
            ?? throw new InvalidOperationException("Speaking attempt id was missing.");
    }

    private static async Task<string> CreateObjectiveAttemptAsync(HttpClient client, string subtest, string contentId, string context)
    {
        var response = await client.PostAsJsonAsync($"/v1/{subtest}/attempts", new
        {
            contentId,
            context,
            mode = "exam",
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var attemptId = json.RootElement.GetProperty("attemptId").GetString()
            ?? throw new InvalidOperationException($"{subtest} attempt id was missing.");

        var answersResponse = await client.PatchAsJsonAsync($"/v1/{subtest}/attempts/{attemptId}/answers", new
        {
            answers = new Dictionary<string, string?>
            {
                ["q-1"] = "answer-1",
                ["q-2"] = "answer-2"
            }
        });
        answersResponse.EnsureSuccessStatusCode();

        return attemptId;
    }

    private static async Task WaitForAsync(Func<Task<bool>> predicate, string reason)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Timed out waiting for {reason}.");
    }
}
