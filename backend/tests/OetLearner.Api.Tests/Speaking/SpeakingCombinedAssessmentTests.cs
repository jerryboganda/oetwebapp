using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The Full Mock is assessed as ONE performance (owner spec 4 Oct 2026): the grader reads both role-plays together and
/// scores each criterion once, so the candidate gets one set of criterion scores, one reported score and one grade — never
/// an average of two card scores. These tests pin that: what the grader is (and is not) shown, how the audio evidence of
/// two cards becomes one, the durable operation behind it, and what the results show while it runs, fails or is retried.
/// </summary>
public sealed class SpeakingCombinedAssessmentTests : IAsyncLifetime
{
    private const string UserId = "combined-learner";
    private const string ExamId = "spx_combined";
    private const string SessionA = "sps_spx_combined_a";
    private const string SessionB = "sps_spx_combined_b";
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private LearnerDbContext _db = default!;

    public Task InitializeAsync()
    {
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-combined-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        _db.Users.Add(new LearnerUser
        {
            Id = UserId,
            DisplayName = "Combined Learner",
            Email = $"{UserId}@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = Now,
            LastActiveAt = Now,
        });
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = "rpc-combined",
            ContentItemId = "ci-combined",
            ProfessionId = "medicine",
            ScenarioTitle = "Asthma review",
            Setting = "General practice",
            CandidateRole = "Doctor",
            InterlocutorRole = "Patient",
            Background = "Review of poorly controlled asthma.",
            Task1 = "Explain the peak-flow result",
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = 300,
            Status = ContentStatus.Published,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _db.SpeakingExamSessions.Add(new SpeakingExamSession
        {
            Id = ExamId,
            UserId = UserId,
            CardAId = "rpc-combined",
            CardBId = "rpc-combined",
            SessionAId = SessionA,
            SessionBId = SessionB,
            State = SpeakingExamState.Completed,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        foreach (var (sessionId, slot, words) in new[]
                 {
                     (SessionA, "a", "sentinel alpha: let me explain your peak-flow result"),
                     (SessionB, "b", "sentinel bravo: I understand you are worried about the inhaler"),
                 })
        {
            _db.SpeakingSessions.Add(new SpeakingSession
            {
                Id = sessionId,
                UserId = UserId,
                RolePlayCardId = "rpc-combined",
                ExamSessionId = ExamId,
                ExamSlot = slot,
                Mode = SpeakingSessionMode.AiExam,
                State = SpeakingSessionState.Finished,
                EndedAt = Now,
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            _db.SpeakingTranscripts.Add(new SpeakingTranscript
            {
                Id = $"tx-{sessionId}",
                SpeakingSessionId = sessionId,
                Provider = "live-roleplay",
                Language = "en",
                SegmentsJson = JsonSerializer.Serialize(new[]
                {
                    new { speaker = "candidate", startMs = 0, endMs = 4000, text = words },
                }),
                IsLatest = true,
                WordCount = 10,
                MeanConfidence = 0.9,
                GeneratedAt = Now,
            });
        }

        _db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ── The combined run ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_ReadsBothTranscriptsTogether_StoresOneResultOnTheExam_AndNeverShowsTheGraderACardScore()
    {
        AddCardGrade(SessionA, summary: "CARD-A-SUMMARY-MARKER");
        AddCardGrade(SessionB, summary: "CARD-B-SUMMARY-MARKER");
        await _db.SaveChangesAsync();
        var gateway = new ReplyGateway();

        var projection = await Assessor(gateway).RunCombinedAssessmentAsync(ExamId, default);

        // One grader call over the whole test; neither card's result is in the prompt.
        var call = Assert.Single(gateway.Requests);
        Assert.Equal(AiFeatureCodes.SpeakingGrade, call.FeatureCode);
        Assert.Equal(SpeakingAiAssessmentService.CombinedPromptTemplateId, call.PromptTemplateId);
        Assert.Equal(UserId, call.UserId);
        var input = call.UserInput!;
        Assert.Contains("ONE COMPLETE OET SPEAKING TEST", input);
        Assert.Contains("ROLE-PLAY 1 OF 2", input);
        Assert.Contains("ROLE-PLAY 2 OF 2", input);
        Assert.Contains("TRANSCRIPT OF ROLE-PLAY 1", input);
        Assert.Contains("TRANSCRIPT OF ROLE-PLAY 2", input);
        Assert.Contains("sentinel alpha", input);
        Assert.Contains("sentinel bravo", input);
        Assert.DoesNotContain("CARD-A-SUMMARY-MARKER", input);
        Assert.DoesNotContain("CARD-B-SUMMARY-MARKER", input);

        // One set of nine criterion scores, one reported score, one grade, labelled provisional.
        var expected = OetScoring.SpeakingReportedScaled(new OetScoring.SpeakingCriterionScores(5, 5, 5, 5, 3, 2, 2, 2, 2));
        Assert.Equal(expected, projection.EstimatedScaledScore);
        Assert.Equal(OetScoring.OetGradeLetterFromScaled(expected), projection.Grade);
        Assert.Equal(OetScoring.SpeakingScoreLabelProvisional, projection.ScoreLabel);
        Assert.Equal(9, projection.CriterionScores.Count);
        Assert.Equal(5, projection.CriterionScores["intelligibility"].Score);

        // Stored on the exam, in the shape of a card row, and never as a third assessment row.
        var exam = await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == ExamId);
        Assert.False(string.IsNullOrWhiteSpace(exam.CombinedAssessmentJson));
        Assert.Equal(expected, exam.CombinedScaledSnapshot);
        Assert.Equal(2, await _db.SpeakingAiAssessments.CountAsync());
        var stored = SpeakingAiAssessmentService.ProjectCombinedJson(exam.CombinedAssessmentJson)!;
        Assert.Equal(expected, stored.EstimatedScaledScore);
        Assert.StartsWith(SpeakingAiAssessmentService.CombinedPromptTemplateId + "|", JsonDocument
            .Parse(exam.CombinedAssessmentJson!).RootElement.GetProperty("GraderVersion").GetString());

        // Idempotent: a stored result is returned, the grader is not called again.
        var again = await Assessor(gateway).RunCombinedAssessmentAsync(ExamId, default);
        Assert.Equal(expected, again.EstimatedScaledScore);
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task Run_BothCardsJudgedFromAudio_UsesOneCombinedIntelligibility_AndKeepsTheEvidenceOfBoth()
    {
        AddCardGrade(SessionA, intelligibility: 3, acoustic: AudioBlock("Pronounced 'inhaler' with a dropped ending."));
        AddCardGrade(SessionB, intelligibility: 4, acoustic: AudioBlock("Word stress on 'asthma' was off."));
        await _db.SaveChangesAsync();
        var gateway = new ReplyGateway(); // the grader's own text-only Intelligibility is 5

        var projection = await Assessor(gateway).RunCombinedAssessmentAsync(ExamId, default);

        // (3 + 4) judged from audio is one Intelligibility of 4, whatever the grader said from the words.
        Assert.Equal(4, projection.CriterionScores["intelligibility"].Score);
        var input = Assert.Single(gateway.Requests).UserInput!;
        Assert.Contains("ACOUSTIC EVIDENCE", input);
        Assert.Contains("4/6", input);
        var evidence = projection.IntelligibilityEvidence!;
        Assert.Equal("audio", evidence.Source);
        Assert.Equal(2, evidence.Observations.Count);
        Assert.StartsWith("Role-play 1: ", evidence.Observations[0].Issue);
        Assert.StartsWith("Role-play 2: ", evidence.Observations[1].Issue);
        var stored = await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == ExamId);
        Assert.Contains("audio-openai.v2:gpt-audio-1.5", stored.CombinedAssessmentJson);
    }

    [Fact]
    public async Task Run_OnlyOneCardJudgedFromAudio_IsLabelledTranscriptOnly_WithLowConfidence_AndTheGradersOwnEstimate()
    {
        AddCardGrade(SessionA, intelligibility: 3, acoustic: AudioBlock("Dropped endings."));
        AddCardGrade(SessionB, intelligibility: 5, acoustic: """{"source":"transcript_only","reason":"no_audio","confidence":"low"}""");
        await _db.SaveChangesAsync();
        var gateway = new ReplyGateway();

        var projection = await Assessor(gateway).RunCombinedAssessmentAsync(ExamId, default);

        // Half an audio judgement is never presented as audio for the whole test.
        Assert.Equal(5, projection.CriterionScores["intelligibility"].Score);
        Assert.Equal("low", projection.ConfidenceBand);
        var evidence = projection.IntelligibilityEvidence!;
        Assert.Equal("transcript_only", evidence.Source);
        Assert.Equal("partial_audio", evidence.Reason);
        Assert.Contains("only one of the two role-plays", evidence.ReasonText);
        Assert.Contains("No audio evidence could be used", Assert.Single(gateway.Requests).UserInput!);
    }

    [Fact]
    public async Task Run_WhenTheAudioStageNeverRan_GradesExactlyAsWithoutIt_AndSaysTranscriptOnly()
    {
        AddCardGrade(SessionA);
        AddCardGrade(SessionB);
        await _db.SaveChangesAsync();
        var gateway = new ReplyGateway();

        var projection = await Assessor(gateway).RunCombinedAssessmentAsync(ExamId, default);

        var input = Assert.Single(gateway.Requests).UserInput!;
        Assert.DoesNotContain("ACOUSTIC EVIDENCE", input);
        Assert.DoesNotContain("No audio evidence could be used", input);
        Assert.Equal("transcript_only", projection.IntelligibilityEvidence!.Source);
        var stored = await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == ExamId);
        Assert.Contains($"{SpeakingAiAssessmentService.CombinedPromptTemplateId}|{OetScoring.SpeakingMappingVersion}|audio-none", stored.CombinedAssessmentJson);
    }

    [Fact]
    public async Task Run_WhileACardHasNoGradeYet_IsARetryableConflict_AndCallsNobody()
    {
        AddCardGrade(SessionA);
        await _db.SaveChangesAsync();
        var gateway = new ReplyGateway();

        var failure = await Assert.ThrowsAsync<ApiException>(() => Assessor(gateway).RunCombinedAssessmentAsync(ExamId, default));

        Assert.Equal(SpeakingAiAssessmentService.ExamCardsNotGradedCode, failure.ErrorCode);
        Assert.Empty(gateway.Requests);
    }

    [Fact]
    public async Task Run_ATutorExam_IsNotGradedByAi()
    {
        var exam = await _db.SpeakingExamSessions.SingleAsync(e => e.Id == ExamId);
        exam.Mode = SpeakingExamMode.LiveTutor;
        await _db.SaveChangesAsync();

        var failure = await Assert.ThrowsAsync<ApiException>(() => Assessor(new ReplyGateway()).RunCombinedAssessmentAsync(ExamId, default));

        Assert.Equal("speaking_exam_not_gradable", failure.ErrorCode);
    }

    // ── The durable operation ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CardGrades_QueueTheCombinedOperation_OnlyOnceBothCardsAreGraded_AndOnlyOnce()
    {
        var gateway = new ReplyGateway();
        var canonical = Canonical(gateway);

        await canonical.AssessNowAsync(SessionA, default);
        Assert.False(await _db.AiOperations.AnyAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType));

        await canonical.AssessNowAsync(SessionB, default);
        var operation = await _db.AiOperations.AsNoTracking()
            .SingleAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType);
        Assert.Equal(ExamId, operation.ResourceId);
        Assert.Equal(AiFeatureCodes.SpeakingGrade, operation.FeatureCode);
        Assert.Equal($"speaking.assess.exam:{ExamId}", operation.IdempotencyKey);
        Assert.Equal(AiOperationState.Queued, operation.State);

        // Grading a card again (a retry, a poll) never queues a second one.
        await canonical.AssessNowAsync(SessionB, default);
        Assert.Equal(1, await _db.AiOperations.CountAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType));
    }

    [Fact]
    public async Task TheWorker_RunsTheExamOperation_StoresTheResult_AndMarksItCompleted()
    {
        var gateway = new ReplyGateway();
        var canonical = Canonical(gateway);
        await canonical.AssessNowAsync(SessionA, default);
        await canonical.AssessNowAsync(SessionB, default);
        var operation = await _db.AiOperations.AsNoTracking()
            .SingleAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType);
        var callsBefore = gateway.Requests.Count;

        await canonical.ExecuteQueuedAsync(operation.Id, default);

        Assert.Equal(callsBefore + 1, gateway.Requests.Count);
        Assert.Equal(SpeakingCanonicalAssessmentService.PromptVersion + "-combined", gateway.Requests[^1].PromptTemplateId);
        var done = await _db.AiOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(AiOperationState.Completed, done.State);
        Assert.False(string.IsNullOrWhiteSpace((await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == ExamId)).CombinedAssessmentJson));
    }

    [Fact]
    public async Task AFailedCombinedGrade_ShowsAsFailed_ThenRetryRequeuesIt_AndTheNextRunSucceeds()
    {
        var gateway = new ReplyGateway();
        var canonical = Canonical(gateway);
        await canonical.AssessNowAsync(SessionA, default);
        await canonical.AssessNowAsync(SessionB, default);
        var operation = await _db.AiOperations.AsNoTracking()
            .SingleAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType);

        gateway.Fail = true;
        await canonical.ExecuteQueuedAsync(operation.Id, default); // the worker never sees the exception
        Assert.Equal(AiOperationState.FailedTerminal, (await _db.AiOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)).State);
        Assert.Equal(SpeakingExamCombinedStates.Failed, await canonical.GetExamCombinedStateAsync(ExamId, default));
        Assert.Null((await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == ExamId)).CombinedAssessmentJson);

        // "Try again" re-queues exactly the failed operation: no new operation, no charge.
        await canonical.RetryExamCombinedAsync(ExamId, default);
        Assert.Equal(AiOperationState.Queued, (await _db.AiOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)).State);
        Assert.Equal(SpeakingExamCombinedStates.Pending, await canonical.GetExamCombinedStateAsync(ExamId, default));
        await canonical.RetryExamCombinedAsync(ExamId, default); // a second tap changes nothing
        Assert.Equal(1, await _db.AiOperations.CountAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType));

        gateway.Fail = false;
        await canonical.ExecuteQueuedAsync(operation.Id, default);
        Assert.Equal(AiOperationState.Completed, (await _db.AiOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)).State);
        Assert.False(string.IsNullOrWhiteSpace((await _db.SpeakingExamSessions.AsNoTracking().SingleAsync(e => e.Id == ExamId)).CombinedAssessmentJson));
    }

    // ── What the learner sees ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Results_AreOneCombinedResult_NotAnAverage_AndStayPendingUntilItIsThere()
    {
        var gateway = new ReplyGateway();
        var canonical = Canonical(gateway);
        var exams = Exams(gateway, canonical);
        await canonical.AssessNowAsync(SessionA, default);

        // One card graded: nothing to show yet, and nothing queued.
        var half = await exams.GetResultsAsync(UserId, ExamId, default);
        Assert.Equal("pending", half.OverallStatus);
        Assert.Null(half.CombinedState);
        Assert.False(await _db.AiOperations.AnyAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType));

        // Both graded: per-card breakdown is there, the overall number is not (it is being judged as one test).
        await canonical.AssessNowAsync(SessionB, default);
        var waiting = await exams.GetResultsAsync(UserId, ExamId, default);
        Assert.All(waiting.Cards, card => Assert.Equal("scored", card.Status));
        Assert.Equal("pending", waiting.OverallStatus);
        Assert.Equal(SpeakingExamCombinedStates.Pending, waiting.CombinedState);
        Assert.Null(waiting.CombinedScaledScore);
        Assert.Null(waiting.Grade);
        Assert.Null(waiting.CombinedAssessment);

        var operation = await _db.AiOperations.AsNoTracking()
            .SingleAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType);
        await canonical.ExecuteQueuedAsync(operation.Id, default);

        var ready = await exams.GetResultsAsync(UserId, ExamId, default);
        var expected = OetScoring.SpeakingReportedScaled(new OetScoring.SpeakingCriterionScores(5, 5, 5, 5, 3, 2, 2, 2, 2));
        Assert.Equal("scored", ready.OverallStatus);
        Assert.Equal(SpeakingExamCombinedStates.Ready, ready.CombinedState);
        Assert.Equal(expected, ready.CombinedScaledScore);
        Assert.Equal(OetScoring.OetGradeLetterFromScaled(expected), ready.Grade);
        Assert.Equal(OetScoring.SpeakingScoreLabelProvisional, ready.ScoreLabel);
        Assert.NotNull(ready.CombinedAssessment);
        Assert.Equal(expected, ready.CombinedAssessment!.EstimatedScaledScore);
        Assert.Equal(9, ready.CombinedAssessment.CriterionScores.Count);
    }

    [Fact]
    public async Task Results_ShowAFailedCombinedGrade_SoThePageCanOfferTryAgain()
    {
        var gateway = new ReplyGateway();
        var canonical = Canonical(gateway);
        var exams = Exams(gateway, canonical);
        await canonical.AssessNowAsync(SessionA, default);
        await canonical.AssessNowAsync(SessionB, default);
        var operation = await _db.AiOperations.AsNoTracking()
            .SingleAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType);
        gateway.Fail = true;
        await canonical.ExecuteQueuedAsync(operation.Id, default);

        var failed = await exams.GetResultsAsync(UserId, ExamId, default);
        Assert.Equal("pending", failed.OverallStatus);
        Assert.Equal(SpeakingExamCombinedStates.Failed, failed.CombinedState);
        Assert.Null(failed.CombinedScaledScore);

        gateway.Fail = false;
        Assert.Equal(SpeakingExamCombinedStates.Pending, await exams.RetryCombinedAssessmentAsync(UserId, ExamId, default));
        await canonical.ExecuteQueuedAsync(operation.Id, default);
        Assert.Equal("scored", (await exams.GetResultsAsync(UserId, ExamId, default)).OverallStatus);
    }

    [Fact]
    public async Task Results_AnExamFinishedBeforeTheCombinedGradeExisted_KeepsItsAveragedNumber()
    {
        AddCardGrade(SessionA);
        AddCardGrade(SessionB);
        var exam = await _db.SpeakingExamSessions.SingleAsync(e => e.Id == ExamId);
        exam.CombinedScaledSnapshot = 380;
        exam.ReadinessBandSnapshot = "exam_ready";
        await _db.SaveChangesAsync();

        var results = await Exams(new ReplyGateway(), Canonical(new ReplyGateway())).GetResultsAsync(UserId, ExamId, default);

        Assert.Equal("scored", results.OverallStatus);
        Assert.Equal(SpeakingExamCombinedStates.Legacy, results.CombinedState);
        Assert.Equal(380, results.CombinedScaledScore);
        Assert.Equal("B", results.Grade);
        Assert.Null(results.CombinedAssessment);
        Assert.False(await _db.AiOperations.AnyAsync(o => o.ResourceType == SpeakingCanonicalAssessmentService.ExamResourceType));
    }

    [Fact]
    public async Task RetryCombined_IsOnlyForTheExamsOwner()
    {
        var exams = Exams(new ReplyGateway(), Canonical(new ReplyGateway()));

        var failure = await Assert.ThrowsAsync<ApiException>(() => exams.RetryCombinedAssessmentAsync("someone-else", ExamId, default));

        Assert.Equal("speaking_exam_not_found", failure.ErrorCode);
    }

    // ── Pure rules ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3, 4, 4)]
    [InlineData(4, 4, 4)]
    [InlineData(0, 1, 1)]
    [InlineData(6, 5, 6)]
    [InlineData(2, 2, 2)]
    [InlineData(9, -3, 3)]
    public void CombinedIntelligibility_IsTheMeanOfTheTwoJudgements_AHalfRoundingUp(int one, int two, int expected)
        => Assert.Equal(expected, OetScoring.SpeakingCombinedIntelligibility(one, two));

    [Theory]
    [InlineData("role_play", "role_play", "role_play")]
    [InlineData("breaking_bad_news", "role_play", "breaking_bad_news")]
    [InlineData("role_play", "follow_up", "follow_up")]
    [InlineData("follow_up", "follow_up", "follow_up")]
    [InlineData("breaking_bad_news", "follow_up", "role_play")]
    public void CombinedCardToken_LetsASpecialisedCardTypeWin_OverTheGenericOne(string first, string second, string expected)
        => Assert.Equal(expected, SpeakingAiAssessmentService.CombinedCardToken(first, second));

    [Fact]
    public void CombineAudioEvidence_NeverPresentsHalfAnAudioJudgementAsAudio()
    {
        var audio = Judged(3, "high");

        Assert.Null(SpeakingAiAssessmentService.CombineAudioEvidence(null, null));
        Assert.Equal("partial_audio", SpeakingAiAssessmentService.CombineAudioEvidence(audio, SpeakingAudioEvidence.Unavailable("timeout"))!.Reason);
        Assert.Equal("partial_audio", SpeakingAiAssessmentService.CombineAudioEvidence(null, audio)!.Reason);
        Assert.Equal("no_audio", SpeakingAiAssessmentService.CombineAudioEvidence(
            SpeakingAudioEvidence.Unavailable("no_audio"), SpeakingAudioEvidence.Unavailable("timeout"))!.Reason);

        var both = SpeakingAiAssessmentService.CombineAudioEvidence(audio, Judged(4, "low"))!;
        Assert.True(both.IsAudio);
        Assert.Equal(4, both.IntelligibilityScore);
        Assert.Equal("low", both.Confidence); // the weaker of the two
        Assert.Equal(2, both.ClipCount);
        Assert.StartsWith("Role-play 1: ", both.IntelligibilityRationale);
        Assert.Contains("Role-play 2: ", both.IntelligibilityRationale);
    }

    // ── Builders ───────────────────────────────────────────────────────────────────────────

    private SpeakingAiAssessmentService Assessor(IAiGatewayService gateway)
        => new(_db, gateway, NullLogger<SpeakingAiAssessmentService>.Instance);

    private SpeakingCanonicalAssessmentService Canonical(IAiGatewayService gateway)
        => new(_db, Assessor(gateway), v11: null!, TimeProvider.System, NullLogger<SpeakingCanonicalAssessmentService>.Instance);

    private SpeakingExamService Exams(IAiGatewayService gateway, SpeakingCanonicalAssessmentService canonical)
        => new(_db, Assessor(gateway), NullLogger<SpeakingExamService>.Instance, canonical: canonical);

    private static SpeakingAudioEvidence Judged(int score, string confidence) => new()
    {
        Status = SpeakingAudioEvidence.StatusAudio,
        IntelligibilityScore = score,
        IntelligibilityRationale = $"Judged {score}.",
        Confidence = confidence,
        Model = "gpt-audio-1.5",
        ClipCount = 1,
        DurationMs = 10_000,
    };

    /// <summary>The <c>_acoustic</c> block a card grade stores when its Intelligibility was judged from audio.</summary>
    private static string AudioBlock(string issue) => JsonSerializer.Serialize(new
    {
        source = "audio",
        model = "gpt-audio-1.5",
        confidence = "high",
        audioQuality = "good",
        patientVoiceBleed = false,
        clips = 3,
        durationMs = 40_000,
        observations = new[] { new { clip = 1, approxSecond = 12, issue, example = "inha-uh" } },
    });

    private void AddCardGrade(string sessionId, int intelligibility = 5, string? acoustic = null, string summary = "Clear and kind.")
    {
        var rationales = new Dictionary<string, object?>
        {
            ["intelligibility"] = new { rationale = "Mostly clear.", evidenceQuotes = Array.Empty<string>() },
        };
        if (acoustic is not null) rationales["_acoustic"] = JsonDocument.Parse(acoustic).RootElement.Clone();
        _db.SpeakingAiAssessments.Add(new SpeakingAiAssessment
        {
            Id = $"spa_{sessionId}",
            SpeakingSessionId = sessionId,
            TranscriptId = $"tx-{sessionId}",
            Provider = "ai_gateway",
            ModelId = "gateway-default",
            PromptTemplateId = SpeakingAiAssessmentService.PromptTemplateId,
            GraderVersion = SpeakingAiAssessmentService.GraderVersion,
            Intelligibility = intelligibility,
            Fluency = 4,
            Appropriateness = 4,
            GrammarExpression = 4,
            RelationshipBuilding = 2,
            PatientPerspective = 2,
            Structure = 2,
            InformationGathering = 2,
            InformationGiving = 2,
            EstimatedScaledScore = 300,
            ReadinessBand = "borderline",
            PerCriterionRationalesJson = JsonSerializer.Serialize(rationales),
            OverallSummary = summary,
            ConfidenceBand = "medium",
            GeneratedAt = Now,
            IsAdvisory = true,
        });
    }

    /// <summary>Answers every grade call with the same valid nine-criterion reply (raw 31/39); records what it was sent.</summary>
    private sealed class ReplyGateway : IAiGatewayService
    {
        public bool Fail { get; set; }
        public List<AiGatewayRequest> Requests { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new()
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

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (Fail) throw new InvalidOperationException("provider unreachable");
            return Task.FromResult(new AiGatewayResult
            {
                Completion = Reply,
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt.Metadata.AppliedRuleIds,
                ResolvedProvider = "writing-claude-sub",
                ResolvedModel = "claude-opus-5-5",
            });
        }

        private static string Crit(int score, string rationale) => $$"""{"score":{{score}},"rationale":"{{rationale}}","evidenceQuotes":[]}""";

        private static readonly string Reply = $$"""
            {
              "criterionScores": {
                "intelligibility": {{Crit(5, "Clear.")}},
                "fluency": {{Crit(5, "Smooth.")}},
                "appropriateness": {{Crit(5, "Warm.")}},
                "grammarExpression": {{Crit(5, "Accurate.")}},
                "relationshipBuilding": {{Crit(3, "Empathetic.")}},
                "patientPerspective": {{Crit(2, "Checked concerns.")}},
                "structure": {{Crit(2, "Signposted.")}},
                "informationGathering": {{Crit(2, "Open questions.")}},
                "informationGiving": {{Crit(2, "Checked understanding.")}}
              },
              "overallSummary": "Strong, organised communication across both role-plays.",
              "confidenceBand": "high",
              "strengths": [ { "criterion": "structure", "text": "Clear signposting.", "quote": "", "action": "" },
                             { "criterion": "relationshipBuilding", "text": "Warm tone.", "quote": "", "action": "" } ],
              "priorityWeaknesses": [ { "criterion": "informationGiving", "text": "Some jargon.", "quote": "", "action": "Use plain words." },
                                      { "criterion": "fluency", "text": "A few long pauses.", "quote": "", "action": "Plan the next question." } ],
              "drills": [ { "title": "Plain words", "criterion": "informationGiving", "weakPoint": "Jargon.", "practise": "Explain without jargon.", "example": "" },
                          { "title": "Pauses", "criterion": "fluency", "weakPoint": "Pauses.", "practise": "Bridge phrases.", "example": "" } ]
            }
            """;
    }
}
