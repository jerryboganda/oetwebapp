using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.FreeSamples;

namespace OetLearner.Api.Tests.FreeSamples;

/// <summary>
/// Free Mocks (owner 2026-09-22) — the per-learner free AI-graded Writing/Speaking
/// sample: which item is offered per profession, the once-only claim lifecycle
/// (spent only once its attempt is graded; a failed grade leaves it re-usable),
/// and the dark-launch flag. Logic tests run on InMemory; the unique-index test
/// runs on SQLite (InMemory ignores unique indexes).
/// </summary>
public sealed class FreeSampleServiceTests
{
    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    internal static async Task EnableAsync(LearnerDbContext db, bool enabled = true)
    {
        var now = DateTimeOffset.UtcNow;
        db.FeatureFlags.Add(new FeatureFlag
        {
            Id = $"ff-{Guid.NewGuid():N}",
            Name = "Free samples",
            Key = FreeSampleService.FeatureFlagKey,
            Enabled = enabled,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    internal static async Task<Guid> SeedScenarioAsync(
        LearnerDbContext db, string profession, string title, int difficulty = 3,
        string status = "published", bool loadable = true)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = id,
            Title = title,
            LetterType = "LT-RR",
            Profession = profession,
            Difficulty = difficulty,
            Status = status,
            AuthorId = "admin-1",
            TaskPromptMarkdown = loadable ? "Write a referral letter." : null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        if (loadable)
        {
            db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = id,
                Ordinal = 0,
                SentenceText = "Patient has chest pain.",
                CreatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedSubmissionAsync(
        LearnerDbContext db, string userId, Guid scenarioId, string status, Guid? id = null)
    {
        var submissionId = id ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = submissionId,
            UserId = userId,
            ScenarioId = scenarioId,
            LetterContent = "Dear Dr Smith,",
            LetterContentHash = "hash",
            WordCount = 3,
            StartedAt = now,
            SubmittedAt = now,
            CreatedAt = now,
            Status = status,
        });
        await db.SaveChangesAsync();
        return submissionId;
    }

    internal static async Task<(string ContentItemId, string CardId)> SeedCardAsync(
        LearnerDbContext db, string profession, int? cardNumber = null,
        ContentStatus cardStatus = ContentStatus.Published, ContentStatus itemStatus = ContentStatus.Published)
    {
        var now = DateTimeOffset.UtcNow;
        var itemId = $"ci-{Guid.NewGuid():N}";
        db.ContentItems.Add(new ContentItem
        {
            Id = itemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = profession,
            Title = "Test role play",
            Difficulty = "core",
            Status = itemStatus,
            PublishedRevisionId = $"{itemId}-r1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
        });
        var cardId = $"rpc-{Guid.NewGuid():N}";
        db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = itemId,
            ProfessionId = profession,
            ScenarioTitle = "Test role play",
            Setting = "Clinic",
            CandidateRole = "Nurse",
            InterlocutorRole = "Patient",
            Background = "Background detail",
            Task1 = "Task 1",
            PatientEmotion = "neutral",
            CommunicationGoal = "Inform",
            ClinicalTopic = "general",
            Difficulty = "core",
            PrimaryCategory = "First Visit",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = cardStatus,
            DisplayCardNumber = cardNumber,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        await db.SaveChangesAsync();
        return (itemId, cardId);
    }

    private static async Task<string> SeedAttemptAsync(LearnerDbContext db, string userId, string contentId, AttemptState state)
    {
        var id = $"sa-{Guid.NewGuid():N}";
        db.Attempts.Add(new Attempt
        {
            Id = id,
            UserId = userId,
            ContentId = contentId,
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "self",
            State = state,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceType = "web",
        });
        await db.SaveChangesAsync();
        return id;
    }

    // ── dark launch ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WithoutTheFlag_NothingIsOffered_AndNothingCanBeClaimed()
    {
        await using var db = NewDb();
        var scenario = await SeedScenarioAsync(db, "medicine", "Task A");
        var svc = new FreeSampleService(db);

        // Absent flag row = OFF (fail closed): shipping the code changes nothing.
        Assert.Empty(await svc.ListAsync("u1", "writing", default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", scenario.ToString("D"), default));
        Assert.False(await svc.TryClaimAsync("u1", "writing", scenario.ToString("D"), "sub-1", default));

        await EnableAsync(db, enabled: false);
        Assert.Empty(await svc.ListAsync("u1", "writing", default));
        Assert.Empty(db.FreeSampleClaims);
    }

    [Fact]
    public async Task UnknownSubtest_OffersNothing()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        Assert.Empty(await new FreeSampleService(db).ListAsync("u1", "reading", default));
    }

    // ── Writing: designation + auto-pick ─────────────────────────────────────

    [Fact]
    public async Task Writing_AutoPicksTheLowestOrderLiveScenario_PerProfession()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var hard = await SeedScenarioAsync(db, "medicine", "Zulu task", difficulty: 4);
        var easy = await SeedScenarioAsync(db, "Medicine", "Alpha task", difficulty: 1);          // mixed-case stored profession
        await SeedScenarioAsync(db, "medicine", "Draft task", difficulty: 0, status: "draft");    // not live
        await SeedScenarioAsync(db, "nursing", "Nursing task", loadable: false);                   // published but cannot render
        var dentistry = await SeedScenarioAsync(db, "dentistry", "Dental task");
        _ = hard;

        var offers = await new FreeSampleService(db).ListAsync("u1", "writing", default);

        Assert.Equal(new[] { "dentistry", "medicine" }, offers.Select(o => o.ProfessionId).ToArray());
        Assert.Equal(dentistry.ToString("D"), offers.Single(o => o.ProfessionId == "dentistry").ContentId);
        Assert.Equal(easy.ToString("D"), offers.Single(o => o.ProfessionId == "medicine").ContentId);
        Assert.All(offers, o => Assert.Equal(FreeSampleService.StateAvailable, o.State));
    }

    [Fact]
    public async Task Writing_AnAdminDesignationWinsWhileLive_AndAStaleOneFallsBackToAutoPick()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var first = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var second = await SeedScenarioAsync(db, "medicine", "Bravo", difficulty: 2);
        var nursingId = await SeedScenarioAsync(db, "nursing", "Nurse", difficulty: 1);
        _ = nursingId;
        db.FreeSampleDesignations.Add(new FreeSampleDesignation
        {
            Id = "fsd-1", Subtest = "writing", Profession = "medicine", ContentId = second.ToString("D"), UpdatedAt = DateTimeOffset.UtcNow,
        });
        // A designation pointing at ANOTHER profession's item must never be honoured.
        db.FreeSampleDesignations.Add(new FreeSampleDesignation
        {
            Id = "fsd-2", Subtest = "writing", Profession = "dentistry", ContentId = second.ToString("D"), UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = new FreeSampleService(db);

        var offers = await svc.ListAsync("u1", "writing", default);
        Assert.Equal(second.ToString("D"), offers.Single(o => o.ProfessionId == "medicine").ContentId);
        Assert.DoesNotContain(offers, o => o.ProfessionId == "dentistry");

        // The designated item is archived -> the profession falls back to the auto-pick.
        (await db.WritingScenarios.SingleAsync(s => s.Id == second)).Status = "archived";
        await db.SaveChangesAsync();
        offers = await svc.ListAsync("u1", "writing", default);
        Assert.Equal(first.ToString("D"), offers.Single(o => o.ProfessionId == "medicine").ContentId);
    }

    [Fact]
    public async Task Writing_OnlyTheOfferedScenarioIsFree()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var picked = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var other = await SeedScenarioAsync(db, "medicine", "Bravo", difficulty: 2);
        var svc = new FreeSampleService(db);

        Assert.True(await svc.IsOfferedAsync("u1", "writing", picked.ToString("D"), default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", other.ToString("D"), default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", "not-a-guid", default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", Guid.NewGuid().ToString("D"), default));
        Assert.False(await svc.TryClaimAsync("u1", "writing", other.ToString("D"), Guid.NewGuid().ToString("N"), default));
        Assert.Empty(db.FreeSampleClaims);
    }

    // ── Writing: claim lifecycle ─────────────────────────────────────────────

    [Fact]
    public async Task Writing_TheClaimIsOnceOnly_ReusableAfterAFailedGrade_AndSpentWhenGraded()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var scenario = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var content = scenario.ToString("D");
        var svc = new FreeSampleService(db);

        var first = await SeedSubmissionAsync(db, "u1", scenario, "grading");
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, first.ToString("N"), default));
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, first.ToString("N"), default)); // retry-grade is idempotent
        Assert.True(await svc.IsOfferedAsync("u1", "writing", content, default));                     // in progress: may continue
        Assert.Equal(FreeSampleService.StateInProgress, (await svc.ListAsync("u1", "writing", default)).Single().State);

        // Another submission while the first is still grading gets nothing free.
        var second = Guid.NewGuid();
        Assert.False(await svc.TryClaimAsync("u1", "writing", content, second.ToString("N"), default));

        // The first grade FAILED: the learner's one sample is still unspent and moves on.
        (await db.WritingSubmissions.SingleAsync(s => s.Id == first)).Status = WritingSubmissionStatuses.Failed;
        await db.SaveChangesAsync();
        await SeedSubmissionAsync(db, "u1", scenario, "grading", second);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, second.ToString("N"), default));
        Assert.True(await svc.IsFreeAttemptAsync("u1", "writing", second.ToString("N"), default));
        Assert.False(await svc.IsFreeAttemptAsync("u1", "writing", first.ToString("N"), default));

        // Graded: spent for good.
        (await db.WritingSubmissions.SingleAsync(s => s.Id == second)).Status = WritingSubmissionStatuses.Graded;
        await db.SaveChangesAsync();
        Assert.False(await svc.IsOfferedAsync("u1", "writing", content, default));
        Assert.False(await svc.TryClaimAsync("u1", "writing", content, Guid.NewGuid().ToString("N"), default));
        var used = (await svc.ListAsync("u1", "writing", default)).Single();
        Assert.Equal(FreeSampleService.StateUsed, used.State);
        Assert.Equal("medicine", used.ProfessionId);
    }

    [Fact]
    public async Task Writing_TheClaimIsPerLearnerAndPerSubtest()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var scenario = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var (_, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        var svc = new FreeSampleService(db);

        Assert.True(await svc.TryClaimAsync("u1", "writing", scenario.ToString("D"), Guid.NewGuid().ToString("N"), default));
        Assert.True(await svc.TryClaimAsync("u2", "writing", scenario.ToString("D"), Guid.NewGuid().ToString("N"), default));
        Assert.True(await svc.TryClaimAsync("u1", "speaking", card, "sa-1", default)); // Writing does not use up Speaking
    }

    // ── Speaking ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Speaking_OffersOnePublishedCardPerProfession_ByCardOrContentItemId()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (medItem, medCard) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        await SeedCardAsync(db, "medicine", cardNumber: 2);
        var (_, nurseCard) = await SeedCardAsync(db, "nursing", cardNumber: 5);
        await SeedCardAsync(db, "pharmacy", itemStatus: ContentStatus.Draft);               // shell not published: grading would fail
        await SeedCardAsync(db, "dentistry", cardStatus: ContentStatus.Draft);              // card not published
        var svc = new FreeSampleService(db);

        var offers = await svc.ListAsync("u1", "speaking", default);

        Assert.Equal(new[] { "medicine", "nursing" }, offers.Select(o => o.ProfessionId).ToArray());
        Assert.Equal(medCard, offers.Single(o => o.ProfessionId == "medicine").ContentId);
        Assert.Equal(nurseCard, offers.Single(o => o.ProfessionId == "nursing").ContentId);
        Assert.True(await svc.IsOfferedAsync("u1", "speaking", medCard, default));
        Assert.True(await svc.IsOfferedAsync("u1", "speaking", medItem, default)); // the attempt API may pass either id
    }

    [Fact]
    public async Task Speaking_TheClaimLifecycle_FollowsTheAttemptState()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (item, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        var svc = new FreeSampleService(db);

        var first = await SeedAttemptAsync(db, "u1", item, AttemptState.InProgress);
        Assert.True(await svc.TryClaimAsync("u1", "speaking", card, first, default));
        Assert.True(await svc.IsFreeAttemptAsync("u1", "speaking", first, default));

        // Another attempt while the first is still live gets nothing free.
        var second = await SeedAttemptAsync(db, "u1", item, AttemptState.InProgress);
        Assert.False(await svc.TryClaimAsync("u1", "speaking", card, second, default));

        // The first failed: the sample moves to the next attempt.
        (await db.Attempts.SingleAsync(a => a.Id == first)).State = AttemptState.Failed;
        await db.SaveChangesAsync();
        Assert.True(await svc.TryClaimAsync("u1", "speaking", card, second, default));

        // Submitted (grading queued/running) or completed = spent.
        (await db.Attempts.SingleAsync(a => a.Id == second)).State = AttemptState.Submitted;
        await db.SaveChangesAsync();
        Assert.False(await svc.IsOfferedAsync("u1", "speaking", card, default));
        Assert.Equal(FreeSampleService.StateUsed, (await svc.ListAsync("u1", "speaking", default)).Single().State);
    }

    [Fact]
    public async Task IsFreeAttempt_IsFalseForAnAttemptThatIsNotTheClaimedOne()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (item, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        var svc = new FreeSampleService(db);
        var attempt = await SeedAttemptAsync(db, "u1", item, AttemptState.InProgress);
        await svc.TryClaimAsync("u1", "speaking", card, attempt, default);

        Assert.False(await svc.IsFreeAttemptAsync("u2", "speaking", attempt, default));   // someone else's
        Assert.False(await svc.IsFreeAttemptAsync("u1", "speaking", "sa-other", default));
        Assert.False(await svc.IsFreeAttemptAsync("u1", "speaking", null, default));
        Assert.False(await svc.IsFreeAttemptAsync("u1", "writing", attempt, default));
    }

    // ── the DB-enforced once-only claim ──────────────────────────────────────

    [Fact]
    public async Task TheDatabaseRejectsASecondClaimForTheSameLearnerAndSubtest()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        FreeSampleClaim Claim(string id) => new()
        {
            Id = id, UserId = "u1", Subtest = "writing", Profession = "medicine", ContentId = "c", ClaimedAt = now, UpdatedAt = now,
        };

        db.FreeSampleClaims.Add(Claim("fsc-1"));
        await db.SaveChangesAsync();
        db.FreeSampleClaims.Add(Claim("fsc-2"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
