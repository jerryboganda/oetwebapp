using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

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

    // ── Governed v1.1 Writing submit/grade flow ──────────────────────────
    // The legacy attempt-based AI grading routes are retired (409
    // writing_v11_required); these tests exercise the supported
    // /v1/writing/submissions/ flow end to end against the deterministic
    // test AI provider.

    // Shared grading prerequisites (scenario + case notes + approved model
    // answer + assessment pack + calibration gates), seeded once by fixed id.
    private static readonly Guid V11ScenarioId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private const string V11LetterContent =
        "Dear Dr Green, I am writing to request your review of Mrs Vance, a 68-year-old woman recovering well after right knee replacement. " +
        "Her wound is clean and dry, observations are stable, and physiotherapy is progressing. She reports mild swelling in the evenings. " +
        "Current medication includes paracetamol and apixaban as charted. Past history includes hypertension and osteoarthritis. " +
        "She lives alone with family nearby and mobilises with a frame. Please review her in six weeks with repeat bloods. " +
        "Thank you for your ongoing care of this patient.";

    private async Task EnsureV11GradingPrerequisitesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        // Platform AI budget: the gateway refuses every provider call when no
        // global policy row exists (ai_platform_budget_exhausted). Mirror the
        // production seed so grading can run in the test host.
        if (!await db.AiGlobalPolicies.AnyAsync(p => p.Id == "global"))
        {
            db.AiGlobalPolicies.Add(new AiGlobalPolicy
            {
                Id = "global",
                KillSwitchEnabled = false,
                MonthlyBudgetUsd = 10m,
                SoftWarnPct = 80,
                HardKillPct = 100,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        if (await db.WritingScenarios.AnyAsync(s => s.Id == V11ScenarioId))
        {
            await db.SaveChangesAsync();
            return;
        }

        db.WritingScenarios.Add(new WritingScenario
        {
            Id = V11ScenarioId,
            Title = "V11 E2E Referral",
            InternalCode = "E2E-WR-V11",
            Profession = "medicine",
            LetterType = "LT-RR",
            Difficulty = 3,
            TopicsJson = "[]",
            TaskPromptMarkdown = "Write to Dr Green requesting review of Mrs Vance following knee replacement.",
            WordGuideMin = 180,
            WordGuideMax = 200,
            Status = "published",
            Version = 1,
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.WritingScenarioStructuredSentences.AddRange(
            new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = V11ScenarioId,
                Ordinal = 1,
                SentenceText = "Diagnosis: osteoarthritis, right knee, post replacement.",
                RelevanceLabel = "relevant",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = V11ScenarioId,
                Ordinal = 2,
                SentenceText = "Plan: routine GP review in six weeks with repeat bloods.",
                RelevanceLabel = "relevant",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
        {
            Id = Guid.NewGuid(),
            ScenarioId = V11ScenarioId,
            Status = WritingAssessmentModelAnswerStatus.Ready,
            IsCandidateVisible = true,
            ModelAnswerText = "Dear Dr Green, Re: Mrs Vance. Thank you for the referral. ...",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.WritingAssessmentPackVersions.Add(new WritingAssessmentPackVersion
        {
            Id = Guid.NewGuid(),
            Profession = "medicine",
            LetterType = "routine_referral",
            VersionKey = "e2e-medicine-core",
            Status = WritingAssessmentReleaseStatus.Approved,
            CandidateFacing = true,
        });
        // The resolved grading model depends on gateway routing; seed an
        // approved gate for every plausible candidate so the candidate grade
        // is released regardless of which one resolves in this environment.
        foreach (var model in new[] { "claude-sonnet-5", "mock", "glm-5", "gpt-5.5-medium" })
        {
            db.WritingAssessmentReleaseGates.Add(new WritingAssessmentReleaseGate
            {
                Id = Guid.NewGuid(),
                ModelVersion = model,
                CalibrationSetVersion = "unreleased",
                Status = WritingAssessmentReleaseStatus.Approved,
                CandidateNumericScoreEnabled = true,
                MeanAbsoluteError = 1.0m,
                ContentConcisenessCorrelation = 0.9m,
                LanguageCorrelation = 0.5m,
                InventedClaimRate = 0m,
                OwnerApprovedTolerance = 5.0m,
                QualifiedReviewerCount = 1,
                HumanRatingsPerBenchmark = 2,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SubmitV11LetterAsync(HttpClient client, string letterContent)
    {
        var submitResponse = await client.PostAsJsonAsync("/v1/writing/submissions/", new
        {
            scenarioId = V11ScenarioId,
            mode = "practice",
            letterContent,
            wordCount = 140,
            timeSpentSeconds = 2400,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        var body = await submitResponse.Content.ReadAsStringAsync();
        Assert.True(submitResponse.IsSuccessStatusCode, $"Submit failed {(int)submitResponse.StatusCode}: {body}");
        using var submitJson = JsonDocument.Parse(body);
        return submitJson.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpClient> CreateClientForUserWithoutAiCreditsAsync(string userId)
    {
        // Learner profile only: no AI credit grant, so grading refuses with
        // 402 ai_credits_insufficient (the v1.1 entitlement signal).
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId, "nursing");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private async Task GrantWritingPackageAsync(string userId, int writingCredits)
    {
        // The grading credit reservation reads the package-credit snapshot
        // (AiPackageCreditTransactions), not the promo AI ledger — grant a
        // writing package so ReserveWritingAsync succeeds.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var credit = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var now = DateTimeOffset.UtcNow;
        await credit.GrantPackageAsync(
            userId,
            new BillingAddOn
            {
                Id = $"addon-writing-{userId}",
                Code = "pkg_writing_starter",
                Name = "Writing tests",
                Price = 1m,
                Currency = "GBP",
                Interval = "one_time",
                Status = BillingAddOnStatus.Active,
                DurationDays = 30,
                GrantCredits = 0,
                GrantEntitlementsJson = $$"""{"package_type":"writing","writing_only_credits":{{writingCredits}}}""",
                AddonKind = "ai_package",
                AppliesToAllPlans = true,
                IsStackable = true,
                QuantityStep = 1,
                CreatedAt = now,
                UpdatedAt = now,
            },
            1,
            $"cs-writing-{Guid.NewGuid():N}",
            null,
            CancellationToken.None);
    }

    private async Task<HttpClient> CreateGradedClientAsync(string userId)
    {
        // Both credit systems the grading path checks: the promo AI ledger
        // (gateway debit) and the package snapshot (credit reservation).
        var client = await CreateClientForUserAsync(userId, walletCredits: 0);
        await _factory.EnsureAiCreditsAsync(userId);
        await GrantWritingPackageAsync(userId, 5);
        return client;
    }

    [Fact]
    public async Task WritingSubmission_QueuesAndCompletesEvaluation()
    {
        await EnsureV11GradingPrerequisitesAsync();
        var userId = $"writing-v11-{Guid.NewGuid():N}";
        using var client = await CreateGradedClientAsync(userId);

        var submitResponse = await client.PostAsJsonAsync("/v1/writing/submissions/", new
        {
            scenarioId = V11ScenarioId,
            mode = "practice",
            letterContent = V11LetterContent,
            wordCount = 140,
            timeSpentSeconds = 2400,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        Assert.True(submitResponse.IsSuccessStatusCode, $"Submit failed {(int)submitResponse.StatusCode}: {submitBody}");
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);

        using var submitJson = JsonDocument.Parse(submitBody);
        var submissionId = submitJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(V11ScenarioId, submitJson.RootElement.GetProperty("scenarioId").GetGuid());
        Assert.False(submitJson.RootElement.GetProperty("isRevision").GetBoolean());

        // Grading runs inline: the deterministic test provider returns
        // purpose 2 + 5s (raw total 27), released to the candidate by the
        // seeded calibration gate.
        var gradeResponse = await client.GetAsync($"/v1/writing/submissions/{submissionId}/grade");
        gradeResponse.EnsureSuccessStatusCode();
        using var gradeJson = JsonDocument.Parse(await gradeResponse.Content.ReadAsStringAsync());
        Assert.Equal(27, gradeJson.RootElement.GetProperty("rawTotal").GetInt32());
        Assert.Equal(2, gradeJson.RootElement.GetProperty("c1Purpose").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(gradeJson.RootElement.GetProperty("modelUsed").GetString()));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var submission = await db.WritingSubmissions.SingleAsync(x => x.Id == submissionId);
        Assert.Equal("graded", submission.Status);
        Assert.True(await db.WritingGrades.AnyAsync(x => x.SubmissionId == submissionId));
    }

    [Fact]
    public async Task WritingSubmission_RequiresAiCredits()
    {
        await EnsureV11GradingPrerequisitesAsync();
        var userId = $"writing-nocredits-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserWithoutAiCreditsAsync(userId);

        var submitResponse = await client.PostAsJsonAsync("/v1/writing/submissions/", new
        {
            scenarioId = V11ScenarioId,
            mode = "practice",
            letterContent = V11LetterContent,
            wordCount = 140,
            timeSpentSeconds = 2400,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.PaymentRequired, submitResponse.StatusCode);
        using var errorJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        Assert.Equal("ai_credits_insufficient", errorJson.RootElement.GetProperty("code").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        // The refused submission row persists ungraded (no-charge-on-
        // failure); no grade is ever produced for it.
        var submission = await db.WritingSubmissions.SingleOrDefaultAsync(x => x.UserId == userId);
        Assert.NotNull(submission);
        Assert.NotEqual("graded", submission!.Status);
        Assert.False(await db.WritingGrades.AnyAsync(x => x.SubmissionId == submission.Id));
    }

    [Fact]
    public async Task WritingSubmission_SecondCreateWhileLocked_Conflicts()
    {
        // The §17.7 submission lock: a second fresh submit for the same
        // learner+scenario while one is locked must conflict; revisions are
        // the supported retry path.
        await EnsureV11GradingPrerequisitesAsync();
        var userId = $"writing-lock-{Guid.NewGuid():N}";
        using var client = await CreateGradedClientAsync(userId);
        await SubmitV11LetterAsync(client, V11LetterContent);

        var secondResponse = await client.PostAsJsonAsync("/v1/writing/submissions/", new
        {
            scenarioId = V11ScenarioId,
            mode = "practice",
            letterContent = V11LetterContent + " Additional sentence to vary the payload.",
            wordCount = 145,
            timeSpentSeconds = 2400,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        using var json = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal("writing_submission_locked", json.RootElement.GetProperty("code").GetString());
    }

    private const string V11RevisionContent =
        "Dear Dr Green, I am writing to revise my earlier referral for Mrs Vance following her knee replacement review. " +
        "Her recovery remains satisfactory with a clean wound and stable observations, though evening swelling persists. " +
        "Physiotherapy continues and she mobilises with a frame at home with family support nearby. " +
        "Medication is unchanged. I would appreciate an earlier review in four weeks with repeat bloods given the swelling. " +
        "Thank you for your continued care of this patient.";

    [Fact]
    public async Task WritingRevision_QueuesLinkedEvaluation()
    {
        await EnsureV11GradingPrerequisitesAsync();
        var userId = $"writing-revision-{Guid.NewGuid():N}";
        using var client = await CreateGradedClientAsync(userId);
        var submissionId = await SubmitV11LetterAsync(client, V11LetterContent);

        var response = await client.PostAsJsonAsync($"/v1/writing/submissions/{submissionId}/revise", new
        {
            letterContent = V11RevisionContent,
            wordCount = 130,
            timeSpentSeconds = 1200
        });

        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var revisionId = json.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(submissionId, revisionId);
        Assert.True(json.RootElement.GetProperty("isRevision").GetBoolean());
        Assert.Equal(submissionId, json.RootElement.GetProperty("originalSubmissionId").GetGuid());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var revision = await db.WritingSubmissions.SingleAsync(x => x.Id == revisionId);
        Assert.Equal(userId, revision.UserId);
        Assert.True(revision.IsRevision);
        Assert.Equal(submissionId, revision.OriginalSubmissionId);
        Assert.Equal("graded", revision.Status);
        Assert.True(await db.WritingGrades.AnyAsync(x => x.SubmissionId == revisionId));
    }

    [Fact]
    public async Task WritingRevision_RapidDuplicate_IsRejectedWithoutDuplicateWorkflow()
    {
        // Double-tap protection is layered: the AiScoring edge limiter (2/min)
        // rejects an immediate duplicate revise with 429 before any grading
        // workflow starts, so no duplicate paid workflow and no duplicate row
        // can result. Same-content idempotency below the edge is covered at
        // the pipeline level (derived idempotency key + content dedup).
        await EnsureV11GradingPrerequisitesAsync();
        var userId = $"writing-revision-idem-{Guid.NewGuid():N}";
        using var client = await CreateGradedClientAsync(userId);
        var submissionId = await SubmitV11LetterAsync(client, V11LetterContent);
        var body = new
        {
            letterContent = V11RevisionContent,
            wordCount = 130,
            timeSpentSeconds = 1200
        };

        var firstResponse = await client.PostAsJsonAsync($"/v1/writing/submissions/{submissionId}/revise", body);
        firstResponse.EnsureSuccessStatusCode();
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var revisionId = firstJson.RootElement.GetProperty("id").GetGuid();

        var secondResponse = await client.PostAsJsonAsync($"/v1/writing/submissions/{submissionId}/revise", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(1, await db.WritingSubmissions.CountAsync(x => x.OriginalSubmissionId == submissionId));
        Assert.True(await db.WritingGrades.AnyAsync(x => x.SubmissionId == revisionId));
    }

    [Fact]
    public async Task WritingRevision_RejectsNonWritingAttempt()
    {
        await EnsureV11GradingPrerequisitesAsync();
        var userId = $"writing-revision-nonwriting-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 0);

        var unknownId = Guid.NewGuid();
        var reviseResponse = await client.PostAsJsonAsync($"/v1/writing/submissions/{unknownId}/revise", new
        {
            letterContent = "This should not be accepted as a Writing revision.",
            wordCount = 12,
            timeSpentSeconds = 60
        });
        var gradeResponse = await client.GetAsync($"/v1/writing/submissions/{unknownId}/grade");

        Assert.Equal(HttpStatusCode.NotFound, reviseResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gradeResponse.StatusCode);
    }

    [Fact]
    public async Task LegacyWritingRevisionRoute_ReturnsGovernedFlowConflict()
    {
        // The legacy attempt-based revision route is retired: it must refuse
        // with the governed-flow signal instead of silently accepting.
        var userId = $"writing-revision-legacy-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 0);
        var baseAttemptId = await SeedCompletedWritingAttemptAsync(userId);

        var response = await client.PostAsJsonAsync($"/v1/writing/revisions/{baseAttemptId}/submit", new
        {
            content = "Dear Dr Patterson, I am trying to revise via the retired route.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing_v11_required", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReviewRequest_DeductsCredits_WhenPayingWithCredits()
    {
        var userId = $"review-credit-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId, walletCredits: 2);
        var attemptId = await CreateCompletedWritingAttemptAsync(userId);

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
        var attemptId = await CreateCompletedWritingAttemptAsync(userId);
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
        // Seed content used here (wt-001 writing) is nursing-scoped; the
        // Master Catalogue profession-isolation gate 404s otherwise.
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId, "nursing");
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

    private async Task<string> CreateCompletedWritingAttemptAsync(string userId)
    {
        // Seed directly: the legacy attempt-submit HTTP route is retired (409
        // writing_v11_required), but reviews attach to completed attempts, so
        // insert a completed attempt row for the user instead of driving it.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attemptId = $"wa-{Guid.NewGuid():N}";
        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = userId,
            ContentId = "wt-001",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "timed",
            State = AttemptState.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-45),
            SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            DraftContent = "Dear Dr Patterson, completed seed letter.",
            DraftVersion = 1,
            LastClientSyncAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        });
        await db.SaveChangesAsync();
        return attemptId;
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

}
