using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// TeacherClassService — OWASP A01 cross-user authorization tests.
/// Every read/write path MUST refuse access if the caller is not the
/// owner. These tests pin the behavior so accidental future regressions
/// (e.g., dropping an OwnerUserId filter) fail fast.
/// </summary>
public class TeacherClassServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static TeacherClassService NewSvc(LearnerDbContext db)
        => new(db, TimeProvider.System);

    [Fact]
    public async Task ListMineAsync_returns_only_caller_owned_classes()
    {
        await using var db = NewDb();
        var svc = NewSvc(db);
        await svc.CreateAsync("teacher-A", "Class A", null, CancellationToken.None);
        await svc.CreateAsync("teacher-B", "Class B", null, CancellationToken.None);

        var mineA = await svc.ListMineAsync("teacher-A", CancellationToken.None);
        var mineB = await svc.ListMineAsync("teacher-B", CancellationToken.None);

        Assert.Single(mineA);
        Assert.Single(mineB);
        Assert.Equal("Class A", mineA[0].Name);
        Assert.Equal("Class B", mineB[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_refuses_non_owner()
    {
        await using var db = NewDb();
        var svc = NewSvc(db);
        var cls = await svc.CreateAsync("teacher-A", "Class A", null, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.DeleteAsync("teacher-B", cls.Id, CancellationToken.None));

        // Still present after the rejected delete.
        Assert.Single(await svc.ListMineAsync("teacher-A", CancellationToken.None));
    }

    [Fact]
    public async Task AddMemberAsync_refuses_non_owner()
    {
        await using var db = NewDb();
        var svc = NewSvc(db);
        var cls = await svc.CreateAsync("teacher-A", "Class A", null, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.AddMemberAsync("teacher-B", cls.Id, "learner-1", CancellationToken.None));
    }

    [Fact]
    public async Task AddMemberAsync_is_idempotent_for_owner()
    {
        await using var db = NewDb();
        var svc = NewSvc(db);
        var cls = await svc.CreateAsync("teacher-A", "Class A", null, CancellationToken.None);
        SeedLearner(db, "learner-1");
        await db.SaveChangesAsync();

        await svc.AddMemberAsync("teacher-A", cls.Id, "learner-1", CancellationToken.None);
        await svc.AddMemberAsync("teacher-A", cls.Id, "learner-1", CancellationToken.None);
        var members = await svc.ListMemberUserIdsAsync("teacher-A", cls.Id, CancellationToken.None);
        Assert.Single(members);
    }

    [Fact]
    public async Task AddMemberAsync_refuses_unknown_learner_id()
    {
        await using var db = NewDb();
        var svc = NewSvc(db);
        var cls = await svc.CreateAsync("teacher-A", "Class A", null, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.AddMemberAsync("teacher-A", cls.Id, "missing-learner", CancellationToken.None));
    }

    private static void SeedLearner(LearnerDbContext db, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = userId,
            Email = $"{userId}@example.test",
            Timezone = "UTC",
            Locale = "en-AU",
            ActiveProfessionId = "medicine",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        });
    }
}
