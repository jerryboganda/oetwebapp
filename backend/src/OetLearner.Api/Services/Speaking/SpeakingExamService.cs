using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Speaking module rebuild (2026-06-11 spec). Orchestrates the two-card
/// Speaking exam that replaces the legacy mock-set + 60s-bridge flow.
///
/// State machine (server-authoritative, NO bridge step):
///
///   intro → prep_a → active_a → prep_b → active_b → completed
///
/// Card A auto-closes after its 8-minute window (3-min prep + 5-min
/// discussion) and Card B auto-reveals. Every transition is recomputed from
/// persisted timestamps (never from in-memory timers), so the exam survives a
/// server restart — see <see cref="AdvanceAsync"/>.
///
/// Credits (AI mode): Card A first tries to fund the whole exam from a "Full
/// Mock Speaking Exam Access" unit (<see cref="Domain.SpeakingExamSession.FundedByMockCredit"/>);
/// otherwise 2 AI credits are held per card at card reveal (prep start),
/// idempotent on the exam+slot reference, so an exam costs exactly 4 AI
/// credits. The holds are committed only once both cards are graded and
/// refunded if the exam ends without a result (see
/// <see cref="SpeakingCreditSettlement"/>). Live-tutor exams cost no credits
/// (pay-per-session via the Stripe booking) and are human-marked.
///
/// Live AI capacity (owner decision 5 Oct 2026): an AI exam takes ONE slot of the live-session cap at
/// <see cref="FinishIntroAsync"/>, before any credit hold or clock (see <see cref="SpeakingLiveAdmissionService"/>);
/// while the cap is full the exam stays in Intro and its detail carries <c>Admission</c>.
/// </summary>
public sealed class SpeakingExamService(
    LearnerDbContext db,
    SpeakingAiAssessmentService assessor,
    ILogger<SpeakingExamService> logger,
    IAiPackageCreditService? creditService = null,
    SpeakingSimulationV11PersonaService? personaService = null,
    OetLearner.Api.Services.Ai.IAiCreditReservationService? creditReservations = null,
    ISpeakingCanonicalAssessmentService? canonical = null,
    SpeakingComplianceService? compliance = null,
    LiveVoiceProviderProbeState? liveVoiceProbe = null,
    IOptions<LiveVoiceOptions>? liveVoiceOptions = null,
    SpeakingLiveAdmissionService? admission = null)
{
    private const int DefaultPrepSeconds = 180;

    /// <summary>Idle exams stuck in intro/prep past this window are expired by
    /// the sweeper so they cannot linger forever.</summary>
    public static readonly TimeSpan IdleExpiry = TimeSpan.FromHours(2);

    // ─────────────────────────────────────────────────────────────────
    // Create
    // ─────────────────────────────────────────────────────────────────

    public async Task<SpeakingExamDetail> CreateExamAsync(
        string userId,
        CreateSpeakingExamRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("speaking_exam_unauthenticated",
                "You must be signed in to start a Speaking exam.");
        }
        if (req is null)
        {
            throw ApiException.Validation("SPEAKING_EXAM_REQUEST_REQUIRED", "A request body is required.");
        }

        var mode = SpeakingExamModes.Parse(req.Mode);

        // ── Full Mock Speaking 7-day AI/tutor gate (2026-07-22 owner rule) ────
        // Supersedes the old unconditional "mock Speaking always needs a
        // live-tutor booking" rule. Inside ANY mock with a Speaking section
        // (Full/Diagnostic/FinalReadiness/standalone Sub/Part): if the
        // candidate's target exam is under 7 days away, a live-tutor booking
        // can't reliably be arranged in time, so only AI is allowed. At 7+
        // days out, either mode is allowed — the candidate's choice.

        // The exam always uses the caller's OWN registered profession; a
        // client-supplied ProfessionId is ignored.
        var ownProfession = await ResolveOwnProfessionAsync(userId, ct);
        var (cardA, cardB, professionId) = await ResolveCardsAsync(userId, req.MockSetId, ownProfession, ct);

        // A MockAttemptId only pre-pays the exam when it is the caller's own
        // active mock attempt that includes Speaking; anything else is
        // charged like a normal AI exam.
        var coveredByMockAttempt = !string.IsNullOrWhiteSpace(req.MockAttemptId)
            && await IsCoveredByMockAttemptAsync(userId, req.MockAttemptId, ct);

        // AI exams pre-check the wallet so the candidate is never stranded
        // after Card A with no credit for Card B (see EnsureCardsFundableAsync).
        if (mode == SpeakingExamMode.Ai)
        {
            await EnsureCardsFundableAsync(userId, coveredByMockAttempt, ct);
        }

        if (mode == SpeakingExamMode.LiveTutor && string.IsNullOrWhiteSpace(req.BookingId))
        {
            throw ApiException.Validation("SPEAKING_EXAM_BOOKING_REQUIRED",
                "A tutor booking is required for a live-tutor Speaking exam.");
        }

        var now = DateTimeOffset.UtcNow;
        var exam = new SpeakingExamSession
        {
            Id = $"spx_{Guid.NewGuid():N}",
            UserId = userId,
            ProfessionId = professionId,
            Mode = mode,
            State = SpeakingExamState.Intro,
            MockSetId = string.IsNullOrWhiteSpace(req.MockSetId) ? null : req.MockSetId,
            MockAttemptId = string.IsNullOrWhiteSpace(req.MockAttemptId) ? null : req.MockAttemptId,
            MockSectionId = string.IsNullOrWhiteSpace(req.MockSectionId) ? null : req.MockSectionId,
            CardAId = cardA.Id,
            CardBId = cardB.Id,
            BookingId = string.IsNullOrWhiteSpace(req.BookingId) ? null : req.BookingId,
            IntroStartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.SpeakingExamSessions.Add(exam);
        await db.SaveChangesAsync(ct);

        return await ProjectAsync(exam, now, ct);
    }

    /// <summary>Creates (or returns the existing) live-tutor exam attached to a
    /// PrivateSpeaking booking. The human tutor plays the patient and marks the
    /// result — no AI, no credits. Idempotent on the booking's ExamSessionId so
    /// the learner can re-open the booked session safely.</summary>
    public async Task<SpeakingExamDetail> CreateExamForBookingAsync(
        string userId, string bookingId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("speaking_exam_unauthenticated",
                "You must be signed in to start a Speaking exam.");
        }

        var booking = await db.PrivateSpeakingBookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct)
            ?? throw ApiException.NotFound("private_speaking_booking_not_found",
                "That booking does not exist.");
        if (!string.Equals(booking.LearnerUserId, userId, StringComparison.Ordinal))
        {
            // IDOR guard.
            throw ApiException.NotFound("private_speaking_booking_not_found",
                "That booking does not exist.");
        }
        if (booking.Status is not (PrivateSpeakingBookingStatus.Confirmed
            or PrivateSpeakingBookingStatus.ZoomCreated
            or PrivateSpeakingBookingStatus.InProgress))
        {
            throw ApiException.Conflict("private_speaking_booking_not_active",
                "The live-tutor booking is not ready for a Speaking exam.");
        }

        var now = DateTimeOffset.UtcNow;

        // Idempotent — reuse the exam already linked to this booking.
        if (!string.IsNullOrWhiteSpace(booking.ExamSessionId))
        {
            var existing = await db.SpeakingExamSessions.FirstOrDefaultAsync(e => e.Id == booking.ExamSessionId, ct);
            if (existing is not null)
            {
                if (existing.Mode == SpeakingExamMode.LiveTutor && string.IsNullOrWhiteSpace(existing.SessionAId))
                {
                    existing.SessionAId = await CreateChildSessionAsync(existing, existing.CardAId, "a", now, ct);
                    existing.UpdatedAt = now;
                    await db.SaveChangesAsync(ct);
                }
                var changed = await AdvanceAsync(existing, now, ct);
                if (changed) { existing.UpdatedAt = now; await db.SaveChangesAsync(ct); }
                return await ProjectAsync(existing, now, ct);
            }
        }

        var profession = string.IsNullOrWhiteSpace(booking.ProfessionTrack)
            ? "medicine"
            : booking.ProfessionTrack!.Trim().ToLowerInvariant();

        var (cardA, cardB, resolvedProfession) = await ResolveCardsAsync(userId, mockSetId: null, profession, ct);

        var exam = new SpeakingExamSession
        {
            Id = $"spx_{Guid.NewGuid():N}",
            UserId = userId,
            ProfessionId = resolvedProfession,
            Mode = SpeakingExamMode.LiveTutor,
            State = SpeakingExamState.Intro,
            CardAId = cardA.Id,
            CardBId = cardB.Id,
            BookingId = bookingId,
            IntroStartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SpeakingExamSessions.Add(exam);
        exam.SessionAId = await CreateChildSessionAsync(exam, exam.CardAId, "a", now, ct);

        booking.ExamSessionId = exam.Id;
        booking.SessionFormat = "exam";
        booking.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return await ProjectAsync(exam, now, ct);
    }

    public async Task<SpeakingExamDetail> CreateExamForTutorFromBookingAsync(
        string expertUserId, string bookingId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expertUserId))
        {
            throw ApiException.Unauthorized("speaking_exam_unauthenticated",
                "You must be signed in to join a live-tutor Speaking exam.");
        }

        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct)
            ?? throw ApiException.NotFound("private_speaking_booking_not_found",
                "That booking does not exist.");
        if (!string.Equals(booking.TutorProfile?.ExpertUserId, expertUserId, StringComparison.Ordinal))
        {
            throw ApiException.NotFound("private_speaking_booking_not_found",
                "That booking does not exist.");
        }

        return await CreateExamForBookingAsync(booking.LearnerUserId, bookingId, ct);
    }

    /// <summary>Tutor-only view of a live-tutor exam: both roleplayer (patient)
    /// cards + the current phase clock. Authorisation (expert role) is enforced
    /// at the endpoint; this method does not apply the learner IDOR guard.</summary>
    public async Task<SpeakingExamTutorView> GetExamForTutorAsync(
        string examId,
        CancellationToken ct,
        string? expertUserId = null)
    {
        if (string.IsNullOrWhiteSpace(examId))
        {
            throw ApiException.Validation("SPEAKING_EXAM_ID_REQUIRED", "Speaking exam id is required.");
        }
        var exam = await db.SpeakingExamSessions.FirstOrDefaultAsync(e => e.Id == examId, ct)
            ?? throw ApiException.NotFound("speaking_exam_not_found", "That Speaking exam does not exist.");

        if (!string.IsNullOrWhiteSpace(expertUserId))
        {
            var assigned = await db.PrivateSpeakingBookings
                .Include(b => b.TutorProfile)
                .AnyAsync(b => b.Id == exam.BookingId
                    && b.TutorProfile != null
                    && b.TutorProfile.ExpertUserId == expertUserId, ct);
            if (!assigned)
            {
                throw ApiException.Forbidden(
                    "speaking_exam_tutor_forbidden",
                    "You are not the assigned tutor for this Speaking exam.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var changed = await AdvanceAsync(exam, now, ct);
        if (changed) { exam.UpdatedAt = now; await db.SaveChangesAsync(ct); }

        var cards = new List<SpeakingExamRoleplayerCard>(2)
        {
            await BuildRoleplayerCardAsync(exam.CardAId, 1, ct),
            await BuildRoleplayerCardAsync(exam.CardBId, 2, ct),
        };

        var detail = await ProjectAsync(exam, now, ct);
        var currentCardId = exam.State switch
        {
            SpeakingExamState.PrepA or SpeakingExamState.ActiveA => exam.CardAId,
            SpeakingExamState.PrepB or SpeakingExamState.ActiveB => exam.CardBId,
            _ => null,
        };
        var currentSessionId = exam.State is SpeakingExamState.PrepA or SpeakingExamState.ActiveA
            ? exam.SessionAId
            : exam.SessionBId;
        var liveRoomId = currentCardId is null
                ? null
            : await db.SpeakingLiveRooms.AsNoTracking()
                .Where(r => r.State == SpeakingLiveRoomState.Active
                    && r.SpeakingSessionId == currentSessionId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(ct);
        return new SpeakingExamTutorView(
            ExamId: exam.Id,
            Mode: SpeakingExamModes.ToCode(exam.Mode),
            State: SpeakingExamStates.ToCode(exam.State),
            CurrentCardNumber: detail.CurrentCardNumber,
            ProfessionId: exam.ProfessionId,
            BookingId: exam.BookingId,
            Clock: detail.Clock,
            Cards: cards,
            CurrentCardId: currentCardId,
            LiveRoomId: liveRoomId);
    }

    private async Task<SpeakingExamRoleplayerCard> BuildRoleplayerCardAsync(
        string cardId, int cardNumber, CancellationToken ct)
    {
        var card = await db.RolePlayCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardId, ct);
        var script = card is null
            ? null
            : await db.InterlocutorScripts.AsNoTracking().FirstOrDefaultAsync(s => s.RolePlayCardId == cardId, ct);
        string? cardTypeName = card is { CardTypeId: { } typeId } && !string.IsNullOrWhiteSpace(typeId)
            ? await db.SpeakingCardTypes.AsNoTracking().Where(t => t.Id == typeId).Select(t => t.Name).FirstOrDefaultAsync(ct)
            : null;

        var tasks = script is null
            ? Array.Empty<string>()
            : script.PatientTasks.ToArray();

        return new SpeakingExamRoleplayerCard(
            CardNumber: cardNumber,
            Setting: card?.Setting ?? string.Empty,
            InterlocutorRole: card?.InterlocutorRole ?? "Patient",
            PatientName: card?.PatientName,
            PatientAge: card?.PatientAge,
            PatientBackground: script?.PatientBackground ?? string.Empty,
            PatientTasks: tasks,
            DisplayCardNumber: card?.DisplayCardNumber,
            CardTypeName: cardTypeName);
    }

    // ─────────────────────────────────────────────────────────────────
    // Transitions
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Finish the unscored Intro (Part 1) and reveal Card A. Holds credit A
    /// (AI mode) first, then creates child Session A: a refused hold (402) leaves the
    /// exam in Intro with nothing persisted. An AI exam first passes the live-session
    /// admission gate: while the cap is full the exam stays in Intro, nothing is held or
    /// timed, and the detail carries <c>Admission</c> (the page repeats this call).</summary>
    public async Task<SpeakingExamDetail> FinishIntroAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: true);
        if (exam.State != SpeakingExamState.Intro)
        {
            throw ApiException.Conflict("speaking_exam_invalid_state",
                $"Intro cannot be finished in state '{SpeakingExamStates.ToCode(exam.State)}'.");
        }

        // Hold Card A BEFORE the exam or its child session is touched. The credit calls flush this scoped
        // DbContext (GetSnapshotAsync and the reservation insert both SaveChanges), so changing the exam
        // first persisted PrepA even when the hold was then refused (402) and the candidate ran Card A
        // unpaid. Card B's hold only happens at the A->B reveal, so both cards must be fundable now: a
        // refusal there would leave Card B running with no hold. A retry that already holds Card A skips
        // the check (the hold below is adopted; its own 2 credits must not make the exam look unfundable).
        //
        // Live AI capacity gate (owner decision 5 Oct 2026): AFTER the read-only fundability check (an
        // unfunded learner never queues) and BEFORE the credit hold and the clock, so a learner who has to
        // wait has paid nothing and started nothing. Nothing on the exam or its child is touched here; a
        // waiting exam stays in Intro and the page repeats this call until a place is free. The exam keeps
        // its slot for both cards (Card B's reveal is not gated). With no healthy live provider the learner
        // uses the recorder fallback and the gate does not apply. When the gate applies the check runs from
        // inside it, only when this call is about to take a place (never on a waiting learner's repeat poll,
        // which would cost a full wallet snapshot every few seconds), and it also refuses an account with no
        // credit wallet at all, which the hold would refuse anyway; otherwise it runs here as it always did.
        SpeakingLiveAdmissionResult? gate = null;
        if (exam.Mode == SpeakingExamMode.Ai && admission is not null)
        {
            gate = await admission.AdmitOrQueueAsync(
                exam.UserId,
                SpeakingLiveAdmissionKinds.Exam,
                exam.Id,
                liveVoiceProbe?.IsLiveVoiceAvailable(liveVoiceOptions?.Value) == true,
                ct,
                beforeNewPlace: token => EnsureCardAFundableAsync(exam, refuseWalletlessAccount: true, token));
            if (gate.MustWait)
            {
                return await ProjectAsync(exam, DateTimeOffset.UtcNow, ct, gate.Waiting);
            }
        }

        if (gate is null || gate.Outcome == SpeakingLiveAdmissionOutcome.Bypassed)
        {
            await EnsureCardAFundableAsync(exam, refuseWalletlessAccount: false, ct);
        }

        try
        {
            await DebitCardAsync(exam, "a", ct);

            var now = DateTimeOffset.UtcNow;
            exam.IntroEndedAt = now;
            exam.PrepAStartedAt = now;
            exam.State = SpeakingExamState.PrepA;
            if (string.IsNullOrWhiteSpace(exam.SessionAId))
            {
                exam.SessionAId = await CreateChildSessionAsync(exam, exam.CardAId, "a", now, ct);
            }
            else
            {
                var existingCard = await db.SpeakingSessions
                    .FirstOrDefaultAsync(s => s.Id == exam.SessionAId, ct);
                if (existingCard is null)
                {
                    exam.SessionAId = await CreateChildSessionAsync(exam, exam.CardAId, "a", now, ct);
                }
                else
                {
                    existingCard.State = SpeakingSessionState.Prep;
                    existingCard.PrepStartedAt = now;
                    existingCard.RolePlayStartedAt = null;
                    existingCard.EndedAt = null;
                    existingCard.UpdatedAt = now;
                }
            }
            exam.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            return await ProjectAsync(exam, now, ct);
        }
        catch when (gate is { TookNewPlace: true })
        {
            // The start failed AFTER this call took a place (a refused or failed credit hold): give the place back at
            // once instead of leaving it idle for the claim window. Never touches a running exam; never throws.
            if (admission is not null)
            {
                await admission.ReleaseAsync(SpeakingLiveAdmissionKinds.Exam, exam.Id, CancellationToken.None);
            }
            throw;
        }
    }

    /// <summary>The read-only "can both cards be paid for" check that precedes the Card A hold. Skipped for a
    /// non-AI exam and for a retry that already holds Card A (the hold below is adopted; its own 2 credits must not
    /// make the exam look unfundable). <paramref name="refuseWalletlessAccount"/>: see
    /// <see cref="EnsureCardsFundableAsync"/>.</summary>
    private async Task EnsureCardAFundableAsync(SpeakingExamSession exam, bool refuseWalletlessAccount, CancellationToken ct)
    {
        if (exam.Mode != SpeakingExamMode.Ai || !string.IsNullOrWhiteSpace(exam.CreditARefId))
        {
            return;
        }

        var cardAReference = CardReference(exam, "a");
        if (await db.AiCreditReservations.AsNoTracking().AnyAsync(r => r.BusinessReference == cardAReference, ct))
        {
            return;
        }

        var coveredByMockAttempt = !string.IsNullOrWhiteSpace(exam.MockAttemptId)
            && await IsCoveredByMockAttemptAsync(exam.UserId, exam.MockAttemptId, ct);
        await EnsureCardsFundableAsync(exam.UserId, coveredByMockAttempt, ct, refuseWalletlessAccount);
    }

    /// <summary>
    /// The learner left the admission line ("Leave the queue"): releases this exam's WAITING place at once so it stops
    /// counting towards the positions of everyone behind it. The exam itself stays in its intro. Owner-checked; an
    /// exam that is not waiting is a no-op.
    /// </summary>
    public async Task LeaveAdmissionQueueAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct);
        if (admission is not null)
        {
            await admission.LeaveQueueAsync(SpeakingLiveAdmissionKinds.Exam, exam.Id, ct);
        }
    }

    /// <summary>Start the current card's discussion (prep → active) early. The
    /// 5-minute discussion window then runs from now; the card still hard-closes
    /// no later than its 8-minute total. Auto-advance also fires this at prep
    /// end if the candidate doesn't.</summary>
    public async Task<SpeakingExamDetail> StartCardAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: true);
        var now = DateTimeOffset.UtcNow;
        // Roll forward any overdue transitions first.
        await AdvanceAsync(exam, now, ct);

        if (exam.State == SpeakingExamState.PrepA)
        {
            exam.ActiveAStartedAt = now;
            exam.State = SpeakingExamState.ActiveA;
            await MarkChildActiveAsync(exam.SessionAId, now, ct);
        }
        else if (exam.State == SpeakingExamState.PrepB)
        {
            exam.ActiveBStartedAt = now;
            exam.State = SpeakingExamState.ActiveB;
            await MarkChildActiveAsync(exam.SessionBId, now, ct);
        }
        else
        {
            throw ApiException.Conflict("speaking_exam_invalid_state",
                $"No card is in preparation (state '{SpeakingExamStates.ToCode(exam.State)}').");
        }

        exam.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return await ProjectAsync(exam, now, ct);
    }

    public async Task<SpeakingExamDetail> GetExamForLearnerAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: true);
        var now = DateTimeOffset.UtcNow;
        var changed = await AdvanceAsync(exam, now, ct);
        if (changed)
        {
            exam.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        return await ProjectAsync(exam, now, ct);
    }

    public async Task<SpeakingExamDetail> CancelAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: true);
        if (SpeakingExamStates.IsTerminal(exam.State))
        {
            return await ProjectAsync(exam, DateTimeOffset.UtcNow, ct);
        }
        var now = DateTimeOffset.UtcNow;
        exam.State = SpeakingExamState.Cancelled;
        exam.CompletedAt = now;
        exam.UpdatedAt = now;
        await EndChildIfPresentAsync(exam.SessionAId, now, ct);
        await EndChildIfPresentAsync(exam.SessionBId, now, ct);
        await db.SaveChangesAsync(ct);
        // A cancelled exam can no longer run: its live-session place (a waiting one, or one admitted but never
        // started) is given back at once. A running exam is cancelled above first, so it is never "running" here.
        if (admission is not null)
        {
            await admission.ReleaseAsync(SpeakingLiveAdmissionKinds.Exam, exam.Id, ct);
        }
        // A cancelled exam produces no exam result, so its card holds are
        // refunded (full mock = 4 credits only for a graded result).
        if (creditReservations is not null)
        {
            await SpeakingCreditSettlement.ReleaseExamAsync(db, creditReservations, exam, ct);
        }
        return await ProjectAsync(exam, now, ct);
    }

    /// <summary>
    /// Records the learner's recording + AI-processing consent at the exam
    /// intro, BEFORE Card A's prep timer. Card A's child session is created
    /// now (and reused by <see cref="FinishIntroAsync"/>) so the consent is
    /// stamped on it; Card B inherits it at reveal. The realtime voice
    /// consent gate therefore never blocks inside a timed card.
    /// </summary>
    public async Task<SpeakingExamDetail> AcceptConsentAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: true);
        if (SpeakingExamStates.IsTerminal(exam.State))
        {
            throw ApiException.Conflict("speaking_exam_closed", "This Speaking exam has already finished.");
        }

        var now = DateTimeOffset.UtcNow;
        if (compliance is not null)
        {
            await compliance.EnsureSessionConsentsAsync(userId, ct);
        }
        if (string.IsNullOrWhiteSpace(exam.SessionAId))
        {
            exam.SessionAId = await CreateChildSessionAsync(exam, exam.CardAId, "a", now, ct);
        }

        foreach (var sessionId in new[] { exam.SessionAId, exam.SessionBId })
        {
            if (string.IsNullOrWhiteSpace(sessionId)) continue;
            var child = db.SpeakingSessions.Local.FirstOrDefault(s => s.Id == sessionId)
                ?? await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (child is null) continue;
            child.ConsentAcceptedAt ??= now;
            child.UpdatedAt = now;
        }

        exam.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return await ProjectAsync(exam, now, ct);
    }

    public async Task<SpeakingExamDetail> ReportTechnicalIssueAsync(
        string userId, string examId, string? note, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: true);
        var now = DateTimeOffset.UtcNow;
        foreach (var sid in new[] { exam.SessionAId, exam.SessionBId })
        {
            if (string.IsNullOrWhiteSpace(sid)) continue;
            var child = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sid, ct);
            if (child is null) continue;
            child.TechnicalIssueFlag = true;
            var trimmed = note?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                child.TechnicalIssueNote = trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
            }
            child.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return await ProjectAsync(exam, now, ct);
    }

    // ─────────────────────────────────────────────────────────────────
    // Server-authoritative advancement (lazy + worker)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Recomputes overdue transitions from timestamps. Returns true if
    /// any transition was applied (caller persists). Safe to call repeatedly;
    /// it walks forward until the current phase deadline is in the future.
    /// Used by every read, the hub TimeUp callback, and the sweeper.</summary>
    public async Task<bool> AdvanceAsync(SpeakingExamSession exam, DateTimeOffset now, CancellationToken ct)
    {
        if (SpeakingExamStates.IsTerminal(exam.State)) return false;

        var (prepA, discA) = await TimingAsync(exam.CardAId, ct);
        var (prepB, discB) = await TimingAsync(exam.CardBId, ct);
        var changed = false;

        // Loop so a long gap (e.g. server was down) can roll through multiple
        // phases in one pass.
        var guard = 0;
        while (guard++ < 8)
        {
            switch (exam.State)
            {
                case SpeakingExamState.PrepA:
                {
                    var prepEnds = (exam.PrepAStartedAt ?? now).AddSeconds(prepA);
                    if (now >= prepEnds)
                    {
                        exam.ActiveAStartedAt = prepEnds; // anchor to scheduled time
                        exam.State = SpeakingExamState.ActiveA;
                        await MarkChildActiveAsync(exam.SessionAId, prepEnds, ct);
                        changed = true;
                        continue;
                    }
                    return changed;
                }
                case SpeakingExamState.ActiveA:
                {
                    var discEnds = (exam.ActiveAStartedAt ?? now).AddSeconds(discA);
                    if (now >= discEnds)
                    {
                        exam.CardAEndedAt = discEnds;
                        await EndChildIfPresentAsync(exam.SessionAId, discEnds, ct);
                        // Reveal Card B — no bridge.
                        exam.PrepBStartedAt = discEnds;
                        exam.State = SpeakingExamState.PrepB;
                        exam.SessionBId = await CreateChildSessionAsync(exam, exam.CardBId, "b", discEnds, ct);
                        await DebitCardAsync(exam, "b", ct);
                        changed = true;
                        continue;
                    }
                    return changed;
                }
                case SpeakingExamState.PrepB:
                {
                    var prepEnds = (exam.PrepBStartedAt ?? now).AddSeconds(prepB);
                    if (now >= prepEnds)
                    {
                        exam.ActiveBStartedAt = prepEnds;
                        exam.State = SpeakingExamState.ActiveB;
                        await MarkChildActiveAsync(exam.SessionBId, prepEnds, ct);
                        changed = true;
                        continue;
                    }
                    return changed;
                }
                case SpeakingExamState.ActiveB:
                {
                    var discEnds = (exam.ActiveBStartedAt ?? now).AddSeconds(discB);
                    if (now >= discEnds)
                    {
                        exam.CardBEndedAt = discEnds;
                        await EndChildIfPresentAsync(exam.SessionBId, discEnds, ct);
                        exam.State = SpeakingExamState.Completed;
                        exam.CompletedAt = discEnds;
                        changed = true;
                        continue;
                    }
                    return changed;
                }
                default:
                    return changed;
            }
        }
        return changed;
    }

    // ─────────────────────────────────────────────────────────────────
    // Results
    // ─────────────────────────────────────────────────────────────────

    public async Task<SpeakingExamResults> GetResultsAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: true);
        var now = DateTimeOffset.UtcNow;
        var changed = await AdvanceAsync(exam, now, ct);
        if (changed) { exam.UpdatedAt = now; await db.SaveChangesAsync(ct); }

        var cards = new List<SpeakingExamCardResult>(2);
        var cardA = await ResultForCardAsync(exam, exam.SessionAId, 1, ct);
        var cardB = await ResultForCardAsync(exam, exam.SessionBId, 2, ct);
        cards.Add(cardA.Result);
        cards.Add(cardB.Result);

        // Only a live-tutor booking is human-marked. A curated mock-set may be
        // an AI exam; its child sessions are routed to the released v1.1
        // assessor and must never be mislabeled as awaiting tutor review.
        var humanMarked = exam.Mode == SpeakingExamMode.LiveTutor;
        var cardsScored = cards.All(c => c.Status == "scored");

        string overall;
        int? combined = null;
        string? band = null;
        string? combinedState = null;
        SpeakingAiAssessmentProjection? combinedAssessment = null;

        // A human-marked exam (two tutor marks) and an exam graded by the retired v1.1 assessor have no single
        // judgement of the whole test: their two card scores are averaged, as they always were.
        async Task AverageCardsAsync()
        {
            if (cardA.RawScaledScore is not { } scaledA || cardB.RawScaledScore is not { } scaledB) return;
            combined = (int)Math.Round((scaledA + scaledB) / 2.0);
            // The readiness band follows the reported (10-point) score the learner sees,
            // never the unrounded average: a combined 345 is shown as 350 and must not read "Borderline".
            band = OetScoring.SpeakingReadinessBandCode(
                OetScoring.SpeakingReadinessBandFromScaled(OetScoring.OetReportedScaledScore(combined.Value)));
            if (exam.CombinedScaledSnapshot is null)
            {
                exam.CombinedScaledSnapshot = combined;
                exam.ReadinessBandSnapshot = band;
                exam.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }
        }

        if (humanMarked)
        {
            overall = cardsScored ? "scored" : "awaiting_tutor";
            if (cardsScored) await AverageCardsAsync();
        }
        else
        {
            // Owner spec 4 Oct 2026: a Full Mock is assessed as ONE performance. The overall score, grade and
            // criterion scores come from the combined judgement of both role-plays together, never from averaging
            // the two card scores. An exam that finished before that existed keeps the number it was given then.
            combinedAssessment = SpeakingAiAssessmentService.ProjectCombinedJson(exam.CombinedAssessmentJson);
            if (combinedAssessment is not null)
            {
                overall = "scored";
                combinedState = SpeakingExamCombinedStates.Ready;
            }
            else if (cardsScored && (cardA.UsesV11 || cardB.UsesV11))
            {
                // Graded by the retired v1.1 assessor: there is no classic grade to judge as one test.
                overall = "scored";
                combinedState = SpeakingExamCombinedStates.Legacy;
                await AverageCardsAsync();
            }
            else if (exam.CombinedScaledSnapshot is not null)
            {
                overall = "scored";
                combinedState = SpeakingExamCombinedStates.Legacy;
            }
            else
            {
                // Both cards graded but no overall result yet: the combined judgement runs in the background.
                overall = "pending";
                if (cardsScored) combinedState = await QueueCombinedAsync(exam.Id, ct);
            }
        }

        if (!humanMarked && cardsScored && creditReservations is not null)
        {
            // The card grades are what the held credits paid for: settle them once both cards are graded,
            // whether or not the combined judgement has finished.
            await SpeakingCreditSettlement.CommitExamIfGradedAsync(db, creditReservations, exam, ct);
        }

        // One reported score (10-point steps) for the whole exam, and the grade, readiness band and
        // label all derive from it: a stored snapshot from before the reported score existed is
        // re-derived here, never shown with a band that was computed on an unrounded number.
        int? reportedScore = combinedAssessment is not null
            ? combinedAssessment.EstimatedScaledScore
            : combined is { } liveCombined
                ? OetScoring.OetReportedScaledScore(liveCombined)
                : exam.CombinedScaledSnapshot is { } snapshot
                    ? OetScoring.OetReportedScaledScore(snapshot)
                    : null;
        var examLabel = combinedAssessment is not null
            ? combinedAssessment.ScoreLabel ?? OetScoring.SpeakingScoreLabelProvisional
            : cards.Count > 0
                && cards.All(c => c.Assessment?.ScoreLabel == OetScoring.SpeakingScoreLabelPracticeEstimate)
                    ? OetScoring.SpeakingScoreLabelPracticeEstimate
                    : OetScoring.SpeakingScoreLabelProvisional;

        return new SpeakingExamResults(
            ExamId: exam.Id,
            Mode: SpeakingExamModes.ToCode(exam.Mode),
            State: SpeakingExamStates.ToCode(exam.State),
            OverallStatus: overall,
            CombinedScaledScore: reportedScore,
            ReadinessBand: reportedScore is { } reportedForBand
                ? OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBandFromScaled(reportedForBand))
                : exam.ReadinessBandSnapshot,
            Cards: cards,
            Grade: reportedScore is { } reportedForGrade ? OetScoring.OetGradeLetterFromScaled(reportedForGrade) : null,
            ScoreLabel: examLabel,
            CombinedAssessment: combinedAssessment,
            CombinedState: combinedState);
    }

    /// <summary>Makes sure the combined judgement of a fully graded AI exam is queued, and says whether it is still
    /// coming or has failed. Never fails the results read.</summary>
    private async Task<string> QueueCombinedAsync(string examId, CancellationToken ct)
    {
        if (canonical is null) return SpeakingExamCombinedStates.Pending;
        try
        {
            await canonical.EnqueueExamCombinedAsync(examId, ct);
            return await canonical.GetExamCombinedStateAsync(examId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not queue the combined judgement for exam {ExamId}.", examId);
            return SpeakingExamCombinedStates.Pending;
        }
    }

    /// <summary>The learner's "Try again" for a combined judgement that failed: re-queues it (no charge; the credits were
    /// settled by the card grades) and returns the new state.</summary>
    public async Task<string> RetryCombinedAssessmentAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: false);
        if (exam.Mode != SpeakingExamMode.Ai)
        {
            throw ApiException.Conflict("speaking_exam_not_ai", "Only an AI exam is graded as one test.");
        }

        if (canonical is null)
        {
            throw ApiException.Conflict("speaking_grading_unavailable", "Grading is not available right now. Please try again shortly.");
        }

        await canonical.RetryExamCombinedAsync(examId, ct);
        return await canonical.GetExamCombinedStateAsync(examId, ct);
    }

    private async Task<(SpeakingExamCardResult Result, int? RawScaledScore, bool UsesV11)> ResultForCardAsync(
        SpeakingExamSession exam, string? sessionId, int cardNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return (new SpeakingExamCardResult(cardNumber, string.Empty, "pending", null), null, false);
        }

        // Human-marked only for a live-tutor booking. AI curated mock-set
        // sessions continue to the v1.1 guard below.
        if (exam.Mode == SpeakingExamMode.LiveTutor)
        {
            var tutor = await db.SpeakingTutorAssessments.AsNoTracking()
                .Where(t => t.SpeakingSessionId == sessionId && t.IsFinal)
                .OrderByDescending(t => t.SubmittedAt)
                .FirstOrDefaultAsync(ct);
            return (new SpeakingExamCardResult(
                cardNumber, sessionId, tutor is null ? "awaiting_tutor" : "scored", null), null, false);
        }

        // A session with a captured v1.1 persona is owned by the released
        // ten-criterion simulation path (unless it was recorded through the
        // recorder fallback, which the classic assessor scores). Its complete
        // v1.1 card report counts as the card's score so the exam result is
        // reachable here instead of staying "pending" forever.
        // Read the result from the assessor that grading actually routed to:
        // until v1.1 is released for the profession a live-voice card is scored
        // by the classic assessor, and looking only for a v1.1 report left every
        // AI exam "pending" forever (production 26 Sep 2026).
        var recorderFallbackId = SpeakingSessionRecordingService.RecordingIdFor(sessionId);
        var usesSimulationV11 = canonical is not null
            ? await canonical.UsesV11Async(sessionId, ct)
            : await db.SpeakingSimulationV11PersonaRuntimeSnapshots
                    .AsNoTracking()
                    .AnyAsync(x => x.SpeakingSessionId == sessionId, ct)
                && !await db.SpeakingRecordings.AsNoTracking().AnyAsync(r => r.Id == recorderFallbackId, ct);

        SpeakingAiAssessmentProjection? latest;
        int? rawScaledScore = null;
        if (usesSimulationV11)
        {
            var v11Report = (await db.SpeakingSimulationV11Assessments.AsNoTracking()
                    .Where(a => a.SpeakingSessionId == sessionId
                        && a.AssessmentKind == "card"
                        && a.Status == SpeakingSimulationV11AssessmentStatus.Complete)
                    .ToListAsync(ct))
                .OrderByDescending(a => a.GeneratedAt)
                .FirstOrDefault();
            latest = v11Report is null ? null : ProjectV11CardScore(v11Report);
            rawScaledScore = v11Report is null ? null : v11Report.EstimatedPracticeScore ?? 0;
        }
        else
        {
            latest = await assessor.GetLatestAsync(sessionId, ct);
            if (latest is not null)
            {
                rawScaledScore = await db.SpeakingAiAssessments.AsNoTracking()
                    .Where(a => a.Id == latest.AssessmentId)
                    .Select(a => a.EstimatedScaledScore)
                    .SingleAsync(ct);
            }
        }

        if (latest is null)
        {
            // Make sure grading is queued once the card has finished; the AI
            // worker (or the learner's own /ai-assess) completes it.
            var child = await db.SpeakingSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (child is not null && child.State == SpeakingSessionState.Finished && canonical is not null)
            {
                await canonical.EnqueueAsync(sessionId, ct);
            }
        }

        return (new SpeakingExamCardResult(
            cardNumber, sessionId, latest is null ? "pending" : "scored", latest), rawScaledScore, usesSimulationV11);
    }

    /// <summary>Summary projection of a complete v1.1 card report into the
    /// exam card result shape. The full ten-criterion report stays on the
    /// dedicated v1.1 endpoints.</summary>
    private static SpeakingAiAssessmentProjection ProjectV11CardScore(SpeakingSimulationV11Assessment report)
    {
        var reported = OetScoring.OetReportedScaledScore(report.EstimatedPracticeScore ?? 0);
        return new SpeakingAiAssessmentProjection(
            AssessmentId: report.Id,
            Provider: report.Provider ?? "speaking_simulation_v11",
            ModelId: report.ModelName ?? string.Empty,
            PromptTemplateId: report.PromptTemplateId ?? string.Empty,
            CriterionScores: new Dictionary<string, CriterionScore>(),
            EstimatedScaledScore: reported,
            ReadinessBand: OetScoring.SpeakingReadinessBandCode(OetScoring.SpeakingReadinessBandFromScaled(reported)),
            OverallSummary: string.Empty,
            ConfidenceBand: report.ConfidenceLabel ?? "medium",
            GeneratedAt: report.GeneratedAt,
            IsAdvisory: true,
            Grade: OetScoring.OetGradeLetterFromScaled(reported),
            ScoreLabel: OetScoring.SpeakingScoreLabel(null, report.ModelName));
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>The caller's own registered profession. A learner with no
    /// profession cannot start an exam (fail closed, never a default).</summary>
    private async Task<string> ResolveOwnProfessionAsync(string userId, CancellationToken ct)
    {
        var profession = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ActiveProfessionId)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(profession))
        {
            throw ApiException.Forbidden("speaking_profession_required",
                "Choose your profession before starting a Speaking exam.");
        }
        return profession.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Picks the exam's two cards, restricted to <paramref name="professionId"/>
    /// and to cards whose hidden role-player persona is ready: an approved
    /// interlocutor script is what <see cref="SpeakingSimulationV11PersonaService.CaptureAtRevealAsync"/>
    /// needs at card reveal and what realtime voice needs to play the
    /// patient, so Card B can never fail with
    /// <c>speaking_simulation_v11_persona_missing</c> mid-exam.
    /// </summary>
    private async Task<(RolePlayCard A, RolePlayCard B, string ProfessionId)> ResolveCardsAsync(
        string userId, string? mockSetId, string professionId, CancellationToken ct)
    {
        var profession = professionId.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(mockSetId))
        {
            var set = await db.SpeakingMockSets.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == mockSetId, ct);
            // Another profession's mock set is reported as not found.
            if (set is null || !string.Equals(set.ProfessionId?.Trim(), profession, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.NotFound("speaking_mock_set_not_found", "That mock set does not exist.");
            }

            var a = await db.RolePlayCards.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ContentItemId == set.RolePlay1ContentId, ct);
            var b = await db.RolePlayCards.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ContentItemId == set.RolePlay2ContentId, ct);
            if (a is null || b is null)
            {
                throw ApiException.Conflict("speaking_mock_set_incomplete",
                    "This mock set is missing one of its role-play cards.");
            }
            if (!await IsPersonaReadyAsync(a.Id, ct) || !await IsPersonaReadyAsync(b.Id, ct))
            {
                throw ApiException.Conflict("speaking_mock_set_not_ready",
                    "This mock set's role-play cards are not ready for an AI exam yet.");
            }
            return (a, b, set.ProfessionId);
        }

        var published = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.ProfessionId == profession
                && c.Status == ContentStatus.Published
                && db.InterlocutorScripts.Any(s => s.RolePlayCardId == c.Id && !s.NeedsOwnerInput))
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (published.Count < 2)
        {
            throw ApiException.Conflict("speaking_exam_not_enough_cards",
                "There aren't enough published role-play cards for this profession to run an exam.");
        }

        // Server-side no-repeat rotation (owner spec 7 Oct 2026): history is the
        // candidate's own earlier exams, so it follows them across devices and logins.
        var past = await db.SpeakingExamSessions.AsNoTracking()
            .Where(e => e.UserId == userId && e.ProfessionId == profession)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new { e.CardAId, e.CardBId, ASeen = e.SessionAId != null, BSeen = e.SessionBId != null })
            .ToListAsync(ct);
        var history = new List<string>();
        foreach (var e in past)
        {
            if (e.ASeen) history.Add(e.CardAId);
            if (e.BSeen) history.Add(e.CardBId);
        }
        var recent = new HashSet<string>(StringComparer.Ordinal);
        if (past.Count > 0)
        {
            var last = past[^1];
            if (last.ASeen) recent.Add(last.CardAId);
            if (last.BSeen) recent.Add(last.CardBId);
        }
        var selected = PickRotatingPair(published, history, recent, RandomNumberGenerator.GetInt32);
        var cardA = await db.RolePlayCards.AsNoTracking().FirstAsync(c => c.Id == selected.First, ct);
        var cardB = await db.RolePlayCards.AsNoTracking().FirstAsync(c => c.Id == selected.Second, ct);
        return (cardA, cardB, profession);
    }

    private Task<bool> IsPersonaReadyAsync(string cardId, CancellationToken ct)
        => db.InterlocutorScripts.AsNoTracking()
            .AnyAsync(s => s.RolePlayCardId == cardId && !s.NeedsOwnerInput, ct);

    /// <summary>True only for the caller's own, still-active mock attempt that
    /// includes Speaking; that attempt already paid for the exam.</summary>
    private async Task<bool> IsCoveredByMockAttemptAsync(string userId, string mockAttemptId, CancellationToken ct)
    {
        var attempt = await db.MockAttempts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == mockAttemptId, ct);
        if (attempt is null
            || !string.Equals(attempt.UserId, userId, StringComparison.Ordinal)
            || attempt.State is not (AttemptState.NotStarted or AttemptState.InProgress or AttemptState.Paused))
        {
            return false;
        }

        if (string.Equals(attempt.MockType, "full", StringComparison.OrdinalIgnoreCase)
            || string.Equals(attempt.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sectionSubtests = await db.MockSectionAttempts.AsNoTracking()
            .Where(s => s.MockAttemptId == mockAttemptId)
            .Select(s => s.SubtestCode)
            .ToListAsync(ct);
        return sectionSubtests.Any(code => string.Equals(code, "speaking", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Two DIFFERENT cards, preferring ones the candidate has not seen in the current cycle.
    /// The cycle is replayed from <paramref name="historyOldestFirst"/>: it resets each time every
    /// published card has been used, so once the pool is exhausted all cards become eligible again,
    /// and the most recent exam's cards (<paramref name="recent"/>) are avoided where possible.
    /// </summary>
    internal static (string First, string Second) PickRotatingPair(
        IReadOnlyList<string> published,
        IReadOnlyList<string> historyOldestFirst,
        ISet<string> recent,
        Func<int, int> nextIndex)
    {
        var pool = new HashSet<string>(published, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in historyOldestFirst)
        {
            if (!pool.Contains(id)) continue; // since unpublished
            used.Add(id);
            if (used.Count == pool.Count) used.Clear(); // cycle complete: new cycle
        }

        string Draw(IEnumerable<string> candidates, string? forbid)
        {
            var list = candidates.Where(c => c != forbid).ToList();
            var preferred = list.Where(c => !recent.Contains(c)).ToList();
            var from = preferred.Count > 0 ? preferred : list;
            return from[nextIndex(from.Count)];
        }

        var unused = published.Where(c => !used.Contains(c)).ToList();
        var first = Draw(unused, null);
        var second = Draw(unused.Count > 1 ? unused : published, first);
        return (first, second);
    }

    internal static (T First, T Second) SampleTwo<T>(
        IReadOnlyList<T> items,
        Func<int, int> nextIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nextIndex);
        if (items.Count < 2)
        {
            throw new ArgumentException("At least two items are required.", nameof(items));
        }

        var firstIndex = nextIndex(items.Count);
        var secondIndex = nextIndex(items.Count - 1);
        if ((uint)firstIndex >= (uint)items.Count
            || (uint)secondIndex >= (uint)(items.Count - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(nextIndex),
                "The random index provider returned an index outside its requested range.");
        }

        // Skip the first selection in the remaining compact index range. This
        // is equivalent to two swaps of a partial Fisher-Yates shuffle.
        if (secondIndex >= firstIndex)
        {
            secondIndex++;
        }

        return (items[firstIndex], items[secondIndex]);
    }

    private async Task<(int Prep, int Discussion)> TimingAsync(string cardId, CancellationToken ct)
    {
        var card = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => new { c.PrepTimeSeconds, c.RolePlayTimeSeconds })
            .FirstOrDefaultAsync(ct);
        var prep = card?.PrepTimeSeconds is > 0 ? card!.PrepTimeSeconds : DefaultPrepSeconds;
        // The discussion is capped like a standalone role-play, so an absurd card value cannot
        // hold the whole exam (and a live provider session) open for hours.
        var disc = SpeakingRolePlayLimits.EffectiveSeconds(card?.RolePlayTimeSeconds ?? 0, liveVoiceOptions?.Value);
        return (prep, disc);
    }

    /// <summary>Creates the child SpeakingSession (and its legacy Attempt) for a
    /// card, opening directly in Prep (the exam Intro already covered warm-up).
    /// Returns the new session id.</summary>
    private async Task<string> CreateChildSessionAsync(
        SpeakingExamSession exam, string cardId, string slot, DateTimeOffset now, CancellationToken ct)
    {
        var card = await db.RolePlayCards.AsNoTracking().FirstAsync(c => c.Id == cardId, ct);
        var attemptId = $"att_{Guid.NewGuid():N}";
        var sessionId = $"sps_{Guid.NewGuid():N}";
        var sessionMode = exam.Mode == SpeakingExamMode.LiveTutor
            ? SpeakingSessionMode.LiveTutor
            : SpeakingSessionMode.AiExam;

        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = exam.UserId,
            ContentId = card.ContentItemId,
            SubtestCode = "speaking",
            Context = "practice",
            Mode = SpeakingSessionModes.ToCode(sessionMode),
            State = AttemptState.InProgress,
            StartedAt = now,
            CreatedAt = now,
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
        });

        var childSession = new SpeakingSession
        {
            Id = sessionId,
            UserId = exam.UserId,
            RolePlayCardId = card.Id,
            ExamSessionId = exam.Id,
            // Carry mock provenance onto the child so the assessor (which loads
            // only the child) can tell a genuine mock from a random AI exam.
            // Null for non-mock exams → treated as AI-allowed.
            MockSetId = exam.MockSetId,
            ExamSlot = slot,
            Mode = sessionMode,
            State = SpeakingSessionState.Prep,
            InterlocutorActorId = sessionMode == SpeakingSessionMode.LiveTutor
                ? await ResolveTutorActorIdAsync(exam.BookingId, ct)
                : null,
            AttemptId = attemptId,
            PrepStartedAt = now,
            RulebookVersion = exam.RulebookVersion,
            // Card B inherits the consent given at the exam intro so the
            // realtime voice consent gate never blocks a timed card.
            ConsentAcceptedAt = await ExamConsentAcceptedAtAsync(exam, ct),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SpeakingSessions.Add(childSession);
        var snapshotService = personaService ?? new SpeakingSimulationV11PersonaService(db);
        await snapshotService.CaptureAtRevealAsync(exam, childSession, card, now, ct);

        return sessionId;
    }

    /// <summary>When the exam's consent was given (stamped on Card A's child
    /// session by <see cref="AcceptConsentAsync"/>), else null.</summary>
    private async Task<DateTimeOffset?> ExamConsentAcceptedAtAsync(SpeakingExamSession exam, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(exam.SessionAId)) return null;
        var sessionA = db.SpeakingSessions.Local.FirstOrDefault(s => s.Id == exam.SessionAId);
        if (sessionA is not null) return sessionA.ConsentAcceptedAt;
        return await db.SpeakingSessions.AsNoTracking()
            .Where(s => s.Id == exam.SessionAId)
            .Select(s => s.ConsentAcceptedAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<string?> ResolveTutorActorIdAsync(string? bookingId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bookingId)) return null;

        return await db.PrivateSpeakingBookings
            .Where(b => b.Id == bookingId)
            .Join(
                db.PrivateSpeakingTutorProfiles,
                booking => booking.TutorProfileId,
                profile => profile.Id,
                (_, profile) => profile.ExpertUserId)
            .FirstOrDefaultAsync(ct);
    }

    private async Task MarkChildActiveAsync(string? sessionId, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var child = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (child is null || child.State == SpeakingSessionState.Finished) return;
        child.State = SpeakingSessionState.Active;
        child.RolePlayStartedAt ??= now;
        child.UpdatedAt = now;
        // Card credit holds are committed only once the exam result is
        // graded (GetResultsAsync / SpeakingCreditSettlement), never here.
    }

    private async Task EndChildIfPresentAsync(string? sessionId, DateTimeOffset endedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var child = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (child is null || child.State == SpeakingSessionState.Finished) return;
        child.State = SpeakingSessionState.Finished;
        child.EndedAt ??= endedAt;
        if (child.RolePlayStartedAt is { } started)
        {
            child.ElapsedSeconds = Math.Max(0, (int)(endedAt - started).TotalSeconds);
        }
        child.UpdatedAt = endedAt;
        if (canonical is not null)
        {
            await canonical.EnqueueAsync(child.Id, ct);
        }

        // Mark the legacy attempt submitted so downstream queries stay coherent.
        if (!string.IsNullOrWhiteSpace(child.AttemptId))
        {
            var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == child.AttemptId, ct);
            if (attempt is not null && attempt.State == AttemptState.InProgress)
            {
                attempt.State = AttemptState.Submitted;
                attempt.SubmittedAt ??= endedAt;
            }
        }
    }

    /// <summary>The idempotency reference of one card's credit hold: "exam:{examId}:cardA" / "...cardB".</summary>
    private static string CardReference(SpeakingExamSession exam, string slot)
        => $"exam:{exam.Id}:card{slot.ToUpperInvariant()}";

    /// <summary>Refuses (402) an AI exam when the account holds a package wallet that cannot fund both
    /// cards (4 credits), so a candidate is never stranded after Card A with nothing left for Card B.
    /// Exempt: an exam covered by a mock attempt, an account with a "Full Mock Speaking Exam Access"
    /// unit (it alone funds the whole exam, see <see cref="DebitCardAsync"/>) and an account with no
    /// package wallet at all (its Card A hold is refused by <see cref="DebitCardAsync"/> instead).
    /// Checked at creation and again when Part 2 begins, because the balance can change in between.
    /// <paramref name="refuseWalletlessAccount"/> (only when the live-session gate applies, and only where credit holds
    /// go through <c>IAiCreditReservationService</c> as in production): an account with no package wallet at all is
    /// refused here (402 <c>ai_credits_insufficient</c>, the very code its Card A hold would answer with) instead of
    /// being queued behind paying learners for a place it could only fail to pay for.</summary>
    private async Task EnsureCardsFundableAsync(
        string userId, bool coveredByMockAttempt, CancellationToken ct, bool refuseWalletlessAccount = false)
    {
        if (creditService is null || coveredByMockAttempt) return;

        var snapshot = await creditService.GetSnapshotAsync(userId, 0, ct);
        if (snapshot.MockExamsRemaining >= 1) return;

        var hasPackageWallet = snapshot.ExpiresAt is not null
            || snapshot.SharedCredits > 0
            || snapshot.SpeakingOnlyCredits > 0
            || snapshot.FlexibleCredits > 0
            || snapshot.WritingOnlyCredits > 0
            || snapshot.SpeakingUnlimited;
        if (hasPackageWallet && snapshot.AvailableSpeakingActivities < AiGradingCreditCost.SpeakingExam)
        {
            throw ApiException.PaymentRequired("speaking_exam_insufficient_credits",
                "You do not have enough credits to start this activity. Please purchase another package or upgrade your plan.");
        }

        if (!hasPackageWallet && refuseWalletlessAccount && creditReservations is not null)
        {
            throw ApiException.PaymentRequired("ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }
    }

    /// <summary>Debits credit for a card at reveal, idempotent on the
    /// exam+slot reference. AI mode only. Stores the ref on the exam so a
    /// retried transition never double-charges. FINAL 2026-09-06: 2 AI
    /// credits per card, 4 total across Card A + Card B.
    ///
    /// Card A first tries to fund the WHOLE exam from the account's "Full
    /// Mock Speaking Exam Access" allowance (<c>MockExamsRemaining</c>, one
    /// unit = one two-card exam) — a distinct, separately-purchasable quota
    /// from the per-card "AI Speaking Credits" wallet. If that allowance is
    /// absent/exhausted, Card A falls back to the existing per-card debit
    /// unchanged. Card B is a no-op once Card A was mock-funded (already
    /// covered); otherwise it debits its own per-card credit as before.</summary>
    private async Task DebitCardAsync(SpeakingExamSession exam, string slot, CancellationToken ct)
    {
        if (exam.Mode != SpeakingExamMode.Ai || creditService is null) return;

        var covered = $"exam:{exam.Id}:mock-attempt";
        if (!string.IsNullOrWhiteSpace(exam.MockAttemptId)
            && (slot == "b"
                ? string.Equals(exam.CreditARefId, covered, StringComparison.Ordinal)
                : await IsCoveredByMockAttemptAsync(exam.UserId, exam.MockAttemptId, ct)))
        {
            if (slot == "a") exam.CreditARefId = covered; else exam.CreditBRefId = covered;
            return;
        }

        var alreadyDebited = slot == "a" ? exam.CreditARefId : exam.CreditBRefId;
        if (!string.IsNullOrWhiteSpace(alreadyDebited)) return;

        if (slot == "a")
        {
            // IMPORTANT: only attempt the mock-exam debit when the balance is
            // actually positive. DeductMockAsync has a "legacy account"
            // grandfather bypass (grants it for free when MockExamsRemaining
            // is 0 AND the account has never received a mock-exam grant) —
            // correct for its original caller (MockService, where that really
            // does mean a pre-quota legacy account) but WRONG here, since
            // every Speaking account today has zero MockExamsRemaining and no
            // grant history simply because Speaking never drew from this pool
            // before. Gating on a positive balance first guarantees we only
            // ever call DeductMockAsync when it will do a real decrement, per
            // its own first-line check, never the bypass.
            var snapshot = await creditService.GetSnapshotAsync(exam.UserId, 0, ct);
            if (snapshot.MockExamsRemaining > 0)
            {
                var mockRefId = $"exam:{exam.Id}:mock";
                var mockDebit = await creditService.DeductMockAsync(exam.UserId, mockRefId, ct);
                if (mockDebit.Debited
                    || string.Equals(mockDebit.ErrorCode, "already_debited", StringComparison.Ordinal))
                {
                    exam.FundedByMockCredit = true;
                    exam.CreditARefId = mockRefId;
                    return;
                }
                // Lost a race for the last unit (or expired) — fall through to
                // the per-card AI Speaking Credits wallet below.
            }
        }
        else if (exam.FundedByMockCredit)
        {
            // Whole exam already paid for by Card A's mock-credit debit.
            exam.CreditBRefId = exam.CreditARefId;
            return;
        }

        var refId = CardReference(exam, slot);
        if (creditReservations is not null)
        {
            var operationId = Guid.NewGuid().ToString("N");
            await creditReservations.ReserveSpeakingAsync(exam.UserId, operationId, refId, ct);
            if (slot == "a") exam.CreditARefId = refId; else exam.CreditBRefId = refId;
            return;
        }

        var debit = await creditService.DeductGradingCreditAsync(exam.UserId, "speaking", refId, ct);
        if (!debit.Debited)
        {
            if (string.Equals(debit.ErrorCode, "already_debited", StringComparison.Ordinal))
            {
                if (slot == "a") exam.CreditARefId = refId; else exam.CreditBRefId = refId;
                return;
            }
            throw ApiException.PaymentRequired(
                debit.ErrorCode ?? "no_ai_package_credits",
                    debit.ErrorMessage ?? "You do not have enough credits to start this activity. Please purchase another package or upgrade your plan.");
        }

        if (slot == "a") exam.CreditARefId = refId; else exam.CreditBRefId = refId;
    }

    private async Task<SpeakingExamSession> LoadOwnedAsync(
        string userId, string examId, CancellationToken ct, bool tracking = false)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("speaking_exam_unauthenticated",
                "You must be signed in to interact with a Speaking exam.");
        }
        if (string.IsNullOrWhiteSpace(examId))
        {
            throw ApiException.Validation("SPEAKING_EXAM_ID_REQUIRED", "Speaking exam id is required.");
        }
        var q = tracking ? db.SpeakingExamSessions : db.SpeakingExamSessions.AsNoTracking();
        var exam = await q.FirstOrDefaultAsync(e => e.Id == examId, ct)
            ?? throw ApiException.NotFound("speaking_exam_not_found", "That Speaking exam does not exist.");
        if (!string.Equals(exam.UserId, userId, StringComparison.Ordinal))
        {
            // IDOR guard — NotFound so ids don't leak.
            throw ApiException.NotFound("speaking_exam_not_found", "That Speaking exam does not exist.");
        }
        return exam;
    }

    private async Task<SpeakingExamDetail> ProjectAsync(
        SpeakingExamSession exam,
        DateTimeOffset now,
        CancellationToken ct,
        SpeakingLiveAdmissionView? waitingView = null)
    {
        // The line a waiting AI exam is in (state stays intro). Passed in by the finish-intro call that has just
        // decided it; on a plain read it is looked up. Null whenever the exam is not waiting.
        var admissionView = waitingView;
        if (admissionView is null
            && admission is not null
            && exam.State == SpeakingExamState.Intro
            && exam.Mode == SpeakingExamMode.Ai)
        {
            admissionView = await admission.GetWaitingViewAsync(SpeakingLiveAdmissionKinds.Exam, exam.Id, ct);
        }

        var (prepA, discA) = await TimingAsync(exam.CardAId, ct);
        var (prepB, discB) = await TimingAsync(exam.CardBId, ct);

        string stage = SpeakingExamStates.ToCode(exam.State);
        DateTimeOffset? stageStart = null;
        DateTimeOffset? stageEnds = null;
        int currentCardNumber = 0;
        string? currentSessionId = null;
        string? currentCardId = null;

        switch (exam.State)
        {
            case SpeakingExamState.Intro:
                stageStart = exam.IntroStartedAt;
                break;
            case SpeakingExamState.PrepA:
                stageStart = exam.PrepAStartedAt;
                stageEnds = stageStart?.AddSeconds(prepA);
                currentCardNumber = 1; currentSessionId = exam.SessionAId; currentCardId = exam.CardAId;
                break;
            case SpeakingExamState.ActiveA:
                stageStart = exam.ActiveAStartedAt;
                stageEnds = stageStart?.AddSeconds(discA);
                currentCardNumber = 1; currentSessionId = exam.SessionAId; currentCardId = exam.CardAId;
                break;
            case SpeakingExamState.PrepB:
                stageStart = exam.PrepBStartedAt;
                stageEnds = stageStart?.AddSeconds(prepB);
                currentCardNumber = 2; currentSessionId = exam.SessionBId; currentCardId = exam.CardBId;
                break;
            case SpeakingExamState.ActiveB:
                stageStart = exam.ActiveBStartedAt;
                stageEnds = stageStart?.AddSeconds(discB);
                currentCardNumber = 2; currentSessionId = exam.SessionBId; currentCardId = exam.CardBId;
                break;
        }

        int? secondsRemaining = null;
        var expired = false;
        if (stageEnds is { } ends &&
            exam.State is SpeakingExamState.PrepA or SpeakingExamState.ActiveA
                or SpeakingExamState.PrepB or SpeakingExamState.ActiveB)
        {
            var remaining = (int)Math.Floor((ends - now).TotalSeconds);
            secondsRemaining = Math.Max(0, remaining);
            expired = remaining <= 0;
        }

        object? card = null;
        if (currentCardId is not null)
        {
            var cardRow = await db.RolePlayCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentCardId, ct);
            if (cardRow is not null)
            {
                card = SpeakingSessionService.ProjectLearnerCard(cardRow);
            }
        }

        var consentAccepted = await ExamConsentAcceptedAtAsync(exam, ct) is not null;

        return new SpeakingExamDetail(
            ExamId: exam.Id,
            Mode: SpeakingExamModes.ToCode(exam.Mode),
            State: stage,
            ProfessionId: exam.ProfessionId,
            CurrentCardNumber: currentCardNumber,
            CurrentSessionId: currentSessionId,
            CurrentCard: card,
            Clock: new SpeakingExamClock(stage, now, stageStart, stageEnds, secondsRemaining, expired),
            CompletedAt: exam.CompletedAt,
            MockAttemptId: exam.MockAttemptId,
            MockSectionId: exam.MockSectionId,
            LiveRoomId: currentSessionId is null
                ? null
                : await db.SpeakingLiveRooms.AsNoTracking()
                    .Where(r => r.State == SpeakingLiveRoomState.Active
                        && r.SpeakingSessionId == currentSessionId)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => r.Id)
                    .FirstOrDefaultAsync(ct),
            ConsentAccepted: consentAccepted,
            LiveVoiceAvailable: liveVoiceProbe?.IsLiveVoiceAvailable(liveVoiceOptions?.Value) == true,
            Cards:
            [
                new SpeakingExamCardSession(1, exam.SessionAId),
                new SpeakingExamCardSession(2, exam.SessionBId),
            ],
            Admission: admissionView);
    }
}
