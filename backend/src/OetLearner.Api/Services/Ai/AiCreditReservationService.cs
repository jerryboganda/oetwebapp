using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// A credit hold. <c>FeedbackMessage</c> is the ledger's "N credits used, M remaining"
/// line (master catalogue Rule E) when THIS call took the debit; null when the hold was
/// reused, adopted or free.
/// </summary>
public sealed record AiCreditReservationTicket(
    string ReservationId,
    string OperationId,
    string BucketKind,
    int Units,
    AiCreditReservationState State,
    bool AlreadyExisted,
    string? FeedbackMessage = null);

public interface IAiCreditReservationService
{
    /// <summary>
    /// Hold 2 AI credits (Writing → Flexible W/S → Shared) for one writing grade.
    /// Idempotent on <paramref name="businessReference"/>: one hold per reference,
    /// reused by every retry of the letter. A reference the start gate already
    /// debited is adopted with no second debit and no balance check (WAI-01).
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

    /// <summary>
    /// Free Mocks: a ZERO-unit "free_sample" hold for the learner's one free
    /// AI-graded Writing sample. Never touches the credit ledger, so it works
    /// for a brand-new zero-credit account. Only ever called after the server
    /// derived the free grant (FreeSampleService claim) — never from a request.
    /// Default = the paid path, so reservation fakes that predate free samples
    /// keep their behaviour; the real service overrides it.
    /// </summary>
    Task<AiCreditReservationTicket> ReserveFreeSampleAsync(
        string userId,
        string operationId,
        string businessReference,
        CancellationToken ct)
        => ReserveWritingAsync(userId, operationId, businessReference, ct);

    Task CommitAsync(string reservationId, CancellationToken ct);

    Task CommitByBusinessReferenceAsync(string businessReference, CancellationToken ct);

    Task ReleaseAsync(string reservationId, CancellationToken ct);
}

/// <summary>
/// W6 two-phase learner credit hold. Deducts via the package ledger on reserve
/// (idempotent on business reference) and commits the reservation row on
/// delivery. Speaking releases (refunds) a hold on terminal failure; a Writing
/// hold is never released: it stays Reserved through every retry until the
/// grade commits it (WAI-01, owner decision 2 Oct 2026).
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

        // A legacy Released hold (refunded by a failure before 2 Oct 2026) is never
        // handed back as funded: it goes through the funding decision below and is
        // re-armed in place only once that decision pays for the grade.
        var existing = await db.AiCreditReservations
            .FirstOrDefaultAsync(x => x.BusinessReference == businessReference, ct);
        if (existing is { State: not AiCreditReservationState.Released })
        {
            return Ticket(existing, alreadyExisted: true);
        }

        // Adopt a reference that is already paid — the start gate debited it when
        // the task opened. BEFORE the balance gate, so the learner whose last
        // credit paid for this very letter is graded instead of refused (402).
        if (await packageCredits.FindGradingDebitAsync(userId, businessReference, ct) is not null)
        {
            return await HoldAsync(existing, userId, operationId, businessReference, bucketKind: "writing", units: 0, ct);
        }

        // Admission policy (proven codes, kept verbatim): the fundability
        // rule itself lives in the CreditLedger — snapshot flags are
        // ledger-computed, and the atomic debit inside InsertForSubtestAsync
        // re-verifies and decides the funding bucket. This method holds no
        // cost math: no bucket-pick, no Shared/2.
        var snapshot = await packageCredits.GetSnapshotAsync(userId, 0, ct);

        // Owner clarification (Writing Rule Enforcement Addendum Rev5, 10 Sep
        // 2026, §12): "If Writing Credits are Unlimited and valid, authorise
        // the attempt immediately with no decrement. The unlimited
        // entitlement itself is sufficient; a zero balance in another pool
        // must not block the attempt." WritingUnlimited must therefore be
        // checked BEFORE the generic account-expiry/insufficient-balance
        // throw below — previously that throw ran unconditionally first, so
        // an account-level expiry/zero-balance state on the shared ledger
        // could block Submit for Grading even while the dashboard still
        // showed Writing Credits as Unlimited (the exact production
        // contradiction reported: "No AI credits remaining" / "Not enough
        // credits" despite an active Unlimited Writing entitlement).
        if (snapshot.WritingUnlimited)
        {
            return await HoldAsync(existing, userId, operationId, businessReference, bucketKind: "writing", units: 0, ct);
        }

        if (snapshot.ExpiredBecausePassed
            || (snapshot.ExpiresAt is { } expires && expires <= clock.GetUtcNow()))
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "Not enough AI credits: one Writing letter or Speaking card costs 2 AI credits. Purchase an AI Credits package to continue.");
        }

        if (!snapshot.HasWritingActivity)
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "Not enough AI credits: one Writing letter or Speaking card costs 2 AI credits. Purchase an AI Credits package to continue.");
        }

        // Nothing has paid this reference yet (a revision, or a letter submitted
        // without opening the task): debit it once under the SAME reference, so a
        // later start gate dedupes to already-debited. Whichever runs first pays.
        await EnsureOperationAsync(operationId, userId, businessReference, ct);
        return await InsertForSubtestAsync(userId, operationId, businessReference, "writing", ct, existing);
    }

    public async Task<AiCreditReservationTicket> ReserveFreeSampleAsync(
        string userId,
        string operationId,
        string businessReference,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessReference);

        // Idempotent on the business reference (retry-grade re-enters here).
        var existing = await db.AiCreditReservations
            .FirstOrDefaultAsync(x => x.BusinessReference == businessReference, ct);
        if (existing is not null)
        {
            return new AiCreditReservationTicket(
                existing.Id, existing.OperationId, existing.BucketKind, existing.Units, existing.State,
                AlreadyExisted: true);
        }

        // FK to AiOperations is Restrict — the operation row must exist first.
        await EnsureOperationAsync(operationId, userId, businessReference, ct);
        return await InsertRowAsync(
            userId, operationId, businessReference, bucketKind: "free_sample", units: 0, ct);
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
        // Unlimited checked before the account-expiry throw for the same
        // reason as ReserveWritingAsync above — keep the two symmetric.
        var snapshot = await packageCredits.GetSnapshotAsync(userId, 0, ct);
        if (snapshot.SpeakingUnlimited)
        {
            await EnsureSpeakingOperationAsync(operationId, userId, businessReference, ct);
            return await InsertRowAsync(
                userId, operationId, businessReference, bucketKind: "speaking", units: 0, ct);
        }

        if (snapshot.ExpiredBecausePassed
            || (snapshot.ExpiresAt is { } expires && expires <= clock.GetUtcNow()))
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "Not enough AI credits: one Writing letter or Speaking card costs 2 AI credits. Purchase an AI Credits package to continue.");
        }

        if (!snapshot.HasSpeakingActivity)
        {
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "Not enough AI credits: one Writing letter or Speaking card costs 2 AI credits. Purchase an AI Credits package to continue.");
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
            // The hold's operation records which subtest it paid for (writing / speaking), so the
            // refund row never says "Writing" for a Speaking card or exam hold.
            var module = await db.AiOperations.AsNoTracking()
                .Where(x => x.Id == row.OperationId)
                .Select(x => x.Module)
                .FirstOrDefaultAsync(ct);
            var subtest = module switch
            {
                "speaking" => "Speaking",
                "writing" => "Writing",
                _ => "AI",
            };
            await packageCredits.RefundAsync(
                row.UserId,
                originalReferenceId: row.BusinessReference,
                refundReferenceId: $"{row.BusinessReference}:release",
                description: $"{subtest} grade reservation released after terminal failure.",
                ct);
        }

        row.State = AiCreditReservationState.Released;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private static AiCreditReservationTicket Ticket(AiCreditReservation row, bool alreadyExisted)
        => new(row.Id, row.OperationId, row.BucketKind, row.Units, row.State, alreadyExisted);

    /// <summary>
    /// Holds the grade on a new row, or re-arms a legacy Released row in place (the
    /// business reference is unique, so a second row cannot exist). A re-armed row
    /// keeps its original operation, which the foreign key already points at.
    /// </summary>
    private async Task<AiCreditReservationTicket> HoldAsync(
        AiCreditReservation? released,
        string userId,
        string operationId,
        string businessReference,
        string bucketKind,
        int units,
        CancellationToken ct)
    {
        if (released is null)
        {
            await EnsureOperationAsync(operationId, userId, businessReference, ct);
            return await InsertRowAsync(userId, operationId, businessReference, bucketKind, units, ct);
        }

        released.BucketKind = bucketKind;
        released.Units = units;
        released.State = AiCreditReservationState.Reserved;
        released.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Ticket(released, alreadyExisted: true);
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
        CancellationToken ct,
        AiCreditReservation? released = null)
    {
        var debitResult = await packageCredits.DeductGradingCreditAsync(
            userId, subtest, businessReference, ct);
        if (!debitResult.Debited && !debitResult.Bypassed)
        {
            throw ApiException.PaymentRequired(
                debitResult.ErrorCode ?? "ai_credits_insufficient",
                debitResult.ErrorMessage
                ?? "Not enough AI credits: one Writing letter or Speaking card costs 2 AI credits. Purchase an AI Credits package to continue.");
        }

        var bucketKind = debitResult.BalanceSource switch
        {
            "flexible_ws" => "flexible_ws",
            "shared" => "shared",
            // dedicated, mixed, or sourceless. "mixed" IS reachable at one activity (2 credits): a lone
            // dedicated credit plus one Flexible credit fund it together (only Shared never pairs with a
            // stranded credit), so the hold is labelled by the subtest and Units carries the real total.
            _ => subtest,
        };
        var units = debitResult.CreditsUsed > 0 ? debitResult.CreditsUsed : 1;
        // The debit above is already committed, so the request's cancellation (client timeout, refresh,
        // deploy drain) must not lose the reservation row: a debit without a row is an orphan that no
        // commit or sweep can ever settle.
        var ticket = released is null
            ? await InsertRowAsync(userId, operationId, businessReference, bucketKind, units, CancellationToken.None)
            : await HoldAsync(released, userId, operationId, businessReference, bucketKind, units, CancellationToken.None);
        // Rule E: hand the "credits used / remaining" line to the caller instead of dropping it.
        return ticket with { FeedbackMessage = debitResult.FeedbackMessage };
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
