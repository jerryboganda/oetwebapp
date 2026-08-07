using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;

namespace OetLearner.Api.Tests;

/// <summary>
/// Direct unit coverage for <see cref="TrustedDeviceService"/> — the real
/// bootstrap/trust/reset/resolve device logic backing the currently
/// enforced "trusted device" feature (SecurityTrustedDeviceRequired defaults
/// to true in the mandatory production profile). <see cref="AuthFlowsTests"/> only ever
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

    private static async Task SeedAccountAsync(
        LearnerDbContext db,
        string authAccountId,
        int? maxDevicesOverride)
    {
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = authAccountId,
            Email = $"{authAccountId}@example.test",
            NormalizedEmail = $"{authAccountId}@EXAMPLE.TEST".ToUpperInvariant(),
            PasswordHash = "test-hash",
            MaxDevicesOverride = maxDevicesOverride,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
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
        Assert.Single(events.Calls);
        Assert.Equal(SecurityEventKinds.DeviceTrustRejected, events.Calls[0].Kind);
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
        // Two OTP-approved replacement events within the last 30 days: one
        // superseded, one currently active. Cooldown counts replacement rows
        // with TrustedAt inside the window, not just the active one.
        await SeedDeviceAsync(
            db,
            AccountId,
            "device-a",
            clock.GetUtcNow().AddDays(-5),
            revokedAt: clock.GetUtcNow().AddDays(-3),
            trustGrantedVia: "otp_verified");
        await SeedDeviceAsync(
            db,
            AccountId,
            "device-b",
            clock.GetUtcNow().AddDays(-3),
            trustGrantedVia: "otp_verified");

        var result = await service.ResolveForSignInAsync(AccountId, "device-c", changeWindowDays: 30, changeMaxPerWindow: 2, default);

        Assert.Equal(DeviceResolution.CooldownBlocked, result.Resolution);
        Assert.Contains(events.Calls, logged =>
            logged.Kind == SecurityEventKinds.DeviceChangeBlockedCooldown
            && logged.AuthAccountId == AccountId
            && logged.DeviceId == "device-c");
        Assert.Contains(events.Calls, logged => logged.Kind == SecurityEventKinds.DeviceTrustRejected);
        Assert.Empty(sessions.RevokeAllCalls);
        Assert.Equal(2, await db.TrustedDevices.CountAsync(d => d.ApplicationUserAccountId == AccountId));
    }

    [Fact]
    public async Task ResolveForSignInAsync_InitialBootstrapDoesNotConsumeDeviceChangeBudget()
    {
        var (db, service, clock, sessions, events) = Build();
        await SeedDeviceAsync(
            db,
            AccountId,
            "device-bootstrap",
            clock.GetUtcNow().AddDays(-5),
            trustGrantedVia: "bootstrap");
        await SeedDeviceAsync(
            db,
            AccountId,
            "device-otp-1",
            clock.GetUtcNow().AddDays(-4),
            revokedAt: clock.GetUtcNow().AddDays(-3),
            trustGrantedVia: "otp_verified");
        await SeedDeviceAsync(
            db,
            AccountId,
            "device-otp-2",
            clock.GetUtcNow().AddDays(-2),
            trustGrantedVia: "otp_verified");

        var result = await service.ResolveForSignInAsync(
            AccountId,
            "device-new",
            changeWindowDays: 30,
            changeMaxPerWindow: 3,
            default);

        Assert.Equal(DeviceResolution.OtpRequired, result.Resolution);
        Assert.Contains(events.Calls, call => call.Kind == SecurityEventKinds.DeviceTrustRequested);
        Assert.DoesNotContain(events.Calls, call => call.Kind == SecurityEventKinds.DeviceChangeBlockedCooldown);
        Assert.Empty(sessions.RevokeAllCalls);
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

    [Fact]
    public async Task ResolveForSignInAsync_PerLearnerDeviceSlotsDoNotChangeRollingCooldownBudget()
    {
        var (db, service, clock, sessions, events) = Build();
        await SeedAccountAsync(db, AccountId, maxDevicesOverride: 5);
        await SeedDeviceAsync(
            db,
            AccountId,
            "device-a",
            clock.GetUtcNow().AddDays(-5),
            revokedAt: clock.GetUtcNow().AddDays(-3),
            trustGrantedVia: "otp_verified");
        await SeedDeviceAsync(
            db,
            AccountId,
            "device-b",
            clock.GetUtcNow().AddDays(-3),
            trustGrantedVia: "otp_verified");

        var result = await service.ResolveForSignInAsync(
            AccountId, "device-c", changeWindowDays: 30, changeMaxPerWindow: 2, default);

        Assert.Equal(DeviceResolution.CooldownBlocked, result.Resolution);
        Assert.Contains(events.Calls, call => call.Kind == SecurityEventKinds.DeviceChangeBlockedCooldown);
        Assert.Contains(events.Calls, call => call.Kind == SecurityEventKinds.DeviceTrustRejected);
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
        Assert.Equal("device_replaced", revokeCall.Reason);

        Assert.Equal(2, events.Calls.Count);
        Assert.Contains(events.Calls, c => c.Kind == SecurityEventKinds.DeviceTrusted && c.DeviceId == "device-new");
        Assert.Contains(events.Calls, c => c.Kind == SecurityEventKinds.DeviceRevoked && c.DeviceId == "device-old");
    }

    /// <summary>
    /// Anomalous state that should never arise under single-writer operation
    /// (TrustDeviceAsync always revokes every prior active device before
    /// adding the new one) but could occur under a concurrent-sign-in race:
    /// two prior active rows exist. All prior rows get revoked in the
    /// database, session revocation fires exactly once (RevokeAllFamiliesAsync
    /// revokes every family for the account regardless of device count), and
    /// — per the fix closing a prior under-reporting gap — a DeviceRevoked
    /// security event is logged for EACH prior row, not just the first, so
    /// the audit trail doesn't silently omit which devices were actually
    /// revoked.
    /// </summary>
    [Fact]
    public async Task TrustDeviceAsync_MultiplePriorActiveDeviceRows_RevokesAllRowsAndLogsOneRevokedEventPerRow()
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
        Assert.Equal(2, revokedEvents.Count);
        Assert.Contains(revokedEvents, c => c.DeviceId == "device-a");
        Assert.Contains(revokedEvents, c => c.DeviceId == "device-b");
    }

    [Fact]
    public async Task TrustDeviceAsync_PositiveOverrideRetainsApprovedIdentitiesUntilTheOverrideIsFull()
    {
        var (db, service, clock, sessions, events) = Build();
        await SeedAccountAsync(db, AccountId, maxDevicesOverride: 2);
        await SeedDeviceAsync(db, AccountId, "device-old", clock.GetUtcNow());

        await service.TrustDeviceAsync(AccountId, "device-new", "App", "capacitor-ios", "otp_verified", default);

        var active = await db.TrustedDevices.AsNoTracking()
            .Where(d => d.ApplicationUserAccountId == AccountId && d.RevokedAt == null)
            .Select(d => d.DeviceId)
            .ToListAsync();
        Assert.Equal(2, active.Count);
        Assert.Contains("device-old", active);
        Assert.Contains("device-new", active);
        Assert.Empty(sessions.RevokeAllCalls);
        Assert.Empty(sessions.RevokeDeviceCalls);
        Assert.Contains(events.Calls, call => call.Kind == SecurityEventKinds.DeviceTrusted && call.DeviceId == "device-new");
    }

    [Fact]
    public async Task TrustDeviceAsync_PositiveOverrideAtCapacityRevokesOldestIdentityAndItsSessions()
    {
        var (db, service, clock, sessions, events) = Build();
        await SeedAccountAsync(db, AccountId, maxDevicesOverride: 2);
        await SeedDeviceAsync(db, AccountId, "device-oldest", clock.GetUtcNow().AddMinutes(-20));
        await SeedDeviceAsync(db, AccountId, "device-recent", clock.GetUtcNow().AddMinutes(-10));

        await service.TrustDeviceAsync(AccountId, "device-new", null, "web", "otp_verified", default);

        var active = await db.TrustedDevices.AsNoTracking()
            .Where(d => d.ApplicationUserAccountId == AccountId && d.RevokedAt == null)
            .Select(d => d.DeviceId)
            .ToListAsync();
        Assert.Equal(2, active.Count);
        Assert.DoesNotContain("device-oldest", active);
        Assert.Contains("device-recent", active);
        Assert.Contains("device-new", active);
        var revokeCall = Assert.Single(sessions.RevokeDeviceCalls);
        Assert.Equal("device-oldest", revokeCall.DeviceId);
        Assert.Equal("device_limit_replaced", revokeCall.Reason);
        Assert.Contains(events.Calls, call => call.Kind == SecurityEventKinds.DeviceRevoked && call.DeviceId == "device-oldest");
    }

    [Fact]
    public async Task EnforceDeviceLimitAsync_LoweringLimitRevokesOldestIdentityAndItsSessions()
    {
        var (db, service, clock, sessions, events) = Build();
        await SeedAccountAsync(db, AccountId, maxDevicesOverride: 3);
        await SeedDeviceAsync(db, AccountId, "device-oldest", clock.GetUtcNow().AddMinutes(-20));
        await SeedDeviceAsync(db, AccountId, "device-recent", clock.GetUtcNow().AddMinutes(-10));

        var revoked = await service.EnforceDeviceLimitAsync(AccountId, 1, default);

        Assert.Equal(1, revoked);
        Assert.Single(sessions.RevokeDeviceCalls);
        Assert.Equal("device-oldest", sessions.RevokeDeviceCalls[0].DeviceId);
        Assert.Contains(events.Calls, call => call.Kind == SecurityEventKinds.DeviceRevoked && call.DeviceId == "device-oldest");
        Assert.Equal(1, await db.TrustedDevices.CountAsync(d => d.ApplicationUserAccountId == AccountId && d.RevokedAt == null));
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
        // Unlike TrustDeviceAsync's replacement reason, the
        // admin-reset path passes the caller-supplied reason straight through.
        Assert.Equal("owner_requested_reset", revokeCall.Reason);

        // The account has no active device anymore, so the very next
        // sign-in resolution silently bootstraps a new one.
        var resolution = await service.ResolveForSignInAsync(AccountId, "device-2", changeWindowDays: 30, changeMaxPerWindow: 3, default);
        Assert.Equal(DeviceResolution.Bootstrap, resolution.Resolution);
    }

    /// <summary>
    /// When there is nothing to reset, ResetDeviceAsync still logs a
    /// DeviceAdminReset event (flagged noActiveDevice=true in its details) —
    /// per the fix closing a prior gap where an admin-triggered reset attempt
    /// against an account with no active device left ZERO audit trail that
    /// it was even attempted. Session revocation is correctly still skipped:
    /// there is nothing live tied to a device that never existed.
    /// </summary>
    [Fact]
    public async Task ResetDeviceAsync_NoActiveDevice_LogsAttemptButRevokesNoSessions()
    {
        var (db, service, _, sessions, events) = Build();

        await service.ResetDeviceAsync(AccountId, "owner_requested_reset", default);

        Assert.Equal(0, await db.TrustedDevices.CountAsync(d => d.ApplicationUserAccountId == AccountId));

        var logged = Assert.Single(events.Calls);
        Assert.Equal(SecurityEventKinds.DeviceAdminReset, logged.Kind);
        Assert.Equal(AccountId, logged.AuthAccountId);
        var noActiveDevice = logged.Details?.GetType().GetProperty("noActiveDevice")?.GetValue(logged.Details) as bool?;
        Assert.True(noActiveDevice);

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
        public List<(string AuthAccountId, string DeviceId, string Reason)> RevokeDeviceCalls { get; } = new();

        public Task<int> RevokeAllFamiliesAsync(string authAccountId, Guid? exceptFamilyId, string reason, CancellationToken ct)
        {
            RevokeAllCalls.Add((authAccountId, exceptFamilyId, reason));
            return Task.FromResult(0);
        }

        public Task<bool> RevokeFamilyAsync(string authAccountId, Guid familyId, string reason, CancellationToken ct)
            => throw new NotSupportedException("TrustedDeviceService does not call single-family revocation directly.");

        public Task<int> RevokeDeviceFamiliesAsync(string authAccountId, string deviceId, string reason, CancellationToken ct)
        {
            RevokeDeviceCalls.Add((authAccountId, deviceId, reason));
            return Task.FromResult(0);
        }
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
