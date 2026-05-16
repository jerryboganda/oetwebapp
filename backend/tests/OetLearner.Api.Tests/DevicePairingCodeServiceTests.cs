using System;
using OetLearner.Api.Services.DevicePairing;
using Xunit;

namespace OetLearner.Api.Tests;

public class DevicePairingCodeServiceTests
{
    private const string DeviceChallenge = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Initiate_ReturnsCodeAndFutureExpiry()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);

        var result = svc.Initiate("acct-1");

        Assert.Equal(6, result.Code.Length);
        Assert.True(result.ExpiresAt > clock.GetUtcNow());
        Assert.True((result.ExpiresAt - clock.GetUtcNow()).TotalSeconds <= 90);
    }

    [Fact]
    public void Redeem_ReturnsSuccess_ForValidCode()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-42");

        var result = svc.Redeem(init.Code, DeviceChallenge);

        var success = Assert.IsType<DevicePairingRedeemResult.Success>(result);
        Assert.False(string.IsNullOrWhiteSpace(success.HandoffToken));
        Assert.True(success.ExpiresAt > clock.GetUtcNow());

        var exchange = Assert.IsType<DevicePairingExchangeResult.Success>(
            svc.Exchange(success.HandoffToken, DeviceChallenge));
        Assert.Equal("acct-42", exchange.AuthAccountId);
    }

    [Fact]
    public void Redeem_IsSingleUse()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");

        Assert.IsType<DevicePairingRedeemResult.Success>(svc.Redeem(init.Code, DeviceChallenge));
        Assert.IsType<DevicePairingRedeemResult.AlreadyRedeemed>(svc.Redeem(init.Code, DeviceChallenge));
    }

    [Fact]
    public void Redeem_ReturnsExpired_AfterTtl()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");

        clock.Advance(TimeSpan.FromSeconds(91));

        Assert.IsType<DevicePairingRedeemResult.Expired>(svc.Redeem(init.Code, DeviceChallenge));
    }

    [Fact]
    public void Redeem_ReturnsNotFound_ForUnknownCode()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);

        Assert.IsType<DevicePairingRedeemResult.NotFound>(svc.Redeem("ZZZZZZ", DeviceChallenge));
    }

    [Fact]
    public void Redeem_IsCaseInsensitive()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");

        Assert.IsType<DevicePairingRedeemResult.Success>(svc.Redeem(init.Code.ToLowerInvariant(), DeviceChallenge));
    }

    [Fact]
    public void Initiate_RejectsEmptyAccountId()
    {
        var svc = new InMemoryDevicePairingCodeService(TimeProvider.System);
        Assert.Throws<ArgumentException>(() => svc.Initiate(""));
    }

    [Fact]
    public void Redeem_RejectsWeakDeviceChallenge()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");

        Assert.IsType<DevicePairingRedeemResult.InvalidDeviceChallenge>(svc.Redeem(init.Code, "short"));
    }

    [Fact]
    public void Exchange_IsSingleUse()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");
        var redeem = Assert.IsType<DevicePairingRedeemResult.Success>(svc.Redeem(init.Code, DeviceChallenge));

        Assert.IsType<DevicePairingExchangeResult.Success>(svc.Exchange(redeem.HandoffToken, DeviceChallenge));
        Assert.IsType<DevicePairingExchangeResult.AlreadyConsumed>(svc.Exchange(redeem.HandoffToken, DeviceChallenge));
    }

    [Fact]
    public void Exchange_RequiresMatchingDeviceChallenge()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");
        var redeem = Assert.IsType<DevicePairingRedeemResult.Success>(svc.Redeem(init.Code, DeviceChallenge));

        Assert.IsType<DevicePairingExchangeResult.ChallengeMismatch>(
            svc.Exchange(redeem.HandoffToken, "fedcba9876543210fedcba9876543210"));
    }

    [Fact]
    public void Exchange_ReturnsExpired_AfterHandoffTtl()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");
        var redeem = Assert.IsType<DevicePairingRedeemResult.Success>(svc.Redeem(init.Code, DeviceChallenge));

        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.IsType<DevicePairingExchangeResult.Expired>(svc.Exchange(redeem.HandoffToken, DeviceChallenge));
    }

    /// <summary>Minimal deterministic TimeProvider for tests — avoids adding Microsoft.Extensions.TimeProvider.Testing.</summary>
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;
        public TestClock(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
