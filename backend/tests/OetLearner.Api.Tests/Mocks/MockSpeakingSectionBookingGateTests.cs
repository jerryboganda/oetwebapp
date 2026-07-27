using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Mocks;

/// <summary>
/// Speaking-section completion evidence gate
/// (<c>MockService.RequireProductiveSectionEvidenceAsync</c>): mocks are
/// human-marked, so completing a Speaking section requires a live-tutor
/// booking on the SAME attempt — and, when the booking carries a
/// MockSectionId, on the SAME section — or a started section with an
/// evidence payload. Cancelled bookings never count.
/// </summary>
public class MockSpeakingSectionBookingGateTests
{
    private const string UserId = "speaking-gate-learner";
    private const string AttemptId = "speaking-gate-attempt";
    private const string SectionId = "speaking-gate-section";

    private static LearnerDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task SpeakingCompletion_RejectedWithoutBookingOrEvidence()
    {
        await using var db = NewDb();
        SeedSpeakingSection(db);
        await db.SaveChangesAsync();

        var service = new MockService(db);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.CompleteMockSectionAsync(UserId, AttemptId, SectionId, NoEvidenceRequest(), CancellationToken.None));

        Assert.Equal("speaking_evidence_required", ex.ErrorCode);
    }

    [Fact]
    public async Task SpeakingCompletion_AcceptedWithAttemptScopedBooking()
    {
        await using var db = NewDb();
        SeedSpeakingSection(db);
        SeedBooking(db, mockSectionId: null, status: MockBookingStatuses.Scheduled);
        await db.SaveChangesAsync();

        var service = new MockService(db);
        await service.CompleteMockSectionAsync(UserId, AttemptId, SectionId, NoEvidenceRequest(), CancellationToken.None);

        var section = await db.MockSectionAttempts.SingleAsync(x => x.Id == SectionId);
        Assert.Equal(AttemptState.Completed, section.State);
    }

    [Fact]
    public async Task SpeakingCompletion_AcceptedWithSectionScopedBooking()
    {
        await using var db = NewDb();
        SeedSpeakingSection(db);
        SeedBooking(db, mockSectionId: SectionId, status: MockBookingStatuses.Scheduled);
        await db.SaveChangesAsync();

        var service = new MockService(db);
        await service.CompleteMockSectionAsync(UserId, AttemptId, SectionId, NoEvidenceRequest(), CancellationToken.None);

        var section = await db.MockSectionAttempts.SingleAsync(x => x.Id == SectionId);
        Assert.Equal(AttemptState.Completed, section.State);
    }

    [Fact]
    public async Task SpeakingCompletion_RejectedWhenBookingScopedToDifferentSection()
    {
        await using var db = NewDb();
        SeedSpeakingSection(db);
        SeedBooking(db, mockSectionId: "some-other-section", status: MockBookingStatuses.Scheduled);
        await db.SaveChangesAsync();

        var service = new MockService(db);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.CompleteMockSectionAsync(UserId, AttemptId, SectionId, NoEvidenceRequest(), CancellationToken.None));

        Assert.Equal("speaking_evidence_required", ex.ErrorCode);
    }

    [Fact]
    public async Task SpeakingCompletion_IgnoresCancelledBooking()
    {
        await using var db = NewDb();
        SeedSpeakingSection(db);
        SeedBooking(db, mockSectionId: null, status: MockBookingStatuses.Cancelled);
        await db.SaveChangesAsync();

        var service = new MockService(db);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.CompleteMockSectionAsync(UserId, AttemptId, SectionId, NoEvidenceRequest(), CancellationToken.None));

        Assert.Equal("speaking_evidence_required", ex.ErrorCode);
    }

    // -- helpers -----------------------------------------------------------

    /// <summary>No content attempt, no evidence payload — completion must be
    /// justified by the booking alone.</summary>
    private static MockSectionCompleteRequest NoEvidenceRequest() => new(
        ContentAttemptId: null,
        RawScore: null,
        RawScoreMax: null,
        ScaledScore: null,
        Grade: null,
        Evidence: null);

    private static void SeedBooking(LearnerDbContext db, string? mockSectionId, string status)
    {
        var now = DateTimeOffset.UtcNow;
        db.MockBookings.Add(new MockBooking
        {
            Id = $"speaking-gate-booking-{Guid.NewGuid():N}",
            UserId = UserId,
            MockBundleId = "bundle-speaking-gate",
            MockAttemptId = AttemptId,
            MockSectionId = mockSectionId,
            ScheduledStartAt = now.AddDays(2),
            TimezoneIana = "UTC",
            Status = status,
            DeliveryMode = MockDeliveryModes.Computer,
            LiveRoomState = MockLiveRoomStates.Waiting,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static void SeedSpeakingSection(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.MockBundles.Add(new MockBundle
        {
            Id = "bundle-speaking-gate",
            Title = "Speaking gate bundle",
            Slug = "bundle-speaking-gate",
            MockType = MockTypes.FinalReadiness,
            AppliesToAllProfessions = true,
            Status = ContentStatus.Published,
            EstimatedDurationMinutes = 20,
            ReleasePolicy = MockReleasePolicies.AfterTeacherMarking,
            SourceStatus = MockSourceStatuses.Original,
            QualityStatus = MockQualityStatuses.Approved,
            SourceProvenance = "Speaking booking gate test seed.",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-speaking-gate",
            SubtestCode = "speaking",
            Title = "Speaking gate paper",
            Slug = "paper-speaking-gate",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 20,
            Status = ContentStatus.Published,
            SourceProvenance = "Speaking booking gate test paper.",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.MockAttempts.Add(new MockAttempt
        {
            Id = AttemptId,
            UserId = UserId,
            MockBundleId = "bundle-speaking-gate",
            MockType = MockTypes.FinalReadiness,
            State = AttemptState.InProgress,
            StartedAt = now.AddMinutes(-10),
            ConfigJson = "{}",
            ReviewSelection = "none",
            StrictTimer = false,
        });
        db.MockBundleSections.Add(new MockBundleSection
        {
            Id = "bundle-section-speaking-gate",
            MockBundleId = "bundle-speaking-gate",
            SectionOrder = 1,
            SubtestCode = "speaking",
            ContentPaperId = "paper-speaking-gate",
            TimeLimitMinutes = 20,
            ReviewEligible = true,
            IsRequired = true,
            CreatedAt = now,
        });
        db.MockSectionAttempts.Add(new MockSectionAttempt
        {
            Id = SectionId,
            MockAttemptId = AttemptId,
            MockBundleSectionId = "bundle-section-speaking-gate",
            SubtestCode = "speaking",
            ContentPaperId = "paper-speaking-gate",
            LaunchRoute = "/mocks",
            State = AttemptState.InProgress,
            StartedAt = now.AddMinutes(-10),
        });
    }
}
