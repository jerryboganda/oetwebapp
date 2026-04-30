using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// Wave 7 of docs/SPEAKING-MODULE-PLAN.md - compliance polish.
//   - Non-owner (tutor) speaking-recording fetches must produce
//     exactly one AuditEvent row with Action =
//     "speaking_recording_accessed".
//   - The /v1/speaking/compliance endpoint must surface the
//     SpeakingComplianceOptions copy + retention window.
[Collection("AuthFlows")]
public class SpeakingComplianceFlowsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public SpeakingComplianceFlowsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExpertSpeakingAudio_Stream_WritesAuditRow()
    {
        using var client = _factory.CreateAuthenticatedClient(
            SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var claim = await client.PostAsync("/v1/expert/queue/review-queue-001/claim", content: null);
        claim.EnsureSuccessStatusCode();

        var beforeCount = await CountSpeakingRecordingAuditAsync("review-queue-001");

        var response = await client.GetAsync("/v1/expert/reviews/review-queue-001/speaking/audio");
        response.EnsureSuccessStatusCode();
        // Drain the stream so the request completes server-side before
        // we assert on the audit row.
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);

        var afterCount = await CountSpeakingRecordingAuditAsync("review-queue-001");
        Assert.Equal(beforeCount + 1, afterCount);
    }

    [Fact]
    public async Task ComplianceEndpoint_ReturnsCopyAndRetention()
    {
        using var client = _factory.CreateAuthenticatedClient(
            SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var resp = await client.GetAsync("/v1/speaking/compliance");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var consent = doc.RootElement.GetProperty("consentText").GetString();
        var disclaimer = doc.RootElement.GetProperty("scoreDisclaimer").GetString();
        var retention = doc.RootElement.GetProperty("audioRetentionDays").GetInt32();

        Assert.False(string.IsNullOrWhiteSpace(consent));
        Assert.False(string.IsNullOrWhiteSpace(disclaimer));
        Assert.True(retention > 0);
        // Default in SpeakingComplianceOptions.
        Assert.Equal(365, retention);
        Assert.Contains("Estimated score", disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComplianceEndpoint_RequiresAuth()
    {
        using var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/v1/speaking/compliance");
        // Endpoint is mapped under the v1 group with default auth — anon
        // requests should be rejected.
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private async Task<int> CountSpeakingRecordingAuditAsync(string reviewRequestId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "speaking_recording_accessed"
                        && a.ResourceId == reviewRequestId)
            .CountAsync();
    }
}
