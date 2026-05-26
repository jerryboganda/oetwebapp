using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts.Classes;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Classes;

namespace OetLearner.Api.Tests.Classes;

/// <summary>
/// Verifies TutorService CRUD, availability replace, earnings sum, and the
/// v2 Zoom-user provisioning stub. Mirrors the in-memory fixture style used
/// by LiveClassProjectionTests / LiveClassZoomAndRefundTests.
/// </summary>
public sealed class TutorServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsTutorProfileWithDefaults()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now);

        var profile = await service.CreateAsync(
            "expert-1",
            new TutorUpsertRequest(
                DisplayName: "Dr Sarah",
                DisplayNameAr: null,
                Bio: "Specialist OET tutor.",
                BioAr: null,
                AvatarUrl: null,
                Specialties: new[] { "nursing", "medicine" },
                Languages: new[] { "en" },
                HourlyRateUsd: 45m,
                TimeZone: "Australia/Sydney",
                IsActive: true),
            CancellationToken.None);

        Assert.Equal("expert-1", profile.UserId);
        Assert.Equal("Dr Sarah", profile.DisplayName);
        Assert.Equal("Australia/Sydney", profile.TimeZone);
        Assert.Equal(45m, profile.HourlyRateUsd);
        Assert.Equal(new[] { "nursing", "medicine" }, profile.Specialties);
        Assert.True(profile.IsActive);
        Assert.Equal(1, await db.Tutors.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateForSameUser()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now);

        await service.CreateAsync("expert-1", SampleUpsert(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateAsync("expert-1", SampleUpsert(), CancellationToken.None));
        Assert.Equal("tutor_already_exists", exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_PreservesBioWhenRequestOmitsBio()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now);
        await service.CreateAsync("expert-1", SampleUpsert(bio: "original bio"), CancellationToken.None);

        var updated = await service.UpdateAsync(
            "expert-1",
            new TutorUpsertRequest(
                DisplayName: "Dr Sarah Smith",
                DisplayNameAr: null,
                Bio: null,
                BioAr: null,
                AvatarUrl: null,
                Specialties: null,
                Languages: null,
                HourlyRateUsd: null,
                TimeZone: null,
                IsActive: null),
            CancellationToken.None);

        Assert.Equal("Dr Sarah Smith", updated.DisplayName);
        Assert.Equal("original bio", updated.Bio);
    }

    [Fact]
    public async Task ReplaceAvailabilityAsync_ReplacesExistingSlots()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now);
        await service.CreateAsync("expert-1", SampleUpsert(), CancellationToken.None);

        await service.ReplaceAvailabilityAsync(
            "expert-1",
            new[]
            {
                new TutorAvailabilityUpsertRequest(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0), true),
                new TutorAvailabilityUpsertRequest(DayOfWeek.Monday, new TimeOnly(14, 0), new TimeOnly(17, 0), true),
            },
            CancellationToken.None);

        var afterFirst = await db.TutorAvailabilities.CountAsync();
        Assert.Equal(2, afterFirst);

        // Replace with a single slot — the first two should be gone.
        var slots = await service.ReplaceAvailabilityAsync(
            "expert-1",
            new[]
            {
                new TutorAvailabilityUpsertRequest(DayOfWeek.Tuesday, new TimeOnly(10, 0), new TimeOnly(11, 0), true),
            },
            CancellationToken.None);

        Assert.Single(slots);
        Assert.Equal(DayOfWeek.Tuesday, slots[0].DayOfWeek);
        Assert.Equal(1, await db.TutorAvailabilities.CountAsync());
    }

    [Fact]
    public async Task ReplaceAvailabilityAsync_RejectsOverlappingSameDaySlots()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now);
        await service.CreateAsync("expert-1", SampleUpsert(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ReplaceAvailabilityAsync(
            "expert-1",
            new[]
            {
                new TutorAvailabilityUpsertRequest(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0), true),
                new TutorAvailabilityUpsertRequest(DayOfWeek.Monday, new TimeOnly(11, 0), new TimeOnly(13, 0), true),
            },
            CancellationToken.None));

        Assert.Equal("tutor_availability_overlap", exception.ErrorCode);
    }

    [Fact]
    public async Task ReplaceAvailabilityAsync_RejectsInvertedRange()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now);
        await service.CreateAsync("expert-1", SampleUpsert(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ReplaceAvailabilityAsync(
            "expert-1",
            new[]
            {
                new TutorAvailabilityUpsertRequest(DayOfWeek.Monday, new TimeOnly(15, 0), new TimeOnly(10, 0), true),
            },
            CancellationToken.None));

        Assert.Equal("tutor_availability_invalid_range", exception.ErrorCode);
    }

    [Fact]
    public async Task GetEarningsAsync_SumsCreditTimesUsdValueTimesRevenueShare()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now);
        await service.CreateAsync("expert-1", SampleUpsert(), CancellationToken.None);

        // Set up the bridge: a PrivateSpeakingTutorProfile referenced by the
        // LiveClass with ExpertUserId matching the tutor's UserId.
        var tutorProfile = new PrivateSpeakingTutorProfile
        {
            Id = "psp-1",
            ExpertUserId = "expert-1",
            DisplayName = "Dr Sarah",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PrivateSpeakingTutorProfiles.Add(tutorProfile);

        var liveClass = new LiveClass
        {
            Id = "LC-1",
            Slug = "lc-1",
            Title = "Test class",
            Description = "Desc",
            Type = LiveClassType.GroupClass,
            CreditCost = 5,
            Status = LiveClassStatus.Published,
            TutorProfileId = tutorProfile.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var session = new LiveClassSession
        {
            Id = "LCS-1",
            LiveClassId = liveClass.Id,
            ScheduledStartAt = now.AddDays(-1),
            ScheduledEndAt = now.AddDays(-1).AddHours(1),
            Capacity = 10,
            EnrolledCount = 2,
            Status = LiveClassSessionStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
            LiveClass = liveClass,
        };
        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);

        // 2 attended + 1 no-show — only the 2 attended count toward earnings.
        db.LiveClassEnrollments.AddRange(
            new LiveClassEnrollment
            {
                Id = "LCE-1",
                ClassSessionId = session.Id,
                UserId = "learner-1",
                EnrolledAt = now.AddDays(-2),
                CreditsCharged = 5,
                IdempotencyKey = "k1",
                Status = LiveClassEnrollmentStatus.Attended,
            },
            new LiveClassEnrollment
            {
                Id = "LCE-2",
                ClassSessionId = session.Id,
                UserId = "learner-2",
                EnrolledAt = now.AddDays(-2),
                CreditsCharged = 5,
                IdempotencyKey = "k2",
                Status = LiveClassEnrollmentStatus.Attended,
            },
            new LiveClassEnrollment
            {
                Id = "LCE-3",
                ClassSessionId = session.Id,
                UserId = "learner-3",
                EnrolledAt = now.AddDays(-2),
                CreditsCharged = 5,
                IdempotencyKey = "k3",
                Status = LiveClassEnrollmentStatus.NoShow,
            });
        await db.SaveChangesAsync();

        var earnings = await service.GetEarningsAsync("expert-1", from: null, to: null, CancellationToken.None);

        // 2 attended × 5 credits × $1.00/credit × 70% share = $7.00 net, $10.00 gross.
        Assert.Single(earnings.Lines);
        var line = earnings.Lines[0];
        Assert.Equal(2, line.AttendedCount);
        Assert.Equal(5, line.CreditCost);
        Assert.Equal(10m, line.GrossUsd);
        Assert.Equal(7m, line.NetUsd);
        Assert.Equal(10m, earnings.GrossUsd);
        Assert.Equal(7m, earnings.NetUsd);
        Assert.Equal(TutorService.DefaultRevenueSharePercent, earnings.RevenueSharePercent);
    }

    [Fact]
    public async Task ProvisionZoomUserAsync_FallsBackToPlatformDefaultHost()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now, new ZoomOptions { HostUserId = "platform-default-host" });
        await service.CreateAsync("expert-1", SampleUpsert(), CancellationToken.None);

        var zoomUserId = await service.ProvisionZoomUserAsync("expert-1", CancellationToken.None);

        Assert.Equal("platform-default-host", zoomUserId);
        var persisted = await db.Tutors.SingleAsync(t => t.UserId == "expert-1");
        Assert.Equal("platform-default-host", persisted.ZoomUserId);
    }

    [Fact]
    public async Task ProvisionZoomUserAsync_ReturnsNullWhenNoDefaultHostConfigured()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now, new ZoomOptions { HostUserId = null });
        await service.CreateAsync("expert-1", SampleUpsert(), CancellationToken.None);

        var zoomUserId = await service.ProvisionZoomUserAsync("expert-1", CancellationToken.None);

        Assert.Null(zoomUserId);
    }

    // -- helpers -----------------------------------------------------------

    private static TutorUpsertRequest SampleUpsert(string bio = "Sample bio")
        => new(
            DisplayName: "Dr Sarah",
            DisplayNameAr: null,
            Bio: bio,
            BioAr: null,
            AvatarUrl: null,
            Specialties: new[] { "nursing" },
            Languages: new[] { "en" },
            HourlyRateUsd: 45m,
            TimeZone: "UTC",
            IsActive: true);

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static TutorService CreateService(LearnerDbContext db, DateTimeOffset now, ZoomOptions? zoomOptions = null)
        => new(
            db,
            TestRuntimeSettingsProvider.FromZoomOptions(zoomOptions ?? new ZoomOptions()),
            new FixedTimeProvider(now),
            NullLogger<TutorService>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
