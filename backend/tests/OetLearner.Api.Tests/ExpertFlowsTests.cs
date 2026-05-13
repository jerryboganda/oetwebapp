using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class ExpertFlowsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public ExpertFlowsTests(FirstPartyAuthTestWebApplicationFactory factory)
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
    public async Task ExpertMe_ReturnsExpertProfile_WithSeededJwtSignIn()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

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
    public async Task ExpertDashboard_ReturnsOperationalSummary()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/dashboard");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("activeAssignedReviews").GetInt32() >= 0);
        Assert.True(json.RootElement.GetProperty("savedDraftCount").GetInt32() >= 0);
        Assert.True(json.RootElement.GetProperty("recentActivity").GetArrayLength() >= 0);
        Assert.True(json.RootElement.GetProperty("assignedReviews").GetArrayLength() >= 0);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("generatedAt").GetString()));
    }

    [Fact]
    public async Task ExpertDashboard_UsesBearerToken_WhenDevelopmentAuthIsEnabled()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient("expert@oet-prep.dev", "Password123!", expectedRole: "expert");

        var response = await client.GetAsync("/v1/expert/dashboard");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("activeAssignedReviews").GetInt32() >= 0);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("generatedAt").GetString()));
    }

    [Fact]
    public async Task ExpertDashboard_RemainsQueryable_WhenSqliteBacksDesktopRuntime()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"oet-expert-dashboard-{Guid.NewGuid():N}.db");

        try
        {
            await using var factory = new SqliteExpertWebApplicationFactory(sqlitePath);
            using var client = factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

            var response = await client.GetAsync("/v1/expert/dashboard");
            var payload = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(payload);
            Assert.True(json.RootElement.GetProperty("activeAssignedReviews").GetInt32() >= 0);
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("generatedAt").GetString()));
        }
        finally
        {
            foreach (var path in new[] { sqlitePath, $"{sqlitePath}-wal", $"{sqlitePath}-shm" })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Windows can briefly retain SQLite file handles after host disposal.
                }
            }
        }
    }

    [Fact]
    public async Task ExpertCanClaimAndReleaseReview()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
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
    public async Task ExpertCalibrationCaseDetail_ReturnsBenchmarkWorkspaceData()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/calibration/cases/cal-001");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("cal-001", json.RootElement.GetProperty("id").GetString());
        Assert.True(json.RootElement.GetProperty("artifacts").GetArrayLength() >= 1);
        Assert.True(json.RootElement.GetProperty("benchmarkRubric").GetArrayLength() >= 1);
        Assert.True(json.RootElement.GetProperty("referenceNotes").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ExpertCalibrationDraft_RoundTripsAndRemainsUnfinished()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var saveResponse = await client.PostAsJsonAsync("/v1/expert/calibration/cases/cal-002/draft", new
        {
            scores = new Dictionary<string, int>
            {
                ["intelligibility"] = 5,
                ["relationshipBuilding"] = 2
            },
            notes = "Saved for later."
        });
        saveResponse.EnsureSuccessStatusCode();

        using var saveJson = JsonDocument.Parse(await saveResponse.Content.ReadAsStringAsync());
        Assert.True(saveJson.RootElement.GetProperty("isDraft").GetBoolean());
        Assert.Equal(2, saveJson.RootElement.GetProperty("scores").GetProperty("relationshipBuilding").GetInt32());

        var detailResponse = await client.GetAsync("/v1/expert/calibration/cases/cal-002");
        detailResponse.EnsureSuccessStatusCode();
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("draft", detailJson.RootElement.GetProperty("status").GetString());
        var existingSubmission = detailJson.RootElement.GetProperty("existingSubmission");
        Assert.True(existingSubmission.GetProperty("isDraft").GetBoolean());
        Assert.Equal("Saved for later.", existingSubmission.GetProperty("notes").GetString());
        Assert.Equal(5, existingSubmission.GetProperty("submittedScores").GetProperty("intelligibility").GetInt32());

        var casesResponse = await client.GetAsync("/v1/expert/calibration/cases");
        casesResponse.EnsureSuccessStatusCode();
        using var casesJson = JsonDocument.Parse(await casesResponse.Content.ReadAsStringAsync());
        var draftCase = FindArrayItem(casesJson.RootElement, "id", "cal-002");
        Assert.Equal("draft", draftCase.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, draftCase.GetProperty("alignmentScore").ValueKind);

        var dashboardResponse = await client.GetAsync("/v1/expert/dashboard");
        dashboardResponse.EnsureSuccessStatusCode();
        using var dashboardJson = JsonDocument.Parse(await dashboardResponse.Content.ReadAsStringAsync());
        Assert.True(dashboardJson.RootElement.GetProperty("calibrationDueCount").GetInt32() >= 1);

        var historyResponse = await client.GetAsync("/v1/expert/calibration/history");
        historyResponse.EnsureSuccessStatusCode();
        using var historyJson = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain(historyJson.RootElement.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("caseId").GetString() == "cal-002");
    }

    [Fact]
    public async Task ExpertCalibrationDraft_SubmitUpgradesAndUsesNormalizedSpeakingAlignment()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var draftResponse = await client.PostAsJsonAsync("/v1/expert/calibration/cases/cal-002/draft", new
        {
            scores = new Dictionary<string, int>
            {
                ["intelligibility"] = 4
            },
            notes = "Initial pass."
        });
        draftResponse.EnsureSuccessStatusCode();

        var submitResponse = await client.PostAsJsonAsync("/v1/expert/calibration/cases/cal-002/submit", new
        {
            scores = new Dictionary<string, int>
            {
                ["intelligibility"] = 5,
                ["fluency"] = 5,
                ["appropriateness"] = 4,
                ["grammar"] = 5,
                ["relationshipBuilding"] = 2,
                ["patientPerspective"] = 2,
                ["providingStructure"] = 3,
                ["informationGathering"] = 2,
                ["informationGiving"] = 2
            },
            notes = "Final aligned submission."
        });
        submitResponse.EnsureSuccessStatusCode();

        using var submitJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        var alignment = submitJson.RootElement.GetProperty("alignment").GetDouble();
        Assert.InRange(alignment, 96.25, 96.35);

        var detailResponse = await client.GetAsync("/v1/expert/calibration/cases/cal-002");
        detailResponse.EnsureSuccessStatusCode();
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("completed", detailJson.RootElement.GetProperty("status").GetString());
        var existingSubmission = detailJson.RootElement.GetProperty("existingSubmission");
        Assert.False(existingSubmission.GetProperty("isDraft").GetBoolean());
        Assert.InRange(existingSubmission.GetProperty("alignmentScore").GetDouble(), 96.25, 96.35);
        Assert.Equal("Final aligned submission.", existingSubmission.GetProperty("notes").GetString());

        var casesResponse = await client.GetAsync("/v1/expert/calibration/cases");
        casesResponse.EnsureSuccessStatusCode();
        using var casesJson = JsonDocument.Parse(await casesResponse.Content.ReadAsStringAsync());
        var completedCase = FindArrayItem(casesJson.RootElement, "id", "cal-002");
        Assert.Equal("completed", completedCase.GetProperty("status").GetString());
        Assert.InRange(completedCase.GetProperty("alignmentScore").GetDouble(), 96.25, 96.35);

        var historyResponse = await client.GetAsync("/v1/expert/calibration/history");
        historyResponse.EnsureSuccessStatusCode();
        using var historyJson = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        var historyEntry = FindArrayItem(historyJson.RootElement.GetProperty("entries"), "caseId", "cal-002");
        Assert.InRange(historyEntry.GetProperty("alignmentScore").GetDouble(), 96.25, 96.35);

        var alignmentResponse = await client.GetAsync("/v1/expert/calibration/alignment");
        alignmentResponse.EnsureSuccessStatusCode();
        using var alignmentJson = JsonDocument.Parse(await alignmentResponse.Content.ReadAsStringAsync());
        Assert.True(alignmentJson.RootElement.GetProperty("totalSubmissions").GetInt32() >= 2);
        Assert.InRange(alignmentJson.RootElement.GetProperty("latestAlignment").GetDouble(), 96.25, 96.35);
    }

    [Fact]
    public async Task ExpertCalibrationDraft_SubmitThenRejectsFurtherDraftOrSubmitMutations()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var submitPayload = new
        {
            scores = new Dictionary<string, int>
            {
                ["intelligibility"] = 5,
                ["fluency"] = 5,
                ["appropriateness"] = 4,
                ["grammar"] = 5,
                ["relationshipBuilding"] = 2,
                ["patientPerspective"] = 2,
                ["providingStructure"] = 3,
                ["informationGathering"] = 2,
                ["informationGiving"] = 2
            },
            notes = "Final aligned submission."
        };

        var firstSubmit = await client.PostAsJsonAsync("/v1/expert/calibration/cases/cal-002/submit", submitPayload);
        firstSubmit.EnsureSuccessStatusCode();

        var draftConflict = await client.PostAsJsonAsync("/v1/expert/calibration/cases/cal-002/draft", new
        {
            scores = new Dictionary<string, int> { ["intelligibility"] = 4 },
            notes = "Cannot overwrite."
        });
        var draftConflictBody = await draftConflict.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, draftConflict.StatusCode);
        Assert.Contains("calibration_already_submitted", draftConflictBody);

        var submitConflict = await client.PostAsJsonAsync("/v1/expert/calibration/cases/cal-002/submit", submitPayload);
        var submitConflictBody = await submitConflict.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, submitConflict.StatusCode);
        Assert.Contains("calibration_already_submitted", submitConflictBody);

        var lockedDetailResponse = await client.GetAsync("/v1/expert/calibration/cases/cal-002");
        lockedDetailResponse.EnsureSuccessStatusCode();
        using var lockedDetailJson = JsonDocument.Parse(await lockedDetailResponse.Content.ReadAsStringAsync());
        Assert.Equal("completed", lockedDetailJson.RootElement.GetProperty("status").GetString());
        var lockedExistingSubmission = lockedDetailJson.RootElement.GetProperty("existingSubmission");
        Assert.False(lockedExistingSubmission.GetProperty("isDraft").GetBoolean());
        Assert.InRange(lockedExistingSubmission.GetProperty("alignmentScore").GetDouble(), 96.25, 96.35);
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
    public async Task ExpertLearners_ReturnsAssignedOnlyDirectory()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/learners");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("totalCount").GetInt32() >= 1);
        var first = json.RootElement.GetProperty("items")[0];
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("name").GetString()));
        Assert.True(first.GetProperty("reviewsInScope").GetInt32() >= 1);
    }

    [Fact]
    public async Task ExpertQueueFilterMetadata_ReturnsBackedFilterSets()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/queue/filters/metadata");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("types").GetArrayLength() >= 2);
        Assert.True(json.RootElement.GetProperty("professions").GetArrayLength() >= 1);
        Assert.True(json.RootElement.GetProperty("assignmentStates").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ExpertReviewHistory_ReturnsTimelineForAssignedReview()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var claimResponse = await client.PostAsync("/v1/expert/queue/review-queue-002/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/v1/expert/reviews/review-queue-002/history");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("review-queue-002", json.RootElement.GetProperty("reviewRequestId").GetString());
        Assert.True(json.RootElement.GetProperty("draftVersionCount").GetInt32() >= 0);
        Assert.True(json.RootElement.GetProperty("entries").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ExpertLearnerReviewContext_ReturnsScopedSignals()
    {
        using var client = CreateExpertClient(_factory);

        var response = await client.GetAsync("/v1/expert/learners/mock-user-001/review-context");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mock-user-001", json.RootElement.GetProperty("id").GetString());
        Assert.True(json.RootElement.GetProperty("reviewsInScope").GetInt32() >= 1);
        Assert.True(json.RootElement.GetProperty("subTestScores").GetArrayLength() >= 0);
        Assert.True(json.RootElement.GetProperty("priorReviews").GetArrayLength() >= 0);
    }

    [Fact]
    public async Task ExpertWritingReviewBundle_RequiresClaimAndReturnsDetail()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
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
        Assert.True(json.RootElement.GetProperty("aiSuggestedScores").EnumerateObject().Any());
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
    public async Task ExpertSpeakingAudio_ReturnsAudioStreamAfterClaim()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var claimResponse = await client.PostAsync("/v1/expert/queue/review-queue-001/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/v1/expert/reviews/review-queue-001/speaking/audio");
        response.EnsureSuccessStatusCode();

        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.StartsWith("audio/", response.Content.Headers.ContentType!.MediaType, StringComparison.OrdinalIgnoreCase);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task ExpertDraftSaveAndSubmit_WritingFlow()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = CreateExpertClient(factory);

        var claimResponse = await client.PostAsync("/v1/expert/queue/review-queue-002/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var draftResponse = await client.PutAsJsonAsync("/v1/expert/reviews/review-queue-002/draft", new
        {
            scores = new Dictionary<string, int> { ["purpose"] = 3, ["content"] = 5, ["conciseness"] = 4, ["genre"] = 4, ["organization"] = 5, ["language"] = 4 },
            criterionComments = new Dictionary<string, string> { ["purpose"] = "Excellent clarity." },
            finalComment = "Strong submission overall.",
            anchoredComments = new[] { new { text = "Tighten this point.", startOffset = 5, endOffset = 18 } },
            scratchpad = "Check tone against discharge intent.",
            checklistItems = new[]
            {
                new { id = "purpose", label = "Purpose is explicit in opening.", @checked = true },
                new { id = "content", label = "All clinically relevant facts are represented.", @checked = false }
            }
        });
        draftResponse.EnsureSuccessStatusCode();

        using var draftJson = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        var version = draftJson.RootElement.GetProperty("version").GetInt32();
        Assert.False(string.IsNullOrWhiteSpace(draftJson.RootElement.GetProperty("savedAt").GetString()));
        Assert.Equal("Check tone against discharge intent.", draftJson.RootElement.GetProperty("scratchpad").GetString());
        Assert.Equal(2, draftJson.RootElement.GetProperty("checklistItems").GetArrayLength());

        await AttachWritingVoiceNoteAsync(factory, client, "review-queue-002");

        var submitResponse = await client.PostAsJsonAsync("/v1/expert/reviews/review-queue-002/writing/submit", new
        {
            scores = new Dictionary<string, int> { ["purpose"] = 3, ["content"] = 5, ["conciseness"] = 4, ["genre"] = 4, ["organization"] = 5, ["language"] = 4 },
            criterionComments = new Dictionary<string, string> { ["purpose"] = "Excellent clarity." },
            finalComment = "Strong submission overall.",
            version
        });
        submitResponse.EnsureSuccessStatusCode();
    }

    private static async Task AttachWritingVoiceNoteAsync(
        FirstPartyAuthTestWebApplicationFactory factory,
        HttpClient client,
        string reviewRequestId)
    {
        const string mediaAssetId = "media-review-queue-002-voice-note";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (await db.MediaAssets.FindAsync(mediaAssetId) is null)
        {
            var now = DateTimeOffset.UtcNow;
            db.MediaAssets.Add(new MediaAsset
            {
                Id = mediaAssetId,
                OriginalFilename = "review-queue-002-feedback.webm",
                MimeType = "audio/webm",
                Format = "webm",
                SizeBytes = 4096,
                DurationSeconds = 92,
                StoragePath = "test/review-queue-002-feedback.webm",
                Status = MediaAssetStatus.Ready,
                MediaKind = "audio",
                UploadedBy = "expert-001",
                UploadedAt = now.AddMinutes(-5),
                ProcessedAt = now.AddMinutes(-4)
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync($"/v1/expert/reviews/{reviewRequestId}/writing/voice-notes", new
        {
            mediaAssetId,
            durationSeconds = 92,
            transcriptText = "Purpose is clear; clinical relevance needs a little tighter prioritisation.",
            writtenNotes = "Good structure overall. Tighten the action request and remove lower-value history.",
            rubricScores = new Dictionary<string, int>
            {
                ["purpose"] = 3,
                ["content"] = 5,
                ["conciseness"] = 4,
                ["genre"] = 4,
                ["organization"] = 5,
                ["language"] = 4
            }
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ExpertSubmitRejectsIncompleteRubric()
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
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
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var learnerClient = factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");
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

    [Fact]
    public async Task ScheduleExceptions_CrudLifecycle()
    {
        using var client = CreateExpertClient(_factory);

        // List exceptions — initially empty
        var listResponse = await client.GetAsync("/v1/expert/schedule/exceptions");
        listResponse.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, listJson.RootElement.GetProperty("exceptions").GetArrayLength());

        // Create a blocked-date exception
        var createBlockedResponse = await client.PostAsJsonAsync("/v1/expert/schedule/exceptions", new
        {
            date = "2026-12-25",
            isBlocked = true,
            reason = "Christmas holiday"
        });
        createBlockedResponse.EnsureSuccessStatusCode();
        using var createdBlocked = JsonDocument.Parse(await createBlockedResponse.Content.ReadAsStringAsync());
        var blockedId = createdBlocked.RootElement.GetProperty("id").GetString();
        Assert.NotNull(blockedId);
        Assert.True(createdBlocked.RootElement.GetProperty("isBlocked").GetBoolean());
        Assert.Equal("2026-12-25", createdBlocked.RootElement.GetProperty("date").GetString());

        // Create a custom-hours exception
        var createCustomResponse = await client.PostAsJsonAsync("/v1/expert/schedule/exceptions", new
        {
            date = "2026-12-24",
            isBlocked = false,
            startTime = "09:00",
            endTime = "12:00",
            reason = "Christmas Eve — morning only"
        });
        createCustomResponse.EnsureSuccessStatusCode();
        using var createdCustom = JsonDocument.Parse(await createCustomResponse.Content.ReadAsStringAsync());
        var customId = createdCustom.RootElement.GetProperty("id").GetString();
        Assert.False(createdCustom.RootElement.GetProperty("isBlocked").GetBoolean());
        Assert.Equal("09:00", createdCustom.RootElement.GetProperty("startTime").GetString());

        // List exceptions — should have 2
        var listResponse2 = await client.GetAsync("/v1/expert/schedule/exceptions");
        listResponse2.EnsureSuccessStatusCode();
        using var listJson2 = JsonDocument.Parse(await listResponse2.Content.ReadAsStringAsync());
        Assert.Equal(2, listJson2.RootElement.GetProperty("exceptions").GetArrayLength());

        // List with date range filter
        var filteredResponse = await client.GetAsync("/v1/expert/schedule/exceptions?from=2026-12-25&to=2026-12-31");
        filteredResponse.EnsureSuccessStatusCode();
        using var filteredJson = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, filteredJson.RootElement.GetProperty("exceptions").GetArrayLength());

        // Delete exception
        var deleteResponse = await client.DeleteAsync($"/v1/expert/schedule/exceptions/{blockedId}");
        deleteResponse.EnsureSuccessStatusCode();

        // List exceptions — should have 1
        var listResponse3 = await client.GetAsync("/v1/expert/schedule/exceptions");
        listResponse3.EnsureSuccessStatusCode();
        using var listJson3 = JsonDocument.Parse(await listResponse3.Content.ReadAsStringAsync());
        Assert.Equal(1, listJson3.RootElement.GetProperty("exceptions").GetArrayLength());
    }

    [Fact]
    public async Task ScheduleExceptions_DuplicateDateReturns400()
    {
        using var client = CreateExpertClient(_factory);

        await client.PostAsJsonAsync("/v1/expert/schedule/exceptions", new
        {
            date = "2026-11-15",
            isBlocked = true,
            reason = "Test"
        });

        var dupResponse = await client.PostAsJsonAsync("/v1/expert/schedule/exceptions", new
        {
            date = "2026-11-15",
            isBlocked = true,
            reason = "Duplicate"
        });

        Assert.Equal(HttpStatusCode.BadRequest, dupResponse.StatusCode);
    }

    private static JsonElement FindArrayItem(JsonElement array, string propertyName, string expectedValue)
    {
        var match = array.EnumerateArray().FirstOrDefault(item =>
            item.TryGetProperty(propertyName, out var property) &&
            property.GetString() == expectedValue);
        Assert.NotEqual(JsonValueKind.Undefined, match.ValueKind);
        return match;
    }

    private static HttpClient CreateExpertClient(TestWebApplicationFactory factory, string expertId = "expert-001")
    {
        var email = string.Equals(expertId, "expert-unauthorised", StringComparison.Ordinal)
            ? SeedData.ExpertSecondaryEmail
            : SeedData.ExpertEmail;
        return factory.CreateAuthenticatedClient(email, SeedData.LocalSeedPassword, expectedRole: "expert");
    }

    private sealed class SqliteExpertWebApplicationFactory(string sqlitePath) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={sqlitePath}"
                });
            });
        }
    }

}
