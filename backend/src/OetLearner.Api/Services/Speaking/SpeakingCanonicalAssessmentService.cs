using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Services.Speaking;

public sealed record SpeakingFinalizationTicket(
    string OperationId,
    string SessionId,
    bool AlreadyExisted,
    AiOperationState State);

public interface ISpeakingCanonicalAssessmentService
{
    string ComputeIdentityHash(string sessionId, string cardId, string transcriptHash, string rubricVersion, string promptVersion);

    Task<SpeakingFinalizationTicket> EnqueueAsync(string sessionId, CancellationToken ct);

    Task ExecuteQueuedAsync(string operationId, CancellationToken ct);

    Task AssessNowAsync(string sessionId, CancellationToken ct);

    /// <summary>Queues the one combined judgement of a finished AI Full Mock: one durable operation per exam (idempotent).
    /// The ticket's <c>SessionId</c> carries the exam id.</summary>
    Task<SpeakingFinalizationTicket> EnqueueExamCombinedAsync(string examId, CancellationToken ct);

    /// <summary>Puts a failed combined judgement back in the worker's queue; a no-op while it is queued, running or done.</summary>
    Task RetryExamCombinedAsync(string examId, CancellationToken ct);

    /// <summary><c>failed</c> when the combined judgement ended in a terminal failure, otherwise <c>pending</c>.</summary>
    Task<string> GetExamCombinedStateAsync(string examId, CancellationToken ct);

    /// <summary>True only when the session already carries a complete v1.1
    /// report (history); every new session is scored by the classic assessor.</summary>
    Task<bool> UsesV11Async(string sessionId, CancellationToken ct);

    /// <summary>Learner-facing grading state for
    /// <c>GET /v1/speaking/sessions/{id}/results</c>.</summary>
    Task<SpeakingAssessmentState> GetStateAsync(string sessionId, CancellationToken ct);
}

/// <param name="AssessmentState"><c>processing</c> | <c>completed</c> | <c>failed</c>.</param>
/// <param name="Retryable">True when <c>POST /ai-assess</c> may be called again (no extra charge).</param>
/// <param name="FailureReason">Learner-safe reason when failed.</param>
public sealed record SpeakingAssessmentState(string AssessmentState, bool Retryable, string? FailureReason)
{
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>
/// W7 — one durable Speaking assessment operation per session. Competing
/// TimeUp / POST / result-page callers enqueue the same slot; the worker
/// (or AssessNow) runs the existing classic or v1.1 scorer exactly once.
/// </summary>
public sealed class SpeakingCanonicalAssessmentService(
    LearnerDbContext db,
    SpeakingAiAssessmentService classic,
    SpeakingSimulationV11AssessmentService v11,
    TimeProvider clock,
    ILogger<SpeakingCanonicalAssessmentService> logger,
    IAiCreditReservationService? creditReservations = null) : ISpeakingCanonicalAssessmentService
{
    public const string FeatureCode = AiFeatureCodes.SpeakingGrade;
    public const string PromptVersion = SpeakingAiAssessmentService.PromptTemplateId;

    /// <summary>Resource type of the one durable operation that grades a Full Mock as a single performance.</summary>
    public const string ExamResourceType = "speaking_exam";

    private const string NoTranscriptErrorCode = "speaking_session_no_transcript";
    // Mode-neutral on purpose: a live voice role-play has no recording to point at.
    private const string GradingFailedMessage =
        "We couldn't finish grading your role-play. Try grading again. You won't be charged twice.";

    /// <summary>How long an assessment may wait for a recorder-fallback
    /// transcript before it is reported as failed.</summary>
    private static readonly TimeSpan TranscriptWait = TimeSpan.FromHours(1);

    public string ComputeIdentityHash(
        string sessionId,
        string cardId,
        string transcriptHash,
        string rubricVersion,
        string promptVersion)
        => HashIdentity(sessionId, cardId, transcriptHash, rubricVersion, promptVersion);

    public static string HashIdentity(
        string sessionId,
        string cardId,
        string transcriptHash,
        string rubricVersion,
        string promptVersion)
    {
        var canonical = string.Join('|',
            (sessionId ?? string.Empty).Trim().ToLowerInvariant(),
            (cardId ?? string.Empty).Trim().ToLowerInvariant(),
            (transcriptHash ?? string.Empty).Trim().ToLowerInvariant(),
            (rubricVersion ?? string.Empty).Trim().ToLowerInvariant(),
            (promptVersion ?? string.Empty).Trim().ToLowerInvariant());
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string HashTranscript(string? text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<SpeakingFinalizationTicket> EnqueueAsync(string sessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw ApiException.NotFound("speaking_session_not_found", "That Speaking session does not exist.");

        var existing = await db.AiOperations
            .FirstOrDefaultAsync(
                o => o.FeatureCode == FeatureCode
                    && o.ResourceType == "speaking_session"
                    && o.ResourceId == sessionId,
                ct);
        if (existing is not null)
        {
            return new SpeakingFinalizationTicket(existing.Id, sessionId, true, existing.State);
        }

        var now = clock.GetUtcNow();
        var operation = new AiOperation
        {
            Id = Guid.NewGuid().ToString("N"),
            Module = "speaking",
            FeatureCode = FeatureCode,
            UserId = session.UserId,
            ResourceType = "speaking_session",
            ResourceId = sessionId,
            IdempotencyKey = $"speaking.assess:{sessionId}",
            ResourceSlotKey = AiOperationResourceSlot.Build(
                FeatureCode, "speaking", session.UserId, sessionId, "speaking_session",
                resourceVersion: null, PromptVersion, session.RulebookVersion),
            State = AiOperationState.Queued,
            OperationClass = AiOperationClass.ScoringCritical,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AiOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var raced = await db.AiOperations
                .FirstOrDefaultAsync(
                    o => o.FeatureCode == FeatureCode
                        && o.ResourceType == "speaking_session"
                        && o.ResourceId == sessionId,
                    ct);
            if (raced is not null)
            {
                return new SpeakingFinalizationTicket(raced.Id, sessionId, true, raced.State);
            }

            throw;
        }

        return new SpeakingFinalizationTicket(operation.Id, sessionId, false, operation.State);
    }

    public async Task ExecuteQueuedAsync(string operationId, CancellationToken ct)
    {
        var row = await db.AiOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.ResourceId)) return;
        if (row.State is AiOperationState.Completed or AiOperationState.ProviderSucceeded) return;

        try
        {
            // The worker already holds this operation's lease: run it directly,
            // never through the direct-run claim (which would see that live
            // lease and decline, leaving the session ungraded).
            if (string.Equals(row.ResourceType, ExamResourceType, StringComparison.Ordinal))
            {
                await AssessExamCoreAsync(row.ResourceId, ct);
            }
            else
            {
                await AssessCoreAsync(row.ResourceId, claim: false, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AssessNowAsync already persisted the outcome (FailedTerminal or
            // a scheduled retry) and released the lease; rethrowing would
            // abort the worker's whole claimed batch.
        }
    }

    public Task AssessNowAsync(string sessionId, CancellationToken ct)
        => AssessCoreAsync(sessionId, claim: true, ct);

    // ── The combined Full Mock judgement (one durable operation per exam) ─────────────────

    public async Task<SpeakingFinalizationTicket> EnqueueExamCombinedAsync(string examId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(examId);

        var exam = await db.SpeakingExamSessions.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == examId, ct)
            ?? throw ApiException.NotFound("speaking_exam_not_found", "That Speaking exam does not exist.");

        var existing = await FindExamOperationAsync(examId, ct);
        if (existing is not null)
        {
            return new SpeakingFinalizationTicket(existing.Id, examId, true, existing.State);
        }

        var now = clock.GetUtcNow();
        var operation = new AiOperation
        {
            Id = Guid.NewGuid().ToString("N"),
            Module = "speaking",
            FeatureCode = FeatureCode,
            UserId = exam.UserId,
            ResourceType = ExamResourceType,
            ResourceId = examId,
            IdempotencyKey = $"speaking.assess.exam:{examId}",
            ResourceSlotKey = AiOperationResourceSlot.Build(
                FeatureCode, "speaking", exam.UserId, examId, ExamResourceType,
                resourceVersion: null, SpeakingAiAssessmentService.CombinedPromptTemplateId, exam.RulebookVersion),
            State = AiOperationState.Queued,
            OperationClass = AiOperationClass.ScoringCritical,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AiOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another caller queued it first. Only this row is detached: the caller may hold other tracked entities.
            db.Entry(operation).State = EntityState.Detached;
            var raced = await FindExamOperationAsync(examId, ct);
            if (raced is not null)
            {
                return new SpeakingFinalizationTicket(raced.Id, examId, true, raced.State);
            }

            throw;
        }

        return new SpeakingFinalizationTicket(operation.Id, examId, false, operation.State);
    }

    public async Task RetryExamCombinedAsync(string examId, CancellationToken ct)
    {
        var operation = await FindExamOperationAsync(examId, ct);
        if (operation is null)
        {
            await EnqueueExamCombinedAsync(examId, ct);
            return;
        }

        // Only a finished-and-failed operation is put back in the queue; queued, running or done ones are left alone.
        if (operation.State is AiOperationState.FailedTerminal or AiOperationState.Indeterminate)
        {
            operation.State = AiOperationState.Queued;
            operation.NextAttemptAt = null;
            operation.LeaseOwner = null;
            operation.LeaseExpiresAt = null;
            operation.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<string> GetExamCombinedStateAsync(string examId, CancellationToken ct)
    {
        var state = await db.AiOperations.AsNoTracking()
            .Where(o => o.FeatureCode == FeatureCode && o.ResourceType == ExamResourceType && o.ResourceId == examId)
            .Select(o => (AiOperationState?)o.State)
            .FirstOrDefaultAsync(ct);
        return state == AiOperationState.FailedTerminal ? SpeakingExamCombinedStates.Failed : SpeakingExamCombinedStates.Pending;
    }

    private Task<AiOperation?> FindExamOperationAsync(string examId, CancellationToken ct)
        => db.AiOperations.FirstOrDefaultAsync(
            o => o.FeatureCode == FeatureCode && o.ResourceType == ExamResourceType && o.ResourceId == examId, ct);

    private async Task AssessExamCoreAsync(string examId, CancellationToken ct)
    {
        var ticket = await EnqueueExamCombinedAsync(examId, ct);
        try
        {
            await classic.RunCombinedAssessmentAsync(examId, ct);
            await MarkOperationAsync(ticket.OperationId, AiOperationState.Completed, nextAttemptAt: null, CancellationToken.None);
        }
        catch (ApiException ex) when (ex.ErrorCode is NoTranscriptErrorCode or SpeakingAiAssessmentService.ExamCardsNotGradedCode)
        {
            // A card's transcript or grade is still on its way: look again shortly, and give up after an hour.
            var op = await db.AiOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == ticket.OperationId, ct);
            var expired = op is not null && clock.GetUtcNow() - op.CreatedAt > TranscriptWait;
            await MarkOperationAsync(
                ticket.OperationId,
                expired ? AiOperationState.FailedTerminal : AiOperationState.RetryScheduled,
                expired ? null : clock.GetUtcNow().AddMinutes(1),
                ct);
            throw;
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Combined Speaking assessment interrupted for exam {ExamId}; requeued.", examId);
            await MarkOperationAsync(ticket.OperationId, AiOperationState.RetryScheduled, clock.GetUtcNow(), CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Combined Speaking assessment failed for exam {ExamId}.", examId);
            // Terminal for this run but learner-retryable (the results page offers "Try again", no charge).
            await MarkOperationAsync(ticket.OperationId, AiOperationState.FailedTerminal, nextAttemptAt: null, CancellationToken.None);
            throw;
        }
    }

    /// <summary>After a card is graded: when it belongs to an AI exam whose two cards are now both graded by the classic
    /// assessor, queue the combined judgement. Never fails the card's own grade.</summary>
    private async Task TryEnqueueExamCombinedAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var exam = await db.SpeakingExamSessions.AsNoTracking()
                .Where(e => e.Mode == SpeakingExamMode.Ai && (e.SessionAId == sessionId || e.SessionBId == sessionId))
                .Select(e => new { e.Id, e.SessionAId, e.SessionBId, e.CombinedAssessmentJson })
                .FirstOrDefaultAsync(ct);
            if (exam is null
                || !string.IsNullOrEmpty(exam.CombinedAssessmentJson)
                || string.IsNullOrWhiteSpace(exam.SessionAId)
                || string.IsNullOrWhiteSpace(exam.SessionBId))
            {
                return;
            }

            var graded = await db.SpeakingAiAssessments.AsNoTracking()
                .Where(a => a.SpeakingSessionId == exam.SessionAId || a.SpeakingSessionId == exam.SessionBId)
                .Select(a => a.SpeakingSessionId)
                .Distinct()
                .CountAsync(ct);
            if (graded == 2) await EnqueueExamCombinedAsync(exam.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not queue the combined Full Mock judgement after session {SessionId}.", sessionId);
        }
    }


    private async Task AssessCoreAsync(string sessionId, bool claim, CancellationToken ct)
    {
        var ticket = await EnqueueAsync(sessionId, ct);
        // Exactly one runner per session: /submit hands the operation to the
        // worker and the page then calls /ai-assess, so both used to grade at
        // once (production 25 Sep 2026: duplicate v1.1 turn-evidence rows, 500).
        // Whoever loses the claim simply leaves the running/finished grade alone.
        if (claim && !await TryClaimDirectRunAsync(ticket.OperationId, ct)) return;
        try
        {
            if (await UsesV11Async(sessionId, ct))
            {
                await v11.RunAssessmentAsync(sessionId, ct);
            }
            else
            {
                await classic.RunAssessmentAsync(sessionId, ct);
            }

            await MarkOperationAsync(ticket.OperationId, AiOperationState.Completed, nextAttemptAt: null, CancellationToken.None);
            if (creditReservations is not null)
            {
                await SpeakingCreditSettlement.CommitIfGradedAsync(db, creditReservations, sessionId, ct);
            }

            // The second card of a Full Mock: queue the one combined judgement of the whole test.
            await TryEnqueueExamCombinedAsync(sessionId, ct);
        }
        catch (ApiException ex) when (ex.ErrorCode == NoTranscriptErrorCode)
        {
            // Recorder fallback: the transcript is still being produced. The
            // transcription queue re-runs this assessment once it lands; the
            // scheduled retry is only a backstop for a lost hand-off.
            var op = await db.AiOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == ticket.OperationId, ct);
            var expired = op is not null && clock.GetUtcNow() - op.CreatedAt > TranscriptWait;
            await MarkOperationAsync(
                ticket.OperationId,
                expired ? AiOperationState.FailedTerminal : AiOperationState.RetryScheduled,
                expired ? null : clock.GetUtcNow().AddMinutes(1),
                ct);
            throw;
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            // The caller went away mid-grade (request aborted, host shutting
            // down). Hand the grade straight back to the worker instead of
            // leaving it leased for 30 minutes (production 26 Sep 2026: exam
            // Card B cancelled and stuck). The caller's token is dead, so the
            // bookkeeping must not use it.
            logger.LogWarning(ex, "Speaking canonical assessment interrupted for session {SessionId}; requeued.", sessionId);
            await MarkOperationAsync(ticket.OperationId, AiOperationState.RetryScheduled, clock.GetUtcNow(), CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Speaking canonical assessment failed for session {SessionId}.", sessionId);
            // Terminal for this run but learner-retryable: POST /ai-assess
            // re-runs it and the credit hold is still only committed once.
            await MarkOperationAsync(ticket.OperationId, AiOperationState.FailedTerminal, nextAttemptAt: null, CancellationToken.None);
            throw;
        }
    }

    private async Task<bool> TryClaimDirectRunAsync(string operationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var seen = await db.AiOperations.AsNoTracking()
            .Where(o => o.Id == operationId)
            .Select(o => new { o.State, o.LeaseOwner, o.LeaseExpiresAt })
            .FirstOrDefaultAsync(ct);
        var claimable = seen is not null
            && (seen.State is AiOperationState.Queued or AiOperationState.RetryScheduled
                    or AiOperationState.FailedTerminal or AiOperationState.Indeterminate
                || (seen.State == AiOperationState.Leased && (seen.LeaseExpiresAt is null || seen.LeaseExpiresAt < now)));
        if (!claimable) return false;

        var owner = $"assess-now:{Guid.NewGuid():N}";
        // Same lease as the worker: max-reasoning grading of a full 5-minute
        // role-play takes ~11-12 min, and a 10-minute lease let the worker take
        // over mid-grade, hit ai_operation_in_flight and mark it FailedTerminal
        // (production 26 Sep 2026: "Grading could not be completed" shown while
        // the original run was still finishing).
        var leaseUntil = now.Add(AiOperationWorker.LeaseDuration);
        if (db.Database.IsRelational())
        {
            // Compare-and-swap on the state + lease we just read, so exactly one
            // concurrent claimer (worker or request) wins.
            return await db.AiOperations
                .Where(o => o.Id == operationId && o.State == seen!.State && o.LeaseOwner == seen.LeaseOwner)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(o => o.State, AiOperationState.Leased)
                    .SetProperty(o => o.LeaseOwner, owner)
                    .SetProperty(o => o.LeaseExpiresAt, leaseUntil)
                    .SetProperty(o => o.UpdatedAt, now), ct) == 1;
        }

        // In-memory test provider: no ExecuteUpdate; single-threaded anyway.
        var op = await db.AiOperations.FirstAsync(o => o.Id == operationId, ct);
        op.State = AiOperationState.Leased;
        op.LeaseOwner = owner;
        op.LeaseExpiresAt = leaseUntil;
        op.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// True only for a session that ALREADY has a complete v1.1 card report, so that history stays
    /// readable. Owner spec 4 Oct 2026: the nine official OET criteria are the only scoring model —
    /// the ten weighted v1.1 criteria (one of them invented) never score new work, whatever the
    /// release-gate approvals say. Every new session is therefore graded by the classic assessor.
    /// </summary>
    public async Task<bool> UsesV11Async(string sessionId, CancellationToken ct)
        => await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .AnyAsync(a => a.SpeakingSessionId == sessionId
                && a.AssessmentKind == "card"
                && a.Status == SpeakingSimulationV11AssessmentStatus.Complete, ct);

    public async Task<SpeakingAssessmentState> GetStateAsync(string sessionId, CancellationToken ct)
    {
        if (await SpeakingCreditSettlement.IsGradedAsync(db, sessionId, ct))
        {
            return new SpeakingAssessmentState(SpeakingAssessmentState.Completed, false, null);
        }

        // Ordered in memory: SQLite cannot ORDER BY a DateTimeOffset, and a
        // session only has a handful of these rows.
        var v11Latest = (await db.SpeakingSimulationV11Assessments.AsNoTracking()
                .Where(a => a.SpeakingSessionId == sessionId && a.AssessmentKind == "card")
                .ToListAsync(ct))
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefault();
        if (v11Latest is { Status: SpeakingSimulationV11AssessmentStatus.TechnicalReview or SpeakingSimulationV11AssessmentStatus.Invalid })
        {
            return new SpeakingAssessmentState(SpeakingAssessmentState.Failed, true,
                "Your role-play could not be scored automatically. Try grading again.");
        }

        var transcripts = await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.SpeakingSessionId == sessionId)
            .ToListAsync(ct);
        var head = transcripts.OrderByDescending(t => t.GeneratedAt).FirstOrDefault();
        if (head is not null
            && head.Provider == SpeakingTranscriptionPipeline.StateFailed
            && !transcripts.Any(t => t.IsLatest))
        {
            var noSpeech = SpeakingTranscriptionPipeline.ReadFailureField(head.SegmentsJson, "reasonCode") == "no_speech";
            return new SpeakingAssessmentState(
                SpeakingAssessmentState.Failed,
                Retryable: !noSpeech,
                FailureReason: noSpeech
                    ? "No speech could be detected in the recording."
                    : "We couldn't transcribe your recording. Try grading again.");
        }

        var operation = await db.AiOperations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.FeatureCode == FeatureCode
                && o.ResourceType == "speaking_session"
                && o.ResourceId == sessionId, ct);
        if (operation?.State is AiOperationState.FailedTerminal)
        {
            return new SpeakingAssessmentState(SpeakingAssessmentState.Failed, true, GradingFailedMessage);
        }

        return new SpeakingAssessmentState(SpeakingAssessmentState.Processing, false, null);
    }

    private async Task MarkOperationAsync(
        string operationId,
        AiOperationState state,
        DateTimeOffset? nextAttemptAt,
        CancellationToken ct)
    {
        try
        {
            var op = await db.AiOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);
            if (op is null) return;
            op.State = state;
            op.NextAttemptAt = nextAttemptAt;
            op.LeaseOwner = null;
            op.LeaseExpiresAt = null;
            op.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never mask the assessment outcome with a bookkeeping failure.
            logger.LogWarning(ex, "Could not record state {State} on Speaking operation {OperationId}.", state, operationId);
        }
    }
}
