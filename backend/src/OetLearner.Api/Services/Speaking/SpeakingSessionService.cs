using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.FreeSamples;

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
    IAiPackageCreditService? aiPackageCreditService = null,
    IEffectiveEntitlementResolver? entitlementResolver = null,
    SpeakingSimulationV11PersonaService? personaService = null,
    OetLearner.Api.Services.Ai.IAiCreditReservationService? creditReservations = null,
    ISpeakingCanonicalAssessmentService? canonical = null,
    SpeakingComplianceService? compliance = null,
    LiveVoiceProviderProbeState? liveVoiceProbe = null,
    IOptions<LiveVoiceOptions>? liveVoiceOptions = null,
    SpeakingLiveAdmissionService? admission = null)
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
            .FirstOrDefaultAsync(x => x.Id == req.RolePlayCardId || x.ContentItemId == req.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        // Profession lock (23 Sep 2026): another profession's card (or a
        // learner with no profession) is "not found", before any card payload.
        // The designated free card is own-profession by construction.
        await LearnerProfessionGuard.RequireRolePlayCardAsync(db, userId, card.Id,
            "role_play_card_not_found", "That role-play card does not exist.", ct);

        if (card.Status != ContentStatus.Published)
        {
            throw ApiException.Conflict("role_play_card_not_published",
                "That role-play card is not currently available for practice.");
        }

        var mode = SpeakingSessionModes.Parse(req.Mode);

        // An AI exam card is created by its exam (SpeakingExamService), which also takes its credit hold.
        // Accepting the mode here opened a billed live-voice card with no exam and no hold.
        if (mode == SpeakingSessionMode.AiExam)
        {
            throw ApiException.Conflict("speaking_session_exam_managed", "AI exam cards are created by their exam.");
        }

        var consentVersion = string.IsNullOrWhiteSpace(req.ConsentVersion)
            ? DefaultConsentVersion
            : req.ConsentVersion!.Trim();

        var now = DateTimeOffset.UtcNow;
        var sessionId = $"sps_{Guid.NewGuid():N}";
        var attemptId = $"att_{Guid.NewGuid():N}";

        // Free sample retry addendum (owner 23 Sep 2026): the learner's pinned
        // free Speaking card runs on this shared engine. The server alone decides
        // (no client flag): a practice session on the offered card is bound as a
        // free use — restarting rebinds, and only a produced result counts.
        var freeSamples = new FreeSampleService(db);
        var isFreeSample = mode == SpeakingSessionMode.AiSelfPractice
            && string.IsNullOrWhiteSpace(req.MockSetId)
            && await freeSamples.IsOfferedAsync(userId, FreeSampleService.Speaking, card.Id, ct);
        if (isFreeSample
            && !await freeSamples.TryClaimAsync(
                userId, FreeSampleService.Speaking, card.Id, FreeSampleUse.KindSpeakingSession, sessionId, ct))
        {
            // Lost a race for the last free slot (second tab) — never silently paid.
            throw ApiException.Conflict("free_sample_unavailable", "Your free Speaking sample is no longer available.");
        }

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
        // Capture the immutable v1.1 persona at card reveal. The script
        // existence check keeps legacy unconfigured cards on their existing
        // path and never manufactures hidden persona facts.
        if (await db.InterlocutorScripts.AsNoTracking()
            .AnyAsync(x => x.RolePlayCardId == card.Id, ct))
        {
            var runtimePersona = personaService ?? new SpeakingSimulationV11PersonaService(db);
            await runtimePersona.CaptureAtRevealAsync(null, session, card, now, ct);
        }
        await db.SaveChangesAsync(ct);

        // Until warm-up finishes the prep window has not started, so the
        // computed deadlines are forward-looking — the frontend can use
        // them as soft hints, then re-fetch the session after the
        // /finish-warmup call to get authoritative timestamps.
        var prepStartedAt = session.PrepStartedAt ?? now;
        var prepEndsAt = prepStartedAt.AddSeconds(card.PrepTimeSeconds);
        var rolePlayEndsAt = prepEndsAt.AddSeconds(
            SpeakingRolePlayLimits.EffectiveSeconds(card.RolePlayTimeSeconds, liveVoiceOptions?.Value));

        return new CreateSpeakingSessionResponse(
            SessionId: sessionId,
            PrepStartedAt: prepStartedAt,
            PrepEndsAt: prepEndsAt,
            RolePlayEndsAt: rolePlayEndsAt,
            ConsentVersion: consentVersion,
            Card: ProjectLearnerCard(card),
            IsFreeSample: isFreeSample,
            ConsentAccepted: false,
            LiveVoiceAvailable: IsLiveVoiceAvailable());
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
    /// An AI practice card first passes the live-session admission gate: while the
    /// cap is full the session stays in <c>warmup</c>, nothing is held or timed, and
    /// the detail carries <c>Admission</c> (the page repeats this call).
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

        // "Practice Card Access": a plan can disable ai_self_practice
        // outright (see BillingPlan.SpeakingPracticeAccessEnabled). Only
        // takes effect for a resolved, eligible subscription that explicitly
        // disables it — no-subscription/free accounts (today's a-la-carte
        // credit buyers) are never blocked by this check.
        if (session.Mode == SpeakingSessionMode.AiSelfPractice && entitlementResolver is not null)
        {
            var snapshot = await entitlementResolver.ResolveAsync(userId, ct);
            if (snapshot.HasEligibleSubscription && !snapshot.SpeakingPracticeAccessEnabled)
            {
                throw ApiException.Forbidden(
                    "speaking_practice_not_included",
                    "Speaking practice cards are not included in your current plan.");
            }
        }

        // Live AI capacity gate (owner decision 5 Oct 2026): AFTER the plan check (a learner whose plan
        // excludes practice never queues) and BEFORE the credit hold and the prep clock, so a learner who has
        // to wait has paid nothing and started nothing. The session is untouched while waiting (still
        // warm-up); the page repeats this call until a place is free. A free-sample card uses live voice too,
        // so it is gated like any other. With no healthy live provider the learner uses the recorder
        // fallback and the gate does not apply. A learner who cannot fund the hold is refused (402) by the
        // gate's pre-check BEFORE taking a place or a line position (it runs only when this call is about to
        // take a place, never on a waiting learner's repeat poll).
        SpeakingLiveAdmissionResult? gate = null;
        if (session.Mode == SpeakingSessionMode.AiSelfPractice && admission is not null)
        {
            gate = await admission.AdmitOrQueueAsync(
                userId,
                SpeakingLiveAdmissionKinds.Practice,
                session.Id,
                IsLiveVoiceAvailable(),
                ct,
                beforeNewPlace: async token =>
                {
                    if (!await new FreeSampleService(db).IsFreeAttemptAsync(userId, FreeSampleService.Speaking, session.Id, token))
                    {
                        await EnsurePracticeFundableAsync(userId, session.Id, token);
                    }
                });
            if (gate.MustWait)
            {
                return await GetSessionForLearnerAsync(userId, sessionId, ct, waitingView: gate.Waiting);
            }
        }

        // Speaking module rebuild (2026-06-11): AI self-practice charges exactly
        // 2 AI credits per card (FINAL 2026-09-06: 1 card = 2 credits), taken here at CARD REVEAL (prep
        // start, right after warm-up). Idempotent on the session reference, so a
        // retried finish-warmup never double-charges. Live-tutor practice is
        // pay-per-session (no credit) and AI-exam cards are charged by
        // SpeakingExamService, so only AiSelfPractice debits here.
        string? feedbackMessage = null;
        try
        {
            // A bound free-sample use holds no credits (free = 0 AI credits).
            if (session.Mode == SpeakingSessionMode.AiSelfPractice
                && !await new FreeSampleService(db).IsFreeAttemptAsync(userId, FreeSampleService.Speaking, session.Id, ct))
            {
                var refId = $"practice:{session.Id}";
                if (creditReservations is not null)
                {
                    var operationId = Guid.NewGuid().ToString("N");
                    await creditReservations.ReserveSpeakingAsync(userId, operationId, refId, ct);
                }
                else if (aiPackageCreditService is not null)
                {
                    var debit = await aiPackageCreditService.DeductGradingCreditAsync(
                        userId, "speaking", refId, ct);
                    if (!debit.Debited
                        && !string.Equals(debit.ErrorCode, "already_debited", StringComparison.Ordinal))
                    {
                        throw ApiException.PaymentRequired(
                            debit.ErrorCode ?? "no_ai_package_credits",
                            debit.ErrorMessage ?? "You do not have enough credits to start this activity. Please purchase another package or upgrade your plan.");
                    }

                    feedbackMessage = debit.FeedbackMessage;
                }
            }

            session.WarmupEndedAt = now;
            session.State = SpeakingSessionState.Prep;
            session.PrepStartedAt = now;
            session.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        catch when (gate is { TookNewPlace: true })
        {
            // The start failed AFTER this call took a place (a refused or failed credit hold): give the place back at
            // once instead of leaving it idle for the claim window. Never touches a running session; never throws.
            if (admission is not null)
            {
                await admission.ReleaseAsync(SpeakingLiveAdmissionKinds.Practice, session.Id, CancellationToken.None);
            }
            throw;
        }

        return await GetSessionForLearnerAsync(userId, sessionId, ct, feedbackMessage);
    }

    /// <summary>
    /// Read-only: refuses (402) a practice card whose 2-credit hold would be refused, mirroring the hold itself
    /// (<c>AiCreditReservationService.ReserveSpeakingAsync</c> when reservations are wired, as in production, else the
    /// package ledger's own read-only check) so the live-session gate never queues or admits a learner who can only
    /// fail at the hold. A reference that already holds its credit (a retry) is funded.
    /// </summary>
    private async Task EnsurePracticeFundableAsync(string userId, string sessionId, CancellationToken ct)
    {
        if (aiPackageCreditService is null)
        {
            return;
        }

        var refId = $"practice:{sessionId}";
        if (creditReservations is not null)
        {
            if (await db.AiCreditReservations.AsNoTracking().AnyAsync(r => r.BusinessReference == refId, ct))
            {
                return;
            }

            var snapshot = await aiPackageCreditService.GetSnapshotAsync(userId, 0, ct);
            if (snapshot.SpeakingUnlimited)
            {
                return;
            }

            if (snapshot.ExpiredBecausePassed
                || (snapshot.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
                || !snapshot.HasSpeakingActivity)
            {
                throw ApiException.PaymentRequired(
                    "ai_credits_insufficient",
                    "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
            }

            return;
        }

        if (await aiPackageCreditService.FindGradingDebitAsync(userId, refId, ct) is not null)
        {
            return;
        }

        var check = await aiPackageCreditService.CheckGradingCreditAsync(userId, "speaking", ct);
        if (!check.Debited)
        {
            throw ApiException.PaymentRequired(
                check.ErrorCode ?? "no_ai_package_credits",
                check.ErrorMessage ?? "You do not have enough credits to start this activity. Please purchase another package or upgrade your plan.");
        }
    }

    /// <summary>
    /// The learner left the admission line ("Leave the queue"): releases this card's WAITING place at once so it stops
    /// counting towards the positions of everyone behind it. Owner-checked; a card that is not waiting is a no-op.
    /// </summary>
    public async Task LeaveAdmissionQueueAsync(string userId, string sessionId, CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct);
        if (admission is not null)
        {
            await admission.LeaveQueueAsync(SpeakingLiveAdmissionKinds.Practice, session.Id, ct);
        }
    }

    public async Task<SpeakingSessionDetail> GetSessionForLearnerAsync(
        string userId,
        string sessionId,
        CancellationToken ct,
        string? feedbackMessage = null,
        SpeakingLiveAdmissionView? waitingView = null)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct);
        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == session.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        // The line a waiting AI practice card is in (state stays warmup). Passed in by the finish-warmup call
        // that has just decided it; on a plain read it is looked up. Null whenever the card is not waiting.
        var admissionView = waitingView;
        if (admissionView is null
            && admission is not null
            && session.State == SpeakingSessionState.WarmUp
            && session.Mode == SpeakingSessionMode.AiSelfPractice)
        {
            admissionView = await admission.GetWaitingViewAsync(SpeakingLiveAdmissionKinds.Practice, session.Id, ct);
        }

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
            Card: ProjectLearnerCard(card),
            FeedbackMessage: feedbackMessage,
            IsFreeSample: await new FreeSampleService(db).IsFreeAttemptAsync(userId, FreeSampleService.Speaking, session.Id, ct),
            ConsentAccepted: session.ConsentAcceptedAt is not null,
            LiveVoiceAvailable: IsLiveVoiceAvailable(),
            RolePlayEndsAt: session.RolePlayStartedAt?.AddSeconds(
                SpeakingRolePlayLimits.EffectiveSeconds(card.RolePlayTimeSeconds, liveVoiceOptions?.Value)),
            Admission: admissionView);
    }

    private bool IsLiveVoiceAvailable()
        => liveVoiceProbe?.IsLiveVoiceAvailable(liveVoiceOptions?.Value) == true;

    /// <summary>An exam card runs on the exam's own clock (prep, start and end are derived from the
    /// exam's persisted timestamps), so the standalone start and end endpoints must not drive it:
    /// starting one early would open a billed provider session during prep.</summary>
    private static void RequireStandalone(SpeakingSession session)
    {
        if (session.ExamSessionId is not null)
        {
            throw ApiException.Conflict("speaking_session_exam_managed",
                "This Speaking card is timed by its exam and cannot be started or ended separately.");
        }
    }

    public async Task<SpeakingSessionDetail> StartRolePlayAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        RequireStandalone(session);

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
        // The practice credit hold taken at finish-warmup is committed only
        // when the card is GRADED (SpeakingCanonicalAssessmentService via
        // SpeakingCreditSettlement) and refunded if it never is.
        await db.SaveChangesAsync(ct);

        return await GetSessionForLearnerAsync(userId, sessionId, ct);
    }

    public async Task<SpeakingSessionDetail> EndSessionAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var session = await LoadOwnedSessionAsync(userId, sessionId, ct, tracking: true);
        RequireStandalone(session);
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
        if (canonical is not null)
        {
            await canonical.EnqueueAsync(session.Id, ct);
        }

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
                "There is nothing to submit for marking yet. Complete the role-play first.");
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
        // The server's version is the truth (a client may still send an old literal).
        session.ConsentVersion = compliance?.ResolveCurrentConsentVersion(SpeakingComplianceConsentTypes.Recording)
            ?? consentVersion.Trim();
        session.ConsentAcceptedAt = now;
        session.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        // One client call covers the account-level Recording + AI-processing
        // + Retention consents the realtime voice and recorder paths require,
        // so no consent prompt can appear inside a timed screen.
        if (compliance is not null)
        {
            await compliance.EnsureSessionConsentsAsync(userId, ct);
        }

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
        var rolePlaySeconds = SpeakingRolePlayLimits.EffectiveSeconds(card.RolePlayTimeSeconds, liveVoiceOptions?.Value);

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

        // When the server force-ends an unfinished role-play (deadline plus grace). Only an Active
        // session can still be force-ended; a session with no start time has no clock yet.
        DateTimeOffset? hardStopAt = session.State == SpeakingSessionState.Active && stageStartedAt is { } started
            ? started.AddSeconds(rolePlaySeconds + SpeakingRolePlayLimits.GraceSeconds(liveVoiceOptions?.Value))
            : null;

        return new SpeakingSessionClock(
            Stage: stage,
            RoleplayIndex: session.MockSessionId is null ? 1 : ResolveRoleplayIndex(session),
            ServerNow: now,
            StageStartedAt: stageStartedAt,
            StageEndsAt: stageEndsAt,
            SecondsRemaining: secondsRemaining,
            Expired: expired,
            CanAdvanceTo: canAdvanceTo,
            HardStopAt: hardStopAt);
    }

    /// <summary>
    /// Server-side hard stop of an AI role-play the client never ended (closed tab, dead socket, a
    /// hostile client): once <c>now</c> is past the hard stop the session is finished as the learner's
    /// own /end would finish it (state, end time anchored to the deadline, elapsed seconds, legacy
    /// attempt) plus one <c>SpeakingRolePlayHardStopped</c> audit event. Only an exam card is handed
    /// to canonical grading (its exam clock grades it anyway). An abandoned standalone practice
    /// role-play is NOT graded, so it neither commits its 2-credit hold nor consumes a free-sample use
    /// (owner rule: abandoned uses never count) until the owner confirms otherwise; a late but alive
    /// client still gets graded through its own /ai-assess. Returns true only for the caller that won
    /// the Active to Finished compare-and-swap, so a race with the learner's /end or a second sweep
    /// finishes it once.
    /// </summary>
    public async Task<bool> FinalizeAtHardStopAsync(string sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var session = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null
            || session.State != SpeakingSessionState.Active
            || session.Mode == SpeakingSessionMode.LiveTutor)
        {
            return false;
        }

        var cardSeconds = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == session.RolePlayCardId)
            .Select(c => (int?)c.RolePlayTimeSeconds)
            .FirstOrDefaultAsync(ct);
        var window = SpeakingRolePlayLimits.Resolve(session, cardSeconds ?? 0, liveVoiceOptions?.Value);
        if (now < window.HardStopAt)
        {
            return false;
        }

        var deadline = window.DeadlineAt;
        var elapsed = window.EffectiveSeconds;
        bool won;
        if (db.Database.IsRelational())
        {
            // Compare-and-swap on the state we just read: exactly one finisher wins.
            won = await db.SpeakingSessions
                .Where(s => s.Id == sessionId && s.State == SpeakingSessionState.Active)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.State, SpeakingSessionState.Finished)
                    .SetProperty(s => s.EndedAt, deadline)
                    .SetProperty(s => s.ElapsedSeconds, elapsed)
                    .SetProperty(s => s.UpdatedAt, now), ct) == 1;
        }
        else
        {
            // In-memory test provider: no ExecuteUpdate and single-threaded, so re-read the row to
            // let a /end that landed after our load win.
            await db.Entry(session).ReloadAsync(ct);
            won = session.State == SpeakingSessionState.Active;
            if (won)
            {
                session.State = SpeakingSessionState.Finished;
                session.EndedAt = deadline;
                session.ElapsedSeconds = elapsed;
                session.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }
        }
        if (!won)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(session.AttemptId))
        {
            var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == session.AttemptId, ct);
            if (attempt is { State: AttemptState.InProgress })
            {
                attempt.State = AttemptState.Submitted;
                attempt.SubmittedAt ??= deadline;
                attempt.ElapsedSeconds = elapsed;
            }
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = "system",
            ActorName = "SpeakingRolePlayHardStop",
            Action = "SpeakingRolePlayHardStopped",
            ResourceType = "SpeakingSession",
            ResourceId = session.Id,
            Details = JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                examSessionId = session.ExamSessionId,
                mode = SpeakingSessionModes.ToCode(session.Mode),
                effectiveSeconds = elapsed,
                deadlineAt = deadline,
                hardStopAt = window.HardStopAt,
                overdueSeconds = (int)Math.Max(0, (now - deadline).TotalSeconds),
            }),
        });
        await db.SaveChangesAsync(ct);

        // ponytail: exam cards only until the owner confirms grading abandoned practice
        // (docs/speaking/live-voice.md); drop the ExamSessionId test to enable it.
        if (canonical is not null && session.ExamSessionId is not null)
        {
            await canonical.EnqueueAsync(session.Id, ct);
        }
        return true;
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

        // Profession lock (23 Sep 2026): every learner read/transition routes
        // through here, so a session resumed from before the lock on another
        // profession's card 404s before any state change or card payload.
        await LearnerProfessionGuard.RequireRolePlayCardAsync(db, userId, session.RolePlayCardId,
            "speaking_session_not_found", "That Speaking session does not exist.", ct);

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
        var tasks = card.Tasks.ToArray();

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
            // Emotion / Goal / Topic are internal (AI patient prompt only) and
            // never sent to learners (owner, 23 Sep 2026).
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
