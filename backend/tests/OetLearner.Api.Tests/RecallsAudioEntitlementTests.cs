using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
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

        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("X-Recalls-Tts-Provider"));
    }

    [Fact]
    public async Task Audio_denies_non_recall_terms_even_for_active_subscriber()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);
        var termId = await SeedVocabularyCardAsync(learnerId, recallSetCodesJson: "[]");

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/recalls/audio/{termId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Generic_media_endpoint_denies_recall_audio_media_for_learner()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);
        var (_, mediaAssetId) = await SeedVocabularyCardWithMediaAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaAssetId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(MediaAssetStatus.Processing)]
    [InlineData(MediaAssetStatus.Failed)]
    [InlineData(MediaAssetStatus.Quarantined)]
    public async Task Audio_denies_non_ready_recall_media_even_for_active_subscriber(MediaAssetStatus mediaStatus)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);
        var (termId, _) = await SeedVocabularyCardWithMediaAsync(learnerId, mediaStatus: mediaStatus);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/recalls/audio/{termId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Audio_streams_legacy_stored_key_only_when_matching_media_asset_is_ready()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);
        var (termId, _) = await SeedVocabularyCardWithMediaAsync(learnerId);
        await ClearAudioMediaAssetIdAsync(termId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/recalls/audio/{termId}");

        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(MediaAssetStatus.Processing)]
    [InlineData(MediaAssetStatus.Failed)]
    [InlineData(MediaAssetStatus.Quarantined)]
    public async Task Audio_denies_legacy_stored_key_without_ready_media_asset(MediaAssetStatus mediaStatus)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);
        var (termId, _) = await SeedVocabularyCardWithMediaAsync(learnerId, mediaStatus: mediaStatus);
        await ClearAudioMediaAssetIdAsync(termId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/recalls/audio/{termId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("slow", MediaAssetStatus.Failed)]
    [InlineData("sentence", MediaAssetStatus.Quarantined)]
    public async Task Audio_denies_variant_stored_keys_without_ready_media_asset(string speed, MediaAssetStatus mediaStatus)
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);
        var (termId, _) = await SeedVocabularyCardWithMediaAsync(learnerId);
        await SeedVariantMediaAsync(termId, speed, mediaStatus);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/recalls/audio/{termId}?speed={speed}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        await SeedLearnerAsync(learnerId, hasActiveSubscription: true);
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
        Assert.DoesNotContain("recalls/audio/", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vocabulary/audio/", payload, StringComparison.OrdinalIgnoreCase);
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
        else
        {
            db.Subscriptions.Add(new Subscription
            {
                Id = $"sub-{Guid.NewGuid():N}",
                UserId = learnerId,
                PlanId = "basic-monthly",
                Status = SubscriptionStatus.Cancelled,
                StartedAt = now.AddMonths(-2),
                ChangedAt = now.AddMonths(-1),
                PriceAmount = 0m,
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

    private async Task<string> SeedVocabularyCardAsync(string learnerId, string recallSetCodesJson = "[\"2026\"]")
    {
        var (termId, _) = await SeedVocabularyCardWithMediaAsync(learnerId, recallSetCodesJson);
        return termId;
    }

    private async Task<(string TermId, string MediaAssetId)> SeedVocabularyCardWithMediaAsync(
        string learnerId,
        string recallSetCodesJson = "[\"2026\"]",
        MediaAssetStatus mediaStatus = MediaAssetStatus.Ready)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var termId = $"term-{Guid.NewGuid():N}";
        var mediaAssetId = $"media-audio-bypass-test-{Guid.NewGuid():N}";
        var storageKey = $"recalls/audio/{termId}.wav";
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using (var stream = new MemoryStream(new byte[] { 82, 73, 70, 70, 1, 2, 3, 4 }))
        {
            await storage.WriteAsync(storageKey, stream, CancellationToken.None);
        }

        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaAssetId,
            OriginalFilename = $"{termId}.wav",
            MimeType = "audio/wav",
            Format = "wav",
            SizeBytes = 8,
            DurationSeconds = 1,
            StoragePath = storageKey,
            Status = mediaStatus,
            Sha256 = Guid.NewGuid().ToString("N"),
            MediaKind = "audio",
            UploadedBy = "test",
            UploadedAt = now,
            ProcessedAt = now,
        });

        var slowStorageKey = $"recalls/audio/{termId}-slow.wav";
        var sentenceStorageKey = $"recalls/audio/{termId}-sentence.wav";

        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = termId,
            Term = "anaemia",
            Definition = "A reduced number of red blood cells or haemoglobin.",
            ExampleSentence = "The patient was investigated for anaemia.",
            ExamTypeCode = "oet",
            ProfessionId = "medicine",
            Category = "spelling",
            IpaPronunciation = "/əˈniːmiə/",
            AudioUrl = storageKey,
            AudioMediaAssetId = mediaAssetId,
            RecallSetCodesJson = recallSetCodesJson,
            AudioSlowUrl = slowStorageKey,
            AudioSentenceUrl = sentenceStorageKey,
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
        return (termId, mediaAssetId);
    }

    private async Task ClearAudioMediaAssetIdAsync(string termId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var term = await db.VocabularyTerms.SingleAsync(t => t.Id == termId);
        term.AudioMediaAssetId = null;
        await db.SaveChangesAsync();
    }

    private async Task SeedVariantMediaAsync(string termId, string speed, MediaAssetStatus mediaStatus)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var term = await db.VocabularyTerms.AsNoTracking().SingleAsync(t => t.Id == termId);
        var storageKey = speed == "sentence" ? term.AudioSentenceUrl : term.AudioSlowUrl;
        Assert.False(string.IsNullOrWhiteSpace(storageKey));

        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using (var stream = new MemoryStream(new byte[] { 82, 73, 70, 70, 5, 6, 7, 8 }))
        {
            await storage.WriteAsync(storageKey!, stream, CancellationToken.None);
        }

        var now = DateTimeOffset.UtcNow;
        db.MediaAssets.Add(new MediaAsset
        {
            Id = $"media-audio-variant-test-{Guid.NewGuid():N}",
            OriginalFilename = $"{termId}-{speed}.wav",
            MimeType = "audio/wav",
            Format = "wav",
            SizeBytes = 8,
            DurationSeconds = 1,
            StoragePath = storageKey!,
            Status = mediaStatus,
            Sha256 = Guid.NewGuid().ToString("N"),
            MediaKind = "audio",
            UploadedBy = "test",
            UploadedAt = now,
            ProcessedAt = now,
        });
        await db.SaveChangesAsync();
    }
}
