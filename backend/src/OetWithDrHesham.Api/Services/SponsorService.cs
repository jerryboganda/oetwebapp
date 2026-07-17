using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services;

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
    private async Task<SponsorBillingSnapshot> ComputeBillingAsync(
        string sponsorUserId,
        bool includeInvoices,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var aggregate = await (
            from sponsorship in db.Sponsorships.AsNoTracking()
            join transaction in db.PaymentTransactions.AsNoTracking()
                on sponsorship.LearnerUserId equals transaction.LearnerUserId
            where sponsorship.SponsorUserId == sponsorUserId
                && sponsorship.LearnerUserId != null
                && transaction.Status == "completed"
                && transaction.CreatedAt >= sponsorship.CreatedAt
                && transaction.CreatedAt <= (sponsorship.RevokedAt ?? now)
            select new
            {
                transaction.Amount,
                transaction.Currency,
                transaction.CreatedAt,
            })
            .GroupBy(_ => 1)
            .Select(group => new SponsorBillingAggregate(
                group.Sum(row => row.Amount),
                group.Sum(row => row.CreatedAt >= monthStart ? row.Amount : 0m),
                group.Select(row => row.Currency).Distinct().Count(),
                group.Min(row => row.Currency)))
            .SingleOrDefaultAsync(ct);

        if (aggregate is null)
        {
            return new SponsorBillingSnapshot(0m, 0m, null, Array.Empty<SponsorInvoice>());
        }

        var invoices = includeInvoices
            ? await (
                from sponsorship in db.Sponsorships.AsNoTracking()
                join transaction in db.PaymentTransactions.AsNoTracking()
                    on sponsorship.LearnerUserId equals transaction.LearnerUserId
                where sponsorship.SponsorUserId == sponsorUserId
                    && sponsorship.LearnerUserId != null
                    && transaction.Status == "completed"
                    && transaction.CreatedAt >= sponsorship.CreatedAt
                    && transaction.CreatedAt <= (sponsorship.RevokedAt ?? now)
                orderby transaction.CreatedAt descending
                select new SponsorInvoice(
                    transaction.Id,
                    sponsorship.Id,
                    transaction.LearnerUserId,
                    sponsorship.LearnerEmail,
                    transaction.Gateway,
                    transaction.GatewayTransactionId,
                    transaction.TransactionType,
                    transaction.ProductType,
                    transaction.ProductId,
                    transaction.Amount,
                    transaction.Currency,
                    transaction.Status,
                    transaction.CreatedAt))
                .Take(50)
                .ToArrayAsync(ct)
            : Array.Empty<SponsorInvoice>();

        return new SponsorBillingSnapshot(
            aggregate.TotalSpend,
            aggregate.CurrentMonthSpend,
            aggregate.CurrencyCount == 1 ? aggregate.Currency : null,
            invoices);
    }

    public async Task<object> GetDashboardAsync(string sponsorUserId, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts
            .AsNoTracking()
            .Where(s => s.AuthAccountId == sponsorUserId)
            .Select(s => new SponsorIdentity(s.Name, s.OrganizationName))
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Sponsor account not found.");

        var sponsorshipCounts = await db.Sponsorships
            .AsNoTracking()
            .Where(s => s.SponsorUserId == sponsorUserId)
            .GroupBy(_ => 1)
            .Select(group => new SponsorDashboardCounts(
                group.Count(s => s.Status != "Revoked"),
                group.Count(s => s.Status == "Active"),
                group.Count(s => s.Status == "Pending")))
            .SingleOrDefaultAsync(ct)
            ?? new SponsorDashboardCounts(0, 0, 0);

        var billing = await ComputeBillingAsync(sponsorUserId, includeInvoices: false, ct);

        return new
        {
            sponsorName = sponsor.Name,
            organizationName = sponsor.OrganizationName,
            learnersSponsored = sponsorshipCounts.Total,
            activeSponsorships = sponsorshipCounts.Active,
            pendingSponsorships = sponsorshipCounts.Pending,
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
            .Where(s => s.AuthAccountId == sponsorUserId)
            .Select(s => new SponsorIdentity(s.Name, s.OrganizationName))
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Sponsor account not found.");

        var totalSponsorships = await db.Sponsorships
            .AsNoTracking()
            .CountAsync(s => s.SponsorUserId == sponsorUserId && s.Status != "Revoked", ct);

        var billing = await ComputeBillingAsync(sponsorUserId, includeInvoices: true, ct);

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

internal sealed record SponsorBillingAggregate(
    decimal TotalSpend,
    decimal CurrentMonthSpend,
    int CurrencyCount,
    string Currency);

internal sealed record SponsorDashboardCounts(int Total, int Active, int Pending);

internal sealed record SponsorIdentity(string Name, string? OrganizationName);

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
