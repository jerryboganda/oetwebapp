using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
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
        using var client = await CreateClientForUserAsync("settings-user");

        var response = await client.GetAsync("/v1/settings");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("goals", out var goals));
        Assert.True(goals.TryGetProperty("targetExamDate", out _));
        Assert.True(goals.TryGetProperty("studyHoursPerWeek", out _));
        Assert.Equal("Australia", goals.GetProperty("targetCountry").GetString());
        Assert.True(json.RootElement.TryGetProperty("study", out var study));
        Assert.Equal(goals.GetProperty("targetCountry").GetString(), study.GetProperty("targetCountry").GetString());
    }

    [Fact]
    public async Task SettingsSectionEndpoints_ExposeConcreteLearnerSections()
    {
        using var client = await CreateClientForUserAsync("settings-section-user");

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
    public async Task GoalsPatch_RejectsTargetCountryOutsidePrdList()
    {
        using var client = await CreateClientForUserAsync("goals-country-validation-user");

        var response = await client.PatchAsJsonAsync("/v1/learner/goals/", new
        {
            targetCountry = "Atlantis"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("country_target_invalid", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SettingsStudyPatch_RejectsTargetCountryOutsidePrdList()
    {
        using var client = await CreateClientForUserAsync("settings-country-validation-user");

        var response = await client.PatchAsJsonAsync("/v1/settings/study", new
        {
            values = new Dictionary<string, object?>
            {
                ["targetCountry"] = "Atlantis"
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("country_target_invalid", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SettingsStudyPatch_CanonicalizesTargetCountryFromPrdList()
    {
        using var client = await CreateClientForUserAsync("settings-country-canonical-user");

        var response = await client.PatchAsJsonAsync("/v1/settings/study", new
        {
            values = new Dictionary<string, object?>
            {
                ["targetCountry"] = "usa"
            }
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("USA", json.RootElement.GetProperty("values").GetProperty("targetCountry").GetString());
    }

    [Fact]
    public async Task MockAttemptCreation_PersistsReviewSelectionAndLaunchRoutes()
    {
        const string userId = "mock-route-user";
        using var client = await CreateClientForUserAsync(userId);
        var bundleId = await SeedPublishedFullMockBundleAsync(userId, walletCredits: 2);

        var response = await client.PostAsJsonAsync("/v1/mock-attempts", new
        {
            bundleId,
            mockType = "full",
            subType = (string?)null,
            mode = "exam",
            profession = "Medicine",
            includeReview = true,
            strictTimer = true,
            reviewSelection = "writing_and_speaking"
        });
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, responseBody);

        using var json = JsonDocument.Parse(responseBody);
        Assert.Equal("writing_and_speaking", json.RootElement.GetProperty("config").GetProperty("reviewSelection").GetString());
        Assert.Equal(bundleId, json.RootElement.GetProperty("config").GetProperty("bundleId").GetString());
        Assert.True(json.RootElement.TryGetProperty("reviewReservation", out var reservation));
        Assert.Equal(2, reservation.GetProperty("reservedCredits").GetInt32());
        Assert.Equal(2, reservation.GetProperty("pendingCredits").GetInt32());

        var sectionStates = json.RootElement.GetProperty("sectionStates");
        Assert.Equal(4, sectionStates.GetArrayLength());
        var sections = sectionStates.EnumerateArray().ToArray();
        Assert.Equal(new[] { "listening", "reading", "writing", "speaking" }, sections.Select(section => section.GetProperty("subtest").GetString()).ToArray());
        Assert.Contains("/listening/player/mock-listening-regression", sections[0].GetProperty("launchRoute").GetString());
        Assert.Contains("/reading/paper/mock-reading-regression", sections[1].GetProperty("launchRoute").GetString());
        Assert.Contains("/writing/player?taskId=mock-writing-regression", sections[2].GetProperty("launchRoute").GetString());
        Assert.Contains("/speaking/task/mock-speaking-regression", sections[3].GetProperty("launchRoute").GetString());
        Assert.All(sections, section =>
        {
            var launchRoute = section.GetProperty("launchRoute").GetString();
            Assert.DoesNotContain("/mocks/player/", launchRoute);
            Assert.Contains("mockAttemptId=", launchRoute);
            Assert.Contains("mockSectionId=", launchRoute);
        });
    }

    [Fact]
    public async Task BillingPlansAndListeningDrills_ExposeLearnerContracts()
    {
        using var client = await CreateClientForUserAsync("billing-contract-user");

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
        Assert.Contains("/listening/player/", drillJson.RootElement.GetProperty("launchRoute").GetString());
        Assert.Contains("/listening/review/", drillJson.RootElement.GetProperty("reviewRoute").GetString());
    }

    [Fact]
    public async Task LearnerOwnedAttempt_CannotBeFetchedByAnotherUser()
    {
        using var ownerClient = await CreateClientForUserAsync("attempt-owner");
        var attemptId = await CreateWritingAttemptAsync(ownerClient, "practice");

        using var otherClient = await CreateClientForUserAsync("attempt-intruder");
        var response = await otherClient.GetAsync($"/v1/writing/attempts/{attemptId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WritingDraftPatch_RejectsStaleDraftVersion_WithStructuredConflict()
    {
        using var client = await CreateClientForUserAsync("draft-version-user");
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
    public async Task WritingExamDraftPatch_RejectsContentDuringReadingWindow()
    {
        using var client = await CreateClientForUserAsync("writing-reading-window-draft");
        var attemptId = await CreateWritingAttemptAsync(client, "practice", mode: "exam");

        var response = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = "Dear Dr Smith, I am writing during the reading-only window.",
            scratchpad = (string?)null,
            checklist = (Dictionary<string, bool>?)null,
            draftVersion = 1
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_reading_window_active", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WritingExamDraftPatch_RejectsScratchpadDuringReadingWindow()
    {
        using var client = await CreateClientForUserAsync("writing-reading-window-scratchpad");
        var attemptId = await CreateWritingAttemptAsync(client, "practice", mode: "exam");

        var response = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = (string?)null,
            scratchpad = "Plan a sentence during the reading-only window.",
            checklist = (Dictionary<string, bool>?)null,
            draftVersion = 1
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_reading_window_active", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WritingExamDraftPatch_RejectsChecklistDuringReadingWindow()
    {
        using var client = await CreateClientForUserAsync("writing-reading-window-checklist");
        var attemptId = await CreateWritingAttemptAsync(client, "practice", mode: "exam");

        var response = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = (string?)null,
            scratchpad = (string?)null,
            checklist = new Dictionary<string, bool> { ["Started planning before writing time"] = true },
            draftVersion = 1
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_reading_window_active", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WritingExamSubmit_RejectsContentDuringReadingWindow()
    {
        using var client = await CreateClientForUserAsync("writing-reading-window-submit");
        var attemptId = await CreateWritingAttemptAsync(client, "practice", mode: "exam");

        var response = await client.PostAsJsonAsync($"/v1/writing/attempts/{attemptId}/submit", new
        {
            content = "Dear Dr Smith, I am writing during the reading-only window.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_reading_window_active", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WritingExamAttemptCreation_ReusesInProgressAttemptForSameTaskAndContext()
    {
        using var client = await CreateClientForUserAsync("writing-exam-attempt-reuse");

        var firstAttemptId = await CreateWritingAttemptAsync(client, "exam", mode: "exam");
        var secondAttemptId = await CreateWritingAttemptAsync(client, "exam", mode: "exam");

        Assert.Equal(firstAttemptId, secondAttemptId);
    }

    [Fact]
    public async Task WritingExamDraftPatch_AllowsContentAfterReadingWindow()
    {
        using var client = await CreateClientForUserAsync("writing-reading-window-draft-allowed");
        var attemptId = await CreateWritingAttemptAsync(client, "practice", mode: "exam");
        await MoveAttemptStartBackAsync(attemptId, TimeSpan.FromMinutes(6));

        var response = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = "Dear Dr Smith, I am writing after the reading-only window.",
            scratchpad = (string?)null,
            checklist = (Dictionary<string, bool>?)null,
            draftVersion = 1
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("saved").GetBoolean());
    }

    [Fact]
    public async Task WritingExamSubmit_AllowsContentAfterReadingWindow()
    {
        using var client = await CreateClientForUserAsync("writing-reading-window-submit-allowed");
        var attemptId = await CreateWritingAttemptAsync(client, "practice", mode: "exam");
        await MoveAttemptStartBackAsync(attemptId, TimeSpan.FromMinutes(6));

        var response = await client.PostAsJsonAsync($"/v1/writing/attempts/{attemptId}/submit", new
        {
            content = "Dear Dr Smith, I am writing after the reading-only window and submitting this response for evaluation.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(attemptId, json.RootElement.GetProperty("attemptId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("evaluationId").GetString()));
    }

    [Theory]
    [InlineData(AttemptState.Submitted)]
    [InlineData(AttemptState.Evaluating)]
    [InlineData(AttemptState.Completed)]
    [InlineData(AttemptState.Failed)]
    [InlineData(AttemptState.Abandoned)]
    public async Task WritingDraftPatch_RejectsAlreadySubmittedAttemptStates(AttemptState state)
    {
        using var client = await CreateClientForUserAsync($"writing-locked-{state.ToString().ToLowerInvariant()}");
        var attemptId = await CreateWritingAttemptAsync(client, "practice");
        await SetAttemptStateAsync(attemptId, state);

        var response = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = "This stale save should not reopen a submitted writing attempt.",
            scratchpad = (string?)null,
            checklist = (Dictionary<string, bool>?)null,
            draftVersion = 1
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_attempt_locked", json.RootElement.GetProperty("code").GetString());

        await AssertAttemptStateAsync(attemptId, state);
    }

    [Fact]
    public async Task WritingDraftPatch_RejectsAttemptFromAnotherSubtest()
    {
        using var client = await CreateClientForUserAsync("writing-cross-subtest-draft");
        var listeningAttemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "practice");
        var before = await ReadAttemptSnapshotAsync(listeningAttemptId);

        var response = await client.PatchAsJsonAsync($"/v1/writing/attempts/{listeningAttemptId}/draft", new
        {
            content = "This must not mutate a Listening attempt through the Writing route.",
            scratchpad = (string?)null,
            checklist = (Dictionary<string, bool>?)null,
            draftVersion = 1
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_attempt_not_found", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await ReadAttemptSnapshotAsync(listeningAttemptId));
    }

    [Fact]
    public async Task WritingGet_RejectsAttemptFromAnotherSubtest()
    {
        using var client = await CreateClientForUserAsync("writing-cross-subtest-get");
        var listeningAttemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "practice");

        var response = await client.GetAsync($"/v1/writing/attempts/{listeningAttemptId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_attempt_not_found", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WritingHeartbeat_RejectsAttemptFromAnotherSubtest()
    {
        using var client = await CreateClientForUserAsync("writing-cross-subtest-heartbeat");
        var listeningAttemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "practice");
        var before = await ReadAttemptSnapshotAsync(listeningAttemptId);

        var response = await client.PatchAsJsonAsync($"/v1/writing/attempts/{listeningAttemptId}/heartbeat", new
        {
            elapsedSeconds = 42,
            deviceType = "desktop"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_attempt_not_found", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await ReadAttemptSnapshotAsync(listeningAttemptId));
    }

    [Theory]
    [InlineData("speaking")]
    [InlineData("listening")]
    public async Task SubtestHeartbeat_RejectsAttemptFromAnotherSubtest(string subtest)
    {
        using var client = await CreateClientForUserAsync($"{subtest}-cross-subtest-heartbeat");
        var writingAttemptId = await CreateWritingAttemptAsync(client, "practice");
        var before = await ReadAttemptSnapshotAsync(writingAttemptId);

        var response = await client.PatchAsJsonAsync($"/v1/{subtest}/attempts/{writingAttemptId}/heartbeat", new
        {
            elapsedSeconds = 42,
            deviceType = "desktop"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"{subtest}_attempt_not_found", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await ReadAttemptSnapshotAsync(writingAttemptId));
    }

    [Fact]
    public async Task WritingSubmit_RejectsAttemptFromAnotherSubtest()
    {
        using var client = await CreateClientForUserAsync("writing-cross-subtest-submit");
        var listeningAttemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "practice");
        var before = await ReadAttemptSnapshotAsync(listeningAttemptId);

        var response = await client.PostAsJsonAsync($"/v1/writing/attempts/{listeningAttemptId}/submit", new
        {
            content = "This must not queue a Writing evaluation for a Listening attempt.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_attempt_not_found", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await ReadAttemptSnapshotAsync(listeningAttemptId));
        Assert.False(await HasEvaluationAsync(listeningAttemptId, "writing"));
    }

    [Fact]
    public async Task WritingSubmit_DoesNotReplayIdempotentResponseAcrossAttempts()
    {
        using var client = await CreateClientForUserAsync("writing-idempotency-cross-attempt");
        var writingAttemptId = await CreateWritingAttemptAsync(client, "practice");
        var listeningAttemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "practice");
        var listeningBefore = await ReadAttemptSnapshotAsync(listeningAttemptId);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var writingSubmit = await client.PostAsJsonAsync($"/v1/writing/attempts/{writingAttemptId}/submit", new
        {
            content = "Dear Dr Smith, this first Writing attempt creates the idempotency record.",
            idempotencyKey
        });
        writingSubmit.EnsureSuccessStatusCode();

        var replayAgainstListening = await client.PostAsJsonAsync($"/v1/writing/attempts/{listeningAttemptId}/submit", new
        {
            content = "This must not replay the earlier Writing response for a Listening attempt.",
            idempotencyKey
        });

        Assert.Equal(HttpStatusCode.NotFound, replayAgainstListening.StatusCode);
        using var json = JsonDocument.Parse(await replayAgainstListening.Content.ReadAsStringAsync());
        Assert.Equal("writing_attempt_not_found", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(listeningBefore, await ReadAttemptSnapshotAsync(listeningAttemptId));
        Assert.False(await HasEvaluationAsync(listeningAttemptId, "writing"));
    }

    [Fact]
    public async Task ObjectiveAttemptGet_ReturnsOwnSubtestAttempts()
    {
        using var client = await CreateClientForUserAsync("listening-attempt-get");
        var attemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "practice");

        var response = await client.GetAsync($"/v1/listening/attempts/{attemptId}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(attemptId, json.RootElement.GetProperty("attemptId").GetString());
        Assert.Equal("listening", json.RootElement.GetProperty("subtest").GetString());
    }

    [Fact]
    public async Task ObjectiveAnswersPatch_RejectsAttemptFromAnotherSubtest()
    {
        using var client = await CreateClientForUserAsync("listening-answers-cross-subtest");
        var writingAttemptId = await CreateWritingAttemptAsync(client, "practice");
        var before = await ReadAttemptSnapshotAsync(writingAttemptId);

        var response = await client.PatchAsJsonAsync($"/v1/listening/attempts/{writingAttemptId}/answers", new
        {
            answers = new Dictionary<string, string?>
            {
                ["q-1"] = "wrong route"
            }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("listening_attempt_not_found", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await ReadAttemptSnapshotAsync(writingAttemptId));
    }

    [Fact]
    public async Task ObjectiveAnswersPatch_RejectsCompletedAttempt()
    {
        using var client = await CreateClientForUserAsync("listening-answers-completed");
        var attemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "practice");
        var submit = await client.PostAsync($"/v1/listening/attempts/{attemptId}/submit", content: null);
        submit.EnsureSuccessStatusCode();
        var before = await ReadAttemptSnapshotAsync(attemptId);

        var response = await client.PatchAsJsonAsync($"/v1/listening/attempts/{attemptId}/answers", new
        {
            answers = new Dictionary<string, string?>
            {
                ["q-1"] = "too late"
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("objective_attempt_locked", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await ReadAttemptSnapshotAsync(attemptId));
    }

    [Fact]
    public async Task ObjectiveSubmit_RejectsAttemptFromAnotherSubtest()
    {
        using var client = await CreateClientForUserAsync("listening-submit-cross-subtest");
        var writingAttemptId = await CreateWritingAttemptAsync(client, "practice");
        var before = await ReadAttemptSnapshotAsync(writingAttemptId);

        var response = await client.PostAsync($"/v1/listening/attempts/{writingAttemptId}/submit", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("listening_attempt_not_found", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await ReadAttemptSnapshotAsync(writingAttemptId));
        Assert.False(await HasEvaluationAsync(writingAttemptId, "listening"));
    }

    [Fact]
    public async Task ObjectiveEvaluationGet_RejectsEvaluationFromAnotherSubtest()
    {
        using var client = await CreateClientForUserAsync("listening-evaluation-cross-subtest");
        var writingAttemptId = await CreateWritingAttemptAsync(client, "practice");
        var submit = await client.PostAsJsonAsync($"/v1/writing/attempts/{writingAttemptId}/submit", new
        {
            content = "Dear Dr Smith, this Writing evaluation must not be readable through Listening.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        submit.EnsureSuccessStatusCode();
        using var submitJson = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        var evaluationId = submitJson.RootElement.GetProperty("evaluationId").GetString();

        var response = await client.GetAsync($"/v1/listening/evaluations/{evaluationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("listening_evaluation_not_found", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WritingLint_DerivesCaseNotesMarkersFromServerContent_WhenContentIdProvided()
    {
        const string contentId = "writing-lint-server-marker-regression";
        await SeedWritingLintContentAsync(contentId,
            "Patient requested referral after a shared decision-making discussion. Consent was documented in the notes.");
        using var client = await CreateClientForUserAsync("writing-lint-server-marker-user");

        var response = await client.PostAsJsonAsync("/v1/writing/lint", new
        {
            contentId,
            letterText = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for your assessment.\n\nYours sincerely,\nDoctor",
            letterType = "routine_referral",
            profession = "nursing",
            caseNotesMarkers = new
            {
                patientInitiatedReferral = false,
                consentDocumented = false
            }
        });

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var findings = json.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Contains(findings, finding => finding.GetProperty("ruleId").GetString() == "R09.7");
        Assert.Contains(findings, finding => finding.GetProperty("ruleId").GetString() == "R09.8");
        Assert.All(findings, finding =>
        {
            var severity = finding.GetProperty("severity");
            Assert.Equal(JsonValueKind.String, severity.ValueKind);
            var severityText = severity.GetString();
            Assert.True(
                severityText is "critical" or "major" or "minor" or "info",
                $"Unexpected severity '{severityText}'.");
        });
    }

    [Theory]
    [InlineData("occupational-therapy")]
    [InlineData("speech-pathology")]
    [InlineData("other-allied-health")]
    public async Task WritingRulebookEndpoint_AcceptsKebabProfessionSlugs(string profession)
    {
        using var client = await CreateClientForUserAsync($"rulebook-slug-{profession}");

        var response = await client.GetAsync($"/v1/rulebooks/writing/{profession}");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing", json.RootElement.GetProperty("kind").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task DiagnosticFlow_CompletesAcrossAllSubtests_AndProducesScopedResults()
    {
        using var client = await CreateClientForUserAsync("diagnostic-user");

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
            contentType = "audio/webm",
            consentAccepted = true,
            consentText = "Test consent accepted"
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

        var readingCreate = await client.PostAsJsonAsync("/v1/reading/attempts", new
        {
            contentId = "rt-001",
            context = "diagnostic",
            mode = "exam",
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        Assert.Equal(HttpStatusCode.Gone, readingCreate.StatusCode);

        var listeningAttemptId = await CreateObjectiveAttemptAsync(client, "listening", "lt-001", "diagnostic");
        var listeningSubmit = await client.PostAsync($"/v1/listening/attempts/{listeningAttemptId}/submit", content: null);
        listeningSubmit.EnsureSuccessStatusCode();

        await WaitForAsync(
            async () =>
            {
                var response = await client.GetAsync($"/v1/diagnostic/attempts/{diagnosticId}/hub");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("completedCount").GetInt32() == 3
                    && json.RootElement.GetProperty("state").GetString() == "completed";
            },
            "diagnostic hub to reach the completed state");

        var resultsResponse = await client.GetAsync($"/v1/diagnostic/attempts/{diagnosticId}/results");
        resultsResponse.EnsureSuccessStatusCode();
        using var resultsJson = JsonDocument.Parse(await resultsResponse.Content.ReadAsStringAsync());

        Assert.Equal(3, resultsJson.RootElement.GetProperty("results").GetArrayLength());
        Assert.Equal("Writing", resultsJson.RootElement.GetProperty("results")[0].GetProperty("subTest").GetString());
    }

    [Fact]
    public async Task DiagnosticTaskEndpoint_ReturnsPublishedTaskIds_ForEachSubtest()
    {
        using var client = await CreateClientForUserAsync("diagnostic-task-user");

        var subtests = new[] { "Writing", "Speaking", "Reading", "Listening" };

        foreach (var subtest in subtests)
        {
            var response = await client.GetAsync($"/v1/diagnostic/tasks?subtest={subtest}");
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var taskId = json.RootElement.GetProperty("taskId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(taskId));
            Assert.Equal(taskId, json.RootElement.GetProperty("contentId").GetString());
            var normalizedSubtest = subtest.ToLowerInvariant();
            Assert.Equal(normalizedSubtest, json.RootElement.GetProperty("subtest").GetString());

            // Reading was migrated off /v1/reading/tasks/{id} (now 410 Gone)
            // to the structured paper API. Use the paper structure endpoint
            // so the per-subtest follow-up call exercises real production
            // routing.
            var taskRoute = normalizedSubtest == "reading"
                ? $"/v1/reading-papers/papers/{taskId}/structure"
                : $"/v1/{normalizedSubtest}/tasks/{taskId}";
            var taskResponse = await client.GetAsync(taskRoute);
            taskResponse.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task CompletedEvaluation_RegeneratesTheStudyPlan()
    {
        using var client = await CreateClientForUserAsync("study-plan-user");

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

    [Fact]
    public async Task SpeakingTask_LearnerProjection_NeverLeaksInterlocutorCard()
    {
        // Wave 2 of docs/SPEAKING-MODULE-PLAN.md: the hidden interlocutor
        // card is curated for tutors/admins only. Mirroring the Reading
        // CorrectAnswerJson pattern, the learner-facing payload must never
        // contain `interlocutorCard` at any nesting level.
        using var client = await CreateClientForUserAsync("speaking-projection");

        var listResponse = await client.GetAsync("/v1/speaking/tasks");
        listResponse.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        AssertNoInterlocutorCard(listJson.RootElement);

        var detailResponse = await client.GetAsync("/v1/speaking/tasks/st-001");
        detailResponse.EnsureSuccessStatusCode();
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        AssertNoInterlocutorCard(detailJson.RootElement);

        // Compliance flag must declare the card hidden so admins/auditors
        // can grep this single field across the API surface.
        Assert.True(detailJson.RootElement
            .GetProperty("compliance")
            .GetProperty("hiddenInterlocutorCard")
            .GetBoolean());
    }

    private static void AssertNoInterlocutorCard(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    Assert.False(
                        string.Equals(prop.Name, "interlocutorCard", StringComparison.OrdinalIgnoreCase),
                        "Learner speaking projection leaked the interlocutorCard property.");
                    AssertNoInterlocutorCard(prop.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoInterlocutorCard(item);
                }
                break;
        }
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

    private async Task<string> SeedPublishedFullMockBundleAsync(string userId, int walletCredits)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        const string provenance = "Regression test content supplied by platform owner for internal practice use.";
        var papers = new[]
        {
            ("mock-listening-regression", "listening", "Listening Regression Paper", 42, false),
            ("mock-reading-regression", "reading", "Reading Regression Paper", 60, false),
            ("mock-writing-regression", "writing", "Writing Regression Paper", 45, true),
            ("mock-speaking-regression", "speaking", "Speaking Regression Paper", 20, true)
        };

        foreach (var (paperId, subtest, title, duration, professionScoped) in papers)
        {
            if (await db.ContentPapers.AnyAsync(x => x.Id == paperId))
            {
                continue;
            }

            db.ContentPapers.Add(new ContentPaper
            {
                Id = paperId,
                SubtestCode = subtest,
                Title = title,
                Slug = paperId,
                ProfessionId = professionScoped ? "medicine" : null,
                AppliesToAllProfessions = !professionScoped,
                Difficulty = "standard",
                EstimatedDurationMinutes = duration,
                Status = ContentStatus.Published,
                SourceProvenance = provenance,
                CreatedByAdminId = "test-admin",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now
            });
        }

        const string bundleId = "mock-bundle-regression-full";
        if (!await db.MockBundles.AnyAsync(x => x.Id == bundleId))
        {
            db.MockBundles.Add(new MockBundle
            {
                Id = bundleId,
                Title = "Regression Full Mock",
                Slug = "regression-full-mock",
                MockType = "full",
                AppliesToAllProfessions = true,
                Status = ContentStatus.Published,
                EstimatedDurationMinutes = 167,
                SourceProvenance = provenance,
                CreatedByAdminId = "test-admin",
                UpdatedByAdminId = "test-admin",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now
            });

            for (var index = 0; index < papers.Length; index++)
            {
                var (paperId, subtest, _, duration, reviewEligible) = papers[index];
                db.MockBundleSections.Add(new MockBundleSection
                {
                    Id = $"mock-bundle-section-regression-{subtest}",
                    MockBundleId = bundleId,
                    SectionOrder = index + 1,
                    SubtestCode = subtest,
                    ContentPaperId = paperId,
                    TimeLimitMinutes = duration,
                    ReviewEligible = reviewEligible,
                    IsRequired = true,
                    CreatedAt = now
                });
            }
        }

        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId);
        wallet.CreditBalance = walletCredits;
        wallet.LastUpdatedAt = now;

        await db.SaveChangesAsync();
        return bundleId;
    }

    private async Task SeedWritingLintContentAsync(string contentId, string caseNotes)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (await db.ContentItems.AnyAsync(content => content.Id == contentId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.ContentItems.Add(new ContentItem
        {
            Id = contentId,
            ContentType = "writing_task",
            SubtestCode = "writing",
            ProfessionId = "medicine",
            Title = "Server Marker Regression Task",
            Difficulty = "standard",
            EstimatedDurationMinutes = 45,
            ScenarioType = "routine_referral",
            PublishedRevisionId = "server-marker-regression",
            Status = ContentStatus.Published,
            CaseNotes = caseNotes,
            DetailJson = "{\"letterType\":\"routine_referral\"}",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });

        await db.SaveChangesAsync();
    }

    private static async Task<string> CreateWritingAttemptAsync(HttpClient client, string context, string mode = "timed")
    {
        var response = await client.PostAsJsonAsync("/v1/writing/attempts", new
        {
            contentId = "wt-001",
            context,
            mode,
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("attemptId").GetString()
            ?? throw new InvalidOperationException("Writing attempt id was missing.");
    }

    private async Task MoveAttemptStartBackAsync(string attemptId, TimeSpan age)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempt = await db.Attempts.FirstAsync(x => x.Id == attemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.Subtract(age);
        await db.SaveChangesAsync();
    }

    private async Task SetAttemptStateAsync(string attemptId, AttemptState state)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempt = await db.Attempts.FirstAsync(x => x.Id == attemptId);
        attempt.State = state;
        if (state is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            attempt.SubmittedAt ??= DateTimeOffset.UtcNow;
        }
        if (state == AttemptState.Completed)
        {
            attempt.CompletedAt ??= DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    private async Task AssertAttemptStateAsync(string attemptId, AttemptState expected)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempt = await db.Attempts.AsNoTracking().FirstAsync(x => x.Id == attemptId);
        Assert.Equal(expected, attempt.State);
    }

    private async Task<(string SubtestCode, AttemptState State, string DraftContent, string Scratchpad, string ChecklistJson, string AnswersJson, int ElapsedSeconds, DateTimeOffset? LastClientSyncAt)> ReadAttemptSnapshotAsync(string attemptId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempt = await db.Attempts.AsNoTracking().FirstAsync(x => x.Id == attemptId);
        return (attempt.SubtestCode, attempt.State, attempt.DraftContent, attempt.Scratchpad, attempt.ChecklistJson, attempt.AnswersJson, attempt.ElapsedSeconds, attempt.LastClientSyncAt);
    }

    private async Task<bool> HasEvaluationAsync(string attemptId, string subtest)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await db.Evaluations.AnyAsync(x => x.AttemptId == attemptId && x.SubtestCode == subtest);
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
