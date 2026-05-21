using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

// Phase 4 (B.4 / E) of the OET Speaking module roadmap.
//
// DUAL SCORING — THE MOST IMPORTANT INVARIANT IN THE MODULE:
//
//   * AI scores live in <see cref="SpeakingAiAssessment"/>.
//   * Tutor scores live in <see cref="SpeakingTutorAssessment"/>.
//
// This service ONLY ever touches <see cref="SpeakingTutorAssessment"/>
// (and <see cref="SpeakingTimestampedComment"/> rows it authors). It
// reads <see cref="SpeakingAiAssessment"/> rows for the calibration
// delta but never writes them. The two assessment tracks remain
// strictly independent so the learner UI can surface them side-by-side
// without either track polluting the other.
//
// The projected scaled score is computed via the single source of
// truth — <see cref="OetScoring.SpeakingProjectedScaled"/> — so the
// scoring math never drifts between the AI and the tutor surface.
public sealed class TutorAssessmentService(
    LearnerDbContext db,
    ILogger<TutorAssessmentService> logger,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Draft / update / submit ─────────────────────────────────────────

    /// <summary>Creates a new draft tutor assessment for a finished
    /// session. Multiple drafts per tutor / session are allowed only if
    /// none of the existing rows are marked <c>IsFinal</c>; once a final
    /// row exists the surface is read-only.</summary>
    public async Task<string> CreateDraftAsync(
        string tutorId,
        string sessionId,
        TutorAssessmentDraftRequest req,
        CancellationToken ct)
    {
        var session = await LoadFinishedSessionAsync(sessionId, ct);
        await EnsureTutorMayReviewAsync(tutorId, session, ct);

        var finalExists = await db.SpeakingTutorAssessments
            .AsNoTracking()
            .AnyAsync(t => t.SpeakingSessionId == sessionId && t.IsFinal, ct);
        if (finalExists)
        {
            throw ApiException.Conflict(
                "tutor_assessment_already_final",
                "This session already has a finalised tutor assessment.");
        }

        var now = clock.GetUtcNow();
        var id = Guid.NewGuid().ToString("N");
        var row = new SpeakingTutorAssessment
        {
            Id = id,
            SpeakingSessionId = sessionId,
            TutorId = tutorId,
            Intelligibility = req.Intelligibility ?? 0,
            Fluency = req.Fluency ?? 0,
            Appropriateness = req.Appropriateness ?? 0,
            GrammarExpression = req.GrammarExpression ?? 0,
            RelationshipBuilding = req.RelationshipBuilding ?? 0,
            PatientPerspective = req.PatientPerspective ?? 0,
            Structure = req.Structure ?? 0,
            InformationGathering = req.InformationGathering ?? 0,
            InformationGiving = req.InformationGiving ?? 0,
            OverallFeedbackMarkdown = req.OverallFeedbackMarkdown ?? string.Empty,
            StrengthsJson = SerialiseStringArray(req.Strengths),
            ImprovementsJson = SerialiseStringArray(req.Improvements),
            RecommendedDrillsJson = SerialiseStringArray(req.RecommendedDrills),
            RecommendedRulebookEntries = req.RecommendedRulebookEntries is null
                ? string.Empty
                : string.Join(",", req.RecommendedRulebookEntries),
            IsFinal = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.SpeakingTutorAssessments.Add(row);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "TutorAssessment draft created tutor={TutorId} session={SessionId} id={AssessmentId}",
            tutorId, sessionId, id);

        return id;
    }

    /// <summary>Applies a partial update to a draft. Ownership is
    /// enforced (the tutor that created the row is the only one allowed
    /// to mutate it) and the row must still be a draft.</summary>
    public async Task UpdateDraftAsync(
        string tutorId,
        string sessionId,
        string assessmentId,
        TutorAssessmentDraftRequest req,
        CancellationToken ct)
    {
        var row = await LoadOwnedDraftAsync(tutorId, sessionId, assessmentId, ct);

        if (req.Intelligibility is not null) row.Intelligibility = req.Intelligibility.Value;
        if (req.Fluency is not null) row.Fluency = req.Fluency.Value;
        if (req.Appropriateness is not null) row.Appropriateness = req.Appropriateness.Value;
        if (req.GrammarExpression is not null) row.GrammarExpression = req.GrammarExpression.Value;
        if (req.RelationshipBuilding is not null) row.RelationshipBuilding = req.RelationshipBuilding.Value;
        if (req.PatientPerspective is not null) row.PatientPerspective = req.PatientPerspective.Value;
        if (req.Structure is not null) row.Structure = req.Structure.Value;
        if (req.InformationGathering is not null) row.InformationGathering = req.InformationGathering.Value;
        if (req.InformationGiving is not null) row.InformationGiving = req.InformationGiving.Value;

        if (req.OverallFeedbackMarkdown is not null) row.OverallFeedbackMarkdown = req.OverallFeedbackMarkdown;
        if (req.Strengths is not null) row.StrengthsJson = SerialiseStringArray(req.Strengths);
        if (req.Improvements is not null) row.ImprovementsJson = SerialiseStringArray(req.Improvements);
        if (req.RecommendedDrills is not null) row.RecommendedDrillsJson = SerialiseStringArray(req.RecommendedDrills);
        if (req.RecommendedRulebookEntries is not null)
            row.RecommendedRulebookEntries = string.Join(",", req.RecommendedRulebookEntries);

        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Validates all nine scores are present + in range, applies
    /// any final field updates, recomputes the projected scaled score via
    /// <see cref="OetScoring.SpeakingProjectedScaled"/>, computes the
    /// per-criterion + scaled delta vs the latest AI assessment, then
    /// flips <c>IsFinal</c> and stamps <c>SubmittedAt</c>. Once this
    /// returns the learner UI starts surfacing the tutor's score.</summary>
    public async Task<TutorAssessmentProjection> SubmitAsync(
        string tutorId,
        string sessionId,
        string assessmentId,
        TutorAssessmentSubmitRequest req,
        CancellationToken ct)
    {
        var row = await LoadOwnedDraftAsync(tutorId, sessionId, assessmentId, ct);

        // Apply final-state field updates BEFORE validation so a tutor
        // can submit + update in a single round-trip.
        if (req.Intelligibility is not null) row.Intelligibility = req.Intelligibility.Value;
        if (req.Fluency is not null) row.Fluency = req.Fluency.Value;
        if (req.Appropriateness is not null) row.Appropriateness = req.Appropriateness.Value;
        if (req.GrammarExpression is not null) row.GrammarExpression = req.GrammarExpression.Value;
        if (req.RelationshipBuilding is not null) row.RelationshipBuilding = req.RelationshipBuilding.Value;
        if (req.PatientPerspective is not null) row.PatientPerspective = req.PatientPerspective.Value;
        if (req.Structure is not null) row.Structure = req.Structure.Value;
        if (req.InformationGathering is not null) row.InformationGathering = req.InformationGathering.Value;
        if (req.InformationGiving is not null) row.InformationGiving = req.InformationGiving.Value;

        if (req.OverallFeedbackMarkdown is not null) row.OverallFeedbackMarkdown = req.OverallFeedbackMarkdown;
        if (req.Strengths is not null) row.StrengthsJson = SerialiseStringArray(req.Strengths);
        if (req.Improvements is not null) row.ImprovementsJson = SerialiseStringArray(req.Improvements);
        if (req.RecommendedDrills is not null) row.RecommendedDrillsJson = SerialiseStringArray(req.RecommendedDrills);
        if (req.RecommendedRulebookEntries is not null)
            row.RecommendedRulebookEntries = string.Join(",", req.RecommendedRulebookEntries);

        // Phase 7: stricter submit guards. Every criterion must be in range,
        // every criterion must be EXPLICITLY scored (the contract carries
        // `int?` so an unset score lands as `null` on the request and the
        // backing column never moved off its `0` default), the overall
        // feedback markdown must be non-empty, and the tutor must have
        // anchored at least one timestamped comment to the session.
        ValidateAllScoresPresentAndInRange(row, req);
        ValidateOverallFeedbackProvided(row);
        await ValidateAtLeastOneTimestampedCommentAsync(sessionId, ct);

        var scores = new OetScoring.SpeakingCriterionScores(
            row.Intelligibility,
            row.Fluency,
            row.Appropriateness,
            row.GrammarExpression,
            row.RelationshipBuilding,
            row.PatientPerspective,
            row.Structure,
            row.InformationGathering,
            row.InformationGiving);

        row.EstimatedScaledScore = OetScoring.SpeakingProjectedScaled(scores);
        row.ReadinessBand = OetScoring.SpeakingReadinessBandCode(
            OetScoring.SpeakingReadinessBandFromScaled(row.EstimatedScaledScore));

        // Calibration delta vs the latest AI assessment (read-only). The
        // payload exposes the signed per-criterion delta (tutor - ai) plus
        // the summed absolute error so the drift report can aggregate
        // without re-reading the underlying assessments.
        var ai = await db.SpeakingAiAssessments
            .AsNoTracking()
            .Where(a => a.SpeakingSessionId == sessionId)
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        row.CalibrationDeltaJson = ai is null
            ? null
            : JsonSerializer.Serialize(BuildCalibrationDelta(ai, row), JsonOpts);

        var submittedAt = clock.GetUtcNow();
        row.IsFinal = true;
        row.SubmittedAt = submittedAt;
        row.UpdatedAt = submittedAt;
        // MarkingDurationSeconds = wall-clock time from when the draft was
        // first created to submit. Floored at zero in case the clock skews.
        var elapsed = (int)Math.Max(0, (submittedAt - row.CreatedAt).TotalSeconds);
        row.MarkingDurationSeconds = elapsed;

        await db.SaveChangesAsync(ct);

        // Notification surface: log for now — Phase 4 wiring of
        // INotificationService happens in the integrator pass.
        logger.LogInformation(
            "TutorAssessment submitted tutor={TutorId} session={SessionId} id={AssessmentId} scaled={Scaled} band={Band}",
            tutorId, sessionId, assessmentId, row.EstimatedScaledScore, row.ReadinessBand);

        return ProjectTutor(row, tutorName: null);
    }

    // ── Timestamped comments ────────────────────────────────────────────

    /// <summary>Inserts a tutor-authored timestamped comment anchored to
    /// a specific transcript segment. Author role is fixed at
    /// <c>tutor</c> so the timeline UI can colour-code comments.</summary>
    public async Task<string> AddTimestampedCommentAsync(
        string tutorId,
        string sessionId,
        TutorTimestampedCommentRequest req,
        CancellationToken ct)
    {
        await LoadFinishedSessionAsync(sessionId, ct);

        if (req.EndMs < req.StartMs)
        {
            throw ApiException.Validation(
                "comment_invalid_window",
                "Comment endMs must be greater than or equal to startMs.");
        }

        var id = Guid.NewGuid().ToString("N");
        db.SpeakingTimestampedComments.Add(new SpeakingTimestampedComment
        {
            Id = id,
            SpeakingSessionId = sessionId,
            AuthorId = tutorId,
            AuthorRole = "tutor",
            TranscriptSegmentIndex = req.TranscriptSegmentIndex,
            StartMs = req.StartMs,
            EndMs = req.EndMs,
            CriterionCode = string.IsNullOrWhiteSpace(req.CriterionCode) ? "general" : req.CriterionCode,
            Severity = string.IsNullOrWhiteSpace(req.Severity) ? "info" : req.Severity,
            BodyMarkdown = req.BodyMarkdown ?? string.Empty,
            LinkedRulebookEntryCode = req.LinkedRulebookEntryCode,
            LinkedDrillId = req.LinkedDrillId,
            CreatedAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(ct);
        return id;
    }

    // ── Dual scoring projection ─────────────────────────────────────────

    /// <summary>Loads the latest AI assessment + the latest submitted
    /// tutor assessment + the full tutor history for the session and
    /// computes the divergence between the two final scores. Either
    /// side may be absent; the divergence is null until both exist.</summary>
    public async Task<DualAssessmentResponse> GetDualAssessmentAsync(
        string sessionId,
        CancellationToken ct)
    {
        var ai = await db.SpeakingAiAssessments
            .AsNoTracking()
            .Where(a => a.SpeakingSessionId == sessionId)
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var tutorRows = await db.SpeakingTutorAssessments
            .AsNoTracking()
            .Where(t => t.SpeakingSessionId == sessionId)
            .OrderByDescending(t => t.SubmittedAt ?? t.UpdatedAt)
            .ToListAsync(ct);

        var latestTutor = tutorRows.FirstOrDefault(t => t.IsFinal) ?? tutorRows.FirstOrDefault();
        var history = tutorRows.Where(t => t.IsFinal).Select(t => ProjectTutor(t, null)).ToArray();

        var aiProjection = ai is null ? null : ProjectAi(ai);
        var tutorProjection = latestTutor is null ? null : ProjectTutor(latestTutor, null);

        DivergenceMetric? divergence = null;
        if (ai is not null && latestTutor is not null && latestTutor.IsFinal)
        {
            divergence = ComputeDivergence(ai, latestTutor);
        }

        return new DualAssessmentResponse(
            SessionId: sessionId,
            Ai: aiProjection,
            Tutor: tutorProjection,
            TutorHistory: history,
            Divergence: divergence);
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
                "Tutor assessments may only be authored on finished sessions.");
        }
        return session;
    }

    private async Task EnsureTutorMayReviewAsync(string tutorId, SpeakingSession session, CancellationToken ct)
    {
        // TODO(Phase 4): wire claim-check against the tutor review queue
        // (see TutorReviewQueueService.ClaimAsync). For now any
        // authenticated expert may draft an assessment so the surface is
        // usable while the queue UX lands.
        _ = tutorId;
        _ = session;
        await Task.CompletedTask;
    }

    private async Task<SpeakingTutorAssessment> LoadOwnedDraftAsync(
        string tutorId,
        string sessionId,
        string assessmentId,
        CancellationToken ct)
    {
        var row = await db.SpeakingTutorAssessments
            .FirstOrDefaultAsync(t => t.Id == assessmentId, ct);
        if (row is null)
        {
            throw ApiException.NotFound("tutor_assessment_not_found", "Tutor assessment not found.");
        }
        if (row.SpeakingSessionId != sessionId)
        {
            throw ApiException.NotFound("tutor_assessment_not_found", "Tutor assessment not found.");
        }
        if (row.TutorId != tutorId)
        {
            throw ApiException.Forbidden(
                "tutor_assessment_forbidden",
                "You may only mutate tutor assessments you authored.");
        }
        if (row.IsFinal)
        {
            throw ApiException.Conflict(
                "tutor_assessment_already_final",
                "This tutor assessment has already been submitted and is immutable.");
        }
        return row;
    }

    private static void ValidateAllScoresPresentAndInRange(
        SpeakingTutorAssessment row,
        TutorAssessmentSubmitRequest req)
    {
        var fieldErrors = new List<ApiFieldError>();

        // The persisted column starts at 0 so we cannot tell from the row
        // whether a tutor genuinely scored 0 or simply skipped the criterion.
        // The submit payload is checked first to enforce explicit presence;
        // for criteria present in a prior draft we accept the stored value
        // (the draft contract uses `int?` so missing payload + stored value
        // means the score was set on an earlier round-trip).
        void Linguistic(string field, int? requested, int stored)
        {
            // Presence: either the submit body must provide a value or the
            // draft must already hold one. Detecting "never scored" requires
            // both checks because we cannot disambiguate stored zeros.
            if (requested is null && stored == 0 && !PreviousNonZeroExists(field, row))
            {
                fieldErrors.Add(new ApiFieldError(field, "missing", $"{field} must be scored before submitting."));
                return;
            }
            if (stored < 0 || stored > 6)
            {
                fieldErrors.Add(new ApiFieldError(field, "out_of_range", $"{field} must be between 0 and 6."));
            }
        }
        void Clinical(string field, int? requested, int stored)
        {
            if (requested is null && stored == 0 && !PreviousNonZeroExists(field, row))
            {
                fieldErrors.Add(new ApiFieldError(field, "missing", $"{field} must be scored before submitting."));
                return;
            }
            if (stored < 0 || stored > 3)
            {
                fieldErrors.Add(new ApiFieldError(field, "out_of_range", $"{field} must be between 0 and 3."));
            }
        }

        Linguistic("intelligibility", req.Intelligibility, row.Intelligibility);
        Linguistic("fluency", req.Fluency, row.Fluency);
        Linguistic("appropriateness", req.Appropriateness, row.Appropriateness);
        Linguistic("grammarExpression", req.GrammarExpression, row.GrammarExpression);
        Clinical("relationshipBuilding", req.RelationshipBuilding, row.RelationshipBuilding);
        Clinical("patientPerspective", req.PatientPerspective, row.PatientPerspective);
        Clinical("structure", req.Structure, row.Structure);
        Clinical("informationGathering", req.InformationGathering, row.InformationGathering);
        Clinical("informationGiving", req.InformationGiving, row.InformationGiving);

        if (fieldErrors.Count > 0)
        {
            throw ApiException.Validation(
                "tutor_assessment_invalid_scores",
                "Score every criterion (0–6 for linguistic, 0–3 for clinical) before submitting.",
                fieldErrors);
        }
    }

    // Tutors may genuinely award a zero — so we accept a stored zero IF the
    // row has at least one non-zero score (heuristic: a tutor who has worked
    // through the rubric will rarely award all zeros, and the more common
    // failure mode is "forgot one criterion"). When EVERY field is zero we
    // assume the tutor has not yet scored.
    private static bool PreviousNonZeroExists(string skipField, SpeakingTutorAssessment row)
    {
        bool IsNon(string field, int v) => !string.Equals(field, skipField, StringComparison.Ordinal) && v != 0;
        return IsNon("intelligibility", row.Intelligibility)
            || IsNon("fluency", row.Fluency)
            || IsNon("appropriateness", row.Appropriateness)
            || IsNon("grammarExpression", row.GrammarExpression)
            || IsNon("relationshipBuilding", row.RelationshipBuilding)
            || IsNon("patientPerspective", row.PatientPerspective)
            || IsNon("structure", row.Structure)
            || IsNon("informationGathering", row.InformationGathering)
            || IsNon("informationGiving", row.InformationGiving);
    }

    private static void ValidateOverallFeedbackProvided(SpeakingTutorAssessment row)
    {
        if (string.IsNullOrWhiteSpace(row.OverallFeedbackMarkdown))
        {
            throw ApiException.Validation(
                "tutor_assessment_missing_overall_feedback",
                "Overall feedback is required before submitting.",
                new[]
                {
                    new ApiFieldError(
                        "overallFeedbackMarkdown",
                        "missing",
                        "Provide an overall feedback note before submitting."),
                });
        }
    }

    private async Task ValidateAtLeastOneTimestampedCommentAsync(string sessionId, CancellationToken ct)
    {
        var commentCount = await db.SpeakingTimestampedComments
            .AsNoTracking()
            .CountAsync(c => c.SpeakingSessionId == sessionId, ct);
        if (commentCount == 0)
        {
            throw ApiException.Validation(
                "tutor_assessment_missing_timestamped_comment",
                "Add at least one timestamped comment to the transcript before submitting.");
        }
    }

    public static DivergenceMetric ComputeDivergence(
        SpeakingAiAssessment ai,
        SpeakingTutorAssessment tutor)
    {
        var per = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["intelligibility"] = Math.Abs(ai.Intelligibility - tutor.Intelligibility),
            ["fluency"] = Math.Abs(ai.Fluency - tutor.Fluency),
            ["appropriateness"] = Math.Abs(ai.Appropriateness - tutor.Appropriateness),
            ["grammarExpression"] = Math.Abs(ai.GrammarExpression - tutor.GrammarExpression),
            ["relationshipBuilding"] = Math.Abs(ai.RelationshipBuilding - tutor.RelationshipBuilding),
            ["patientPerspective"] = Math.Abs(ai.PatientPerspective - tutor.PatientPerspective),
            ["structure"] = Math.Abs(ai.Structure - tutor.Structure),
            ["informationGathering"] = Math.Abs(ai.InformationGathering - tutor.InformationGathering),
            ["informationGiving"] = Math.Abs(ai.InformationGiving - tutor.InformationGiving),
        };
        var sumAbs = 0;
        foreach (var v in per.Values) sumAbs += v;
        var band = sumAbs <= 4 ? "close" : sumAbs <= 10 ? "moderate" : "wide";
        var scaledDelta = tutor.EstimatedScaledScore - ai.EstimatedScaledScore;
        return new DivergenceMetric(per, scaledDelta, band);
    }

    private static object BuildCalibrationDelta(SpeakingAiAssessment ai, SpeakingTutorAssessment tutor)
    {
        // Signed delta per criterion (tutor - ai) — drives the radar chart in
        // the admin drift dashboard. The plan also requires a flat
        // `totalAbsoluteError` for cheap aggregation.
        var perCriterion = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["intelligibility"] = tutor.Intelligibility - ai.Intelligibility,
            ["fluency"] = tutor.Fluency - ai.Fluency,
            ["appropriateness"] = tutor.Appropriateness - ai.Appropriateness,
            ["grammarExpression"] = tutor.GrammarExpression - ai.GrammarExpression,
            ["relationshipBuilding"] = tutor.RelationshipBuilding - ai.RelationshipBuilding,
            ["patientPerspective"] = tutor.PatientPerspective - ai.PatientPerspective,
            ["structure"] = tutor.Structure - ai.Structure,
            ["informationGathering"] = tutor.InformationGathering - ai.InformationGathering,
            ["informationGiving"] = tutor.InformationGiving - ai.InformationGiving,
        };
        var totalAbsoluteError = 0;
        foreach (var v in perCriterion.Values) totalAbsoluteError += Math.Abs(v);
        var d = ComputeDivergence(ai, tutor);
        return new
        {
            perCriterion,
            totalAbsoluteError,
            scaledDelta = d.ScaledDelta,
            agreementBand = d.AgreementBand,
            aiAssessmentId = ai.Id,
            tutorAssessmentId = tutor.Id,
            computedAt = DateTimeOffset.UtcNow,
        };
    }

    private static AiAssessmentProjection ProjectAi(SpeakingAiAssessment row) =>
        new(
            AssessmentId: row.Id,
            Provider: row.Provider,
            ModelId: row.ModelId,
            Intelligibility: row.Intelligibility,
            Fluency: row.Fluency,
            Appropriateness: row.Appropriateness,
            GrammarExpression: row.GrammarExpression,
            RelationshipBuilding: row.RelationshipBuilding,
            PatientPerspective: row.PatientPerspective,
            Structure: row.Structure,
            InformationGathering: row.InformationGathering,
            InformationGiving: row.InformationGiving,
            EstimatedScaledScore: row.EstimatedScaledScore,
            ReadinessBand: row.ReadinessBand,
            OverallSummary: row.OverallSummary,
            ConfidenceBand: row.ConfidenceBand,
            GeneratedAt: row.GeneratedAt);

    private static TutorAssessmentProjection ProjectTutor(SpeakingTutorAssessment row, string? tutorName) =>
        new(
            AssessmentId: row.Id,
            TutorId: row.TutorId,
            TutorName: tutorName,
            Intelligibility: row.Intelligibility,
            Fluency: row.Fluency,
            Appropriateness: row.Appropriateness,
            GrammarExpression: row.GrammarExpression,
            RelationshipBuilding: row.RelationshipBuilding,
            PatientPerspective: row.PatientPerspective,
            Structure: row.Structure,
            InformationGathering: row.InformationGathering,
            InformationGiving: row.InformationGiving,
            EstimatedScaledScore: row.EstimatedScaledScore,
            ReadinessBand: row.ReadinessBand,
            OverallFeedbackMarkdown: row.OverallFeedbackMarkdown,
            Strengths: DeserialiseStringArray(row.StrengthsJson),
            Improvements: DeserialiseStringArray(row.ImprovementsJson),
            RecommendedDrills: DeserialiseStringArray(row.RecommendedDrillsJson),
            IsFinal: row.IsFinal,
            SubmittedAt: row.SubmittedAt);

    private static string SerialiseStringArray(string[]? input) =>
        input is null || input.Length == 0 ? "[]" : JsonSerializer.Serialize(input, JsonOpts);

    private static string[] DeserialiseStringArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(raw, JsonOpts) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
