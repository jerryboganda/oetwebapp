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
    ILogger<SpeakingCanonicalAssessmentService> logger) : ISpeakingCanonicalAssessmentService
{
    public const string FeatureCode = AiFeatureCodes.SpeakingGrade;
    public const string PromptVersion = "speaking.score.v2";

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

        await AssessNowAsync(row.ResourceId, ct);

        row = await db.AiOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);
        if (row is null) return;
        row.State = AiOperationState.Completed;
        row.LeaseOwner = null;
        row.LeaseExpiresAt = null;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task AssessNowAsync(string sessionId, CancellationToken ct)
    {
        var ticket = await EnqueueAsync(sessionId, ct);
        var hasV11 = await db.SpeakingSimulationV11PersonaRuntimeSnapshots.AsNoTracking()
            .AnyAsync(x => x.SpeakingSessionId == sessionId, ct);
        try
        {
            if (hasV11)
            {
                await v11.RunAssessmentAsync(sessionId, ct);
            }
            else
            {
                await classic.RunAssessmentAsync(sessionId, ct);
            }

            var op = await db.AiOperations.FirstOrDefaultAsync(o => o.Id == ticket.OperationId, ct);
            if (op is not null && op.State != AiOperationState.Completed)
            {
                op.State = AiOperationState.Completed;
                op.UpdatedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Speaking canonical assessment failed for session {SessionId}.", sessionId);
            throw;
        }
    }
}
