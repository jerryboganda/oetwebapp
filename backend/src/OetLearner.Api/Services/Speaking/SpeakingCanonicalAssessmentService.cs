using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>True when the session is scored by the v1.1 simulation
    /// assessor rather than the classic one.</summary>
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
    IAiCreditReservationService? creditReservations = null,
    SpeakingSimulationV11ReleaseGate? v11ReleaseGate = null) : ISpeakingCanonicalAssessmentService
{
    public const string FeatureCode = AiFeatureCodes.SpeakingGrade;
    public const string PromptVersion = "speaking.score.v2";

    private const string NoTranscriptErrorCode = "speaking_session_no_transcript";
    private const string GradingFailedMessage =
        "We couldn't finish grading this recording. Try grading again. You won't be charged twice.";

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
            await AssessNowAsync(row.ResourceId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AssessNowAsync already persisted the outcome (FailedTerminal or
            // a scheduled retry) and released the lease; rethrowing would
            // abort the worker's whole claimed batch.
        }
    }

    public async Task AssessNowAsync(string sessionId, CancellationToken ct)
    {
        var ticket = await EnqueueAsync(sessionId, ct);
        // Exactly one runner per session: /submit hands the operation to the
        // worker and the page then calls /ai-assess, so both used to grade at
        // once (production 25 Sep 2026: duplicate v1.1 turn-evidence rows, 500).
        // Whoever loses the claim simply leaves the running/finished grade alone.
        if (!await TryClaimDirectRunAsync(ticket.OperationId, ct)) return;
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

            await MarkOperationAsync(ticket.OperationId, AiOperationState.Completed, nextAttemptAt: null, ct);
            if (creditReservations is not null)
            {
                await SpeakingCreditSettlement.CommitIfGradedAsync(db, creditReservations, sessionId, ct);
            }
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Speaking canonical assessment failed for session {SessionId}.", sessionId);
            // Terminal for this run but learner-retryable: POST /ai-assess
            // re-runs it and the credit hold is still only committed once.
            await MarkOperationAsync(ticket.OperationId, AiOperationState.FailedTerminal, nextAttemptAt: null, ct);
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
        var leaseUntil = now.AddMinutes(10);
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

    public async Task<bool> UsesV11Async(string sessionId, CancellationToken ct)
    {
        // A recorder-fallback recording has no v1.1 turn evidence (that only
        // comes from the realtime voice loop), so it is always scored by the
        // classic assessor from its server-side transcript.
        var recorderFallbackId = SpeakingSessionRecordingService.RecordingIdFor(sessionId);
        var v11Evidence = await db.SpeakingSimulationV11PersonaRuntimeSnapshots.AsNoTracking()
                   .AnyAsync(x => x.SpeakingSessionId == sessionId, ct)
               && !await db.SpeakingRecordings.AsNoTracking()
                   .AnyAsync(r => r.Id == recorderFallbackId, ct);
        if (!v11Evidence || v11ReleaseGate is null) return v11Evidence;

        // The v1.1 scorer only runs once the owner has approved its release for
        // the profession; until then it refuses every session (409), so a live
        // voice role-play would never get a result. Score those from the saved
        // live transcript with the classic rulebook assessor instead.
        var professionId = await db.SpeakingSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Join(db.RolePlayCards, s => s.RolePlayCardId, c => c.Id, (s, c) => c.ProfessionId)
            .FirstOrDefaultAsync(ct);
        return professionId is not null
            && (await v11ReleaseGate.EvaluateAsync(professionId, ct)).IsReleased;
    }

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
                "Your recording could not be scored automatically. Try grading again.");
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
