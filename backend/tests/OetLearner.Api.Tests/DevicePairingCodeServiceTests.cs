using System;
using OetLearner.Api.Services.DevicePairing;
using Xunit;

namespace OetLearner.Api.Tests;

public class DevicePairingCodeServiceTests
{
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

        var result = svc.Redeem(init.Code);

        var success = Assert.IsType<DevicePairingRedeemResult.Success>(result);
        Assert.Equal("acct-42", success.AuthAccountId);
    }

    [Fact]
    public void Redeem_IsSingleUse()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");

        Assert.IsType<DevicePairingRedeemResult.Success>(svc.Redeem(init.Code));
        Assert.IsType<DevicePairingRedeemResult.AlreadyRedeemed>(svc.Redeem(init.Code));
    }

    [Fact]
    public void Redeem_ReturnsExpired_AfterTtl()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");

        clock.Advance(TimeSpan.FromSeconds(91));

        Assert.IsType<DevicePairingRedeemResult.Expired>(svc.Redeem(init.Code));
    }

    [Fact]
    public void Redeem_ReturnsNotFound_ForUnknownCode()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);

        Assert.IsType<DevicePairingRedeemResult.NotFound>(svc.Redeem("ZZZZZZ"));
    }

    [Fact]
    public void Redeem_IsCaseInsensitive()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        var svc = new InMemoryDevicePairingCodeService(clock);
        var init = svc.Initiate("acct-1");

        Assert.IsType<DevicePairingRedeemResult.Success>(svc.Redeem(init.Code.ToLowerInvariant()));
    }

    [Fact]
    public void Initiate_RejectsEmptyAccountId()
    {
        var svc = new InMemoryDevicePairingCodeService(TimeProvider.System);
        Assert.Throws<ArgumentException>(() => svc.Initiate(""));
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
