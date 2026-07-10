using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
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
/// otherwise one AI Speaking Credit is debited per card at card reveal (prep
/// start), idempotent on the exam+slot reference, so an exam costs exactly
/// two credits. Live-tutor exams cost no credits (pay-per-session via the
/// Stripe booking) and are human-marked.
/// </summary>
public sealed class SpeakingExamService(
    LearnerDbContext db,
    SpeakingAiAssessmentService assessor,
    ILogger<SpeakingExamService> logger,
    IAiPackageCreditService? creditService = null)
{
    private const int DefaultPrepSeconds = 180;
    private const int DefaultDiscussionSeconds = 300;

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

        // ── No AI for MOCK Speaking (2026-06-29 owner rule) ───────────────────
        // A Speaking exam launched from a curated Mock Set (or a full mock
        // bundle) is a MOCK: it must be marked by a human examiner via a
        // live-tutor booking, never by AI. Reject an AI-mode mock launch up
        // front with a clear error rather than letting it fall through to the
        // AI wallet pre-check (which would surface a misleading 402). The
        // LiveTutor branch below then requires a BookingId, so mock ⇒ booked.
        var isMockLaunch = !string.IsNullOrWhiteSpace(req.MockSetId);
        if (isMockLaunch && mode != SpeakingExamMode.LiveTutor)
        {
            throw ApiException.Validation("SPEAKING_MOCK_REQUIRES_LIVE_TUTOR",
                "Mock Speaking exams are marked by a human examiner. Start this mock as a live-tutor booking.");
        }

        var (cardA, cardB, professionId) = await ResolveCardsAsync(req, ct);

        // AI exams pre-check the wallet so the candidate is never stranded
        // after Card A with no credit for Card B. Two speaking credits needed
        // — UNLESS the account has a "Full Mock Speaking Exam Access" unit
        // (MockExamsRemaining), which alone funds the whole exam (see
        // DebitCardAsync). This mirrors the fallback order used at debit time.
        if (mode == SpeakingExamMode.Ai && creditService is not null)
        {
            var snapshot = await creditService.GetSnapshotAsync(userId, 0, ct);
            if (snapshot.MockExamsRemaining < 1)
            {
                var available = snapshot.SpeakingOnlyCredits + snapshot.FlexibleCredits;
                // Legacy/subscription accounts whose package is uninitialised still
                // pass — the per-card debit later bypasses them the same way grading
                // did. We only hard-block when a package wallet exists and is short.
                var hasPackageWallet = snapshot.ExpiresAt is not null
                    || snapshot.SpeakingOnlyCredits > 0
                    || snapshot.FlexibleCredits > 0
                    || snapshot.WritingOnlyCredits > 0;
                if (hasPackageWallet && available < 2)
                {
                    throw ApiException.PaymentRequired("speaking_exam_insufficient_credits",
                        "A Speaking exam needs 2 AI credits, or 1 Full Mock Exam credit. Purchase a package to continue.");
                }
            }
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

        var now = DateTimeOffset.UtcNow;

        // Idempotent — reuse the exam already linked to this booking.
        if (!string.IsNullOrWhiteSpace(booking.ExamSessionId))
        {
            var existing = await db.SpeakingExamSessions.FirstOrDefaultAsync(e => e.Id == booking.ExamSessionId, ct);
            if (existing is not null)
            {
                var changed = await AdvanceAsync(existing, now, ct);
                if (changed) { existing.UpdatedAt = now; await db.SaveChangesAsync(ct); }
                return await ProjectAsync(existing, now, ct);
            }
        }

        var profession = string.IsNullOrWhiteSpace(booking.ProfessionTrack)
            ? "medicine"
            : booking.ProfessionTrack!.Trim().ToLowerInvariant();

        var (cardA, cardB, resolvedProfession) = await ResolveCardsAsync(
            new CreateSpeakingExamRequest("live_tutor", ProfessionId: profession), ct);

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

        booking.ExamSessionId = exam.Id;
        booking.SessionFormat = "exam";
        booking.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return await ProjectAsync(exam, now, ct);
    }

    /// <summary>Tutor-only view of a live-tutor exam: both roleplayer (patient)
    /// cards + the current phase clock. Authorisation (expert role) is enforced
    /// at the endpoint; this method does not apply the learner IDOR guard.</summary>
    public async Task<SpeakingExamTutorView> GetExamForTutorAsync(string examId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(examId))
        {
            throw ApiException.Validation("SPEAKING_EXAM_ID_REQUIRED", "Speaking exam id is required.");
        }
        var exam = await db.SpeakingExamSessions.FirstOrDefaultAsync(e => e.Id == examId, ct)
            ?? throw ApiException.NotFound("speaking_exam_not_found", "That Speaking exam does not exist.");

        var now = DateTimeOffset.UtcNow;
        var changed = await AdvanceAsync(exam, now, ct);
        if (changed) { exam.UpdatedAt = now; await db.SaveChangesAsync(ct); }

        var cards = new List<SpeakingExamRoleplayerCard>(2)
        {
            await BuildRoleplayerCardAsync(exam.CardAId, 1, ct),
            await BuildRoleplayerCardAsync(exam.CardBId, 2, ct),
        };

        var detail = await ProjectAsync(exam, now, ct);
        return new SpeakingExamTutorView(
            ExamId: exam.Id,
            Mode: SpeakingExamModes.ToCode(exam.Mode),
            State: SpeakingExamStates.ToCode(exam.State),
            CurrentCardNumber: detail.CurrentCardNumber,
            ProfessionId: exam.ProfessionId,
            BookingId: exam.BookingId,
            Clock: detail.Clock,
            Cards: cards);
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
            : new[] { script.PatientTask1, script.PatientTask2, script.PatientTask3, script.PatientTask4, script.PatientTask5 }
                .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToArray();

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

    /// <summary>Finish the unscored Intro (Part 1) and reveal Card A. Creates
    /// child Session A and debits credit A (AI mode).</summary>
    public async Task<SpeakingExamDetail> FinishIntroAsync(string userId, string examId, CancellationToken ct)
    {
        var exam = await LoadOwnedAsync(userId, examId, ct, tracking: true);
        if (exam.State != SpeakingExamState.Intro)
        {
            throw ApiException.Conflict("speaking_exam_invalid_state",
                $"Intro cannot be finished in state '{SpeakingExamStates.ToCode(exam.State)}'.");
        }

        var now = DateTimeOffset.UtcNow;
        exam.IntroEndedAt = now;
        exam.PrepAStartedAt = now;
        exam.State = SpeakingExamState.PrepA;
        exam.SessionAId = await CreateChildSessionAsync(exam, exam.CardAId, "a", now, ct);
        await DebitCardAsync(exam, "a", ct);
        exam.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await ProjectAsync(exam, now, ct);
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
        cards.Add(await ResultForCardAsync(exam, exam.SessionAId, 1, ct));
        cards.Add(await ResultForCardAsync(exam, exam.SessionBId, 2, ct));

        // Aggregate once both cards are scored.
        string overall;
        int? combined = null;
        string? band = null;
        // Human-marked when a live-tutor booking OR a mock-set exam. Mock
        // Speaking is always LiveTutor after the creation guard; the MockSetId
        // clause is defensive so any legacy AI-mode mock row still reports
        // awaiting_tutor (human marking) rather than pending (AI).
        var humanMarked = exam.Mode == SpeakingExamMode.LiveTutor
            || !string.IsNullOrWhiteSpace(exam.MockSetId);
        if (humanMarked)
        {
            overall = cards.All(c => c.Status == "scored") ? "scored" : "awaiting_tutor";
        }
        else
        {
            overall = cards.All(c => c.Status == "scored") ? "scored" : "pending";
        }

        if (overall == "scored")
        {
            var scaledA = cards[0].Assessment?.EstimatedScaledScore;
            var scaledB = cards[1].Assessment?.EstimatedScaledScore;
            if (scaledA is not null && scaledB is not null)
            {
                combined = (int)Math.Round((scaledA.Value + scaledB.Value) / 2.0);
                band = OetScoring.SpeakingReadinessBandCode(
                    OetScoring.SpeakingReadinessBandFromScaled(combined.Value));
                if (exam.CombinedScaledSnapshot is null)
                {
                    exam.CombinedScaledSnapshot = combined;
                    exam.ReadinessBandSnapshot = band;
                    exam.UpdatedAt = now;
                    await db.SaveChangesAsync(ct);
                }
            }
        }

        return new SpeakingExamResults(
            ExamId: exam.Id,
            Mode: SpeakingExamModes.ToCode(exam.Mode),
            State: SpeakingExamStates.ToCode(exam.State),
            OverallStatus: overall,
            CombinedScaledScore: combined ?? (exam.CombinedScaledSnapshot is { } s ? (int)Math.Round(s) : null),
            ReadinessBand: band ?? exam.ReadinessBandSnapshot,
            Cards: cards);
    }

    private async Task<SpeakingExamCardResult> ResultForCardAsync(
        SpeakingExamSession exam, string? sessionId, int cardNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new SpeakingExamCardResult(cardNumber, string.Empty, "pending", null);
        }

        // Human-marked: live-tutor booking OR a mock-set exam (defensive against
        // any legacy AI-mode mock row). Surface the tutor assessment when final,
        // else pending — never fall through to AI scoring below.
        if (exam.Mode == SpeakingExamMode.LiveTutor || !string.IsNullOrWhiteSpace(exam.MockSetId))
        {
            var tutor = await db.SpeakingTutorAssessments.AsNoTracking()
                .Where(t => t.SpeakingSessionId == sessionId && t.IsFinal)
                .OrderByDescending(t => t.SubmittedAt)
                .FirstOrDefaultAsync(ct);
            return new SpeakingExamCardResult(
                cardNumber, sessionId, tutor is null ? "awaiting_tutor" : "scored", null);
        }

        // AI exam: official AI assessment. Generate lazily if the card is
        // finished but not yet scored (transcript may still be settling).
        var latest = await assessor.GetLatestAsync(sessionId, ct);
        if (latest is null)
        {
            var child = await db.SpeakingSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (child is not null && child.State == SpeakingSessionState.Finished)
            {
                try
                {
                    latest = await assessor.RunAssessmentAsync(sessionId, ct);
                }
                catch (ApiException ex) when (ex.ErrorCode is "speaking_session_no_transcript"
                    or "speaking_ai_unavailable" or "speaking_ai_unparseable")
                {
                    logger.LogInformation(
                        "Exam {ExamId} card {Card} not yet scorable: {Code}", exam.Id, cardNumber, ex.ErrorCode);
                }
            }
        }

        return new SpeakingExamCardResult(
            cardNumber, sessionId, latest is null ? "pending" : "scored", latest);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private async Task<(RolePlayCard A, RolePlayCard B, string ProfessionId)> ResolveCardsAsync(
        CreateSpeakingExamRequest req, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(req.MockSetId))
        {
            var set = await db.SpeakingMockSets.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == req.MockSetId, ct)
                ?? throw ApiException.NotFound("speaking_mock_set_not_found", "That mock set does not exist.");

            var a = await db.RolePlayCards.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ContentItemId == set.RolePlay1ContentId, ct);
            var b = await db.RolePlayCards.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ContentItemId == set.RolePlay2ContentId, ct);
            if (a is null || b is null)
            {
                throw ApiException.Conflict("speaking_mock_set_incomplete",
                    "This mock set is missing one of its role-play cards.");
            }
            return (a, b, set.ProfessionId);
        }

        var profession = string.IsNullOrWhiteSpace(req.ProfessionId)
            ? "medicine"
            : req.ProfessionId!.Trim().ToLowerInvariant();

        var published = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.ProfessionId == profession && c.Status == ContentStatus.Published)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (published.Count < 2)
        {
            throw ApiException.Conflict("speaking_exam_not_enough_cards",
                "There aren't enough published role-play cards for this profession to run an exam.");
        }

        // Random distinct pair.
        var shuffled = published.OrderBy(_ => Guid.NewGuid()).Take(2).ToList();
        var cardA = await db.RolePlayCards.AsNoTracking().FirstAsync(c => c.Id == shuffled[0], ct);
        var cardB = await db.RolePlayCards.AsNoTracking().FirstAsync(c => c.Id == shuffled[1], ct);
        return (cardA, cardB, profession);
    }

    private async Task<(int Prep, int Discussion)> TimingAsync(string cardId, CancellationToken ct)
    {
        var card = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => new { c.PrepTimeSeconds, c.RolePlayTimeSeconds })
            .FirstOrDefaultAsync(ct);
        var prep = card?.PrepTimeSeconds is > 0 ? card!.PrepTimeSeconds : DefaultPrepSeconds;
        var disc = card?.RolePlayTimeSeconds is > 0 ? card!.RolePlayTimeSeconds : DefaultDiscussionSeconds;
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

        db.SpeakingSessions.Add(new SpeakingSession
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
            AttemptId = attemptId,
            PrepStartedAt = now,
            RulebookVersion = exam.RulebookVersion,
            CreatedAt = now,
            UpdatedAt = now,
        });

        return sessionId;
    }

    private async Task MarkChildActiveAsync(string? sessionId, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var child = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (child is null || child.State == SpeakingSessionState.Finished) return;
        child.State = SpeakingSessionState.Active;
        child.RolePlayStartedAt ??= now;
        child.UpdatedAt = now;
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

    /// <summary>Debits credit for a card at reveal, idempotent on the
    /// exam+slot reference. AI mode only. Stores the ref on the exam so a
    /// retried transition never double-charges.
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

        var refId = $"exam:{exam.Id}:card{slot.ToUpperInvariant()}";
        var debit = await creditService.DeductGradingCreditAsync(exam.UserId, "speaking", refId, ct);
        if (!debit.Debited)
        {
            // already_debited is benign (a retried transition); anything else is
            // a genuine insufficient-credit refusal.
            if (string.Equals(debit.ErrorCode, "already_debited", StringComparison.Ordinal))
            {
                if (slot == "a") exam.CreditARefId = refId; else exam.CreditBRefId = refId;
                return;
            }
            throw ApiException.PaymentRequired(
                debit.ErrorCode ?? "no_ai_package_credits",
                debit.ErrorMessage ?? "You have no credits remaining. Purchase a package to continue.");
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

    private async Task<SpeakingExamDetail> ProjectAsync(SpeakingExamSession exam, DateTimeOffset now, CancellationToken ct)
    {
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

        return new SpeakingExamDetail(
            ExamId: exam.Id,
            Mode: SpeakingExamModes.ToCode(exam.Mode),
            State: stage,
            ProfessionId: exam.ProfessionId,
            CurrentCardNumber: currentCardNumber,
            CurrentSessionId: currentSessionId,
            CurrentCard: card,
            Clock: new SpeakingExamClock(stage, now, stageStart, stageEnds, secondsRemaining, expired),
            CompletedAt: exam.CompletedAt);
    }
}
