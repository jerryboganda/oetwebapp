using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Services.OwnerAgent;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>Step-up token single use / binding, and the hash-chained, sanitized audit.</summary>
public sealed class OwnerAgentStepUpAndAuditTests
{
    private const string AccountId = "auth_step_up_unit_owner";

    private static OwnerAgentUnlockValidation Unlock(Guid familyId, string ticketId = "ticket-family-1", DateTimeOffset? expiresAt = null)
        => new(true, null, AccountId, familyId, ticketId, DateTimeOffset.UtcNow, expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(45), DateTimeOffset.UtcNow.AddHours(8));

    [Fact]
    public void StepUpToken_CanBeConsumedExactlyOnce()
    {
        var clock = new OwnerAgentTestClock();
        var service = new OwnerAgentStepUpService(new EphemeralDataProtectionProvider(), new OwnerAgentStepUpReplayCache(), clock);
        var familyId = Guid.NewGuid();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, familyId);
        var unlock = Unlock(familyId);

        var token = service.Issue(principal, unlock);

        Assert.True(service.Consume(principal, unlock, token.Token).IsValid);
        var replay = service.Consume(principal, unlock, token.Token);
        Assert.False(replay.IsValid);
        Assert.Equal(OwnerAgentStepUpFailureCodes.AlreadyUsed, replay.FailureCode);
    }

    [Fact]
    public void StepUpToken_IsSingleUseAcrossServiceInstancesOnTheSameSlot()
    {
        // Scoped services share the singleton replay cache.
        var clock = new OwnerAgentTestClock();
        var keys = new EphemeralDataProtectionProvider();
        var cache = new OwnerAgentStepUpReplayCache();
        var familyId = Guid.NewGuid();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, familyId);
        var unlock = Unlock(familyId);
        var token = new OwnerAgentStepUpService(keys, cache, clock).Issue(principal, unlock);

        Assert.True(new OwnerAgentStepUpService(keys, cache, clock).Consume(principal, unlock, token.Token).IsValid);
        Assert.False(new OwnerAgentStepUpService(keys, cache, clock).Consume(principal, unlock, token.Token).IsValid);
    }

    [Fact]
    public void StepUpToken_ExpiresAfterFiveMinutes()
    {
        var clock = new OwnerAgentTestClock();
        var service = new OwnerAgentStepUpService(new EphemeralDataProtectionProvider(), new OwnerAgentStepUpReplayCache(), clock);
        var familyId = Guid.NewGuid();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, familyId);
        var unlock = Unlock(familyId);
        var token = service.Issue(principal, unlock);

        Assert.True(token.ExpiresAt <= clock.GetUtcNow() + OwnerAgentStepUpService.Lifetime);
        clock.Advance(OwnerAgentStepUpService.Lifetime + TimeSpan.FromSeconds(1));

        var result = service.Consume(principal, unlock, token.Token);
        Assert.Equal(OwnerAgentStepUpFailureCodes.Expired, result.FailureCode);
    }

    [Fact]
    public void StepUpToken_IsBoundToSessionFamilyAndUnlockFamily()
    {
        var clock = new OwnerAgentTestClock();
        var service = new OwnerAgentStepUpService(new EphemeralDataProtectionProvider(), new OwnerAgentStepUpReplayCache(), clock);
        var familyId = Guid.NewGuid();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, familyId);
        var token = service.Issue(principal, Unlock(familyId, "unlock-a")).Token;

        // Another session (different sfam) cannot use it.
        var otherSession = OwnerAgentWebApplicationFactory.Principal(AccountId, Guid.NewGuid());
        Assert.Equal(OwnerAgentStepUpFailureCodes.Invalid, service.Consume(otherSession, Unlock(familyId, "unlock-a"), token).FailureCode);

        // A re-unlocked console (new unlock ticket family) cannot use it.
        Assert.Equal(OwnerAgentStepUpFailureCodes.Invalid, service.Consume(principal, Unlock(familyId, "unlock-b"), token).FailureCode);

        // Missing header.
        Assert.Equal(OwnerAgentStepUpFailureCodes.Required, service.Consume(principal, Unlock(familyId, "unlock-a"), null).FailureCode);

        // The genuine binding still works (the failed attempts above did not burn it).
        Assert.True(service.Consume(principal, Unlock(familyId, "unlock-a"), token).IsValid);
    }

    [Fact]
    public void StepUpToken_NeverOutlivesTheUnlock()
    {
        var clock = new OwnerAgentTestClock();
        var service = new OwnerAgentStepUpService(new EphemeralDataProtectionProvider(), new OwnerAgentStepUpReplayCache(), clock);
        var familyId = Guid.NewGuid();
        var principal = OwnerAgentWebApplicationFactory.Principal(AccountId, familyId);
        var unlockExpires = clock.GetUtcNow().AddMinutes(2);

        var token = service.Issue(principal, Unlock(familyId, expiresAt: unlockExpires));

        Assert.True(token.ExpiresAt <= unlockExpires);
    }

    // ── Audit ───────────────────────────────────────────────────────────────

    private static (LearnerDbContext Db, OwnerAgentAuditService Audit) CreateAudit()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"owner-agent-audit-{Guid.NewGuid():N}")
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new OwnerAgentAuditService(db, new OwnerAgentTestClock(), NullLogger<OwnerAgentAuditService>.Instance));
    }

    private static ClaimsPrincipal Actor() => OwnerAgentWebApplicationFactory.Principal(AccountId, Guid.NewGuid());

    [Fact]
    public async Task Audit_IsHashChained_AndDetectsTampering()
    {
        var (db, audit) = CreateAudit();
        await using var _ = db;

        await audit.WriteAsync(Actor(), OwnerAgentAuditActions.Unlock, null, new Dictionary<string, object?> { ["ticketId"] = "t1" }, CancellationToken.None);
        await audit.WriteAsync(Actor(), OwnerAgentAuditActions.SessionCreated, FakeSidecarHandler.SessionId, new Dictionary<string, object?> { ["engine"] = "claude" }, CancellationToken.None);
        await audit.WriteAsync(Actor(), OwnerAgentAuditActions.KillSwitch, null, null, CancellationToken.None);

        var page = await audit.ListAsync(100, CancellationToken.None);
        Assert.Equal(3, page.Items.Count);
        Assert.True(page.ChainIntact);
        Assert.All(page.Items, item => Assert.True(item.HashValid));
        Assert.Equal(OwnerAgentAuditService.GenesisHash, page.Items[^1].PreviousHash);
        Assert.Equal(page.Items[1].Hash, page.Items[0].PreviousHash);
        Assert.Equal(OwnerAgentAuditActions.KillSwitch, page.Items[0].Action);

        // Rewrite the middle row's payload (e.g. the agent editing audit rows via its DB role).
        var middle = await db.AuditEvents.SingleAsync(e => e.Id == page.Items[1].Id);
        middle.Details = middle.Details!.Replace("\"claude\"", "\"codex\"", StringComparison.Ordinal);
        await db.SaveChangesAsync();

        var tampered = await audit.ListAsync(100, CancellationToken.None);
        Assert.False(tampered.ChainIntact);
        Assert.False(tampered.Items.Single(i => i.Id == middle.Id).HashValid);
    }

    [Fact]
    public async Task Audit_DeletingARow_BreaksTheChain()
    {
        var (db, audit) = CreateAudit();
        await using var _ = db;
        for (var i = 0; i < 3; i++)
        {
            await audit.WriteAsync(Actor(), OwnerAgentAuditActions.MessageSent, FakeSidecarHandler.SessionId,
                new Dictionary<string, object?> { ["length"] = i }, CancellationToken.None);
        }

        var page = await audit.ListAsync(100, CancellationToken.None);
        db.AuditEvents.Remove(await db.AuditEvents.SingleAsync(e => e.Id == page.Items[1].Id));
        await db.SaveChangesAsync();

        Assert.False((await audit.ListAsync(100, CancellationToken.None)).ChainIntact);
    }

    [Fact]
    public async Task Audit_Details_AreSanitized_AndCapped()
    {
        var (db, audit) = CreateAudit();
        await using var _ = db;
        var longText = new string('x', 5_000);
        // Credential-shaped values are assembled at runtime so static secret scanners
        // never see a token-looking literal in the repository.
        var fakePat = "github" + "_pat_" + new string('A', 22) + "_" + new string('b', 20);
        var fakeDsn = "postgres" + "://app:" + "not-a-real-pw" + "@db:5432/oet";
        var fakeJwt = "ey" + "J" + new string('a', 12) + ".ey" + "J" + new string('b', 12) + "." + new string('c', 16);
        await audit.WriteAsync(Actor(), OwnerAgentAuditActions.MessageSent, FakeSidecarHandler.SessionId, new Dictionary<string, object?>
        {
            ["preview"] = $"deploy with {fakePat} and {fakeDsn}",
            ["jwt"] = fakeJwt,
            ["long"] = longText,
        }, CancellationToken.None);

        var stored = (await db.AuditEvents.SingleAsync()).Details!;
        Assert.DoesNotContain(fakePat, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-real-pw", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(fakeJwt, stored, StringComparison.Ordinal);
        Assert.Contains(OwnerAgentAuditSanitizer.Redacted, stored, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(stored);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(OwnerAgentAuditSanitizer.MaxStringLength, data.GetProperty("long").GetString()!.Length);
    }

    public static IEnumerable<object[]> CredentialShapes()
    {
        // Built at runtime (never a token-looking literal in source).
        yield return ["gh" + "p_" + new string('a', 36)];
        yield return ["gh" + "s_" + new string('b', 36)];
        yield return ["sk" + "-proj-" + new string('c', 24)];
        yield return ["xo" + "xb-" + new string('1', 10) + "-" + new string('d', 10)];
        yield return ["AK" + "IA" + new string('E', 16)];
        yield return ["-----BEGIN " + "OPENSSH PRIVATE KEY-----\nabc\n-----END " + "OPENSSH PRIVATE KEY-----"];
    }

    [Theory]
    [MemberData(nameof(CredentialShapes))]
    public void Sanitizer_RedactsCredentialShapes(string secret)
    {
        var scrubbed = OwnerAgentAuditSanitizer.Scrub($"before {secret} after", maxLength: 10_000);
        Assert.DoesNotContain(secret, scrubbed, StringComparison.Ordinal);
        Assert.Contains(OwnerAgentAuditSanitizer.Redacted, scrubbed, StringComparison.Ordinal);
    }
}
