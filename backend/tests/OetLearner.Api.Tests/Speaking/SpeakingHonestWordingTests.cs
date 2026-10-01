using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// A live voice role-play records no audio, so nothing the learner reads about its grading may point
/// at a recording: the failure reasons the results page shows verbatim are neutral, the recorder-only
/// transcription failures keep their recorder wording, and the grader is told there is no audio.
/// </summary>
public sealed class SpeakingHonestWordingTests
{
    // ── Failure reasons the results page shows as written ────────────

    [Fact]
    public async Task AFailedGrade_TellsTheLearnerAboutTheirRolePlay_NotARecording()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedFinishedAsync(rig);
        rig.Db.AiOperations.Add(new AiOperation
        {
            Id = Guid.NewGuid().ToString("N"),
            Module = "speaking",
            FeatureCode = AiFeatureCodes.SpeakingGrade,
            UserId = session.UserId,
            ResourceType = "speaking_session",
            ResourceId = session.SessionId,
            IdempotencyKey = $"speaking-grade:{session.SessionId}",
            State = AiOperationState.FailedTerminal,
            CreatedAt = rig.Clock.GetUtcNow(),
            UpdatedAt = rig.Clock.GetUtcNow(),
        });
        await rig.Db.SaveChangesAsync();

        var state = await BuildCanonical(rig.Db).GetStateAsync(session.SessionId, CancellationToken.None);

        Assert.Equal(SpeakingAssessmentState.Failed, state.AssessmentState);
        Assert.True(state.Retryable);
        Assert.Equal(
            "We couldn't finish grading your role-play. Try grading again. You won't be charged twice.",
            state.FailureReason);
        Assert.DoesNotContain("recording", state.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AV11TechnicalReview_TellsTheLearnerAboutTheirRolePlay_NotARecording()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedFinishedAsync(rig);
        rig.Db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
        {
            Id = $"v11-{Guid.NewGuid():N}",
            SpeakingSessionId = session.SessionId,
            AssessmentKind = "card",
            Status = SpeakingSimulationV11AssessmentStatus.TechnicalReview,
        });
        await rig.Db.SaveChangesAsync();

        var state = await BuildCanonical(rig.Db).GetStateAsync(session.SessionId, CancellationToken.None);

        Assert.Equal(SpeakingAssessmentState.Failed, state.AssessmentState);
        Assert.True(state.Retryable);
        Assert.Equal("Your role-play could not be scored automatically. Try grading again.", state.FailureReason);
    }

    [Theory]
    [InlineData("no_speech", false, "No speech could be detected in the recording.")]
    [InlineData("provider_error", true, "We couldn't transcribe your recording. Try grading again.")]
    public async Task RecorderTranscriptionFailures_KeepTheirRecordingWording(
        string reasonCode,
        bool retryable,
        string expectedReason)
    {
        // Only the recorder fallback transcribes anything, so these two are accurate and stay.
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedFinishedAsync(rig);
        rig.Db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = $"spt_{Guid.NewGuid():N}",
            SpeakingSessionId = session.SessionId,
            Provider = SpeakingTranscriptionPipeline.StateFailed,
            SegmentsJson = $"{{\"failure\":{{\"reasonCode\":\"{reasonCode}\",\"message\":\"failed\",\"retryable\":false}}}}",
            IsLatest = false,
            GeneratedAt = rig.Clock.GetUtcNow(),
        });
        await rig.Db.SaveChangesAsync();

        var state = await BuildCanonical(rig.Db).GetStateAsync(session.SessionId, CancellationToken.None);

        Assert.Equal(SpeakingAssessmentState.Failed, state.AssessmentState);
        Assert.Equal(retryable, state.Retryable);
        Assert.Equal(expectedReason, state.FailureReason);
    }

    // ── What the grader is told ──────────────────────────────────────

    [Theory]
    [InlineData("realtime-openai", true)]
    [InlineData("realtime-gemini", true)]
    [InlineData("openai-whisper", false)]
    [InlineData("live-roleplay", false)]
    public async Task TheGraderIsToldThereIsNoAudio_OnlyForALiveVoiceTranscript(string transcriptProvider, bool noteExpected)
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedFinishedAsync(rig);
        rig.Db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = $"spt_{Guid.NewGuid():N}",
            SpeakingSessionId = session.SessionId,
            Provider = transcriptProvider,
            SegmentsJson = """[{"speaker":"candidate","startMs":0,"endMs":4000,"text":"Hello, I am the doctor looking after you today."}]""",
            IsLatest = true,
            GeneratedAt = rig.Clock.GetUtcNow(),
        });
        await rig.Db.SaveChangesAsync();
        var gateway = new CapturingGateway();

        await new SpeakingAiAssessmentService(rig.Db, gateway, NullLogger<SpeakingAiAssessmentService>.Instance)
            .RunAssessmentAsync(session.SessionId, CancellationToken.None);

        var input = Assert.Single(gateway.Requests).UserInput!;
        var noteAt = input.IndexOf("no audio recording (transcript only)", StringComparison.Ordinal);
        var transcriptAt = input.IndexOf("---- TRANSCRIPT (latest revision) ----", StringComparison.Ordinal);
        Assert.True(transcriptAt > 0);
        if (noteExpected)
        {
            // In front of the transcript, and it says what the feedback must not do.
            Assert.InRange(noteAt, 1, transcriptAt - 1);
            Assert.Contains("must never tell the candidate to listen to or check a recording", input, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(-1, noteAt);
        }
        // Rubric, scoring rules and output schema are the same for every kind of input.
        Assert.StartsWith("You are an OET Speaking examiner scoring a single role-play session.", input, StringComparison.Ordinal);
        Assert.Contains("\"criterionScores\"", input, StringComparison.Ordinal);
        Assert.EndsWith("Now produce the strict JSON object specified above.", input.TrimEnd(), StringComparison.Ordinal);
        // The note changes neither the template identity nor the stored assessment's identity inputs.
        var row = await rig.Db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == session.SessionId);
        Assert.Equal(SpeakingCanonicalAssessmentService.PromptVersion, row.PromptTemplateId);
        Assert.Equal(SpeakingCanonicalAssessmentService.PromptVersion, Assert.Single(gateway.Requests).PromptTemplateId);
    }

    // ── Fixtures ─────────────────────────────────────────────────────

    private static Task<SeededLiveVoiceSession> SeedFinishedAsync(LiveVoiceRig rig)
        => LiveVoiceTestKit.SeedReadySessionAsync(rig.Db, rig.Clock.GetUtcNow(), SpeakingSessionState.Finished);

    /// <summary>Only <c>GetStateAsync</c> is exercised, which reads the database alone.</summary>
    private static SpeakingCanonicalAssessmentService BuildCanonical(LearnerDbContext db)
        => new(
            db,
            new SpeakingAiAssessmentService(db, null!, NullLogger<SpeakingAiAssessmentService>.Instance),
            v11: null!,
            TimeProvider.System,
            NullLogger<SpeakingCanonicalAssessmentService>.Instance);

    private static string ValidAssessmentJson() => JsonSerializer.Serialize(new
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

    /// <summary>Records what the assessor sends and answers with a valid nine-criterion reply.</summary>
    private sealed class CapturingGateway : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new()
        {
            SystemPrompt = "OET AI - Rulebook-Grounded System Prompt (test fixture)",
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
            return Task.FromResult(new AiGatewayResult
            {
                Completion = ValidAssessmentJson(),
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt!.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt!.Metadata.AppliedRuleIds,
            });
        }
    }
}
