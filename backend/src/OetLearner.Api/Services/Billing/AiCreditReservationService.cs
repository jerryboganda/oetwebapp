using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

public sealed record AiCreditReservationResult(
    bool Granted,
    string? ReservationId,
    string? DenyReason,
    AiCreditReservationState? State = null);

public interface IAiCreditReservationService
{
    Task<AiCreditReservationResult> ReserveAsync(
        string userId,
        string operationId,
        string subtest,
        int units,
        string businessReference,
        CancellationToken ct);

    Task CommitAsync(string reservationId, CancellationToken ct);

    Task ReleaseAsync(string reservationId, CancellationToken ct);
}

/// <summary>
/// Two-phase learner-credit hold for an <see cref="AiOperation"/>. The
/// Dedicated → Flexible W/S → 2× Shared priority lives entirely inside
/// <see cref="IAiPackageCreditService"/>; this service only sequences
/// check → deduct → persist, and refunds on release.
/// </summary>
public sealed class AiCreditReservationService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiCreditReservationService> logger) : IAiCreditReservationService
{
    public async Task<AiCreditReservationResult> ReserveAsync(
        string userId,
        string operationId,
        string subtest,
        int units,
        string businessReference,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subtest);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessReference);
        if (units <= 0) return new(false, null, "invalid_units");

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var sp = dbScope.ServiceProvider;
            var db = sp.GetRequiredService<LearnerDbContext>();
            var credits = sp.GetRequiredService<IAiPackageCreditService>();

            var existing = await db.AiCreditReservations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.BusinessReference == businessReference, ct);
            if (existing is not null)
            {
                var granted = existing.State is AiCreditReservationState.Reserved or AiCreditReservationState.Committed;
                return new(granted, existing.Id, granted ? null : "already_released", existing.State);
            }

            var check = await credits.CheckGradingCreditAsync(userId, subtest, units, ct);
            if (!check.Debited)
            {
                return new(false, null, check.ErrorCode ?? "ai_credits_insufficient");
            }

            var deduct = await credits.DeductGradingCreditAsync(userId, subtest, businessReference, units, ct);
            if (!deduct.Debited)
            {
                return new(false, null, deduct.ErrorCode ?? "ai_credits_insufficient");
            }

            var now = DateTimeOffset.UtcNow;
            var row = new AiCreditReservation
            {
                Id = Guid.NewGuid().ToString("N"),
                OperationId = operationId,
                UserId = userId,
                BucketKind = string.IsNullOrWhiteSpace(deduct.BalanceSource) ? "grading" : Truncate(deduct.BalanceSource!, 32),
                Units = units,
                State = AiCreditReservationState.Reserved,
                BusinessReference = businessReference,
                CreatedAt = now,
                UpdatedAt = now,
            };

            try
            {
                db.AiCreditReservations.Add(row);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception persistEx)
            {
                logger.LogError(persistEx,
                    "AiCreditReservationService.ReserveAsync failed to persist reservation for {Reference}; refunding the debit.",
                    businessReference);
                try
                {
                    await credits.RefundAsync(
                        userId,
                        businessReference,
                        $"{businessReference}:release",
                        "Reservation persist failed.",
                        CancellationToken.None);
                }
                catch (Exception refundEx)
                {
                    logger.LogError(refundEx,
                        "AiCreditReservationService.ReserveAsync could not refund {Reference} after persist failure.",
                        businessReference);
                }

                return new(false, null, "credit_store_unavailable");
            }

            return new(true, row.Id, null, row.State);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AiCreditReservationService.ReserveAsync failed for user {UserId}; refusing the hold.", userId);
            return new(false, null, "credit_store_unavailable");
        }
    }

    public async Task CommitAsync(string reservationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reservationId)) return;

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var row = await db.AiCreditReservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct);
            if (row is null) return;
            if (row.State != AiCreditReservationState.Reserved) return;

            row.State = AiCreditReservationState.Committed;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AiCreditReservationService.CommitAsync failed for {ReservationId}.", reservationId);
        }
    }

    public async Task ReleaseAsync(string reservationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reservationId)) return;

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var sp = dbScope.ServiceProvider;
            var db = sp.GetRequiredService<LearnerDbContext>();
            var credits = sp.GetRequiredService<IAiPackageCreditService>();

            var row = await db.AiCreditReservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct);
            if (row is null) return;
            if (row.State != AiCreditReservationState.Reserved) return;

            await credits.RefundAsync(
                row.UserId,
                row.BusinessReference,
                $"{row.BusinessReference}:release",
                "AI credit reservation released.",
                ct);

            row.State = AiCreditReservationState.Released;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AiCreditReservationService.ReleaseAsync failed for {ReservationId}.", reservationId);
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
