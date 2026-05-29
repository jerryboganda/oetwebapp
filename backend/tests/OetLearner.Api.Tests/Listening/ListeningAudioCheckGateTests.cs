using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;

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
    // Frozen clock so the TTL boundary is deterministic.
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 05, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private const string UserId = "learner-1";

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

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
        LearnerDbContext db, ListeningAttemptMode mode, string attemptId = "att-1")
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
            TechReadinessJson = JsonSerializer.Serialize(
                new TechReadinessSnapshot(AudioOk: true, DurationMs: 1500, CheckedAt: Now), WebJson),
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
        var svc = NewSessionService(db, new FixedClock(Now));

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
        var svc = NewSessionService(db, new FixedClock(Now));

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
        var svc = NewSessionService(db, new FixedClock(Now));

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
        var svc = NewSessionService(db, new FixedClock(Now));

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
        var svc = NewSessionService(db, new FixedClock(Now));

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
        var svc = NewSessionService(db, new FixedClock(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        Assert.Equal("rejected", result.Outcome);
        Assert.Equal("audio-check-required", result.RejectionReason);
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
        var svc = NewSessionService(db, new FixedClock(Now));

        var result = await svc.AdvanceAsync("att-1", UserId, AdvanceToFirstStrict(), CancellationToken.None);

        // Free-nav modes apply the transition directly — never gated, never a
        // confirm round-trip.
        Assert.Equal("applied", result.Outcome);
        Assert.Null(result.RejectionReason);
    }

    // ─────────────────────────────────────────────────────────────────────
    // StartAttemptAsync — exam-mode start gate
    // ─────────────────────────────────────────────────────────────────────

    private sealed class AllowAllContentEntitlementService : IContentEntitlementService
    {
        public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.FromResult(new ContentEntitlementResult(true, "test", "premium", null));

        public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.CompletedTask;

        public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal) => false;
    }

    /// <summary>Seed a published relational Listening paper (+ owning user) with
    /// a primary audio asset so an exam-mode start can clear the audio-asset
    /// guard once the sound-check gate has passed.</summary>
    private static async Task SeedRelationalPaperWithAudioAsync(LearnerDbContext db)
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
                    MediaAssetId = media.Id,
                    MediaAsset = media,
                    IsPrimary = true,
                },
            ],
        };
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
        db.ListeningParts.Add(part);
        db.ListeningQuestions.Add(question);
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
