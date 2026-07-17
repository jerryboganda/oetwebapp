using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Admin;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Verifies the model-driven user hard-delete on a real relational (SQLite) DB, where
/// FK constraints are enforced — so the dependents-first ordering and the
/// LearnerUser-before-ApplicationUserAccount sequencing are actually exercised.
/// </summary>
public sealed class UserHardDeleteServiceTests
{
    private static (LearnerDbContext db, SqliteConnection conn) NewDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    private static LearnerGoal NewGoal(string userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ProfessionId = "nursing",
        WeakSubtestsJson = "[]",
        StudyHoursPerWeek = 8,
        UpdatedAt = DateTimeOffset.UtcNow,
        ExamTypeCode = "OET",
        ExamFamilyCode = "OET",
    };

    [Fact]
    public async Task PurgeAsync_removes_user_account_and_all_referencing_rows_but_spares_other_users()
    {
        var (db, conn) = NewDb();
        try
        {
            db.ApplicationUserAccounts.Add(new ApplicationUserAccount { Id = "acc-1", Email = "a@x.com", NormalizedEmail = "A@X.COM", PasswordHash = "h" });
            db.ApplicationUserAccounts.Add(new ApplicationUserAccount { Id = "acc-2", Email = "b@x.com", NormalizedEmail = "B@X.COM", PasswordHash = "h" });
            db.Users.Add(new LearnerUser { Id = "user-1", AuthAccountId = "acc-1", DisplayName = "U1", Email = "a@x.com" });
            db.Users.Add(new LearnerUser { Id = "user-2", AuthAccountId = "acc-2", DisplayName = "U2", Email = "b@x.com" });
            db.Goals.Add(NewGoal("user-1"));
            db.Goals.Add(NewGoal("user-2"));
            db.MockAttempts.Add(new MockAttempt { Id = "ma-1", UserId = "user-1" });
            db.MockAttempts.Add(new MockAttempt { Id = "ma-2", UserId = "user-2" });
            await db.SaveChangesAsync();

            var report = await new UserHardDeleteService(db, NullLogger<UserHardDeleteService>.Instance)
                .PurgeAsync("user-1", default);

            // user-1 and everything referencing it is gone
            Assert.False(await db.Users.AsNoTracking().AnyAsync(u => u.Id == "user-1"));
            Assert.False(await db.ApplicationUserAccounts.AsNoTracking().AnyAsync(a => a.Id == "acc-1"));
            Assert.False(await db.Goals.AsNoTracking().AnyAsync(g => g.UserId == "user-1"));
            Assert.False(await db.MockAttempts.AsNoTracking().AnyAsync(m => m.UserId == "user-1"));

            // user-2 is completely untouched
            Assert.True(await db.Users.AsNoTracking().AnyAsync(u => u.Id == "user-2"));
            Assert.True(await db.ApplicationUserAccounts.AsNoTracking().AnyAsync(a => a.Id == "acc-2"));
            Assert.True(await db.Goals.AsNoTracking().AnyAsync(g => g.UserId == "user-2"));
            Assert.True(await db.MockAttempts.AsNoTracking().AnyAsync(m => m.UserId == "user-2"));

            Assert.Contains("LearnerUser", report.Keys);
            Assert.Contains("ApplicationUserAccount", report.Keys);
            Assert.True(report.Values.Sum() >= 4);
        }
        finally
        {
            await db.DisposeAsync();
            conn.Dispose();
        }
    }
}
