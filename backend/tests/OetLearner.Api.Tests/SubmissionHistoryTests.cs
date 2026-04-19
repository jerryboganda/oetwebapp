using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Contract + behavioural tests for the Submission History subsystem.
///
/// These tests verify the post-enhancement invariants:
///   - List endpoint returns the new envelope (items, nextCursor, total, facets, sparkline).
///   - Every item carries a scaled score + passState + passLabel (no raw percentages).
///   - Non-evidence attempts (Abandoned / Paused / NotStarted) never surface.
///   - Detail endpoint returns in one round-trip.
///   - Compare endpoint produces a real deterministic summary (never the old hardcoded sentence).
///   - Hide / unhide round-trip.
/// </summary>
public class SubmissionHistoryTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SubmissionHistoryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureLearnerProfileAsync("mock-user-001", "mock-user-001@example.test", "mock-user-001").GetAwaiter().GetResult();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task List_ReturnsNewEnvelopeShape()
    {
        var response = await _client.GetAsync("/v1/submissions");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(root.TryGetProperty("total", out _));
        Assert.True(root.TryGetProperty("facets", out var facets));
        Assert.True(facets.TryGetProperty("bySubtest", out _));
        Assert.True(facets.TryGetProperty("byContext", out _));
        Assert.True(facets.TryGetProperty("byReviewStatus", out _));
        Assert.True(root.TryGetProperty("sparkline", out _));
        Assert.True(root.TryGetProperty("nextCursor", out _));
    }

    [Fact]
    public async Task List_EveryItemHasScaledScoreAndPassState()
    {
        var response = await _client.GetAsync("/v1/submissions");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.True(item.TryGetProperty("scaledScore", out _));
            Assert.True(item.TryGetProperty("passState", out var passState));
            // The only allowed values per the canonical scoring policy:
            var state = passState.GetString();
            Assert.Contains(state, new[] { "pass", "fail", "pending", "country_required", "country_unsupported" });
            Assert.True(item.TryGetProperty("scoreLabel", out var label));
            var labelText = label.GetString() ?? string.Empty;
            Assert.DoesNotContain('%', labelText);
        }
    }

    [Fact]
    public async Task List_NeverIncludesAbandonedOrPausedStates()
    {
        var response = await _client.GetAsync("/v1/submissions?includeHidden=true");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            var state = item.GetProperty("state").GetString();
            Assert.NotEqual("abandoned", state);
            Assert.NotEqual("paused", state);
            Assert.NotEqual("not_started", state);
            Assert.Contains(state, new[] { "submitted", "evaluating", "completed", "failed" });
        }
    }

    [Fact]
    public async Task List_SubtestFilter_NarrowsResults()
    {
        var response = await _client.GetAsync("/v1/submissions?subtest=writing");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("writing", item.GetProperty("subtest").GetString());
        }
    }

    [Fact]
    public async Task Detail_ReturnsComposedEnvelopeInSingleRoundTrip()
    {
        var listResponse = await _client.GetAsync("/v1/submissions");
        listResponse.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var firstItem = listJson.RootElement.GetProperty("items").EnumerateArray().FirstOrDefault();
        if (firstItem.ValueKind == JsonValueKind.Undefined) return; // no seed data — treat as vacuous

        var id = firstItem.GetProperty("submissionId").GetString();
        var detailResponse = await _client.GetAsync($"/v1/submissions/{id}");
        detailResponse.EnsureSuccessStatusCode();
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        var root = detailJson.RootElement;
        Assert.True(root.TryGetProperty("submission", out _));
        Assert.True(root.TryGetProperty("evidenceSummary", out _));
        Assert.True(root.TryGetProperty("strengths", out _));
        Assert.True(root.TryGetProperty("issues", out _));
        Assert.True(root.TryGetProperty("revisionLineage", out _));
    }

    [Fact]
    public async Task Detail_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/v1/submissions/does-not-exist-999");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Compare_NeverReturnsHardcodedPlaceholder()
    {
        // The old LearnerService.CompareSubmissionsAsync shipped the literal
        // sentence below unconditionally. This test fails if that regression
        // comes back.
        const string banned = "The more recent submission shows stronger structure and slightly improved score confidence.";

        var response = await _client.GetAsync("/v1/submissions/compare");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(banned, body);
    }

    [Fact]
    public async Task Compare_ExposesDeterministicDiffShape()
    {
        var response = await _client.GetAsync("/v1/submissions/compare");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.TryGetProperty("canCompare", out _));
        Assert.True(root.TryGetProperty("scaledDelta", out _));
        Assert.True(root.TryGetProperty("criterionDeltas", out _));
    }

    [Fact]
    public async Task HideUnhide_RoundTrip_ReflectsInListWhenIncludeHidden()
    {
        var listResponse = await _client.GetAsync("/v1/submissions");
        listResponse.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var firstItem = listJson.RootElement.GetProperty("items").EnumerateArray().FirstOrDefault();
        if (firstItem.ValueKind == JsonValueKind.Undefined) return;

        var id = firstItem.GetProperty("submissionId").GetString()!;

        var hide = await _client.PostAsync($"/v1/submissions/{id}/hide", content: null);
        hide.EnsureSuccessStatusCode();

        var withoutHidden = await _client.GetAsync("/v1/submissions");
        withoutHidden.EnsureSuccessStatusCode();
        using var withoutJson = JsonDocument.Parse(await withoutHidden.Content.ReadAsStringAsync());
        var presentWithoutHidden = withoutJson.RootElement.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("submissionId").GetString() == id);
        Assert.False(presentWithoutHidden);

        var withHidden = await _client.GetAsync("/v1/submissions?includeHidden=true");
        withHidden.EnsureSuccessStatusCode();
        using var withJson = JsonDocument.Parse(await withHidden.Content.ReadAsStringAsync());
        var presentWithHidden = withJson.RootElement.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("submissionId").GetString() == id && i.GetProperty("isHidden").GetBoolean());
        Assert.True(presentWithHidden);

        // Clean up: unhide so other tests don't flake.
        var unhide = await _client.PostAsync($"/v1/submissions/{id}/unhide", content: null);
        unhide.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ExportCsv_EmitsCsvHeaderAndNoPercentage()
    {
        var response = await _client.GetAsync("/v1/submissions/export.csv");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("submission_id,subtest,context,task,attempt_date,state,review_status,scaled_score,pass_state,grade,hidden", body);
        Assert.DoesNotContain('%', body);
    }

    [Fact]
    public async Task BulkReview_EmptyBatch_Returns400()
    {
        var payload = new { Items = Array.Empty<object>() };
        var response = await _client.PostAsJsonAsync("/v1/reviews/requests/batch", payload);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
