using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

public class CriticalFlowsTests : IClassFixture<SeededTestWebApplicationFactory>
{
    private readonly SeededTestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CriticalFlowsTests(SeededTestWebApplicationFactory factory)
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
        // Grant AI credits to the default dev-auth user before kicking off
        // the V1 Writing submit path. The AI gateway debits credits for the
        // WritingGrade feature and refuses the call when the user has zero
        // balance (ai_credits_insufficient), which would mark the evaluation
        // Failed instead of Completed.
        await _factory.EnsureAiCreditsAsync("mock-user-001");

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

        // Drive the background job pipeline deterministically — the hosted
        // service is stripped from the test host so we drain it manually
        // before polling for the completed evaluation state.
        await _factory.DrainBackgroundJobsAsync();

        JsonDocument? summaryJson = null;
        for (var i = 0; i < 6; i++)
        {
            var summaryResponse = await _client.GetAsync($"/v1/writing/evaluations/{evaluationId}/summary");
            summaryResponse.EnsureSuccessStatusCode();
            summaryJson?.Dispose();
            summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
            if (summaryJson.RootElement.GetProperty("state").GetString() == "completed")
            {
                break;
            }
            await _factory.DrainBackgroundJobsAsync();
        }

        Assert.NotNull(summaryJson);
        Assert.Equal("completed", summaryJson!.RootElement.GetProperty("state").GetString());
        var scoreRange = summaryJson.RootElement.GetProperty("scoreRange").GetString();
        Assert.False(string.IsNullOrWhiteSpace(scoreRange));
    }

    [Fact]
    public async Task WritingRevision_QueuesLinkedEvaluation()
    {
        var userId = $"writing-revision-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 0);
        var baseAttemptId = await SeedCompletedWritingAttemptAsync(userId);

        var response = await client.PostAsJsonAsync($"/v1/writing/revisions/{baseAttemptId}/submit", new
        {
            content = "Dear Dr Patterson, I am writing to revise the referral with a clearer purpose and concise follow-up request.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var revisionAttemptId = json.RootElement.GetProperty("attemptId").GetString();
        var evaluationId = json.RootElement.GetProperty("evaluationId").GetString();

        Assert.StartsWith("wa-", revisionAttemptId);
        Assert.StartsWith("we-", evaluationId);
        Assert.Equal("queued", json.RootElement.GetProperty("state").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var revision = await db.Attempts.SingleAsync(x => x.Id == revisionAttemptId);
        Assert.Equal(baseAttemptId, revision.ParentAttemptId);
        Assert.Equal("writing", revision.SubtestCode);
        Assert.Equal(AttemptState.Evaluating, revision.State);
        Assert.True(await db.Evaluations.AnyAsync(x => x.Id == evaluationId && x.AttemptId == revisionAttemptId && x.State == AsyncState.Queued));
        Assert.True(await db.BackgroundJobs.AnyAsync(x => x.ResourceId == evaluationId && x.AttemptId == revisionAttemptId));
    }

    [Fact]
    public async Task WritingRevision_IsIdempotent_ForDuplicateSubmission()
    {
        var userId = $"writing-revision-idem-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 0);
        var baseAttemptId = await SeedCompletedWritingAttemptAsync(userId);
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var body = new
        {
            content = "Dear Dr Patterson, I have revised the letter while preserving the clinical facts and clarifying the request.",
            idempotencyKey
        };

        var firstResponse = await client.PostAsJsonAsync($"/v1/writing/revisions/{baseAttemptId}/submit", body);
        var secondResponse = await client.PostAsJsonAsync($"/v1/writing/revisions/{baseAttemptId}/submit", body);

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var revisionAttemptId = firstJson.RootElement.GetProperty("attemptId").GetString();

        Assert.Equal(revisionAttemptId, secondJson.RootElement.GetProperty("attemptId").GetString());
        Assert.Equal(
            firstJson.RootElement.GetProperty("evaluationId").GetString(),
            secondJson.RootElement.GetProperty("evaluationId").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(1, await db.Attempts.CountAsync(x => x.ParentAttemptId == baseAttemptId));
    }

    [Fact]
    public async Task WritingRevision_RejectsNonWritingAttempt()
    {
        var userId = $"writing-revision-nonwriting-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 0);
        var readingAttemptId = await SeedCompletedNonWritingAttemptAsync(userId);

        var getResponse = await client.GetAsync($"/v1/writing/revisions/{readingAttemptId}");
        var submitResponse = await client.PostAsJsonAsync($"/v1/writing/revisions/{readingAttemptId}/submit", new
        {
            content = "This should not be accepted as a Writing revision.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, submitResponse.StatusCode);
    }

    [Fact]
    public async Task WritingRevision_RequiresCompletedFeedback()
    {
        var userId = $"writing-revision-not-ready-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 0);
        var baseAttemptId = await SeedCompletedWritingAttemptAsync(userId, includeEvaluation: false);

        var response = await client.PostAsJsonAsync($"/v1/writing/revisions/{baseAttemptId}/submit", new
        {
            content = "Dear Dr Patterson, I am trying to revise before feedback is ready.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task WritingRevision_RequiresWritingEntitlement()
    {
        var userId = $"writing-revision-free-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 0);
        var baseAttemptId = await SeedCompletedWritingAttemptAsync(userId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.Subscriptions.RemoveRange(db.Subscriptions.Where(subscription => subscription.UserId == userId));
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync($"/v1/writing/revisions/{baseAttemptId}/submit", new
        {
            content = "Dear Dr Patterson, I am writing to revise this letter without an active subscription.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.False(await verifyDb.Attempts.AnyAsync(x => x.ParentAttemptId == baseAttemptId));
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
        await _factory.EnsureAiCreditsAsync(userId);
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

    private async Task<string> CreateCompletedWritingAttemptAsync(HttpClient client)
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

        // Drive the background job pipeline deterministically — the hosted
        // service tick is stripped in tests, so we drain the queue manually.
        await _factory.DrainBackgroundJobsAsync();

        for (var poll = 0; poll < 20; poll++)
        {
            var summaryResponse = await client.GetAsync($"/v1/writing/evaluations/{evaluationId}/summary");
            summaryResponse.EnsureSuccessStatusCode();
            using var summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
            if (summaryJson.RootElement.GetProperty("state").GetString() == "completed")
            {
                return attemptId;
            }

            await _factory.DrainBackgroundJobsAsync();
        }

        throw new TimeoutException("Timed out waiting for writing evaluation to complete.");
    }

    private async Task<string> SeedCompletedWritingAttemptAsync(string userId, bool includeEvaluation = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempt = new Attempt
        {
            Id = $"wa-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = "wt-001",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "timed",
            State = AttemptState.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-45),
            SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            DraftContent = "Dear Dr Patterson, I am writing to update you regarding Mrs Vance after her knee replacement.",
            DraftVersion = 1,
            LastClientSyncAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        db.Attempts.Add(attempt);
        if (includeEvaluation)
        {
            db.Evaluations.Add(new Evaluation
            {
                Id = $"we-{Guid.NewGuid():N}",
                AttemptId = attempt.Id,
                SubtestCode = "writing",
                State = AsyncState.Completed,
                ScoreRange = "350-380",
                ConfidenceBand = ConfidenceBand.Medium,
                StrengthsJson = "[\"Clear purpose\"]",
                IssuesJson = "[\"Clarify the follow-up request.\"]",
                CriterionScoresJson = "[{\"criterionCode\":\"purpose\",\"scoreRange\":\"2\"}]",
                FeedbackItemsJson = "[]",
                GeneratedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                ModelExplanationSafe = "Completed seed evaluation.",
                LearnerDisclaimer = "Practice estimate only.",
                StatusReasonCode = "completed",
                StatusMessage = "Completed.",
                Retryable = false,
                LastTransitionAt = DateTimeOffset.UtcNow.AddMinutes(-3)
            });
        }
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    private async Task<string> SeedCompletedNonWritingAttemptAsync(string userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempt = new Attempt
        {
            Id = $"ra-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = "rt-001",
            SubtestCode = "reading",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-45),
            SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            DraftContent = "{}",
            DraftVersion = 1,
            LastClientSyncAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }
}
