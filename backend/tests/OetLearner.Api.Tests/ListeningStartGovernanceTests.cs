using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class ListeningStartGovernanceTests
{
    [Fact]
    public async Task StartAttempt_DoesNotDebitCreditWhenMarkingPolicyIsUnavailable()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = "governance-learner",
            AuthAccountId = "governance-auth",
            DisplayName = "Governance Learner",
            Email = "governance@example.test",
            Role = ApplicationUserRoles.Learner,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "governance-listening-paper",
            SubtestCode = "listening",
            Title = "Governance Listening Paper",
            Slug = "governance-listening-paper",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 45,
            Status = ContentStatus.Published,
            ExtractedTextJson = """
                {
                  "listeningQuestions": [
                    { "id": "q-1", "number": 1, "partCode": "A1", "type": "short_answer", "text": "Dose: ____", "correctAnswer": "five" }
                  ]
                }
                """,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.ListeningPolicies.Add(new ListeningPolicy
        {
            Id = "global",
            FullPaperTimerMinutes = 45,
            GracePeriodSeconds = 10,
        });
        await db.SaveChangesAsync();

        var credit = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await credit.GrantPackageAsync(
            "governance-learner",
            new BillingAddOn
            {
                Id = "governance-listening-addon",
                Code = "governance-listening-addon",
                Name = "Listening tests",
                Price = 1m,
                Currency = "GBP",
                Interval = "one_time",
                Status = BillingAddOnStatus.Active,
                DurationDays = 30,
                GrantCredits = 0,
                GrantEntitlementsJson = "{\"package_type\":\"listening\",\"listening_tests\":1}",
                AddonKind = "ai_package",
                AppliesToAllPlans = true,
                IsStackable = true,
                QuantityStep = 1,
                CreatedAt = now,
                UpdatedAt = now,
            },
            1,
            "cs-governance-listening",
            null,
            CancellationToken.None);

        var service = new ListeningLearnerService(
            db,
            new AllowAllContentEntitlementService(),
            aiPackageCreditService: credit);

        await Assert.ThrowsAsync<ApiException>(() => service.StartAttemptAsync(
            "governance-learner",
            "governance-listening-paper",
            "practice",
            null,
            forceNewAttempt: true,
            CancellationToken.None));

        var snapshot = await credit.GetSnapshotAsync("governance-learner", 20, CancellationToken.None);
        Assert.Equal(1, snapshot.ListeningTestsRemaining);
        Assert.Empty(await db.Attempts.ToListAsync());
    }

    [Fact]
    public async Task JsonBackedExam_ExposesServerDeadlineAndRejectsLateAnswerWrites()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = "deadline-learner",
            AuthAccountId = "deadline-auth",
            DisplayName = "Deadline Learner",
            Email = "deadline@example.test",
            Role = ApplicationUserRoles.Learner,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "deadline-listening-paper",
            SubtestCode = "listening",
            Title = "Deadline Listening Paper",
            Slug = "deadline-listening-paper",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 45,
            Status = ContentStatus.Published,
            ExtractedTextJson = """
                {
                  "listeningQuestions": [
                    { "id": "q-deadline", "number": 1, "partCode": "A1", "type": "short_answer", "text": "Dose: ____", "correctAnswer": "five" }
                  ]
                }
                """,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.AssessmentMarkingPolicyVersions.Add(new AssessmentMarkingPolicyVersion
        {
            Id = "listening-policy-deadline",
            Assessment = "listening",
            ScopeKey = "default",
            VersionKey = "deadline-v1",
            PolicyJson = new AssessmentMarkingPolicyDocument().Serialize(),
            Status = AssessmentGovernanceStatus.Effective,
            EffectiveFrom = now.AddMinutes(-1),
            CreatedByUserId = "owner",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new ListeningLearnerService(
            db,
            new AllowAllContentEntitlementService());
        var started = await service.StartAttemptAsync(
            "deadline-learner",
            "deadline-listening-paper",
            "exam",
            null,
            forceNewAttempt: true,
            CancellationToken.None);
        using var startedJson = JsonDocument.Parse(JsonSerializer.Serialize(started));
        Assert.Equal(JsonValueKind.String, startedJson.RootElement.GetProperty("expiresAt").ValueKind);

        var attempt = await db.Attempts.SingleAsync(a => a.UserId == "deadline-learner");
        attempt.PolicySnapshotJson = JsonSerializer.Serialize(new
        {
            deadlineAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApiException>(() => service.SaveAnswerAsync(
            "deadline-learner",
            attempt.Id,
            "q-deadline",
            new ListeningAnswerSaveRequest("five"),
            CancellationToken.None));

        var review = await service.SubmitAsync(
            "deadline-learner",
            attempt.Id,
            new Dictionary<string, string?> { ["q-deadline"] = "five" },
            CancellationToken.None);
        Assert.Contains("\"rawScore\":0", JsonSerializer.Serialize(review));
    }

    [Fact]
    public async Task JsonBackedExam_AppliesActiveListeningExtraTimeToAuthoritativeDeadline()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = "extra-time-learner",
            AuthAccountId = "extra-time-auth",
            DisplayName = "Extra Time Learner",
            Email = "extra-time@example.test",
            Role = ApplicationUserRoles.Learner,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "extra-time-listening-paper",
            SubtestCode = "listening",
            Title = "Extra Time Listening Paper",
            Slug = "extra-time-listening-paper",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 45,
            Status = ContentStatus.Published,
            ExtractedTextJson = """
                {
                  "listeningQuestions": [
                    { "id": "q-extra-time", "number": 1, "partCode": "A1", "type": "short_answer", "text": "Dose: ____", "correctAnswer": "five" }
                  ]
                }
                """,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.AssessmentMarkingPolicyVersions.Add(new AssessmentMarkingPolicyVersion
        {
            Id = "listening-policy-extra-time",
            Assessment = "listening",
            ScopeKey = "default",
            VersionKey = "extra-time-v1",
            PolicyJson = new AssessmentMarkingPolicyDocument().Serialize(),
            Status = AssessmentGovernanceStatus.Effective,
            EffectiveFrom = now.AddMinutes(-1),
            CreatedByUserId = "owner",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningPolicies.Add(new ListeningPolicy
        {
            Id = "global",
            FullPaperTimerMinutes = 45,
            GracePeriodSeconds = 10,
            DefaultExtraTimePct = 0,
        });
        db.ListeningUserPolicyOverrides.Add(new ListeningUserPolicyOverride
        {
            UserId = "extra-time-learner",
            ExtraTimeEntitlementPct = 20,
            ExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new ListeningLearnerService(
            db,
            new AllowAllContentEntitlementService());
        var started = await service.StartAttemptAsync(
            "extra-time-learner",
            "extra-time-listening-paper",
            "exam",
            null,
            forceNewAttempt: true,
            CancellationToken.None);

        using var startedJson = JsonDocument.Parse(JsonSerializer.Serialize(started));
        var expiresAt = startedJson.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange((expiresAt - now).TotalMinutes, 53.5, 55.5);

        var attempt = await db.Attempts.SingleAsync(a => a.UserId == "extra-time-learner");
        using var snapshot = JsonDocument.Parse(attempt.PolicySnapshotJson!);
        var listeningPolicy = snapshot.RootElement.GetProperty("listeningPolicy");
        Assert.Equal(54, listeningPolicy.GetProperty("fullPaperTimerMinutes").GetInt32());
        Assert.Equal(20, listeningPolicy.GetProperty("extraTimeEntitlementPct").GetInt32());

        var globalPolicy = await db.ListeningPolicies.SingleAsync(policy => policy.Id == "global");
        globalPolicy.AttemptsPerPaperPerUser = 1;
        await db.SaveChangesAsync();

        var capError = await Assert.ThrowsAsync<ApiException>(() => service.StartAttemptAsync(
            "extra-time-learner",
            "extra-time-listening-paper",
            "exam",
            null,
            forceNewAttempt: true,
            CancellationToken.None));
        Assert.Contains("attempt cap", capError.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AllowAllContentEntitlementService : IContentEntitlementService
    {
        public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.FromResult(new ContentEntitlementResult(true, "test", "premium", null));

        public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.CompletedTask;

        public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal) => false;
    }
}
