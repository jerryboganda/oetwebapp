using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
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

    private sealed class AllowAllContentEntitlementService : IContentEntitlementService
    {
        public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.FromResult(new ContentEntitlementResult(true, "test", "premium", null));

        public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.CompletedTask;

        public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal) => false;
    }
}
