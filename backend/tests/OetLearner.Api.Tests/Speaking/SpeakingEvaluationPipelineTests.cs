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
/// Pins the Attempt→Session bridge added 2026-09-09 after a full learner
/// acceptance pass found that the ONLY reachable learner UI for Speaking
/// practice (Selection → /speaking/roleplay/[id] → /speaking/task/[id])
/// submits through the older Attempt model, which has had no linked
/// SpeakingSession since the W7 canonical-assessment cutover
/// (commit ba5df294ad) — so every real submission through that flow was
/// permanently failing with "canonical_speaking_required" / "Submit
/// through the typed Speaking session flow", a flow with no learner-facing
/// button. See docs/speaking-attempt-session-bridge-2026-09-09/README.md.
///
/// These tests cover: (1) the pre-existing linked-session path now also
/// populates real score data (it previously left the Evaluation columns
/// the results page reads entirely blank), (2) the new bridge succeeds end
/// to end when a real (non-mock) ASR provider is available, (3) the bridge
/// fails honestly and retryably — never with fabricated/mock-graded
/// scores — when only the mock ASR provider is available (never trust
/// mock transcripts as real learner evidence), and (4) a transient ASR
/// failure degrades the same way instead of throwing.
/// </summary>
public sealed class SpeakingEvaluationPipelineTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;

    public Task InitializeAsync()
    {
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-eval-pipeline-{Guid.NewGuid():N}")
            .Options);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteEvaluationAsync_LinkedSession_PopulatesRealScoreData()
    {
        var (attempt, evaluation, job) = SeedAttemptAndCard(seedAudioObjectKey: false);
        var sessionId = SeedLinkedSessionWithTranscript(attempt.Id, "rpc-linked");

        var pipeline = new SpeakingEvaluationPipeline(
            _db, new FakeAiGateway(BuildValidAssessmentJson()), new SpeakingRuleEngine(new NoOpRulebookLoader()),
            NullLogger<SpeakingEvaluationPipeline>.Instance,
            sessionAssessor: BuildAssessor(BuildValidAssessmentJson()));

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedAttempt = await _db.Attempts.FirstAsync(x => x.Id == attempt.Id);
        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);

        Assert.Equal(AttemptState.Completed, reloadedAttempt.State);
        Assert.Equal(AsyncState.Completed, reloadedEval.State);
        Assert.Equal("canonical_speaking_assessment", reloadedEval.StatusReasonCode);
        // Before this fix, only State/StatusReasonCode/StatusMessage were set —
        // ScoreRange/CriterionScoresJson/ConfidenceBand stayed at their
        // "pending" seed defaults, so the results page would have rendered a
        // "Completed" evaluation with no actual score.
        Assert.NotEqual("pending", reloadedEval.ScoreRange);
        var criteria = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(reloadedEval.CriterionScoresJson, []);
        Assert.Equal(9, criteria.Count);
        Assert.Contains("speakingBand", reloadedAttempt.AnalysisJson);
        Assert.NotNull(sessionId); // documents which session was scored
    }

    [Fact]
    public async Task CompleteEvaluationAsync_AttemptWithoutSession_RealAsr_BridgesAndGrades()
    {
        var (attempt, evaluation, job) = SeedAttemptAndCard(seedAudioObjectKey: true);

        var pipeline = new SpeakingEvaluationPipeline(
            _db, new FakeAiGateway(BuildValidAssessmentJson()), new SpeakingRuleEngine(new NoOpRulebookLoader()),
            NullLogger<SpeakingEvaluationPipeline>.Instance,
            sessionAssessor: BuildAssessor(BuildValidAssessmentJson()),
            attemptTranscriptionProvider: new FakeTranscriptionProvider("openai-whisper", BuildRealTranscription));

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedAttempt = await _db.Attempts.FirstAsync(x => x.Id == attempt.Id);
        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);

        Assert.Equal(AttemptState.Completed, reloadedAttempt.State);
        Assert.Equal(AsyncState.Completed, reloadedEval.State);
        Assert.Equal("canonical_speaking_assessment", reloadedEval.StatusReasonCode);
        var criteria = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(reloadedEval.CriterionScoresJson, []);
        Assert.Equal(9, criteria.Count);

        // A real typed SpeakingSession + transcript must have been
        // synthesized and linked back to this attempt.
        var bridgedSession = await _db.SpeakingSessions.SingleAsync(s => s.AttemptId == attempt.Id);
        Assert.Equal(SpeakingSessionState.Finished, bridgedSession.State);
        var bridgedTranscript = await _db.SpeakingTranscripts.SingleAsync(t => t.SpeakingSessionId == bridgedSession.Id);
        Assert.Equal("openai-whisper", bridgedTranscript.Provider);
        Assert.True(bridgedTranscript.IsLatest);
    }

    [Fact]
    public async Task CompleteEvaluationAsync_AttemptWithoutSession_OnlyMockAsrAvailable_FailsHonestlyAndRetryably()
    {
        var (attempt, evaluation, job) = SeedAttemptAndCard(seedAudioObjectKey: true);

        var pipeline = new SpeakingEvaluationPipeline(
            _db, new FakeAiGateway(BuildValidAssessmentJson()), new SpeakingRuleEngine(new NoOpRulebookLoader()),
            NullLogger<SpeakingEvaluationPipeline>.Instance,
            sessionAssessor: BuildAssessor(BuildValidAssessmentJson()),
            // Mirrors Program.cs's real fallback when Whisper has no API key:
            // ISpeakingTranscriptionProvider resolves to the mock. The bridge
            // must refuse to grade off a fabricated transcript rather than
            // silently producing a fake-but-plausible-looking score.
            attemptTranscriptionProvider: new FakeTranscriptionProvider("mock", BuildRealTranscription));

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedAttempt = await _db.Attempts.FirstAsync(x => x.Id == attempt.Id);
        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);

        Assert.Equal(AttemptState.Submitted, reloadedAttempt.State);
        Assert.Equal(AsyncState.Failed, reloadedEval.State);
        Assert.Equal("speaking_transcription_unavailable", reloadedEval.StatusReasonCode);
        Assert.True(reloadedEval.Retryable);
        Assert.Equal(60_000, reloadedEval.RetryAfterMs);
        // The old message told learners to use a flow with no UI entry point.
        Assert.DoesNotContain("typed Speaking session flow", reloadedEval.StatusMessage);
        Assert.Empty(await _db.SpeakingSessions.Where(s => s.AttemptId == attempt.Id).ToListAsync());
    }

    [Fact]
    public async Task CompleteEvaluationAsync_AttemptWithoutSession_TranscriptionThrows_FailsRetryable_NoOrphanRows()
    {
        var (attempt, evaluation, job) = SeedAttemptAndCard(seedAudioObjectKey: true);

        var pipeline = new SpeakingEvaluationPipeline(
            _db, new FakeAiGateway(BuildValidAssessmentJson()), new SpeakingRuleEngine(new NoOpRulebookLoader()),
            NullLogger<SpeakingEvaluationPipeline>.Instance,
            sessionAssessor: BuildAssessor(BuildValidAssessmentJson()),
            attemptTranscriptionProvider: new FakeTranscriptionProvider(
                "openai-whisper", resultFactory: null, throwException: new InvalidOperationException("provider unreachable")));

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);
        Assert.Equal(AsyncState.Failed, reloadedEval.State);
        Assert.Equal("speaking_transcription_unavailable", reloadedEval.StatusReasonCode);
        Assert.True(reloadedEval.Retryable);
        // A failed transcription must not leave a half-written session behind.
        Assert.Empty(await _db.SpeakingSessions.Where(s => s.AttemptId == attempt.Id).ToListAsync());
        Assert.Empty(await _db.SpeakingTranscripts.ToListAsync());
    }

    [Fact]
    public async Task CompleteEvaluationAsync_LiveTutorSession_StillRoutesToHumanReview()
    {
        var (attempt, evaluation, job) = SeedAttemptAndCard(seedAudioObjectKey: false);
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = $"sps-{Guid.NewGuid():N}",
            UserId = attempt.UserId,
            RolePlayCardId = "rpc-linked",
            AttemptId = attempt.Id,
            Mode = SpeakingSessionMode.LiveTutor,
            State = SpeakingSessionState.Finished,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var pipeline = new SpeakingEvaluationPipeline(
            _db, new FakeAiGateway(BuildValidAssessmentJson()), new SpeakingRuleEngine(new NoOpRulebookLoader()),
            NullLogger<SpeakingEvaluationPipeline>.Instance,
            sessionAssessor: BuildAssessor(BuildValidAssessmentJson()));

        await pipeline.CompleteEvaluationAsync(job, default);
        await _db.SaveChangesAsync();

        var reloadedEval = await _db.Evaluations.FirstAsync(x => x.Id == evaluation.Id);
        Assert.Equal(AsyncState.Queued, reloadedEval.State);
        Assert.Equal("awaiting_human_review", reloadedEval.StatusReasonCode);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private (Attempt attempt, Evaluation evaluation, BackgroundJobItem job) SeedAttemptAndCard(bool seedAudioObjectKey)
    {
        var userId = $"u-{Guid.NewGuid():N}";
        var contentId = $"c-{Guid.NewGuid():N}";
        var attemptId = $"a-{Guid.NewGuid():N}";

        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.com",
            ActiveProfessionId = "nursing",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });

        _db.ContentItems.Add(new ContentItem
        {
            Id = contentId,
            ContentType = "role_play",
            SubtestCode = "speaking",
            ProfessionId = "nursing",
            Title = "Discharge advice role play",
            Difficulty = "standard",
            PublishedRevisionId = "rev-1",
            Status = ContentStatus.Published,
        });

        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = "rpc-linked",
            ContentItemId = contentId,
            ProfessionId = "nursing",
            ScenarioTitle = "Discharge advice role play",
            Setting = "Surgical ward",
            CandidateRole = "Nurse",
            Status = ContentStatus.Published,
        });

        var attempt = new Attempt
        {
            Id = attemptId,
            UserId = userId,
            ContentId = contentId,
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "self",
            State = AttemptState.Submitted,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            SubmittedAt = DateTimeOffset.UtcNow,
            AudioObjectKey = seedAudioObjectKey ? $"audio/{attemptId}/upload-{Guid.NewGuid():N}" : null,
        };
        _db.Attempts.Add(attempt);

        var evaluation = new Evaluation
        {
            Id = $"eval-{Guid.NewGuid():N}",
            AttemptId = attemptId,
            SubtestCode = "speaking",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ModelExplanationSafe = "pending",
            LearnerDisclaimer = "pending",
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        _db.Evaluations.Add(evaluation);

        var job = new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = JobType.SpeakingEvaluation,
            AttemptId = attemptId,
            State = AsyncState.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        _db.BackgroundJobs.Add(job);

        _db.SaveChanges();
        return (attempt, evaluation, job);
    }

    private string SeedLinkedSessionWithTranscript(string attemptId, string rolePlayCardId)
    {
        var sessionId = $"sps-{Guid.NewGuid():N}";
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = "irrelevant-for-this-test",
            RolePlayCardId = rolePlayCardId,
            AttemptId = attemptId,
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.Finished,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakingSessionId = sessionId,
            Provider = "live-roleplay",
            Language = "en",
            SegmentsJson = BuildRealTranscription().SegmentsJson,
            IsLatest = true,
            WordCount = 20,
            MeanConfidence = 0.9,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
        return sessionId;
    }

    private static SpeakingTranscriptionProviderResult BuildRealTranscription() => new()
    {
        Provider = "openai-whisper",
        Language = "en",
        SegmentsJson = """[{"speaker":"candidate","startMs":0,"endMs":4000,"text":"Hello Mrs Carter, I am the nurse looking after you today.","confidence":0.94,"words":[]}]""",
        WordCount = 11,
        MeanConfidence = 0.94,
    };

    private static string BuildValidAssessmentJson() => JsonSerializer.Serialize(new
    {
        criterionScores = new
        {
            intelligibility = new { score = 5, rationale = "Clear.", evidenceQuotes = Array.Empty<string>() },
            fluency = new { score = 5, rationale = "Smooth.", evidenceQuotes = Array.Empty<string>() },
            appropriateness = new { score = 5, rationale = "Warm.", evidenceQuotes = Array.Empty<string>() },
            grammarExpression = new { score = 5, rationale = "Accurate.", evidenceQuotes = Array.Empty<string>() },
            relationshipBuilding = new { score = 3, rationale = "Empathetic.", evidenceQuotes = Array.Empty<string>() },
            patientPerspective = new { score = 2, rationale = "Checked concerns.", evidenceQuotes = Array.Empty<string>() },
            structure = new { score = 2, rationale = "Signposted.", evidenceQuotes = Array.Empty<string>() },
            informationGathering = new { score = 2, rationale = "Open questions.", evidenceQuotes = Array.Empty<string>() },
            informationGiving = new { score = 2, rationale = "Checked understanding.", evidenceQuotes = Array.Empty<string>() },
        },
        readinessBand = "exam_ready",
        overallSummary = "Strong, organised communication.",
        confidenceBand = "high",
        strengths = new[] { "Clear opening" },
        improvements = Array.Empty<string>(),
        recommendedDrillKinds = Array.Empty<string>(),
    });

    private SpeakingAiAssessmentService BuildAssessor(string completion)
        => new(_db, new FakeAiGateway(completion), NullLogger<SpeakingAiAssessmentService>.Instance);

    // -------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------

    /// <summary>The pipeline's `ruleEngine` ctor param is dead weight on
    /// every path these tests exercise (grading routes entirely through
    /// SpeakingAiAssessmentService now) — stub it out so these tests don't
    /// depend on loading real rulebook JSON content from disk.</summary>
    private sealed class NoOpRulebookLoader : IRulebookLoader
    {
        public OetRulebook Load(RuleKind kind, ExamProfession profession) => throw new NotSupportedException();
        public IEnumerable<OetRulebook> All() => throw new NotSupportedException();
        public OetRule? FindRule(RuleKind kind, ExamProfession profession, string ruleId) => throw new NotSupportedException();
        public JsonElement GetAssessmentCriteria(RuleKind kind) => throw new NotSupportedException();
    }

    private sealed class FakeTranscriptionProvider(
        string providerCode,
        Func<SpeakingTranscriptionProviderResult>? resultFactory,
        Exception? throwException = null) : ISpeakingTranscriptionProvider
    {
        public string ProviderCode => providerCode;

        public Task<SpeakingTranscriptionProviderResult> TranscribeAsync(string mediaAssetUrl, string language, CancellationToken ct)
        {
            if (throwException is not null) throw throwException;
            return Task.FromResult(resultFactory!());
        }
    }

    private sealed class FakeAiGateway(string completion) : IAiGatewayService
    {
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
            => Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt!.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt!.Metadata.AppliedRuleIds,
            });
    }
}
