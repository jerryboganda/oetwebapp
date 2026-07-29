using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;

namespace OetLearner.Api.Tests;

/// <summary>
/// Direct unit coverage for <see cref="TrustedDeviceService"/> — the real
/// bootstrap/trust/reset/resolve device logic backing the currently
/// dark-launched "trusted device" feature (SecurityTrustedDeviceRequired
/// defaults to false in production). <see cref="AuthFlowsTests"/> only ever
/// exercises a hand-rolled no-op stub of this service, so none of the
/// branches below previously had any automated coverage.
/// </summary>
public class TrustedDeviceServiceTests
{
    private const string AccountId = "auth_trusted_device_tests_001";

    private static (LearnerDbContext Db, TrustedDeviceService Service, TestClock Clock,
        RecordingSessionRevocationService Sessions, RecordingSecurityEventLogger Events) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var sessions = new RecordingSessionRevocationService();
        var events = new RecordingSecurityEventLogger();
        var service = new TrustedDeviceService(db, sessions, events, clock);
        return (db, service, clock, sessions, events);
    }

    private static async Task<TrustedDevice> SeedDeviceAsync(
        LearnerDbContext db,
        string authAccountId,
        string deviceId,
        DateTimeOffset trustedAt,
        DateTimeOffset? revokedAt = null,
        DateTimeOffset? lastSeenAt = null,
        string trustGrantedVia = "bootstrap")
    {
        var device = new TrustedDevice
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = authAccountId,
            DeviceId = deviceId,
            CreatedAt = trustedAt,
            TrustedAt = trustedAt,
            LastSeenAt = lastSeenAt ?? trustedAt,
            RevokedAt = revokedAt,
            TrustGrantedVia = trustGrantedVia,
        };
        db.TrustedDevices.Add(device);
        await db.SaveChangesAsync();
        return device;
    }

    // ---------------------------------------------------------------
    // ResolveForSignInAsync
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveForSignInAsync_NoOrBlankDeviceId_ReturnsNoDeviceIdRegardlessOfTrustedState(string? deviceId)
    {
        var (db, service, clock, sessions, events) = Build();
        var seeded = await SeedDeviceAsync(db, AccountId, "device-1", clock.GetUtcNow());

        var result = await service.ResolveForSignInAsync(AccountId, deviceId, changeWindowDays: 30, changeMaxPerWindow: 3, default);

        Assert.Equal(DeviceResolution.NoDeviceId, result.Resolution);
        Assert.Empty(events.Calls);
        Assert.Empty(sessions.RevokeAllCalls);

        var reloaded = await db.TrustedDevices.AsNoTracking().SingleAsync(d => d.Id == seeded.Id);
        Assert.Equal(seeded.LastSeenAt, reloaded.LastSeenAt);
    }

    /// <summary>
    /// A brand-new account with no trusted device yet is bootstrapped
    /// SILENTLY — it does NOT require OTP verification. This differs from
    /// changing an already-trusted device (which does require OTP, see
    /// <see cref="ResolveForSignInAsync_DifferentDeviceWithinCooldownBudget_RequiresOtpAndLogsTrustRequested"/>).
    /// Per the source comment on <see cref="DeviceResolution.Bootstrap"/>,
    /// this is deliberate: registration already proved email ownership, so
    /// the first device gets a free pass. <see cref="TrustedDeviceService.ResolveForSignInAsync"/>
    /// itself never writes to the database or logs a security event for this
    /// branch — the caller (AuthService) is responsible for actually calling
    /// <see cref="TrustedDeviceService.TrustDeviceAsync"/> afterward.
    /// </summary>
    [Fact]
    public async Task ResolveForSignInAsync_FreshAccountWithNoTrustedDevice_BootstrapsSilentlyWithoutOtpOrEventLog()
    {
        var (db, service, _, sessions, events) = Build();

        var result = await service.ResolveForSignInAsync(AccountId, "device-1", changeWindowDays: 30, changeMaxPerWindow: 3, default);

        Assert.Equal(DeviceResolution.Bootstrap, result.Resolution);
        Assert.Equal(0, await db.TrustedDevices.CountAsync(d => d.ApplicationUserAccountId == AccountId));
        Assert.Empty(events.Calls);
        Assert.Empty(sessions.RevokeAllCalls);
    }

    [Fact]
    public async Task ResolveForSignInAsync_SameDeviceAsCurrentlyTrusted_ReturnsTrustedAndTouchesLastSeenAt_NoOtpNoCooldownEvent()
    {
        var (db, service, clock, sessions, events) = Build();
        var seeded = await SeedDeviceAsync(db, AccountId, "device-1", clock.GetUtcNow());
        clock.Advance(TimeSpan.FromHours(2));

        var result = await service.ResolveForSignInAsync(AccountId, "device-1", changeWindowDays: 30, changeMaxPerWindow: 3, default);

        Assert.Equal(DeviceResolution.Trusted, result.Resolution);
        Assert.Empty(events.Calls);
        Assert.Empty(sessions.RevokeAllCalls);

        var reloaded = await db.TrustedDevices.AsNoTracking().SingleAsync(d => d.Id == seeded.Id);
        Assert.Equal(clock.GetUtcNow(), reloaded.LastSeenAt);
        Assert.NotEqual(seeded.LastSeenAt, reloaded.LastSeenAt);
    }

    [Fact]
    public async Task ResolveForSignInAsync_DifferentDeviceWithinCooldownBudget_RequiresOtpAndLogsTrustRequested()
    {
        var (db, service, clock, sessions, events) = Build();
        await SeedDeviceAsync(db, AccountId, "device-old", clock.GetUtcNow());

        var result = await service.ResolveForSignInAsync(AccountId, "device-new", changeWindowDays: 30, changeMaxPerWindow: 3, default);

        Assert.Equal(DeviceResolution.OtpRequired, result.Resolution);
        var logged = Assert.Single(events.Calls);
        Assert.Equal(SecurityEventKinds.DeviceTrustRequested, logged.Kind);
        Assert.Equal(AccountId, logged.AuthAccountId);
        Assert.Equal("device-new", logged.DeviceId);
        Assert.Empty(sessions.RevokeAllCalls);
        Assert.Equal(1, await db.TrustedDevices.CountAsync(d => d.ApplicationUserAccountId == AccountId));
    }

    [Fact]
    public async Task ResolveForSignInAsync_DeviceChangesAtConfiguredMaxWithinWindow_BlocksWithCooldownAndLogsBlockedEvent()
    {
        var (db, service, clock, sessions, events) = Build();
        // Two trust events within the last 30 days: one superseded, one
        // currently active. Cooldown counts ALL TrustedDevices rows with
        // TrustedAt inside the window (no RevokedAt filter), not just the
        // active one — see TrustedDeviceService.ResolveForSignInAsync.
        await SeedDeviceAsync(db, AccountId, "device-a", clock.GetUtcNow().AddDays(-5), revokedAt: clock.GetUtcNow().AddDays(-3));
        await SeedDeviceAsync(db, AccountId, "device-b", clock.GetUtcNow().AddDays(-3));

        var result = await service.ResolveForSignInAsync(AccountId, "device-c", changeWindowDays: 30, changeMaxPerWindow: 2, default);

        Assert.Equal(DeviceResolution.CooldownBlocked, result.Resolution);
        var logged = Assert.Single(events.Calls);
        Assert.Equal(SecurityEventKinds.DeviceChangeBlockedCooldown, logged.Kind);
        Assert.Equal(AccountId, logged.AuthAccountId);
        Assert.Equal("device-c", logged.DeviceId);
        Assert.Empty(sessions.RevokeAllCalls);
        Assert.Equal(2, await db.TrustedDevices.CountAsync(d => d.ApplicationUserAccountId == AccountId));
    }

    [Fact]
    public async Task ResolveForSignInAsync_PriorDeviceChangeOutsideWindow_DoesNotCountTowardCooldownBudget()
    {
        var (db, service, clock, sessions, events) = Build();
        // Only trust event is 40 days old; a 30-day window means it must NOT
        // count toward the cooldown budget, even with a max-per-window of 1.
        await SeedDeviceAsync(db, AccountId, "device-old", clock.GetUtcNow().AddDays(-40));

        var result = await service.ResolveForSignInAsync(AccountId, "device-new", changeWindowDays: 30, changeMaxPerWindow: 1, default);

        Assert.Equal(DeviceResolution.OtpRequired, result.Resolution);
        var logged = Assert.Single(events.Calls);
        Assert.Equal(SecurityEventKinds.DeviceTrustRequested, logged.Kind);
        Assert.Empty(sessions.RevokeAllCalls);
    }

    // ---------------------------------------------------------------
    // TrustDeviceAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task TrustDeviceAsync_FirstDeviceForAccount_BootstrapsWithoutRevokingAnySessions()
    {
        var (db, service, clock, sessions, events) = Build();

        await service.TrustDeviceAsync(AccountId, "device-1", "Chrome on Windows", "web", "bootstrap", default);

        var device = await db.TrustedDevices.AsNoTracking().SingleAsync(d => d.ApplicationUserAccountId == AccountId);
        Assert.Equal("device-1", device.DeviceId);
        Assert.Equal("Chrome on Windows", device.DeviceName);
        Assert.Equal("web", device.Platform);
        Assert.Equal("bootstrap", device.TrustGrantedVia);
        Assert.Null(device.RevokedAt);
        Assert.Equal(clock.GetUtcNow(), device.CreatedAt);
        Assert.Equal(clock.GetUtcNow(), device.TrustedAt);
        Assert.Equal(clock.GetUtcNow(), device.LastSeenAt);

        // The money assertion for "first device ever" — there is nothing
        // prior to revoke, so neither session revocation nor a DeviceRevoked
        // event should fire.
        Assert.Empty(sessions.RevokeAllCalls);
        Assert.DoesNotContain(events.Calls, c => c.Kind == SecurityEventKinds.DeviceRevoked);

        var trustedCall = Assert.Single(events.Calls, c => c.Kind == SecurityEventKinds.DeviceTrusted);
        Assert.Equal("device-1", trustedCall.DeviceId);
        var grantedVia = trustedCall.Details?.GetType().GetProperty("grantedVia")?.GetValue(trustedCall.Details) as string;
        Assert.Equal("bootstrap", grantedVia);
    }

    /// <summary>
    /// Core coverage for the spec requirement (TrustedDeviceService.cs XML
    /// doc §3.2): "approving a new device revokes the old one" — this must
    /// actually invoke <see cref="ISessionRevocationService.RevokeAllFamiliesAsync"/>,
    /// not just flip a database flag.
    /// </summary>
    [Fact]
    public async Task TrustDeviceAsync_TrustingNewDevice_RevokesPreviousDeviceRecordAndAllAccountSessions()
    {
        var (db, service, clock, sessions, events) = Build();
        var oldDevice = await SeedDeviceAsync(db, AccountId, "device-old", clock.GetUtcNow());
        clock.Advance(TimeSpan.FromMinutes(30));

        await service.TrustDeviceAsync(AccountId, "device-new", "Firefox on Mac", "web", "otp_verified", default);

        var reloadedOld = await db.TrustedDevices.AsNoTracking().SingleAsync(d => d.Id == oldDevice.Id);
        Assert.Equal(clock.GetUtcNow(), reloadedOld.RevokedAt);

        var newDevice = await db.TrustedDevices.AsNoTracking().SingleAsync(d => d.DeviceId == "device-new");
        Assert.Null(newDevice.RevokedAt);
        Assert.Equal("otp_verified", newDevice.TrustGrantedVia);
        Assert.Equal(clock.GetUtcNow(), newDevice.TrustedAt);

        Assert.Equal(2, await db.TrustedDevices.CountAsync(d => d.ApplicationUserAccountId == AccountId));

        var revokeCall = Assert.Single(sessions.RevokeAllCalls);
        Assert.Equal(AccountId, revokeCall.AuthAccountId);
        Assert.Null(revokeCall.ExceptFamilyId);
        Assert.Equal("device_trusted", revokeCall.Reason);

        Assert.Equal(2, events.Calls.Count);
        Assert.Contains(events.Calls, c => c.Kind == SecurityEventKinds.DeviceTrusted && c.DeviceId == "device-new");
        Assert.Contains(events.Calls, c => c.Kind == SecurityEventKinds.DeviceRevoked && c.DeviceId == "device-old");
    }

    /// <summary>
    /// Documents current behavior for an anomalous state that should never
    /// arise under single-writer operation (TrustDeviceAsync always revokes
    /// every prior active device before adding the new one) but could occur
    /// under a concurrent-sign-in race: two prior active rows exist. All
    /// prior rows DO get revoked in the database and session revocation
    /// still fires once (RevokeAllFamiliesAsync revokes every family for the
    /// account regardless of device count), but ONLY ONE DeviceRevoked
    /// security-event is logged (using priorDevices[0]) — see the flagged
    /// discrepancy in the final report to the human reviewer.
    /// </summary>
    [Fact]
    public async Task TrustDeviceAsync_MultiplePriorActiveDeviceRows_RevokesAllRowsInDbButLogsOnlyOneRevokedEvent()
    {
        var (db, service, clock, sessions, events) = Build();
        await SeedDeviceAsync(db, AccountId, "device-a", clock.GetUtcNow().AddMinutes(-10));
        await SeedDeviceAsync(db, AccountId, "device-b", clock.GetUtcNow().AddMinutes(-5));

        await service.TrustDeviceAsync(AccountId, "device-c", null, null, "otp_verified", default);

        var priorRows = await db.TrustedDevices.AsNoTracking()
            .Where(d => d.DeviceId == "device-a" || d.DeviceId == "device-b")
            .ToListAsync();
        Assert.All(priorRows, d => Assert.NotNull(d.RevokedAt));

        var revokeCall = Assert.Single(sessions.RevokeAllCalls);
        Assert.Equal(AccountId, revokeCall.AuthAccountId);

        var revokedEvents = events.Calls.Where(c => c.Kind == SecurityEventKinds.DeviceRevoked).ToList();
        var revokedEvent = Assert.Single(revokedEvents);
        Assert.True(revokedEvent.DeviceId is "device-a" or "device-b");
    }

    // ---------------------------------------------------------------
    // ResetDeviceAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task ResetDeviceAsync_ClearsActiveDeviceAndRevokesSessionsWithCallerSuppliedReason_ThenNextSignInBootstraps()
    {
        var (db, service, clock, sessions, events) = Build();
        var device = await SeedDeviceAsync(db, AccountId, "device-1", clock.GetUtcNow());
        clock.Advance(TimeSpan.FromMinutes(15));

        await service.ResetDeviceAsync(AccountId, "owner_requested_reset", default);

        var reloaded = await db.TrustedDevices.AsNoTracking().SingleAsync(d => d.Id == device.Id);
        Assert.Equal(clock.GetUtcNow(), reloaded.RevokedAt);

        var logged = Assert.Single(events.Calls);
        Assert.Equal(SecurityEventKinds.DeviceAdminReset, logged.Kind);
        Assert.Equal(AccountId, logged.AuthAccountId);
        Assert.Null(logged.DeviceId);

        var revokeCall = Assert.Single(sessions.RevokeAllCalls);
        Assert.Equal(AccountId, revokeCall.AuthAccountId);
        Assert.Null(revokeCall.ExceptFamilyId);
        // Unlike TrustDeviceAsync's hardcoded "device_trusted" reason, the
        // admin-reset path passes the caller-supplied reason straight through.
        Assert.Equal("owner_requested_reset", revokeCall.Reason);

        // The account has no active device anymore, so the very next
        // sign-in resolution silently bootstraps a new one.
        var resolution = await service.ResolveForSignInAsync(AccountId, "device-2", changeWindowDays: 30, changeMaxPerWindow: 3, default);
        Assert.Equal(DeviceResolution.Bootstrap, resolution.Resolution);
    }

    /// <summary>
    /// When there is nothing to reset, ResetDeviceAsync returns early —
    /// before logging a <see cref="SecurityEventKinds.DeviceAdminReset"/>
    /// event or calling session revocation at all. Worth a human look: an
    /// admin-triggered reset attempted against an account with no active
    /// device currently leaves ZERO audit trail of the attempt.
    /// </summary>
    [Fact]
    public async Task ResetDeviceAsync_NoActiveDevice_IsSilentNoOp_LogsNothingAndRevokesNoSessions()
    {
        var (db, service, _, sessions, events) = Build();

        await service.ResetDeviceAsync(AccountId, "owner_requested_reset", default);

        Assert.Equal(0, await db.TrustedDevices.CountAsync(d => d.ApplicationUserAccountId == AccountId));
        Assert.Empty(events.Calls);
        Assert.Empty(sessions.RevokeAllCalls);
    }

    // ---------------------------------------------------------------
    // GetActiveDeviceAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetActiveDeviceAsync_NoTrustedDevice_ReturnsNull()
    {
        var (_, service, _, _, _) = Build();

        var device = await service.GetActiveDeviceAsync(AccountId, default);

        Assert.Null(device);
    }

    [Fact]
    public async Task GetActiveDeviceAsync_ExistingActiveDevice_ReturnsItWithoutTouchingLastSeenAt()
    {
        var (db, service, clock, _, _) = Build();
        var originalLastSeenAt = clock.GetUtcNow();
        await SeedDeviceAsync(db, AccountId, "device-1", clock.GetUtcNow(), lastSeenAt: originalLastSeenAt);
        clock.Advance(TimeSpan.FromDays(1));

        var device = await service.GetActiveDeviceAsync(AccountId, default);

        Assert.NotNull(device);
        Assert.Equal("device-1", device!.DeviceId);
        Assert.Equal(originalLastSeenAt, device.LastSeenAt);

        var reloaded = await db.TrustedDevices.AsNoTracking().SingleAsync(d => d.DeviceId == "device-1");
        Assert.Equal(originalLastSeenAt, reloaded.LastSeenAt);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public TestClock(DateTimeOffset start) => _utcNow = start;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    /// <summary>Records every RevokeAllFamiliesAsync call so tests can assert
    /// TrustedDeviceService actually invokes session revocation rather than
    /// merely returning a plausible-looking result.</summary>
    private sealed class RecordingSessionRevocationService : ISessionRevocationService
    {
        public List<(string AuthAccountId, Guid? ExceptFamilyId, string Reason)> RevokeAllCalls { get; } = new();

        public Task<int> RevokeAllFamiliesAsync(string authAccountId, Guid? exceptFamilyId, string reason, CancellationToken ct)
        {
            RevokeAllCalls.Add((authAccountId, exceptFamilyId, reason));
            return Task.FromResult(0);
        }

        public Task<bool> RevokeFamilyAsync(string authAccountId, Guid familyId, string reason, CancellationToken ct)
            => throw new NotSupportedException("TrustedDeviceService only ever calls RevokeAllFamiliesAsync.");
    }

    private sealed class RecordingSecurityEventLogger : ISecurityEventLogger
    {
        public List<(string? AuthAccountId, string Kind, string? DeviceId, object? Details)> Calls { get; } = new();

        public Task TryLogAsync(
            string? authAccountId, string kind, Guid? sessionFamilyId = null, string? deviceId = null,
            object? details = null, string? severity = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((authAccountId, kind, deviceId, details));
            return Task.CompletedTask;
        }
    }
}
