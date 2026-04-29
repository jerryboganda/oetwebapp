using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class MediaEndpointSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Upload_rejects_declared_pdf_when_magic_bytes_do_not_match()
    {
        using var client = CreateLearnerClient($"learner-{Guid.NewGuid():N}");
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("not-a-pdf"u8.ToArray());
        file.Headers.ContentType = new("application/pdf");
        content.Add(file, "file", "fake.pdf");

        var response = await client.PostAsync("/v1/media/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_file_content", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_denies_published_paper_media_without_active_entitlement()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedPublishedPaperMediaAsync(mediaId, learnerId, hasActiveSubscription: false);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_allows_published_paper_media_for_matching_entitled_learner()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedPublishedPaperMediaAsync(mediaId, learnerId, hasActiveSubscription: true);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Download_denies_published_paper_media_when_plan_excludes_paper()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var planCode = $"limited-plan-{Guid.NewGuid():N}";
        await SeedPublishedPaperMediaAsync(
            mediaId,
            learnerId,
            hasActiveSubscription: true,
            billingPlanCode: planCode,
            billingPlanEntitlementsJson: System.Text.Json.JsonSerializer.Serialize(new { content = new { tier = "free", subtests = new[] { "writing" } } }));

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SignedUrl_denies_published_paper_media_when_plan_excludes_paper()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var planCode = $"limited-url-plan-{Guid.NewGuid():N}";
        await SeedPublishedPaperMediaAsync(
            mediaId,
            learnerId,
            hasActiveSubscription: true,
            billingPlanCode: planCode,
            billingPlanEntitlementsJson: System.Text.Json.JsonSerializer.Serialize(new { content = new { tier = "free", subtests = new[] { "writing" } } }));

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/url");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_allows_accessFree_paper_media_without_subscription()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedPublishedPaperMediaAsync(mediaId, learnerId, hasActiveSubscription: false, paperTagsCsv: "access:free");

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Download_denies_nonLearnerVisible_paper_asset_role_even_with_entitlement()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedPublishedPaperMediaAsync(mediaId, learnerId, hasActiveSubscription: true, role: PaperAssetRole.AnswerKey);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_allows_published_free_preview_media_without_subscription()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedStandaloneMediaAsync(mediaId, uploadedBy: "admin-1");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.FreePreviewAssets.Add(new FreePreviewAsset
        {
            Id = $"preview-{Guid.NewGuid():N}",
            Title = "Preview",
            PreviewType = "sample_task",
            MediaAssetId = mediaId,
            Status = ContentStatus.Published,
            DisplayOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadingStructure_denies_premium_paper_without_subscription_before_returning_stimuli()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var paperId = await SeedPublishedPaperAsync(learnerId, "reading", hasActiveSubscription: false);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/reading-papers/papers/{paperId}/structure");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task ListeningSession_denies_premium_paper_without_subscription_before_returning_questions()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var paperId = await SeedPublishedPaperAsync(learnerId, "listening", hasActiveSubscription: false);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/listening-papers/papers/{paperId}/session");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task ReadingStructure_denies_crossProfession_paper_even_when_free()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var paperId = await SeedPublishedPaperAsync(
            learnerId,
            "reading",
            hasActiveSubscription: false,
            paperTagsCsv: "access:free",
            paperProfessionId: "nursing");

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/reading-papers/papers/{paperId}/structure");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListeningSession_denies_crossProfession_paper_even_when_free()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var paperId = await SeedPublishedPaperAsync(
            learnerId,
            "listening",
            hasActiveSubscription: false,
            paperTagsCsv: "access:free",
            paperProfessionId: "nursing");

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/listening-papers/papers/{paperId}/session");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListeningStartAttempt_denies_crossProfession_paper_even_when_free()
    {
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var paperId = await SeedPublishedPaperAsync(
            learnerId,
            "listening",
            hasActiveSubscription: false,
            paperTagsCsv: "access:free",
            paperProfessionId: "nursing");

        using var client = CreateLearnerClient(learnerId);
        var response = await client.PostAsJsonAsync($"/v1/listening-papers/papers/{paperId}/attempts", new { mode = "practice" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PaperDetail_denies_premium_paper_without_subscription_before_returning_asset_metadata()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var paperId = await SeedPublishedPaperMediaAsync(
            mediaId,
            learnerId,
            hasActiveSubscription: false,
            role: PaperAssetRole.AnswerKey);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/papers/{paperId}");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.DoesNotContain(mediaId, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaperDetail_filters_nonLearnerVisible_asset_metadata_for_entitled_learner()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var paperId = await SeedPublishedPaperMediaAsync(
            mediaId,
            learnerId,
            hasActiveSubscription: true,
            role: PaperAssetRole.AnswerKey);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/papers/{paperId}");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(mediaId, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("AnswerKey", payload, StringComparison.Ordinal);
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

    private async Task<string> SeedPublishedPaperMediaAsync(
        string mediaId,
        string learnerId,
        bool hasActiveSubscription,
        string? billingPlanCode = null,
        string? billingPlanEntitlementsJson = null,
        string paperTagsCsv = "access:premium",
        PaperAssetRole role = PaperAssetRole.QuestionPaper)
    {
        await SeedStandaloneMediaAsync(mediaId, uploadedBy: "admin-1");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;

        SeedLearnerAndOptionalSubscription(db, learnerId, hasActiveSubscription, now, billingPlanCode, billingPlanEntitlementsJson);

        var paper = new ContentPaper
        {
            Id = $"paper-{Guid.NewGuid():N}",
            SubtestCode = "reading",
            Title = "Reading entitlement test",
            Slug = $"reading-entitlement-{Guid.NewGuid():N}",
            ProfessionId = "medicine",
            AppliesToAllProfessions = false,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            SourceProvenance = ContentDefaults.DefaultSourceProvenance,
            TagsCsv = paperTagsCsv,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now
        };
        db.ContentPapers.Add(paper);
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = $"paper-asset-{Guid.NewGuid():N}",
            PaperId = paper.Id,
            Role = role,
            MediaAssetId = mediaId,
            IsPrimary = true,
            DisplayOrder = 0,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        return paper.Id;
    }

    private async Task<string> SeedPublishedPaperAsync(
        string learnerId,
        string subtestCode,
        bool hasActiveSubscription,
        string paperTagsCsv = "access:premium",
        string paperProfessionId = "medicine")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;

        SeedLearnerAndOptionalSubscription(db, learnerId, hasActiveSubscription, now, billingPlanCode: null, billingPlanEntitlementsJson: null);

        var paper = new ContentPaper
        {
            Id = $"paper-{Guid.NewGuid():N}",
            SubtestCode = subtestCode,
            Title = $"{subtestCode} entitlement test",
            Slug = $"{subtestCode}-entitlement-{Guid.NewGuid():N}",
            ProfessionId = paperProfessionId,
            AppliesToAllProfessions = false,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            SourceProvenance = ContentDefaults.DefaultSourceProvenance,
            TagsCsv = paperTagsCsv,
            ExtractedTextJson = "{\"questions\":[]}",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper.Id;
    }

    private static void SeedLearnerAndOptionalSubscription(
        LearnerDbContext db,
        string learnerId,
        bool hasActiveSubscription,
        DateTimeOffset now,
        string? billingPlanCode,
        string? billingPlanEntitlementsJson)
    {
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

        if (!hasActiveSubscription)
        {
            return;
        }

        var effectivePlanCode = billingPlanCode ?? "premium-monthly";
        if (!db.BillingPlans.Any(plan => plan.Id == effectivePlanCode || plan.Code == effectivePlanCode))
        {
            db.BillingPlans.Add(new BillingPlan
            {
                Id = effectivePlanCode,
                Code = effectivePlanCode,
                Name = string.IsNullOrWhiteSpace(billingPlanCode) ? "Premium monthly" : "Limited content plan",
                EntitlementsJson = billingPlanEntitlementsJson ?? "{}",
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
            PlanId = effectivePlanCode,
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddDays(30),
            PriceAmount = 49.99m,
            Currency = "AUD",
            Interval = "monthly"
        });
    }

    private async Task SeedStandaloneMediaAsync(string mediaId, string uploadedBy)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var storageKey = $"uploads/published/security/{mediaId}.pdf";
        await storage.WriteAsync(storageKey, new MemoryStream([0x25, 0x50, 0x44, 0x46]), default);

        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = $"{mediaId}.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = 4,
            StoragePath = storageKey,
            Status = MediaAssetStatus.Ready,
            UploadedBy = uploadedBy,
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
