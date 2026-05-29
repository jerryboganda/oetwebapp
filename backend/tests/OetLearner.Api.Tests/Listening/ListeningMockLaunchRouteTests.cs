using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Listening;

// WORK-STREAM 1 — covers the launch route MockService.BuildLaunchRoute emits for
// a Listening mock section. The route is consumed verbatim by the strict
// Listening player (`/listening/player/[paperId]`), so the player relies on the
// mockAttemptId / mockSectionId / strictness / deliveryMode query params being
// present and correctly escaped. BuildLaunchRoute is private static, so we
// exercise it through the public StartMockSectionAsync path (which is exactly
// how the frontend obtains the launchRoute) and assert on the projection.
public class ListeningMockLaunchRouteTests
{
    private static LearnerDbContext NewDb(string? name = null) =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task StartListeningSection_BuildsStrictListeningPlayerLaunchRoute()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedListeningMockSection(db, "mock-listening", "section-listening", "bundle-section-listening", "learner-1", "paper-listening", now);
        await db.SaveChangesAsync();

        var service = new MockService(db);
        var started = await service.StartMockSectionAsync(
            "learner-1",
            "mock-listening",
            "section-listening",
            new MockSectionStartRequest(),
            CancellationToken.None);

        var launchRoute = ReadLaunchRoute(started);

        Assert.Equal(
            "/listening/player/paper-listening?mockAttemptId=mock-listening&mockSectionId=section-listening&paperId=paper-listening&mockMode=exam&strictness=exam&deliveryMode=computer&strictTimer=true",
            launchRoute);
    }

    [Fact]
    public async Task StartListeningSection_LaunchRouteCarriesMockBindingAndDeliveryParams()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedListeningMockSection(db, "mock-listening", "section-listening", "bundle-section-listening", "learner-1", "paper-listening", now);
        await db.SaveChangesAsync();

        var service = new MockService(db);
        var started = await service.StartMockSectionAsync(
            "learner-1",
            "mock-listening",
            "section-listening",
            new MockSectionStartRequest(),
            CancellationToken.None);

        var launchRoute = ReadLaunchRoute(started);

        Assert.StartsWith("/listening/player/paper-listening?", launchRoute);
        Assert.Contains("mockAttemptId=mock-listening", launchRoute);
        Assert.Contains("mockSectionId=section-listening", launchRoute);
        Assert.Contains("deliveryMode=computer", launchRoute);
        Assert.Contains("strictness=exam", launchRoute);
        Assert.Contains("strictTimer=true", launchRoute);
        // A fresh launch has no bound content attempt yet, so the route must not
        // yet carry the content `attemptId` param (only mock binding params).
        Assert.DoesNotContain("&attemptId=", launchRoute);
    }

    // The projection is the same anonymous object MockService returns at the API
    // boundary, so we read launchRoute the way the serializer presents it to the
    // client rather than reflecting over the anonymous type directly.
    private static string ReadLaunchRoute(object projection)
    {
        var element = JsonSerializer.SerializeToElement(projection);
        Assert.Equal(JsonValueKind.String, element.GetProperty("launchRoute").ValueKind);
        return element.GetProperty("launchRoute").GetString()!;
    }

    private static void SeedListeningMockSection(
        LearnerDbContext db,
        string mockAttemptId,
        string sectionAttemptId,
        string bundleSectionId,
        string userId,
        string paperId,
        DateTimeOffset now)
    {
        db.MockBundles.Add(new MockBundle
        {
            Id = "bundle-test",
            Title = "Listening launch-route bundle",
            Slug = $"listening-launch-route-{mockAttemptId}",
            MockType = MockTypes.Sub,
            AppliesToAllProfessions = true,
            Status = ContentStatus.Published,
            EstimatedDurationMinutes = 45,
            ReleasePolicy = MockReleasePolicies.Instant,
            SourceStatus = MockSourceStatuses.Original,
            QualityStatus = MockQualityStatuses.Approved,
            SourceProvenance = "Listening mock launch-route test seed.",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "listening",
            Title = "Listening launch-route paper",
            Slug = $"{paperId}-slug",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 45,
            Status = ContentStatus.Published,
            SourceProvenance = "Listening mock launch-route test paper.",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.MockAttempts.Add(new MockAttempt
        {
            Id = mockAttemptId,
            UserId = userId,
            MockBundleId = "bundle-test",
            MockType = MockTypes.Sub,
            SubtestCode = "listening",
            Mode = "exam",
            Strictness = MockStrictness.Exam,
            DeliveryMode = MockDeliveryModes.Computer,
            StrictTimer = true,
            State = AttemptState.InProgress,
            StartedAt = now.AddMinutes(-2),
            ConfigJson = "{}",
            ReviewSelection = "none",
        });
        db.MockBundleSections.Add(new MockBundleSection
        {
            Id = bundleSectionId,
            MockBundleId = "bundle-test",
            SectionOrder = 1,
            SubtestCode = "listening",
            ContentPaperId = paperId,
            TimeLimitMinutes = 45,
            ReviewEligible = false,
            IsRequired = true,
            CreatedAt = now,
        });
        db.MockSectionAttempts.Add(new MockSectionAttempt
        {
            Id = sectionAttemptId,
            MockAttemptId = mockAttemptId,
            MockBundleSectionId = bundleSectionId,
            SubtestCode = "listening",
            ContentPaperId = paperId,
            LaunchRoute = "/mocks",
            State = AttemptState.NotStarted,
        });
    }
}
