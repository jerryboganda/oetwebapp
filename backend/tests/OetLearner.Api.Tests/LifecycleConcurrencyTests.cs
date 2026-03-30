using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests;

public class LifecycleConcurrencyTests
{
    [Fact]
    public void LearnerUser_AccountStatus_IsConfiguredAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new LearnerDbContext(options);
        var learnerEntity = db.Model.FindEntityType(typeof(LearnerUser));

        Assert.NotNull(learnerEntity);
        var accountStatus = learnerEntity!.FindProperty(nameof(LearnerUser.AccountStatus));
        Assert.NotNull(accountStatus);
        Assert.True(accountStatus!.IsConcurrencyToken);
    }

    [Fact]
    public async Task LearnerUser_StaleLifecycleWrite_ThrowsConcurrencyException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        var now = new DateTimeOffset(2026, 03, 30, 12, 0, 0, TimeSpan.Zero);
        await using (var seedDb = new LearnerDbContext(options))
        {
            await seedDb.Database.EnsureCreatedAsync();

            seedDb.Users.Add(new LearnerUser
            {
                Id = "usr-001",
                DisplayName = "Learner One",
                Email = "learner@example.test",
                CreatedAt = now,
                LastActiveAt = now,
                AccountStatus = "active"
            });

            await seedDb.SaveChangesAsync();
        }

        await using var firstDb = new LearnerDbContext(options);
        await using var secondDb = new LearnerDbContext(options);

        var firstLearner = await firstDb.Users.SingleAsync(x => x.Id == "usr-001");
        var secondLearner = await secondDb.Users.SingleAsync(x => x.Id == "usr-001");

        secondLearner.AccountStatus = "deleted";
        await secondDb.SaveChangesAsync();

        firstDb.Entry(firstLearner).Property(x => x.AccountStatus).IsModified = true;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => firstDb.SaveChangesAsync());
    }

    [Fact]
    public void ExpertUser_IsActive_IsConfiguredAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new LearnerDbContext(options);
        var expertEntity = db.Model.FindEntityType(typeof(ExpertUser));

        Assert.NotNull(expertEntity);
        var isActive = expertEntity!.FindProperty(nameof(ExpertUser.IsActive));
        Assert.NotNull(isActive);
        Assert.True(isActive!.IsConcurrencyToken);
    }

    [Fact]
    public async Task ExpertUser_StaleLifecycleWrite_ThrowsConcurrencyException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        var now = new DateTimeOffset(2026, 03, 30, 12, 0, 0, TimeSpan.Zero);
        await using (var seedDb = new LearnerDbContext(options))
        {
            await seedDb.Database.EnsureCreatedAsync();

            seedDb.ExpertUsers.Add(new ExpertUser
            {
                Id = "expert-001",
                DisplayName = "Expert One",
                Email = "expert@example.test",
                CreatedAt = now,
                IsActive = true
            });

            await seedDb.SaveChangesAsync();
        }

        await using var firstDb = new LearnerDbContext(options);
        await using var secondDb = new LearnerDbContext(options);

        var firstExpert = await firstDb.ExpertUsers.SingleAsync(x => x.Id == "expert-001");
        var secondExpert = await secondDb.ExpertUsers.SingleAsync(x => x.Id == "expert-001");

        secondExpert.IsActive = false;
        await secondDb.SaveChangesAsync();

        firstDb.Entry(firstExpert).Property(x => x.IsActive).IsModified = true;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => firstDb.SaveChangesAsync());
    }
}