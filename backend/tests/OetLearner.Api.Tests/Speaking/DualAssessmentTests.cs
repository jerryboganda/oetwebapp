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
//   * Divergence exposes signed tutor-minus-AI deltas and bands by sum-of-abs:
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

        Assert.Equal(aiBefore.Intelligibility, aiAfter.Intelligibility);
        Assert.Equal(aiBefore.Fluency, aiAfter.Fluency);
        Assert.Equal(aiBefore.Appropriateness, aiAfter.Appropriateness);
        Assert.Equal(aiBefore.GrammarExpression, aiAfter.GrammarExpression);
        Assert.Equal(aiBefore.RelationshipBuilding, aiAfter.RelationshipBuilding);
        Assert.Equal(aiBefore.PatientPerspective, aiAfter.PatientPerspective);
        Assert.Equal(aiBefore.Structure, aiAfter.Structure);
        Assert.Equal(aiBefore.InformationGathering, aiAfter.InformationGathering);
        Assert.Equal(aiBefore.InformationGiving, aiAfter.InformationGiving);
        Assert.Equal(aiBefore.EstimatedScaledScore, aiAfter.EstimatedScaledScore);
        Assert.Equal(aiBefore.ReadinessBand, aiAfter.ReadinessBand);
        Assert.Equal(aiBefore.OverallSummary, aiAfter.OverallSummary);
        Assert.Equal(aiBefore.ConfidenceBand, aiAfter.ConfidenceBand);
        Assert.Equal(aiBefore.GeneratedAt, aiAfter.GeneratedAt);
        Assert.Equal(aiBefore.RulebookFindingsJson, aiAfter.RulebookFindingsJson);
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

        Assert.Equal(tutorBefore.Intelligibility, tutorAfter.Intelligibility);
        Assert.Equal(tutorBefore.Fluency, tutorAfter.Fluency);
        Assert.Equal(tutorBefore.Appropriateness, tutorAfter.Appropriateness);
        Assert.Equal(tutorBefore.GrammarExpression, tutorAfter.GrammarExpression);
        Assert.Equal(tutorBefore.RelationshipBuilding, tutorAfter.RelationshipBuilding);
        Assert.Equal(tutorBefore.PatientPerspective, tutorAfter.PatientPerspective);
        Assert.Equal(tutorBefore.Structure, tutorAfter.Structure);
        Assert.Equal(tutorBefore.InformationGathering, tutorAfter.InformationGathering);
        Assert.Equal(tutorBefore.InformationGiving, tutorAfter.InformationGiving);
        Assert.Equal(tutorBefore.EstimatedScaledScore, tutorAfter.EstimatedScaledScore);
        Assert.Equal(tutorBefore.ReadinessBand, tutorAfter.ReadinessBand);
        Assert.Equal(tutorBefore.OverallFeedbackMarkdown, tutorAfter.OverallFeedbackMarkdown);
        Assert.Equal(tutorBefore.IsFinal, tutorAfter.IsFinal);
        Assert.Equal(tutorBefore.SubmittedAt, tutorAfter.SubmittedAt);
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

    [Fact]
    public async Task LearnerCannotReadAnotherLearnersDualAssessment()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-owner");
        await SeedAiAssessmentAsync(sessionId, new[] { 5, 5, 5, 5 }, new[] { 2, 2, 2, 2, 2 });

        var ex = await Assert.ThrowsAsync<ApiException>(() => _svc.GetDualAssessmentForLearnerAsync(
            "learner-other",
            sessionId,
            CancellationToken.None));

        Assert.Equal("speaking_session_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task LearnerCannotSeeTutorDraftBeforeFinalSubmission()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-draft");
        await SeedAiAssessmentAsync(sessionId, new[] { 5, 5, 5, 5 }, new[] { 2, 2, 2, 2, 2 });

        await _svc.CreateDraftAsync(
            "tutor-draft",
            sessionId,
            new TutorAssessmentDraftRequest(
                4, 4, 4, 4, 2, 2, 2, 2, 2,
                "Draft feedback should stay hidden.", null, null, null, null),
            CancellationToken.None);

        var dual = await _svc.GetDualAssessmentForLearnerAsync("learner-draft", sessionId, CancellationToken.None);

        Assert.NotNull(dual.Ai);
        Assert.Null(dual.Tutor);
        Assert.Empty(dual.TutorHistory);
        Assert.Null(dual.Divergence);
    }

    // ── Divergence agreement bands ───────────────────────────────────────

    [Theory]
    [InlineData(2, "close")]
    [InlineData(4, "close")]
    [InlineData(7, "moderate")]
    [InlineData(10, "moderate")]
    [InlineData(15, "wide")]
    public void DivergenceCalculation_ComputesAgreementBand(int sumOfAbs, string expectedBand)
    {
        var (ai, tutor) = BuildPairWithSumOfAbs(sumOfAbs);
        var divergence = TutorAssessmentService.ComputeDivergence(ai, tutor);
        Assert.Equal(expectedBand, divergence.AgreementBand);
        var actualSum = divergence.PerCriterion.Values.Sum(Math.Abs);
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

    [Fact]
    public async Task Submit_RequiresEveryScoreInRequest_EvenWhenDraftHadScores()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-6");
        await SeedTimestampedCommentAsync(sessionId, "tutor-6");

        var draftId = await _svc.CreateDraftAsync(
            "tutor-6",
            sessionId,
            new TutorAssessmentDraftRequest(
                5, 4, 5, 4, 2, 3, 2, 2, 3,
                "Complete draft feedback.", null, null, null, null),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => _svc.SubmitAsync(
            "tutor-6",
            sessionId,
            draftId,
            new TutorAssessmentSubmitRequest(
                null, 4, 5, 4, 2, 3, 2, 2, 3,
                null, null, null, null, null),
            CancellationToken.None));

        Assert.Equal("tutor_assessment_invalid_scores", ex.ErrorCode);
        Assert.Contains(ex.FieldErrors, err => err.Field == "intelligibility" && err.Code == "missing");
    }

    [Fact]
    public async Task Submit_RequiresTimestampedCommentBySubmittingTutor()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-7");
        await SeedTimestampedCommentAsync(sessionId, "tutor-other");

        var draftId = await _svc.CreateDraftAsync(
            "tutor-7",
            sessionId,
            new TutorAssessmentDraftRequest(
                5, 4, 5, 4, 2, 3, 2, 2, 3,
                "Complete draft feedback.", null, null, null, null),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => _svc.SubmitAsync(
            "tutor-7",
            sessionId,
            draftId,
            new TutorAssessmentSubmitRequest(
                5, 4, 5, 4, 2, 3, 2, 2, 3,
                null, null, null, null, null),
            CancellationToken.None));

        Assert.Equal("tutor_assessment_missing_timestamped_comment", ex.ErrorCode);
    }

    [Fact]
    public async Task TimestampedComment_RejectsBlankBody()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-8");

        var ex = await Assert.ThrowsAsync<ApiException>(() => _svc.AddTimestampedCommentAsync(
            "tutor-8",
            sessionId,
            new TutorTimestampedCommentRequest(
                TranscriptSegmentIndex: 2,
                StartMs: 1000,
                EndMs: 1500,
                CriterionCode: "fluency",
                Severity: "minor",
                BodyMarkdown: "   ",
                LinkedRulebookEntryCode: null,
                LinkedDrillId: null),
            CancellationToken.None));

        Assert.Equal("comment_body_required", ex.ErrorCode);
        Assert.Empty(_db.SpeakingTimestampedComments);
    }

    [Fact]
    public async Task TimestampedComment_RequiresAssignedOrClaimedTutor()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-9");

        var ex = await Assert.ThrowsAsync<ApiException>(() => _svc.AddTimestampedCommentAsync(
            "tutor-other",
            sessionId,
            new TutorTimestampedCommentRequest(
                TranscriptSegmentIndex: 0,
                StartMs: 0,
                EndMs: 1500,
                CriterionCode: "fluency",
                Severity: "minor",
                BodyMarkdown: "Wrong tutor should not be able to comment.",
                LinkedRulebookEntryCode: null,
                LinkedDrillId: null),
            CancellationToken.None));

        Assert.Equal("tutor_assessment_forbidden", ex.ErrorCode);
        Assert.Empty(_db.SpeakingTimestampedComments);
    }

    [Fact]
    public async Task TutorAssessment_AllowsTutorWithActiveClaim()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-claim");
        await SeedReviewClaimAsync(sessionId, "tutor-claimed");

        var response = await _svc.GetDualAssessmentForTutorAsync(
            "tutor-claimed",
            sessionId,
            CancellationToken.None);

        Assert.Equal(sessionId, response.SessionId);
    }

    [Fact]
    public async Task TutorAssessment_RefreshesActiveClaimOnAssessmentActivity()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-heartbeat");
        var originalCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-(TutorReviewQueueService.IdleClaimTtlMinutes - 1));
        await SeedReviewClaimAsync(sessionId, "tutor-heartbeat", originalCreatedAt);

        await _svc.GetDualAssessmentForTutorAsync(
            "tutor-heartbeat",
            sessionId,
            CancellationToken.None);

        var refreshedCreatedAt = await _db.ReviewRequests.AsNoTracking()
            .Where(r => r.AttemptId == sessionId && r.SubtestCode == TutorReviewQueueService.SubtestCode)
            .Select(r => r.CreatedAt)
            .SingleAsync();

        Assert.True(refreshedCreatedAt > originalCreatedAt);
    }

    [Fact]
    public async Task TutorAssessment_RejectsExpiredClaim()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-stale");
        await SeedReviewClaimAsync(
            sessionId,
            "tutor-expired",
            DateTimeOffset.UtcNow.AddMinutes(-(TutorReviewQueueService.IdleClaimTtlMinutes + 1)));

        var ex = await Assert.ThrowsAsync<ApiException>(() => _svc.GetDualAssessmentForTutorAsync(
            "tutor-expired",
            sessionId,
            CancellationToken.None));

        Assert.Equal("tutor_assessment_forbidden", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateDraft_RequiresCurrentClaimAfterDraftCreation()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-release");
        await SeedReviewClaimAsync(sessionId, "tutor-released");

        var draftId = await _svc.CreateDraftAsync(
            "tutor-released",
            sessionId,
            new TutorAssessmentDraftRequest(
                4, 4, 4, 4, 2, 2, 2, 2, 2,
                "Draft while claim is active.", null, null, null, null),
            CancellationToken.None);

        _db.ReviewRequests.RemoveRange(_db.ReviewRequests);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => _svc.UpdateDraftAsync(
            "tutor-released",
            sessionId,
            draftId,
            new TutorAssessmentDraftRequest(
                5, null, null, null, null, null, null, null, null,
                null, null, null, null, null),
            CancellationToken.None));

        Assert.Equal("tutor_assessment_forbidden", ex.ErrorCode);
    }

    [Fact]
    public void DivergenceCalculation_ExposesSignedTutorMinusAiDeltas()
    {
        var ai = new SpeakingAiAssessment
        {
            Id = "ai-signed",
            SpeakingSessionId = "sess-signed",
            TranscriptId = "tx-signed",
            Provider = "openai",
            ModelId = "gpt-test",
            Intelligibility = 6,
            Fluency = 3,
            Appropriateness = 3,
            GrammarExpression = 3,
            RelationshipBuilding = 2,
            PatientPerspective = 1,
            Structure = 1,
            InformationGathering = 1,
            InformationGiving = 1,
            EstimatedScaledScore = 350,
            ReadinessBand = "borderline",
            GeneratedAt = DateTimeOffset.UtcNow,
        };
        var tutor = new SpeakingTutorAssessment
        {
            Id = "tu-signed",
            SpeakingSessionId = "sess-signed",
            TutorId = "tutor-signed",
            Intelligibility = 4,
            Fluency = 4,
            Appropriateness = 3,
            GrammarExpression = 3,
            RelationshipBuilding = 3,
            PatientPerspective = 1,
            Structure = 1,
            InformationGathering = 1,
            InformationGiving = 1,
            EstimatedScaledScore = 340,
            ReadinessBand = "not_ready",
            IsFinal = true,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var divergence = TutorAssessmentService.ComputeDivergence(ai, tutor);

        Assert.Equal(-2, divergence.PerCriterion["intelligibility"]);
        Assert.Equal(1, divergence.PerCriterion["fluency"]);
        Assert.Equal(1, divergence.PerCriterion["relationshipBuilding"]);
        Assert.Equal(-10, divergence.ScaledDelta);
        Assert.Equal("close", divergence.AgreementBand);
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

    private async Task SeedReviewClaimAsync(string sessionId, string tutorId, DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        _db.ReviewRequests.Add(new ReviewRequest
        {
            Id = $"rr-{Guid.NewGuid():N}",
            AttemptId = sessionId,
            SubtestCode = TutorReviewQueueService.SubtestCode,
            State = ReviewRequestState.InReview,
            TurnaroundOption = "standard",
            FocusAreasJson = "[]",
            LearnerNotes = string.Empty,
            PaymentSource = "included",
            PriceSnapshot = 0,
            ReviewerCompensation = 0,
            CreatedAt = now,
            EligibilitySnapshotJson = "{}",
        });
        _db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            Action = "SpeakingSessionClaimed",
            ActorId = tutorId,
            ActorName = tutorId,
            ResourceId = sessionId,
            ResourceType = "SpeakingSession",
            Details = "fixture",
            OccurredAt = now,
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
