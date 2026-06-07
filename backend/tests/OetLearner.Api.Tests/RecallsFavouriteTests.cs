using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Favourites (reuse of the Star mechanism). A plain favourite must NOT require a
/// reason — the reason is optional difficulty metadata. These tests lock the
/// optional-reason contract on <c>POST /v1/recalls/star</c> so the frontend can
/// favourite a card with a single tap.
/// </summary>
public class RecallsFavouriteTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Star_without_reason_favourites_card_and_leaves_reason_null()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerWithSubscriptionAsync(learnerId);
        var cardId = await SeedVocabularyCardAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.PostAsJsonAsync("/v1/recalls/star", new
        {
            kind = "vocab",
            id = cardId.ToString(),
            starred = true,
            reason = (string?)null,
        });

        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var card = await db.LearnerVocabularies.SingleAsync(lv => lv.Id == cardId);
        Assert.True(card.Starred);
        Assert.Null(card.StarReason);
    }

    [Fact]
    public async Task Star_with_optional_reason_persists_reason()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerWithSubscriptionAsync(learnerId);
        var cardId = await SeedVocabularyCardAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.PostAsJsonAsync("/v1/recalls/star", new
        {
            kind = "vocab",
            id = cardId.ToString(),
            starred = true,
            reason = "spelling",
        });

        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var card = await db.LearnerVocabularies.SingleAsync(lv => lv.Id == cardId);
        Assert.True(card.Starred);
        Assert.Equal("spelling", card.StarReason);
    }

    [Fact]
    public async Task Star_with_invalid_reason_is_rejected()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerWithSubscriptionAsync(learnerId);
        var cardId = await SeedVocabularyCardAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.PostAsJsonAsync("/v1/recalls/star", new
        {
            kind = "vocab",
            id = cardId.ToString(),
            starred = true,
            reason = "not-a-real-reason",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_REASON", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unfavouriting_clears_reason()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerWithSubscriptionAsync(learnerId);
        var cardId = await SeedVocabularyCardAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        await client.PostAsJsonAsync("/v1/recalls/star", new { kind = "vocab", id = cardId.ToString(), starred = true, reason = "meaning" });
        var response = await client.PostAsJsonAsync("/v1/recalls/star", new { kind = "vocab", id = cardId.ToString(), starred = false, reason = (string?)null });

        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var card = await db.LearnerVocabularies.SingleAsync(lv => lv.Id == cardId);
        Assert.False(card.Starred);
        Assert.Null(card.StarReason);
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

    private async Task SeedLearnerWithSubscriptionAsync(string learnerId)
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

        const string planCode = "premium-monthly-recalls-fav-test";
        if (!db.BillingPlans.Any(plan => plan.Id == planCode || plan.Code == planCode))
        {
            db.BillingPlans.Add(new BillingPlan
            {
                Id = planCode,
                Code = planCode,
                Name = "Premium monthly (recalls favourite test)",
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

        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedVocabularyCardAsync(string learnerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var termId = $"term-{Guid.NewGuid():N}";
        var cardId = Guid.NewGuid();

        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = termId,
            Term = "anaemia",
            Definition = "A reduced number of red blood cells or haemoglobin.",
            ExampleSentence = "The patient was investigated for anaemia.",
            ExamTypeCode = "oet",
            ProfessionId = "medicine",
            Category = "spelling",
            RecallSetCodesJson = "[\"2026\"]",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });

        db.LearnerVocabularies.Add(new LearnerVocabulary
        {
            Id = cardId,
            UserId = learnerId,
            TermId = termId,
            Mastery = "new",
            NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AddedAt = now
        });

        await db.SaveChangesAsync();
        return cardId;
    }
}
