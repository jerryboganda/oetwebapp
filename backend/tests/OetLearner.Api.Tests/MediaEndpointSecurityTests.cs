using System.Net;
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
    public async Task Download_denies_published_paper_media_when_active_plan_does_not_grant_paper()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedPublishedPaperMediaAsync(
            mediaId,
            learnerId,
            hasActiveSubscription: true,
            planId: $"listening-only-{Guid.NewGuid():N}",
            entitlementsJson: "{\"content\":{\"tier\":\"free\",\"subtests\":[\"listening\"],\"papers\":[]}}",
            includedSubtestsJson: "[\"listening\"]");

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_allows_published_paper_media_for_sponsor_seat_learner()
    {
        var mediaId = $"media-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        await SeedPublishedPaperMediaAsync(mediaId, learnerId, hasActiveSubscription: false);
        await SeedActiveSponsorshipAsync(learnerId);

        using var client = CreateLearnerClient(learnerId);
        var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
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

    private async Task SeedPublishedPaperMediaAsync(
        string mediaId,
        string learnerId,
        bool hasActiveSubscription,
        string planId = "premium-monthly",
        string? entitlementsJson = null,
        string? includedSubtestsJson = null)
    {
        await SeedStandaloneMediaAsync(mediaId, uploadedBy: "admin-1");

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
            if (entitlementsJson is not null || includedSubtestsJson is not null)
            {
                db.BillingPlans.Add(new BillingPlan
                {
                    Id = planId,
                    Code = planId,
                    Name = "Scoped media entitlement plan",
                    Description = "Scoped media entitlement plan",
                    Price = 49.99m,
                    Currency = "AUD",
                    Interval = "monthly",
                    DurationMonths = 1,
                    EntitlementsJson = entitlementsJson ?? "{}",
                    IncludedSubtestsJson = includedSubtestsJson ?? "[]"
                });
            }

            db.Subscriptions.Add(new Subscription
            {
                Id = $"sub-{Guid.NewGuid():N}",
                UserId = learnerId,
                PlanId = planId,
                Status = SubscriptionStatus.Active,
                StartedAt = now,
                ChangedAt = now,
                NextRenewalAt = now.AddDays(30),
                PriceAmount = 49.99m,
                Currency = "AUD",
                Interval = "monthly"
            });
        }

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
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now
        };
        db.ContentPapers.Add(paper);
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = $"paper-asset-{Guid.NewGuid():N}",
            PaperId = paper.Id,
            Role = PaperAssetRole.QuestionPaper,
            MediaAssetId = mediaId,
            IsPrimary = true,
            DisplayOrder = 0,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
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

    private async Task SeedActiveSponsorshipAsync(string learnerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = $"sponsor-{Guid.NewGuid():N}",
            LearnerUserId = learnerId,
            LearnerEmail = $"{learnerId}@example.test",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
    }
}
