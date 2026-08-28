using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// One-time-per-row legacy backfill: drains pre-existing <see cref="Invoice"/> rows
/// that predate the evidence-source model (<see cref="Invoice.ReconciledAt"/> null) and
/// resolves their real payment evidence via <see cref="InvoiceEvidenceResolver"/>. Runs
/// on every startup (see <c>DatabaseBootstrapper.InitializeAsync</c>) processing a
/// bounded batch each time, so a large backlog drains over a few deploys instead of one
/// long-running migration. Pure EF LINQ -- no raw SQL -- so it behaves identically
/// against the InMemory/Sqlite providers used in tests and the real Postgres provider.
/// Static and stateless, matching <see cref="InvoiceEvidenceResolver"/>.
/// </summary>
public static class InvoiceEvidenceReconciliationService
{
    /// <summary>Bounded per-boot batch size so one run never becomes a long-running migration.</summary>
    private const int BatchSize = 500;

    /// <summary>
    /// Reconciles up to <see cref="BatchSize"/> un-reconciled invoices. For each row, finds
    /// the subscription it most plausibly belongs to (same user, amount, currency, closest
    /// StartedAt to the invoice's IssuedAt -- legacy rows have no SubscriptionId to join on
    /// directly) and resolves that subscription's real payment evidence. Rows with no
    /// plausible subscription are flagged via an AuditEvent for manual review rather than
    /// guessed at. Returns the number of invoice rows touched.
    /// </summary>
    public static async Task<int> ReconcileAsync(LearnerDbContext db, CancellationToken ct)
    {
        var pending = await db.Invoices
            .Where(x => x.ReconciledAt == null)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var touched = 0;

        foreach (var invoice in pending)
        {
            // Candidate subscriptions matching user/amount/currency are pulled into memory
            // before picking the one whose StartedAt is closest to IssuedAt -- an absolute
            // time-difference ordering doesn't translate consistently across the
            // Postgres/Sqlite/InMemory providers this runs against, so the final pick is
            // plain C#, not a translated LINQ expression.
            var candidates = await db.Subscriptions
                .Where(s => s.UserId == invoice.UserId
                    && s.PriceAmount == invoice.Amount
                    && s.Currency == invoice.Currency)
                .ToListAsync(ct);

            var subscription = candidates
                .OrderBy(s => Math.Abs((s.StartedAt - invoice.IssuedAt).Ticks))
                .FirstOrDefault();

            if (subscription is null)
            {
                // No plausible subscription found. Never guess a Source here -- leave it
                // exactly as the migration's interim default left it, and flag the row for
                // a human to review instead.
                invoice.ReconciledAt = now;
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OccurredAt = now,
                    ActorId = "system",
                    ActorName = "InvoiceEvidenceReconciliationService",
                    Action = "invoice.reconciliation.orphan_flagged",
                    ResourceType = "Invoice",
                    ResourceId = invoice.Id,
                    Details = JsonSerializer.Serialize(new
                    {
                        message = "No matching subscription found (same user/amount/currency, closest StartedAt) for this legacy invoice. Source left unresolved -- needs manual review.",
                        userId = invoice.UserId,
                        amount = invoice.Amount,
                        currency = invoice.Currency,
                        issuedAt = invoice.IssuedAt
                    })
                });
                touched++;
                continue;
            }

            var evidence = await InvoiceEvidenceResolver.ResolveAsync(db, subscription, ct);
            invoice.SubscriptionId = subscription.Id;
            invoice.Source = evidence.Source;
            if (evidence.Quote is not null)
            {
                invoice.QuoteId = evidence.Quote.Id;
                invoice.CheckoutSessionId = evidence.Quote.CheckoutSessionId ?? evidence.Payment?.GatewayTransactionId;
            }
            else
            {
                // Reclassifying to ManualProof/AdminGrant -- clear any QuoteId/CheckoutSessionId
                // the legacy row happened to carry so the evidence drawer's "Not applicable
                // (Manual/Admin)" copy actually applies instead of showing a stale gateway-shaped
                // value next to a non-gateway Source.
                invoice.QuoteId = null;
                invoice.CheckoutSessionId = null;
            }
            invoice.ReconciledAt = now;
            touched++;
        }

        await db.SaveChangesAsync(ct);
        return touched;
    }
}
