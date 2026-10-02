using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// WAI-06 draft contract over HTTP: GET/PUT /v1/writing/drafts/{scenarioId}/{mode}
/// with compare-and-set versions, plus the Post Submissions list.
/// </summary>
public sealed class WritingDraftEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task AStaleRetriedPut_CanNeverClobberNewerText()
    {
        using var client = await LearnerClientAsync();
        var url = $"/v1/writing/drafts/{Guid.NewGuid()}/practice";

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, Body("first words", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, Body("newer text", 1))).StatusCode);

        // The slow first-version retry arrives last: it must be refused, not applied.
        var stale = await client.PutAsJsonAsync(url, Body("stale older text", 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("draft_version_conflict", (await JsonAsync(stale)).GetProperty("code").GetString());

        var draft = await JsonAsync(await client.GetAsync(url));
        Assert.Equal("newer text", draft.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Draft_RoundTrip_CreateOnly_CompareAndSet_AndTheExactDtoShape()
    {
        using var client = await LearnerClientAsync();
        var scenarioId = Guid.NewGuid();
        var url = $"/v1/writing/drafts/{scenarioId}/practice";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);

        var created = await client.PutAsJsonAsync(url, new
        {
            content = "",
            wordCount = 0,
            timeSpentSeconds = 0,
            expectedVersion = 0,
            phase = "reading",
            readingSecondsRemaining = 300,
            writingSecondsRemaining = 2400,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(1, (await JsonAsync(created)).GetProperty("version").GetInt32());

        var duplicateCreate = await client.PutAsJsonAsync(url, Body("second tab", 0));
        Assert.Equal(HttpStatusCode.Conflict, duplicateCreate.StatusCode);
        Assert.Equal("draft_version_conflict", (await JsonAsync(duplicateCreate)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, Body("Dear Dr Green", 1))).StatusCode);
        var legacy = await JsonAsync(await client.PutAsJsonAsync(url, Body("Dear Dr Green, I am writing", null)));
        Assert.Equal(3, legacy.GetProperty("version").GetInt32());

        var draft = await JsonAsync(await client.GetAsync(url));
        Assert.Equal(
            new[]
            {
                "attemptStartedAt", "content", "draftId", "lastSavedAt", "mode", "phase", "readingSecondsRemaining",
                "scenarioId", "status", "submissionId", "submissionStatus", "timeSpentSeconds", "userId", "version",
                "wordCount", "writingSecondsRemaining",
            },
            draft.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Dear Dr Green, I am writing", draft.GetProperty("content").GetString());
        Assert.Equal(("active", "reading", 300, 2400), (
            draft.GetProperty("status").GetString(),
            draft.GetProperty("phase").GetString(),
            draft.GetProperty("readingSecondsRemaining").GetInt32(),
            draft.GetProperty("writingSecondsRemaining").GetInt32()));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("submissionId").ValueKind);
        Assert.Equal(scenarioId, draft.GetProperty("scenarioId").GetGuid());
    }

    private static object Body(string content, int? expectedVersion) => new
    {
        content,
        wordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
        timeSpentSeconds = 60,
        expectedVersion,
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> LearnerClientAsync(string? userId = null)
    {
        userId ??= $"draft-learner-{Guid.NewGuid():N}";
        await factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }
}
