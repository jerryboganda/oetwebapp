using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed record SponsorDashboardResponse(
    string SponsorName,
    string? OrganizationName,
    int LearnersSponsored,
    int ActiveSponsorships,
    int PendingSponsorships,
    decimal TotalSpend);

public sealed record SponsorBillingInvoiceResponse(
    string Id,
    string InvoiceId,
    string LearnerUserId,
    string? LearnerEmail,
    string Description,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset IssuedAt,
    string? QuoteId,
    string? CheckoutSessionId,
    bool DownloadAvailable);

public sealed record SponsorSeatUsageResponse(
    int Capacity,
    int Assigned,
    int Active,
    int Pending,
    int Consented,
    int Remaining,
    bool CapacityTracked);

public sealed record SponsorBillingResponse(
    string SponsorName,
    string? OrganizationName,
    int TotalSponsorships,
    int ActiveSponsorships,
    int PendingSponsorships,
    int SponsoredLearnerCount,
    decimal TotalSpend,
    decimal CurrentMonthSpend,
    string BillingCycle,
    string Currency,
    IReadOnlyList<string> Currencies,
    SponsorSeatUsageResponse Seats,
    int InvoiceCount,
    int PaidInvoiceCount,
    DateTimeOffset? LastInvoiceAt,
    IReadOnlyList<SponsorBillingInvoiceResponse> Invoices);

public class SponsorService(LearnerDbContext db, ILogger<SponsorService> logger)
{
    private sealed record SponsoredLearnerScope(string LearnerId, string? LearnerEmail, DateTimeOffset StartsAt);

    public async Task<SponsorDashboardResponse> GetDashboardAsync(string sponsorUserId, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(sponsorAccount => sponsorAccount.AuthAccountId == sponsorUserId, ct)
            ?? throw new InvalidOperationException("Sponsor account not found.");

        var sponsorships = await db.Sponsorships
            .AsNoTracking()
            .Where(sponsorship => sponsorship.SponsorUserId == sponsorUserId)
            .ToListAsync(ct);

        var activeCount = sponsorships.Count(sponsorship => sponsorship.Status == "Active");
        var pendingCount = sponsorships.Count(sponsorship => sponsorship.Status == "Pending");
        var totalCount = sponsorships.Count(sponsorship => sponsorship.Status != "Revoked");

        var totalSpend = await ComputeSponsoredInvoiceTotalSpendAsync(sponsor.Id, sponsorUserId, ct);

        return new SponsorDashboardResponse(
            sponsor.Name,
            sponsor.OrganizationName,
            totalCount,
            activeCount,
            pendingCount,
            totalSpend);
    }

    public async Task<object> GetLearnersAsync(string sponsorUserId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Sponsorships
            .AsNoTracking()
            .Where(sponsorship => sponsorship.SponsorUserId == sponsorUserId && sponsorship.Status != "Revoked")
            .OrderByDescending(sponsorship => sponsorship.CreatedAt);

        var total = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sponsorship => new
            {
                id = sponsorship.Id,
                learnerEmail = sponsorship.LearnerEmail,
                learnerUserId = sponsorship.LearnerUserId,
                status = sponsorship.Status,
                createdAt = sponsorship.CreatedAt,
                revokedAt = sponsorship.RevokedAt,
            })
            .ToListAsync(ct);

        return new { items, total, page, pageSize };
    }

    public async Task<object> InviteLearnerAsync(string sponsorUserId, string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        try
        {
            await using var tx = await BeginSerializableTransactionIfNeededAsync(ct);

            var existing = await db.Sponsorships
                .AsNoTracking()
                .FirstOrDefaultAsync(sponsorship =>
                    sponsorship.SponsorUserId == sponsorUserId &&
                    sponsorship.LearnerEmail == normalizedEmail &&
                    sponsorship.Status != "Revoked", ct);

            if (existing != null)
                throw ApiException.Conflict("sponsorship_already_exists", "A sponsorship for this email already exists.");

            var sponsor = await db.SponsorAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(sponsorAccount => sponsorAccount.AuthAccountId == sponsorUserId, ct);

            if (sponsor is not null)
            {
                var sponsorships = await db.Sponsorships
                    .AsNoTracking()
                    .Where(sponsorship => sponsorship.SponsorUserId == sponsorUserId)
                    .ToListAsync(ct);
                var seatUsage = await GetSeatUsageAsync(sponsor.Id, sponsorships, ct);

                if (seatUsage.CapacityTracked && seatUsage.Assigned >= seatUsage.Capacity)
                {
                    throw ApiException.Conflict(
                        "sponsor_seat_capacity_exceeded",
                        "All tracked sponsor seats are already assigned.");
                }
            }

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
            await CommitIfOwnedAsync(tx, ct);

            logger.LogInformation("Sponsor {SponsorUserId} invited learner {Email}", sponsorUserId, normalizedEmail);

            return new
            {
                id = sponsorship.Id,
                learnerEmail = sponsorship.LearnerEmail,
                status = sponsorship.Status,
                createdAt = sponsorship.CreatedAt,
            };
        }
        catch (Exception ex) when (IsSerializableInviteConflict(ex))
        {
            logger.LogWarning(ex, "Sponsor {SponsorUserId} invite hit a serializable seat-capacity conflict", sponsorUserId);
            throw ApiException.Conflict(
                "sponsor_seat_capacity_changed",
                "Sponsor seat capacity changed while processing the invite. Please retry.");
        }
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionIfNeededAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return null;
        if (!db.Database.IsRelational()) return null;
        return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }

    private static async Task CommitIfOwnedAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private static bool IsSerializableInviteConflict(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException
                && (postgresException.SqlState == PostgresErrorCodes.SerializationFailure
                    || postgresException.SqlState == PostgresErrorCodes.DeadlockDetected))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<object> RemoveSponsorshipAsync(string sponsorUserId, Guid sponsorshipId, CancellationToken ct)
    {
        var sponsorship = await db.Sponsorships
            .FirstOrDefaultAsync(sponsorship => sponsorship.Id == sponsorshipId && sponsorship.SponsorUserId == sponsorUserId, ct)
            ?? throw new InvalidOperationException("Sponsorship not found.");

        if (sponsorship.Status == "Revoked")
            throw new InvalidOperationException("Sponsorship is already revoked.");

        sponsorship.Status = "Revoked";
        sponsorship.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Sponsor {SponsorUserId} revoked sponsorship {SponsorshipId}", sponsorUserId, sponsorshipId);

        return new { revoked = true };
    }

    public async Task<SponsorBillingResponse> GetBillingAsync(string sponsorUserId, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(sponsorAccount => sponsorAccount.AuthAccountId == sponsorUserId, ct)
            ?? throw new InvalidOperationException("Sponsor account not found.");

        var sponsorships = await db.Sponsorships
            .AsNoTracking()
            .Where(sponsorship => sponsorship.SponsorUserId == sponsorUserId)
            .ToListAsync(ct);

        var totalSponsorships = sponsorships.Count(sponsorship => sponsorship.Status != "Revoked");
        var activeSponsorships = sponsorships.Count(sponsorship => sponsorship.Status == "Active");
        var pendingSponsorships = sponsorships.Count(sponsorship => sponsorship.Status == "Pending");

        var sponsoredLearnerScopes = await GetSponsoredLearnerScopesAsync(sponsor.Id, sponsorUserId, ct);
        var sponsoredLearnerCount = sponsoredLearnerScopes.Count;
        var seatUsage = await GetSeatUsageAsync(sponsor.Id, sponsorships, ct);
        var scopeByLearnerId = sponsoredLearnerScopes.ToDictionary(scope => scope.LearnerId, StringComparer.OrdinalIgnoreCase);
        var sponsoredLearnerIds = sponsoredLearnerScopes.Select(scope => scope.LearnerId).ToArray();

        var allInvoiceRows = sponsoredLearnerIds.Length == 0
            ? new List<Invoice>()
            : await db.Invoices
                .AsNoTracking()
                .Where(invoice => sponsoredLearnerIds.Contains(invoice.UserId))
                .ToListAsync(ct);

        var scopedInvoiceRows = allInvoiceRows
            .Where(invoice => scopeByLearnerId.TryGetValue(invoice.UserId, out var scope) && invoice.IssuedAt >= scope.StartsAt)
            .OrderByDescending(invoice => invoice.IssuedAt)
            .ToList();

        var invoices = scopedInvoiceRows
            .Take(25)
            .Select(invoice =>
            {
                var learnerScope = scopeByLearnerId[invoice.UserId];
                return new SponsorBillingInvoiceResponse(
                    invoice.Id,
                    invoice.Id,
                    invoice.UserId,
                    learnerScope.LearnerEmail,
                    invoice.Description,
                    invoice.Amount,
                    invoice.Currency,
                    invoice.Status,
                    invoice.IssuedAt,
                    invoice.QuoteId,
                    invoice.CheckoutSessionId,
                    true);
            })
            .ToList();

        var paidInvoices = scopedInvoiceRows
            .Where(invoice => IsPaidInvoiceStatus(invoice.Status))
            .ToList();
        var totalSpend = paidInvoices.Sum(invoice => invoice.Amount);
        var currentMonthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var currentMonthSpend = paidInvoices
            .Where(invoice => invoice.IssuedAt >= currentMonthStart)
            .Sum(invoice => invoice.Amount);
        var invoiceCount = scopedInvoiceRows.Count;
        var paidInvoiceCount = paidInvoices.Count;
        var lastInvoiceAt = scopedInvoiceRows.Count == 0 ? null : (DateTimeOffset?)scopedInvoiceRows[0].IssuedAt;
        var currencies = scopedInvoiceRows
            .Select(invoice => invoice.Currency)
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(currency => currency)
            .ToArray();

        return new SponsorBillingResponse(
            sponsor.Name,
            sponsor.OrganizationName,
            totalSponsorships,
            activeSponsorships,
            pendingSponsorships,
            sponsoredLearnerCount,
            totalSpend,
            currentMonthSpend,
            "monthly",
            currencies.Length == 1 ? currencies[0] : "AUD",
            currencies,
            seatUsage,
            invoiceCount,
            paidInvoiceCount,
            lastInvoiceAt,
            invoices);
    }

    public async Task<GeneratedDownloadFile> GetInvoiceDownloadAsync(string sponsorUserId, string invoiceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            throw ApiException.NotFound("invoice_not_found", "Invoice not found.");
        }

        var sponsor = await db.SponsorAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(account => account.AuthAccountId == sponsorUserId, ct)
            ?? throw new InvalidOperationException("Sponsor account not found.");

        var sponsoredLearnerScopes = await GetSponsoredLearnerScopesAsync(sponsor.Id, sponsorUserId, ct);
        var scopeByLearnerId = sponsoredLearnerScopes.ToDictionary(scope => scope.LearnerId, StringComparer.OrdinalIgnoreCase);
        var invoice = await db.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == invoiceId, ct);

        if (invoice is null
            || !scopeByLearnerId.TryGetValue(invoice.UserId, out var learnerScope)
            || invoice.IssuedAt < learnerScope.StartsAt)
        {
            throw ApiException.NotFound("invoice_not_found", "Invoice not found.");
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine,
            "OET Prep Sponsor Invoice",
            $"Invoice ID: {invoice.Id}",
            $"Sponsor: {sponsor.OrganizationName ?? sponsor.Name}",
            $"Learner: {learnerScope.LearnerEmail ?? invoice.UserId}",
            $"Issued At: {invoice.IssuedAt:yyyy-MM-dd HH:mm:ss zzz}",
            $"Status: {invoice.Status}",
            $"Amount: {invoice.Amount:0.00} {invoice.Currency}",
            $"Description: {invoice.Description}"));

        return new GeneratedDownloadFile(new MemoryStream(bytes), "text/plain", $"{invoice.Id}.txt");
    }

    private async Task<List<SponsoredLearnerScope>> GetSponsoredLearnerScopesAsync(string sponsorId, string sponsorUserId, CancellationToken ct)
    {
        var scopes = new Dictionary<string, SponsoredLearnerScope>(StringComparer.OrdinalIgnoreCase);

        var sponsorshipLearners = await db.Sponsorships
            .AsNoTracking()
            .Where(sponsorship => sponsorship.SponsorUserId == sponsorUserId
                && sponsorship.Status != "Revoked"
                && sponsorship.LearnerUserId != null)
            .Select(sponsorship => new
            {
                LearnerId = sponsorship.LearnerUserId!,
                sponsorship.LearnerEmail,
                sponsorship.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var sponsorshipLearner in sponsorshipLearners)
        {
            AddSponsoredLearnerScope(scopes, sponsorshipLearner.LearnerId, sponsorshipLearner.LearnerEmail, sponsorshipLearner.CreatedAt);
        }

        var linkedLearners = await db.SponsorLearnerLinks
            .AsNoTracking()
            .Where(link => link.SponsorId == sponsorId && link.LearnerConsented)
            .Select(link => new { link.LearnerId, StartsAt = link.ConsentedAt ?? link.LinkedAt })
            .ToListAsync(ct);

        foreach (var linkedLearner in linkedLearners)
        {
            var matchingEmail = sponsorshipLearners
                .FirstOrDefault(sponsorshipLearner => string.Equals(sponsorshipLearner.LearnerId, linkedLearner.LearnerId, StringComparison.OrdinalIgnoreCase))
                ?.LearnerEmail;
            AddSponsoredLearnerScope(scopes, linkedLearner.LearnerId, matchingEmail, linkedLearner.StartsAt);
        }

        return scopes.Values.ToList();
    }

    private async Task<SponsorSeatUsageResponse> GetSeatUsageAsync(
        string sponsorId,
        IReadOnlyCollection<Sponsorship> sponsorships,
        CancellationToken ct)
    {
        var activeSeatCapacity = await db.Cohorts
            .AsNoTracking()
            .Where(cohort => cohort.SponsorId == sponsorId && (cohort.Status == "active" || cohort.Status == "Active"))
            .SumAsync(cohort => cohort.MaxSeats, ct);

        var links = await db.SponsorLearnerLinks
            .AsNoTracking()
            .Where(link => link.SponsorId == sponsorId)
            .ToListAsync(ct);

        var sponsorshipLearnerIds = sponsorships
            .Where(sponsorship => sponsorship.Status != "Revoked" && !string.IsNullOrWhiteSpace(sponsorship.LearnerUserId))
            .Select(sponsorship => sponsorship.LearnerUserId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var assignedLearnerIds = sponsorshipLearnerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pendingInvitesWithoutLearner = sponsorships.Count(sponsorship =>
            sponsorship.Status != "Revoked" && string.IsNullOrWhiteSpace(sponsorship.LearnerUserId));

        foreach (var link in links)
        {
            assignedLearnerIds.Add(link.LearnerId);
        }

        var activeLearnerIds = sponsorships
            .Where(sponsorship => sponsorship.Status == "Active" && !string.IsNullOrWhiteSpace(sponsorship.LearnerUserId))
            .Select(sponsorship => sponsorship.LearnerUserId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links.Where(link => link.LearnerConsented))
        {
            activeLearnerIds.Add(link.LearnerId);
        }

        var assigned = assignedLearnerIds.Count + pendingInvitesWithoutLearner;
        var capacityTracked = activeSeatCapacity > 0;
        var remaining = capacityTracked ? Math.Max(0, activeSeatCapacity - assigned) : 0;

        return new SponsorSeatUsageResponse(
            Capacity: activeSeatCapacity,
            Assigned: assigned,
            Active: activeLearnerIds.Count,
            Pending: sponsorships.Count(sponsorship => sponsorship.Status == "Pending"),
            Consented: links.Where(link => link.LearnerConsented)
                .Select(link => link.LearnerId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Remaining: remaining,
            CapacityTracked: capacityTracked);
    }

    private async Task<decimal> ComputeSponsoredInvoiceTotalSpendAsync(string sponsorId, string sponsorUserId, CancellationToken ct)
    {
        var sponsoredLearnerScopes = await GetSponsoredLearnerScopesAsync(sponsorId, sponsorUserId, ct);
        var sponsoredLearnerIds = sponsoredLearnerScopes.Select(scope => scope.LearnerId).ToArray();
        if (sponsoredLearnerIds.Length == 0)
        {
            return 0m;
        }

        var scopeByLearnerId = sponsoredLearnerScopes.ToDictionary(scope => scope.LearnerId, StringComparer.OrdinalIgnoreCase);
        var invoiceRows = await db.Invoices
            .AsNoTracking()
            .Where(invoice => sponsoredLearnerIds.Contains(invoice.UserId))
            .ToListAsync(ct);

        return invoiceRows
            .Where(invoice => IsPaidInvoiceStatus(invoice.Status)
                && scopeByLearnerId.TryGetValue(invoice.UserId, out var scope)
                && invoice.IssuedAt >= scope.StartsAt)
            .Sum(invoice => invoice.Amount);
    }

    private static void AddSponsoredLearnerScope(
        IDictionary<string, SponsoredLearnerScope> scopes,
        string learnerId,
        string? learnerEmail,
        DateTimeOffset startsAt)
    {
        if (!scopes.TryGetValue(learnerId, out var existingScope))
        {
            scopes[learnerId] = new SponsoredLearnerScope(learnerId, NormalizeEmail(learnerEmail), startsAt);
            return;
        }

        var earliestStart = startsAt < existingScope.StartsAt ? startsAt : existingScope.StartsAt;
        var email = string.IsNullOrWhiteSpace(existingScope.LearnerEmail) ? NormalizeEmail(learnerEmail) : existingScope.LearnerEmail;
        scopes[learnerId] = new SponsoredLearnerScope(learnerId, email, earliestStart);
    }

    private static string? NormalizeEmail(string? learnerEmail)
        => string.IsNullOrWhiteSpace(learnerEmail) ? null : learnerEmail.Trim().ToLowerInvariant();

    private static bool IsPaidInvoiceStatus(string? status)
        => string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
}
