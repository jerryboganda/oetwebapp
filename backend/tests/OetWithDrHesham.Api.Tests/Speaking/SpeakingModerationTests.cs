using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Tests.Speaking;

// OET Speaking module — double-marking + senior moderation (§15.4 / §15.5).
//
// These tests PIN the exam-integrity contract of the moderation lifecycle:
//
//   * A session can only enter moderation once its PRIMARY tutor assessment
//     is final.
//   * Separation of duties: the second marker must differ from the first; the
//     senior moderator must differ from both human markers.
//   * When the two human markers agree within the scaled-score threshold the
//     case auto-finalizes on the per-criterion average; otherwise it escalates
//     to senior moderation with no final score yet.
//   * The moderator can either record a reconciled final score or request a
//     reattempt.
//   * The learner-facing canonical tutor score prefers the moderated final
//     over the primary mark, so the ordinary single-marker flow is never
//     altered when only a primary final exists.
public sealed class SpeakingModerationTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private SpeakingModerationService _svc = default!;
    private TutorAssessmentService _tutorSvc = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-moderation-{Guid.NewGuid():N}")
            .Options;
        _db = new LearnerDbContext(options);
        _svc = new SpeakingModerationService(
            _db,
            NullLogger<SpeakingModerationService>.Instance,
            TimeProvider.System);
        _tutorSvc = new TutorAssessmentService(
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

    // ── Open ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Open_RequiresFinalisedPrimaryAssessment()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-1");

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.OpenAsync("expert-a", sessionId, "tutor_request", CancellationToken.None));

        Assert.Equal("speaking_moderation_primary_not_final", ex.ErrorCode);
    }

    [Fact]
    public async Task Open_IsIdempotent_AndCapturesPrimaryScore()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-2");
        await SeedPrimaryAssessmentAsync(sessionId, "tutor-primary", new[] { 5, 5, 5, 5 }, new[] { 2, 2, 2, 2, 2 });

        var first = await _svc.OpenAsync("expert-a", sessionId, "tutor_request", CancellationToken.None);
        var second = await _svc.OpenAsync("expert-a", sessionId, "dispute", CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("pending_second", first.Status);
        Assert.Equal("tutor-primary", first.FirstMarkerId);
        Assert.NotNull(first.FirstScore);
        Assert.Single(await _db.SpeakingModerationCases.ToListAsync());
    }

    // ── Second mark ─────────────────────────────────────────────────────

    [Fact]
    public async Task SecondMark_RejectsSameMarkerAsFirst()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-3");
        await SeedPrimaryAssessmentAsync(sessionId, "tutor-primary", new[] { 5, 5, 5, 5 }, new[] { 2, 2, 2, 2, 2 });
        await _svc.OpenAsync("expert-a", sessionId, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.SubmitSecondMarkAsync(
                "tutor-primary", sessionId, Mark(5, 5, 5, 5, 2, 2, 2, 2, 2),
                SpeakingModerationService.DefaultVarianceThreshold, CancellationToken.None));

        Assert.Equal("speaking_moderation_same_marker", ex.ErrorCode);
    }

    [Fact]
    public async Task SecondMark_WithinThreshold_AutoFinalizes()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-4");
        await SeedPrimaryAssessmentAsync(sessionId, "tutor-primary", new[] { 5, 5, 5, 5 }, new[] { 2, 2, 2, 2, 2 });
        await _svc.OpenAsync("expert-a", sessionId, null, CancellationToken.None);

        // Identical second mark → zero variance → auto-finalize.
        var result = await _svc.SubmitSecondMarkAsync(
            "tutor-second", sessionId, Mark(5, 5, 5, 5, 2, 2, 2, 2, 2),
            SpeakingModerationService.DefaultVarianceThreshold, CancellationToken.None);

        Assert.Equal("finalized", result.Status);
        Assert.Equal(0, result.VariancePoints);
        Assert.NotNull(result.SecondScore);
        Assert.NotNull(result.FinalScore);
        // A moderated final row must exist for the canonical projection.
        Assert.Contains(
            await _db.SpeakingTutorAssessments.ToListAsync(),
            t => t.MarkerRole == "moderated" && t.IsFinal);
    }

    [Fact]
    public async Task SecondMark_BeyondThreshold_EscalatesToModeration()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-5");
        await SeedPrimaryAssessmentAsync(sessionId, "tutor-primary", new[] { 6, 6, 6, 6 }, new[] { 3, 3, 3, 3, 3 });
        await _svc.OpenAsync("expert-a", sessionId, null, CancellationToken.None);

        // Very different second mark with a zero threshold → escalation.
        var result = await _svc.SubmitSecondMarkAsync(
            "tutor-second", sessionId, Mark(1, 1, 1, 1, 0, 0, 0, 0, 0),
            varianceThreshold: 0, CancellationToken.None);

        Assert.Equal("pending_moderation", result.Status);
        Assert.NotNull(result.SecondScore);
        Assert.Null(result.FinalScore);
        Assert.True(result.VariancePoints > 0);
    }

    // ── Senior moderation ───────────────────────────────────────────────

    [Fact]
    public async Task Finalize_RejectsModeratorWhoIsAlsoAMarker()
    {
        var sessionId = await SeedEscalatedCaseAsync("learner-6", "tutor-primary", "tutor-second");

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _svc.FinalizeAsync(
                "tutor-second", sessionId, Finalize(4, 4, 4, 4, 2, 2, 2, 2, 2, requestReattempt: false),
                CancellationToken.None));

        Assert.Equal("speaking_moderation_marker_cannot_moderate", ex.ErrorCode);
    }

    [Fact]
    public async Task Finalize_RecordsModeratedFinalScore()
    {
        var sessionId = await SeedEscalatedCaseAsync("learner-7", "tutor-primary", "tutor-second");

        var result = await _svc.FinalizeAsync(
            "moderator-1", sessionId, Finalize(4, 4, 4, 4, 2, 2, 2, 2, 2, requestReattempt: false, note: "Balanced view."),
            CancellationToken.None);

        Assert.Equal("finalized", result.Status);
        Assert.Equal("moderator-1", result.ModeratorId);
        Assert.NotNull(result.FinalScore);
        Assert.Equal("Balanced view.", result.FinalDecisionNote);
        Assert.False(result.RequestReattempt);
    }

    [Fact]
    public async Task Finalize_RequestReattempt_LeavesNoFinalScore()
    {
        var sessionId = await SeedEscalatedCaseAsync("learner-8", "tutor-primary", "tutor-second");

        var result = await _svc.FinalizeAsync(
            "moderator-1", sessionId, Finalize(0, 0, 0, 0, 0, 0, 0, 0, 0, requestReattempt: true),
            CancellationToken.None);

        Assert.Equal("reattempt_requested", result.Status);
        Assert.True(result.RequestReattempt);
        Assert.Null(result.FinalScore);
    }

    // ── Canonical projection ────────────────────────────────────────────

    [Fact]
    public async Task LearnerDual_PrefersModeratedFinalOverPrimary()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-9");
        // Primary final: high scores.
        await SeedPrimaryAssessmentAsync(sessionId, "tutor-primary", new[] { 6, 6, 6, 6 }, new[] { 3, 3, 3, 3, 3 });
        // Moderated final: low scores — must win.
        await SeedAssessmentRowAsync(sessionId, "moderator-1", "moderated", new[] { 2, 2, 2, 2 }, new[] { 1, 1, 1, 1, 1 });

        var dual = await _tutorSvc.GetDualAssessmentForLearnerAsync("learner-9", sessionId, CancellationToken.None);

        Assert.NotNull(dual.Tutor);
        var moderatedScaled = OetScoring.SpeakingProjectedScaled(
            new OetScoring.SpeakingCriterionScores(2, 2, 2, 2, 1, 1, 1, 1, 1));
        Assert.Equal(moderatedScaled, dual.Tutor!.EstimatedScaledScore);
    }

    [Fact]
    public async Task LearnerDual_FallsBackToPrimaryWhenNoModeratedFinal()
    {
        var sessionId = await SeedFinishedSessionAsync("learner-10");
        await SeedPrimaryAssessmentAsync(sessionId, "tutor-primary", new[] { 4, 4, 4, 4 }, new[] { 2, 2, 2, 2, 2 });

        var dual = await _tutorSvc.GetDualAssessmentForLearnerAsync("learner-10", sessionId, CancellationToken.None);

        Assert.NotNull(dual.Tutor);
        var primaryScaled = OetScoring.SpeakingProjectedScaled(
            new OetScoring.SpeakingCriterionScores(4, 4, 4, 4, 2, 2, 2, 2, 2));
        Assert.Equal(primaryScaled, dual.Tutor!.EstimatedScaledScore);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static SpeakingSecondMarkRequest Mark(
        int intel, int flu, int app, int gram, int rel, int pat, int str, int gather, int give)
        => new(intel, flu, app, gram, rel, pat, str, gather, give, null, null, null, null);

    private static SpeakingModerationFinalizeRequest Finalize(
        int intel, int flu, int app, int gram, int rel, int pat, int str, int gather, int give,
        bool requestReattempt, string? note = null)
        => new(intel, flu, app, gram, rel, pat, str, gather, give, null, note, requestReattempt);

    private async Task<string> SeedEscalatedCaseAsync(string learnerId, string firstMarker, string secondMarker)
    {
        var sessionId = await SeedFinishedSessionAsync(learnerId);
        await SeedPrimaryAssessmentAsync(sessionId, firstMarker, new[] { 6, 6, 6, 6 }, new[] { 3, 3, 3, 3, 3 });
        await _svc.OpenAsync("expert-a", sessionId, null, CancellationToken.None);
        await _svc.SubmitSecondMarkAsync(
            secondMarker, sessionId, Mark(1, 1, 1, 1, 0, 0, 0, 0, 0),
            varianceThreshold: 0, CancellationToken.None);
        return sessionId;
    }

    private Task SeedPrimaryAssessmentAsync(string sessionId, string tutorId, int[] linguistic, int[] clinical)
        => SeedAssessmentRowAsync(sessionId, tutorId, "primary", linguistic, clinical);

    private async Task SeedAssessmentRowAsync(
        string sessionId, string tutorId, string markerRole, int[] linguistic, int[] clinical)
    {
        var scores = new OetScoring.SpeakingCriterionScores(
            linguistic[0], linguistic[1], linguistic[2], linguistic[3],
            clinical[0], clinical[1], clinical[2], clinical[3], clinical[4]);
        var scaled = OetScoring.SpeakingProjectedScaled(scores);
        var now = DateTimeOffset.UtcNow;
        _db.SpeakingTutorAssessments.Add(new SpeakingTutorAssessment
        {
            Id = $"tu-{Guid.NewGuid():N}",
            SpeakingSessionId = sessionId,
            TutorId = tutorId,
            MarkerRole = markerRole,
            Intelligibility = scores.Intelligibility,
            Fluency = scores.Fluency,
            Appropriateness = scores.Appropriateness,
            GrammarExpression = scores.GrammarExpression,
            RelationshipBuilding = scores.RelationshipBuilding,
            PatientPerspective = scores.PatientPerspective,
            Structure = scores.Structure,
            InformationGathering = scores.InformationGathering,
            InformationGiving = scores.InformationGiving,
            EstimatedScaledScore = scaled,
            ReadinessBand = OetScoring.SpeakingReadinessBandCode(
                OetScoring.SpeakingReadinessBandFromScaled(scaled)),
            OverallFeedbackMarkdown = string.Empty,
            StrengthsJson = "[]",
            ImprovementsJson = "[]",
            RecommendedDrillsJson = "[]",
            RecommendedRulebookEntries = string.Empty,
            IsFinal = true,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();
    }

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
            ScenarioTitle = "Moderation scenario",
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
}
