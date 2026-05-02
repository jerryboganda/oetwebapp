using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// PRD Phase 2 §2 + §3: pronunciation audio on Recalls is paid-only.
/// Free / frozen learners must receive 402 Payment Required from
/// <c>GET /v1/recalls/audio/{termId}</c> so the frontend can prompt for upgrade.
/// </summary>
public class RecallsAudioEntitlementTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Audio_returns_402_for_learner_without_active_subscription()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: false);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync("/v1/recalls/audio/term-does-not-need-to-exist");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("subscription_required", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audio_passes_entitlement_gate_for_active_subscriber()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync("/v1/recalls/audio/term-does-not-exist");

        // The entitlement gate has been cleared. Any non-402 response is acceptable
        // here — what matters is that the paid-only block does NOT trigger.
        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task Audio_streams_private_content_for_active_subscriber()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);
        var termId = await SeedVocabularyCardAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/recalls/audio/{termId}");

        response.EnsureSuccessStatusCode();
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.TryGetValues("X-Recalls-Tts-Provider", out var providers));
        Assert.NotEmpty(providers);
    }

    [Fact]
    public async Task Audio_returns_402_for_frozen_active_subscriber()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true, isFrozen: true);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync("/v1/recalls/audio/term-does-not-need-to-exist");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("subscription_required", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Queue_exposes_term_id_but_never_cached_audio_urls()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: false);
        var termId = await SeedVocabularyCardAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync("/v1/recalls/queue?limit=10");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        AssertNoCachedAudioLeak(payload);
        using var json = JsonDocument.Parse(payload);
        var item = json.RootElement.EnumerateArray()
            .Single(element => element.GetProperty("kind").GetString() == "vocab");

        Assert.Equal(termId, item.GetProperty("termId").GetString());
        Assert.False(item.TryGetProperty("audioUrl", out _));
        Assert.False(item.TryGetProperty("audioSentenceUrl", out _));
    }

    [Fact]
    public async Task Quiz_never_returns_cached_audio_urls()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: false);
        var termId = await SeedVocabularyCardAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync("/v1/recalls/quiz?mode=word_recognition&limit=1");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        AssertNoCachedAudioLeak(payload);
        using var json = JsonDocument.Parse(payload);
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(termId, item.GetProperty("termId").GetString());
        Assert.False(item.TryGetProperty("audioUrl", out _));
        Assert.False(item.TryGetProperty("audioSentenceUrl", out _));
    }

    [Fact]
    public async Task Vocabulary_term_payload_redacts_cached_audio_fields()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: false);
        var termId = await SeedVocabularyCardAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/vocabulary/terms/{termId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        AssertNoCachedAudioLeak(payload);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("audioUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("audioMediaAssetId").ValueKind);
    }

    private static void AssertNoCachedAudioLeak(string payload)
    {
        Assert.DoesNotContain("/media/recalls/tts/", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cached-word", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cached-sentence", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("media-audio-bypass-test", payload, StringComparison.OrdinalIgnoreCase);
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
            const string planCode = "premium-monthly-recalls-test";
            if (!db.BillingPlans.Any(plan => plan.Id == planCode || plan.Code == planCode))
            {
                db.BillingPlans.Add(new BillingPlan
                {
                    Id = planCode,
                    Code = planCode,
                    Name = "Premium monthly (recalls audio test)",
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

    private async Task<string> SeedVocabularyCardAsync(string learnerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var termId = $"term-{Guid.NewGuid():N}";

        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = termId,
            Term = "anaemia",
            Definition = "A reduced number of red blood cells or haemoglobin.",
            ExampleSentence = "The patient was investigated for anaemia.",
            ExamTypeCode = "oet",
            ProfessionId = "medicine",
            Category = "spelling",
            Difficulty = "medium",
            IpaPronunciation = "/əˈniːmiə/",
            AudioUrl = "/media/recalls/tts/cached-word.wav",
            AudioMediaAssetId = "media-audio-bypass-test",
            AudioSlowUrl = "/media/recalls/tts/cached-word-slow.wav",
            AudioSentenceUrl = "/media/recalls/tts/cached-sentence.wav",
            Status = "active",
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
}
