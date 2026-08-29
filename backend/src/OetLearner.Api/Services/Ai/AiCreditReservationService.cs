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
            return await InsertAsync(
                userId, operationId, businessReference, bucketKind: "writing", units: 0, debit: false, ct);
        }

        if (!snapshot.HasWritingActivity)
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }

        var bucket = snapshot.WritingOnlyCredits >= 1
            ? "writing"
            : snapshot.FlexibleCredits >= 1
                ? "flexible_ws"
                : "shared";
        var units = bucket == "shared" ? AiGradingCreditCost.SharedWritingOrSpeaking : 1;
        await EnsureOperationAsync(operationId, userId, businessReference, ct);
        return await InsertAsync(userId, operationId, businessReference, bucket, units, debit: true, ct);
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
            return await InsertSpeakingAsync(
                userId, operationId, businessReference, bucketKind: "speaking", units: 0, debit: false, ct);
        }

        if (!snapshot.HasSpeakingActivity)
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }

        var bucket = snapshot.SpeakingOnlyCredits >= 1
            ? "speaking"
            : snapshot.FlexibleCredits >= 1
                ? "flexible_ws"
                : "shared";
        var units = bucket == "shared" ? AiGradingCreditCost.SharedWritingOrSpeaking : 1;
        await EnsureSpeakingOperationAsync(operationId, userId, businessReference, ct);
        return await InsertSpeakingAsync(userId, operationId, businessReference, bucket, units, debit: true, ct);
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

    private async Task<AiCreditReservationTicket> InsertAsync(
        string userId,
        string operationId,
        string businessReference,
        string bucketKind,
        int units,
        bool debit,
        CancellationToken ct)
    {
        if (debit)
        {
            var debitResult = await packageCredits.DeductGradingCreditAsync(
                userId, "writing", businessReference, ct);
            if (!debitResult.Debited && !debitResult.Bypassed)
            {
                throw ApiException.PaymentRequired(
                    debitResult.ErrorCode ?? "ai_credits_insufficient",
                    debitResult.ErrorMessage
                    ?? "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
            }
        }

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

    private Task<AiCreditReservationTicket> InsertSpeakingAsync(
        string userId,
        string operationId,
        string businessReference,
        string bucketKind,
        int units,
        bool debit,
        CancellationToken ct)
        => InsertForSubtestAsync(userId, operationId, businessReference, bucketKind, units, debit, "speaking", ct);

    private async Task<AiCreditReservationTicket> InsertForSubtestAsync(
        string userId,
        string operationId,
        string businessReference,
        string bucketKind,
        int units,
        bool debit,
        string subtest,
        CancellationToken ct)
    {
        if (debit)
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
        }

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
