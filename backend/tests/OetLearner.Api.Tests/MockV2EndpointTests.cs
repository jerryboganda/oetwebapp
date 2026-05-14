using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class MockV2EndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MockV2EndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateBooking_ReturnsLearnerSafeDto()
    {
        var bundleId = await EnsurePublishedBundleAsync("mock-v2-booking-bundle");
        using var client = await CreateLearnerClientAsync("mock-v2-booking-user");

        var response = await client.PostAsJsonAsync("/v1/mock-bookings", new
        {
            mockBundleId = bundleId,
            scheduledStartAt = DateTimeOffset.UtcNow.AddDays(2),
            timezoneIana = "Asia/Karachi",
            deliveryMode = MockDeliveryModes.OetHome,
            consentToRecording = true,
            learnerNotes = "Need a final readiness slot."
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("id").GetString()));
        Assert.Equal(bundleId, root.GetProperty("mockBundleId").GetString());
        Assert.Equal("OET@Home Readiness Bundle", root.GetProperty("title").GetString());
        Assert.Equal(MockDeliveryModes.OetHome, root.GetProperty("deliveryMode").GetString());
        Assert.True(root.GetProperty("candidateCardVisible").GetBoolean());
        Assert.False(root.GetProperty("interlocutorCardVisible").GetBoolean());
        Assert.False(root.TryGetProperty("zoomStartUrl", out _));
    }

    [Fact]
    public async Task LearnerBookingPatch_CannotMutateStatus()
    {
        var bundleId = await EnsurePublishedBundleAsync("mock-v2-booking-status-readonly");
        using var client = await CreateLearnerClientAsync("mock-v2-booking-status-user");

        var create = await client.PostAsJsonAsync("/v1/mock-bookings", new
        {
            mockBundleId = bundleId,
            scheduledStartAt = DateTimeOffset.UtcNow.AddDays(2),
            timezoneIana = "Asia/Karachi",
            deliveryMode = MockDeliveryModes.OetHome,
            consentToRecording = true
        });
        create.EnsureSuccessStatusCode();
        using var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var bookingId = createdJson.RootElement.GetProperty("id").GetString()!;

        var response = await client.PatchAsJsonAsync($"/v1/mock-bookings/{bookingId}", new
        {
            status = MockBookingStatuses.Completed
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var errorJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("booking_status_readonly", errorJson.RootElement.GetProperty("code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var booking = await db.MockBookings.AsNoTracking().SingleAsync(x => x.Id == bookingId);
        Assert.Equal(MockBookingStatuses.Scheduled, booking.Status);
    }

    [Fact]
    public async Task SubmitMockAttempt_RejectsIncompleteRequiredSections()
    {
        var bundleId = await EnsurePublishedBundleWithSectionsAsync("mock-v2-incomplete-sections");
        using var client = await CreateLearnerClientAsync("mock-v2-incomplete-user");

        var create = await client.PostAsJsonAsync("/v1/mock-attempts", new
        {
            mockType = MockTypes.Diagnostic,
            bundleId,
            profession = "nursing",
            targetCountry = "UK",
            mode = "practice",
            includeReview = false,
            strictTimer = false
        });
        create.EnsureSuccessStatusCode();
        using var attemptJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var root = attemptJson.RootElement;
        var attemptId = root.GetProperty("mockAttemptId").GetString()!;
        var firstSectionId = root.GetProperty("sectionStates")[0].GetProperty("id").GetString()!;
        var readingAttemptId = await SeedSubmittedReadingAttemptAsync("mock-v2-incomplete-user", $"{bundleId}-reading-paper", firstSectionId);

        var complete = await client.PostAsJsonAsync($"/v1/mock-attempts/{attemptId}/sections/{firstSectionId}/complete", new
        {
            contentAttemptId = readingAttemptId,
            rawScore = 42,
            rawScoreMax = 42,
            scaledScore = 500,
            grade = "A",
            evidence = new Dictionary<string, object?> { ["source"] = "test" }
        });
        complete.EnsureSuccessStatusCode();

        var submit = await client.PostAsync($"/v1/mock-attempts/{attemptId}/submit", null);

        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
        using var errorJson = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        Assert.Equal("mock_sections_incomplete", errorJson.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SubmitMockAttempt_AllowsIncompleteOptionalSections()
    {
        var bundleId = await EnsurePublishedBundleWithSectionsAsync("mock-v2-optional-sections", secondSectionRequired: false);
        using var client = await CreateLearnerClientAsync("mock-v2-optional-user");

        var create = await client.PostAsJsonAsync("/v1/mock-attempts", new
        {
            mockType = MockTypes.Diagnostic,
            bundleId,
            profession = "nursing",
            targetCountry = "UK",
            mode = "practice",
            includeReview = false,
            strictTimer = false
        });
        create.EnsureSuccessStatusCode();
        using var attemptJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var root = attemptJson.RootElement;
        var attemptId = root.GetProperty("mockAttemptId").GetString()!;
        var firstSectionId = root.GetProperty("sectionStates")[0].GetProperty("id").GetString()!;
        var readingAttemptId = await SeedSubmittedReadingAttemptAsync("mock-v2-optional-user", $"{bundleId}-reading-paper", firstSectionId);

        var complete = await client.PostAsJsonAsync($"/v1/mock-attempts/{attemptId}/sections/{firstSectionId}/complete", new
        {
            contentAttemptId = readingAttemptId,
            rawScore = 42,
            rawScoreMax = 42,
            scaledScore = 500,
            grade = "A",
            evidence = new Dictionary<string, object?> { ["source"] = "test" }
        });
        complete.EnsureSuccessStatusCode();

        var submit = await client.PostAsync($"/v1/mock-attempts/{attemptId}/submit", null);

        submit.EnsureSuccessStatusCode();
        using var submittedJson = JsonDocument.Parse(await submit.Content.ReadAsStringAsync());
        Assert.Equal("queued", submittedJson.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task DiagnosticEntitlement_UsesBillingPlanConfiguration()
    {
        const string userId = "mock-v2-diagnostic-disabled";
        using var client = await CreateLearnerClientAsync(userId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            var planId = $"plan-{Guid.NewGuid():N}";
            db.BillingPlans.Add(new BillingPlan
            {
                Id = planId,
                Code = planId,
                Name = "Diagnostic Disabled Plan",
                Description = "Test plan",
                Price = 0,
                Currency = "AUD",
                Interval = "month",
                DurationMonths = 1,
                IsVisible = true,
                IsRenewable = true,
                DiagnosticMockEntitlement = MockDiagnosticEntitlementService.Disabled,
                IncludedSubtestsJson = "[]",
                EntitlementsJson = "{}",
                Status = BillingPlanStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Subscriptions.Add(new Subscription
            {
                Id = $"sub-{Guid.NewGuid():N}",
                UserId = userId,
                PlanId = planId,
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddDays(-1),
                ChangedAt = now,
                NextRenewalAt = now.AddMonths(1),
                PriceAmount = 0,
                Currency = "AUD",
                Interval = "monthly",
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/v1/mocks/diagnostic/entitlement");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("allowed").GetBoolean());
        Assert.Equal(MockDiagnosticEntitlementService.Disabled, json.RootElement.GetProperty("entitlement").GetString());
        Assert.Equal("diagnostic_disabled_for_plan", json.RootElement.GetProperty("reason").GetString());
    }

    private async Task<HttpClient> CreateLearnerClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private async Task<string> EnsurePublishedBundleAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var existing = await db.MockBundles.FirstOrDefaultAsync(x => x.Id == id);
        if (existing is null)
        {
            db.MockBundles.Add(new MockBundle
            {
                Id = id,
                Title = "OET@Home Readiness Bundle",
                Slug = id,
                ExamFamilyCode = "oet",
                ExamTypeCode = "oet",
                MockType = MockTypes.FinalReadiness,
                AppliesToAllProfessions = true,
                Status = ContentStatus.Published,
                EstimatedDurationMinutes = 180,
                ReleasePolicy = MockReleasePolicies.AfterTeacherMarking,
                SourceStatus = MockSourceStatuses.Original,
                QualityStatus = MockQualityStatuses.Approved,
                SourceProvenance = "Admin-authored test bundle.",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Status = ContentStatus.Published;
            existing.ReleasePolicy = MockReleasePolicies.AfterTeacherMarking;
            existing.QualityStatus = MockQualityStatuses.Approved;
            existing.SourceStatus = MockSourceStatuses.Original;
        }
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<string> EnsurePublishedBundleWithSectionsAsync(string id, bool secondSectionRequired = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var paper1Id = $"{id}-reading-paper";
        var paper2Id = $"{id}-writing-paper";

        await EnsurePaperAsync(db, paper1Id, "reading", "Reading diagnostic paper", now);
        await EnsurePaperAsync(db, paper2Id, "writing", "Writing diagnostic paper", now);

        var existing = await db.MockBundles.Include(x => x.Sections).FirstOrDefaultAsync(x => x.Id == id);
        if (existing is null)
        {
            db.MockBundles.Add(new MockBundle
            {
                Id = id,
                Title = "Two Section Diagnostic Bundle",
                Slug = id,
                ExamFamilyCode = "oet",
                ExamTypeCode = "oet",
                MockType = MockTypes.Diagnostic,
                AppliesToAllProfessions = true,
                Status = ContentStatus.Published,
                EstimatedDurationMinutes = 105,
                ReleasePolicy = MockReleasePolicies.Instant,
                SourceStatus = MockSourceStatuses.Original,
                QualityStatus = MockQualityStatuses.Approved,
                SourceProvenance = "Admin-authored test bundle.",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
                Sections = new List<MockBundleSection>
                {
                    new()
                    {
                        Id = $"{id}-section-reading",
                        MockBundleId = id,
                        SectionOrder = 1,
                        SubtestCode = "reading",
                        ContentPaperId = paper1Id,
                        TimeLimitMinutes = 60,
                        ReviewEligible = false,
                        IsRequired = true,
                        CreatedAt = now
                    },
                    new()
                    {
                        Id = $"{id}-section-writing",
                        MockBundleId = id,
                        SectionOrder = 2,
                        SubtestCode = "writing",
                        ContentPaperId = paper2Id,
                        TimeLimitMinutes = 45,
                        ReviewEligible = false,
                        IsRequired = secondSectionRequired,
                        CreatedAt = now
                    }
                }
            });
        }
        else
        {
            existing.Status = ContentStatus.Published;
            existing.MockType = MockTypes.Diagnostic;
            existing.ReleasePolicy = MockReleasePolicies.Instant;
            existing.QualityStatus = MockQualityStatuses.Approved;
            existing.SourceStatus = MockSourceStatuses.Original;
            existing.SourceProvenance = "Admin-authored test bundle.";
            foreach (var section in existing.Sections.Where(x => x.SubtestCode == "writing"))
            {
                section.IsRequired = secondSectionRequired;
            }
        }

        await db.SaveChangesAsync();
        return id;
    }

    private async Task<string> SeedSubmittedReadingAttemptAsync(string userId, string paperId, string? mockSectionId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attemptId = $"reading-attempt-{Guid.NewGuid():N}";
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = attemptId,
            UserId = userId,
            PaperId = paperId,
            Status = ReadingAttemptStatus.Submitted,
            StartedAt = now.AddMinutes(-60),
            SubmittedAt = now,
            LastActivityAt = now,
            RawScore = 30,
            ScaledScore = OetScoring.OetRawToScaled(30),
            MaxRawScore = 42,
        });
        if (!string.IsNullOrWhiteSpace(mockSectionId))
        {
            var section = await db.MockSectionAttempts.FirstAsync(x => x.Id == mockSectionId);
            section.ContentAttemptId = attemptId;
            section.State = AttemptState.InProgress;
            section.StartedAt ??= now.AddMinutes(-60);
        }
        await db.SaveChangesAsync();
        return attemptId;
    }

    private static async Task EnsurePaperAsync(LearnerDbContext db, string id, string subtest, string title, DateTimeOffset now)
    {
        var existing = await db.ContentPapers.FirstOrDefaultAsync(x => x.Id == id);
        if (existing is not null)
        {
            existing.Status = ContentStatus.Published;
            existing.SourceProvenance = "Admin-authored test paper.";
            existing.UpdatedAt = now;
            return;
        }

        db.ContentPapers.Add(new ContentPaper
        {
            Id = id,
            SubtestCode = subtest,
            Title = title,
            Slug = id,
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = subtest == "reading" ? 60 : 45,
            Status = ContentStatus.Published,
            SourceProvenance = "Admin-authored test paper.",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now
        });
    }
}
