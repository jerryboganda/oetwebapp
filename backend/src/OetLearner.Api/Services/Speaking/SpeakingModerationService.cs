using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

// OET Speaking module — double-marking + senior moderation lifecycle
// (Developer Implementation Notes §15.4 / §15.5).
//
// EXAM-INTEGRITY INVARIANT — read TutorAssessmentService.cs first.
//
//   The ordinary tutor flow (AI advisory + ONE primary human assessor) is the
//   default and is NEVER touched here. This service only runs the *exception*
//   path: a session whose primary tutor assessment is already final can be
//   escalated into a moderation case, which adds a strictly-separate SECOND
//   independent human assessor and — when the two human markers diverge beyond
//   the configured scaled-score threshold — a SENIOR moderator who records the
//   reconciled final.
//
//   Separation of duties is enforced:
//     * the second marker must differ from the first marker;
//     * the moderator must differ from both human markers.
//
//   Every marker's score is persisted as its own
//   <see cref="SpeakingTutorAssessment"/> row distinguished by
//   <see cref="SpeakingTutorAssessment.MarkerRole"/> ("primary" | "second" |
//   "moderated"). The projected scaled score for every row is computed via the
//   single source of truth — <see cref="OetScoring.SpeakingProjectedScaled"/>
//   — so the moderation math never drifts from the AI / tutor surfaces.
public sealed class SpeakingModerationService(
    LearnerDbContext db,
    ILogger<SpeakingModerationService> logger,
    TimeProvider clock)
{
    /// <summary>Default inter-marker scaled-score variance threshold. When the
    /// absolute delta between the two human markers' projected scaled scores is
    /// within this band the case auto-finalizes on the per-criterion average;
    /// otherwise it escalates to senior moderation.</summary>
    public const int DefaultVarianceThreshold = 30;

    private static readonly string[] AllowedReasons = ["tutor_request", "wide_divergence", "dispute"];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Open a case ─────────────────────────────────────────────────────

    /// <summary>Opens (or returns the existing) moderation case for a finished
    /// session whose primary tutor assessment is already final. Idempotent.</summary>
    public async Task<SpeakingModerationCaseProjection> OpenAsync(
        string actingExpertId,
        string sessionId,
        string? reason,
        CancellationToken ct)
    {
        var session = await LoadFinishedSessionAsync(sessionId, ct);

        var existing = await db.SpeakingModerationCases
            .FirstOrDefaultAsync(c => c.SpeakingSessionId == sessionId, ct);
        if (existing is not null)
        {
            return Project(existing);
        }

        var primary = await db.SpeakingTutorAssessments
            .AsNoTracking()
            .Where(t => t.SpeakingSessionId == sessionId
                        && t.IsFinal
                        && (t.MarkerRole == "primary" || t.MarkerRole == ""))
            .OrderByDescending(t => t.SubmittedAt ?? t.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (primary is null)
        {
            throw ApiException.Validation(
                "speaking_moderation_primary_not_final",
                "A finalised primary tutor assessment is required before a session can be sent to moderation.");
        }

        var normalisedReason = NormaliseReason(reason);
        var now = clock.GetUtcNow();
        var moderation = new SpeakingModerationCase
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakingSessionId = sessionId,
            Reason = normalisedReason,
            FirstMarkerId = primary.TutorId,
            FirstAssessmentId = primary.Id,
            FirstScoreJson = SerialiseScore(ScoreFrom(primary)),
            Status = "pending_second",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SpeakingModerationCases.Add(moderation);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Speaking moderation opened expert={ExpertId} session={SessionId} case={CaseId} reason={Reason}",
            actingExpertId, sessionId, moderation.Id, normalisedReason);

        return Project(moderation);
    }

    // ── Queue ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SpeakingModerationQueueItem>> ListQueueAsync(
        string professionId,
        CancellationToken ct)
    {
        var query =
            from c in db.SpeakingModerationCases.AsNoTracking()
            where c.Status == "pending_second" || c.Status == "pending_moderation"
            join session in db.SpeakingSessions.AsNoTracking()
                on c.SpeakingSessionId equals session.Id
            join card in db.RolePlayCards.AsNoTracking()
                on session.RolePlayCardId equals card.Id
            select new { c, card.ProfessionId };

        if (!string.IsNullOrWhiteSpace(professionId))
        {
            var pid = professionId.Trim().ToLowerInvariant();
            query = query.Where(x => x.ProfessionId == pid);
        }

        var rows = await query
            .OrderBy(x => x.c.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(x => new SpeakingModerationQueueItem(
            CaseId: x.c.Id,
            SessionId: x.c.SpeakingSessionId,
            ProfessionId: x.ProfessionId,
            Reason: x.c.Reason,
            Status: x.c.Status,
            VariancePoints: x.c.VariancePoints,
            NeedsSecondMark: x.c.Status == "pending_second",
            NeedsModeration: x.c.Status == "pending_moderation",
            CreatedAt: x.c.CreatedAt,
            UpdatedAt: x.c.UpdatedAt)).ToList();
    }

    public async Task<SpeakingModerationCaseProjection?> GetAsync(string sessionId, CancellationToken ct)
    {
        var moderation = await db.SpeakingModerationCases
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SpeakingSessionId == sessionId, ct);
        return moderation is null ? null : Project(moderation);
    }

    // ── Second independent mark ─────────────────────────────────────────

    public async Task<SpeakingModerationCaseProjection> SubmitSecondMarkAsync(
        string secondMarkerId,
        string sessionId,
        SpeakingSecondMarkRequest req,
        int varianceThreshold,
        CancellationToken ct)
    {
        await LoadFinishedSessionAsync(sessionId, ct);

        var moderation = await db.SpeakingModerationCases
            .FirstOrDefaultAsync(c => c.SpeakingSessionId == sessionId, ct);
        if (moderation is null)
        {
            throw ApiException.NotFound(
                "speaking_moderation_not_found",
                "No moderation case exists for this session.");
        }
        if (moderation.Status != "pending_second")
        {
            throw ApiException.Conflict(
                "speaking_moderation_second_mark_closed",
                "A second mark may only be submitted while the case is awaiting a second marker.");
        }
        if (string.Equals(moderation.FirstMarkerId, secondMarkerId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden(
                "speaking_moderation_same_marker",
                "The second marker must be a different assessor from the first marker.");
        }

        var scores = ValidateScores(
            req.Intelligibility, req.Fluency, req.Appropriateness, req.GrammarExpression,
            req.RelationshipBuilding, req.PatientPerspective, req.Structure,
            req.InformationGathering, req.InformationGiving);

        var now = clock.GetUtcNow();
        var secondRow = CreateAssessmentRow(
            sessionId, secondMarkerId, "second", scores,
            req.OverallFeedbackMarkdown, req.Strengths, req.Improvements, req.RecommendedDrills, now);
        db.SpeakingTutorAssessments.Add(secondRow);

        moderation.SecondMarkerId = secondMarkerId;
        moderation.SecondAssessmentId = secondRow.Id;
        moderation.SecondScoreJson = SerialiseScore(ScoreFrom(secondRow));

        var firstScaled = DeserialiseScore(moderation.FirstScoreJson)?.EstimatedScaledScore ?? 0;
        var secondScaled = secondRow.EstimatedScaledScore;
        var variance = Math.Abs(firstScaled - secondScaled);
        moderation.VariancePoints = variance;

        if (variance <= varianceThreshold)
        {
            // Auto-finalize on the per-criterion average of the two human markers.
            var firstRow = await db.SpeakingTutorAssessments.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == moderation.FirstAssessmentId, ct);
            var averaged = AverageScores(ScoreFrom(ScoreFrom(firstRow)) ?? scores, scores);
            var moderatedRow = CreateAssessmentRow(
                sessionId, secondMarkerId, "moderated", averaged,
                "Auto-reconciled average of the first and second markers (variance within threshold).",
                null, null, null, now);
            db.SpeakingTutorAssessments.Add(moderatedRow);

            moderation.FinalAssessmentId = moderatedRow.Id;
            moderation.FinalScoreJson = SerialiseScore(ScoreFrom(moderatedRow));
            moderation.Status = "finalized";
            moderation.VarianceReason =
                $"Auto-finalized: scaled variance {variance} within threshold {varianceThreshold}.";
        }
        else
        {
            moderation.Status = "pending_moderation";
            moderation.VarianceReason =
                $"Scaled variance {variance} exceeds threshold {varianceThreshold}; senior moderation required.";
        }

        moderation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Speaking second mark submitted marker={MarkerId} session={SessionId} variance={Variance} status={Status}",
            secondMarkerId, sessionId, variance, moderation.Status);

        return Project(moderation);
    }

    // ── Senior moderation finalize ──────────────────────────────────────

    public async Task<SpeakingModerationCaseProjection> FinalizeAsync(
        string moderatorId,
        string sessionId,
        SpeakingModerationFinalizeRequest req,
        CancellationToken ct)
    {
        await LoadFinishedSessionAsync(sessionId, ct);

        var moderation = await db.SpeakingModerationCases
            .FirstOrDefaultAsync(c => c.SpeakingSessionId == sessionId, ct);
        if (moderation is null)
        {
            throw ApiException.NotFound(
                "speaking_moderation_not_found",
                "No moderation case exists for this session.");
        }
        if (moderation.Status != "pending_moderation")
        {
            throw ApiException.Conflict(
                "speaking_moderation_not_awaiting_moderation",
                "This case is not awaiting senior moderation.");
        }
        if (string.Equals(moderation.FirstMarkerId, moderatorId, StringComparison.Ordinal)
            || string.Equals(moderation.SecondMarkerId, moderatorId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden(
                "speaking_moderation_marker_cannot_moderate",
                "The senior moderator must be different from both human markers.");
        }

        var now = clock.GetUtcNow();
        moderation.ModeratorId = moderatorId;

        if (req.RequestReattempt)
        {
            moderation.RequestReattempt = true;
            moderation.FinalDecisionNote = string.IsNullOrWhiteSpace(req.DecisionNote)
                ? "Reattempt requested by moderator."
                : req.DecisionNote.Trim();
            moderation.Status = "reattempt_requested";
            moderation.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Speaking moderation reattempt requested moderator={ModeratorId} session={SessionId}",
                moderatorId, sessionId);

            return Project(moderation);
        }

        var scores = ValidateScores(
            req.Intelligibility, req.Fluency, req.Appropriateness, req.GrammarExpression,
            req.RelationshipBuilding, req.PatientPerspective, req.Structure,
            req.InformationGathering, req.InformationGiving);

        var moderatedRow = CreateAssessmentRow(
            sessionId, moderatorId, "moderated", scores,
            req.OverallFeedbackMarkdown, null, null, null, now);
        db.SpeakingTutorAssessments.Add(moderatedRow);

        moderation.FinalAssessmentId = moderatedRow.Id;
        moderation.FinalScoreJson = SerialiseScore(ScoreFrom(moderatedRow));
        moderation.FinalDecisionNote = string.IsNullOrWhiteSpace(req.DecisionNote)
            ? null
            : req.DecisionNote.Trim();
        moderation.Status = "finalized";
        moderation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Speaking moderation finalized moderator={ModeratorId} session={SessionId} scaled={Scaled}",
            moderatorId, sessionId, moderatedRow.EstimatedScaledScore);

        return Project(moderation);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task<SpeakingSession> LoadFinishedSessionAsync(string sessionId, CancellationToken ct)
    {
        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            throw ApiException.NotFound("speaking_session_not_found", "Speaking session not found.");
        }
        if (session.State != SpeakingSessionState.Finished)
        {
            throw ApiException.Validation(
                "speaking_session_not_finished",
                "Moderation may only run on finished sessions.");
        }
        return session;
    }

    private static string NormaliseReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "tutor_request";
        var trimmed = reason.Trim().ToLowerInvariant();
        return Array.IndexOf(AllowedReasons, trimmed) >= 0 ? trimmed : "tutor_request";
    }

    private static OetScoring.SpeakingCriterionScores ValidateScores(
        int? intelligibility, int? fluency, int? appropriateness, int? grammarExpression,
        int? relationshipBuilding, int? patientPerspective, int? structure,
        int? informationGathering, int? informationGiving)
    {
        var fieldErrors = new List<ApiFieldError>();

        int Linguistic(string field, int? value)
        {
            if (value is null)
            {
                fieldErrors.Add(new ApiFieldError(field, "missing", $"{field} must be scored before submitting."));
                return 0;
            }
            if (value < 0 || value > 6)
            {
                fieldErrors.Add(new ApiFieldError(field, "out_of_range", $"{field} must be between 0 and 6."));
            }
            return value.Value;
        }
        int Clinical(string field, int? value)
        {
            if (value is null)
            {
                fieldErrors.Add(new ApiFieldError(field, "missing", $"{field} must be scored before submitting."));
                return 0;
            }
            if (value < 0 || value > 3)
            {
                fieldErrors.Add(new ApiFieldError(field, "out_of_range", $"{field} must be between 0 and 3."));
            }
            return value.Value;
        }

        var scores = new OetScoring.SpeakingCriterionScores(
            Linguistic("intelligibility", intelligibility),
            Linguistic("fluency", fluency),
            Linguistic("appropriateness", appropriateness),
            Linguistic("grammarExpression", grammarExpression),
            Clinical("relationshipBuilding", relationshipBuilding),
            Clinical("patientPerspective", patientPerspective),
            Clinical("structure", structure),
            Clinical("informationGathering", informationGathering),
            Clinical("informationGiving", informationGiving));

        if (fieldErrors.Count > 0)
        {
            throw ApiException.Validation(
                "speaking_moderation_invalid_scores",
                "Score every criterion (0–6 for linguistic, 0–3 for clinical) before submitting.",
                fieldErrors);
        }
        return scores;
    }

    private SpeakingTutorAssessment CreateAssessmentRow(
        string sessionId,
        string markerId,
        string markerRole,
        OetScoring.SpeakingCriterionScores scores,
        string? overallFeedback,
        string[]? strengths,
        string[]? improvements,
        string[]? recommendedDrills,
        DateTimeOffset now)
    {
        var scaled = OetScoring.SpeakingProjectedScaled(scores);
        var band = OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBandFromScaled(scaled));
        return new SpeakingTutorAssessment
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakingSessionId = sessionId,
            TutorId = markerId,
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
            ReadinessBand = band,
            OverallFeedbackMarkdown = overallFeedback ?? string.Empty,
            StrengthsJson = SerialiseStringArray(strengths),
            ImprovementsJson = SerialiseStringArray(improvements),
            RecommendedDrillsJson = SerialiseStringArray(recommendedDrills),
            RecommendedRulebookEntries = string.Empty,
            IsFinal = true,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static OetScoring.SpeakingCriterionScores AverageScores(
        OetScoring.SpeakingCriterionScores a,
        OetScoring.SpeakingCriterionScores b)
    {
        static int Avg(int x, int y) => (int)Math.Round((x + y) / 2.0, MidpointRounding.AwayFromZero);
        return new OetScoring.SpeakingCriterionScores(
            Avg(a.Intelligibility, b.Intelligibility),
            Avg(a.Fluency, b.Fluency),
            Avg(a.Appropriateness, b.Appropriateness),
            Avg(a.GrammarExpression, b.GrammarExpression),
            Avg(a.RelationshipBuilding, b.RelationshipBuilding),
            Avg(a.PatientPerspective, b.PatientPerspective),
            Avg(a.Structure, b.Structure),
            Avg(a.InformationGathering, b.InformationGathering),
            Avg(a.InformationGiving, b.InformationGiving));
    }

    private static SpeakingCriterionScorePayload ScoreFrom(SpeakingTutorAssessment? row)
        => row is null
            ? null!
            : new SpeakingCriterionScorePayload(
                row.Intelligibility, row.Fluency, row.Appropriateness, row.GrammarExpression,
                row.RelationshipBuilding, row.PatientPerspective, row.Structure,
                row.InformationGathering, row.InformationGiving)
            {
                EstimatedScaledScore = row.EstimatedScaledScore,
            };

    private static OetScoring.SpeakingCriterionScores? ScoreFrom(SpeakingCriterionScorePayload? payload)
        => payload is null
            ? null
            : new OetScoring.SpeakingCriterionScores(
                payload.Intelligibility, payload.Fluency, payload.Appropriateness, payload.GrammarExpression,
                payload.RelationshipBuilding, payload.PatientPerspective, payload.Structure,
                payload.InformationGathering, payload.InformationGiving);

    private static string? SerialiseScore(SpeakingCriterionScorePayload? payload)
        => payload is null ? null : JsonSerializer.Serialize(payload, JsonOpts);

    private static SpeakingCriterionScorePayload? DeserialiseScore(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SpeakingCriterionScorePayload>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SerialiseStringArray(string[]? input)
        => input is null || input.Length == 0 ? "[]" : JsonSerializer.Serialize(input, JsonOpts);

    private static SpeakingModerationCaseProjection Project(SpeakingModerationCase c)
        => new(
            Id: c.Id,
            SessionId: c.SpeakingSessionId,
            Reason: c.Reason,
            Status: c.Status,
            FirstMarkerId: c.FirstMarkerId,
            FirstAssessmentId: c.FirstAssessmentId,
            FirstScore: DeserialiseScore(c.FirstScoreJson),
            SecondMarkerId: c.SecondMarkerId,
            SecondAssessmentId: c.SecondAssessmentId,
            SecondScore: DeserialiseScore(c.SecondScoreJson),
            ModeratorId: c.ModeratorId,
            FinalAssessmentId: c.FinalAssessmentId,
            FinalScore: DeserialiseScore(c.FinalScoreJson),
            VariancePoints: c.VariancePoints,
            VarianceReason: c.VarianceReason,
            FinalDecisionNote: c.FinalDecisionNote,
            RequestReattempt: c.RequestReattempt,
            CreatedAt: c.CreatedAt,
            UpdatedAt: c.UpdatedAt);
}
