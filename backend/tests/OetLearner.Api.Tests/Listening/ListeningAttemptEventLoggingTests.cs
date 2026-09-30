using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// WORK-STREAM 7d — spec §17.11 Listening attempt-event logging.
/// Verifies that <see cref="ListeningLearnerService.RecordIntegrityEventAsync"/>
/// accepts and persists every event type in the attempt-event stream
/// (audio lifecycle, reading-time windows, answer changes, annotations, and
/// the timer auto-submit), threads the structured <c>cuePointMs</c> /
/// <c>questionId</c> fields into the AuditEvent payload, and APPENDS audio
/// start/end entries to <see cref="ListeningAttempt.AudioCueTimelineJson"/>
/// without clobbering prior entries. Mirrors the in-memory DbContext setup
/// used by <c>ListeningRelationalRuntimeTests</c>.
/// </summary>
public class ListeningAttemptEventLoggingTests
{
    private static (LearnerDbContext db, ListeningLearnerService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        // Attempt start fails closed without an effective marking policy (test
        // hosts never run the governance seed migration).
        OetLearner.Api.Tests.Infrastructure.AssessmentGovernanceSeeder.SeedDefaultEffectivePolicies(db);
        db.SaveChanges();
        return (db, new ListeningLearnerService(db, new AllowAllContentEntitlementService()));
    }

    private static async Task<(string userId, string paperId, string questionId)> SeedRelationalPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new LearnerUser
        {
            Id = "learner-7d",
            AuthAccountId = "auth-7d",
            DisplayName = "Learner 7d",
            Email = "learner-7d@example.test",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        };
        // WS2 — exam/Home-mode attempt start also server-verifies a primary
        // audio asset exists (ListeningLearnerService.StartRelationalAttemptAsync).
        // Seed one so the integrity-locked `home` attempt clears the audio-asset
        // guard once the sound-check gate has passed.
        var media = new MediaAsset
        {
            Id = "media-audio-7d",
            OriginalFilename = "paper-7d.mp3",
            MimeType = "audio/mpeg",
            Format = "mp3",
            SizeBytes = 1024,
            StoragePath = "content/paper-7d.mp3",
            Status = MediaAssetStatus.Ready,
            MediaKind = "audio",
        };
        var paper = new ContentPaper
        {
            Id = "paper-7d",
            SubtestCode = "listening",
            Title = "Attempt-Event Listening Paper",
            Slug = "attempt-event-listening-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            AppliesToAllProfessions = true,
            EstimatedDurationMinutes = 45,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            ExtractedTextJson = "{}",
            Assets =
            [
                new ContentPaperAsset
                {
                    Id = "asset-audio-7d",
                    PaperId = "paper-7d",
                    Role = PaperAssetRole.Audio,
                    MediaAssetId = media.Id,
                    MediaAsset = media,
                    IsPrimary = true,
                },
            ],
        };
        var part = new ListeningPart
        {
            Id = "part-a1-7d",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var extract = new ListeningExtract
        {
            Id = "extract-a1-7d",
            ListeningPartId = part.Id,
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "Consultation 1",
            AccentCode = "en-GB",
            SpeakersJson = "[{\"id\":\"s1\",\"role\":\"GP\",\"gender\":\"f\"}]",
            TranscriptSegmentsJson = "[{\"startMs\":1000,\"endMs\":3000,\"speakerId\":\"s1\",\"text\":\"The dose is five milligrams.\"}]",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var question = new OetLearner.Api.Domain.ListeningQuestion
        {
            Id = "question-7d",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            ListeningExtractId = extract.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Dose: ____ milligrams",
            CorrectAnswerJson = "\"five\"",
            AcceptedSynonymsJson = "[\"5\"]",
            CaseSensitive = false,
            ExplanationMarkdown = "The speaker says five milligrams.",
            SkillTag = "numbers_units",
            TranscriptEvidenceText = "The dose is five milligrams.",
            TranscriptEvidenceStartMs = 1000,
            TranscriptEvidenceEndMs = 3000,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        db.MediaAssets.Add(media);
        db.ContentPapers.Add(paper);
        db.ListeningParts.Add(part);
        db.ListeningExtracts.Add(extract);
        db.ListeningQuestions.Add(question);
        db.ListeningPolicies.Add(new ListeningPolicy { Id = "global", FullPaperTimerMinutes = 45, GracePeriodSeconds = 10 });
        // WS2 — OET@Home / Exam attempt-start is gated on a recent pathway
        // sound-check. Stamp a fresh AudioCheckPassedAt so the integrity-locked
        // `home` attempt (where this event stream matters most) can start.
        db.LearnerListeningProfiles.Add(new LearnerListeningProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TargetBand = "B",
            Profession = "nursing",
            CurrentStage = "practice",
            OnboardingCompletedAt = now,
            AudioCheckPassedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (user.Id, paper.Id, question.Id);
    }

    private static async Task<ListeningAttempt> StartHomeAttemptAsync(LearnerDbContext db, ListeningLearnerService svc, string userId, string paperId)
    {
        // OET@Home is integrity-locked, so the player records the full event
        // stream against it. The start gate needs a passing audio check.
        await svc.StartAttemptAsync(userId, paperId, "home", default);
        return await db.ListeningAttempts.SingleAsync(a => a.UserId == userId && a.PaperId == paperId);
    }

    [Theory]
    [InlineData("audio_started")]
    [InlineData("audio_ended")]
    [InlineData("audio_buffering_start")]
    [InlineData("audio_buffering_end")]
    [InlineData("audio_stalled")]
    [InlineData("audio_error")]
    [InlineData("reading_time_started")]
    [InlineData("reading_time_ended")]
    [InlineData("answer_changed")]
    [InlineData("highlight")]
    [InlineData("strikethrough")]
    [InlineData("auto_submit")]
    public async Task RecordIntegrityEvent_AcceptsAndPersistsEachNewEventType(string eventType)
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);
        var attempt = await StartHomeAttemptAsync(db, svc, userId, paperId);

        await svc.RecordIntegrityEventAsync(
            userId,
            attempt.Id,
            new ListeningIntegrityEventRequest(eventType, "{\"cuePointMs\":42000}", DateTimeOffset.UtcNow),
            default);

        var audit = await db.AuditEvents.SingleAsync(e => e.Action == "ListeningIntegrityEvent");
        Assert.Equal("ListeningAttempt", audit.ResourceType);
        Assert.Equal(attempt.Id, audit.ResourceId);
        Assert.Contains(eventType, audit.Details);
        // The recognised flag must be true for every event in the §17.11 set.
        Assert.Contains("\"recognized\":true", audit.Details);
    }

    [Fact]
    public async Task RecordIntegrityEvent_ThreadsCuePointAndQuestionIdIntoPayload()
    {
        var (db, svc) = Build();
        var (userId, paperId, questionId) = await SeedRelationalPaperAsync(db);
        var attempt = await StartHomeAttemptAsync(db, svc, userId, paperId);

        await svc.RecordIntegrityEventAsync(
            userId,
            attempt.Id,
            new ListeningIntegrityEventRequest(
                "answer_changed",
                JsonSerializer.Serialize(new { cuePointMs = 12345, questionId }),
                DateTimeOffset.UtcNow),
            default);

        var audit = await db.AuditEvents.SingleAsync(e => e.Action == "ListeningIntegrityEvent");
        using var doc = JsonDocument.Parse(audit.Details!);
        Assert.Equal("answer_changed", doc.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(12345, doc.RootElement.GetProperty("cuePointMs").GetInt32());
        Assert.Equal(questionId, doc.RootElement.GetProperty("questionId").GetString());
    }

    [Fact]
    public async Task RecordIntegrityEvent_AudioErrorFlagsAttemptForAdminReview()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);
        var attempt = await StartHomeAttemptAsync(db, svc, userId, paperId);

        await svc.RecordIntegrityEventAsync(
            userId,
            attempt.Id,
            new ListeningIntegrityEventRequest("audio_error", "{\"playbackValidity\":\"admin_review_required\"}", DateTimeOffset.UtcNow),
            default);

        var updated = await db.ListeningAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.True(updated.RequiresAdminReview);
        Assert.Equal("audio_playback_error", updated.AdminReviewReason);
        Assert.NotNull(updated.AdminReviewFlaggedAt);

        var audit = await db.AuditEvents.SingleAsync(e => e.Action == "ListeningIntegrityEvent");
        Assert.Contains("\"requiresAdminReview\":true", audit.Details);
        Assert.Contains("audio_playback_error", audit.Details);
    }

    [Fact]
    public async Task RecordIntegrityEvent_AppendsAudioStartAndEndToCueTimeline()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);
        var attempt = await StartHomeAttemptAsync(db, svc, userId, paperId);

        // Cue timeline starts empty.
        Assert.True(string.IsNullOrWhiteSpace(attempt.AudioCueTimelineJson));

        await svc.RecordIntegrityEventAsync(
            userId, attempt.Id,
            new ListeningIntegrityEventRequest("audio_started", "{\"cuePointMs\":12000}", DateTimeOffset.UtcNow),
            default);
        await svc.RecordIntegrityEventAsync(
            userId, attempt.Id,
            new ListeningIntegrityEventRequest("audio_ended", "{\"cuePointMs\":240000}", DateTimeOffset.UtcNow),
            default);

        var updated = await db.ListeningAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.False(string.IsNullOrWhiteSpace(updated.AudioCueTimelineJson));

        using var doc = JsonDocument.Parse(updated.AudioCueTimelineJson!);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var entries = doc.RootElement.EnumerateArray().ToList();
        // Both audio lifecycle events appended (not overwritten).
        Assert.Equal(2, entries.Count);
        Assert.Equal("audio_started", entries[0].GetProperty("cue").GetString());
        Assert.Equal(12000, entries[0].GetProperty("atMs").GetInt32());
        Assert.Equal("audio_ended", entries[1].GetProperty("cue").GetString());
        Assert.Equal(240000, entries[1].GetProperty("atMs").GetInt32());
    }

    [Fact]
    public async Task RecordIntegrityEvent_NonAudioEventDoesNotTouchCueTimeline()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);
        var attempt = await StartHomeAttemptAsync(db, svc, userId, paperId);

        await svc.RecordIntegrityEventAsync(
            userId, attempt.Id,
            new ListeningIntegrityEventRequest("answer_changed", "{\"cuePointMs\":5000,\"questionId\":\"question-7d\"}", DateTimeOffset.UtcNow),
            default);

        var updated = await db.ListeningAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        // answer_changed must NOT populate the audio cue timeline.
        Assert.True(string.IsNullOrWhiteSpace(updated.AudioCueTimelineJson));
        Assert.NotNull(updated.LastActivityAt);
    }

    [Fact]
    public async Task RecordIntegrityEvent_AudioStartEndAppendsAcrossMultipleSections()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);
        var attempt = await StartHomeAttemptAsync(db, svc, userId, paperId);

        // Simulate A1 then A2 audio runs — four lifecycle events total.
        var positions = new[] { ("audio_started", 12000), ("audio_ended", 240000), ("audio_started", 250000), ("audio_ended", 480000) };
        foreach (var (type, cue) in positions)
        {
            await svc.RecordIntegrityEventAsync(
                userId, attempt.Id,
                new ListeningIntegrityEventRequest(type, $"{{\"cuePointMs\":{cue}}}", DateTimeOffset.UtcNow),
                default);
        }

        var updated = await db.ListeningAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        using var doc = JsonDocument.Parse(updated.AudioCueTimelineJson!);
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(4, entries.Count);
        Assert.Equal(480000, entries[^1].GetProperty("atMs").GetInt32());

        // Every event also lands as its own AuditEvent row.
        var auditCount = await db.AuditEvents.CountAsync(e => e.Action == "ListeningIntegrityEvent");
        Assert.Equal(4, auditCount);
    }

    // ── Relational write path (SQLite) ───────────────────────────────────────
    // Production incident 30 Sep 2026: the player posts an integrity event for every
    // blur / focus / click / audio tick. The tracked save carried the attempt's
    // RowVersion [ConcurrencyCheck] although it never bumped it, so any autosave or
    // section update landing between the read and the write made it throw
    // DbUpdateConcurrencyException -> 409 { retryable: true }; the browser retried
    // twice, and the retry waves used up every database connection. This is
    // best-effort telemetry and must never lose that race. The in-memory provider
    // cannot express it (no ExecuteUpdate, no token enforcement), hence SQLite.

    private sealed class RelationalDb : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public RelationalDb()
        {
            _connection.Open();
            Options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
            using var db = Create();
            db.Database.EnsureCreated();
        }

        public DbContextOptions<LearnerDbContext> Options { get; }

        public LearnerDbContext Create() => new(Options);

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private const string RelationalUserId = "learner-rv";
    private const string RelationalAttemptId = "lat-rv";

    private static async Task SeedBareAttemptAsync(LearnerDbContext db, string? holdReason = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = RelationalUserId,
            DisplayName = "Learner rv",
            Email = "learner-rv@example.test",
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = RelationalAttemptId,
            UserId = RelationalUserId,
            PaperId = "paper-rv",
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now.AddMinutes(-5),
            Mode = ListeningAttemptMode.Home,
            RowVersion = 1,
            RequiresAdminReview = holdReason is not null,
            AdminReviewReason = holdReason,
            AdminReviewFlaggedAt = holdReason is null ? null : now.AddMinutes(-3),
        });
        await db.SaveChangesAsync();
    }

    private static Task RecordAsync(LearnerDbContext db, string eventType, string? details = null)
        => new ListeningLearnerService(db, new AllowAllContentEntitlementService()).RecordIntegrityEventAsync(
            RelationalUserId,
            RelationalAttemptId,
            new ListeningIntegrityEventRequest(eventType, details, DateTimeOffset.UtcNow),
            default);

    [Fact]
    public async Task RecordIntegrityEvent_Relational_SurvivesAnAutosaveBumpingRowVersionMidFlight()
    {
        await using var rel = new RelationalDb();
        await using var db = rel.Create();
        await SeedBareAttemptAsync(db);

        // This request's context already tracks the attempt at RowVersion 1 ...
        var seen = await db.ListeningAttempts.SingleAsync();
        var activityBefore = seen.LastActivityAt;
        // ... when an autosave from another request commits RowVersion 2.
        await using (var autosave = rel.Create())
        {
            var row = await autosave.ListeningAttempts.SingleAsync();
            row.RowVersion++;
            await autosave.SaveChangesAsync();
        }

        await RecordAsync(db, "audio_started", "{\"cuePointMs\":1000}");

        await using var check = rel.Create();
        var after = await check.ListeningAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(2, after.RowVersion); // the autosave's bump is left alone
        Assert.True(after.LastActivityAt > activityBefore);
        Assert.Contains("audio_started", after.AudioCueTimelineJson);
        Assert.Equal(1, await check.AuditEvents.CountAsync(e => e.Action == "ListeningIntegrityEvent"));
    }

    [Fact]
    public async Task RecordIntegrityEvent_Relational_TimelineKeepsOnlyTheLatestProgressAndIgnoresOtherEvents()
    {
        await using var rel = new RelationalDb();
        await using var db = rel.Create();
        await SeedBareAttemptAsync(db);

        await RecordAsync(db, "audio_started", "{\"cuePointMs\":1000}");
        await RecordAsync(db, "audio_progress", "{\"cuePointMs\":5000}");
        await RecordAsync(db, "audio_progress", "{\"cuePointMs\":6500}");
        await RecordAsync(db, "window_blur");

        await using var check = rel.Create();
        var after = await check.ListeningAttempts.AsNoTracking().SingleAsync();
        using var doc = JsonDocument.Parse(after.AudioCueTimelineJson!);
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(["audio_started", "audio_progress"], entries.Select(e => e.GetProperty("cue").GetString()));
        Assert.Equal(6500, entries[1].GetProperty("atMs").GetInt32());
        Assert.Equal(4, await check.AuditEvents.CountAsync(e => e.Action == "ListeningIntegrityEvent"));
    }

    [Fact]
    public async Task RecordIntegrityEvent_Relational_AudioErrorHoldsAndAPlaybackStartReleasesIt()
    {
        await using var rel = new RelationalDb();
        await using var db = rel.Create();
        await SeedBareAttemptAsync(db);

        await RecordAsync(db, "audio_error");
        DateTimeOffset? flaggedAt;
        await using (var check = rel.Create())
        {
            var held = await check.ListeningAttempts.AsNoTracking().SingleAsync();
            Assert.True(held.RequiresAdminReview);
            Assert.Equal("audio_playback_error", held.AdminReviewReason);
            flaggedAt = held.AdminReviewFlaggedAt;
            Assert.NotNull(flaggedAt);
        }

        // A second error keeps the original flag time instead of moving it.
        await RecordAsync(db, "audio_error");
        await using (var check = rel.Create())
        {
            var held = await check.ListeningAttempts.AsNoTracking().SingleAsync();
            Assert.Equal(flaggedAt, held.AdminReviewFlaggedAt);
        }

        await RecordAsync(db, "audio_started", "{\"cuePointMs\":0}");
        await using (var check = rel.Create())
        {
            var released = await check.ListeningAttempts.AsNoTracking().SingleAsync();
            Assert.False(released.RequiresAdminReview);
            Assert.Null(released.AdminReviewReason);
            Assert.Null(released.AdminReviewFlaggedAt);
        }
    }

    [Fact]
    public async Task RecordIntegrityEvent_Relational_NeverReleasesOrRewritesAHoldItDidNotRaise()
    {
        await using var rel = new RelationalDb();
        await using var db = rel.Create();
        await SeedBareAttemptAsync(db, holdReason: "scored_media_failure");

        await RecordAsync(db, "audio_error");
        await RecordAsync(db, "audio_started", "{\"cuePointMs\":0}");

        await using var check = rel.Create();
        var after = await check.ListeningAttempts.AsNoTracking().SingleAsync();
        Assert.True(after.RequiresAdminReview);
        Assert.Equal("scored_media_failure", after.AdminReviewReason);
    }
}
