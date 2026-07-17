using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain.Billing;

namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// Wave A5 — daily sweep that emails carts which have been idle for &gt; 24h.
/// Triggered on the recurring <see cref="OetWithDrHesham.Api.Domain.JobType.BillingAbandonedCartEmail"/>
/// job (cron 03:00 UTC). One email per cart — never re-sends after the first
/// successful Brevo dispatch (tracked via <c>Cart.RecoveryEmailSentAt</c>).
///
/// A second recovery email at +72h with a 10% coupon is on the v2 roadmap;
/// see the TODO inside <see cref="SweepAsync"/>.
/// </summary>
public interface IAbandonedCartRecoveryService
{
    /// <summary>
    /// Find carts where <c>UpdatedAt &lt; now - 24h</c>, status is not converted,
    /// the cart belongs to a known user, and no recovery email has been sent
    /// yet. Dispatches the recovery email and sets <c>RecoveryEmailSentAt</c>.
    /// Returns the number of emails sent.
    /// </summary>
    Task<int> SweepAsync(CancellationToken ct = default);
}

public sealed class AbandonedCartRecoveryService : IAbandonedCartRecoveryService
{
    /// <summary>Carts older than this without checkout are considered abandoned.</summary>
    public static readonly TimeSpan FirstRecoveryAge = TimeSpan.FromHours(24);

    /// <summary>Default cap on carts processed per sweep — protects DB/email volume.</summary>
    public const int DefaultBatchSize = 200;

    private readonly LearnerDbContext _db;
    private readonly IBillingNotificationDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<AbandonedCartRecoveryService>? _logger;

    public AbandonedCartRecoveryService(
        LearnerDbContext db,
        IBillingNotificationDispatcher dispatcher,
        TimeProvider? clock = null,
        ILogger<AbandonedCartRecoveryService>? logger = null)
    {
        _db = db;
        _dispatcher = dispatcher;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var cutoff = now - FirstRecoveryAge;

        // Eligibility: idle &gt; 24h, never-converted, identifiable user, no
        // recovery email yet. Status "converted" is the only terminal state
        // we explicitly exclude; "abandoned" carts are valid recovery targets.
        var query = _db.Carts
            .Where(c => c.UpdatedAt < cutoff
                     && c.Status != "converted"
                     && c.UserId != null
                     && c.RecoveryEmailSentAt == null);

        var carts = await query
            .OrderBy(c => c.UpdatedAt)
            .Take(DefaultBatchSize)
            .ToListAsync(ct);

        if (carts.Count == 0) return 0;

        var sent = 0;
        foreach (var cart in carts)
        {
            try
            {
                var resumeUrl = $"/cart?resume={cart.Id:N}";
                await _dispatcher.SendAbandonedCartRecoveryAsync(
                    userId: cart.UserId!,
                    cartId: cart.Id.ToString("N"),
                    resumeUrl: resumeUrl,
                    ct);
                cart.RecoveryEmailSentAt = now;
                sent++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Abandoned-cart recovery dispatch failed for cart {CartId}", cart.Id);
            }
        }

        await _db.SaveChangesAsync(ct);

        // TODO (Wave A5 follow-up): +72h second recovery with 10% coupon code.
        // Requires a separate `RecoveryEmail2SentAt` column + the recovery
        // dispatcher to fan out coupon variables and exit eligibility on
        // checkout. Deferred to keep the v1 simple.

        return sent;
    }
}
