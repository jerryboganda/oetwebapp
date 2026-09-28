using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// WORK-STREAM 2 — strict Listening exams (Exam / OET@Home, OneWayLocks) must
/// not start until the learner has a sound-check that passed within
/// <see cref="ListeningSessionService.AudioCheckTtlMs"/>.
///
/// Two enforcement points are covered:
///   • <see cref="ListeningSessionService.AdvanceAsync"/> — the first FSM
///     transition (<c>intro → a1_preview</c>) is rejected with
///     <c>audio-check-required</c> when the check is missing / expired, allowed
///     when fresh, and NEVER gated for free-nav modes (Learning / Diagnostic).
///   • <see cref="ListeningLearnerService.StartAttemptAsync"/> — exam-mode
///     attempt creation throws <c>listening_audio_check_required</c> when the
///     check is missing, and succeeds with a fresh one.
/// </summary>
public class ListeningAudioCheckGateTests
{
    // Use a date far enough in the future that the 24-h audio-check TTL never
    // expires relative to the real DateTimeOffset.UtcNow used by
    // ListeningLearnerService.StartRelationalAttemptAsync.
    private static readonly DateTimeOffset Now = new(2030, 01, 01, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private const string UserId = "learner-1";

    private static LearnerDbContext NewDb()
    {
        var db = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        // Attempt start fails closed without an effective marking policy (test
        // hosts never run the governance seed migration).
        OetLearner.Api.Tests.Infrastructure.AssessmentGovernanceSeeder.SeedDefaultEffectivePolicies(db);
        db.SaveChanges();
        return db;
    }

    private static ListeningSessionService NewSessionService(LearnerDbContext db, TimeProvider clock)
        => new(
            db,
            new ListeningModePolicyResolver(),
            new ListeningConfirmTokenService(Options.Create(new AuthTokenOptions
            {
                AccessTokenSigningKey = "test-signing-key-1234567890123456789012",
            })),
            new ListeningSequenceService(db),
            clock);

    // ─────────────────────────────────────────────────────────────────────
    // Fixtures
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Seed an in-progress attempt parked at the implicit <c>intro</c>
    /// state. A valid tech-readiness snapshot is written so the (separate)
    /// tech-readiness gate at the same transition passes — isolating the
    /// sound-check gate as the thing under test.</summary>
    private static ListeningAttempt SeedIntroAttempt(
        LearnerDbContext db, ListeningAttemptMode mode, string attemptId = "att-1", bool withTechReadiness = true)
    {
        var attempt = new ListeningAttempt
        {
            Id = attemptId,
            UserId = UserId,
            PaperId = "paper-1",
            StartedAt = Now,
            LastActivityAt = Now,
            MaxRawScore = 42,
            Mode = mode,
            Status = ListeningAttemptStatus.InProgress,
            // Null NavigationStateJson → service seeds `intro` for in-progress.
            NavigationStateJson = null,
            // Fresh, passing device probe so RequiresTechReadiness is satisfied.
            TechReadinessJson = withTechReadiness
                ? JsonSerializer.Serialize(
                    new TechReadinessSnapshot(AudioOk: true, DurationMs: 1500, CheckedAt: Now), WebJson)
                : null,
        };
        db.ListeningAttempts.Add(attempt);
        db.SaveChanges();
        return attempt;
    }

    /// <summary>Upsert the learner's Listening pathway profile with the given
    /// sound-check timestamp (null = never passed).</summary>
    private static void SeedProfile(LearnerDbContext db, DateTimeOffset? audioCheckPassedAt)
    {
        db.LearnerListeningProfiles.Add(new LearnerListeningProfile
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TargetBand = "B",
            Profession = "medicine",
            CurrentStage = "foundation",
            OnboardingCompletedAt = Now.AddDays(-2),
            AudioCheckPassedAt = audioCheckPassedAt,
            UpdatedAt = Now,
        });
        db.SaveChanges();
    }

    private static AdvanceCommand AdvanceToFirstStrict()
        => new(ListeningFsmTransitions.A1Preview, ConfirmToken: null);

    // ─────────────────────────────────────────────────────────────────────
    // AdvanceAsync — strict modes
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceAsync_strict_rejects_when_no_sound_check()
    {
        await using var db = NewDb();
        SeedIntroAttempt(db, ListeningAttemptMode.Exam);
        SeedProfile(db, audioCheckPassedAt: null);
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        Assert.Equal("rejected", result.Outcome);
        Assert.Equal("audio-check-required", result.RejectionReason);
    }

    [Fact]
    public async Task AdvanceAsync_strict_rejects_when_profile_missing_entirely()
    {
        await using var db = NewDb();
        SeedIntroAttempt(db, ListeningAttemptMode.Exam);
        // No LearnerListeningProfile row at all — fail closed.
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        Assert.Equal("rejected", result.Outcome);
        Assert.Equal("audio-check-required", result.RejectionReason);
    }

    [Fact]
    public async Task AdvanceAsync_strict_rejects_when_sound_check_expired()
    {
        await using var db = NewDb();
        SeedIntroAttempt(db, ListeningAttemptMode.Exam);
        // Passed just past the TTL boundary → expired.
        SeedProfile(db, audioCheckPassedAt: Now.AddMilliseconds(-(ListeningSessionService.AudioCheckTtlMs + 1)));
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        Assert.Equal("rejected", result.Outcome);
        Assert.Equal("audio-check-required", result.RejectionReason);
    }

    [Fact]
    public async Task AdvanceAsync_strict_applies_with_fresh_sound_check()
    {
        await using var db = NewDb();
        SeedIntroAttempt(db, ListeningAttemptMode.Exam);
        SeedProfile(db, audioCheckPassedAt: Now.AddHours(-1));
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        // Exam mode requires a confirm token for the linear advance; reaching
        // "confirm-required" proves the gate let the transition through (a
        // gated request returns "rejected" before any token is issued).
        Assert.Equal("confirm-required", result.Outcome);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public async Task AdvanceAsync_strict_applies_exactly_at_ttl_boundary()
    {
        await using var db = NewDb();
        SeedIntroAttempt(db, ListeningAttemptMode.Exam);
        // Passed exactly TTL ago — still valid (>= boundary).
        SeedProfile(db, audioCheckPassedAt: Now.AddMilliseconds(-ListeningSessionService.AudioCheckTtlMs));
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        Assert.NotEqual("rejected", result.Outcome);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public async Task AdvanceAsync_home_mode_is_gated_like_exam()
    {
        await using var db = NewDb();
        SeedIntroAttempt(db, ListeningAttemptMode.Home);
        SeedProfile(db, audioCheckPassedAt: null);
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        Assert.Equal("rejected", result.Outcome);
        Assert.Equal("audio-check-required", result.RejectionReason);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Owner Free Mocks (23 Sep 2026) — the free-sample paper skips BOTH the
    // tech-readiness and the sound-check gates; a paid paper keeps them.
    // ─────────────────────────────────────────────────────────────────────

    private static void SeedPaperRow(LearnerDbContext db, string tagsCsv)
    {
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-1",
            SubtestCode = "listening",
            Title = "Gate Listening Paper",
            Slug = "gate-listening-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            AppliesToAllProfessions = true,
            EstimatedDurationMinutes = 45,
            TagsCsv = tagsCsv,
            CreatedAt = Now,
            UpdatedAt = Now,
            PublishedAt = Now,
            ExtractedTextJson = "{}",
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task AdvanceAsync_free_sample_paper_skips_readiness_and_sound_check()
    {
        await using var db = NewDb();
        SeedPaperRow(db, ContentEntitlementService.FreeSampleTag);
        // No tech-readiness snapshot AND no sound-check profile.
        SeedIntroAttempt(db, ListeningAttemptMode.Exam, withTechReadiness: false);
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        // Neither gate fired: the strict advance reached its confirm step.
        Assert.Equal("confirm-required", result.Outcome);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public async Task AdvanceAsync_paid_paper_still_requires_readiness_then_sound_check()
    {
        await using var db = NewDb();
        SeedPaperRow(db, "access:paid");
        SeedIntroAttempt(db, ListeningAttemptMode.Exam, withTechReadiness: false);
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var noReadiness = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);
        Assert.Equal("rejected", noReadiness.Outcome);
        Assert.Equal("tech-readiness-required", noReadiness.RejectionReason);

        await using var db2 = NewDb();
        SeedPaperRow(db2, "access:paid");
        SeedIntroAttempt(db2, ListeningAttemptMode.Exam);
        var svc2 = NewSessionService(db2, new FixedTimeProvider(Now));

        var noSoundCheck = await svc2.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);
        Assert.Equal("rejected", noSoundCheck.Outcome);
        Assert.Equal("audio-check-required", noSoundCheck.RejectionReason);
    }

    // ─────────────────────────────────────────────────────────────────────
    // AdvanceAsync — non-strict modes are never gated
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ListeningAttemptMode.Learning)]
    [InlineData(ListeningAttemptMode.Diagnostic)]
    public async Task AdvanceAsync_non_strict_is_never_gated_even_without_check(ListeningAttemptMode mode)
    {
        await using var db = NewDb();
        SeedIntroAttempt(db, mode);
        SeedProfile(db, audioCheckPassedAt: null);
        var svc = NewSessionService(db, new FixedTimeProvider(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        // Free-nav modes apply the transition directly — never gated, never a
        // confirm round-trip.
        Assert.Equal("applied", result.Outcome);
        Assert.Null(result.RejectionReason);
    }

    // ─────────────────────────────────────────────────────────────────────
    // StartAttemptAsync — exam-mode start gate
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Seed a published relational Listening paper (+ owning user) with
    /// a primary audio asset so an exam-mode start can clear the audio-asset
    /// guard once the sound-check gate has passed.</summary>
    private static async Task SeedRelationalPaperWithAudioAsync(
        LearnerDbContext db,
        bool perSectionAudioOnly = false,
        bool jsonBacked = false,
        bool freeSample = false)
    {
        var user = new LearnerUser
        {
            Id = UserId,
            AuthAccountId = "auth-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = Now,
            LastActiveAt = Now,
            AccountStatus = "active",
        };
        var media = new MediaAsset
        {
            Id = "media-audio-1",
            OriginalFilename = "paper-1.mp3",
            MimeType = "audio/mpeg",
            Format = "mp3",
            SizeBytes = 1024,
            StoragePath = "content/paper-1.mp3",
            Status = MediaAssetStatus.Ready,
            MediaKind = "audio",
        };
        var paper = new ContentPaper
        {
            Id = "paper-1",
            SubtestCode = "listening",
            Title = "Gate Listening Paper",
            Slug = "gate-listening-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            AppliesToAllProfessions = true,
            EstimatedDurationMinutes = 45,
            TagsCsv = freeSample ? ContentEntitlementService.FreeSampleTag : string.Empty,
            CreatedAt = Now,
            UpdatedAt = Now,
            PublishedAt = Now,
            ExtractedTextJson = "{}",
            Assets =
            [
                new ContentPaperAsset
                {
                    Id = "asset-audio-1",
                    PaperId = "paper-1",
                    Role = PaperAssetRole.Audio,
                    Part = perSectionAudioOnly ? "A1" : null,
                    MediaAssetId = media.Id,
                    MediaAsset = media,
                    IsPrimary = true,
                },
            ],
        };

        if (jsonBacked)
        {
            paper.ExtractedTextJson = "{\"listeningQuestions\":[{\"id\":\"question-json\",\"number\":1,\"partCode\":\"A1\",\"stem\":\"Patient name?\",\"answer\":\"Smith\"}]}";
        }

        var part = new ListeningPart
        {
            Id = "part-a1",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        var question = new ListeningQuestion
        {
            Id = "question-1",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Dose: ____ milligrams",
            CorrectAnswerJson = "\"five\"",
            CaseSensitive = false,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        db.Users.Add(user);
        db.MediaAssets.Add(media);
        db.ContentPapers.Add(paper);
        if (!jsonBacked)
        {
            db.ListeningParts.Add(part);
            db.ListeningQuestions.Add(question);
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StartAttemptAsync_exam_rejects_when_no_sound_check()
    {
        await using var db = NewDb();
        await SeedRelationalPaperWithAudioAsync(db);
        // No profile → no passed check.
        var svc = new ListeningLearnerService(db, new AllowAllContentEntitlementService());

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.StartAttemptAsync(UserId, "paper-1", "exam", null, forceNewAttempt: true, CancellationToken.None));

        Assert.Equal("listening_audio_check_required", ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
        // No attempt row should have been created.
        Assert.False(await db.ListeningAttempts.AnyAsync());
    }

    [Fact]
    public async Task StartAttemptAsync_json_backed_exam_rejects_when_no_sound_check()
    {
        await using var db = NewDb();
        await SeedRelationalPaperWithAudioAsync(db, jsonBacked: true);
        // No profile — the legacy JSON-backed route must fail closed too.
        var svc = new ListeningLearnerService(db, new AllowAllContentEntitlementService());

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.StartAttemptAsync(UserId, "paper-1", "exam", null, forceNewAttempt: true, CancellationToken.None));

        Assert.Equal("listening_audio_check_required", ex.ErrorCode);
        Assert.False(await db.Attempts.AnyAsync());
    }

    [Fact]
    public async Task StartAttemptAsync_exam_succeeds_with_fresh_sound_check()
    {
        await using var db = NewDb();
        await SeedRelationalPaperWithAudioAsync(db);
        SeedProfile(db, audioCheckPassedAt: Now.AddHours(-1));
        var svc = new ListeningLearnerService(db, new AllowAllContentEntitlementService());

        var dto = await svc.StartAttemptAsync(UserId, "paper-1", "exam", null, forceNewAttempt: true, CancellationToken.None);

        Assert.NotNull(dto);
        // The exam attempt was created (gate passed + audio asset present).
        Assert.True(await db.ListeningAttempts.AnyAsync(a => a.Mode == ListeningAttemptMode.Exam));
    }

    [Fact]
    public async Task StartAttemptAsync_exam_accepts_per_section_audio_without_combined_audio()
    {
        await using var db = NewDb();
        await SeedRelationalPaperWithAudioAsync(db, perSectionAudioOnly: true);
        SeedProfile(db, audioCheckPassedAt: Now.AddHours(-1));
        var svc = new ListeningLearnerService(db, new AllowAllContentEntitlementService());

        var dto = await svc.StartAttemptAsync(UserId, "paper-1", "exam", null, forceNewAttempt: true, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.True(await db.ListeningAttempts.AnyAsync(a => a.Mode == ListeningAttemptMode.Exam));
    }

    [Fact]
    public async Task StartAttemptAsync_free_sample_exam_starts_without_sound_check()
    {
        await using var db = NewDb();
        await SeedRelationalPaperWithAudioAsync(db, freeSample: true);
        // No profile → no passed check; the free-sample paper is not gated.
        var svc = new ListeningLearnerService(db, new AllowAllContentEntitlementService());

        var dto = await svc.StartAttemptAsync(UserId, "paper-1", "exam", null, forceNewAttempt: true, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.True(await db.ListeningAttempts.AnyAsync(a => a.Mode == ListeningAttemptMode.Exam));
    }

    [Fact]
    public async Task GetSessionAsync_reports_isFreeSample_only_for_the_tagged_paper()
    {
        await using var freeDb = NewDb();
        await SeedRelationalPaperWithAudioAsync(freeDb, freeSample: true);
        var freeSession = await new ListeningLearnerService(freeDb, new AllowAllContentEntitlementService())
            .GetSessionAsync(UserId, "paper-1", "practice", null, CancellationToken.None);
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(freeSession, WebJson)))
        {
            Assert.True(json.RootElement.GetProperty("isFreeSample").GetBoolean());
        }

        await using var paidDb = NewDb();
        await SeedRelationalPaperWithAudioAsync(paidDb);
        var paidSession = await new ListeningLearnerService(paidDb, new AllowAllContentEntitlementService())
            .GetSessionAsync(UserId, "paper-1", "practice", null, CancellationToken.None);
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(paidSession, WebJson)))
        {
            Assert.False(json.RootElement.GetProperty("isFreeSample").GetBoolean());
        }
    }

    [Fact]
    public async Task StartAttemptAsync_practice_is_never_gated()
    {
        await using var db = NewDb();
        await SeedRelationalPaperWithAudioAsync(db);
        // No profile — practice (Learning) must still start.
        var svc = new ListeningLearnerService(db, new AllowAllContentEntitlementService());

        var dto = await svc.StartAttemptAsync(UserId, "paper-1", "practice", null, forceNewAttempt: true, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.True(await db.ListeningAttempts.AnyAsync(a => a.Mode == ListeningAttemptMode.Learning));
    }
}
