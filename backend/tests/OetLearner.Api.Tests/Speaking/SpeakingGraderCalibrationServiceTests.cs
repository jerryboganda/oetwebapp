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
        // learner-1 accepted the calibration wording (v3) before every performance these tests record.
        AddConsent(db);
        await db.SaveChangesAsync();
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

    // ── Consent gate (owner decision 2026-10-05: 365-day retention needs wording that covers it) ──

    [Fact]
    public async Task Promote_AndCandidates_RequireConsentWordingThatCoversCalibration_AtTheTimeOfRecording()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        // learner-2 only ever accepted the old wording (v2); learner-3 accepted v3 after the performance;
        // learner-4 accepted v3 and later withdrew it. None of their performances may be used.
        AddConsent(db, "learner-2", "recording.v2");
        AddConsent(db, "learner-3", acceptedAt: Now.AddHours(2));
        AddConsent(db, "learner-4", revokedAt: Now.AddHours(1));
        foreach (var (id, user) in new[] { ("s_old", "learner-2"), ("s_late", "learner-3"), ("s_withdrawn", "learner-4"), ("s_noconsent", "learner-5"), ("s_ok", "learner-1") })
        {
            AddSession(db, id, SpeakingSessionState.Finished, userId: user);
            AddTranscript(db, id);
        }
        await db.SaveChangesAsync();
        var service = Service(db);

        var candidates = await service.ListCandidatesAsync(50, CancellationToken.None);
        Assert.Equal(new[] { "s_ok" }, candidates.Select(c => c.SessionId).ToArray());

        foreach (var id in new[] { "s_old", "s_late", "s_withdrawn", "s_noconsent" })
        {
            var refused = await Assert.ThrowsAsync<ApiException>(() => service.PromoteAsync("a", "A", id, CancellationToken.None));
            Assert.Equal("speaking_calibration_consent_missing", refused.ErrorCode);
        }

        await service.PromoteAsync("a", "A", "s_ok", CancellationToken.None);
    }

    [Fact]
    public async Task Candidates_AndPromote_SkipATranscriptThatWasAlreadyErased()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddSession(db, "s_erased", SpeakingSessionState.Finished);
        AddTranscript(db, "s_erased", segmentsJson: "[]");
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Empty(await service.ListCandidatesAsync(50, CancellationToken.None));
        var refused = await Assert.ThrowsAsync<ApiException>(() => service.PromoteAsync("a", "A", "s_erased", CancellationToken.None));
        Assert.Equal("speaking_calibration_no_transcript", refused.ErrorCode);
    }

    [Fact]
    public async Task ASample_BecomesUnusable_WhenItsAudioIsGone_ItsConsentIsWithdrawn_OrItsTranscriptIsErased_AndStopsCounting()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        foreach (var id in new[] { "s_fine", "s_expired", "s_withdrawn", "s_erased" })
        {
            AddSession(db, id, SpeakingSessionState.Finished, userId: id == "s_withdrawn" ? "learner-w" : "learner-1");
            AddTranscript(db, id);
            AddRecording(db, id, $"rec_{id}");
        }
        AddConsent(db, "learner-w");
        await db.SaveChangesAsync();
        var service = Service(db);
        var ids = new Dictionary<string, string>();
        foreach (var id in new[] { "s_fine", "s_expired", "s_withdrawn", "s_erased" })
        {
            var row = await service.PromoteAsync("a", "A", id, CancellationToken.None);
            ids[id] = row.Id;
            await service.LabelAsync("a", row.Id, new SpeakingGraderCalibrationLabelRequest(FullMarks(), 350, null), CancellationToken.None);
        }

        // Day 365 / learner deletion archives the audio; withdrawal revokes consent; the retention worker erases the transcript.
        (await db.SpeakingRecordings.SingleAsync(r => r.Id == "rec_s_expired")).IsArchived = true;
        (await db.SpeakingComplianceConsents.SingleAsync(c => c.UserId == "learner-w")).RevokedAt = Now.AddHours(1);
        (await db.SpeakingTranscripts.SingleAsync(t => t.SpeakingSessionId == "s_erased")).SegmentsJson = "[]";
        await db.SaveChangesAsync();

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(1, overview.Coverage.Labelled);
        Assert.Equal(4, overview.Coverage.Total);
        Assert.True(overview.Samples.Single(r => r.Id == ids["s_fine"]).Usable);
        foreach (var gone in new[] { "s_expired", "s_withdrawn", "s_erased" })
        {
            Assert.False(overview.Samples.Single(r => r.Id == ids[gone]).Usable, gone);
        }
    }

    [Fact]
    public async Task Marks_AreFrozenWhileACalibrationRunUsesThePerformance()
    {
        await using var db = new LearnerDbContext(_options);
        var sampleId = await SeedSampleAsync(db, "s1");
        var service = Service(db);
        await service.LabelAsync("a", sampleId, new SpeakingGraderCalibrationLabelRequest(FullMarks(), 350, null), CancellationToken.None);
        db.SpeakingGraderCalibrationRuns.Add(new SpeakingGraderCalibrationRun
        {
            Id = "spgr_1", Repeats = 2, UseAudio = true, Status = SpeakingGraderCalibrationRunStatus.Running,
            CreatedById = "a", CreatedAt = Now,
        });
        db.SpeakingGraderCalibrationGrades.Add(new SpeakingGraderCalibrationGrade
        {
            Id = "spgg_1", RunId = "spgr_1", SampleId = sampleId, Repeat = 1,
        });
        await db.SaveChangesAsync();

        var relabel = await Assert.ThrowsAsync<ApiException>(() => service.LabelAsync(
            "a", sampleId, new SpeakingGraderCalibrationLabelRequest(FullMarks(5, 3), 400, null), CancellationToken.None));
        var exclude = await Assert.ThrowsAsync<ApiException>(() => service.ExcludeAsync(
            sampleId, new SpeakingGraderCalibrationExcludeRequest("changed my mind"), CancellationToken.None));
        Assert.Equal("speaking_calibration_run_active", relabel.ErrorCode);
        Assert.Equal("speaking_calibration_run_active", exclude.ErrorCode);

        // Once the run is finalised the marks can be corrected again (the run's report is already frozen).
        (await db.SpeakingGraderCalibrationRuns.SingleAsync()).Status = SpeakingGraderCalibrationRunStatus.Complete;
        await db.SaveChangesAsync();
        await service.LabelAsync("a", sampleId, new SpeakingGraderCalibrationLabelRequest(FullMarks(5, 3), 400, null), CancellationToken.None);
    }

    [Fact]
    public async Task StreamingAClip_IsAudited_WithoutTheLearnersIdentity()
    {
        await using var db = new LearnerDbContext(_options);
        var sampleId = await SeedSampleAsync(db, "s1");
        db.AuditEvents.RemoveRange(await db.AuditEvents.ToListAsync());
        await db.SaveChangesAsync();

        await Service(db).AuditClipAccessAsync("admin-1", "Dr Hesham", sampleId, "rec-1", CancellationToken.None);

        var audit = await db.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("SpeakingRecordingAccessed", audit.Action);
        Assert.Equal("rec-1", audit.ResourceId);
        Assert.Equal("admin-1", audit.ActorId);
        Assert.DoesNotContain("learner-1", audit.Details);
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

    [Fact]
    public void Coverage_RequiresTheNearPassLineBlockToStraddleThePassLine()
    {
        // Ten performances at 350-380 meet the old "ten near the line" rule, but none sit just below 350.
        var allAbove = Enumerable.Range(0, 10).Select(i => Labelled(350 + (i % 4) * 10)).ToList();

        var coverage = SpeakingGraderCalibrationService.BuildCoverage(allAbove);

        Assert.Equal(10, coverage.LabelledNearPassLine);
        Assert.Equal(0, coverage.LabelledBelowPassLine);
        Assert.Equal(10, coverage.LabelledAtOrAbovePassLine);
        Assert.Equal(4, coverage.RequiredEachSideOfPassLine);
        Assert.Contains(coverage.Unmet, u => u.Contains("Just below the pass line (320-340): 0 of 4"));
        Assert.DoesNotContain(coverage.Unmet, u => u.Contains("At or just above the pass line"));
    }

    [Fact]
    public void Coverage_DoesNotCount_APerformanceThatIsNoLongerUsable()
    {
        var kept = Labelled(480);
        var gone = Labelled(470);

        var coverage = SpeakingGraderCalibrationService.BuildCoverage(
            new[] { kept, gone }, new HashSet<string> { gone.Id });

        Assert.Equal(1, coverage.Labelled);
        Assert.Equal(1, coverage.LabelledByGrade["A"]);
    }

    // ── Full Mock samples (owner request 7 Oct 2026) ─────────────────────

    [Fact]
    public async Task MockCandidates_AreCompletedAiExamsWithBothTranscripts_AndCarryNoLearnerIdentity()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddMockExam(db, "exam-ok", sessionA: "exa", sessionB: "exb");
        AddSession(db, "exa", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exa");
        AddSession(db, "exb", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exb");
        // Not candidates: one card without a usable transcript, a cancelled exam, an already-promoted one.
        AddMockExam(db, "exam-notranscript", sessionA: "exnta", sessionB: "exntb");
        AddSession(db, "exnta", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exnta");
        AddSession(db, "exntb", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exntb", isLatest: false);
        AddMockExam(db, "exam-cancelled", sessionA: "exc", sessionB: "exd", state: SpeakingExamState.Cancelled);
        AddSession(db, "exc", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exc");
        AddSession(db, "exd", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exd");
        AddMockExam(db, "exam-taken", sessionA: "exta", sessionB: "extb");
        AddSession(db, "exta", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exta");
        AddSession(db, "extb", state: SpeakingSessionState.Finished);
        AddTranscript(db, "extb");
        db.SpeakingGraderCalibrationMockSamples.Add(new SpeakingGraderCalibrationMockSample
        {
            Id = "spgcm_taken", SpeakingExamId = "exam-taken", SessionAId = "exta", SessionBId = "extb",
            TranscriptAId = "t_exta", TranscriptBId = "t_extb", CardAId = "card-1", CardBId = "card-1",
            ProfessionId = "nursing", PromotedById = "admin-1", PromotedAt = Now, UpdatedAt = Now,
        });
        await db.SaveChangesAsync();

        var candidates = await Service(db).ListMockCandidatesAsync(50, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("exam-ok", candidate.ExamId);
        Assert.Equal("Asthma review", candidate.CardATitle);
        Assert.Equal("Asthma review", candidate.CardBTitle);
        // The contract has no learner identity at all.
        Assert.DoesNotContain(typeof(SpeakingGraderCalibrationMockCandidate).GetProperties(),
            p => p.Name.Contains("User", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MockPromote_PinsBothTranscripts_KeepsBothCardsAudio_AndAuditsIt()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddMockExam(db, "exam-1", sessionA: "exa", sessionB: "exb");
        AddSession(db, "exa", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exa", id: "t_exa");
        AddSession(db, "exb", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exb", id: "t_exb");
        AddRecording(db, "exa", "rec_a");
        AddRecording(db, "exb", "rec_b", retention: Now.AddDays(10));
        await db.SaveChangesAsync();

        var row = await Service(db).PromoteMockAsync("admin-1", "Dr Hesham", "exam-1", CancellationToken.None);

        Assert.Equal("pending", row.Status);
        Assert.True(row.HasAudio);
        var sample = await db.SpeakingGraderCalibrationMockSamples.AsNoTracking().SingleAsync();
        Assert.Equal("t_exa", sample.TranscriptAId);
        Assert.Equal("t_exb", sample.TranscriptBId);
        Assert.Equal("card-1", sample.CardAId);

        var keepUntil = Now + SpeakingGraderCalibrationService.CalibrationAudioRetention;
        var retention = await db.SpeakingRecordings.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r.RetentionExpiresAt);
        Assert.Equal(keepUntil, retention["rec_a"]);
        Assert.Equal(keepUntil, retention["rec_b"]);

        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(a => a.Action == "SpeakingGraderCalibrationMockSamplePromoted");
        Assert.Equal(sample.Id, audit.ResourceId);

        // A second promotion of the same exam is refused.
        var again = await Assert.ThrowsAsync<ApiException>(() =>
            Service(db).PromoteMockAsync("admin-1", "Dr Hesham", "exam-1", CancellationToken.None));
        Assert.Equal("speaking_calibration_already_promoted", again.ErrorCode);
    }

    [Fact]
    public async Task MockPromote_RejectsAMockRecordedUnderAnOlderConsent()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddConsent(db, userId: "learner-old", version: "recording.v2");
        AddMockExam(db, "exam-old", sessionA: "exoa", sessionB: "exob", userId: "learner-old");
        AddSession(db, "exoa", state: SpeakingSessionState.Finished, userId: "learner-old");
        AddTranscript(db, "exoa");
        AddSession(db, "exob", state: SpeakingSessionState.Finished, userId: "learner-old");
        AddTranscript(db, "exob");
        await db.SaveChangesAsync();

        var failure = await Assert.ThrowsAsync<ApiException>(() =>
            Service(db).PromoteMockAsync("admin-1", "Dr Hesham", "exam-old", CancellationToken.None));

        Assert.Equal("speaking_calibration_consent_missing", failure.ErrorCode);
    }

    [Fact]
    public async Task MockDetail_ShowsBothCardsBlind_AndLabelStoresOneSetOfMarksForTheWholeTest()
    {
        await using var db = new LearnerDbContext(_options);
        AddCard(db, "card-1", "Asthma review");
        AddMockExam(db, "exam-1", sessionA: "exa", sessionB: "exb");
        AddSession(db, "exa", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exa", id: "t_exa");
        AddSession(db, "exb", state: SpeakingSessionState.Finished);
        AddTranscript(db, "exb", id: "t_exb");
        await db.SaveChangesAsync();
        var sampleId = (await Service(db).PromoteMockAsync("admin-1", "Dr Hesham", "exam-1", CancellationToken.None)).Id;

        var pending = await Service(db).GetMockDetailAsync(sampleId, CancellationToken.None);
        Assert.Equal("pending", pending.Status);
        Assert.Equal("Asthma review", pending.CardA.Title);
        Assert.Equal("Asthma review", pending.CardB.Title);
        Assert.Single(pending.TranscriptA);
        Assert.Single(pending.TranscriptB);
        Assert.Null(pending.Label);

        var service = Service(db);
        var marked = await service.LabelMockAsync("admin-1", sampleId, new SpeakingGraderCalibrationLabelRequest(
            SpeakingGraderCalibrationService.Criteria.ToDictionary(c => c.Code, c => c.Max == 6 ? 4 : 2), 340, "Mixed test, slightly below the line."),
            CancellationToken.None);

        Assert.Equal("labelled", marked.Status);
        Assert.Equal(340, marked.ExpertOverallScaled);
        Assert.Equal("C+", marked.ExpertGrade);
        var detail = await service.GetMockDetailAsync(sampleId, CancellationToken.None);
        Assert.Equal(340, detail.Label!.OverallScaled);
        Assert.Equal(9, detail.Label.Scores.Count);

        // One mark counts towards the mock coverage, in the same shape as the card coverage.
        var overview = await service.GetMockOverviewAsync(CancellationToken.None);
        Assert.Equal(1, overview.Coverage.Labelled);
        Assert.Equal(1, overview.Coverage.LabelledByGrade["C+"]);
    }

    // ── seeds ────────────────────────────────────────────────────────────

    private static void AddMockExam(
        LearnerDbContext db, string id, string sessionA, string sessionB,
        string userId = "learner-1", SpeakingExamState state = SpeakingExamState.Completed)
        => db.SpeakingExamSessions.Add(new SpeakingExamSession
        {
            Id = id,
            UserId = userId,
            ProfessionId = "nursing",
            Mode = SpeakingExamMode.Ai,
            State = state,
            CardAId = "card-1",
            CardBId = "card-1",
            SessionAId = sessionA,
            SessionBId = sessionB,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

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
        SpeakingSessionMode mode = SpeakingSessionMode.AiSelfPractice,
        string userId = "learner-1")
        => db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = id,
            UserId = userId,
            RolePlayCardId = "card-1",
            Mode = mode,
            State = state,
            ElapsedSeconds = 290,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

    /// <summary>A recording consent. The default is the calibration wording (v3) accepted the day before the performance.</summary>
    private static void AddConsent(
        LearnerDbContext db, string userId = "learner-1", string version = "recording.v3",
        DateTimeOffset? acceptedAt = null, DateTimeOffset? revokedAt = null)
        => db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ConsentType = SpeakingComplianceConsentTypes.Recording,
            ConsentVersion = version,
            AcceptedAt = acceptedAt ?? Now.AddDays(-1),
            RevokedAt = revokedAt,
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
