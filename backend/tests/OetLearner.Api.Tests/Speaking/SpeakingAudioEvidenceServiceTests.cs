using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The acoustic half of Speaking grading (owner spec 4 Oct 2026): the OpenAI audio judge hears the candidate's
/// clips — and only the clips — and its answer is verified against the transcript before it is trusted. These
/// tests pin the properties the owner cares about: the model is never given the transcript, it runs on its own
/// provider row (never the grade chain), every failure is "unavailable" with a reason (grading goes on), and a
/// reply that does not match what the candidate actually said is discarded.
/// </summary>
public sealed class SpeakingAudioEvidenceServiceTests : IAsyncLifetime
{
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

    // ── The request: audio only, pinned provider, nothing but what the clips carry ─────────

    [Fact]
    public async Task Assess_SendsOnlyTheJoinedAudioToThePinnedAudioProvider_NeverTheTranscript()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1, 1], "audio/webm");
        await h.AddClipAsync("s1", "rec-b", [2, 2], "audio/mp4");
        h.Gateway.Completion = AudioReply(heard: "Good morning I am Doctor Hesham and this is your asthma review");

        var evidence = await h.Service.AssessAsync(Request(Segments(
            ("candidate", "Good morning, I am Doctor Hesham and this is your asthma review.", "rec-a"),
            ("patient", "Hello doctor, thanks for seeing me.", null),
            ("candidate", "Let me ask about your inhaler technique.", "rec-b"))), default);

        Assert.True(evidence.IsAudio);
        var call = Assert.Single(h.Gateway.Requests);
        Assert.Equal(AiProviderRegistry.SpeakingAudioProviderCode, call.Provider);
        Assert.Equal(AiFeatureCodes.SpeakingAudioAssess, call.FeatureCode);
        Assert.Equal("speaking.audio_assess.v1", call.PromptTemplateId);
        Assert.Equal(0, call.Temperature);
        var audio = Assert.Single(call.AudioAttachments!);
        Assert.Equal("audio/mpeg", audio.MimeType);
        Assert.Equal(h.Transcoder.Join.Mp3, audio.Data);

        // The model hears the clips; it is never handed the words (neither the candidate's nor the patient's).
        Assert.DoesNotContain("asthma", call.UserInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inhaler", call.UserInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thanks for seeing me", call.UserInput, StringComparison.OrdinalIgnoreCase);

        // The clips reached the transcoder in the order the candidate spoke, patient turns left out.
        Assert.Equal(new[] { "audio/webm", "audio/mp4" }, h.Transcoder.Clips.Select(c => c.MimeType));
        Assert.Equal(new byte[] { 1, 1 }, h.Transcoder.Clips[0].Bytes);
        Assert.Equal(new byte[] { 2, 2 }, h.Transcoder.Clips[1].Bytes);

        Assert.Equal(RuleKind.Speaking, h.Gateway.LastContext!.Kind);
        Assert.Equal(AiTaskMode.Score, h.Gateway.LastContext.Task);
        Assert.Equal("role_play", h.Gateway.LastContext.CardType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assess_UsesTheSameGrantAndContextAsTheGrade(bool grant)
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        h.Gateway.Completion = AudioReply();

        await h.Service.AssessAsync(Request(Segments(("candidate", "Good morning I am Doctor Hesham", "rec-a")), grant: grant), default);

        var call = Assert.Single(h.Gateway.Requests);
        Assert.Equal(grant, call.FreeSampleGrant);
        Assert.Equal("learner-1", call.UserId);
        Assert.Equal(AiAssessmentContext.Practice, call.AssessmentContext);
    }

    [Fact]
    public async Task Assess_TwoRunsOnTheSameClips_NeverShareARequestReference()
    {
        // The coordinator dedupes identical requests; the nonce keeps two real grades from colliding.
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        h.Gateway.Completion = AudioReply();
        var segments = Segments(("candidate", "Good morning I am Doctor Hesham", "rec-a"));

        await h.Service.AssessAsync(Request(segments), default);
        await h.Service.AssessAsync(Request(segments), default);

        Assert.Equal(2, h.Gateway.Requests.Count);
        Assert.NotEqual(h.Gateway.Requests[0].UserInput, h.Gateway.Requests[1].UserInput);
    }

    // ── Which clips are used ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assess_LiveVoiceClips_AreJoinedInSpokenOrder_SkippingArchivedAndWarmupClips()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-1", [1], "audio/webm");
        await h.AddClipAsync("s1", "rec-2", [2], "audio/webm");
        await h.AddClipAsync("s1", "rec-old", [9], "audio/webm", archived: true);
        await h.AddClipAsync("s1", "rec-warm", [8], "audio/webm", warmup: true);
        await h.AddClipAsync("other-session", "rec-x", [7], "audio/webm");
        h.Gateway.Completion = AudioReply(heard: "Hello I am the doctor");

        // Spoken order is 2 then 1; rec-x belongs to another session; rec-old / rec-warm are never used.
        var evidence = await h.Service.AssessAsync(Request(Segments(
            ("candidate", "Hello, I am the doctor.", "rec-2"),
            ("candidate", "How are you feeling?", "rec-1"),
            ("candidate", "Archived.", "rec-old"),
            ("candidate", "Warm up.", "rec-warm"),
            ("candidate", "Not mine.", "rec-x"))), default);

        Assert.True(evidence.IsAudio);
        Assert.Equal(new[] { new byte[] { 2 }, new byte[] { 1 } }, h.Transcoder.Clips.Select(c => c.Bytes));
    }

    [Fact]
    public async Task Assess_RecorderSession_UsesTheSessionRecording()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", SpeakingSessionRecordingService.RecordingIdFor("s1"), [5, 5, 5], "audio/webm");
        await h.AddClipAsync("s1", "rec-extra", [6], "audio/webm");
        h.Gateway.Completion = AudioReply(heard: "Hello I am the doctor looking after you");

        var evidence = await h.Service.AssessAsync(Request(Segments(
            ("candidate", "Hello, I am the doctor looking after you today.", null))), default);

        Assert.True(evidence.IsAudio);
        var clip = Assert.Single(h.Transcoder.Clips);
        Assert.Equal(new byte[] { 5, 5, 5 }, clip.Bytes);
    }

    [Fact]
    public async Task Assess_WithoutAnyUsableRecording_IsNoAudio_AndNeverCallsTheModel()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-old", [9], "audio/webm", archived: true);

        var evidence = await h.Service.AssessAsync(Request(Segments(
            ("candidate", "Hello.", "rec-old"))), default);

        Assert.Equal(SpeakingAudioEvidence.StatusUnavailable, evidence.Status);
        Assert.Equal("no_audio", evidence.Reason);
        Assert.Empty(h.Gateway.Requests);
        Assert.Empty(h.Transcoder.Clips);
    }

    [Fact]
    public async Task Assess_WhenTheStoredBlobIsGone_ReportsMissingBlob()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm", writeBlob: false);

        var evidence = await h.Service.AssessAsync(Request(Segments(("candidate", "Hello.", "rec-a"))), default);

        Assert.Equal("audio_missing_blob", evidence.Reason);
        Assert.Empty(h.Gateway.Requests);
    }

    [Fact]
    public async Task Assess_AudioShorterThanAFewSeconds_IsNotJudged()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        h.Transcoder.Join = h.Transcoder.Join with { DurationMs = 900 };

        var evidence = await h.Service.AssessAsync(Request(Segments(("candidate", "Hello.", "rec-a"))), default);

        Assert.Equal("audio_too_short", evidence.Reason);
        Assert.Empty(h.Gateway.Requests);
    }

    // ── Every failure is "unavailable"; grading never fails because of audio ─────────────

    [Fact]
    public async Task Assess_WhenTheTranscoderIsUnavailable_ReportsItAndDoesNotThrow()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        h.Transcoder.Throw = new SpeakingAudioTranscoderUnavailableException("ffmpeg is not installed.");

        var evidence = await h.Service.AssessAsync(Request(Segments(("candidate", "Hello.", "rec-a"))), default);

        Assert.Equal("audio_transcoder_unavailable", evidence.Reason);
        Assert.Empty(h.Gateway.Requests);
    }

    [Fact]
    public async Task Assess_ProviderFailures_BecomeAReason_NeverAnException()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        var request = Request(Segments(("candidate", "Hello.", "rec-a")));

        var cases = new (Exception Failure, string Reason)[]
        {
            (new AiQuotaDeniedException("ai_quota_exceeded", "no credits"), "ai_refused"),
            (new AiBudgetExhaustedException("monthly"), "ai_refused"),
            (new InvalidOperationException("provider unreachable"), "provider_error"),
            // The stage's own time budget ran out (the caller did not cancel).
            (new OperationCanceledException(), "timeout"),
        };
        foreach (var (failure, reason) in cases)
        {
            h.Gateway.Throw = failure;
            var evidence = await h.Service.AssessAsync(request, default);
            Assert.Equal(SpeakingAudioEvidence.StatusUnavailable, evidence.Status);
            Assert.Equal(reason, evidence.Reason);
        }
    }

    [Fact]
    public async Task Assess_WhenTheCallerCancels_TheCancellationIsNotSwallowed()
    {
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Service.AssessAsync(Request(Segments(("candidate", "Hello.", "rec-a"))), cancelled.Token));
    }

    [Fact]
    public async Task Assess_ReplyThatDoesNotMatchWhatTheCandidateSaid_IsDiscarded()
    {
        // The model was not given the transcript: a "judgement" of audio it did not hear cannot match the opening.
        await using var h = new Harness(_options);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        h.Gateway.Completion = AudioReply(heard: "Please take a seat and tell me what brought you in");

        var evidence = await h.Service.AssessAsync(Request(Segments(
            ("candidate", "Good morning, I am Doctor Hesham.", "rec-a"))), default);

        Assert.Equal("audio_unverified", evidence.Reason);
        Assert.Null(evidence.IntelligibilityScore);
    }

    [Fact]
    public async Task IsEnabled_ReadsTheAdminFeatureFlag()
    {
        await using var h = new Harness(_options);
        Assert.False(await h.Service.IsEnabledAsync(default));

        h.Db.FeatureFlags.Add(new FeatureFlag
        {
            Id = "ff-audio",
            Name = "Speaking audio assessment",
            Key = SpeakingAudioAssessmentOptions.FeatureFlagKey,
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await h.Db.SaveChangesAsync();
        Assert.False(await h.Service.IsEnabledAsync(default));

        var flag = await h.Db.FeatureFlags.SingleAsync(f => f.Id == "ff-audio");
        flag.Enabled = true;
        await h.Db.SaveChangesAsync();
        Assert.True(await h.Service.IsEnabledAsync(default));
    }

    // ── The release probe: one uploaded clip and the phrase that was really said in it ────

    [Fact]
    public async Task Probe_JudgesTheClipOnThePlatformAccount_AndReportsWhatWasHeard()
    {
        await using var h = new Harness(_options);
        const string phrase = "Good afternoon, my name is Doctor Okafor and I will be looking after you today.";
        h.Gateway.Completion = AudioReply(heard: "Good afternoon my name is Doctor Okafor and I will be looking after you");

        var result = await h.Service.ProbeAsync(new MemoryStream(new byte[] { 9, 9 }), "audio/wav", phrase, default);

        Assert.True(result.Evidence.IsAudio);
        Assert.Equal(4, result.Evidence.IntelligibilityScore);
        Assert.Equal("Good afternoon my name is Doctor Okafor and I will be looking after you", result.HeardOpening);
        Assert.InRange(result.OpeningSimilarity, 0.8, 1.0);
        Assert.Equal(5_000, result.DurationMs);

        // The same pinned call a real grade makes, but on the platform account: no learner, no plan gate, no grant.
        var call = Assert.Single(h.Gateway.Requests);
        Assert.Equal(AiProviderRegistry.SpeakingAudioProviderCode, call.Provider);
        Assert.Equal(AiFeatureCodes.SpeakingAudioAssess, call.FeatureCode);
        Assert.Null(call.UserId);
        Assert.False(call.FreeSampleGrant);
        Assert.DoesNotContain("Okafor", call.UserInput, StringComparison.OrdinalIgnoreCase);
        var clip = Assert.Single(h.Transcoder.Clips);
        Assert.Equal(new byte[] { 9, 9 }, clip.Bytes);
        Assert.Equal("audio/wav", clip.MimeType);
    }

    [Fact]
    public async Task Probe_ShowsWhatTheModelHeard_EvenWhenItsJudgementIsNotTrusted()
    {
        await using var h = new Harness(_options);
        h.Gateway.Completion = AudioReply(heard: "Please take a seat and tell me what brought you in");

        var result = await h.Service.ProbeAsync(new MemoryStream(new byte[] { 1 }), "audio/wav",
            "Good afternoon, my name is Doctor Okafor.", default);

        Assert.Equal("audio_unverified", result.Evidence.Reason);
        Assert.Equal("Please take a seat and tell me what brought you in", result.HeardOpening);
        Assert.Equal(0, result.OpeningSimilarity);
    }

    [Fact]
    public async Task Probe_ClipTooShortToJudge_DoesNotCallTheModel()
    {
        await using var h = new Harness(_options);
        h.Transcoder.Join = h.Transcoder.Join with { DurationMs = 400 };

        var result = await h.Service.ProbeAsync(new MemoryStream(new byte[] { 1 }), "audio/wav", "Hello doctor", default);

        Assert.Equal("audio_too_short", result.Evidence.Reason);
        Assert.Empty(h.Gateway.Requests);
    }

    [Fact]
    public async Task Probe_UnlikeGrading_LetsAProviderFailureThrough_SoTheAdminCanSeeIt()
    {
        await using var h = new Harness(_options);
        h.Gateway.Throw = new InvalidOperationException("provider refused the audio");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.ProbeAsync(new MemoryStream(new byte[] { 1 }), "audio/wav", "Hello doctor", default));

        Assert.Equal("provider refused the audio", failure.Message);
    }

    [Theory]
    [InlineData("good afternoon doctor okafor", "Good afternoon, Doctor Okafor!", 1.0)]
    [InlineData("one two three four", "five six seven eight", 0.0)]
    [InlineData("one two three four", "one two five six", 1.0 / 3)]
    [InlineData("", "anything", 0.0)]
    public void OpeningSimilarity_IsTheSharedShareOfTheFirstWords(string heard, string said, double expected)
        => Assert.Equal(expected, SpeakingAudioEvidenceService.OpeningSimilarity(heard, said), precision: 6);

    // ── Reading the model's answer─────────────────────────────────────────────────────────

    [Fact]
    public void ParseResponse_VerifiedJudgement_CarriesScoreObservationsAndFluency()
    {
        var evidence = SpeakingAudioEvidenceService.ParseResponse(
            AudioReply(score: 4), "Good morning, I am Doctor Hesham.", "gpt-audio-1.5", clipCount: 3, durationMs: 61_000, partialCoverage: false);

        Assert.True(evidence.IsAudio);
        Assert.Equal(4, evidence.IntelligibilityScore);
        Assert.Equal("Mostly clear; a few vowels blurred.", evidence.IntelligibilityRationale);
        Assert.Equal("high", evidence.Confidence);
        Assert.Equal("gpt-audio-1.5", evidence.Model);
        Assert.Equal(3, evidence.ClipCount);
        Assert.Equal(61_000, evidence.DurationMs);
        var observation = Assert.Single(evidence.Observations);
        Assert.Equal(new SpeakingAudioObservation(2, 31, "Final consonants dropped in 'inhaler'.", "inha-uh"), observation);
        Assert.NotNull(evidence.Fluency);
        Assert.Equal(121, evidence.Fluency!.SpeechRateWpm);
        Assert.Equal(2, evidence.Fluency.LongPauses);
        Assert.Equal(3, evidence.Fluency.HesitationCount);
        Assert.Equal(4, evidence.Fluency.FillerCount);
        Assert.Equal(1, evidence.Fluency.RestartCount);
    }

    [Theory]
    [InlineData("good", false, false, "high", "high")]
    [InlineData("fair", false, false, "medium", "medium")]
    [InlineData("poor", false, false, "high", "low")]
    [InlineData("good", true, false, "high", "low")]
    [InlineData("good", false, true, "high", "low")]
    [InlineData("good", false, false, "unsure", "medium")]
    public void ParseResponse_WeakRecordingSecondVoiceOrPartialCoverage_KeepsTheScoreButLowersConfidence(
        string quality, bool bleed, bool partial, string modelConfidence, string expected)
    {
        var evidence = SpeakingAudioEvidenceService.ParseResponse(
            AudioReply(quality: quality, bleed: bleed, confidence: modelConfidence), "Good morning, I am Doctor Hesham.", "m", 1, 5_000, partial);

        Assert.True(evidence.IsAudio);
        Assert.Equal(4, evidence.IntelligibilityScore);
        Assert.Equal(expected, evidence.Confidence);
        Assert.Equal(bleed, evidence.PatientVoiceBleed);
    }

    [Theory]
    [InlineData("9", 6)]
    [InlineData("-2", 0)]
    [InlineData("3.5", 4)]
    [InlineData("\"5\"", 5)]
    public void ParseResponse_ScoreIsReadLeniently_AndClampedToTheBand(string rawScore, int expected)
    {
        var reply = $$"""{"heardOpening":"Hello doctor","audioUsable":true,"intelligibility":{"score":{{rawScore}},"rationale":"ok","observations":[]},"confidence":"high"}""";

        var evidence = SpeakingAudioEvidenceService.ParseResponse(reply, "Hello doctor", "m", 1, 5_000, false);

        Assert.True(evidence.IsAudio);
        Assert.Equal(expected, evidence.IntelligibilityScore);
    }

    [Fact]
    public void ParseResponse_UnusableAudio_IsUnavailable()
    {
        var evidence = SpeakingAudioEvidenceService.ParseResponse(
            AudioReply(usable: false), "Hello doctor", "m", 1, 5_000, false);

        Assert.Equal("audio_unusable", evidence.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I could not hear anything.")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"heardOpening":"Hello doctor","audioUsable":true}""")]
    [InlineData("""{"heardOpening":"Hello doctor","audioUsable":true,"intelligibility":{"rationale":"no score"}}""")]
    public void ParseResponse_ReplyWithoutAScore_IsAParseError(string reply)
    {
        var evidence = SpeakingAudioEvidenceService.ParseResponse(reply, "Hello doctor", "m", 1, 5_000, false);

        Assert.Equal(SpeakingAudioEvidence.StatusUnavailable, evidence.Status);
        Assert.Equal("parse_error", evidence.Reason);
    }

    [Fact]
    public void ParseResponse_JsonInsideMarkdownFences_IsStillRead()
    {
        var evidence = SpeakingAudioEvidenceService.ParseResponse(
            "```json\n" + AudioReply(score: 3) + "\n```", "Good morning, I am Doctor Hesham.", "m", 1, 5_000, false);

        Assert.True(evidence.IsAudio);
        Assert.Equal(3, evidence.IntelligibilityScore);
    }

    [Fact]
    public void ParseResponse_MissingHeardOpening_IsUnverified_WhenThereIsATranscriptToCheckAgainst()
    {
        var reply = """{"audioUsable":true,"intelligibility":{"score":5,"rationale":"ok","observations":[]},"confidence":"high"}""";

        Assert.Equal("audio_unverified", SpeakingAudioEvidenceService.ParseResponse(reply, "Hello doctor", "m", 1, 5_000, false).Reason);
        // No candidate words to compare against (a silent transcript): nothing to contradict the model.
        Assert.True(SpeakingAudioEvidenceService.ParseResponse(reply, null, "m", 1, 5_000, false).IsAudio);
    }

    [Fact]
    public void ParseResponse_KeepsAtMostSixObservations_AndSkipsOnesWithoutAnIssue()
    {
        var observations = Enumerable.Range(1, 9)
            .Select(i => new { clip = i, approxSecond = i * 3, issue = i == 2 ? string.Empty : $"issue {i}", example = string.Empty });
        var reply = JsonSerializer.Serialize(new
        {
            heardOpening = "Hello doctor",
            audioUsable = true,
            intelligibility = new { score = 4, rationale = "ok", observations },
            confidence = "high",
        });

        var evidence = SpeakingAudioEvidenceService.ParseResponse(reply, "Hello doctor", "m", 1, 5_000, false);

        Assert.Equal(6, evidence.Observations.Count);
        Assert.DoesNotContain(evidence.Observations, o => string.IsNullOrWhiteSpace(o.Issue));
        Assert.All(evidence.Observations, o => Assert.Null(o.Example));
    }

    [Theory]
    [InlineData("Good morning, I am Doctor Hesham", "Good morning I am doctor Hesham, how are you today", true)]
    [InlineData("Hello", "Hello, I'm the doctor looking after you today", true)]
    [InlineData("one two three four five six", "one two three four seven eight", true)]
    [InlineData("one two three four five six", "one two three seven eight nine", false)]
    [InlineData("Please take a seat", "Hello I am the doctor", false)]
    [InlineData("", "Hello", false)]
    [InlineData("Hello", "", false)]
    public void OpeningMatches_ComparesTheFirstWordsIgnoringCaseAndPunctuation(string heard, string transcript, bool expected)
        => Assert.Equal(expected, SpeakingAudioEvidenceService.OpeningMatches(heard, transcript));

    [Fact]
    public void ReadCandidateTurns_KeepsOnlyTheCandidate_WhateverTheCasing()
    {
        var turns = SpeakingAudioEvidenceService.ReadCandidateTurns(
            """[{"Speaker":"Candidate","Text":"One","StartMs":0,"EndMs":1500,"SourceRecordingId":" r1 "},{"speaker":"patient","text":"No"},{"speaker":"learner","text":"Two","startMs":2000,"endMs":3000}]""");

        Assert.Equal(2, turns.Count);
        Assert.Equal(("One", 0, 1500, "r1"), (turns[0].Text, turns[0].StartMs, turns[0].EndMs, turns[0].RecordingId));
        Assert.Null(turns[1].RecordingId);
        Assert.Empty(SpeakingAudioEvidenceService.ReadCandidateTurns("not json"));
        Assert.Empty(SpeakingAudioEvidenceService.ReadCandidateTurns(null));
    }

    [Fact]
    public void ReadCandidateTurns_DropsLeadingChatterClips_AndKeepsEverythingFromTheRealOpeningOn()
    {
        var turns = SpeakingAudioEvidenceService.ReadCandidateTurns(
            """[{"speaker":"candidate","text":"Hi, can you hear me?","startMs":0,"endMs":1500,"sourceRecordingId":"chatter"},{"speaker":"patient","text":"Yeah, I hear you. Go ahead.","startMs":1600,"endMs":2600},{"speaker":"candidate","text":"Hello, I'm Dr Faisal.","startMs":3000,"endMs":4200,"sourceRecordingId":"real"}]""");

        var turn = Assert.Single(turns);
        Assert.Equal(("Hello, I'm Dr Faisal.", 3000, 4200, "real"), (turn.Text, turn.StartMs, turn.EndMs, turn.RecordingId));
    }

    [Fact]
    public void ReadCandidateTurns_KeepsATurnThatSharesChatterAndTheRealOpening()
    {
        var turns = SpeakingAudioEvidenceService.ReadCandidateTurns(
            """[{"speaker":"candidate","text":"Hi, can you hear me? Hello, I'm Dr Faisal.","startMs":0,"endMs":2000,"sourceRecordingId":"shared"}]""");

        var turn = Assert.Single(turns);
        Assert.Equal("Hello, I'm Dr Faisal.", turn.Text);
        Assert.Equal("shared", turn.RecordingId);
    }

    [Fact]
    public void UserPrompt_AsksOnlyWhatCanBeHeard_AndNamesTheReply()
    {
        var single = SpeakingAudioEvidenceService.BuildUserPrompt(1, 4, 90, "nonce-1");
        var several = SpeakingAudioEvidenceService.BuildUserPrompt(5, 4, 90, "nonce-2");

        Assert.Contains("one recording", single);
        Assert.Contains("5 short clips", several);
        Assert.Contains("nonce-1", single);
        Assert.Contains("heardOpening", single);
        Assert.Contains("Judge only what you can HEAR", single);
        Assert.DoesNotContain("RULE_", single);
    }

    [Fact]
    public void StageVersion_NamesTheModelThatListened()
    {
        Assert.Equal("audio-openai.v1:gpt-audio-1.5", SpeakingAudioEvidenceService.StageVersion(" gpt-audio-1.5 "));
        Assert.Equal("audio-openai.v1:default", SpeakingAudioEvidenceService.StageVersion(null));
        Assert.Equal("audio-openai.v1:default", SpeakingAudioEvidenceService.StageVersion("  "));
    }

    [Fact]
    public void ReasonText_IsPlainLanguage_AndNeverALeakOfTheCode()
    {
        Assert.Contains("no audio recording", SpeakingAudioEvidenceService.ReasonText("no_audio"));
        Assert.Equal("audio evidence was not available", SpeakingAudioEvidenceService.ReasonText(null));
        foreach (var code in new[] { "no_audio", "audio_missing_blob", "audio_too_short", "audio_unusable", "audio_unverified", "audio_transcoder_unavailable", "timeout", "provider_error", "ai_refused", "parse_error", "something_new" })
        {
            Assert.DoesNotContain("_", SpeakingAudioEvidenceService.ReasonText(code));
        }
    }

    // ── Guards: this stage stays beside the grade chain, never inside it ─────────────────

    [Fact]
    public void AudioStage_NeverGoesThroughTheGradeChainOrItsPin()
    {
        var source = File.ReadAllText(FindSource("Services/Speaking/SpeakingAudioEvidenceService.cs"));

        // RULE MAX-ALWAYS-ON: the Claude Max grade route is untouched by this stage, and the stage cannot be
        // re-pointed at it or at any fallback provider by the grading options.
        Assert.DoesNotContain("SpeakingGradeChain", source);
        Assert.DoesNotContain("SpeakingGradingOptions", source);
        Assert.DoesNotContain("PinnedProviderCode", source);
        Assert.DoesNotContain("writing-claude-sub", source);
        Assert.Contains("Provider = AiProviderRegistry.SpeakingAudioProviderCode", source);
        Assert.Contains("FeatureCode = AiFeatureCodes.SpeakingAudioAssess", source);
        // The grade-chain failover (a second, unpinned attempt) must not exist here either.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, @"gateway\.CompleteAsync\(").Cast<System.Text.RegularExpressions.Match>());
    }

    [Fact]
    public void Assessor_RunsTheAudioStageBeforeTheGradeCall_NotAfterIt()
    {
        var source = File.ReadAllText(FindSource("Services/Speaking/SpeakingAiAssessmentService.cs"));

        var stage = source.IndexOf("audioEvidence.AssessAsync(", StringComparison.Ordinal);
        var grade = source.IndexOf("FeatureCode = AiFeatureCodes.SpeakingGrade", StringComparison.Ordinal);
        Assert.True(stage > 0 && grade > 0 && stage < grade, "The audio judgement must exist before the grade request is built.");
    }

    // ── Joining clips (pure) ───────────────────────────────────────────────────────────────

    [Fact]
    public void PcmJoiner_PutsASilenceBetweenClips_AndReportsTheDuration()
    {
        var clip = Pcm(1000, 7);

        var joined = PcmJoiner.Join([clip, clip], gapMilliseconds: 600, maxSeconds: 60);

        Assert.False(joined.Truncated);
        Assert.Equal(2600, joined.DurationMs);
        Assert.Equal((clip.Length * 2) + 19_200, joined.Pcm.Length);
        Assert.All(joined.Pcm.AsSpan(clip.Length, 19_200).ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void PcmJoiner_CutsAtTheMaximumLength_AndSaysSo()
    {
        var joined = PcmJoiner.Join([Pcm(3000, 1), Pcm(3000, 1)], gapMilliseconds: 600, maxSeconds: 4);

        Assert.True(joined.Truncated);
        Assert.Equal(4000, joined.DurationMs);
        Assert.Equal(4 * PcmJoiner.SampleRate * 2, joined.Pcm.Length);
    }

    [Fact]
    public void PcmJoiner_SkipsEmptyClips_AndDropsAStrayOddByte()
    {
        var joined = PcmJoiner.Join([[], [1, 0, 2, 0, 9], [3, 0]], gapMilliseconds: 0, maxSeconds: 60);

        Assert.False(joined.Truncated);
        Assert.Equal(new byte[] { 1, 0, 2, 0, 3, 0 }, joined.Pcm);
    }

    // ── ffmpeg (the real process only where it is installed) ──────────────────────────────

    [Fact]
    public async Task Ffmpeg_WhenTheBinaryIsMissing_ReportsUnavailable_NotACrash()
    {
        var transcoder = new FfmpegSpeakingAudioTranscoder(Options.Create(
            new SpeakingAudioAssessmentOptions { FfmpegPath = "definitely-not-ffmpeg-oet-test" }));

        await Assert.ThrowsAsync<SpeakingAudioTranscoderUnavailableException>(() =>
            transcoder.JoinToMp3Async([new SpeakingAudioClipInput(new MemoryStream(new byte[] { 1, 2, 3 }), "audio/webm")], default));
    }

    [Fact]
    public async Task Ffmpeg_WithNoClips_IsAnArgumentError()
    {
        var transcoder = new FfmpegSpeakingAudioTranscoder(Options.Create(new SpeakingAudioAssessmentOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => transcoder.JoinToMp3Async([], default));
    }

    [Fact]
    public async Task Ffmpeg_WhenInstalled_JoinsClipsIntoOneMp3()
    {
        // Runs the real process only on a machine that has ffmpeg (the API image installs it; the production
        // probe is the end-to-end proof). Elsewhere there is nothing to execute, so the test ends here.
        if (!FfmpegAvailable()) return;

        var transcoder = new FfmpegSpeakingAudioTranscoder(Options.Create(new SpeakingAudioAssessmentOptions()));

        var join = await transcoder.JoinToMp3Async(
            [new SpeakingAudioClipInput(new MemoryStream(Wav(1000)), "audio/wav"), new SpeakingAudioClipInput(new MemoryStream(Wav(1000)), "audio/wav")],
            default);

        Assert.Equal(2, join.ClipCount);
        Assert.InRange(join.DurationMs, 2500, 2700);
        Assert.True(join.Mp3.Length > 500);
        // An mp3 starts with an ID3 tag or an MPEG frame sync.
        Assert.True((join.Mp3[0] == 0x49 && join.Mp3[1] == 0x44 && join.Mp3[2] == 0x33) || join.Mp3[0] == 0xFF);
    }

    // ── A join a helper prepared (media.speaking-join): optional, never waited for, local is always the fallback ──

    [Fact]
    public async Task Assess_UsesAJoinAHelperPrepared_WhenOneMatchesTheClips_AndNeverRunsTheLocalTranscoder()
    {
        var remote = new FakeRemoteJoin { Serve = new SpeakingAudioJoin([9, 9, 9], 7_000, 2, false) };
        await using var h = new Harness(_options, remote);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        await h.AddClipAsync("s1", "rec-b", [2], "audio/webm");
        h.Gateway.Completion = AudioReply(heard: "Hello I am the doctor");

        var evidence = await h.Service.AssessAsync(Request(Segments(
            ("candidate", "Hello, I am the doctor.", "rec-b"),
            ("candidate", "How are you feeling?", "rec-a"))), default);

        Assert.True(evidence.IsAudio);
        Assert.Equal(7_000, evidence.DurationMs);
        Assert.Equal(2, evidence.ClipCount);
        Assert.Empty(h.Transcoder.Clips); // the local ffmpeg never ran
        var audio = Assert.Single(Assert.Single(h.Gateway.Requests).AudioAttachments!);
        Assert.Equal(new byte[] { 9, 9, 9 }, audio.Data);
        var asked = Assert.Single(remote.Serves);
        Assert.Equal("s1", asked.SessionId);
        Assert.Equal(2, asked.Shas.Count);
        Assert.All(asked.Shas, sha => Assert.Equal(new string('a', 64), sha));
    }

    [Fact]
    public async Task Assess_WhenNoPreparedJoinExists_JoinsLocallyAsAlways()
    {
        var remote = new FakeRemoteJoin();
        await using var h = new Harness(_options, remote);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        h.Gateway.Completion = AudioReply(heard: "Hello I am the doctor");

        var evidence = await h.Service.AssessAsync(Request(Segments(("candidate", "Hello, I am the doctor.", "rec-a"))), default);

        Assert.True(evidence.IsAudio);
        Assert.Single(remote.Serves);
        Assert.Single(h.Transcoder.Clips);
        Assert.Equal(h.Transcoder.Join.Mp3, Assert.Single(Assert.Single(h.Gateway.Requests).AudioAttachments!).Data);
    }

    [Fact]
    public async Task Assess_WhenThePreparedJoinCannotBeUsed_JoinsLocally_AndTheGradeGoesOn()
    {
        var remote = new FakeRemoteJoin { Throw = new InvalidOperationException("storage hiccup") };
        await using var h = new Harness(_options, remote);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        h.Gateway.Completion = AudioReply(heard: "Hello I am the doctor");

        var evidence = await h.Service.AssessAsync(Request(Segments(("candidate", "Hello, I am the doctor.", "rec-a"))), default);

        Assert.True(evidence.IsAudio);
        Assert.Single(h.Transcoder.Clips);
    }

    [Fact]
    public async Task Assess_AClipWithNoKnownHash_CannotMatchAPreparedJoin_SoItIsNeverAsked()
    {
        var remote = new FakeRemoteJoin { Serve = new SpeakingAudioJoin([9], 7_000, 1, false) };
        await using var h = new Harness(_options, remote);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm", sha256: string.Empty);
        h.Gateway.Completion = AudioReply(heard: "Hello I am the doctor");

        var evidence = await h.Service.AssessAsync(Request(Segments(("candidate", "Hello, I am the doctor.", "rec-a"))), default);

        Assert.True(evidence.IsAudio);
        Assert.Empty(remote.Serves);
        Assert.Single(h.Transcoder.Clips);
    }

    [Fact]
    public async Task Assess_WhenTheCallerCancelsWhileAskingForAPreparedJoin_TheCancellationIsNotSwallowed()
    {
        using var cancelled = new CancellationTokenSource();
        var remote = new FakeRemoteJoin { CancelOnServe = cancelled };
        await using var h = new Harness(_options, remote);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Service.AssessAsync(Request(Segments(("candidate", "Hello.", "rec-a"))), cancelled.Token));
        Assert.Empty(h.Transcoder.Clips);
    }

    [Fact]
    public async Task Assess_ALookupThatTimesOutOnItsOwn_IsAFallbackToTheLocalJoin_NotAFailure()
    {
        var remote = new FakeRemoteJoin { Throw = new OperationCanceledException() };
        await using var h = new Harness(_options, remote);
        await h.AddClipAsync("s1", "rec-a", [1], "audio/webm");
        h.Gateway.Completion = AudioReply(heard: "Hello I am the doctor");

        var evidence = await h.Service.AssessAsync(Request(Segments(("candidate", "Hello, I am the doctor.", "rec-a"))), default);

        Assert.True(evidence.IsAudio);
        Assert.Single(h.Transcoder.Clips);
    }

    [Fact]
    public async Task Assess_PicksTheSameClipsInTheSameOrder_WhetherOrNotAHelperIsConsulted()
    {
        // The remote precompute reads the clips through the SAME query as the audio stage, so its key can match.
        var remote = new FakeRemoteJoin();
        await using var h = new Harness(_options, remote);
        await h.AddClipAsync("s1", "rec-1", [1], "audio/webm", sha256: new string('1', 64));
        await h.AddClipAsync("s1", "rec-2", [2], "audio/webm", sha256: new string('2', 64));
        await h.AddClipAsync("s1", "rec-old", [9], "audio/webm", archived: true, sha256: new string('9', 64));
        await h.AddClipAsync("s1", "rec-warm", [8], "audio/webm", warmup: true, sha256: new string('8', 64));
        h.Gateway.Completion = AudioReply(heard: "Hello I am the doctor");

        await h.Service.AssessAsync(Request(Segments(
            ("candidate", "Hello, I am the doctor.", "rec-2"),
            ("candidate", "How are you feeling?", "rec-1"))), default);

        Assert.Equal(new[] { new string('2', 64), new string('1', 64) }, Assert.Single(remote.Serves).Shas);
    }

    // ── Builders ───────────────────────────────────────────────────────────────────────────

    private static SpeakingAudioAssessRequest Request(string segmentsJson, bool grant = false)
        => new("s1", "learner-1", "medicine", "role_play", segmentsJson, grant, AiAssessmentContext.Practice);

    private static string Segments(params (string Speaker, string Text, string? RecordingId)[] turns)
    {
        var startMs = 0;
        var segments = new List<Dictionary<string, object?>>();
        foreach (var (speaker, text, recordingId) in turns)
        {
            var segment = new Dictionary<string, object?>
            {
                ["speaker"] = speaker,
                ["startMs"] = startMs,
                ["endMs"] = startMs + 4000,
                ["text"] = text,
            };
            if (recordingId is not null) segment["sourceRecordingId"] = recordingId;
            segments.Add(segment);
            startMs += 5000;
        }

        return JsonSerializer.Serialize(segments);
    }

    private static string AudioReply(
        string heard = "Good morning I am Doctor Hesham",
        int score = 4,
        bool usable = true,
        string quality = "good",
        bool bleed = false,
        string confidence = "high")
        => JsonSerializer.Serialize(new
        {
            heardOpening = heard,
            audioUsable = usable,
            audioQuality = quality,
            patientVoiceBleed = bleed,
            intelligibility = new
            {
                score,
                rationale = "Mostly clear; a few vowels blurred.",
                observations = new[] { new { clip = 2, approxSecond = 31, issue = "Final consonants dropped in 'inhaler'.", example = "inha-uh" } },
            },
            fluency = new
            {
                speechRateWpm = 121,
                longPauses = 2,
                hesitationCount = 3,
                fillerCount = 4,
                restartCount = 1,
                observations = new[] { "Paused before explaining the result." },
            },
            confidence,
        });

    /// <summary>Raw 16 kHz mono 16-bit PCM: <paramref name="milliseconds"/> of the little-endian sample <paramref name="sample"/>.</summary>
    private static byte[] Pcm(int milliseconds, short sample)
    {
        var bytes = new byte[milliseconds * 16 * 2];
        for (var i = 0; i < bytes.Length; i += 2)
        {
            bytes[i] = (byte)(sample & 0xFF);
            bytes[i + 1] = (byte)(sample >> 8);
        }

        return bytes;
    }

    private static byte[] Wav(int milliseconds)
    {
        var samples = 16 * milliseconds;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples * 2));
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16_000);
        writer.Write(32_000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / 16_000.0) * 8000));
        writer.Flush();
        return stream.ToArray();
    }

    private static bool FfmpegAvailable()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (probe is null) return false;
            probe.StandardOutput.ReadToEnd();
            probe.StandardError.ReadToEnd();
            probe.WaitForExit(10_000);
            return probe.HasExited && probe.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
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

    // ── Fakes ──────────────────────────────────────────────────────────────────────────────

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"oet-audio-evidence-{Guid.NewGuid():N}");

        public Harness(DbContextOptions<LearnerDbContext> options, IRemoteSpeakingJoin? remoteJoin = null)
        {
            Db = new LearnerDbContext(options);
            Storage = new LocalFileStorage(new TestHostEnvironment(_root), Options.Create(new StorageOptions { LocalRootPath = _root }));
            Service = new SpeakingAudioEvidenceService(Db, Storage, Transcoder, Gateway, remoteJoin: remoteJoin);
        }

        public LearnerDbContext Db { get; }
        public LocalFileStorage Storage { get; }
        public CapturingGateway Gateway { get; } = new();
        public StubTranscoder Transcoder { get; } = new();
        public SpeakingAudioEvidenceService Service { get; }

        public async Task AddClipAsync(
            string sessionId, string recordingId, byte[] bytes, string mimeType,
            bool archived = false, bool warmup = false, bool writeBlob = true, string? sha256 = null)
        {
            var key = $"audio/{recordingId}.bin";
            if (writeBlob) await Storage.WriteAsync(key, new MemoryStream(bytes), default);

            Db.MediaAssets.Add(new MediaAsset
            {
                Id = $"asset-{recordingId}",
                OriginalFilename = "clip.bin",
                MimeType = mimeType,
                Format = "bin",
                SizeBytes = bytes.Length,
                StoragePath = key,
            });
            Db.SpeakingRecordings.Add(new SpeakingRecording
            {
                Id = recordingId,
                SpeakingSessionId = sessionId,
                MediaAssetId = $"asset-{recordingId}",
                Kind = SpeakingRecordingKind.Audio,
                Source = SpeakingRecordingSource.ConversationHub,
                DurationSeconds = 5,
                SizeBytes = bytes.Length,
                Sha256 = sha256 ?? new string('a', 64),
                MimeType = mimeType,
                IsArchived = archived,
                IsWarmup = warmup,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class CapturingGateway : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = new();
        public AiGroundingContext? LastContext { get; private set; }
        public string Completion { get; set; } = string.Empty;
        public string ResolvedModel { get; set; } = "gpt-audio-1.5-test";
        public Exception? Throw { get; set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        {
            LastContext = context;
            return new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n(test fixture)\n",
                TaskInstruction = "Score this speaking attempt.",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookVersion = "test-1.0.0",
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    ScoringPassMark = 350,
                    ScoringGrade = "B",
                    AppliedRulesCount = 1,
                    AppliedRuleIds = new[] { "RULE_01" },
                },
            };
        }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (Throw is { } failure) throw failure;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new AiGatewayResult
            {
                Completion = Completion,
                ResolvedModel = ResolvedModel,
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt.Metadata.RulebookVersion,
            });
        }
    }

    private sealed class StubTranscoder : ISpeakingAudioTranscoder
    {
        public SpeakingAudioJoin Join { get; set; } = new([1, 2, 3, 4], 5_000, 1, false);
        public Exception? Throw { get; set; }
        public List<(byte[] Bytes, string MimeType)> Clips { get; } = new();

        public async Task<SpeakingAudioJoin> JoinToMp3Async(IReadOnlyList<SpeakingAudioClipInput> clips, CancellationToken ct)
        {
            if (Throw is { } failure) throw failure;
            foreach (var clip in clips)
            {
                using var copy = new MemoryStream();
                await clip.Content.CopyToAsync(copy, ct);
                Clips.Add((copy.ToArray(), clip.MimeType));
            }

            return Join with { ClipCount = clips.Count };
        }
    }

    private sealed class FakeRemoteJoin : IRemoteSpeakingJoin
    {
        public SpeakingAudioJoin? Serve { get; set; }
        public Exception? Throw { get; set; }
        public CancellationTokenSource? CancelOnServe { get; set; }
        public List<(string SessionId, IReadOnlyList<string> Shas)> Serves { get; } = new();

        public Task<SpeakingAudioJoin?> TryServeAsync(string sessionId, IReadOnlyList<string> clipSha256s, CancellationToken ct)
        {
            Serves.Add((sessionId, clipSha256s.ToList()));
            if (CancelOnServe is not null)
            {
                // The caller gives up while the lookup is in flight.
                CancelOnServe.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            if (Throw is { } failure) throw failure;
            return Task.FromResult(Serve);
        }

        public Task<SpeakingJoinEnqueue> EnqueueForSessionAsync(string sessionId, CancellationToken ct)
            => Task.FromResult(SpeakingJoinEnqueue.Disabled);

        public Task DeleteForSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct) => Task.CompletedTask;
    }
}
