using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests.Billing;

public class AccountLookupExtensionsTests
{
    [Theory]
    [InlineData("learner_1", "auth_1")] // learner token subject: the domain id, linked by AuthAccountId
    [InlineData("auth_admin", "auth_admin")] // admin token subject: the account id itself
    [InlineData("nobody", null)]
    public async Task AccountsForUserResolvesEveryKindOfSubject(string subject, string? expectedAccountId)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"{nameof(AccountsForUserResolvesEveryKindOfSubject)}-{subject}")
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        db.ApplicationUserAccounts.AddRange(
            new ApplicationUserAccount { Id = "auth_1", Email = "l@example.test", NormalizedEmail = "L@EXAMPLE.TEST", PasswordHash = "x", CreatedAt = now },
            new ApplicationUserAccount { Id = "auth_admin", Email = "a@example.test", NormalizedEmail = "A@EXAMPLE.TEST", PasswordHash = "x", Role = ApplicationUserRoles.Admin, CreatedAt = now });
        db.Users.Add(new LearnerUser { Id = "learner_1", AuthAccountId = "auth_1", DisplayName = "Learner", Email = "l@example.test", CreatedAt = now, LastActiveAt = now });
        await db.SaveChangesAsync();

        var account = await db.AccountsForUser(subject).FirstOrDefaultAsync();

        Assert.Equal(expectedAccountId, account?.Id);
    }
}
