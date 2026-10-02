using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public static class AccountLookupExtensions
{
    /// <summary>
    /// The auth account behind a token subject or domain user id. A learner's or
    /// expert's subject is its domain id (learner_…), linked by AuthAccountId; an
    /// admin's subject is the account id itself (AuthService.ResolveSubjectAsync).
    /// Matching only <c>Id == userId</c> finds nothing for every learner.
    /// </summary>
    public static IQueryable<ApplicationUserAccount> AccountsForUser(this LearnerDbContext db, string userId)
        => db.ApplicationUserAccounts.Where(a => a.Id == userId
            || db.Users.Any(u => u.Id == userId && u.AuthAccountId == a.Id)
            || db.ExpertUsers.Any(e => e.Id == userId && e.AuthAccountId == a.Id));
}
