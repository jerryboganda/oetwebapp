using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;
using static OetLearner.Api.Tests.Speaking.JevSpeakingTestKit;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The classic Speaking grader with the Jev layer wired in. Pins the contract: flags off means zero
/// calls and an identical row; readiness runs before the grade and the cross-check after it; neither
/// can change a score, band or scaled score, only lower the stored confidence band to "low" and flag
/// tutor review; an unavailable, slow or crashing Jev changes nothing; mocks and live-tutor sessions
/// never reach Jev.
/// </summary>
public sealed class JevSpeakingGraderTests : IAsyncLifetime
{
    private const string UserId = "jev-speaking-learner";
    private const string SegmentsJson =
        """[{"speaker":"interlocutor","startMs":0,"endMs":3000,"text":"I have been getting chest pain when I walk."},{"speaker":"candidate","startMs":3000,"endMs":9000,"text":"Good morning, I am the doctor. Could you tell me more about the pain?"},{"speaker":"interlocutor","startMs":9000,"endMs":12000,"text":"It is a tight feeling in my chest."},{"speaker":"candidate","startMs":12000,"endMs":18000,"text":"I am sorry to hear that. Let me explain what the ECG showed."}]""";

    private static readonly (string Code, double Grader)[] GraderScores =
    [
        ("appropriateness", 5), ("grammarExpression", 5), ("relationshipBuilding", 3), ("patientPerspective", 2),
        ("structure", 2), ("informationGathering", 2), ("informationGiving", 2),
    ];

    private LearnerDbContext _db = default!;
    private readonly List<string> _log = new();

    public Task InitializeAsync()
    {
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"jev-speaking-grader-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = "rpc-jev",
            ContentItemId = "ci-jev",
            ProfessionId = "medicine",
            ScenarioTitle = "Chest pain follow-up",
            Setting = "General practice",
            CandidateRole = "Doctor",
            InterlocutorRole = "Patient",
            Background = "Follow-up after an ECG.",
            Task1 = "Explain the result",
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = 300,
            Status = ContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ── Flags off / no service ──────────────────────────────────────────────

    [Theory]
    [InlineData(true, false, false)]  // master on, both flags off
    [InlineData(false, true, true)]   // master off, both flags on
    public async Task FlagsOff_MakeNoJevCall_AndStoreNothingExtra(bool master, bool readiness, bool crosscheck)
    {
        var sessionId = await SeedSessionAsync();
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(CleanReadiness()), _log);
        var gateway = new GradeGateway(_log);

        var projection = await Assessor(gateway, jev, Flags(readiness, crosscheck, master)).RunAssessmentAsync(sessionId, default);

        Assert.Empty(jev.Calls);
        Assert.Equal(new[] { "grade" }, _log);
        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("high", row.ConfidenceBand);
        Assert.Equal("high", projection.ConfidenceBand);
        Assert.DoesNotContain(JevSpeakingAdvisor.AdvisoryKey, row.PerCriterionRationalesJson, StringComparison.Ordinal);
        Assert.Empty(await _db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task NoJudgmentService_MeansNoJevCall_EvenWithEveryFlagOn()
    {
        var sessionId = await SeedSessionAsync();

        // The corpus harness constructs the assessor with three arguments: judgments stays null.
        await Assessor(new GradeGateway(_log), jev: null, Flags(readiness: true, crosscheck: true))
            .RunAssessmentAsync(sessionId, default);

        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.DoesNotContain(JevSpeakingAdvisor.AdvisoryKey, row.PerCriterionRationalesJson, StringComparison.Ordinal);
    }

    // ── Readiness ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadinessFlag_FlagsTutorReview_ButTheGradeProceedsWithUnchangedNumbers()
    {
        var sessionId = await SeedSessionAsync();
        var jev = Responding(readiness: JevSpeakingTestKit.Readiness(onTask: 0.02, gibberish: 0.01, injection: 0.01));
        var gateway = new GradeGateway(_log);

        var projection = await Assessor(gateway, jev, Flags(readiness: true)).RunAssessmentAsync(sessionId, default);

        // Readiness runs strictly before the one grade call; nothing else is asked.
        Assert.Equal(new[] { AiFeatureCodes.JevSpeakingReadiness, "grade" }, _log);
        var request = Assert.Single(gateway.Requests);
        Assert.Equal(AiFeatureCodes.SpeakingGrade, request.FeatureCode);
        Assert.True(string.IsNullOrEmpty(request.Provider), "Jev must not reroute or pin the grade");

        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("high", row.ConfidenceBand);
        Assert.Equal(ExpectedScaled(), projection.EstimatedScaledScore);

        var audit = Assert.Single(await _db.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Equal(JevSpeakingAdvisor.ReviewFlaggedAction, audit.Action);
        Assert.Equal(sessionId, audit.ResourceId);
        Assert.Contains("off_task", audit.Details);
        Assert.Contains(JevSpeakingAdvisor.AdvisoryKey, row.PerCriterionRationalesJson, StringComparison.Ordinal);
        Assert.Contains("off_task", row.PerCriterionRationalesJson, StringComparison.Ordinal);
        // The advisory entry is inert for the learner projection.
        Assert.Equal(9, projection.CriterionScores.Count);
    }

    [Fact]
    public async Task ReadinessClean_StoresTheAdvisory_WithoutFlaggingAnything()
    {
        var sessionId = await SeedSessionAsync();
        var jev = Responding(readiness: CleanReadiness());

        await Assessor(new GradeGateway(_log), jev, Flags(readiness: true)).RunAssessmentAsync(sessionId, default);

        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("high", row.ConfidenceBand);
        Assert.Contains(JevSpeakingAdvisor.AdvisoryKey, row.PerCriterionRationalesJson, StringComparison.Ordinal);
        Assert.Empty(await _db.AuditEvents.AsNoTracking().ToListAsync());
    }

    // ── Cross-check ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CrosscheckDivergence_LowersTheConfidenceBand_AndEnqueuesReview_WithoutChangingANumber()
    {
        var sessionId = await SeedSessionAsync();
        var jev = Responding(crosscheck: CrosscheckAnswers(scoreOverrides: new() { ["relationshipBuilding"] = 0.5 }));

        var projection = await Assessor(new GradeGateway(_log), jev, Flags(crosscheck: true)).RunAssessmentAsync(sessionId, default);

        // The cross-check follows the grade; it is never inside it.
        Assert.Equal(new[] { "grade", AiFeatureCodes.JevSpeakingCrosscheck }, _log);
        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("low", row.ConfidenceBand);
        Assert.Equal("low", projection.ConfidenceBand);
        Assert.Equal(ExpectedScaled(), projection.EstimatedScaledScore);
        Assert.Equal(3, projection.CriterionScores["relationshipBuilding"].Score);

        var audit = Assert.Single(await _db.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Equal(JevSpeakingAdvisor.ReviewFlaggedAction, audit.Action);
        using var doc = JsonDocument.Parse(row.PerCriterionRationalesJson);
        var advisory = doc.RootElement.GetProperty(JevSpeakingAdvisor.AdvisoryKey).GetProperty("crosscheck");
        Assert.True(advisory.GetProperty("requiresReview").GetBoolean());
    }

    [Fact]
    public async Task ContradictedQuote_LowersTheConfidenceBand()
    {
        var sessionId = await SeedSessionAsync();
        var jev = Responding(crosscheck: CrosscheckAnswers(claimOverrides: new() { ["relationshipBuilding"] = "contradicted" }));

        await Assessor(new GradeGateway(_log), jev, Flags(crosscheck: true)).RunAssessmentAsync(sessionId, default);

        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("low", row.ConfidenceBand);
        Assert.Single(await _db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CrosscheckAgreement_KeepsTheBand_ButStillStoresTheAdvisory()
    {
        var sessionId = await SeedSessionAsync();
        var jev = Responding(crosscheck: CrosscheckAnswers());

        await Assessor(new GradeGateway(_log), jev, Flags(crosscheck: true)).RunAssessmentAsync(sessionId, default);

        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("high", row.ConfidenceBand);
        Assert.Contains(JevSpeakingAdvisor.AdvisoryKey, row.PerCriterionRationalesJson, StringComparison.Ordinal);
        Assert.Empty(await _db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task BothFlags_RunReadinessBeforeTheGrade_AndTheCrosscheckAfterIt_InTwoJevCalls()
    {
        var sessionId = await SeedSessionAsync();
        var jev = Responding(readiness: CleanReadiness(), crosscheck: CrosscheckAnswers());

        await Assessor(new GradeGateway(_log), jev, Flags(readiness: true, crosscheck: true)).RunAssessmentAsync(sessionId, default);

        Assert.Equal(
            new[] { AiFeatureCodes.JevSpeakingReadiness, "grade", AiFeatureCodes.JevSpeakingCrosscheck },
            _log);
        Assert.Equal(2, jev.Calls.Count);
        // Jev is text-only: the audio-bound criteria are never part of the cross-check.
        var crosscheckIds = jev.Calls[1].Request.Questions.Select(q => q.Id).ToList();
        Assert.DoesNotContain(JevSpeakingAdvisor.ScoreId("intelligibility"), crosscheckIds);
        Assert.DoesNotContain(JevSpeakingAdvisor.ScoreId("fluency"), crosscheckIds);
        AssertGradeUnchanged(await RowAsync(sessionId));
    }

    [Fact]
    public async Task ReusedAssessment_ReturnsBeforeAnyJevCall()
    {
        var sessionId = await SeedSessionAsync();
        var jev = Responding(readiness: CleanReadiness(), crosscheck: CrosscheckAnswers());
        var assessor = Assessor(new GradeGateway(_log), jev, Flags(readiness: true, crosscheck: true));

        await assessor.RunAssessmentAsync(sessionId, default);
        var callsAfterFirst = jev.Calls.Count;
        await assessor.RunAssessmentAsync(sessionId, default);

        Assert.Equal(callsAfterFirst, jev.Calls.Count);
        Assert.Equal(1, await _db.SpeakingAiAssessments.CountAsync(a => a.SpeakingSessionId == sessionId));
    }

    // ── Jev gives no judgment ───────────────────────────────────────────────

    [Theory]
    [InlineData("unavailable")]
    [InlineData("exception")]
    public async Task JevUnavailableOrCrashing_ChangesNothing(string outcome)
    {
        var sessionId = await SeedSessionAsync();
        var jev = new FakeJudgments((_, _, _) => outcome == "unavailable"
            ? Task.FromResult(JevJudgmentResult.Unavailable("jev_unavailable"))
            : throw new InvalidOperationException("boom"), _log);

        var projection = await Assessor(new GradeGateway(_log), jev, Flags(readiness: true, crosscheck: true))
            .RunAssessmentAsync(sessionId, default);

        // Jev was asked (before and after the grade) but nothing it did or failed to do is visible.
        Assert.Equal(
            new[] { AiFeatureCodes.JevSpeakingReadiness, "grade", AiFeatureCodes.JevSpeakingCrosscheck },
            _log);
        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("high", row.ConfidenceBand);
        Assert.Equal("high", projection.ConfidenceBand);
        Assert.DoesNotContain(JevSpeakingAdvisor.AdvisoryKey, row.PerCriterionRationalesJson, StringComparison.Ordinal);
        Assert.Empty(await _db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task JevSlow_TimesOutWithinTheTimeBox_AndTheGradeStillCompletesUnchanged()
    {
        var sessionId = await SeedSessionAsync();
        var jev = new FakeJudgments((_, _, ct) => Hang(ct), _log);
        var watch = Stopwatch.StartNew();

        await Assessor(new GradeGateway(_log), jev, Flags(readiness: true)).RunAssessmentAsync(sessionId, default);

        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
        Assert.Equal(new[] { AiFeatureCodes.JevSpeakingReadiness, "grade" }, _log);
        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("high", row.ConfidenceBand);
        Assert.DoesNotContain(JevSpeakingAdvisor.AdvisoryKey, row.PerCriterionRationalesJson, StringComparison.Ordinal);
        Assert.Empty(await _db.AuditEvents.AsNoTracking().ToListAsync());
    }

    // ── Sessions Jev never touches ──────────────────────────────────────────

    [Fact]
    public async Task MockSession_SkipsJevEntirely_AndStillGrades()
    {
        var sessionId = await SeedSessionAsync(s => s.MockSetId = "sms-1");
        var jev = Responding(readiness: JevSpeakingTestKit.Readiness(0.02, 0.9, 0.9), crosscheck: CrosscheckAnswers());
        var gateway = new GradeGateway(_log);

        await Assessor(gateway, jev, Flags(readiness: true, crosscheck: true)).RunAssessmentAsync(sessionId, default);

        Assert.Empty(jev.Calls);
        Assert.Equal(AiAssessmentContext.Mock, Assert.Single(gateway.Requests).AssessmentContext);
        var row = await RowAsync(sessionId);
        AssertGradeUnchanged(row);
        Assert.Equal("high", row.ConfidenceBand);
        Assert.Empty(await _db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task LiveTutorSession_IsHumanMarked_SoNeitherJevNorTheGatewayIsReached()
    {
        var sessionId = await SeedSessionAsync(s => s.Mode = SpeakingSessionMode.LiveTutor);
        var jev = Responding(readiness: CleanReadiness(), crosscheck: CrosscheckAnswers());
        var gateway = new GradeGateway(_log);

        var projection = await Assessor(gateway, jev, Flags(readiness: true, crosscheck: true)).RunAssessmentAsync(sessionId, default);

        Assert.Equal("human_examiner", projection.Provider);
        Assert.Empty(jev.Calls);
        Assert.Empty(gateway.Requests);
    }

    // ── Constructor shape (the corpus harness builds the assessor positionally with 3 args) ──

    [Theory]
    [InlineData(typeof(SpeakingAiAssessmentService))]
    [InlineData(typeof(SpeakingSimulationV11AssessmentService))]
    public void JevConstructorParameters_AreOptionalTrailingAndDefaultToNull(Type graderType)
    {
        var parameters = Assert.Single(graderType.GetConstructors()).GetParameters();

        Assert.Equal("judgments", parameters[^2].Name);
        Assert.Equal(typeof(ITypeSafeJudgmentService), parameters[^2].ParameterType);
        Assert.Equal("typeSafeOptions", parameters[^1].Name);
        Assert.Equal(typeof(IOptions<TypeSafeOptions>), parameters[^1].ParameterType);
        Assert.All(parameters.TakeLast(2), p =>
        {
            Assert.True(p.IsOptional, p.Name);
            Assert.Null(p.DefaultValue);
        });
    }

    // ── Builders ────────────────────────────────────────────────────────────

    private SpeakingAiAssessmentService Assessor(GradeGateway gateway, FakeJudgments? jev, TypeSafeOptions options) =>
        new(_db, gateway, NullLogger<SpeakingAiAssessmentService>.Instance,
            judgments: jev, typeSafeOptions: Microsoft.Extensions.Options.Options.Create(options));

    private async Task<string> SeedSessionAsync(Action<SpeakingSession>? tweak = null)
    {
        var id = $"sps-jev-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var session = new SpeakingSession
        {
            Id = id,
            UserId = UserId,
            RolePlayCardId = "rpc-jev",
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.Finished,
            EndedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        tweak?.Invoke(session);
        _db.SpeakingSessions.Add(session);
        _db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakingSessionId = id,
            Provider = "openai-whisper",
            Language = "en",
            SegmentsJson = SegmentsJson,
            IsLatest = true,
            WordCount = 30,
            MeanConfidence = 0.9,
            GeneratedAt = now,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private Task<SpeakingAiAssessment> RowAsync(string sessionId) =>
        _db.SpeakingAiAssessments.AsNoTracking().SingleAsync(a => a.SpeakingSessionId == sessionId);

    /// <summary>A judgment service answering by feature code; an unspecified verdict is "unavailable".</summary>
    private FakeJudgments Responding(JevJudgmentResult? readiness = null, JevJudgmentResult? crosscheck = null) =>
        new((_, call, _) => Task.FromResult(call.FeatureCode switch
        {
            AiFeatureCodes.JevSpeakingReadiness => readiness ?? JevJudgmentResult.Unavailable("unspecified"),
            AiFeatureCodes.JevSpeakingCrosscheck => crosscheck ?? JevJudgmentResult.Unavailable("unspecified"),
            _ => JevJudgmentResult.Unavailable("unexpected_feature"),
        }), _log);

    /// <summary>Cross-check answers that agree with the grader unless overridden.</summary>
    private static JevJudgmentResult CrosscheckAnswers(
        Dictionary<string, double>? scoreOverrides = null,
        Dictionary<string, string>? claimOverrides = null)
    {
        var answers = new List<(string Id, JevAnswer Answer)>();
        foreach (var (code, grader) in GraderScores)
        {
            var position = scoreOverrides is not null && scoreOverrides.TryGetValue(code, out var overridden) ? overridden : grader;
            var verdict = claimOverrides is not null && claimOverrides.TryGetValue(code, out var claim) ? claim : "supported";
            answers.Add(ScoreAnswer(JevSpeakingAdvisor.ScoreId(code), position));
            answers.Add(ChoiceAnswer(JevSpeakingAdvisor.ClaimId(code), verdict));
        }

        return Ok(answers.ToArray());
    }

    private static int ExpectedScaled() => OetScoring.SpeakingProjectedScaled(
        new OetScoring.SpeakingCriterionScores(5, 5, 5, 5, 3, 2, 2, 2, 2));

    /// <summary>The grader's numbers exactly as it returned them, whatever Jev did.</summary>
    private static void AssertGradeUnchanged(SpeakingAiAssessment row)
    {
        Assert.Equal(5, row.Intelligibility);
        Assert.Equal(5, row.Fluency);
        Assert.Equal(5, row.Appropriateness);
        Assert.Equal(5, row.GrammarExpression);
        Assert.Equal(3, row.RelationshipBuilding);
        Assert.Equal(2, row.PatientPerspective);
        Assert.Equal(2, row.Structure);
        Assert.Equal(2, row.InformationGathering);
        Assert.Equal(2, row.InformationGiving);
        Assert.Equal(ExpectedScaled(), row.EstimatedScaledScore);
        Assert.Equal(
            OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBandFromScaled(ExpectedScaled())),
            row.ReadinessBand);
        Assert.Equal("[]", row.RulebookFindingsJson);
    }

    private static string GradeJson() => JsonSerializer.Serialize(new
    {
        criterionScores = new
        {
            intelligibility = new { score = 5, rationale = "Clear.", evidenceQuotes = Array.Empty<string>() },
            fluency = new { score = 5, rationale = "Smooth.", evidenceQuotes = Array.Empty<string>() },
            appropriateness = new { score = 5, rationale = "Warm.", evidenceQuotes = Array.Empty<string>() },
            grammarExpression = new { score = 5, rationale = "Accurate.", evidenceQuotes = Array.Empty<string>() },
            relationshipBuilding = new { score = 3, rationale = "Empathetic.", evidenceQuotes = new[] { "I am sorry to hear that" } },
            patientPerspective = new { score = 2, rationale = "Checked concerns.", evidenceQuotes = Array.Empty<string>() },
            structure = new { score = 2, rationale = "Signposted.", evidenceQuotes = Array.Empty<string>() },
            informationGathering = new { score = 2, rationale = "Open questions.", evidenceQuotes = new[] { "Could you tell me more about the pain" } },
            informationGiving = new { score = 2, rationale = "Checked understanding.", evidenceQuotes = Array.Empty<string>() },
        },
        readinessBand = "exam_ready",
        overallSummary = "Strong, organised communication.",
        confidenceBand = "high",
        strengths = new[] { "Clear opening" },
        improvements = Array.Empty<string>(),
        recommendedDrillKinds = Array.Empty<string>(),
    });

    /// <summary>Returns the same nine-criterion grade every time and logs "grade" into the shared log.</summary>
    private sealed class GradeGateway(List<string> log) : IAiGatewayService
    {
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
            log.Add("grade");
            return Task.FromResult(new AiGatewayResult
            {
                Completion = GradeJson(),
                Metadata = request.Prompt!.Metadata,
                RulebookVersion = request.Prompt!.Metadata.RulebookVersion,
                AppliedRuleIds = request.Prompt!.Metadata.AppliedRuleIds,
                ResolvedProvider = "writing-claude-sub",
                ResolvedModel = "claude-opus-5-5",
            });
        }
    }
}
