using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Mocks;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Mocks;

/// <summary>
/// POST /v1/mocks/bookings — attempt/section scoping, Zoom provisioning job,
/// and the server-side 7-day AI/tutor rule (2026-07-22 owner rule): a booking
/// scoped to a mock attempt is refused when the learner's TargetExamDate is
/// under 7 days away. Standalone bookings (no mockAttemptId) are exempt.
/// </summary>
public class MockBookingCreateEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MockBookingCreateEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateBooking_PersistsScopingAndQueuesZoomJob()
    {
        var userId = "mock-booking-create-scoped";
        await SeedLearnerWithGoalAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        var (bundleId, attemptId) = await SeedBundleAndAttemptAsync(userId, "scoped");

        using var client = CreateLearnerClient(userId);
        var response = await client.PostAsJsonAsync("/v1/mocks/bookings", new
        {
            bundleId,
            scheduledStartAt = NextSlot(10),
            timezone = "UTC",
            consentToRecording = true,
            mockAttemptId = attemptId,
            mockSectionId = "section-speaking-scoped",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var bookingId = root.GetProperty("id").GetString()!;
        Assert.Equal(attemptId, root.GetProperty("mockAttemptId").GetString());
        Assert.Equal("section-speaking-scoped", root.GetProperty("mockSectionId").GetString());
        // joinUrl is the in-app room; the Zoom link only appears once provisioned.
        Assert.Equal($"/mocks/speaking-room/{Uri.EscapeDataString(bookingId)}", root.GetProperty("joinUrl").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("zoomJoinUrl").ValueKind);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var booking = await db.MockBookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
        Assert.Equal(attemptId, booking.MockAttemptId);
        Assert.Equal("section-speaking-scoped", booking.MockSectionId);
        Assert.Equal(MockBookingZoomStatuses.Pending, booking.ZoomStatus);
        Assert.True(await db.BackgroundJobs.AsNoTracking().AnyAsync(j =>
                j.Type == JobType.MockBookingZoomCreate && j.ResourceId == bookingId),
            "Booking creation must enqueue the MockBookingZoomCreate provisioning job.");
    }

    [Fact]
    public async Task CreateBooking_RejectsTutorBookingInsideAiOnlyWindow()
    {
        var userId = "mock-booking-create-ai-only";
        await SeedLearnerWithGoalAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
        var (bundleId, attemptId) = await SeedBundleAndAttemptAsync(userId, "ai-only");

        using var client = CreateLearnerClient(userId);
        var response = await client.PostAsJsonAsync("/v1/mocks/bookings", new
        {
            bundleId,
            scheduledStartAt = NextSlot(12),
            timezone = "UTC",
            consentToRecording = true,
            mockAttemptId = attemptId,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("speaking_tutor_window_closed", json.RootElement.GetProperty("code").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.False(await db.MockBookings.AsNoTracking().AnyAsync(b => b.UserId == userId));
    }

    [Fact]
    public async Task CreateBooking_AllowsTutorBookingAtExactly7Days()
    {
        var userId = "mock-booking-create-boundary";
        await SeedLearnerWithGoalAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));
        var (bundleId, attemptId) = await SeedBundleAndAttemptAsync(userId, "boundary");

        using var client = CreateLearnerClient(userId);
        var response = await client.PostAsJsonAsync("/v1/mocks/bookings", new
        {
            bundleId,
            scheduledStartAt = NextSlot(14),
            timezone = "UTC",
            consentToRecording = true,
            mockAttemptId = attemptId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_StandaloneBookingIsExemptFromAiOnlyWindow()
    {
        var userId = "mock-booking-create-standalone";
        await SeedLearnerWithGoalAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
        var (bundleId, _) = await SeedBundleAndAttemptAsync(userId, "standalone");

        using var client = CreateLearnerClient(userId);
        var response = await client.PostAsJsonAsync("/v1/mocks/bookings", new
        {
            bundleId,
            scheduledStartAt = NextSlot(16),
            timezone = "UTC",
            consentToRecording = true,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_RejectsMockAttemptOwnedByAnotherLearner()
    {
        var ownerId = "mock-booking-create-owner";
        var intruderId = "mock-booking-create-intruder";
        await SeedLearnerWithGoalAsync(ownerId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        await SeedLearnerWithGoalAsync(intruderId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        var (bundleId, attemptId) = await SeedBundleAndAttemptAsync(ownerId, "foreign");

        using var client = CreateLearnerClient(intruderId);
        var response = await client.PostAsJsonAsync("/v1/mocks/bookings", new
        {
            bundleId,
            scheduledStartAt = NextSlot(18),
            timezone = "UTC",
            consentToRecording = true,
            mockAttemptId = attemptId,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("mock_attempt_not_found", json.RootElement.GetProperty("code").GetString());
    }

    // -- helpers -----------------------------------------------------------

    /// <summary>Distinct far-future slots per test so the span-based
    /// availability collision check never sees two tests overlap.</summary>
    private static DateTimeOffset NextSlot(int daysAhead)
        => new(DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(10), TimeSpan.Zero);

    private async Task SeedLearnerWithGoalAsync(string userId, DateOnly targetExamDate)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId);
        if (goal is null)
        {
            db.Goals.Add(new LearnerGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProfessionId = "medicine",
                TargetExamDate = targetExamDate,
                TargetExamDateSetByUser = true,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            goal.TargetExamDate = targetExamDate;
            goal.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    private async Task<(string BundleId, string AttemptId)> SeedBundleAndAttemptAsync(string userId, string key)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var bundleId = $"mock-booking-create-bundle-{key}";
        var attemptId = $"mock-booking-create-attempt-{key}";

        if (!await db.MockBundles.AnyAsync(b => b.Id == bundleId))
        {
            db.MockBundles.Add(new MockBundle
            {
                Id = bundleId,
                Title = $"Booking create bundle {key}",
                Slug = bundleId,
                MockType = MockTypes.FinalReadiness,
                AppliesToAllProfessions = true,
                Status = ContentStatus.Published,
                EstimatedDurationMinutes = 20,
                ReleasePolicy = MockReleasePolicies.AfterTeacherMarking,
                SourceStatus = MockSourceStatuses.Original,
                QualityStatus = MockQualityStatuses.Approved,
                SourceProvenance = "Booking create endpoint test seed.",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });
        }

        if (!await db.MockAttempts.AnyAsync(a => a.Id == attemptId))
        {
            db.MockAttempts.Add(new MockAttempt
            {
                Id = attemptId,
                UserId = userId,
                MockBundleId = bundleId,
                MockType = MockTypes.FinalReadiness,
                State = AttemptState.InProgress,
                StartedAt = now.AddMinutes(-5),
                ConfigJson = "{}",
                ReviewSelection = "none",
                StrictTimer = false,
            });
        }

        await db.SaveChangesAsync();
        return (bundleId, attemptId);
    }

    private HttpClient CreateLearnerClient(string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }
}
