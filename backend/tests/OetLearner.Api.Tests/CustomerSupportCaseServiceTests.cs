using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class CustomerSupportCaseServiceTests
{
    [Fact]
    public async Task Create_and_active_candidate_read_are_ticket_scoped_and_audited()
    {
        await using var db = NewDb();
        await AddLearnerAsync(db);
        var service = new CustomerSupportCaseService(db);
        var expiry = DateTimeOffset.UtcNow.AddHours(2);

        var supportCase = await service.CreateAsync(
            "support-admin",
            "Support Agent",
            new CustomerSupportCaseCreateRequest("TICKET-100", "learner-1", "Login help", expiry),
            CancellationToken.None);
        var candidate = await service.GetCandidateAsync(
            "support-admin",
            "Support Agent",
            supportCase.Id,
            CancellationToken.None);

        Assert.Equal("TICKET-100", supportCase.ExternalTicketId);
        Assert.Equal("learner-1", candidate.CandidateUserId);
        Assert.Equal("candidate@example.test", candidate.Email);
        var auditDetails = await db.AuditEvents
            .Where(x => x.ResourceId == supportCase.Id)
            .Select(x => x.Details)
            .ToListAsync();
        Assert.All(auditDetails, details =>
            Assert.DoesNotContain("assessment", details ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, await db.AuditEvents.CountAsync(x => x.ResourceId == supportCase.Id));
    }

    [Fact]
    public async Task Expired_or_closed_case_cannot_project_candidate()
    {
        await using var db = NewDb();
        await AddLearnerAsync(db);
        var service = new CustomerSupportCaseService(db);

        var expired = await service.CreateAsync(
            "support-admin",
            "Support Agent",
            new CustomerSupportCaseCreateRequest(
                "TICKET-101",
                "learner-1",
                "Expired access",
                DateTimeOffset.UtcNow.AddMinutes(5)),
            CancellationToken.None);
        var tracked = await db.CustomerSupportCases.SingleAsync(x => x.Id == expired.Id);
        tracked.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var expiredError = await Assert.ThrowsAsync<ApiException>(() => service.GetCandidateAsync(
            "support-admin",
            "Support Agent",
            expired.Id,
            CancellationToken.None));
        Assert.Equal(StatusCodes.Status404NotFound, expiredError.StatusCode);

        var open = await service.CreateAsync(
            "support-admin",
            "Support Agent",
            new CustomerSupportCaseCreateRequest(
                "TICKET-102",
                "learner-1",
                "Closed access",
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);
        await service.CloseAsync(
            "support-admin",
            "Support Agent",
            open.Id,
            new CustomerSupportCaseCloseRequest("Resolved"),
            CancellationToken.None);
        var closedError = await Assert.ThrowsAsync<ApiException>(() => service.GetCandidateAsync(
            "support-admin",
            "Support Agent",
            open.Id,
            CancellationToken.None));
        Assert.Equal(StatusCodes.Status404NotFound, closedError.StatusCode);
    }

    [Fact]
    public async Task Listing_requires_ticket_and_close_is_idempotent()
    {
        await using var db = NewDb();
        await AddLearnerAsync(db);
        var service = new CustomerSupportCaseService(db);
        var supportCase = await service.CreateAsync(
            "support-admin",
            "Support Agent",
            new CustomerSupportCaseCreateRequest(
                "TICKET-103",
                "learner-1",
                "Account help",
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);

        var missingTicket = await Assert.ThrowsAsync<ApiException>(() => service.ListAsync(null, CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, missingTicket.StatusCode);

        var firstClose = await service.CloseAsync(
            "support-admin",
            "Support Agent",
            supportCase.Id,
            new CustomerSupportCaseCloseRequest("Resolved"),
            CancellationToken.None);
        var secondClose = await service.CloseAsync(
            "support-admin",
            "Support Agent",
            supportCase.Id,
            new CustomerSupportCaseCloseRequest("Already resolved"),
            CancellationToken.None);

        Assert.Equal(CustomerSupportCaseStatuses.Closed, firstClose.Status);
        Assert.Equal(firstClose.ClosedAt, secondClose.ClosedAt);
        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "support.case.closed"));
        Assert.Single(await service.ListAsync("TICKET-103", CancellationToken.None));
    }

    private static async Task AddLearnerAsync(LearnerDbContext db)
    {
        db.Users.Add(new LearnerUser
        {
            Id = "learner-1",
            AuthAccountId = "auth-1",
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Candidate One",
            Email = "candidate@example.test",
            AccountStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        });
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = "auth-1",
            Email = "candidate@example.test",
            NormalizedEmail = "CANDIDATE@EXAMPLE.TEST",
            PasswordHash = "test",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
