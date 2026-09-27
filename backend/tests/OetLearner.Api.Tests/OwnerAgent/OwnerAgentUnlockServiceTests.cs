using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using OetLearner.Api.Services.OwnerAgent;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>
/// Unlock tickets: sfam + account binding, 45-minute sliding window, +8 h absolute cap,
/// lock / re-enrolment watermark, session-family liveness, tamper resistance.
/// Pure unit tests (in-memory DB, ephemeral key ring, mutable clock).
/// </summary>
public sealed class OwnerAgentUnlockServiceTests : IDisposable
{
    private const string AccountId = "auth_unlock_unit_owner";
    private readonly DbContextOptions<LearnerDbContext> _dbOptions = new DbContextOptionsBuilder<LearnerDbContext>()
        .UseInMemoryDatabase($"owner-agent-unlock-{Guid.NewGuid():N}")
        .Options;
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();
    private readonly OwnerAgentTestClock _clock = new();
    private readonly LearnerDbContext _db;
    private readonly Guid _familyId = Guid.NewGuid();

    public OwnerAgentUnlockServiceTests()
    {
        _db = new LearnerDbContext(_dbOptions);
        _db.RefreshTokenRecords.Add(new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = AccountId,
            TokenHash = "unit-test-family",
            FamilyId = _familyId,
            CreatedAt = _clock.GetUtcNow(),
            ExpiresAt = _clock.GetUtcNow().AddDays(30),
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private OwnerAgentUnlockService CreateService()
        => new(_dataProtection, _db, _clock, new HttpContextAccessor());

    [Fact]
    public async Task Issue_ThenValidate_WithSameAccountAndFamily_IsValid()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);

        var ticket = service.Issue(principal);
        var validation = await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None);

        Assert.True(validation.IsValid);
        Assert.Equal(AccountId, validation.AccountId);
        Assert.Equal(_familyId, validation.SessionFamilyId);
        Assert.Equal(ticket.TicketId, validation.TicketId);
        Assert.Equal(ticket.IssuedAt + OwnerAgentUnlockService.SlidingLifetime, validation.ExpiresAt);
        Assert.Equal(ticket.IssuedAt + OwnerAgentUnlockService.AbsoluteLifetime, validation.AbsoluteExpiresAt);
    }

    [Fact]
    public void Issue_WithoutSessionFamilyClaim_IsRefused()
    {
        var service = CreateService();
        var ex = Assert.Throws<ApiException>(() => service.Issue(OwnerAgentWebApplicationFactory.Principal(AccountId, familyId: null)));
        Assert.Equal("owner_agent_session_family_required", ex.ErrorCode);
    }

    [Fact]
    public async Task Validate_FromAnotherSessionFamily_IsRejected()
    {
        var service = CreateService();
        var ticket = service.Issue(OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId));

        var otherFamily = OwnerAgentWebApplicationFactory.Principal(AccountId, Guid.NewGuid());
        var validation = await service.ValidateAsync(otherFamily, ticket.Ticket, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockSessionMismatch, validation.FailureCode);
    }

    [Fact]
    public async Task Validate_ByAnotherAccount_IsRejected()
    {
        var service = CreateService();
        var ticket = service.Issue(OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId));

        var otherAccount = OwnerAgentWebApplicationFactory.Principal("auth_someone_else", _familyId);
        var validation = await service.ValidateAsync(otherAccount, ticket.Ticket, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockSessionMismatch, validation.FailureCode);
    }

    [Fact]
    public async Task Validate_WithoutSfamClaim_IsRejected()
    {
        var service = CreateService();
        var ticket = service.Issue(OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId));

        var validation = await service.ValidateAsync(
            OwnerAgentWebApplicationFactory.Principal(AccountId, familyId: null), ticket.Ticket, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockSessionMismatch, validation.FailureCode);
    }

    [Fact]
    public async Task Validate_AfterSlidingWindow_IsExpired()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var ticket = service.Issue(principal);

        _clock.Advance(OwnerAgentUnlockService.SlidingLifetime + TimeSpan.FromSeconds(1));
        var validation = await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockExpired, validation.FailureCode);
    }

    [Fact]
    public async Task Refresh_SlidesWindow_ButNeverPastAbsoluteCap()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var first = service.Issue(principal);
        var current = first.Ticket;

        // Re-mint every 40 minutes for 7h20m: each hop must still be valid.
        for (var hop = 0; hop < 11; hop++)
        {
            _clock.Advance(TimeSpan.FromMinutes(40));
            var validation = await service.ValidateAsync(principal, current, CancellationToken.None);
            Assert.True(validation.IsValid, $"hop {hop}: {validation.FailureCode}");
            var refreshed = service.Refresh(validation);
            Assert.Equal(first.AbsoluteExpiresAt, refreshed.AbsoluteExpiresAt);
            Assert.Equal(first.TicketId, refreshed.TicketId);
            Assert.True(refreshed.ExpiresAt <= refreshed.AbsoluteExpiresAt);
            current = refreshed.Ticket;
        }

        // At 7h20m the 45-minute window is clipped to the absolute cap (8h).
        var last = await service.ValidateAsync(principal, current, CancellationToken.None);
        Assert.True(last.IsValid);
        Assert.Equal(first.AbsoluteExpiresAt, last.ExpiresAt);

        _clock.Advance(TimeSpan.FromMinutes(41));
        var expired = await service.ValidateAsync(principal, current, CancellationToken.None);
        Assert.False(expired.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockExpired, expired.FailureCode);
    }

    [Fact]
    public async Task Refresh_DoesNotResurrectAnExpiredTicket()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var ticket = service.Issue(principal);

        _clock.Advance(TimeSpan.FromMinutes(46));
        var validation = await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Throws<ApiException>(() => service.Refresh(validation));
    }

    [Fact]
    public async Task Lock_RevokesEveryTicketIssuedBeforeIt_ButNotLaterOnes()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var before = service.Issue(principal);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await service.LockAsync(AccountId, _familyId, "unit_test", CancellationToken.None);

        var revoked = await service.ValidateAsync(principal, before.Ticket, CancellationToken.None);
        Assert.False(revoked.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRevoked, revoked.FailureCode);

        _clock.Advance(TimeSpan.FromSeconds(1));
        var after = service.Issue(principal);
        Assert.True((await service.ValidateAsync(principal, after.Ticket, CancellationToken.None)).IsValid);
    }

    [Fact]
    public async Task AuthenticatorReenrolment_RevokesTickets_AndBlocksUnlockFor72Hours()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var ticket = service.Issue(principal);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _db.SecurityEvents.Add(OwnerAgentSecurityEvents.Create(
            AccountId, OwnerAgentSecurityEventKinds.AuthenticatorReenrolled, _clock.GetUtcNow(), httpContext: null));
        await _db.SaveChangesAsync();

        var validation = await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRevoked, validation.FailureCode);

        var blockedUntil = await service.GetUnlockBlockedUntilAsync(AccountId, CancellationToken.None);
        Assert.NotNull(blockedUntil);
        Assert.Equal(_clock.GetUtcNow() + OwnerAgentSecurityEventKinds.ReenrolmentUnlockCooldown, blockedUntil);

        _clock.Advance(OwnerAgentSecurityEventKinds.ReenrolmentUnlockCooldown + TimeSpan.FromMinutes(1));
        Assert.Null(await service.GetUnlockBlockedUntilAsync(AccountId, CancellationToken.None));
    }

    [Fact]
    public async Task StreamRevalidation_IgnoresTheSlidingWindow_ButHonoursLockAndAbsoluteCap()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var initial = await service.ValidateAsync(principal, service.Issue(principal).Ticket, CancellationToken.None);
        Assert.True(initial.IsValid);

        // Past the connect-time ticket's 45 minutes: the family is still unlocked (the client
        // re-mints; each long poll re-presents the fresh ticket to the endpoint policy).
        _clock.Advance(TimeSpan.FromMinutes(50));
        Assert.True((await service.RevalidateFamilyAsync(principal, initial, CancellationToken.None)).IsValid);

        // Another session can never ride on this stream's validation.
        var other = await service.RevalidateFamilyAsync(
            OwnerAgentWebApplicationFactory.Principal(AccountId, Guid.NewGuid()), initial, CancellationToken.None);
        Assert.Equal(OwnerAgentFailureCodes.UnlockSessionMismatch, other.FailureCode);

        await service.LockAsync(AccountId, _familyId, "unit_test", CancellationToken.None);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRevoked,
            (await service.RevalidateFamilyAsync(principal, initial, CancellationToken.None)).FailureCode);
    }

    [Fact]
    public async Task StreamRevalidation_StopsAtTheAbsoluteCap()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var initial = await service.ValidateAsync(principal, service.Issue(principal).Ticket, CancellationToken.None);

        _clock.Advance(OwnerAgentUnlockService.AbsoluteLifetime + TimeSpan.FromSeconds(1));
        Assert.Equal(OwnerAgentFailureCodes.UnlockExpired,
            (await service.RevalidateFamilyAsync(principal, initial, CancellationToken.None)).FailureCode);
    }

    [Fact]
    public async Task Validate_AfterSessionFamilyRevoked_IsRejected()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var ticket = service.Issue(principal);

        var family = await _db.RefreshTokenRecords.SingleAsync(r => r.FamilyId == _familyId);
        family.RevokedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync();

        var validation = await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.SessionRevoked, validation.FailureCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-ticket")]
    [InlineData("CfDJ8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Validate_GarbageOrMissingTicket_IsRejected(string presented)
    {
        var service = CreateService();
        var validation = await service.ValidateAsync(
            OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId), presented, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.FailureCode, new[] { OwnerAgentFailureCodes.UnlockRequired, OwnerAgentFailureCodes.UnlockInvalid });
    }

    [Fact]
    public async Task Validate_TicketFromAnotherPurpose_IsRejected()
    {
        var service = CreateService();
        // A step-up token (different DataProtection purpose) must never pass as an unlock ticket.
        var stepUp = new OwnerAgentStepUpService(_dataProtection, new OwnerAgentStepUpReplayCache(), _clock);
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var unlock = await service.ValidateAsync(principal, service.Issue(principal).Ticket, CancellationToken.None);
        var foreign = stepUp.Issue(principal, unlock).Token;

        var validation = await service.ValidateAsync(principal, foreign, CancellationToken.None);
        Assert.Equal(OwnerAgentFailureCodes.UnlockInvalid, validation.FailureCode);
    }
}
