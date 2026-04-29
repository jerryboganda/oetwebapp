using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class SponsorBillingTests : IAsyncDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly LearnerDbContext _db;
    private readonly SponsorService _sponsorService;

    public SponsorBillingTests()
    {
        _factory = new TestWebApplicationFactory();
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var logger = _scope.ServiceProvider.GetRequiredService<ILogger<SponsorService>>();
        _sponsorService = new SponsorService(_db, logger);
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetBillingAsync_NoLinkedLearners_ReturnsZeroSpendAndEmpty()
    {
        // Arrange
        var sponsorUserId = "sponsor-user-1";
        var sponsorId = "sponsor-1";
        
        var sponsor = new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Test Sponsor",
            Type = "institution",
            ContactEmail = "sponsor@test.com",
            OrganizationName = "Test Org",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.SponsorAccounts.Add(sponsor);
        await _db.SaveChangesAsync();

        // Act
        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);

        // Assert
        Assert.Equal(0m, billing.TotalSpend);
        Assert.Equal(0m, billing.CurrentMonthSpend);
        Assert.Equal(0, billing.TotalSponsorships);
        Assert.Equal(0, billing.ActiveSponsorships);
        Assert.Equal(0, billing.PendingSponsorships);
        Assert.Equal(0, billing.SponsoredLearnerCount);
        Assert.Equal(0, billing.InvoiceCount);
        Assert.Equal(0, billing.PaidInvoiceCount);
        Assert.False(billing.Seats.CapacityTracked);
        Assert.Equal(0, billing.Seats.Capacity);
        Assert.Equal(0, billing.Seats.Assigned);
        Assert.Equal(0, billing.Seats.Active);
        Assert.Equal(0, billing.Seats.Pending);
        Assert.Equal(0, billing.Seats.Remaining);
        Assert.Null(billing.LastInvoiceAt);
        Assert.Empty(billing.Invoices);
        Assert.Equal("Test Sponsor", billing.SponsorName);
        Assert.Equal("Test Org", billing.OrganizationName);
        Assert.Equal("monthly", billing.BillingCycle);
        Assert.Equal("AUD", billing.Currency);
    }

    [Fact]
    public async Task GetBillingAsync_WithSponsoredLearnersAndInvoices_IncludesInvoicesAndExcludesOthers()
    {
        // Arrange
        var sponsorUserId = "sponsor-user-2";
        var sponsorId = "sponsor-2";
        var sponsoredLearnerId1 = "learner-1";
        var sponsoredLearnerId2 = "learner-2";
        var unrelatedLearnerId = "learner-3";
        var currentMonthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var sponsorshipStart = currentMonthStart.AddDays(-10);

        var sponsor = new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Test Sponsor 2",
            Type = "employer",
            ContactEmail = "sponsor2@test.com",
            OrganizationName = "Test Company",
            CreatedAt = sponsorshipStart
        };

        // Create sponsorships
        var sponsorship1 = new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerUserId = sponsoredLearnerId1,
            LearnerEmail = "learner1@test.com",
            Status = "Active",
            CreatedAt = sponsorshipStart
        };

        var sponsorship2 = new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerUserId = sponsoredLearnerId2,
            LearnerEmail = "learner2@test.com",
            Status = "Pending",
            CreatedAt = sponsorshipStart
        };

        // Create a SponsorLearnerLink for additional coverage
        var sponsorLearnerLink = new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            LearnerId = sponsoredLearnerId2, // same learner, should not duplicate
            LearnerConsented = true,
            LinkedAt = sponsorshipStart,
            ConsentedAt = sponsorshipStart
        };

        // Create invoices for sponsored learners
        var invoice1 = new Invoice
        {
            Id = "inv-1",
            UserId = sponsoredLearnerId1,
            Amount = 100.00m,
            Currency = "AUD",
            Status = "Paid",
            Description = "Subscription fee",
            IssuedAt = currentMonthStart.AddDays(2)
        };

        var invoice2 = new Invoice
        {
            Id = "inv-2",
            UserId = sponsoredLearnerId2,
            Amount = 50.00m,
            Currency = "AUD",
            Status = "Succeeded",
            Description = "Add-on purchase",
            IssuedAt = currentMonthStart.AddDays(5)
        };

        // Create invoice for unrelated learner (should be excluded)
        var invoice3 = new Invoice
        {
            Id = "inv-3",
            UserId = unrelatedLearnerId,
            Amount = 75.00m,
            Currency = "AUD",
            Status = "Paid",
            Description = "Should not be included",
            IssuedAt = currentMonthStart.AddDays(6)
        };

        // Create unpaid invoice for sponsored learner (should be included in list but not totals)
        var invoice4 = new Invoice
        {
            Id = "inv-4",
            UserId = sponsoredLearnerId1,
            Amount = 25.00m,
            Currency = "AUD",
            Status = "Pending",
            Description = "Pending payment",
            IssuedAt = currentMonthStart.AddDays(7)
        };

        _db.SponsorAccounts.Add(sponsor);
        _db.Sponsorships.AddRange(sponsorship1, sponsorship2);
        _db.SponsorLearnerLinks.Add(sponsorLearnerLink);
        _db.Invoices.AddRange(invoice1, invoice2, invoice3, invoice4);
        await _db.SaveChangesAsync();

        // Act
        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);

        // Assert
        Assert.Equal(150.00m, billing.TotalSpend); // Only paid invoices (inv-1 + inv-2)
        Assert.Equal(150.00m, billing.CurrentMonthSpend); // Current month paid invoices (inv-1 + inv-2)
        Assert.Equal(2, billing.TotalSponsorships); // Active + Pending
        Assert.Equal(1, billing.ActiveSponsorships);
        Assert.Equal(1, billing.PendingSponsorships);
        Assert.Equal(2, billing.SponsoredLearnerCount); // Unique learners from sponsorships + links
        Assert.False(billing.Seats.CapacityTracked);
        Assert.Equal(2, billing.Seats.Assigned); // Duplicate sponsorship/link learner is counted once
        Assert.Equal(2, billing.Seats.Active); // Active sponsorship + consented link
        Assert.Equal(1, billing.Seats.Pending);
        Assert.Equal(1, billing.Seats.Consented);
        Assert.Equal(3, billing.InvoiceCount); // All invoices for sponsored learners (inv-1, inv-2, inv-4)
        Assert.Equal(2, billing.PaidInvoiceCount); // Only paid invoices
        Assert.NotNull(billing.LastInvoiceAt);
        Assert.Equal(3, billing.Invoices.Count); // All invoices for sponsored learners, newest first

        var invoiceItems = billing.Invoices.ToList();

        // Verify invoice shape contains sanitized fields only
        var firstInvoice = invoiceItems[0];
        Assert.NotNull(firstInvoice.Id);
        Assert.NotNull(firstInvoice.InvoiceId);
        Assert.NotNull(firstInvoice.LearnerUserId);
        Assert.NotNull(firstInvoice.LearnerEmail);
        Assert.NotNull(firstInvoice.Description);
        Assert.True(firstInvoice.Amount > 0);
        Assert.Equal("AUD", firstInvoice.Currency);
        Assert.NotNull(firstInvoice.Status);
        Assert.NotEqual(default, firstInvoice.IssuedAt);

        // Verify unrelated learner's invoice is not included
        var invoiceIds = invoiceItems.Select(invoice => invoice.Id).ToList();
        Assert.DoesNotContain("inv-3", invoiceIds);
        Assert.Contains("inv-1", invoiceIds);
        Assert.Contains("inv-2", invoiceIds);
        Assert.Contains("inv-4", invoiceIds);
    }

    [Fact]
    public async Task GetBillingAsync_WithRevokedSponsorships_ExcludesRevokedFromCounts()
    {
        // Arrange
        var sponsorUserId = "sponsor-user-3";
        var sponsorId = "sponsor-3";
        var learnerId = "learner-revoked";

        var sponsor = new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Test Sponsor 3",
            Type = "parent",
            ContactEmail = "sponsor3@test.com",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var activeSponsorshipId = Guid.NewGuid();
        var revokedSponsorshipId = Guid.NewGuid();

        var activeSponsorship = new Sponsorship
        {
            Id = activeSponsorshipId,
            SponsorUserId = sponsorUserId,
            LearnerUserId = learnerId,
            LearnerEmail = "learner@test.com",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        var revokedSponsorship = new Sponsorship
        {
            Id = revokedSponsorshipId,
            SponsorUserId = sponsorUserId,
            LearnerUserId = learnerId,
            LearnerEmail = "learner@test.com",
            Status = "Revoked",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };

        _db.SponsorAccounts.Add(sponsor);
        _db.Sponsorships.AddRange(activeSponsorship, revokedSponsorship);
        await _db.SaveChangesAsync();

        // Act
        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);

        // Assert
        Assert.Equal(1, billing.TotalSponsorships); // Only active, excludes revoked
        Assert.Equal(1, billing.ActiveSponsorships);
        Assert.Equal(0, billing.PendingSponsorships);
    }

    [Fact]
    public async Task GetDashboardAsync_UsesSameTotalSpendLogic()
    {
        // Arrange
        var sponsorUserId = "sponsor-user-4";
        var sponsorId = "sponsor-4";
        var learnerId = "learner-4";

        var sponsor = new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Dashboard Test Sponsor",
            Type = "institution",
            ContactEmail = "dashboard@test.com",
            OrganizationName = "Dashboard Org",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var sponsorship = new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerUserId = learnerId,
            LearnerEmail = "learner4@test.com",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        var invoice = new Invoice
        {
            Id = "inv-dashboard",
            UserId = learnerId,
            Amount = 200.00m,
            Currency = "AUD",
            Status = "Paid",
            Description = "Dashboard test invoice",
            IssuedAt = DateTimeOffset.UtcNow
        };

        _db.SponsorAccounts.Add(sponsor);
        _db.Sponsorships.Add(sponsorship);
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        // Act
        var dashboard = await _sponsorService.GetDashboardAsync(sponsorUserId, CancellationToken.None);
        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);

        // Assert - Both should return the same totalSpend
        Assert.Equal(200.00m, dashboard.TotalSpend);
        Assert.Equal(200.00m, billing.TotalSpend);
        Assert.Equal(dashboard.TotalSpend, billing.TotalSpend);
    }

    [Fact]
    public async Task GetBillingAsync_CurrentMonthSpendCalculation_OnlyIncludesCurrentMonth()
    {
        // Arrange
        var sponsorUserId = "sponsor-user-5";
        var sponsorId = "sponsor-5";
        var learnerId = "learner-5";
        var currentMonth = DateTimeOffset.UtcNow;
        var currentMonthStart = new DateTimeOffset(currentMonth.Year, currentMonth.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var sponsorshipStart = currentMonthStart.AddDays(-10);

        var sponsor = new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Monthly Test Sponsor",
            Type = "employer",
            ContactEmail = "monthly@test.com",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var sponsorship = new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerUserId = learnerId,
            LearnerEmail = "learner5@test.com",
            Status = "Active",
            CreatedAt = sponsorshipStart
        };
        var currentMonthInvoice = new Invoice
        {
            Id = "inv-current",
            UserId = learnerId,
            Amount = 100.00m,
            Currency = "AUD",
            Status = "Paid",
            Description = "Current month invoice",
            IssuedAt = currentMonthStart.AddDays(5)
        };

        var lastMonthInvoice = new Invoice
        {
            Id = "inv-last",
            UserId = learnerId,
            Amount = 75.00m,
            Currency = "AUD",
            Status = "Paid",
            Description = "Last month invoice",
            IssuedAt = currentMonthStart.AddDays(-5)
        };

        _db.SponsorAccounts.Add(sponsor);
        _db.Sponsorships.Add(sponsorship);
        _db.Invoices.AddRange(currentMonthInvoice, lastMonthInvoice);
        await _db.SaveChangesAsync();

        // Act
        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);

        // Assert
        Assert.Equal(175.00m, billing.TotalSpend); // Both invoices
        Assert.Equal(100.00m, billing.CurrentMonthSpend); // Only current month invoice
    }

    [Fact]
    public async Task GetBillingAsync_ExcludesInvoicesBeforeSponsorshipStarted()
    {
        var sponsorUserId = "sponsor-user-6";
        var sponsorId = "sponsor-6";
        var learnerId = "learner-6";
        var sponsorshipStart = DateTimeOffset.UtcNow.AddDays(-7);

        _db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Scoped Sponsor",
            Type = "institution",
            ContactEmail = "scoped@test.com",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-8)
        });
        _db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerUserId = learnerId,
            LearnerEmail = "learner6@test.com",
            Status = "Active",
            CreatedAt = sponsorshipStart
        });
        _db.Invoices.AddRange(
            new Invoice
            {
                Id = "inv-before-sponsor",
                UserId = learnerId,
                Amount = 99.00m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Before sponsor",
                IssuedAt = sponsorshipStart.AddDays(-2)
            },
            new Invoice
            {
                Id = "inv-after-sponsor",
                UserId = learnerId,
                Amount = 49.00m,
                Currency = "AUD",
                Status = "Paid",
                Description = "After sponsor",
                IssuedAt = sponsorshipStart.AddDays(2)
            });
        await _db.SaveChangesAsync();

        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);
        var invoiceIds = billing.Invoices.Select(invoice => invoice.Id).ToList();

        Assert.Equal(49.00m, billing.TotalSpend);
        Assert.Equal(1, billing.InvoiceCount);
        Assert.Contains("inv-after-sponsor", invoiceIds);
        Assert.DoesNotContain("inv-before-sponsor", invoiceIds);
    }

    [Fact]
    public async Task GetBillingAsync_ExcludesUnconsentedSponsorLearnerLinkInvoices()
    {
        var sponsorUserId = "sponsor-user-unconsented-link";
        var sponsorId = "sponsor-unconsented-link";
        var learnerId = "learner-unconsented-link";
        var linkedAt = DateTimeOffset.UtcNow.AddDays(-3);

        _db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Unconsented Link Sponsor",
            Type = "institution",
            ContactEmail = "unconsented-link@test.com",
            CreatedAt = linkedAt.AddDays(-1)
        });
        _db.SponsorLearnerLinks.Add(new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            LearnerId = learnerId,
            LearnerConsented = false,
            LinkedAt = linkedAt
        });
        _db.Invoices.Add(new Invoice
        {
            Id = "inv-unconsented-link",
            UserId = learnerId,
            Amount = 88.00m,
            Currency = "AUD",
            Status = "Paid",
            Description = "Unconsented sponsor link invoice",
            IssuedAt = linkedAt.AddDays(1)
        });
        await _db.SaveChangesAsync();

        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);

        Assert.Equal(0m, billing.TotalSpend);
        Assert.Equal(0, billing.SponsoredLearnerCount);
        Assert.Equal(0, billing.InvoiceCount);
        Assert.Empty(billing.Invoices);
        await Assert.ThrowsAsync<ApiException>(() =>
            _sponsorService.GetInvoiceDownloadAsync(sponsorUserId, "inv-unconsented-link", CancellationToken.None));
    }

    [Fact]
    public async Task GetBillingAsync_IncludesConsentedSponsorLearnerLinkInvoices()
    {
        var sponsorUserId = "sponsor-user-consented-link";
        var sponsorId = "sponsor-consented-link";
        var learnerId = "learner-consented-link";
        var linkedAt = DateTimeOffset.UtcNow.AddDays(-3);

        _db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Consented Link Sponsor",
            Type = "institution",
            ContactEmail = "consented-link@test.com",
            CreatedAt = linkedAt.AddDays(-1)
        });
        _db.SponsorLearnerLinks.Add(new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            LearnerId = learnerId,
            LearnerConsented = true,
            LinkedAt = linkedAt,
            ConsentedAt = linkedAt.AddHours(1)
        });
        _db.Invoices.AddRange(
            new Invoice
            {
                Id = "inv-before-consent-link",
                UserId = learnerId,
                Amount = 41.00m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Before consent sponsor link invoice",
                IssuedAt = linkedAt.AddMinutes(30)
            },
            new Invoice
            {
                Id = "inv-consented-link",
                UserId = learnerId,
                Amount = 92.00m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Consented sponsor link invoice",
                IssuedAt = linkedAt.AddDays(1)
            });
        await _db.SaveChangesAsync();

        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);
        var file = await _sponsorService.GetInvoiceDownloadAsync(sponsorUserId, "inv-consented-link", CancellationToken.None);

        Assert.Equal(92.00m, billing.TotalSpend);
        Assert.Equal(1, billing.SponsoredLearnerCount);
        Assert.Equal(1, billing.InvoiceCount);
        Assert.Contains(billing.Invoices, invoice => invoice.Id == "inv-consented-link");
        Assert.DoesNotContain(billing.Invoices, invoice => invoice.Id == "inv-before-consent-link");
        Assert.Equal("inv-consented-link.txt", file.FileName);
        await Assert.ThrowsAsync<ApiException>(() =>
            _sponsorService.GetInvoiceDownloadAsync(sponsorUserId, "inv-before-consent-link", CancellationToken.None));
    }

    [Fact]
    public async Task GetBillingAsync_WithActiveCohorts_ReturnsSeatUsageWithoutDoubleCounting()
    {
        var sponsorUserId = "sponsor-user-seats";
        var sponsorId = "sponsor-seats";
        var now = DateTimeOffset.UtcNow;

        _db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Seat Sponsor",
            Type = "institution",
            ContactEmail = "seats@test.com",
            OrganizationName = "Seat Org",
            CreatedAt = now.AddDays(-30)
        });
        _db.Cohorts.AddRange(
            new Cohort
            {
                Id = "cohort-active-seats",
                SponsorId = sponsorId,
                Name = "Active Cohort",
                ExamTypeCode = "oet",
                MaxSeats = 5,
                EnrolledCount = 0,
                Status = "active",
                CreatedAt = now.AddDays(-20)
            },
            new Cohort
            {
                Id = "cohort-archived-seats",
                SponsorId = sponsorId,
                Name = "Archived Cohort",
                ExamTypeCode = "oet",
                MaxSeats = 20,
                EnrolledCount = 0,
                Status = "archived",
                CreatedAt = now.AddDays(-25)
            });
        _db.Sponsorships.AddRange(
            new Sponsorship
            {
                Id = Guid.NewGuid(),
                SponsorUserId = sponsorUserId,
                LearnerUserId = "seat-learner-1",
                LearnerEmail = "seat1@test.com",
                Status = "Active",
                CreatedAt = now.AddDays(-10)
            },
            new Sponsorship
            {
                Id = Guid.NewGuid(),
                SponsorUserId = sponsorUserId,
                LearnerEmail = "pending-seat@test.com",
                Status = "Pending",
                CreatedAt = now.AddDays(-2)
            });
        _db.SponsorLearnerLinks.AddRange(
            new SponsorLearnerLink
            {
                Id = Guid.NewGuid(),
                SponsorId = sponsorId,
                LearnerId = "seat-learner-1",
                LearnerConsented = true,
                LinkedAt = now.AddDays(-9),
                ConsentedAt = now.AddDays(-8)
            },
            new SponsorLearnerLink
            {
                Id = Guid.NewGuid(),
                SponsorId = sponsorId,
                LearnerId = "seat-learner-2",
                LearnerConsented = true,
                LinkedAt = now.AddDays(-7),
                ConsentedAt = now.AddDays(-6)
            });
        await _db.SaveChangesAsync();

        var billing = await _sponsorService.GetBillingAsync(sponsorUserId, CancellationToken.None);

        Assert.True(billing.Seats.CapacityTracked);
        Assert.Equal(5, billing.Seats.Capacity);
        Assert.Equal(3, billing.Seats.Assigned); // active learner + pending invite + linked learner
        Assert.Equal(2, billing.Seats.Active); // active sponsorship/link duplicate + linked learner
        Assert.Equal(1, billing.Seats.Pending);
        Assert.Equal(2, billing.Seats.Consented);
        Assert.Equal(2, billing.Seats.Remaining);
    }

    [Fact]
    public async Task InviteLearnerAsync_WithFullTrackedCapacity_ThrowsConflict()
    {
        var sponsorUserId = "sponsor-user-full-capacity";
        var sponsorId = "sponsor-full-capacity";
        var now = DateTimeOffset.UtcNow;

        _db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Full Capacity Sponsor",
            Type = "institution",
            ContactEmail = "full-capacity@test.com",
            CreatedAt = now.AddDays(-20)
        });
        _db.Cohorts.Add(new Cohort
        {
            Id = "cohort-full-capacity",
            SponsorId = sponsorId,
            Name = "Full Capacity Cohort",
            ExamTypeCode = "oet",
            MaxSeats = 2,
            EnrolledCount = 0,
            Status = "active",
            CreatedAt = now.AddDays(-10)
        });
        _db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerEmail = "pending-full-capacity@test.com",
            Status = "Pending",
            CreatedAt = now.AddDays(-2)
        });
        _db.SponsorLearnerLinks.Add(new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            LearnerId = "linked-full-capacity",
            LearnerConsented = true,
            LinkedAt = now.AddDays(-3),
            ConsentedAt = now.AddDays(-2)
        });
        await _db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            _sponsorService.InviteLearnerAsync(sponsorUserId, "new-full-capacity@test.com", CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("sponsor_seat_capacity_exceeded", exception.ErrorCode);
        Assert.False(await _db.Sponsorships.AnyAsync(sponsorship =>
            sponsorship.SponsorUserId == sponsorUserId && sponsorship.LearnerEmail == "new-full-capacity@test.com"));
    }

    [Fact]
    public async Task InviteLearnerAsync_AllowsInviteWhenCapacityUntrackedOrRemaining()
    {
        var now = DateTimeOffset.UtcNow;
        var untrackedSponsorUserId = "sponsor-user-untracked-capacity";
        var trackedSponsorUserId = "sponsor-user-remaining-capacity";
        var untrackedSponsorId = "sponsor-untracked-capacity";
        var trackedSponsorId = "sponsor-remaining-capacity";

        _db.SponsorAccounts.AddRange(
            new SponsorAccount
            {
                Id = untrackedSponsorId,
                AuthAccountId = untrackedSponsorUserId,
                Name = "Untracked Capacity Sponsor",
                Type = "institution",
                ContactEmail = "untracked-capacity@test.com",
                CreatedAt = now.AddDays(-10)
            },
            new SponsorAccount
            {
                Id = trackedSponsorId,
                AuthAccountId = trackedSponsorUserId,
                Name = "Remaining Capacity Sponsor",
                Type = "institution",
                ContactEmail = "remaining-capacity@test.com",
                CreatedAt = now.AddDays(-10)
            });
        _db.Cohorts.Add(new Cohort
        {
            Id = "cohort-remaining-capacity",
            SponsorId = trackedSponsorId,
            Name = "Remaining Capacity Cohort",
            ExamTypeCode = "oet",
            MaxSeats = 2,
            EnrolledCount = 0,
            Status = "Active",
            CreatedAt = now.AddDays(-5)
        });
        _db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = trackedSponsorUserId,
            LearnerEmail = "existing-remaining-capacity@test.com",
            Status = "Pending",
            CreatedAt = now.AddDays(-1)
        });
        await _db.SaveChangesAsync();

        await _sponsorService.InviteLearnerAsync(untrackedSponsorUserId, "new-untracked-capacity@test.com", CancellationToken.None);
        await _sponsorService.InviteLearnerAsync(trackedSponsorUserId, "new-remaining-capacity@test.com", CancellationToken.None);

        Assert.True(await _db.Sponsorships.AnyAsync(sponsorship =>
            sponsorship.SponsorUserId == untrackedSponsorUserId && sponsorship.LearnerEmail == "new-untracked-capacity@test.com"));
        Assert.True(await _db.Sponsorships.AnyAsync(sponsorship =>
            sponsorship.SponsorUserId == trackedSponsorUserId && sponsorship.LearnerEmail == "new-remaining-capacity@test.com"));
    }

    [Fact]
    public async Task InviteLearnerAsync_WithRelationalProvider_EnforcesCapacityAfterInvite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var sponsorUserId = "sponsor-user-relational-capacity";
        var sponsorId = "sponsor-relational-capacity";
        var now = DateTimeOffset.UtcNow;

        db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Relational Capacity Sponsor",
            Type = "institution",
            ContactEmail = "relational-capacity@test.com",
            CreatedAt = now.AddDays(-10)
        });
        db.Cohorts.Add(new Cohort
        {
            Id = "cohort-relational-capacity",
            SponsorId = sponsorId,
            Name = "Relational Capacity Cohort",
            ExamTypeCode = "oet",
            MaxSeats = 1,
            EnrolledCount = 0,
            Status = "active",
            CreatedAt = now.AddDays(-5)
        });
        await db.SaveChangesAsync();

        var logger = _scope.ServiceProvider.GetRequiredService<ILogger<SponsorService>>();
        var service = new SponsorService(db, logger);

        await service.InviteLearnerAsync(sponsorUserId, "first-relational-capacity@test.com", CancellationToken.None);
        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            service.InviteLearnerAsync(sponsorUserId, "second-relational-capacity@test.com", CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("sponsor_seat_capacity_exceeded", exception.ErrorCode);
        Assert.Equal(1, await db.Sponsorships.CountAsync(sponsorship => sponsorship.SponsorUserId == sponsorUserId));
    }

    [Fact]
    public async Task GetInvoiceDownloadAsync_OnlyAllowsScopedSponsorInvoices()
    {
        var sponsorUserId = "sponsor-user-7";
        var sponsorId = "sponsor-7";
        var learnerId = "learner-7";

        _db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = sponsorId,
            AuthAccountId = sponsorUserId,
            Name = "Download Sponsor",
            Type = "institution",
            ContactEmail = "download@test.com",
            OrganizationName = "Download Org",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        });
        _db.Sponsorships.Add(new Sponsorship
        {
            Id = Guid.NewGuid(),
            SponsorUserId = sponsorUserId,
            LearnerUserId = learnerId,
            LearnerEmail = "learner7@test.com",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        });
        _db.Invoices.AddRange(
            new Invoice
            {
                Id = "inv-download-allowed",
                UserId = learnerId,
                Amount = 39.00m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Allowed sponsor invoice",
                IssuedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new Invoice
            {
                Id = "inv-download-blocked",
                UserId = "unrelated-learner-7",
                Amount = 39.00m,
                Currency = "AUD",
                Status = "Paid",
                Description = "Blocked sponsor invoice",
                IssuedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        await _db.SaveChangesAsync();

        var file = await _sponsorService.GetInvoiceDownloadAsync(sponsorUserId, "inv-download-allowed", CancellationToken.None);

        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal("inv-download-allowed.txt", file.FileName);
        await Assert.ThrowsAsync<ApiException>(() => _sponsorService.GetInvoiceDownloadAsync(sponsorUserId, "inv-download-blocked", CancellationToken.None));
    }
}