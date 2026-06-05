using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Regression coverage for the Listening authoring HTTP 500
/// ("An error occurred while saving the entity changes") that fired whenever
/// the acting admin id was NOT a row in <c>ApplicationUserAccounts</c>.
///
/// Root cause: every Listening authoring audit-write set
/// <c>AuditEvent.ActorAuthAccountId = adminId</c>, but that column carries a
/// foreign key to <c>ApplicationUserAccounts.Id</c>
/// (<c>FK_AuditEvents_ApplicationUserAccounts_ActorAuthAccountId</c>). Under
/// dev-auth the caller id is the raw <c>X-Debug-UserId</c> header — and seeded
/// / legacy admins never had an account row — so the FK was dangling and
/// <c>SaveChanges</c> rejected the whole write.
///
/// These tests deliberately use the <b>SQLite</b> provider rather than the EF
/// in-memory provider: the in-memory provider does NOT enforce referential
/// integrity, so it cannot reproduce (or guard against) this bug. SQLite (like
/// the production Postgres) enforces foreign keys, matching real behaviour.
/// </summary>
public sealed class ListeningAuditActorFkTests
{
    private const string UnmatchedAdminId = "X-Debug-UserId-not-an-account";

    private static DbContextOptions<LearnerDbContext> SqliteOptions(SqliteConnection connection)
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

    private static async Task<ContentPaper> SeedListeningPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Audit FK Listening Paper",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            // ReplaceStructureAsync enforces provenance at mutation time.
            SourceProvenance = "source=unit-test; legal=original-authoring-attested",
            ExtractedTextJson = string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    private static IReadOnlyList<ListeningAuthoredQuestion> OneShortAnswerQuestion() =>
    [
        new ListeningAuthoredQuestion(
            Id: "lq-1",
            Number: 1,
            PartCode: "A1",
            Type: "short_answer",
            Stem: "Patient surname?",
            Options: null,
            CorrectAnswer: "Bell",
            AcceptedAnswers: null,
            Explanation: null,
            SkillTag: "note_completion",
            TranscriptExcerpt: null,
            DistractorExplanation: null,
            Points: 1),
    ];

    /// <summary>
    /// The fix: authoring a Listening structure as an admin whose id is NOT an
    /// ApplicationUserAccounts row must succeed (no FK-violation 500) and still
    /// write the audit event — with a NULL FK but the raw caller id preserved
    /// in the non-FK <c>ActorId</c> column for traceability.
    /// </summary>
    [Fact]
    public async Task ReplaceStructure_AdminWithoutAccountRow_Succeeds_AndWritesAuditWithNullActorFk()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = SqliteOptions(connection);

        await using (var seedDb = new LearnerDbContext(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            // ApplicationUserAccounts is intentionally left EMPTY — the acting
            // admin id below corresponds to no account.
            Assert.False(await seedDb.ApplicationUserAccounts.AnyAsync());
        }

        ContentPaper paper;
        await using (var arrangeDb = new LearnerDbContext(options))
        {
            paper = await SeedListeningPaperAsync(arrangeDb);
        }

        await using (var actDb = new LearnerDbContext(options))
        {
            var svc = new ListeningAuthoringService(actDb, new NoopBackfillService());

            // Before the fix this threw DbUpdateException → surfaced as HTTP 500.
            var result = await svc.ReplaceStructureAsync(
                paper.Id, OneShortAnswerQuestion(), UnmatchedAdminId, default);

            Assert.Single(result.Questions);
        }

        await using (var assertDb = new LearnerDbContext(options))
        {
            var audit = await assertDb.AuditEvents
                .SingleAsync(a => a.Action == "ListeningStructureUpdated");

            // FK column resolved to null because the actor is not an account…
            Assert.Null(audit.ActorAuthAccountId);
            // …but the raw caller id is still captured for traceability.
            Assert.Equal(UnmatchedAdminId, audit.ActorId);
            Assert.Equal("ContentPaper", audit.ResourceType);
            Assert.Equal(paper.Id, audit.ResourceId);
        }
    }

    /// <summary>
    /// When the acting admin DOES have an ApplicationUserAccounts row, the FK is
    /// preserved (the resolver returns the real id), so audit attribution still
    /// links to the account.
    /// </summary>
    [Fact]
    public async Task ReplaceStructure_AdminWithAccountRow_PreservesActorFk()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = SqliteOptions(connection);

        const string realAdminId = "acct-admin-1";
        var now = DateTimeOffset.UtcNow;

        ContentPaper paper;
        await using (var seedDb = new LearnerDbContext(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            seedDb.ApplicationUserAccounts.Add(new ApplicationUserAccount
            {
                Id = realAdminId,
                Email = "admin@oet-prep.dev",
                NormalizedEmail = "ADMIN@OET-PREP.DEV",
                PasswordHash = "x",
                Role = ApplicationUserRoles.Admin,
                EmailVerifiedAt = now,
            });
            await seedDb.SaveChangesAsync();
            paper = await SeedListeningPaperAsync(seedDb);
        }

        await using (var actDb = new LearnerDbContext(options))
        {
            var svc = new ListeningAuthoringService(actDb, new NoopBackfillService());
            await svc.ReplaceStructureAsync(
                paper.Id, OneShortAnswerQuestion(), realAdminId, default);
        }

        await using (var assertDb = new LearnerDbContext(options))
        {
            var audit = await assertDb.AuditEvents
                .SingleAsync(a => a.Action == "ListeningStructureUpdated");
            Assert.Equal(realAdminId, audit.ActorAuthAccountId);
            Assert.Equal(realAdminId, audit.ActorId);
        }
    }

    /// <summary>
    /// Documents the bug and proves the test provider actually enforces the FK:
    /// writing an AuditEvent whose <c>ActorAuthAccountId</c> points at a
    /// non-existent account must be rejected by the database. This is exactly
    /// what the resolver prevents at every authoring call-site.
    /// </summary>
    [Fact]
    public async Task DanglingActorAuthAccountId_IsRejectedByDatabase_ProvingFkEnforced()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = SqliteOptions(connection);

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = UnmatchedAdminId,
            ActorAuthAccountId = UnmatchedAdminId, // dangling FK — the original bug
            ActorName = UnmatchedAdminId,
            Action = "ListeningStructureUpdated",
            ResourceType = "ContentPaper",
            ResourceId = "paper-1",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private sealed class NoopBackfillService : IListeningBackfillService
    {
        public Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, CancellationToken ct)
            => Task.FromResult(new ListeningBackfillReport(paperId, true, 0, 0, 0, 0, null));

        public Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, bool bypassAttemptsGuard, CancellationToken ct)
            => BackfillPaperAsync(paperId, adminId, ct);

        public Task<IReadOnlyList<ListeningBackfillReport>> BackfillAllAsync(string adminId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ListeningBackfillReport>>([]);
    }
}
