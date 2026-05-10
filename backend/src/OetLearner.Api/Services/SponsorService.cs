using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class SponsorService(LearnerDbContext db, ILogger<SponsorService> logger, TimeProvider clock)
{
    /// <summary>
    /// RW-013 — sponsor-attributable spend is computed from
    /// <see cref="PaymentTransaction"/> rows belonging to learners who are
    /// (or were) sponsored by this sponsor, restricted to the time window
    /// during which the <see cref="Sponsorship"/> was active. We do not yet
    /// have a direct sponsor-paid flag on <c>PaymentTransaction</c>, so this
    /// is the most defensible heuristic until institutional billing lands:
    /// any completed transaction by a linked learner inside the active
    /// sponsorship window is counted as sponsor-attributable.
    /// </summary>
    private async Task<SponsorBillingSnapshot> ComputeBillingAsync(string sponsorUserId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var sponsorships = await db.Sponsorships
            .AsNoTracking()
            .Where(s => s.SponsorUserId == sponsorUserId && s.LearnerUserId != null)
            .Select(s => new { s.Id, s.LearnerUserId, s.LearnerEmail, s.CreatedAt, s.RevokedAt, s.Status })
            .ToListAsync(ct);

        if (sponsorships.Count == 0)
        {
            return new SponsorBillingSnapshot(0m, 0m, null, Array.Empty<SponsorInvoice>());
        }

        var learnerIds = sponsorships
            .Select(s => s.LearnerUserId!)
            .Distinct()
            .ToList();

        // Pull only completed transactions for the linked learners. This is
        // an over-fetch versus the per-sponsorship windows but lets us match
        // window membership in memory below — typical sponsor cohorts are
        // small, so the round-trip cost is bounded.
        var transactions = await db.PaymentTransactions
            .AsNoTracking()
            .Where(t => learnerIds.Contains(t.LearnerUserId) && t.Status == "completed")
            .ToListAsync(ct);

        var invoices = new List<SponsorInvoice>();
        foreach (var sponsorship in sponsorships)
        {
            var windowEnd = sponsorship.RevokedAt ?? now;
            foreach (var tx in transactions)
            {
                if (tx.LearnerUserId != sponsorship.LearnerUserId) continue;
                if (tx.CreatedAt < sponsorship.CreatedAt) continue;
                if (tx.CreatedAt > windowEnd) continue;
                invoices.Add(new SponsorInvoice(
                    Id: tx.Id,
                    SponsorshipId: sponsorship.Id,
                    LearnerUserId: tx.LearnerUserId,
                    LearnerEmail: sponsorship.LearnerEmail,
                    Gateway: tx.Gateway,
                    GatewayTransactionId: tx.GatewayTransactionId,
                    TransactionType: tx.TransactionType,
                    ProductType: tx.ProductType,
                    ProductId: tx.ProductId,
                    Amount: tx.Amount,
                    Currency: tx.Currency,
                    Status: tx.Status,
                    CreatedAt: tx.CreatedAt));
            }
        }

        var totalSpend = invoices.Sum(i => i.Amount);
        var currentMonthSpend = invoices
            .Where(i => i.CreatedAt >= monthStart)
            .Sum(i => i.Amount);

        // Aggregate currency: only meaningful when every invoice shares one.
        string? aggregateCurrency = null;
        if (invoices.Count > 0)
        {
            var distinctCurrencies = invoices.Select(i => i.Currency).Distinct().ToList();
            aggregateCurrency = distinctCurrencies.Count == 1 ? distinctCurrencies[0] : null;
        }

        var orderedInvoices = invoices
            .OrderByDescending(i => i.CreatedAt)
            .Take(50)
            .ToArray();

        return new SponsorBillingSnapshot(totalSpend, currentMonthSpend, aggregateCurrency, orderedInvoices);
    }

    public async Task<object> GetDashboardAsync(string sponsorUserId, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AuthAccountId == sponsorUserId, ct)
            ?? throw new InvalidOperationException("Sponsor account not found.");

        var sponsorships = await db.Sponsorships
            .AsNoTracking()
            .Where(s => s.SponsorUserId == sponsorUserId)
            .ToListAsync(ct);

        var activeCount = sponsorships.Count(s => s.Status == "Active");
        var pendingCount = sponsorships.Count(s => s.Status == "Pending");
        var totalCount = sponsorships.Count(s => s.Status != "Revoked");

        var billing = await ComputeBillingAsync(sponsorUserId, ct);

        return new
        {
            sponsorName = sponsor.Name,
            organizationName = sponsor.OrganizationName,
            learnersSponsored = totalCount,
            activeSponsorships = activeCount,
            pendingSponsorships = pendingCount,
            totalSpend = billing.TotalSpend,
            currency = billing.Currency,
        };
    }

    public async Task<object> GetLearnersAsync(string sponsorUserId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Sponsorships
            .AsNoTracking()
            .Where(s => s.SponsorUserId == sponsorUserId && s.Status != "Revoked")
            .OrderByDescending(s => s.CreatedAt);

        var total = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                id = s.Id,
                learnerEmail = s.LearnerEmail,
                learnerUserId = s.LearnerUserId,
                status = s.Status,
                createdAt = s.CreatedAt,
                revokedAt = s.RevokedAt,
            })
            .ToListAsync(ct);

        return new { items, total, page, pageSize };
    }

    public async Task<object> InviteLearnerAsync(string sponsorUserId, string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.");

        var normalizedEmail = email.Trim().ToLowerInvariant();

        // Check for existing non-revoked sponsorship
        var existing = await db.Sponsorships
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.SponsorUserId == sponsorUserId &&
                s.LearnerEmail == normalizedEmail &&
                s.Status != "Revoked", ct);

        if (existing != null)
            throw new InvalidOperationException("A sponsorship for this email already exists.");

        var sponsorship = new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerEmail = normalizedEmail,
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Sponsorships.Add(sponsorship);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Sponsor {SponsorUserId} invited learner {Email}", sponsorUserId, normalizedEmail);

        return new
        {
            id = sponsorship.Id,
            learnerEmail = sponsorship.LearnerEmail,
            status = sponsorship.Status,
            createdAt = sponsorship.CreatedAt,
        };
    }

    public async Task<object> RemoveSponsorshipAsync(string sponsorUserId, Guid sponsorshipId, CancellationToken ct)
    {
        var sponsorship = await db.Sponsorships
            .FirstOrDefaultAsync(s => s.Id == sponsorshipId && s.SponsorUserId == sponsorUserId, ct)
            ?? throw new InvalidOperationException("Sponsorship not found.");

        if (sponsorship.Status == "Revoked")
            throw new InvalidOperationException("Sponsorship is already revoked.");

        sponsorship.Status = "Revoked";
        sponsorship.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Sponsor {SponsorUserId} revoked sponsorship {SponsorshipId}", sponsorUserId, sponsorshipId);

        return new { revoked = true };
    }

    public async Task<object> GetBillingAsync(string sponsorUserId, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AuthAccountId == sponsorUserId, ct)
            ?? throw new InvalidOperationException("Sponsor account not found.");

        var totalSponsorships = await db.Sponsorships
            .AsNoTracking()
            .CountAsync(s => s.SponsorUserId == sponsorUserId && s.Status != "Revoked", ct);

        var billing = await ComputeBillingAsync(sponsorUserId, ct);

        return new
        {
            sponsorName = sponsor.Name,
            organizationName = sponsor.OrganizationName,
            totalSponsorships,
            totalSpend = billing.TotalSpend,
            currentMonthSpend = billing.CurrentMonthSpend,
            currency = billing.Currency,
            billingCycle = "monthly",
            invoices = billing.Invoices,
        };
    }
}

internal sealed record SponsorBillingSnapshot(
    decimal TotalSpend,
    decimal CurrentMonthSpend,
    string? Currency,
    IReadOnlyList<SponsorInvoice> Invoices);

public sealed record SponsorInvoice(
    Guid Id,
    Guid SponsorshipId,
    string LearnerUserId,
    string LearnerEmail,
    string Gateway,
    string GatewayTransactionId,
    string TransactionType,
    string? ProductType,
    string? ProductId,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt);
