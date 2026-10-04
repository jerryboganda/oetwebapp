using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The expert side of Speaking grader calibration (owner spec 4 Oct 2026): promote a finished AI card,
/// let the expert mark it BLIND to the AI score, and report coverage. The blind guarantee is structural —
/// the service never reads an AI assessment — and is pinned here by a source scan as well as behaviour.
/// </summary>
public sealed class SpeakingGraderCalibrationServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        DisableForeignKeys();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        DisableForeignKeys();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private void DisableForeignKeys()
    {
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=OFF;";
        pragma.ExecuteNonQuery();
    }

    private static SpeakingGraderCalibrationService Service(LearnerDbContext db)
        => new(db, new FixedTimeProvider(Now));

    // ── Candidates ───────────────────────────────────────────────────────

    [Fact]
    public async Task Candidates_AreFinishedAiCardsWithATranscript_FlagAudio_AndCarryNoLearnerIdentity()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddSession(db, "s_audio", state: SpeakingSessionState.Finished);
        AddTranscript(db, "s_audio");
        AddRecording(db, "s_audio", "rec-1");
        AddSession(db, "s_noaudio", state: SpeakingSessionState.Finished, mode: SpeakingSessionMode.AiExam);
        AddTranscript(db, "s_noaudio");
        // Not candidates: still running, a human tutor's session, no usable transcript, already promoted.
        AddSession(db, "s_active", state: SpeakingSessionState.Active);
        AddTranscript(db, "s_active");
        AddSession(db, "s_tutor", state: SpeakingSessionState.Finished, mode: SpeakingSessionMode.LiveTutor);
        AddTranscript(db, "s_tutor");
        AddSession(db, "s_failed", state: SpeakingSessionState.Finished);
        AddTranscript(db, "s_failed", provider: SpeakingTranscriptionPipeline.StateFailed, isLatest: false);
        AddSession(db, "s_taken", state: SpeakingSessionState.Finished);
        AddTranscript(db, "s_taken");
        db.SpeakingGraderCalibrationSamples.Add(new SpeakingGraderCalibrationSample
        {
            Id = "spgc_taken", SpeakingSessionId = "s_taken", TranscriptId = "t", RolePlayCardId = "card-1",
            ProfessionId = "nursing", PromotedById = "admin-1", PromotedAt = Now, UpdatedAt = Now,
        });
        await db.SaveChangesAsync();

        var candidates = await Service(db).ListCandidatesAsync(50, CancellationToken.None);

        Assert.Equal(new[] { "s_audio", "s_noaudio" }, candidates.Select(c => c.SessionId).OrderBy(id => id).ToArray());
        Assert.True(candidates.Single(c => c.SessionId == "s_audio").HasAudio);
        Assert.False(candidates.Single(c => c.SessionId == "s_noaudio").HasAudio);
        Assert.All(candidates, c => Assert.Equal("Asthma review", c.CardTitle));
        // The contract has no learner identity at all.
        Assert.DoesNotContain(typeof(SpeakingGraderCalibrationCandidate).GetProperties(),
            p => p.Name.Contains("User", StringComparison.OrdinalIgnoreCase));
    }

    // ── Promote ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_PinsTheTranscript_KeepsTheAudioForAYear_AndAuditsIt()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddSession(db, "s1", state: SpeakingSessionState.Finished);
        AddTranscript(db, "s1", id: "t_latest");
        AddRecording(db, "s1", "rec_default");                                                    // follows the default window
        AddRecording(db, "s1", "rec_short", retention: Now.AddDays(10));                           // would be deleted soon
        AddRecording(db, "s1", "rec_long", retention: Now.AddDays(900));                           // already longer: untouched
        AddRecording(db, "s1", "rec_warmup", isWarmup: true, retention: Now.AddDays(1));           // warm-up is not performance
        AddRecording(db, "s1", "rec_archived", isArchived: true);                                  // blob already gone
        await db.SaveChangesAsync();

        var row = await Service(db).PromoteAsync("admin-1", "Dr Hesham", "s1", CancellationToken.None);

        Assert.Equal("pending", row.Status);
        Assert.True(row.HasAudio);
        Assert.Equal("Asthma review", row.CardTitle);
        var sample = await db.SpeakingGraderCalibrationSamples.AsNoTracking().SingleAsync();
        Assert.Equal("t_latest", sample.TranscriptId);
        Assert.Equal("card-1", sample.RolePlayCardId);

        var keepUntil = Now + SpeakingGraderCalibrationService.CalibrationAudioRetention;
        var retention = await db.SpeakingRecordings.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r.RetentionExpiresAt);
        Assert.Equal(keepUntil, retention["rec_default"]);
        Assert.Equal(keepUntil, retention["rec_short"]);
        Assert.Equal(Now.AddDays(900), retention["rec_long"]);
        Assert.Equal(Now.AddDays(1), retention["rec_warmup"]);
        Assert.Null(retention["rec_archived"]);

        var audit = await db.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("SpeakingGraderCalibrationSamplePromoted", audit.Action);
        Assert.Equal(sample.Id, audit.ResourceId);
        Assert.Equal("admin-1", audit.ActorId);
    }

    [Fact]
    public async Task Promote_RejectsAnUnfinishedSession_ATutorSession_ADuplicate_AndAMissingTranscript()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddSession(db, "s_active", state: SpeakingSessionState.Active);
        AddTranscript(db, "s_active");
        AddSession(db, "s_tutor", state: SpeakingSessionState.Finished, mode: SpeakingSessionMode.LiveTutor);
        AddTranscript(db, "s_tutor");
        AddSession(db, "s_nottranscribed", state: SpeakingSessionState.Finished);
        AddSession(db, "s_ok", state: SpeakingSessionState.Finished);
        AddTranscript(db, "s_ok");
        await db.SaveChangesAsync();
        var service = Service(db);

        foreach (var id in new[] { "s_active", "s_tutor" })
        {
            var notEligible = await Assert.ThrowsAsync<ApiException>(() => service.PromoteAsync("a", "A", id, CancellationToken.None));
            Assert.Equal("speaking_calibration_session_not_eligible", notEligible.ErrorCode);
        }

        var noTranscript = await Assert.ThrowsAsync<ApiException>(() => service.PromoteAsync("a", "A", "s_nottranscribed", CancellationToken.None));
        Assert.Equal("speaking_calibration_no_transcript", noTranscript.ErrorCode);

        await service.PromoteAsync("a", "A", "s_ok", CancellationToken.None);
        var duplicate = await Assert.ThrowsAsync<ApiException>(() => service.PromoteAsync("a", "A", "s_ok", CancellationToken.None));
        Assert.Equal("speaking_calibration_already_promoted", duplicate.ErrorCode);

        var missing = await Assert.ThrowsAsync<ApiException>(() => service.PromoteAsync("a", "A", "nope", CancellationToken.None));
        Assert.Equal("speaking_calibration_session_not_found", missing.ErrorCode);
    }

    // ── Blind labelling view ─────────────────────────────────────────────

    [Fact]
    public async Task Detail_IsBlind_ShowsTheGradersTranscript_AndNeverAnAiResult()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddSession(db, "s1", state: SpeakingSessionState.Finished);
        AddTranscript(db, "s1", id: "t1", segmentsJson: JsonSerializer.Serialize(new object[]
        {
            new { speaker = "candidate", startMs = 0, endMs = 2000, text = "Hi, can you hear me?" },
            new { speaker = "patient", startMs = 2000, endMs = 3000, text = "Yeah, I hear you. Go ahead." },
            new { speaker = "candidate", startMs = 3000, endMs = 9000, text = "Good morning, I am Dr Lee." },
            new { speaker = "patient", startMs = 9000, endMs = 11000, text = "Hello doctor." },
        }));
        AddRecording(db, "s1", "rec-1");
        // An AI result exists for this session; the labelling view must not carry any of it.
        db.SpeakingAiAssessments.Add(new SpeakingAiAssessment
        {
            Id = "spa_1", SpeakingSessionId = "s1", TranscriptId = "t1", Provider = "ai_gateway", ModelId = "m",
            EstimatedScaledScore = 340, ReadinessBand = "developing", GeneratedAt = Now,
            OverallSummary = "LEAKED-AI-SUMMARY",
        });
        await db.SaveChangesAsync();
        var service = Service(db);
        var row = await service.PromoteAsync("admin-1", "Dr Hesham", "s1", CancellationToken.None);

        var detail = await service.GetDetailAsync(row.Id, CancellationToken.None);

        // The grader reads the transcript without the connection check; so does the expert.
        Assert.Equal(new[] { "Good morning, I am Dr Lee.", "Hello doctor." }, detail.Transcript.Select(l => l.Text).ToArray());
        Assert.Equal(9, detail.Criteria.Count);
        Assert.Equal("rec-1", Assert.Single(detail.Clips).RecordingId);
        Assert.Null(detail.Label);

        var json = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("LEAKED-AI-SUMMARY", json);
        Assert.DoesNotContain("340", json);
        Assert.DoesNotContain("developing", json);
    }

    [Fact]
    public void TheService_NeverReadsAnAiAssessment_SoTheViewCannotBeAnchoredByTheScoreUnderTest()
    {
        var source = File.ReadAllText(FindSource("Services/Speaking/SpeakingGraderCalibrationService.cs"));

        Assert.DoesNotContain("SpeakingAiAssessments", source);
        Assert.DoesNotContain("SpeakingSimulationV11Assessments", source);
        Assert.DoesNotContain("SpeakingAiAssessment ", source);
    }

    // ── Label / exclude ──────────────────────────────────────────────────

    private static Dictionary<string, int> FullMarks(int linguistic = 4, int clinical = 2)
        => SpeakingGraderCalibrationService.Criteria.ToDictionary(c => c.Code, c => c.Max == 6 ? linguistic : clinical);

    [Fact]
    public async Task Label_StoresTheNineScoresAndTheOverall_AndCountsTowardsCoverage()
    {
        await using var db = new LearnerDbContext(_options);
        var sampleId = await SeedSampleAsync(db, "s1");
        var service = Service(db);

        var row = await service.LabelAsync("admin-1", sampleId, new SpeakingGraderCalibrationLabelRequest(
            FullMarks(), 350, "  Solid structure, a little hesitant.  "), CancellationToken.None);

        Assert.Equal("labelled", row.Status);
        Assert.Equal(350, row.ExpertOverallScaled);
        Assert.Equal("B", row.ExpertGrade);
        var stored = await db.SpeakingGraderCalibrationSamples.AsNoTracking().SingleAsync();
        Assert.Equal("admin-1", stored.LabelledById);
        Assert.Equal(Now, stored.LabelledAt);
        Assert.Equal("Solid structure, a little hesitant.", stored.ExpertNotes);
        var scores = JsonSerializer.Deserialize<Dictionary<string, int>>(stored.ExpertScoresJson!)!;
        Assert.Equal(9, scores.Count);
        Assert.Equal(4, scores["intelligibility"]);
        Assert.Equal(2, scores["informationGiving"]);

        // The expert can see (and correct) his own marks, still without any AI value.
        var detail = await service.GetDetailAsync(sampleId, CancellationToken.None);
        Assert.Equal(350, detail.Label!.OverallScaled);

        var overview = await service.GetOverviewAsync(CancellationToken.None);
        Assert.Equal(1, overview.Coverage.Labelled);
        Assert.Equal(1, overview.Coverage.LabelledByGrade["B"]);
    }

    [Fact]
    public async Task Label_RejectsAMissingOrOutOfRangeScore_AndAnOverallThatIsNotAStepOfTen()
    {
        await using var db = new LearnerDbContext(_options);
        var sampleId = await SeedSampleAsync(db, "s1");
        var service = Service(db);

        var missing = FullMarks();
        missing.Remove("fluency");
        var tooHighLinguistic = FullMarks();
        tooHighLinguistic["intelligibility"] = 7;
        var tooHighClinical = FullMarks();
        tooHighClinical["structure"] = 4;

        foreach (var request in new[]
        {
            new SpeakingGraderCalibrationLabelRequest(missing, 350, null),
            new SpeakingGraderCalibrationLabelRequest(tooHighLinguistic, 350, null),
            new SpeakingGraderCalibrationLabelRequest(tooHighClinical, 350, null),
            new SpeakingGraderCalibrationLabelRequest(FullMarks(), 345, null),
            new SpeakingGraderCalibrationLabelRequest(FullMarks(), 510, null),
            new SpeakingGraderCalibrationLabelRequest(FullMarks(), null, null),
            new SpeakingGraderCalibrationLabelRequest(null, 350, null),
        })
        {
            var error = await Assert.ThrowsAsync<ApiException>(
                () => service.LabelAsync("admin-1", sampleId, request, CancellationToken.None));
            Assert.Equal("speaking_calibration_label_invalid", error.ErrorCode);
        }

        Assert.Equal(SpeakingGraderCalibrationSampleStatus.Pending,
            (await db.SpeakingGraderCalibrationSamples.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Exclude_NeedsAReason_AndAnExcludedSampleIsNeverCounted_ButCanBeMarkedAgain()
    {
        await using var db = new LearnerDbContext(_options);
        var sampleId = await SeedSampleAsync(db, "s1");
        var service = Service(db);

        var noReason = await Assert.ThrowsAsync<ApiException>(
            () => service.ExcludeAsync(sampleId, new SpeakingGraderCalibrationExcludeRequest("  "), CancellationToken.None));
        Assert.Equal("speaking_calibration_reason_required", noReason.ErrorCode);

        var row = await service.ExcludeAsync(sampleId, new SpeakingGraderCalibrationExcludeRequest("No speech on the recording"), CancellationToken.None);
        Assert.Equal("excluded", row.Status);
        var overview = await service.GetOverviewAsync(CancellationToken.None);
        Assert.Equal(1, overview.Coverage.Excluded);
        Assert.Equal(0, overview.Coverage.Labelled);

        // The expert changes his mind: marking it again makes it usable and clears the reason.
        await service.LabelAsync("admin-1", sampleId, new SpeakingGraderCalibrationLabelRequest(FullMarks(), 300, null), CancellationToken.None);
        var detail = await service.GetDetailAsync(sampleId, CancellationToken.None);
        Assert.Equal("labelled", detail.Status);
        Assert.Equal("", detail.ExcludedReason);
    }

    // ── Coverage (pure) ──────────────────────────────────────────────────

    private static SpeakingGraderCalibrationSample Labelled(int overall, bool hasAudio = true)
        => new()
        {
            Id = $"spgc_{Guid.NewGuid():N}", SpeakingSessionId = Guid.NewGuid().ToString("N"), TranscriptId = "t",
            RolePlayCardId = "c", ProfessionId = "nursing", PromotedById = "a", HasAudio = hasAudio,
            Status = SpeakingGraderCalibrationSampleStatus.Labelled, ExpertOverallScaled = overall,
        };

    [Fact]
    public void Coverage_SaysPlainlyWhatIsStillMissing()
    {
        var samples = new List<SpeakingGraderCalibrationSample>
        {
            Labelled(480), Labelled(460), Labelled(470),                  // A x3
            Labelled(400), Labelled(350), Labelled(360, hasAudio: false), // B x3 (two are near the line)
            Labelled(320), Labelled(310),                                 // C+ x2
        };

        var coverage = SpeakingGraderCalibrationService.BuildCoverage(samples);

        Assert.Equal(8, coverage.Labelled);
        Assert.Equal(3, coverage.LabelledByGrade["A"]);
        Assert.Equal(3, coverage.LabelledByGrade["B"]);
        Assert.Equal(2, coverage.LabelledByGrade["C+"]);
        Assert.Equal(0, coverage.LabelledByGrade["E"]);
        Assert.Equal(3, coverage.LabelledNearPassLine); // 350, 360, 320 (310 is outside 320-380)
        Assert.False(coverage.MeetsCoverage);
        Assert.Contains(coverage.Unmet, u => u.Contains("22 more") && u.Contains("8 of 30"));
        Assert.Contains(coverage.Unmet, u => u.StartsWith("Grade E: 0 of 3"));
        Assert.Contains(coverage.Unmet, u => u.Contains("Near the pass line (320-380): 3 of 10"));
        Assert.DoesNotContain(coverage.Unmet, u => u.StartsWith("Audio:")); // 7 of 8 have audio = 87.5%
        Assert.DoesNotContain(coverage.Unmet, u => u.Contains('_')); // plain words, never codes
    }

    [Fact]
    public void Coverage_IsMet_WhenThirtyMarkedCoverEveryGradeAndTheThresholdAndMostHaveAudio()
    {
        // 6 grades x 5 = 30; four of the B/C+ ones sit in 320-380 plus the rest below.
        var overalls = new[] { 480, 490, 460, 470, 500, 400, 410, 440, 350, 360, 330, 320, 340, 310, 300,
                               250, 260, 220, 230, 280, 150, 160, 120, 130, 140, 50, 60, 20, 30, 40 };
        var samples = overalls.Select(o => Labelled(o)).ToList();
        // Pad the near-the-line band to ten with extra B/C+ performances.
        samples.AddRange(new[] { 370, 380, 330, 340, 350, 360 }.Select(o => Labelled(o)));

        var coverage = SpeakingGraderCalibrationService.BuildCoverage(samples);

        Assert.True(coverage.MeetsCoverage, string.Join(" | ", coverage.Unmet));
        Assert.Empty(coverage.Unmet);
    }

    // ── seeds ────────────────────────────────────────────────────────────

    private async Task<string> SeedSampleAsync(LearnerDbContext db, string sessionId)
    {
        AddCard(db, "card-1", "Asthma review");
        AddSession(db, sessionId, state: SpeakingSessionState.Finished);
        AddTranscript(db, sessionId);
        await db.SaveChangesAsync();
        return (await Service(db).PromoteAsync("admin-1", "Dr Hesham", sessionId, CancellationToken.None)).Id;
    }

    private static void AddCard(LearnerDbContext db, string cardId, string title)
        => db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = $"content-{cardId}",
            ProfessionId = "nursing",
            ScenarioTitle = title,
            Setting = "Clinic",
            CandidateRole = "Nurse",
            InterlocutorRole = "Patient",
            Background = "Background detail",
            Task1 = "Task 1",
            PatientEmotion = "neutral",
            CommunicationGoal = "Inform",
            ClinicalTopic = "general",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = ContentStatus.Published,
            CreatedAt = Now,
            UpdatedAt = Now,
            PublishedAt = Now,
        });

    private static void AddSession(
        LearnerDbContext db, string id, SpeakingSessionState state,
        SpeakingSessionMode mode = SpeakingSessionMode.AiSelfPractice)
        => db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = id,
            UserId = "learner-1",
            RolePlayCardId = "card-1",
            Mode = mode,
            State = state,
            ElapsedSeconds = 290,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

    private static void AddTranscript(
        LearnerDbContext db, string sessionId, string? id = null,
        string provider = "openai-whisper", bool isLatest = true, string? segmentsJson = null)
        => db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = id ?? $"t_{sessionId}",
            SpeakingSessionId = sessionId,
            Provider = provider,
            Language = "en",
            SegmentsJson = segmentsJson ?? "[{\"speaker\":\"candidate\",\"startMs\":0,\"endMs\":1000,\"text\":\"Hello.\"}]",
            IsLatest = isLatest,
            WordCount = 1,
            MeanConfidence = 0.9,
            GeneratedAt = Now,
        });

    private static void AddRecording(
        LearnerDbContext db, string sessionId, string id,
        bool isWarmup = false, bool isArchived = false, DateTimeOffset? retention = null)
    {
        db.MediaAssets.Add(new MediaAsset
        {
            Id = $"asset-{id}",
            OriginalFilename = "audio.webm",
            MimeType = "audio/webm",
            Format = "webm",
            SizeBytes = 1024,
            StoragePath = $"audio/{id}.webm",
        });
        db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = id,
            SpeakingSessionId = sessionId,
            MediaAssetId = $"asset-{id}",
            Kind = SpeakingRecordingKind.Audio,
            Source = SpeakingRecordingSource.ConversationHub,
            DurationSeconds = 60,
            SizeBytes = 1024,
            Sha256 = new string('a', 64),
            MimeType = "audio/webm",
            IsWarmup = isWarmup,
            IsArchived = isArchived,
            RetentionExpiresAt = retention,
            CreatedAt = Now,
        });
    }

    private static string FindSource(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src", "OetLearner.Api", relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relative} from {AppContext.BaseDirectory}.");
    }
}
