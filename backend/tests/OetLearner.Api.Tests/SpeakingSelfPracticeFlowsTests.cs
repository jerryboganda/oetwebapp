using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// Wave 5 of docs/SPEAKING-MODULE-PLAN.md - the speaking task →
// AI-patient deep-link must go through ConversationService end-to-end so
// no new AI provider or grounding code is added. We verify by:
//   1. Posting against /v1/speaking/tasks/{contentId}/self-practice
//      with a real published speaking ContentItem (st-001).
//   2. Asserting the response carries a redirect path of
//      /conversation/session/cs-... and the session id matches a row in
//      ConversationSessions.
//   3. Asserting that row's SubtestCode == "speaking" and TaskTypeCode
//      == "oet-roleplay" (i.e. the conversation service grounded it via
//      the existing rulebook path).
[Collection("AuthFlows")]
public class SpeakingSelfPracticeFlowsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public SpeakingSelfPracticeFlowsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SelfPractice_CreatesConversationSession_AndReturnsRedirectPath()
    {
        using var client = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var resp = await client.PostAsJsonAsync("/v1/speaking/tasks/st-001/self-practice", new { });
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var redirectPath = doc.RootElement.GetProperty("redirectPath").GetString();
        Assert.NotNull(redirectPath);
        Assert.StartsWith("/conversation/session/", redirectPath);

        var sessionId = doc.RootElement.GetProperty("session").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
        Assert.Equal($"/conversation/session/{sessionId}", redirectPath);

        // Verify the session was actually persisted with the speaking
        // subtest + role-play task type — that proves we routed through
        // ConversationService.CreateSessionAsync rather than fabricating
        // the response.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var session = await db.ConversationSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        Assert.NotNull(session);
        Assert.Equal("speaking", session!.SubtestCode);
        Assert.Equal("oet-roleplay", session.TaskTypeCode);
        Assert.Equal("st-001", session.ContentId);
        using var scenario = JsonDocument.Parse(session.ScenarioJson);
        Assert.Equal("st-001", scenario.RootElement.GetProperty("contentId").GetString());
        Assert.Equal("Patient Handover - Post-Op Recovery", scenario.RootElement.GetProperty("title").GetString());
        Assert.Contains("handover", scenario.RootElement.GetProperty("candidateBrief").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelfPractice_RejectsNonSpeakingTask()
    {
        using var client = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        // wt-001 is the seeded writing task (not speaking) — must 400.
        var resp = await client.PostAsJsonAsync("/v1/speaking/tasks/wt-001/self-practice", new { });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SelfPractice_ReturnsNotFoundForUnknownTask()
    {
        using var client = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var resp = await client.PostAsJsonAsync("/v1/speaking/tasks/does-not-exist/self-practice", new { });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
