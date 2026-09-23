using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Owner rule (23 Sep 2026): a Speaking practice card costs exactly 2 AI
/// credits and a full AI mock exactly 4, charged once and only for a GRADED
/// result. The credits are held at card reveal (a reservation, which already
/// debits the ledger) and settled here: committed once the result is graded,
/// refunded (released) when the attempt ends ungraded. Every call is
/// idempotent because commit/release are no-ops on a settled reservation.
/// </summary>
public static class SpeakingCreditSettlement
{
    /// <summary>Holds older than this with no graded result are treated as
    /// abandoned and refunded. Long enough for a learner to retry a failed
    /// grading on the same day.</summary>
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromHours(24);

    public static string PracticeReference(string sessionId) => $"practice:{sessionId}";

    /// <summary>A session is graded once a classic AI assessment or a
    /// complete v1.1 card assessment exists for it.</summary>
    public static async Task<bool> IsGradedAsync(LearnerDbContext db, string sessionId, CancellationToken ct)
        => await db.SpeakingAiAssessments.AsNoTracking()
               .AnyAsync(a => a.SpeakingSessionId == sessionId, ct)
           || await db.SpeakingSimulationV11Assessments.AsNoTracking()
               .AnyAsync(a => a.SpeakingSessionId == sessionId
                   && a.AssessmentKind == "card"
                   && a.Status == SpeakingSimulationV11AssessmentStatus.Complete, ct);

    /// <summary>Commits the hold behind <paramref name="sessionId"/> when its
    /// result is graded: a practice card on its own, an exam card only once
    /// BOTH exam cards are graded (the exam result is complete).</summary>
    public static async Task CommitIfGradedAsync(
        LearnerDbContext db,
        IAiCreditReservationService reservations,
        string sessionId,
        CancellationToken ct)
    {
        var session = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return;

        if (string.IsNullOrWhiteSpace(session.ExamSessionId))
        {
            if (session.Mode == SpeakingSessionMode.AiSelfPractice && await IsGradedAsync(db, sessionId, ct))
            {
                await reservations.CommitByBusinessReferenceAsync(PracticeReference(sessionId), ct);
            }
            return;
        }

        var exam = await db.SpeakingExamSessions.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == session.ExamSessionId, ct);
        if (exam is not null)
        {
            await CommitExamIfGradedAsync(db, reservations, exam, ct);
        }
    }

    public static async Task CommitExamIfGradedAsync(
        LearnerDbContext db,
        IAiCreditReservationService reservations,
        SpeakingExamSession exam,
        CancellationToken ct)
    {
        if (exam.Mode != SpeakingExamMode.Ai
            || string.IsNullOrWhiteSpace(exam.SessionAId)
            || string.IsNullOrWhiteSpace(exam.SessionBId)
            || !await IsGradedAsync(db, exam.SessionAId, ct)
            || !await IsGradedAsync(db, exam.SessionBId, ct))
        {
            return;
        }

        foreach (var reference in ExamReferences(exam))
        {
            await reservations.CommitByBusinessReferenceAsync(reference, ct);
        }
    }

    /// <summary>Refunds the exam's card holds (cancelled exam = no result).</summary>
    public static async Task ReleaseExamAsync(
        LearnerDbContext db,
        IAiCreditReservationService reservations,
        SpeakingExamSession exam,
        CancellationToken ct)
    {
        foreach (var reference in ExamReferences(exam))
        {
            await ReleaseAsync(db, reservations, reference, ct);
        }
    }

    public static async Task ReleaseAsync(
        LearnerDbContext db,
        IAiCreditReservationService reservations,
        string businessReference,
        CancellationToken ct)
    {
        var reservationId = await db.AiCreditReservations.AsNoTracking()
            .Where(r => r.BusinessReference == businessReference)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);
        if (reservationId is not null)
        {
            await reservations.ReleaseAsync(reservationId, ct);
        }
    }

    /// <summary>
    /// Sweeps Speaking holds still Reserved after <see cref="AbandonAfter"/>:
    /// graded ones are committed (a late result), the rest are refunded
    /// (abandoned, or grading never succeeded). Returns the number settled.
    /// </summary>
    public static async Task<int> SettleStaleHoldsAsync(
        LearnerDbContext db,
        IAiCreditReservationService reservations,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var cutoff = now - AbandonAfter;
        var query = db.AiCreditReservations.AsNoTracking()
            .Where(r => r.State == AiCreditReservationState.Reserved
                && (r.BusinessReference.StartsWith("practice:") || r.BusinessReference.StartsWith("exam:")));
        if (db.Database.IsNpgsql())
        {
            query = query.Where(r => r.CreatedAt < cutoff);
        }
        // SQLite cannot compare DateTimeOffset in SQL; the age filter is
        // re-applied in memory for every provider.
        var stale = (await query.Take(500).ToListAsync(ct))
            .Where(r => r.CreatedAt < cutoff)
            .ToList();

        var settled = 0;
        foreach (var hold in stale)
        {
            if (await IsHoldGradedAsync(db, hold.BusinessReference, ct))
            {
                await reservations.CommitAsync(hold.Id, ct);
            }
            else
            {
                await reservations.ReleaseAsync(hold.Id, ct);
            }
            settled++;
        }
        return settled;
    }

    private static async Task<bool> IsHoldGradedAsync(LearnerDbContext db, string reference, CancellationToken ct)
    {
        if (reference.StartsWith("practice:", StringComparison.Ordinal))
        {
            return await IsGradedAsync(db, reference["practice:".Length..], ct);
        }

        // exam:{examId}:cardA / exam:{examId}:cardB — the exam is charged
        // only when both cards were graded.
        var parts = reference.Split(':');
        if (parts.Length < 3) return false;
        var exam = await db.SpeakingExamSessions.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == parts[1], ct);
        return exam is not null
            && !string.IsNullOrWhiteSpace(exam.SessionAId)
            && !string.IsNullOrWhiteSpace(exam.SessionBId)
            && await IsGradedAsync(db, exam.SessionAId, ct)
            && await IsGradedAsync(db, exam.SessionBId, ct);
    }

    private static IEnumerable<string> ExamReferences(SpeakingExamSession exam)
        => new[] { exam.CreditARefId, exam.CreditBRefId }
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .Distinct(StringComparer.Ordinal);
}
