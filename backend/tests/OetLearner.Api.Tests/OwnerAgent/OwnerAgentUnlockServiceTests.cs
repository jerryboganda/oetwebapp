using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using OetLearner.Api.Services.OwnerAgent;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>
/// Unlock tickets: sfam + account binding, ONE fixed lifetime from the unlock (default 60 min,
/// <c>OwnerAgent:UnlockMinutes</c>, clamped 5..480) that a refresh never extends,
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

    private OwnerAgentUnlockService CreateService(int? unlockMinutes = null)
    {
        var options = new OwnerAgentOptions();
        if (unlockMinutes is { } minutes)
        {
            options.UnlockMinutes = minutes;
        }

        return new OwnerAgentUnlockService(_dataProtection, _db, _clock, new HttpContextAccessor(), Options.Create(options));
    }

    [Fact]
    public void DefaultLifetime_IsSixtyMinutes()
    {
        Assert.Equal(60, new OwnerAgentOptions().UnlockMinutes);
        Assert.Equal(TimeSpan.FromMinutes(60), OwnerAgentUnlockService.DefaultLifetime);
        Assert.Equal(TimeSpan.FromMinutes(60), CreateService().Lifetime);
    }

    [Fact]
    public async Task Issue_ThenValidate_WithSameAccountAndFamily_IsValid_ForAFixedSixtyMinutes()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);

        var ticket = service.Issue(principal);
        var validation = await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None);

        Assert.True(validation.IsValid);
        Assert.Equal(AccountId, validation.AccountId);
        Assert.Equal(_familyId, validation.SessionFamilyId);
        Assert.Equal(ticket.TicketId, validation.TicketId);
        Assert.Equal(ticket.IssuedAt + TimeSpan.FromMinutes(60), ticket.ExpiresAt);
        // Fixed lifetime: the expiry and the absolute expiry are one and the same.
        Assert.Equal(ticket.ExpiresAt, ticket.AbsoluteExpiresAt);
        Assert.Equal(ticket.ExpiresAt, validation.ExpiresAt);
        Assert.Equal(ticket.AbsoluteExpiresAt, validation.AbsoluteExpiresAt);
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
    public async Task Validate_IsStillValidAt59Minutes_AndExpiredAt61Minutes()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var ticket = service.Issue(principal);

        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.True((await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None)).IsValid);

        _clock.Advance(TimeSpan.FromMinutes(2));
        var validation = await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockExpired, validation.FailureCode);
    }

    [Fact]
    public async Task Refresh_NeverExtendsTheUnlock()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var first = service.Issue(principal);
        var current = first.Ticket;

        // Re-mint every 10 minutes: expiry and absolute expiry never move.
        for (var hop = 0; hop < 5; hop++)
        {
            _clock.Advance(TimeSpan.FromMinutes(10));
            var validation = await service.ValidateAsync(principal, current, CancellationToken.None);
            Assert.True(validation.IsValid, $"hop {hop}: {validation.FailureCode}");
            var refreshed = service.Refresh(validation);
            Assert.Equal(first.TicketId, refreshed.TicketId);
            Assert.Equal(first.IssuedAt, refreshed.IssuedAt);
            Assert.Equal(first.ExpiresAt, refreshed.ExpiresAt);
            Assert.Equal(first.AbsoluteExpiresAt, refreshed.AbsoluteExpiresAt);
            current = refreshed.Ticket;
        }

        // 61 minutes after the ORIGINAL unlock the refreshed ticket is dead too.
        _clock.Advance(TimeSpan.FromMinutes(11));
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

        _clock.Advance(TimeSpan.FromMinutes(61));
        var validation = await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Throws<ApiException>(() => service.Refresh(validation));
    }

    [Fact]
    public async Task UnlockMinutesOption_IsHonoured()
    {
        var service = CreateService(unlockMinutes: 15);
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var ticket = service.Issue(principal);

        Assert.Equal(TimeSpan.FromMinutes(15), service.Lifetime);
        Assert.Equal(ticket.IssuedAt + TimeSpan.FromMinutes(15), ticket.ExpiresAt);
        Assert.Equal(ticket.ExpiresAt, ticket.AbsoluteExpiresAt);

        _clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True((await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None)).IsValid);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(OwnerAgentFailureCodes.UnlockExpired,
            (await service.ValidateAsync(principal, ticket.Ticket, CancellationToken.None)).FailureCode);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(5, 5)]
    [InlineData(90, 90)]
    [InlineData(480, 480)]
    [InlineData(10_000, 480)]
    [InlineData(-30, 5)]
    public void UnlockMinutesOption_IsClampedTo5Through480(int configured, int expected)
    {
        Assert.Equal(TimeSpan.FromMinutes(expected), new OwnerAgentOptions { UnlockMinutes = configured }.UnlockLifetime);
        Assert.Equal(TimeSpan.FromMinutes(expected), CreateService(configured).Lifetime);
    }

    [Fact]
    public async Task Ticket_LongerThanTheConfiguredLifetime_IsRejected()
    {
        // e.g. minted before UnlockMinutes was lowered.
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var longLived = CreateService(unlockMinutes: 120).Issue(principal);

        var validation = await CreateService(unlockMinutes: 60).ValidateAsync(principal, longLived.Ticket, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockExpired, validation.FailureCode);
    }

    [Fact]
    public async Task LegacySlidingTicket_WithAnEightHourAbsoluteCap_IsRejected()
    {
        // Shape minted by the former 45-minute sliding / 8-hour absolute scheme.
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(_clock.GetUtcNow().ToUnixTimeMilliseconds());
        var legacy = Protect(OwnerAgentUnlockService.Purpose, new
        {
            v = 1,
            id = "legacy-ticket",
            a = AccountId,
            f = _familyId.ToString("D"),
            iat = now.ToUnixTimeMilliseconds(),
            exp = now.AddMinutes(45).ToUnixTimeMilliseconds(),
            abs = now.AddHours(8).ToUnixTimeMilliseconds(),
        }, now.AddMinutes(45));

        var validation = await CreateService().ValidateAsync(principal, legacy, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal(OwnerAgentFailureCodes.UnlockExpired, validation.FailureCode);
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
    public async Task StreamRevalidation_HonoursSessionBindingAndLock()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var initial = await service.ValidateAsync(principal, service.Issue(principal).Ticket, CancellationToken.None);
        Assert.True(initial.IsValid);

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
    public async Task StreamRevalidation_StopsAtTheFixedExpiry()
    {
        var service = CreateService();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var initial = await service.ValidateAsync(principal, service.Issue(principal).Ticket, CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(61));
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
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, _familyId);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(_clock.GetUtcNow().ToUnixTimeMilliseconds());
        // A well-formed payload protected under a DIFFERENT DataProtection purpose must never pass.
        var foreign = Protect("OwnerAgent.SomethingElse.v1", new
        {
            v = 1,
            id = "foreign-ticket",
            a = AccountId,
            f = _familyId.ToString("D"),
            iat = now.ToUnixTimeMilliseconds(),
            exp = now.AddMinutes(30).ToUnixTimeMilliseconds(),
            abs = now.AddMinutes(30).ToUnixTimeMilliseconds(),
        }, now.AddMinutes(30));

        var validation = await service.ValidateAsync(principal, foreign, CancellationToken.None);
        Assert.Equal(OwnerAgentFailureCodes.UnlockInvalid, validation.FailureCode);
    }

    private string Protect(string purpose, object payload, DateTimeOffset expiresAt)
        => _dataProtection.CreateProtector(purpose).ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(payload), expiresAt);
}
