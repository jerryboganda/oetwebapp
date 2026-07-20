using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Contracts.Classes;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.LiveClasses;

namespace OetLearner.Api.Tests;

/// <summary>
/// Object-level authorization regression tests for the tutor live-class portal.
/// The tutor session/class mutation endpoints (<c>/v1/tutor/me/classes/*</c>) reuse
/// admin-surface LiveClassService methods that load by id with no owner check — so any
/// tutor could edit/cancel/add sessions to another tutor's classes. The fix adds
/// ownership guards (<c>EnsureTutorOwns{Class,Session}Async</c>) scoped to
/// <c>LiveClass.TutorProfile.ExpertUserId</c>, and a <c>CreateTutorClassAsync</c> wrapper
/// that stamps the caller's profile so tutor-created classes are actually owned.
///
/// These tests drive the service the way <c>TutorEndpoints</c> composes it: the guard
/// runs first, and the shared mutator only executes when ownership passes. A foreign
/// tutor must be rejected with <c>live_class_not_assigned</c> before the mutator runs;
/// the owning tutor must be allowed through to a successful mutation.
/// </summary>
public sealed class LiveClassTutorOwnershipTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Guards_RejectForeignTutor_AllowOwner()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var (classId, sessionId, ownerId, attackerId) = await SeedOwnedClassAsync(db, now);
        var service = CreateService(db, now);

        var sessionEx = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsureTutorOwnsSessionAsync(sessionId, attackerId, Ct));
        Assert.Equal(StatusCodes.Status403Forbidden, sessionEx.StatusCode);
        Assert.Equal("live_class_not_assigned", sessionEx.ErrorCode);

        var classEx = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsureTutorOwnsClassAsync(classId, attackerId, Ct));
        Assert.Equal(StatusCodes.Status403Forbidden, classEx.StatusCode);
        Assert.Equal("live_class_not_assigned", classEx.ErrorCode);

        // The owning tutor passes both guards (no throw).
        await service.EnsureTutorOwnsSessionAsync(sessionId, ownerId, Ct);
        await service.EnsureTutorOwnsClassAsync(classId, ownerId, Ct);
    }

    [Fact]
    public async Task UpdateSession_ForeignTutorBlocked_OwnerSucceeds()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var (_, sessionId, ownerId, attackerId) = await SeedOwnedClassAsync(db, now);
        var service = CreateService(db, now);
        var request = new AdminLiveClassSessionUpdateRequest(null, null, Capacity: 15, null);

        // Foreign tutor is stopped at the guard — the mutator never runs.
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsureTutorOwnsSessionAsync(sessionId, attackerId, Ct));
        Assert.Equal("live_class_not_assigned", ex.ErrorCode);
        Assert.Equal(10, (await db.LiveClassSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, Ct)).Capacity);

        // Owner clears the guard and the update applies.
        await service.EnsureTutorOwnsSessionAsync(sessionId, ownerId, Ct);
        await service.UpdateSessionAsync(sessionId, request, ownerId, "Owner Tutor", Ct);
        Assert.Equal(15, (await db.LiveClassSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, Ct)).Capacity);
    }

    [Fact]
    public async Task CancelSession_ForeignTutorBlocked_OwnerSucceeds()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var (_, sessionId, ownerId, attackerId) = await SeedOwnedClassAsync(db, now);
        var service = CreateService(db, now);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsureTutorOwnsSessionAsync(sessionId, attackerId, Ct));
        Assert.Equal("live_class_not_assigned", ex.ErrorCode);
        Assert.Equal(LiveClassSessionStatus.Scheduled, (await db.LiveClassSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, Ct)).Status);

        await service.EnsureTutorOwnsSessionAsync(sessionId, ownerId, Ct);
        await service.CancelSessionAsync(sessionId, ownerId, "Owner Tutor", "Cancelled by tutor.", Ct);
        Assert.Equal(LiveClassSessionStatus.Cancelled, (await db.LiveClassSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, Ct)).Status);
    }

    [Fact]
    public async Task AddSession_ForeignTutorBlocked_OwnerSucceeds()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var (classId, _, ownerId, attackerId) = await SeedOwnedClassAsync(db, now);
        var service = CreateService(db, now);
        var request = new AdminLiveClassSessionAddRequest(DateTimeOffset.UtcNow.AddDays(2), DurationMinutes: 60, Capacity: 12);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsureTutorOwnsClassAsync(classId, attackerId, Ct));
        Assert.Equal("live_class_not_assigned", ex.ErrorCode);
        Assert.Equal(1, await db.LiveClassSessions.CountAsync(s => s.LiveClassId == classId, Ct));

        await service.EnsureTutorOwnsClassAsync(classId, ownerId, Ct);
        var created = await service.AddSessionAsync(classId, request, ownerId, "Owner Tutor", Ct);
        Assert.NotNull(created);
        Assert.Equal(2, await db.LiveClassSessions.CountAsync(s => s.LiveClassId == classId, Ct));
    }

    [Fact]
    public async Task CreateTutorClass_LinksCallerProfile_SoOwnershipGuardPasses()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var ownerId = $"lc-owner-{Guid.NewGuid():N}";
        var attackerId = $"lc-attacker-{Guid.NewGuid():N}";
        var ownerProfileId = $"TP-{Guid.NewGuid():N}";
        db.PrivateSpeakingTutorProfiles.Add(NewProfile(ownerProfileId, ownerId, now));
        db.PrivateSpeakingTutorProfiles.Add(NewProfile($"TP-{Guid.NewGuid():N}", attackerId, now));
        await db.SaveChangesAsync(Ct);
        var service = CreateService(db, now);

        var created = await service.CreateTutorClassAsync(NewClassRequest(), ownerId, "Owner Tutor", Ct);

        // The created class is stamped with the caller's profile (the legacy tutor
        // create left TutorProfileId null, which left tutor-created classes unowned).
        var stored = await db.LiveClasses.AsNoTracking().FirstAsync(lc => lc.Id == created.Id, Ct);
        Assert.Equal(ownerProfileId, stored.TutorProfileId);

        // Ownership therefore resolves: the creator passes the guard, a foreign tutor does not.
        await service.EnsureTutorOwnsClassAsync(created.Id, ownerId, Ct);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsureTutorOwnsClassAsync(created.Id, attackerId, Ct));
        Assert.Equal("live_class_not_assigned", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateTutorClass_ForeignTutorBlocked_OwnerSucceeds_MissingClassNotFound()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var (classId, _, ownerId, attackerId) = await SeedOwnedClassAsync(db, now);
        var service = CreateService(db, now);
        var request = new TutorClassUpdateRequest(
            Title: "Renamed Class",
            TitleAr: null,
            Description: "Updated description.",
            DescriptionAr: null,
            CoverImageUrl: null,
            CreditCost: 3,
            DefaultCapacity: 25,
            DefaultDurationMinutes: 90,
            Tags: ["speaking", "b2"]);

        // A foreign tutor is rejected before any mutation happens.
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.UpdateTutorClassAsync(classId, request, attackerId, "Attacker Tutor", Ct));
        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal("live_class_not_assigned", ex.ErrorCode);
        Assert.Equal("Owned Class", (await db.LiveClasses.AsNoTracking().FirstAsync(lc => lc.Id == classId, Ct)).Title);

        // The owner's patch applies and returns the detail DTO shape.
        var detail = await service.UpdateTutorClassAsync(classId, request, ownerId, "Owner Tutor", Ct);
        Assert.Equal("Renamed Class", detail.Title);
        Assert.Equal(3, detail.CreditCost);
        Assert.Equal(25, detail.DefaultCapacity);
        Assert.Equal(90, detail.DefaultDurationMinutes);
        Assert.Contains("speaking", detail.Tags);

        var stored = await db.LiveClasses.AsNoTracking().FirstAsync(lc => lc.Id == classId, Ct);
        Assert.Equal("Renamed Class", stored.Title);
        Assert.Equal("Updated description.", stored.Description);
        Assert.Equal(3, stored.CreditCost);
        Assert.Equal(25, stored.DefaultCapacity);
        Assert.Equal(90, stored.DefaultDurationMinutes);

        // Null fields are "no change": a patch that only bumps capacity keeps the rest.
        var partial = new TutorClassUpdateRequest(null, null, null, null, null, null, DefaultCapacity: 30, null, null);
        var afterPartial = await service.UpdateTutorClassAsync(classId, partial, ownerId, "Owner Tutor", Ct);
        Assert.Equal(30, afterPartial.DefaultCapacity);
        Assert.Equal("Renamed Class", afterPartial.Title);

        // An unknown class id surfaces NotFound.
        var missing = await Assert.ThrowsAsync<ApiException>(
            () => service.UpdateTutorClassAsync($"LC-{Guid.NewGuid():N}", request, ownerId, "Owner Tutor", Ct));
        Assert.Equal(StatusCodes.Status404NotFound, missing.StatusCode);
        Assert.Equal("live_class_not_found", missing.ErrorCode);
    }

    [Fact]
    public async Task SessionAttendance_PopulatesLearnerDisplayNames()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var (_, sessionId, ownerId, attackerId) = await SeedOwnedClassAsync(db, now);
        db.Users.Add(new LearnerUser
        {
            Id = "learner-known",
            DisplayName = "Known Learner",
            Email = "known@test.com",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.LiveClassAttendances.AddRange(
            new LiveClassAttendance
            {
                Id = $"LCA-{Guid.NewGuid():N}",
                ClassSessionId = sessionId,
                UserId = "learner-known",
                JoinedAt = now.AddMinutes(-30),
                LeftAt = now,
                DurationSeconds = 1800,
            },
            new LiveClassAttendance
            {
                Id = $"LCA-{Guid.NewGuid():N}",
                ClassSessionId = sessionId,
                UserId = "learner-ghost", // no Users row — name stays null
                JoinedAt = now.AddMinutes(-20),
                DurationSeconds = 1200,
            });
        await db.SaveChangesAsync(Ct);
        var service = CreateService(db, now);

        var rows = await service.GetSessionAttendanceForTutorAsync(sessionId, ownerId, Ct);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Known Learner", rows[0].DisplayName);
        Assert.Equal("learner-known", rows[0].UserId);
        Assert.Equal(1800, rows[0].DurationSeconds);
        Assert.Null(rows[1].DisplayName);
        Assert.Equal("learner-ghost", rows[1].UserId);

        // Ownership is still enforced on the attendance read.
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.GetSessionAttendanceForTutorAsync(sessionId, attackerId, Ct));
        Assert.Equal("live_class_not_assigned", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateTutorClass_WithoutProfile_IsRejected()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(db, now);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateTutorClassAsync(NewClassRequest(), $"no-profile-{Guid.NewGuid():N}", "Tutor", Ct));
        Assert.Equal("tutor_profile_required", ex.ErrorCode);
    }

    private static async Task<(string classId, string sessionId, string ownerId, string attackerId)> SeedOwnedClassAsync(
        LearnerDbContext db,
        DateTimeOffset now)
    {
        var ownerId = $"lc-owner-{Guid.NewGuid():N}";
        var attackerId = $"lc-attacker-{Guid.NewGuid():N}";
        var classId = $"LC-{Guid.NewGuid():N}";
        var sessionId = $"LCS-{Guid.NewGuid():N}";
        var ownerProfileId = $"TP-{Guid.NewGuid():N}";

        db.PrivateSpeakingTutorProfiles.Add(NewProfile(ownerProfileId, ownerId, now));
        db.PrivateSpeakingTutorProfiles.Add(NewProfile($"TP-{Guid.NewGuid():N}", attackerId, now));
        db.LiveClasses.Add(new LiveClass
        {
            Id = classId,
            Slug = $"slug-{Guid.NewGuid():N}",
            Title = "Owned Class",
            Description = "A tutor-owned class.",
            ProfessionTrack = "All",
            Level = "All",
            DefaultDurationMinutes = 60,
            DefaultCapacity = 10,
            TutorProfileId = ownerProfileId,
            Status = LiveClassStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
            Sessions =
            {
                new LiveClassSession
                {
                    Id = sessionId,
                    LiveClassId = classId,
                    ScheduledStartAt = now.AddDays(1),
                    ScheduledEndAt = now.AddDays(1).AddHours(1),
                    Capacity = 10,
                    Status = LiveClassSessionStatus.Scheduled,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            },
        });
        await db.SaveChangesAsync(Ct);
        return (classId, sessionId, ownerId, attackerId);
    }

    private static PrivateSpeakingTutorProfile NewProfile(string id, string expertUserId, DateTimeOffset now)
        => new()
        {
            Id = id,
            ExpertUserId = expertUserId,
            DisplayName = "Tutor",
            Timezone = "UTC",
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static AdminLiveClassUpsertRequest NewClassRequest()
        => new(
            "Tutor Created Class",
            null,
            "A class created via the tutor portal.",
            null,
            "GroupClass",
            "All",
            "All",
            TutorProfileId: null, // CreateTutorClassAsync resolves and stamps the caller's profile.
            DateTimeOffset.UtcNow.AddDays(2),
            DurationMinutes: 60,
            Capacity: 10,
            CreditCost: 0,
            CoverImageUrl: null,
            Tags: null,
            AutoPublish: false);

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            // LiveClassService wraps cancel/enroll mutations in a Serializable
            // transaction. The in-memory provider has no transaction support and
            // throws on BeginTransactionAsync by default; treat that as a harmless
            // no-op so the guarded mutation flows exercise the real service logic.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static LiveClassService CreateService(LearnerDbContext db, DateTimeOffset now)
        => new(
            db,
            // Default ZoomOptions => integration disabled, so provisioning is a no-op
            // (no network) and the mutation flows stay deterministic.
            new ZoomMeetingService(
                new StaticHttpClientFactory(),
                TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
                NullLogger<ZoomMeetingService>.Instance),
            walletService: new WalletService(db, paymentGateways: null!, platformLinks: null!, billingOptions: Options.Create(new BillingOptions())),
            notificationService: new NotificationService(
                db,
                emailSender: null!,
                webPushDispatcher: null!,
                mobilePushDispatcher: null!,
                hubContext: null!,
                platformLinks: null!,
                timeProvider: new FixedTimeProvider(now),
                webPushOptions: Options.Create(new WebPushOptions()),
                runtimeSettingsProvider: TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions()),
                notificationProofOptions: Options.Create(new NotificationProofHarnessOptions()),
                environment: null!,
                logger: NullLogger<NotificationService>.Instance),
            fileStorage: new TestFileStorage(),
            new FixedTimeProvider(now),
            NullLogger<LiveClassService>.Instance);

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestFileStorage : IFileStorage
    {
        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => Task.FromResult(0L);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0L);
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => new($"/test-media/{Uri.EscapeDataString(key)}", UriKind.Relative);
    }
}
