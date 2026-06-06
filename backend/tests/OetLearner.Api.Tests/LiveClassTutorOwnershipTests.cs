using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.LiveClasses;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Object-level authorization regression tests for the tutor live-class portal.
/// The tutor session/class mutation endpoints (`/v1/tutor/me/classes/*`) reuse
/// admin-surface LiveClassService methods that load by id with no owner check —
/// so any tutor could edit/cancel/add sessions to another tutor's classes. The
/// fix adds ownership guards scoped to LiveClass.TutorProfile.ExpertUserId.
/// </summary>
public class LiveClassTutorOwnershipTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LiveClassTutorOwnershipTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EnsureTutorOwns_RejectsForeignTutor_AllowsOwner()
    {
        var ownerId = $"lc-owner-{Guid.NewGuid():N}";
        var attackerId = $"lc-attacker-{Guid.NewGuid():N}";
        var (classId, sessionId) = await SeedOwnedClassAsync(ownerId, attackerId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<LiveClassService>();

        // A foreign tutor is forbidden from the class and its sessions.
        var sessionEx = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsureTutorOwnsSessionAsync(sessionId, attackerId, CancellationToken.None));
        Assert.Equal("live_class_not_assigned", sessionEx.ErrorCode);

        var classEx = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsureTutorOwnsClassAsync(classId, attackerId, CancellationToken.None));
        Assert.Equal("live_class_not_assigned", classEx.ErrorCode);

        // The owning tutor is allowed (no throw).
        await service.EnsureTutorOwnsSessionAsync(sessionId, ownerId, CancellationToken.None);
        await service.EnsureTutorOwnsClassAsync(classId, ownerId, CancellationToken.None);
    }

    private async Task<(string classId, string sessionId)> SeedOwnedClassAsync(string ownerId, string attackerId)
    {
        var classId = $"LC-{Guid.NewGuid():N}";
        var sessionId = $"LCS-{Guid.NewGuid():N}";
        var ownerProfileId = $"TP-{Guid.NewGuid():N}";
        var attackerProfileId = $"TP-{Guid.NewGuid():N}";
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;

        db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
        {
            Id = ownerProfileId, ExpertUserId = ownerId, DisplayName = "Owner Tutor", CreatedAt = now, UpdatedAt = now
        });
        db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
        {
            Id = attackerProfileId, ExpertUserId = attackerId, DisplayName = "Attacker Tutor", CreatedAt = now, UpdatedAt = now
        });
        db.LiveClasses.Add(new LiveClass
        {
            Id = classId,
            Slug = $"slug-{Guid.NewGuid():N}",
            Title = "Owned Class",
            TutorProfileId = ownerProfileId,
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
                    UpdatedAt = now
                }
            }
        });
        await db.SaveChangesAsync();
        return (classId, sessionId);
    }
}
