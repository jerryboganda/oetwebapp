using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services.Ai;

public sealed record AiCreditReservationTicket(
    string ReservationId,
    string OperationId,
    string BucketKind,
    int Units,
    AiCreditReservationState State,
    bool AlreadyExisted);

public interface IAiCreditReservationService
{
    /// <summary>
    /// Hold Writing → Flexible W/S → 2× Shared for one writing grade.
    /// Idempotent on <paramref name="businessReference"/>.
    /// The funding rule lives in the CreditLedger
    /// (<see cref="Billing.IAiPackageCreditService"/>); this method holds
    /// admission policy only, never cost math.
    /// </summary>
    Task<AiCreditReservationTicket> ReserveWritingAsync(
        string userId,
        string operationId,
        string businessReference,
        CancellationToken ct);

    Task<AiCreditReservationTicket> ReserveSpeakingAsync(
        string userId,
        string operationId,
        string businessReference,
        CancellationToken ct);

    Task CommitAsync(string reservationId, CancellationToken ct);

    Task CommitByBusinessReferenceAsync(string businessReference, CancellationToken ct);

    Task ReleaseAsync(string reservationId, CancellationToken ct);
}

/// <summary>
/// W6 two-phase learner credit hold. Deducts via the package ledger on reserve
/// (idempotent on business reference), commits the reservation row on delivery,
/// and refunds on terminal system failure.
/// </summary>
public sealed class AiCreditReservationService(
    LearnerDbContext db,
    IAiPackageCreditService packageCredits,
    TimeProvider clock) : IAiCreditReservationService
{
    public async Task<AiCreditReservationTicket> ReserveWritingAsync(
        string userId,
        string operationId,
        string businessReference,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessReference);

        var existing = await db.AiCreditReservations
            .FirstOrDefaultAsync(x => x.BusinessReference == businessReference, ct);
        if (existing is not null)
        {
            return new AiCreditReservationTicket(
                existing.Id,
                existing.OperationId,
                existing.BucketKind,
                existing.Units,
                existing.State,
                AlreadyExisted: true);
        }

        // Admission policy (proven codes, kept verbatim): the fundability
        // rule itself lives in the CreditLedger — snapshot flags are
        // ledger-computed, and the atomic debit inside InsertForSubtestAsync
        // re-verifies and decides the funding bucket. This method holds no
        // cost math: no bucket-pick, no Shared/2.
        var snapshot = await packageCredits.GetSnapshotAsync(userId, 0, ct);
        if (snapshot.ExpiredBecausePassed
            || (snapshot.ExpiresAt is { } expires && expires <= clock.GetUtcNow()))
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }

        if (snapshot.WritingUnlimited)
        {
            await EnsureOperationAsync(operationId, userId, businessReference, ct);
            return await InsertRowAsync(
                userId, operationId, businessReference, bucketKind: "writing", units: 0, ct);
        }

        if (!snapshot.HasWritingActivity)
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }

        await EnsureOperationAsync(operationId, userId, businessReference, ct);
        return await InsertForSubtestAsync(userId, operationId, businessReference, "writing", ct);
    }

    public async Task<AiCreditReservationTicket> ReserveSpeakingAsync(
        string userId,
        string operationId,
        string businessReference,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessReference);

        var existing = await db.AiCreditReservations
            .FirstOrDefaultAsync(x => x.BusinessReference == businessReference, ct);
        if (existing is not null)
        {
            return new AiCreditReservationTicket(
                existing.Id,
                existing.OperationId,
                existing.BucketKind,
                existing.Units,
                existing.State,
                AlreadyExisted: true);
        }

        // Admission policy (proven codes, kept verbatim): see ReserveWritingAsync.
        // This method holds no cost math: no bucket-pick, no Shared/2.
        var snapshot = await packageCredits.GetSnapshotAsync(userId, 0, ct);
        if (snapshot.ExpiredBecausePassed
            || (snapshot.ExpiresAt is { } expires && expires <= clock.GetUtcNow()))
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }

        if (snapshot.SpeakingUnlimited)
        {
            await EnsureSpeakingOperationAsync(operationId, userId, businessReference, ct);
            return await InsertRowAsync(
                userId, operationId, businessReference, bucketKind: "speaking", units: 0, ct);
        }

        if (!snapshot.HasSpeakingActivity)
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }

        await EnsureSpeakingOperationAsync(operationId, userId, businessReference, ct);
        return await InsertForSubtestAsync(userId, operationId, businessReference, "speaking", ct);
    }

    public async Task CommitAsync(string reservationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reservationId)) return;
        var row = await db.AiCreditReservations.FirstOrDefaultAsync(x => x.Id == reservationId, ct);
        if (row is null || row.State == AiCreditReservationState.Committed) return;
        if (row.State == AiCreditReservationState.Released) return;
        row.State = AiCreditReservationState.Committed;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task CommitByBusinessReferenceAsync(string businessReference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(businessReference)) return;
        var row = await db.AiCreditReservations
            .FirstOrDefaultAsync(x => x.BusinessReference == businessReference, ct);
        if (row is null) return;
        await CommitAsync(row.Id, ct);
    }

    public async Task ReleaseAsync(string reservationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reservationId)) return;
        var row = await db.AiCreditReservations.FirstOrDefaultAsync(x => x.Id == reservationId, ct);
        if (row is null || row.State == AiCreditReservationState.Released) return;
        if (row.State == AiCreditReservationState.Committed) return;

        if (row.Units > 0)
        {
            await packageCredits.RefundAsync(
                row.UserId,
                originalReferenceId: row.BusinessReference,
                refundReferenceId: $"{row.BusinessReference}:release",
                description: "Writing grade reservation released after terminal failure.",
                ct);
        }

        row.State = AiCreditReservationState.Released;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private async Task<AiCreditReservationTicket> InsertRowAsync(
        string userId,
        string operationId,
        string businessReference,
        string bucketKind,
        int units,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var row = new AiCreditReservation
        {
            Id = Guid.NewGuid().ToString("N"),
            OperationId = operationId,
            UserId = userId,
            BucketKind = bucketKind,
            Units = units,
            State = AiCreditReservationState.Reserved,
            BusinessReference = businessReference,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AiCreditReservations.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            var raced = await db.AiCreditReservations
                .FirstOrDefaultAsync(x => x.BusinessReference == businessReference, ct);
            if (raced is not null)
            {
                return new AiCreditReservationTicket(
                    raced.Id, raced.OperationId, raced.BucketKind, raced.Units, raced.State, true);
            }

            throw;
        }

        return new AiCreditReservationTicket(row.Id, row.OperationId, row.BucketKind, row.Units, row.State, false);
    }

    private async Task EnsureOperationAsync(
        string operationId,
        string userId,
        string businessReference,
        CancellationToken ct)
    {
        var exists = await db.AiOperations.AsNoTracking().AnyAsync(x => x.Id == operationId, ct);
        if (exists) return;

        var now = clock.GetUtcNow();
        db.AiOperations.Add(new AiOperation
        {
            Id = operationId,
            Module = "writing",
            FeatureCode = "writing.score.v1",
            UserId = userId,
            ResourceType = "writing_submission",
            IdempotencyKey = businessReference.Length <= 256 ? businessReference : businessReference[..256],
            State = AiOperationState.Queued,
            OperationClass = AiOperationClass.ScoringCritical,
            CreatedAt = now,
            UpdatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Debited insert: the atomic ledger debit decides which pool funded
    /// the hold, and the row records that outcome (bucket + raw units,
    /// with a defensive 0→1 floor that no real debit produces).
    /// Check-then-debit races resolve here — Deduct re-verifies fundability
    /// atomically and denies with the same caller-visible error.
    /// </summary>
    private async Task<AiCreditReservationTicket> InsertForSubtestAsync(
        string userId,
        string operationId,
        string businessReference,
        string subtest,
        CancellationToken ct)
    {
        var debitResult = await packageCredits.DeductGradingCreditAsync(
            userId, subtest, businessReference, ct);
        if (!debitResult.Debited && !debitResult.Bypassed)
        {
            throw ApiException.PaymentRequired(
                debitResult.ErrorCode ?? "ai_credits_insufficient",
                debitResult.ErrorMessage
                ?? "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }

        var bucketKind = debitResult.BalanceSource switch
        {
            "flexible_ws" => "flexible_ws",
            "shared" => "shared",
            _ => subtest, // dedicated, mixed (unreachable at quantity 1), or sourceless
        };
        var units = debitResult.CreditsUsed > 0 ? debitResult.CreditsUsed : 1;
        return await InsertRowAsync(userId, operationId, businessReference, bucketKind, units, ct);
    }

    private async Task EnsureSpeakingOperationAsync(
        string operationId,
        string userId,
        string businessReference,
        CancellationToken ct)
    {
        var exists = await db.AiOperations.AsNoTracking().AnyAsync(x => x.Id == operationId, ct);
        if (exists) return;

        var now = clock.GetUtcNow();
        db.AiOperations.Add(new AiOperation
        {
            Id = operationId,
            Module = "speaking",
            FeatureCode = AiFeatureCodes.SpeakingGrade,
            UserId = userId,
            ResourceType = "speaking_session",
            IdempotencyKey = businessReference.Length <= 256 ? businessReference : businessReference[..256],
            State = AiOperationState.Queued,
            OperationClass = AiOperationClass.ScoringCritical,
            CreatedAt = now,
            UpdatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }
    }
}
