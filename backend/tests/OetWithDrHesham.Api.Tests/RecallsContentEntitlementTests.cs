using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Recalls entitlement contract after "Free Preview Recalls" (owner directive
/// 2026-07-09). The personal recalls surfaces (today / queue / library) are OPEN
/// to every logged-in learner, with content scoped to free-preview terms for
/// non-premium learners (locked content never leaks). Only the weekly report and
/// the AI revision plan stay paywalled behind an active, non-frozen subscription.
/// Mirrors <see cref="RecallsAudioEntitlementTests"/>.
/// </summary>
public class RecallsContentEntitlementTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("/v1/recalls/today")]
    [InlineData("/v1/recalls/queue?limit=10")]
    [InlineData("/v1/recalls/library")]
    public async Task Open_surfaces_are_available_without_subscription(string route)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: false);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync(route);

        // No paywall: free learners reach these surfaces (content is preview-scoped
        // server-side). Expect 200 — assert only that it is never a 402 upsell.
        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Theory]
    [InlineData("/v1/recalls/report/week")]
    [InlineData("/v1/recalls/revision-plan")]
    public async Task Paywalled_surfaces_return_402_without_subscription(string route)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: false);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("enrolment_required", payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/v1/recalls/report/week")]
    [InlineData("/v1/recalls/revision-plan")]
    public async Task Paywalled_surfaces_return_402_for_frozen_subscriber(string route)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true, isFrozen: true);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("enrolment_required", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paywalled_surface_passes_for_active_subscriber()
    {
        // Weekly report is pure SQL (no AI) — safe to assert the gate is cleared.
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync("/v1/recalls/report/week");

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task Paywalled_surface_bypassed_for_admin()
    {
        var adminId = $"admin-{Guid.NewGuid():N}";
        await SeedLearnerAsync(adminId, hasActiveSubscription: false);

        using var client = CreateAdminClient(adminId);
        var response = await client.GetAsync("/v1/recalls/report/week");

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("enrolment_required", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Free_learner_sees_only_free_preview_content_in_queue_and_library()
    {
        // Defense-in-depth: even if a non-preview card exists in a non-premium
        // learner's list (e.g. lapsed subscriber), it must never surface in the
        // queue or library — those reads are scoped to free-preview terms.
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: false);
        await SeedDueCardAsync(learnerId, "anaemiapreview", isFreePreview: true);
        await SeedDueCardAsync(learnerId, "anaemialocked", isFreePreview: false);

        using var client = CreateLearnerClient(learnerId);

        var library = await client.GetAsync("/v1/recalls/library");
        library.EnsureSuccessStatusCode();
        var libraryBody = await library.Content.ReadAsStringAsync();
        Assert.Contains("anaemiapreview", libraryBody, StringComparison.Ordinal);
        Assert.DoesNotContain("anaemialocked", libraryBody, StringComparison.Ordinal);

        var queue = await client.GetAsync("/v1/recalls/queue?limit=10");
        queue.EnsureSuccessStatusCode();
        var queueBody = await queue.Content.ReadAsStringAsync();
        Assert.Contains("anaemiapreview", queueBody, StringComparison.Ordinal);
        Assert.DoesNotContain("anaemialocked", queueBody, StringComparison.Ordinal);
    }

    private async Task<string> SeedDueCardAsync(string learnerId, string term, bool isFreePreview)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var termId = $"term-{Guid.NewGuid():N}";

        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = termId,
            Term = term,
            Definition = "A test definition.",
            ExampleSentence = "A test sentence.",
            ExamTypeCode = "oet",
            ProfessionId = "medicine",
            Category = "spelling",
            Status = "active",
            IsFreePreview = isFreePreview,
            RecallSetCodesJson = "[\"2026\"]",
            CreatedAt = now,
            UpdatedAt = now
        });

        db.LearnerVocabularies.Add(new LearnerVocabulary
        {
            Id = Guid.NewGuid(),
            UserId = learnerId,
            TermId = termId,
            Mastery = "new",
            NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AddedAt = now
        });

        await db.SaveChangesAsync();
        return termId;
    }

    private HttpClient CreateLearnerClient(string learnerId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{learnerId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Profession", "medicine");
        return client;
    }

    private HttpClient CreateAdminClient(string adminId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", adminId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{adminId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", adminId);
        client.DefaultRequestHeaders.Add("X-Debug-Profession", "medicine");
        return client;
    }

    private async Task SeedLearnerAsync(string learnerId, bool hasActiveSubscription, bool isFrozen = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;

        db.Users.Add(new LearnerUser
        {
            Id = learnerId,
            DisplayName = learnerId,
            Email = $"{learnerId}@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            Timezone = "UTC",
            Locale = "en-AU",
            CreatedAt = now,
            LastActiveAt = now
        });

        if (hasActiveSubscription)
        {
            const string planCode = "premium-monthly-recalls-content-test";
            if (!db.BillingPlans.Any(plan => plan.Id == planCode || plan.Code == planCode))
            {
                db.BillingPlans.Add(new BillingPlan
                {
                    Id = planCode,
                    Code = planCode,
                    Name = "Premium monthly (recalls content test)",
                    EntitlementsJson = "{}",
                    IncludedSubtestsJson = "[]",
                    Status = BillingPlanStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            db.Subscriptions.Add(new Subscription
            {
                Id = $"sub-{Guid.NewGuid():N}",
                UserId = learnerId,
                PlanId = planCode,
                Status = SubscriptionStatus.Active,
                StartedAt = now,
                ChangedAt = now,
                NextRenewalAt = now.AddDays(30),
                PriceAmount = 49.99m,
                Currency = "AUD",
                Interval = "monthly"
            });
        }

        if (isFrozen)
        {
            db.AccountFreezeRecords.Add(new AccountFreezeRecord
            {
                Id = $"freeze-{Guid.NewGuid():N}",
                UserId = learnerId,
                Status = FreezeStatus.Active,
                IsCurrent = true,
                RequestedAt = now,
                StartedAt = now,
                DurationDays = 30,
                Reason = "Test freeze",
                PolicySnapshotJson = "{}",
                EligibilitySnapshotJson = "{}"
            });
        }

        await db.SaveChangesAsync();
    }
}
