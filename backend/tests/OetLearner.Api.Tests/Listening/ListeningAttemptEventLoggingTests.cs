using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;

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
    private sealed class AllowAllContentEntitlementService : IContentEntitlementService
    {
        public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.FromResult(new ContentEntitlementResult(true, "test", "premium", null));

        public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.CompletedTask;

        public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal) => false;
    }

    private static (LearnerDbContext db, ListeningLearnerService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
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
}
