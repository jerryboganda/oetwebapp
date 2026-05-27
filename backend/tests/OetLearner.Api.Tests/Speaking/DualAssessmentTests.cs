using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

// Phase 4 (B.4 / E) of the OET Speaking module plan.
//
// These tests PIN the dual scoring invariant — the most important
// contract in the module:
//
//   * TutorAssessment writes must NEVER mutate SpeakingAiAssessment rows.
//   * AiAssessment writes must NEVER mutate SpeakingTutorAssessment rows.
//   * The learner-facing dual GET must surface both columns when both
//     tracks have produced a row.
//   * Divergence is computed via sum-of-abs across the nine criteria:
//     ≤4 → close, ≤10 → moderate, otherwise wide.
//   * The submitted scaled score MUST equal OetScoring.SpeakingProjectedScaled
//     applied to the tutor's nine criterion scores — no local re-implementation.
public sealed class DualAssessmentTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private TutorAssessmentService _svc = default!;

    public Task InitializeAsync()
    {
        var dbName = $"speaking-dual-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new LearnerDbContext(options);
        _svc = new TutorAssessmentService(
            _db,
            NullLogger<TutorAssessmentService>.Instance,
            TimeProvider.System);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    // ── Invariant #1: tutor writes do not mutate AI rows ─────────────────

    [Fact]
    public async Task TutorAssessment_DoesNotMutateAiAssessment()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-1");
        var aiId = await SeedAiAssessmentAsync(
            sessionId,
            new[] { 5, 5, 5, 5 },     // linguistic
            new[] { 2, 2, 2, 2, 2 }); // clinical
        await SeedTimestampedCommentAsync(sessionId, "tutor-1");

        var aiBefore = await _db.SpeakingAiAssessments.AsNoTracking()
            .FirstAsync(a => a.Id == aiId);

        var draftId = await _svc.CreateDraftAsync(
            "tutor-1",
            sessionId,
            new TutorAssessmentDraftRequest(
                Intelligibility: 6, Fluency: 6, Appropriateness: 6, GrammarExpression: 6,
                RelationshipBuilding: 3, PatientPerspective: 3, Structure: 3,
                InformationGathering: 3, InformationGiving: 3,
                OverallFeedbackMarkdown: "Excellent role-play.",
                Strengths: new[] { "Clear lay language" },
                Improvements: new[] { "Slow pace slightly" },
                RecommendedDrills: null,
                RecommendedRulebookEntries: null),
            CancellationToken.None);

        await _svc.SubmitAsync(
            "tutor-1",
            sessionId,
            draftId,
            new TutorAssessmentSubmitRequest(
                Intelligibility: 6, Fluency: 6, Appropriateness: 6, GrammarExpression: 6,
                RelationshipBuilding: 3, PatientPerspective: 3, Structure: 3,
                InformationGathering: 3, InformationGiving: 3,
                OverallFeedbackMarkdown: null,
                Strengths: null, Improvements: null,
                RecommendedDrills: null, RecommendedRulebookEntries: null),
            CancellationToken.None);

        var aiAfter = await _db.SpeakingAiAssessments.AsNoTracking()
            .FirstAsync(a => a.Id == aiId);

        Assert.Equal(aiBefore.Intelligibility,        aiAfter.Intelligibility);
        Assert.Equal(aiBefore.Fluency,                aiAfter.Fluency);
        Assert.Equal(aiBefore.Appropriateness,        aiAfter.Appropriateness);
        Assert.Equal(aiBefore.GrammarExpression,      aiAfter.GrammarExpression);
        Assert.Equal(aiBefore.RelationshipBuilding,   aiAfter.RelationshipBuilding);
        Assert.Equal(aiBefore.PatientPerspective,     aiAfter.PatientPerspective);
        Assert.Equal(aiBefore.Structure,              aiAfter.Structure);
        Assert.Equal(aiBefore.InformationGathering,   aiAfter.InformationGathering);
        Assert.Equal(aiBefore.InformationGiving,      aiAfter.InformationGiving);
        Assert.Equal(aiBefore.EstimatedScaledScore,   aiAfter.EstimatedScaledScore);
        Assert.Equal(aiBefore.ReadinessBand,          aiAfter.ReadinessBand);
        Assert.Equal(aiBefore.OverallSummary,         aiAfter.OverallSummary);
        Assert.Equal(aiBefore.ConfidenceBand,         aiAfter.ConfidenceBand);
        Assert.Equal(aiBefore.GeneratedAt,            aiAfter.GeneratedAt);
        Assert.Equal(aiBefore.RulebookFindingsJson,   aiAfter.RulebookFindingsJson);
        Assert.True(aiAfter.IsAdvisory);
    }

    // ── Invariant #2: AI writes do not mutate tutor rows ─────────────────

    [Fact]
    public async Task AiAssessment_DoesNotMutateTutorAssessment()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-2");
        await SeedTimestampedCommentAsync(sessionId, "tutor-2");

        // Seed a finalised tutor assessment first.
        var draftId = await _svc.CreateDraftAsync(
            "tutor-2",
            sessionId,
            new TutorAssessmentDraftRequest(
                4, 4, 4, 4, 2, 2, 2, 2, 2,
                "Tutor narrative.", null, null, null, null),
            CancellationToken.None);
        var submitted = await _svc.SubmitAsync(
            "tutor-2",
            sessionId,
            draftId,
            new TutorAssessmentSubmitRequest(
                4, 4, 4, 4, 2, 2, 2, 2, 2,
                null, null, null, null, null),
            CancellationToken.None);

        var tutorBefore = await _db.SpeakingTutorAssessments.AsNoTracking()
            .FirstAsync(t => t.Id == submitted.AssessmentId);

        // Now write a brand-new AI assessment.
        await SeedAiAssessmentAsync(
            sessionId,
            new[] { 6, 6, 6, 6 },
            new[] { 3, 3, 3, 3, 3 });

        var tutorAfter = await _db.SpeakingTutorAssessments.AsNoTracking()
            .FirstAsync(t => t.Id == submitted.AssessmentId);

        Assert.Equal(tutorBefore.Intelligibility,      tutorAfter.Intelligibility);
        Assert.Equal(tutorBefore.Fluency,              tutorAfter.Fluency);
        Assert.Equal(tutorBefore.Appropriateness,      tutorAfter.Appropriateness);
        Assert.Equal(tutorBefore.GrammarExpression,    tutorAfter.GrammarExpression);
        Assert.Equal(tutorBefore.RelationshipBuilding, tutorAfter.RelationshipBuilding);
        Assert.Equal(tutorBefore.PatientPerspective,   tutorAfter.PatientPerspective);
        Assert.Equal(tutorBefore.Structure,            tutorAfter.Structure);
        Assert.Equal(tutorBefore.InformationGathering, tutorAfter.InformationGathering);
        Assert.Equal(tutorBefore.InformationGiving,    tutorAfter.InformationGiving);
        Assert.Equal(tutorBefore.EstimatedScaledScore, tutorAfter.EstimatedScaledScore);
        Assert.Equal(tutorBefore.ReadinessBand,        tutorAfter.ReadinessBand);
        Assert.Equal(tutorBefore.OverallFeedbackMarkdown, tutorAfter.OverallFeedbackMarkdown);
        Assert.Equal(tutorBefore.IsFinal,              tutorAfter.IsFinal);
        Assert.Equal(tutorBefore.SubmittedAt,          tutorAfter.SubmittedAt);
    }

    // ── Dual GET surfaces both columns ───────────────────────────────────

    [Fact]
    public async Task LearnerSeesBothAssessments()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-3");
        await SeedAiAssessmentAsync(sessionId, new[] { 5, 5, 5, 5 }, new[] { 2, 2, 2, 2, 2 });
        await SeedTimestampedCommentAsync(sessionId, "tutor-3");

        var draftId = await _svc.CreateDraftAsync(
            "tutor-3",
            sessionId,
            new TutorAssessmentDraftRequest(
                4, 5, 5, 4, 2, 3, 2, 2, 3,
                "Solid effort.", null, null, null, null),
            CancellationToken.None);
        await _svc.SubmitAsync(
            "tutor-3",
            sessionId,
            draftId,
            new TutorAssessmentSubmitRequest(
                4, 5, 5, 4, 2, 3, 2, 2, 3,
                null, null, null, null, null),
            CancellationToken.None);

        var dual = await _svc.GetDualAssessmentForLearnerAsync("learner-3", sessionId, CancellationToken.None);

        Assert.Equal(sessionId, dual.SessionId);
        Assert.NotNull(dual.Ai);
        Assert.NotNull(dual.Tutor);
        Assert.True(dual.Tutor!.IsFinal);
        Assert.Single(dual.TutorHistory);
        Assert.NotNull(dual.Divergence);
    }

    // ── Divergence agreement bands ───────────────────────────────────────

    [Theory]
    [InlineData(2,  "close")]
    [InlineData(4,  "close")]
    [InlineData(7,  "moderate")]
    [InlineData(10, "moderate")]
    [InlineData(15, "wide")]
    public void DivergenceCalculation_ComputesAgreementBand(int sumOfAbs, string expectedBand)
    {
        var (ai, tutor) = BuildPairWithSumOfAbs(sumOfAbs);
        var divergence = TutorAssessmentService.ComputeDivergence(ai, tutor);
        Assert.Equal(expectedBand, divergence.AgreementBand);
        var actualSum = divergence.PerCriterion.Values.Sum();
        Assert.Equal(sumOfAbs, actualSum);
    }

    // ── Scaled score comes from OetScoring ───────────────────────────────

    [Fact]
    public async Task Submit_RecomputesScaledScoreViaOetScoring()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-5");
        await SeedTimestampedCommentAsync(sessionId, "tutor-5");

        var draftId = await _svc.CreateDraftAsync(
            "tutor-5",
            sessionId,
            new TutorAssessmentDraftRequest(
                5, 4, 5, 4, 2, 3, 2, 2, 3,
                "Scoring test feedback.", null, null, null, null),
            CancellationToken.None);

        var result = await _svc.SubmitAsync(
            "tutor-5",
            sessionId,
            draftId,
            new TutorAssessmentSubmitRequest(
                5, 4, 5, 4, 2, 3, 2, 2, 3,
                null, null, null, null, null),
            CancellationToken.None);

        var expected = OetScoring.SpeakingProjectedScaled(
            new OetScoring.SpeakingCriterionScores(5, 4, 5, 4, 2, 3, 2, 2, 3));
        Assert.Equal(expected, result.EstimatedScaledScore);
    }

    // ── Fixture helpers ──────────────────────────────────────────────────

    private async Task<string> SeedFinishedSessionAsync(string userId)
    {
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        _db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_role_play",
            ProfessionId = "nursing",
            SubtestCode = "speaking",
            Title = "Test card",
            Difficulty = "core",
            Status = ContentStatus.Published,
            PublishedRevisionId = $"rev-{Guid.NewGuid():N}",
        });
        var cardId = $"card-{Guid.NewGuid():N}";
        _db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ScenarioTitle = "Dual scoring scenario",
            Setting = "Ward",
            CandidateRole = "Nurse",
            ProfessionId = "nursing",
            PatientEmotion = "neutral",
            CommunicationGoal = "Inform",
            ClinicalTopic = "general",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Status = ContentStatus.Published,
        });
        var sessionId = $"sess-{Guid.NewGuid():N}";
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = userId,
            RolePlayCardId = cardId,
            Mode = SpeakingSessionMode.AiSelfPractice,
            State = SpeakingSessionState.Finished,
            InterlocutorActorId = userId.Replace("learner-", "tutor-", StringComparison.Ordinal),
            ElapsedSeconds = 300,
            EndedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return sessionId;
    }

    private async Task SeedTimestampedCommentAsync(string sessionId, string tutorId)
    {
        _db.SpeakingTimestampedComments.Add(new SpeakingTimestampedComment
        {
            Id = $"tc-{Guid.NewGuid():N}",
            SpeakingSessionId = sessionId,
            AuthorId = tutorId,
            AuthorRole = "tutor",
            TranscriptSegmentIndex = 0,
            StartMs = 0,
            EndMs = 1500,
            CriterionCode = "general",
            Severity = "info",
            BodyMarkdown = "Fixture timestamped comment.",
        });
        await _db.SaveChangesAsync();
    }

    private async Task<string> SeedAiAssessmentAsync(
        string sessionId,
        int[] linguistic,
        int[] clinical)
    {
        var aiId = $"ai-{Guid.NewGuid():N}";
        var scores = new OetScoring.SpeakingCriterionScores(
            linguistic[0], linguistic[1], linguistic[2], linguistic[3],
            clinical[0], clinical[1], clinical[2], clinical[3], clinical[4]);
        var scaled = OetScoring.SpeakingProjectedScaled(scores);
        _db.SpeakingAiAssessments.Add(new SpeakingAiAssessment
        {
            Id = aiId,
            SpeakingSessionId = sessionId,
            TranscriptId = $"tx-{Guid.NewGuid():N}",
            Provider = "openai",
            ModelId = "gpt-test",
            Intelligibility = linguistic[0],
            Fluency = linguistic[1],
            Appropriateness = linguistic[2],
            GrammarExpression = linguistic[3],
            RelationshipBuilding = clinical[0],
            PatientPerspective = clinical[1],
            Structure = clinical[2],
            InformationGathering = clinical[3],
            InformationGiving = clinical[4],
            EstimatedScaledScore = scaled,
            ReadinessBand = OetScoring.SpeakingReadinessBandCode(
                OetScoring.SpeakingReadinessBandFromScaled(scaled)),
            OverallSummary = "AI summary.",
            ConfidenceBand = "medium",
            GeneratedAt = DateTimeOffset.UtcNow,
            IsAdvisory = true,
        });
        await _db.SaveChangesAsync();
        return aiId;
    }

    private static (SpeakingAiAssessment ai, SpeakingTutorAssessment tutor) BuildPairWithSumOfAbs(int sumOfAbs)
    {
        // Distribute delta across the nine criteria, clamped to the
        // per-criterion range (linguistic 0-6, clinical 0-3).
        var deltas = new int[9];
        var remaining = sumOfAbs;
        int[] caps = { 6, 6, 6, 6, 3, 3, 3, 3, 3 };
        for (var i = 0; i < deltas.Length && remaining > 0; i++)
        {
            var take = Math.Min(remaining, caps[i]);
            deltas[i] = take;
            remaining -= take;
        }
        if (remaining > 0)
        {
            throw new InvalidOperationException(
                $"sumOfAbs={sumOfAbs} cannot be encoded within the OET rubric caps.");
        }

        var ai = new SpeakingAiAssessment
        {
            Id = "ai-pair",
            SpeakingSessionId = "sess-pair",
            TranscriptId = "tx-pair",
            Provider = "openai",
            ModelId = "gpt-test",
            Intelligibility = 0,
            Fluency = 0,
            Appropriateness = 0,
            GrammarExpression = 0,
            RelationshipBuilding = 0,
            PatientPerspective = 0,
            Structure = 0,
            InformationGathering = 0,
            InformationGiving = 0,
            EstimatedScaledScore = 0,
            ReadinessBand = "not_ready",
            GeneratedAt = DateTimeOffset.UtcNow,
        };
        var tutor = new SpeakingTutorAssessment
        {
            Id = "tu-pair",
            SpeakingSessionId = "sess-pair",
            TutorId = "tutor-x",
            Intelligibility = deltas[0],
            Fluency = deltas[1],
            Appropriateness = deltas[2],
            GrammarExpression = deltas[3],
            RelationshipBuilding = deltas[4],
            PatientPerspective = deltas[5],
            Structure = deltas[6],
            InformationGathering = deltas[7],
            InformationGiving = deltas[8],
            EstimatedScaledScore = 0,
            ReadinessBand = "not_ready",
            IsFinal = true,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return (ai, tutor);
    }
}
