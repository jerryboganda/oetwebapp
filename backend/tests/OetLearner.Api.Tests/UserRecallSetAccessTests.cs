using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Covers the per-user Recall-set allow-list added in Phase E (owner directive): a
/// learner with ANY <see cref="UserRecallSetAccess"/> rows sees only vocabulary terms
/// whose RecallSetCodesJson intersects those set codes; a learner with NO rows (or
/// when no userId is supplied at all) is unchanged. Enforced in
/// <see cref="VocabularyService.GetTermsAsync"/>.
/// </summary>
public class UserRecallSetAccessTests
{
    private static (LearnerDbContext db, VocabularyService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new VocabularyService(db, new Sm2Scheduler()));
    }

    private static async Task SeedAsync(LearnerDbContext db)
    {
        db.VocabularyTerms.AddRange(
            new VocabularyTerm
            {
                Id = "vt-a",
                Term = "alpha",
                Definition = "def alpha",
                ExampleSentence = "Example alpha.",
                ExamTypeCode = "oet",
                Category = "medical",
                Status = "active",
                RecallSetCodesJson = "[\"set-a\"]",
            },
            new VocabularyTerm
            {
                Id = "vt-b",
                Term = "bravo",
                Definition = "def bravo",
                ExampleSentence = "Example bravo.",
                ExamTypeCode = "oet",
                Category = "medical",
                Status = "active",
                RecallSetCodesJson = "[\"set-b\"]",
            },
            new VocabularyTerm
            {
                Id = "vt-c",
                Term = "charlie",
                Definition = "def charlie",
                ExampleSentence = "Example charlie.",
                ExamTypeCode = "oet",
                Category = "medical",
                Status = "active",
                RecallSetCodesJson = "[]",
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Learner_WithAllowList_SeesOnlyGrantedSetTerms()
    {
        var (db, svc) = Build();
        await SeedAsync(db);
        db.UserRecallSetAccesses.Add(new UserRecallSetAccess
        {
            Id = "ura-1",
            UserId = "learner-restricted",
            RecallSetCode = "set-a",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var page = await svc.GetTermsAsync(
            "oet", null, null, null, page: 1, pageSize: 20, ct: default,
            userId: "learner-restricted");

        Assert.Equal(1, page.Total);
        var only = Assert.Single(page.Terms);
        Assert.Equal("vt-a", only.Id);
    }

    [Fact]
    public async Task Learner_WithNoAllowListRows_IsUnrestricted()
    {
        var (db, svc) = Build();
        await SeedAsync(db);
        // No UserRecallSetAccess rows for this learner.

        var page = await svc.GetTermsAsync(
            "oet", null, null, null, page: 1, pageSize: 20, ct: default,
            userId: "learner-unrestricted");

        Assert.Equal(3, page.Total);
        Assert.Contains(page.Terms, t => t.Id == "vt-a");
        Assert.Contains(page.Terms, t => t.Id == "vt-b");
        Assert.Contains(page.Terms, t => t.Id == "vt-c");
    }

    [Fact]
    public async Task OmittingUserId_PreservesLegacyUnrestrictedBehaviour()
    {
        var (db, svc) = Build();
        await SeedAsync(db);
        // This learner DOES have a restriction, but the caller below doesn't pass
        // userId at all — proves the new parameter is purely additive and doesn't
        // change any existing (not-yet-threaded) call site's behaviour.
        db.UserRecallSetAccesses.Add(new UserRecallSetAccess
        {
            Id = "ura-2",
            UserId = "learner-restricted-2",
            RecallSetCode = "set-a",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var page = await svc.GetTermsAsync("oet", null, null, null, 1, 20, default);

        Assert.Equal(3, page.Total);
    }
}
