using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.FreeSamples;

namespace OetLearner.Api.Tests.FreeSamples;

/// <summary>
/// Free Mocks (owner 2026-09-22) — the per-learner free AI-graded Writing/Speaking
/// sample: which item is offered per profession, the use lifecycle (a use counts
/// only once it produced a result; a failed grade never counts — see
/// FreeSampleRetryPolicyTests for the two-results policy), and the dark-launch flag. Logic tests run on InMemory; the unique-index test
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

    /// <summary>22 Sep 2026 handoff (item 2): every offer is now scoped to the
    /// caller's own registered profession, so every test below needs a real
    /// LearnerUser row with a matching ActiveProfessionId.</summary>
    internal static async Task SeedLearnerAsync(LearnerDbContext db, string userId, string profession)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = userId,
            Email = $"{userId}@example.test",
            ActiveProfessionId = profession,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
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

    internal static async Task<Guid> SeedSubmissionAsync(
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

    internal static async Task<string> SeedAttemptAsync(LearnerDbContext db, string userId, string contentId, AttemptState state)
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

    internal static async Task<string> SeedEvaluationAsync(LearnerDbContext db, string attemptId, AsyncState state)
    {
        var id = $"ev-{Guid.NewGuid():N}";
        db.Evaluations.Add(new Evaluation
        {
            Id = id,
            AttemptId = attemptId,
            SubtestCode = "speaking",
            State = state,
            ScoreRange = "n/a",
            ModelExplanationSafe = "n/a",
            LearnerDisclaimer = "n/a",
            LastTransitionAt = DateTimeOffset.UtcNow,
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
        Assert.False(await svc.TryClaimAsync("u1", "writing", scenario.ToString("D"), FreeSampleUse.KindWritingSubmission, "sub-1", default));

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
        // CRITICAL SECURITY FIX (22 Sep 2026 handoff, item 2): each learner is
        // scoped to their OWN registered profession, never every live pick at
        // once — a medicine learner and a dentistry learner each see only theirs.
        await SeedLearnerAsync(db, "u1", "medicine");
        await SeedLearnerAsync(db, "u2", "dentistry");
        var svc = new FreeSampleService(db);

        var medicineOffers = await svc.ListAsync("u1", "writing", default);
        var dentistryOffers = await svc.ListAsync("u2", "writing", default);

        var medicineOffer = Assert.Single(medicineOffers);
        Assert.Equal("medicine", medicineOffer.ProfessionId);
        Assert.Equal(easy.ToString("D"), medicineOffer.ContentId);
        Assert.Equal(FreeSampleService.StateAvailable, medicineOffer.State);

        var dentistryOffer = Assert.Single(dentistryOffers);
        Assert.Equal("dentistry", dentistryOffer.ProfessionId);
        Assert.Equal(dentistry.ToString("D"), dentistryOffer.ContentId);
        Assert.Equal(FreeSampleService.StateAvailable, dentistryOffer.State);
    }

    [Fact]
    public async Task Writing_ALearnerNeverSeesOrCanClaimAnotherProfessionsSample_CriticalSecurityFix20260922()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var medicine = await SeedScenarioAsync(db, "medicine", "Alpha task", difficulty: 1);
        var dentistry = await SeedScenarioAsync(db, "dentistry", "Dental task");
        // "nursing" has no live Writing content of its own.
        await SeedLearnerAsync(db, "u1", "nursing");
        var svc = new FreeSampleService(db);

        // Controlled "unavailable" row for the learner's OWN profession — never another profession's item.
        var none = Assert.Single(await svc.ListAsync("u1", "writing", default));
        Assert.Equal("nursing", none.ProfessionId);
        Assert.Equal(FreeSampleService.StateUnavailable, none.State);
        Assert.Null(none.ContentId);
        Assert.Null(none.Route);
        Assert.False(await svc.IsOfferedAsync("u1", "writing", medicine.ToString("D"), default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", dentistry.ToString("D"), default));
        Assert.False(await svc.TryClaimAsync("u1", "writing", medicine.ToString("D"), FreeSampleUse.KindWritingSubmission, Guid.NewGuid().ToString("N"), default));
        Assert.False(await svc.TryClaimAsync("u1", "writing", dentistry.ToString("D"), FreeSampleUse.KindWritingSubmission, Guid.NewGuid().ToString("N"), default));
        Assert.Empty(db.FreeSampleClaims);
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
        await SeedLearnerAsync(db, "u1", "medicine");
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
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);

        Assert.True(await svc.IsOfferedAsync("u1", "writing", picked.ToString("D"), default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", other.ToString("D"), default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", "not-a-guid", default));
        Assert.False(await svc.IsOfferedAsync("u1", "writing", Guid.NewGuid().ToString("D"), default));
        Assert.False(await svc.TryClaimAsync("u1", "writing", other.ToString("D"), FreeSampleUse.KindWritingSubmission, Guid.NewGuid().ToString("N"), default));
        Assert.Empty(db.FreeSampleClaims);
    }

    // ── Writing: claim lifecycle ─────────────────────────────────────────────

    [Fact]
    public async Task Writing_AFailedGradeNeverCounts_AndAGradedOneDoes()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var scenario = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var content = scenario.ToString("D");
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);

        var first = await SeedSubmissionAsync(db, "u1", scenario, "grading");
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, first.ToString("N"), default));
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, first.ToString("N"), default)); // retry-grade is idempotent
        Assert.False(await svc.IsOfferedAsync("u1", "writing", content, default));                   // one use is being graded
        var grading = (await svc.ListAsync("u1", "writing", default)).Single();
        Assert.Equal(FreeSampleService.StateInProgress, grading.State);
        Assert.Equal($"/writing/submissions/{first:D}/grading", grading.Route); // no result page until graded

        // Another submission while the first is still grading gets nothing free.
        var second = Guid.NewGuid();
        Assert.False(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, second.ToString("N"), default));

        // The first grade FAILED: nothing was spent; the learner is routed to retry
        // THAT submission, and a new submission would still be free.
        (await db.WritingSubmissions.SingleAsync(s => s.Id == first)).Status = WritingSubmissionStatuses.Failed;
        await db.SaveChangesAsync();
        var failed = (await svc.ListAsync("u1", "writing", default)).Single();
        Assert.Equal(FreeSampleService.StateGradingFailed, failed.State);
        Assert.Equal($"/writing/submissions/{first:D}/grading", failed.Route);
        Assert.True(await svc.IsOfferedAsync("u1", "writing", content, default));
        await SeedSubmissionAsync(db, "u1", scenario, "grading", second);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, second.ToString("N"), default));
        Assert.True(await svc.IsFreeAttemptAsync("u1", "writing", second.ToString("N"), default));
        Assert.False(await svc.IsFreeAttemptAsync("u1", "writing", first.ToString("N"), default)); // another use is grading

        // Graded: ONE result — the free revision is still left.
        (await db.WritingSubmissions.SingleAsync(s => s.Id == second)).Status = WritingSubmissionStatuses.Graded;
        await db.SaveChangesAsync();
        Assert.True(await svc.IsOfferedAsync("u1", "writing", content, default));
        var retry = (await svc.ListAsync("u1", "writing", default)).Single();
        Assert.Equal(FreeSampleService.StateRetryAvailable, retry.State);
        Assert.Equal(1, retry.SuccessfulCount);
        Assert.Equal(1, retry.Remaining);
        Assert.Equal("medicine", retry.ProfessionId);
    }

    [Fact]
    public async Task Writing_AFailedGrade_IsRetriedOnTheSameSubmission_RequeuedStaysInProgress()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var scenario = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var content = scenario.ToString("D");
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);
        var first = await SeedSubmissionAsync(db, "u1", scenario, WritingSubmissionStatuses.Grading);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, first.ToString("N"), default));

        async Task<FreeSampleOffer> OfferAfterAsync(string status)
        {
            (await db.WritingSubmissions.SingleAsync(s => s.Id == first)).Status = status;
            await db.SaveChangesAsync();
            return (await svc.ListAsync("u1", "writing", default)).Single();
        }

        var failed = await OfferAfterAsync(WritingSubmissionStatuses.Failed);
        Assert.Equal((FreeSampleService.StateGradingFailed, $"/writing/submissions/{first:D}/grading", 0),
            (failed.State, failed.Route, failed.SuccessfulCount));

        // Retry / auto-retry requeues the same row: live grading, never failed.
        var requeued = await OfferAfterAsync(WritingSubmissionStatuses.Queued);
        Assert.Equal((FreeSampleService.StateInProgress, $"/writing/submissions/{first:D}/grading"), (requeued.State, requeued.Route));
        Assert.True(await svc.IsFreeAttemptAsync("u1", "writing", first.ToString("N"), default)); // the retry stays free

        var graded = await OfferAfterAsync(WritingSubmissionStatuses.Graded);
        Assert.Equal((FreeSampleService.StateRetryAvailable, 1), (graded.State, graded.SuccessfulCount));
        Assert.Single(db.FreeSampleUses); // one submission, one use throughout
    }

    [Fact]
    public async Task Writing_GradingFailed_OnlyWhenTheLatestUseFailed()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var scenario = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var content = scenario.ToString("D");
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);

        var original = await SeedSubmissionAsync(db, "u1", scenario, WritingSubmissionStatuses.Failed);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, original.ToString("N"), default));
        await Task.Delay(5); // uses are ordered by CreatedAt
        var newer = await SeedSubmissionAsync(db, "u1", scenario, WritingSubmissionStatuses.Graded);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, newer.ToString("N"), default));
        Assert.Equal(FreeSampleService.StateRetryAvailable, (await svc.ListAsync("u1", "writing", default)).Single().State);

        // The free revision then fails: the learner retries the revision itself.
        await Task.Delay(5);
        var revision = await SeedSubmissionAsync(db, "u1", scenario, WritingSubmissionStatuses.Failed);
        Assert.True(await svc.TryClaimAsync("u1", "writing", content, FreeSampleUse.KindWritingSubmission, revision.ToString("N"), default));
        var offer = (await svc.ListAsync("u1", "writing", default)).Single();
        Assert.Equal((FreeSampleService.StateGradingFailed, $"/writing/submissions/{revision:D}/grading", 1),
            (offer.State, offer.Route, offer.SuccessfulCount));
    }

    [Fact]
    public async Task Writing_TheClaimIsPerLearnerAndPerSubtest()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var scenario = await SeedScenarioAsync(db, "medicine", "Alpha", difficulty: 1);
        var (_, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        await SeedLearnerAsync(db, "u1", "medicine");
        await SeedLearnerAsync(db, "u2", "medicine");
        var svc = new FreeSampleService(db);

        Assert.True(await svc.TryClaimAsync("u1", "writing", scenario.ToString("D"), FreeSampleUse.KindWritingSubmission, Guid.NewGuid().ToString("N"), default));
        Assert.True(await svc.TryClaimAsync("u2", "writing", scenario.ToString("D"), FreeSampleUse.KindWritingSubmission, Guid.NewGuid().ToString("N"), default));
        Assert.True(await svc.TryClaimAsync("u1", "speaking", card, FreeSampleUse.KindLegacyAttempt, "sa-1", default)); // Writing does not use up Speaking
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
        // CRITICAL SECURITY FIX (22 Sep 2026 handoff, item 2): each learner is
        // scoped to their OWN registered profession, never every live card at once.
        await SeedLearnerAsync(db, "u1", "medicine");
        await SeedLearnerAsync(db, "u2", "nursing");
        var svc = new FreeSampleService(db);

        var medicineOffers = await svc.ListAsync("u1", "speaking", default);
        var nursingOffers = await svc.ListAsync("u2", "speaking", default);

        var medicineOffer = Assert.Single(medicineOffers);
        Assert.Equal("medicine", medicineOffer.ProfessionId);
        Assert.Equal(medCard, medicineOffer.ContentId);
        var nursingOffer = Assert.Single(nursingOffers);
        Assert.Equal("nursing", nursingOffer.ProfessionId);
        Assert.Equal(nurseCard, nursingOffer.ContentId);

        Assert.True(await svc.IsOfferedAsync("u1", "speaking", medCard, default));
        Assert.True(await svc.IsOfferedAsync("u1", "speaking", medItem, default)); // the attempt API may pass either id
    }

    [Fact]
    public async Task Speaking_ALearnerNeverSeesOrCanClaimAnotherProfessionsCard_CriticalSecurityFix20260922()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (medItem, medCard) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        var (_, nurseCard) = await SeedCardAsync(db, "nursing", cardNumber: 5);
        // "dentistry" has no live Speaking card of its own.
        await SeedLearnerAsync(db, "u1", "dentistry");
        var svc = new FreeSampleService(db);

        var none = Assert.Single(await svc.ListAsync("u1", "speaking", default));
        Assert.Equal("dentistry", none.ProfessionId);
        Assert.Equal(FreeSampleService.StateUnavailable, none.State);
        Assert.Null(none.ContentId);
        Assert.Null(none.Route);
        Assert.False(await svc.IsOfferedAsync("u1", "speaking", medCard, default));
        Assert.False(await svc.IsOfferedAsync("u1", "speaking", medItem, default));
        Assert.False(await svc.IsOfferedAsync("u1", "speaking", nurseCard, default));
        Assert.False(await svc.TryClaimAsync("u1", "speaking", medCard, FreeSampleUse.KindLegacyAttempt, Guid.NewGuid().ToString("N"), default));
        Assert.False(await svc.TryClaimAsync("u1", "speaking", nurseCard, FreeSampleUse.KindLegacyAttempt, Guid.NewGuid().ToString("N"), default));
        Assert.Empty(db.FreeSampleClaims);
    }

    [Fact]
    public async Task Speaking_ALegacyUse_CountsOnlyOnceItsEvaluationCompleted()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (item, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);

        var first = await SeedAttemptAsync(db, "u1", item, AttemptState.InProgress);
        Assert.True(await svc.TryClaimAsync("u1", "speaking", card, FreeSampleUse.KindLegacyAttempt, first, default));
        Assert.True(await svc.IsFreeAttemptAsync("u1", "speaking", first, default));
        Assert.True(await svc.IsOfferedAsync("u1", "speaking", card, default)); // pre-submit: restart is free

        // Submitted, evaluation pending: in grading — not a result yet, but nothing new starts.
        (await db.Attempts.SingleAsync(a => a.Id == first)).State = AttemptState.Submitted;
        await db.SaveChangesAsync();
        Assert.False(await svc.IsOfferedAsync("u1", "speaking", card, default));
        Assert.Equal(FreeSampleService.StateInProgress, (await svc.ListAsync("u1", "speaking", default)).Single().State);

        // Transcription failed (P0 22 Sep 2026): never counts, the sample is offered again.
        var evaluationId = await SeedEvaluationAsync(db, first, AsyncState.Failed);
        Assert.True(await svc.IsOfferedAsync("u1", "speaking", card, default));
        Assert.Equal(0, (await svc.ListAsync("u1", "speaking", default)).Single().SuccessfulCount);

        // A completed evaluation is the one and only thing that counts.
        (await db.Evaluations.SingleAsync(e => e.Id == evaluationId)).State = AsyncState.Completed;
        await db.SaveChangesAsync();
        var offer = (await svc.ListAsync("u1", "speaking", default)).Single();
        Assert.Equal(FreeSampleService.StateRetryAvailable, offer.State);
        Assert.Equal(1, offer.SuccessfulCount);
        Assert.Equal($"/speaking/results/{evaluationId}", offer.LastResultRoute);
        Assert.Equal(first, offer.LastSubmissionId);
        Assert.Equal(FreeSampleService.StartRoute("speaking", card), offer.Route); // retry = the same card
    }

    [Fact]
    public async Task IsFreeAttempt_IsFalseForAnAttemptThatIsNotTheClaimedOne()
    {
        await using var db = NewDb();
        await EnableAsync(db);
        var (item, card) = await SeedCardAsync(db, "medicine", cardNumber: 1);
        await SeedLearnerAsync(db, "u1", "medicine");
        var svc = new FreeSampleService(db);
        var attempt = await SeedAttemptAsync(db, "u1", item, AttemptState.InProgress);
        await svc.TryClaimAsync("u1", "speaking", card, FreeSampleUse.KindLegacyAttempt, attempt, default);

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
