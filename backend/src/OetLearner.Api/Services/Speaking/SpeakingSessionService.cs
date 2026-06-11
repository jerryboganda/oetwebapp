using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Phase 2 (B.3) of the OET Speaking module roadmap.
///
/// Owns the typed Speaking session lifecycle (prep → active → finished)
/// for both <c>ai_self_practice</c> / <c>ai_exam</c> and (eventually)
/// <c>live_tutor</c> modes. Every transition writes through the new
/// <see cref="SpeakingSession"/> table AND mirrors into a legacy
/// <see cref="Attempt"/> row so the existing learner history / analytics
/// pipelines keep working until Phase 4 retires the dual write.
///
/// The card surface emitted here is intentionally identical to
/// <c>LearnerService.SpeakingRolePlayCards.GetSpeakingRolePlayCardForLearnerAsync</c>
/// so there is one source of truth for the candidate-card schema. The
/// projection NEVER touches <see cref="InterlocutorScript"/>.
/// </summary>
public sealed class SpeakingSessionService(
    LearnerDbContext db,
    IAiPackageCreditService? aiPackageCreditService = null)
{
    private const string DefaultConsentVersion = "recording.v1";

    public async Task<CreateSpeakingSessionResponse> CreateSessionAsync(
        string userId,
        CreateSpeakingSessionRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("speaking_session_unauthenticated",
                "You must be signed in to start a Speaking session.");
        }
        if (req is null || string.IsNullOrWhiteSpace(req.RolePlayCardId))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_ID_REQUIRED",
                "Role-play card id is required.");
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == req.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        if (card.Status != ContentStatus.Published)
        {
            throw ApiException.Conflict("role_play_card_not_published",
                "That role-play card is not currently available for practice.");
        }

        var mode = SpeakingSessionModes.Parse(req.Mode);
        var consentVersion = string.IsNullOrWhiteSpace(req.ConsentVersion)
            ? DefaultConsentVersion
            : req.ConsentVersion!.Trim();

        var now = DateTimeOffset.UtcNow;
        var sessionId = $"sps_{Guid.NewGuid():N}";
        var attemptId = $"att_{Guid.NewGuid():N}";

        var attempt = new Attempt
        {
            Id = attemptId,
            UserId = userId,
            ContentId = card.ContentItemId,
            SubtestCode = "speaking",
            Context = "practice",
            Mode = SpeakingSessionModes.ToCode(mode),
            State = AttemptState.InProgress,
            StartedAt = now,
            CreatedAt = now,
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
        };

        // Sessions now open in the unscored WarmUp state. The learner UI
        // calls POST /start-warmup → /finish-warmup before transitioning
        // into the timed prep window. Live-tutor sessions skip warm-up
        // because the human interlocutor handles introductions in the
        // LiveKit room.
        var initialState = mode == SpeakingSessionMode.LiveTutor
            ? SpeakingSessionState.Prep
            : SpeakingSessionState.WarmUp;

        var session = new SpeakingSession
        {
            Id = sessionId,
            UserId = userId,
            RolePlayCardId = card.Id,
            MockSetId = string.IsNullOrWhiteSpace(req.MockSetId) ? null : req.MockSetId,
            Mode = mode,
            State = initialState,
            AttemptId = attemptId,
            PrepStartedAt = initialState == SpeakingSessionState.Prep ? now : null,
            ConsentVersion = consentVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Attempts.Add(attempt);
        db.SpeakingSessions.Add(session);
        await db.SaveChangesAsync(ct);

        // Until warm-up finishes the prep window has not started, so the
        // computed deadlines are forward-looking — the frontend can use
        // them as soft hints, then re-fetch the session after the
        // /finish-warmup call to get authoritative timestamps.
        var prepStartedAt = session.PrepStartedAt ?? now;
        var prepEndsAt = prepStartedAt.AddSeconds(card.PrepTimeSeconds);
        var rolePlayEndsAt = prepEndsAt.AddSeconds(card.RolePlayTimeSeconds);

        return new CreateSpeakingSessionResponse(
            SessionId: sessionId,
            PrepStartedAt: prepStartedAt,
            PrepEndsAt: prepEndsAt,
            RolePlayEndsAt: rolePlayEndsAt,
            ConsentVersion: consentVersion,
            Card: ProjectLearnerCard(card));
    }

    // ─────────────────────────────────────────────────────────────────
    // Warm-up transitions (Phase 3)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks the warm-up window as started. Idempotent — calling twice
    /// just refreshes <see cref="SpeakingSession.WarmupStartedAt"/>
    /// without resetting the state machine. Rejects sessions that have
    /// already left the warm-up state.
    /// </summary>
    public async Task<SpeakingSessionDetail> StartWarmupAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        if (session.State != SpeakingSessionState.WarmUp)
        {
            throw ApiException.Conflict("speaking_session_invalid_state",
                $"Warm-up cannot start in state '{SpeakingSessionStates.ToCode(session.State)}'.");
        }

        var now = DateTimeOffset.UtcNow;
        session.WarmupStartedAt ??= now;
        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    /// <summary>
    /// Transitions the session from <c>WarmUp</c> into <c>Prep</c>. This is
    /// the only authorised path out of warm-up — clients cannot skip
    /// straight to <c>Active</c>. Stamps both the warm-up end and the
    /// prep start so the analytics layer can measure warm-up duration.
    /// </summary>
    public async Task<SpeakingSessionDetail> FinishWarmupAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        if (session.State != SpeakingSessionState.WarmUp)
        {
            throw ApiException.Conflict("speaking_session_invalid_state",
                $"Warm-up can only finish from the warm-up state (current: {SpeakingSessionStates.ToCode(session.State)}).");
        }

        var now = DateTimeOffset.UtcNow;

        // Speaking module rebuild (2026-06-11): AI self-practice charges exactly
        // ONE speaking package credit per card, taken here at CARD REVEAL (prep
        // start, right after warm-up). Idempotent on the session reference, so a
        // retried finish-warmup never double-charges. Live-tutor practice is
        // pay-per-session (no credit) and AI-exam cards are charged by
        // SpeakingExamService, so only AiSelfPractice debits here.
        if (session.Mode == SpeakingSessionMode.AiSelfPractice && aiPackageCreditService is not null)
        {
            var debit = await aiPackageCreditService.DeductGradingCreditAsync(
                userId, "speaking", $"practice:{session.Id}", ct);
            if (!debit.Debited
                && !string.Equals(debit.ErrorCode, "already_debited", StringComparison.Ordinal))
            {
                throw ApiException.PaymentRequired(
                    debit.ErrorCode ?? "no_ai_package_credits",
                    debit.ErrorMessage ?? "You have no credits remaining. Purchase a package to continue.");
            }
        }

        session.WarmupEndedAt = now;
        session.State = SpeakingSessionState.Prep;
        session.PrepStartedAt = now;
        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    public async Task<SpeakingSessionDetail> GetSessionForLearnerAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct);
        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == session.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        return new SpeakingSessionDetail(
            SessionId: session.Id,
            Mode: SpeakingSessionModes.ToCode(session.Mode),
            State: SpeakingSessionStates.ToCode(session.State),
            RolePlayCardId: session.RolePlayCardId,
            WarmupStartedAt: session.WarmupStartedAt,
            WarmupEndedAt: session.WarmupEndedAt,
            PrepStartedAt: session.PrepStartedAt,
            RolePlayStartedAt: session.RolePlayStartedAt,
            EndedAt: session.EndedAt,
            SubmittedAt: session.SubmittedAt,
            ElapsedSeconds: session.ElapsedSeconds,
            ConsentVersion: session.ConsentVersion,
            Card: ProjectLearnerCard(card));
    }

    public async Task<SpeakingSessionDetail> StartRolePlayAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);

        // Strict state-machine: role-play only starts from Prep. Clients
        // still sitting in WarmUp must call /finish-warmup first so the
        // unscored warm-up audio is properly demarcated from the timed
        // assessment window. This prevents skip-attacks that would let a
        // learner avoid the warm-up loop entirely.
        if (session.State == SpeakingSessionState.WarmUp)
        {
            throw ApiException.Conflict("speaking_session_warmup_not_finished",
                "Finish the warm-up conversation before starting the role-play.");
        }
        if (session.State != SpeakingSessionState.Prep)
        {
            throw ApiException.Conflict("speaking_session_invalid_state",
                $"Role-play can only start from the prep state (current: {SpeakingSessionStates.ToCode(session.State)}).");
        }

        var now = DateTimeOffset.UtcNow;
        session.State = SpeakingSessionState.Active;
        session.RolePlayStartedAt = now;
        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    public async Task<SpeakingSessionDetail> EndSessionAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        if (session.State != SpeakingSessionState.Active)
        {
            throw ApiException.Conflict("speaking_session_invalid_state",
                $"Only an active session can be ended (current: {SpeakingSessionStates.ToCode(session.State)}).");
        }

        var now = DateTimeOffset.UtcNow;
        session.State = SpeakingSessionState.Finished;
        session.EndedAt = now;
        session.UpdatedAt = now;
        var roleplayStart = session.RolePlayStartedAt ?? now;
        session.ElapsedSeconds = (int)Math.Max(0, (now - roleplayStart).TotalSeconds);

        // Mirror to legacy Attempt so existing dashboards and history
        // surfaces continue to see the completed attempt.
        if (!string.IsNullOrWhiteSpace(session.AttemptId))
        {
            var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == session.AttemptId, ct);
            if (attempt is not null)
            {
                attempt.State = AttemptState.Submitted;
                attempt.SubmittedAt = now;
                attempt.ElapsedSeconds = session.ElapsedSeconds;
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    /// <summary>
    /// WS4 — submit-for-marking gate (§14.2). The learner explicitly commits
    /// the finished role-play for official assessment. Guards:
    ///   * the session must already be <c>Finished</c> (role-play ended), and
    ///   * assessable role-play evidence must exist — at least one
    ///     non-warm-up recording OR a non-warm-up transcript with content.
    /// Without recorded/transcribed role-play audio there is nothing for an
    /// assessor to mark, so the gate refuses to stamp <see cref="SpeakingSession.SubmittedAt"/>.
    /// Idempotent: re-submitting a session that already carries a
    /// <c>SubmittedAt</c> simply returns the current detail.
    /// </summary>
    public async Task<SpeakingSessionDetail> SubmitForMarkingAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);

        // Idempotent: already submitted → no-op.
        if (session.SubmittedAt is not null)
        {
            return await GetSessionForLearnerAsync(userId, sessionId, ct);
        }

        if (session.State != SpeakingSessionState.Finished)
        {
            throw ApiException.Conflict("speaking_session_not_finished",
                $"End the role-play before submitting for marking (current: {SpeakingSessionStates.ToCode(session.State)}).");
        }

        // Evidence gate: an assessor needs real role-play audio/transcript.
        var hasRecording = await db.SpeakingRecordings.AsNoTracking()
            .AnyAsync(r => r.SpeakingSessionId == sessionId && !r.IsWarmup && !r.IsArchived, ct);
        var hasTranscript = await db.SpeakingTranscripts.AsNoTracking()
            .AnyAsync(t => t.SpeakingSessionId == sessionId && t.WordCount > 0, ct);

        if (!hasRecording && !hasTranscript)
        {
            throw ApiException.Conflict("speaking_session_no_recording",
                "There is no recorded role-play to submit for marking. Complete the role-play first.");
        }

        var now = DateTimeOffset.UtcNow;
        session.SubmittedAt = now;
        session.UpdatedAt = now;

        // Mirror to the legacy Attempt so existing marking queues see the
        // submission timestamp even if /end ran before recordings landed.
        if (!string.IsNullOrWhiteSpace(session.AttemptId))
        {
            var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == session.AttemptId, ct);
            if (attempt is not null)
            {
                attempt.State = AttemptState.Submitted;
                attempt.SubmittedAt ??= now;
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    public async Task<SpeakingSessionDetail> MarkConsentAsync(
        string userId,
        string sessionId,
        string consentVersion,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(consentVersion))
        {
            throw ApiException.Validation("CONSENT_VERSION_REQUIRED",
                "Consent version is required.");
        }

        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        var now = DateTimeOffset.UtcNow;
        session.ConsentVersion = consentVersion.Trim();
        session.ConsentAcceptedAt = now;
        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    // ─────────────────────────────────────────────────────────────────
    // WS1 — server-authoritative clock & technical-issue reporting
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the authoritative session clock entirely server-side from
    /// persisted timestamps plus the card's prep/role-play windows. The
    /// client never supplies "seconds remaining"; on reconnect it simply
    /// re-reads this endpoint. Terminal states (finished/cancelled/expired)
    /// report no deadline. When a timed stage's deadline has passed the
    /// response carries <c>Expired=true</c> and <c>SecondsRemaining=0</c>,
    /// but the persisted state is NOT mutated here (read-only): the strict
    /// transition endpoints remain the only writers.
    /// </summary>
    public async Task<SpeakingSessionClock> GetClockAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct);
        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == session.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        var now = DateTimeOffset.UtcNow;
        // Strict-mock defaults are 180s prep + 300s role-play; the card is
        // authoritative and the publish gate enforces those values for exam
        // content, so we read straight off the card.
        var prepSeconds = card.PrepTimeSeconds > 0 ? card.PrepTimeSeconds : 180;
        var rolePlaySeconds = card.RolePlayTimeSeconds > 0 ? card.RolePlayTimeSeconds : 300;

        string stage;
        DateTimeOffset? stageStartedAt = null;
        DateTimeOffset? stageEndsAt = null;
        string[] canAdvanceTo;

        switch (session.State)
        {
            case SpeakingSessionState.WarmUp:
                stage = "warmup";
                stageStartedAt = session.WarmupStartedAt;
                canAdvanceTo = ["prep"]; // via /finish-warmup
                break;
            case SpeakingSessionState.Prep:
                stage = "prep";
                stageStartedAt = session.PrepStartedAt;
                stageEndsAt = stageStartedAt?.AddSeconds(prepSeconds);
                canAdvanceTo = ["active"]; // via /start-roleplay
                break;
            case SpeakingSessionState.Active:
                stage = "active";
                stageStartedAt = session.RolePlayStartedAt;
                stageEndsAt = stageStartedAt?.AddSeconds(rolePlaySeconds);
                canAdvanceTo = ["finished"]; // via /end
                break;
            case SpeakingSessionState.Finished:
                stage = "finished";
                stageStartedAt = session.RolePlayStartedAt;
                stageEndsAt = session.EndedAt;
                canAdvanceTo = [];
                break;
            case SpeakingSessionState.Cancelled:
                stage = "cancelled";
                canAdvanceTo = [];
                break;
            case SpeakingSessionState.Expired:
                stage = "expired";
                canAdvanceTo = [];
                break;
            default:
                stage = SpeakingSessionStates.ToCode(session.State);
                canAdvanceTo = [];
                break;
        }

        int? secondsRemaining = null;
        var expired = false;
        if (stageEndsAt is { } ends && session.State is SpeakingSessionState.Prep or SpeakingSessionState.Active)
        {
            var remaining = (int)Math.Floor((ends - now).TotalSeconds);
            secondsRemaining = Math.Max(0, remaining);
            expired = remaining <= 0;
        }

        return new SpeakingSessionClock(
            Stage: stage,
            RoleplayIndex: session.MockSessionId is null ? 1 : ResolveRoleplayIndex(session),
            ServerNow: now,
            StageStartedAt: stageStartedAt,
            StageEndsAt: stageEndsAt,
            SecondsRemaining: secondsRemaining,
            Expired: expired,
            CanAdvanceTo: canAdvanceTo);
    }

    /// <summary>
    /// Flags a technical issue on the session (§22.5). Never alters scoring
    /// or the state machine; only records the flag + optional note for the
    /// assessor console and the analytics technical-issue rate.
    /// </summary>
    public async Task<SpeakingSessionDetail> ReportTechnicalIssueAsync(
        string userId,
        string sessionId,
        string? note,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        var now = DateTimeOffset.UtcNow;
        session.TechnicalIssueFlag = true;
        var trimmed = note?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            session.TechnicalIssueNote = trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
        }
        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    /// <summary>
    /// Resolves which half of a two-role-play mock is currently live from the
    /// per-role-play timestamps. Defaults to 1 until RP2 prep/active begins.
    /// </summary>
    private static int ResolveRoleplayIndex(SpeakingSession session)
        => session.Rp2PrepStartedAt is not null || session.Rp2StartedAt is not null ? 2 : 1;

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private async Task<SpeakingSession> LoadOwnedSessionAsync(
        string userId,
        string sessionId,
        CancellationToken ct,
        bool tracking = false)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("speaking_session_unauthenticated",
                "You must be signed in to interact with a Speaking session.");
        }
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw ApiException.Validation("SPEAKING_SESSION_ID_REQUIRED",
                "Speaking session id is required.");
        }

        var q = tracking
            ? db.SpeakingSessions
            : db.SpeakingSessions.AsNoTracking();
        var session = await q.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw ApiException.NotFound("speaking_session_not_found",
                "That Speaking session does not exist.");

        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            // Use NotFound rather than Forbidden so we do not leak which
            // session ids exist for other users (IDOR guard).
            throw ApiException.NotFound("speaking_session_not_found",
                "That Speaking session does not exist.");
        }

        return session;
    }

    /// <summary>
    /// Learner-safe card projection. Mirrors
    /// <c>LearnerService.SpeakingRolePlayCards.GetSpeakingRolePlayCardForLearnerAsync</c>
    /// exactly so both endpoints share one wire shape. NEVER serialises
    /// any field from <see cref="InterlocutorScript"/>.
    /// </summary>
    public static object ProjectLearnerCard(RolePlayCard card)
    {
        var tasks = new[] { card.Task1, card.Task2, card.Task3, card.Task4, card.Task5 }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .ToArray();

        var criteriaFocus = AdminService.DeserializeCriteriaFocus(card.CriteriaFocusJson);

        return new
        {
            cardId = card.Id,
            professionId = card.ProfessionId,
            scenarioTitle = card.ScenarioTitle,
            setting = card.Setting,
            candidateRole = card.CandidateRole,
            interlocutorRole = card.InterlocutorRole,
            patientName = card.PatientName,
            patientAge = card.PatientAge,
            background = card.Background,
            tasks,
            allowedNotes = card.AllowedNotes,
            prepTimeSeconds = card.PrepTimeSeconds,
            rolePlayTimeSeconds = card.RolePlayTimeSeconds,
            patientEmotion = card.PatientEmotion,
            communicationGoal = card.CommunicationGoal,
            clinicalTopic = card.ClinicalTopic,
            difficulty = card.Difficulty,
            criteriaFocus,
            disclaimer = card.Disclaimer,
            // Speaking module rebuild (2026-06-11). Safe candidate-card fields.
            // NOTE: CardTypeId is deliberately omitted — it is hidden from
            // students. DisplayCardNumber is the number printed on the card face.
            displayCardNumber = card.DisplayCardNumber,
        };
    }
}
