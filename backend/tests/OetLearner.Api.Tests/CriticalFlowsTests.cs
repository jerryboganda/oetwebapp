using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class CriticalFlowsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CriticalFlowsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BootstrapEndpoint_ReturnsLearnerProfileAndReferences()
    {
        var response = await _client.GetAsync("/v1/me/bootstrap");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mock-user-001", json.RootElement.GetProperty("user").GetProperty("userId").GetString());
        Assert.True(json.RootElement.GetProperty("reference").GetProperty("professions").GetArrayLength() > 0);
    }

    [Fact]
    public async Task WritingSubmission_QueuesAndCompletesEvaluation()
    {
        var createAttemptResponse = await _client.PostAsJsonAsync("/v1/writing/attempts", new
        {
            contentId = "wt-001",
            context = "practice",
            mode = "timed",
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        createAttemptResponse.EnsureSuccessStatusCode();

        using var createdAttemptJson = JsonDocument.Parse(await createAttemptResponse.Content.ReadAsStringAsync());
        var attemptId = createdAttemptJson.RootElement.GetProperty("attemptId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(attemptId));

        var draftResponse = await _client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = "Dear Dr Patterson, I am writing to update you regarding Mrs Vance after her knee replacement. She recovered well post-operatively and needs staple removal in 14 days.",
            scratchpad = "Focus on conciseness.",
            checklist = new Dictionary<string, bool> { ["Addressed the purpose clearly"] = true },
            draftVersion = 1
        });
        draftResponse.EnsureSuccessStatusCode();

        var submitResponse = await _client.PostAsJsonAsync($"/v1/writing/attempts/{attemptId}/submit", new { content = "Dear Dr Patterson, I am writing to update you regarding Mrs Vance after her knee replacement. She recovered well post-operatively and needs staple removal in 14 days.", idempotencyKey = Guid.NewGuid().ToString("N") });
        submitResponse.EnsureSuccessStatusCode();

        using var submitJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        var evaluationId = submitJson.RootElement.GetProperty("evaluationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(evaluationId));

        JsonDocument? summaryJson = null;
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            var summaryResponse = await _client.GetAsync($"/v1/writing/evaluations/{evaluationId}/summary");
            summaryResponse.EnsureSuccessStatusCode();
            summaryJson?.Dispose();
            summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
            if (summaryJson.RootElement.GetProperty("state").GetString() == "completed")
            {
                break;
            }
        }

        Assert.NotNull(summaryJson);
        Assert.Equal("completed", summaryJson!.RootElement.GetProperty("state").GetString());
        var scoreRange = summaryJson.RootElement.GetProperty("scoreRange").GetString();
        Assert.False(string.IsNullOrWhiteSpace(scoreRange));
    }

    [Fact]
    public async Task ReviewRequest_DeductsCredits_WhenPayingWithCredits()
    {
        var userId = $"review-credit-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 2);
        var attemptId = await CreateCompletedWritingAttemptAsync(client);

        var billingBeforeResponse = await client.GetAsync("/v1/billing/summary");
        billingBeforeResponse.EnsureSuccessStatusCode();
        using var billingBefore = JsonDocument.Parse(await billingBeforeResponse.Content.ReadAsStringAsync());
        var creditsBefore = billingBefore.RootElement.GetProperty("wallet").GetProperty("creditBalance").GetInt32();

        var requestResponse = await client.PostAsJsonAsync("/v1/reviews/requests", new
        {
            attemptId,
            subtest = "writing",
            turnaroundOption = "standard",
            focusAreas = new[] { "conciseness", "genre" },
            learnerNotes = "Please focus on tone and detail selection.",
            paymentSource = "credits",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        requestResponse.EnsureSuccessStatusCode();

        var billingAfterResponse = await client.GetAsync("/v1/billing/summary");
        billingAfterResponse.EnsureSuccessStatusCode();
        using var billingAfter = JsonDocument.Parse(await billingAfterResponse.Content.ReadAsStringAsync());
        var creditsAfter = billingAfter.RootElement.GetProperty("wallet").GetProperty("creditBalance").GetInt32();

        Assert.Equal(creditsBefore - 1, creditsAfter);
    }

    [Fact]
    public async Task ReviewRequest_IsIdempotent_ForDuplicateSubmission()
    {
        var userId = $"review-idempotent-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 2);
        var attemptId = await CreateCompletedWritingAttemptAsync(client);
        var key = Guid.NewGuid().ToString("N");

        var billingBeforeResponse = await client.GetAsync("/v1/billing/summary");
        billingBeforeResponse.EnsureSuccessStatusCode();
        using var billingBefore = JsonDocument.Parse(await billingBeforeResponse.Content.ReadAsStringAsync());
        var creditsBefore = billingBefore.RootElement.GetProperty("wallet").GetProperty("creditBalance").GetInt32();

        var firstResponse = await client.PostAsJsonAsync("/v1/reviews/requests", new
        {
            attemptId,
            subtest = "writing",
            turnaroundOption = "standard",
            focusAreas = new[] { "conciseness" },
            learnerNotes = "Please focus on conciseness.",
            paymentSource = "credits",
            idempotencyKey = key
        });
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync("/v1/reviews/requests", new
        {
            attemptId,
            subtest = "writing",
            turnaroundOption = "standard",
            focusAreas = new[] { "conciseness" },
            learnerNotes = "Please focus on conciseness.",
            paymentSource = "credits",
            idempotencyKey = key
        });
        secondResponse.EnsureSuccessStatusCode();

        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());

        Assert.Equal(
            firstJson.RootElement.GetProperty("reviewRequestId").GetString(),
            secondJson.RootElement.GetProperty("reviewRequestId").GetString());

        var billingAfterResponse = await client.GetAsync("/v1/billing/summary");
        billingAfterResponse.EnsureSuccessStatusCode();
        using var billingAfter = JsonDocument.Parse(await billingAfterResponse.Content.ReadAsStringAsync());
        var creditsAfter = billingAfter.RootElement.GetProperty("wallet").GetProperty("creditBalance").GetInt32();

        Assert.Equal(creditsBefore - 1, creditsAfter);
    }

    private async Task<HttpClient> CreateClientForUserAsync(string userId, int walletCredits)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId);
            wallet.CreditBalance = walletCredits;
            wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private static async Task<string> CreateCompletedWritingAttemptAsync(HttpClient client)
    {
        var createAttemptResponse = await client.PostAsJsonAsync("/v1/writing/attempts", new
        {
            contentId = "wt-001",
            context = "practice",
            mode = "timed",
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        createAttemptResponse.EnsureSuccessStatusCode();

        using var createdAttemptJson = JsonDocument.Parse(await createAttemptResponse.Content.ReadAsStringAsync());
        var attemptId = createdAttemptJson.RootElement.GetProperty("attemptId").GetString()
            ?? throw new InvalidOperationException("Writing attempt id was missing.");

        var content = "Dear Dr Patterson, I am writing to update you regarding Mrs Vance after her knee replacement. She recovered well post-operatively and needs staple removal in 14 days.";
        var draftResponse = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content,
            scratchpad = "Focus on conciseness.",
            checklist = new Dictionary<string, bool> { ["Addressed the purpose clearly"] = true },
            draftVersion = 1
        });
        draftResponse.EnsureSuccessStatusCode();

        var submitResponse = await client.PostAsJsonAsync($"/v1/writing/attempts/{attemptId}/submit", new
        {
            content,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        submitResponse.EnsureSuccessStatusCode();

        using var submitJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        var evaluationId = submitJson.RootElement.GetProperty("evaluationId").GetString()
            ?? throw new InvalidOperationException("Writing evaluation id was missing.");

        for (var poll = 0; poll < 20; poll++)
        {
            var summaryResponse = await client.GetAsync($"/v1/writing/evaluations/{evaluationId}/summary");
            summaryResponse.EnsureSuccessStatusCode();
            using var summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
            if (summaryJson.RootElement.GetProperty("state").GetString() == "completed")
            {
                return attemptId;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Timed out waiting for writing evaluation to complete.");
    }
}
