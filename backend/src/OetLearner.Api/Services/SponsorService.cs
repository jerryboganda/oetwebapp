using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class SponsorService(LearnerDbContext db, ILogger<SponsorService> logger)
{
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

        return new
        {
            sponsorName = sponsor.Name,
            organizationName = sponsor.OrganizationName,
            learnersSponsored = totalCount,
            activeSponsorships = activeCount,
            pendingSponsorships = pendingCount,
            totalSpend = 0m, // placeholder — billing integration is future work
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

        // Billing summary — placeholder values until billing integration is complete
        return new
        {
            sponsorName = sponsor.Name,
            organizationName = sponsor.OrganizationName,
            totalSponsorships,
            totalSpend = 0m,
            currentMonthSpend = 0m,
            billingCycle = "monthly",
            invoices = Array.Empty<object>(),
        };
    }
}
