using Fleet.Core.Placement;
using Fleet.Core.Policy;
using Fleet.Core.Validation;

namespace Fleet.Manager.Tests.Placement;

/// <summary>Policy ranges of OET-RWP/1 section 7.3 and the pressure hysteresis of section 5.4 (RW-124).</summary>
public sealed class PolicyAndPressureTests
{
    private static DateTimeOffset At(int seconds) => new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

    [Fact]
    public void The_global_default_is_the_four_vcpu_eight_gib_policy_and_is_valid()
    {
        var policy = PolicyDefaults.Global();
        Assert.Equal(new[] { "pdf.extract" }, policy.AllowedKinds);
        Assert.Equal(2, policy.MaxConcurrency);
        Assert.Equal(new Budgets(3000, 5120, 3072), policy.Budgets);
        Assert.Equal(new PressureSettings(80, 20, 60, 25, 120), policy.Pressure);
        Assert.Equal(new PollSettings(10, 5, 30), policy.PollSeconds);
        Assert.Empty(PolicyValidator.Validate(policy));
    }

    [Fact]
    public void A_policy_scales_to_the_facts_of_the_host()
    {
        var big = PolicyDefaults.ForHost(4, 7900);
        Assert.Equal(new Budgets(3000, 5120, 3072), big.Budgets);
        Assert.Equal(2, big.MaxConcurrency);

        var small = PolicyDefaults.ForHost(2, 3800);
        Assert.Equal(1, small.MaxConcurrency);
        Assert.Equal(1000, small.Budgets.CpuMilli);
        Assert.True(small.Budgets.MemMiB <= 3800);
        Assert.True(small.Budgets.TmpMiB <= small.Budgets.MemMiB);
        Assert.Empty(PolicyValidator.Validate(small));

        var huge = PolicyDefaults.ForHost(96, 400_000);
        Assert.Equal(8, huge.MaxConcurrency);
        Assert.True(huge.Budgets.CpuMilli <= 64000);
        Assert.Empty(PolicyValidator.Validate(huge));
    }

    [Fact]
    public void The_host_override_wins_over_the_global_policy()
    {
        var global = PolicyDefaults.Global();
        var host = global with { MaxConcurrency = 1, PerKind = new Dictionary<string, int> { ["pdf.extract"] = 1 } };
        Assert.Same(host, PolicyResolver.Effective(global, host));
        Assert.Same(global, PolicyResolver.Effective(global, null));
    }

    public static IEnumerable<object[]> InvalidPolicies()
    {
        var ok = PolicyDefaults.Global();
        yield return new object[] { "maxConcurrency above 8", ok with { MaxConcurrency = 9, PerKind = new Dictionary<string, int> { ["pdf.extract"] = 2 } } };
        yield return new object[] { "negative maxConcurrency", ok with { MaxConcurrency = -1 } };
        yield return new object[] { "perKind above maxConcurrency", ok with { PerKind = new Dictionary<string, int> { ["pdf.extract"] = 3 } } };
        yield return new object[] { "perKind for a kind that is not allowed", ok with { PerKind = new Dictionary<string, int> { ["media.audio-extract"] = 1 } } };
        yield return new object[] { "duplicate kinds", ok with { AllowedKinds = new[] { "pdf.extract", "pdf.extract" } } };
        yield return new object[] { "malformed kind", ok with { AllowedKinds = new[] { "PDF EXTRACT" }, PerKind = new Dictionary<string, int>() } };
        yield return new object[] { "tmp above mem", ok with { Budgets = new Budgets(3000, 1024, 2048) } };
        yield return new object[] { "cpu above 64000", ok with { Budgets = new Budgets(64001, 5120, 3072) } };
        yield return new object[] { "mem above 262144", ok with { Budgets = new Budgets(3000, 262145, 3072) } };
        yield return new object[] { "restore cpu not below reduce cpu", ok with { Pressure = new PressureSettings(80, 20, 80, 25, 120) } };
        yield return new object[] { "restore mem not above reduce mem", ok with { Pressure = new PressureSettings(80, 20, 60, 20, 120) } };
        yield return new object[] { "percent out of range", ok with { Pressure = new PressureSettings(101, 20, 60, 25, 120) } };
        yield return new object[] { "restoreAfterSeconds below 30", ok with { Pressure = new PressureSettings(80, 20, 60, 25, 29) } };
        yield return new object[] { "poll min below 2", ok with { PollSeconds = new PollSettings(10, 1, 30) } };
        yield return new object[] { "poll idle below min", ok with { PollSeconds = new PollSettings(4, 5, 30) } };
        yield return new object[] { "poll max below idle", ok with { PollSeconds = new PollSettings(10, 5, 9) } };
        yield return new object[] { "digest malformed", ok with { AgentImage = new AgentImagePolicy(new[] { "sha256:abc" }, null, "1.0.0") } };
        yield return new object[] { "target not approved", ok with { AgentImage = new AgentImagePolicy(new[] { "sha256:" + new string('a', 64) }, "sha256:" + new string('b', 64), "1.0.0") } };
        yield return new object[] { "too many digests", ok with { AgentImage = new AgentImagePolicy(Enumerable.Range(0, 9).Select(i => "sha256:" + new string((char)('a' + i), 64)).ToArray(), null, "1.0.0") } };
        yield return new object[] { "min version malformed", ok with { AgentImage = new AgentImagePolicy(Array.Empty<string>(), null, "v1") } };
    }

    [Theory]
    [MemberData(nameof(InvalidPolicies))]
    public void Out_of_range_policies_are_rejected(string why, NodePolicy policy)
    {
        Assert.NotEmpty(PolicyValidator.Validate(policy));
        Assert.Throws<FleetValidationException>(() => PolicyValidator.EnsureValid(policy));
        _ = why;
    }

    [Fact]
    public void The_kind_registry_of_the_api_limits_allowed_kinds()
    {
        var policy = PolicyDefaults.Global();
        Assert.Empty(PolicyValidator.Validate(policy, new[] { "pdf.extract", "media.audio-extract" }));
        Assert.NotEmpty(PolicyValidator.Validate(policy, new[] { "media.audio-extract" }));
    }

    [Fact]
    public void Pressure_is_reduced_after_fifteen_seconds_above_the_threshold()
    {
        var governor = new PressureGovernor(PolicyDefaults.DefaultPressure, 2);

        Assert.Equal(PressureAction.None, governor.Observe(At(0), 85, 50));
        Assert.Equal(PressureAction.None, governor.Observe(At(5), 90, 50));
        Assert.Equal(PressureAction.None, governor.Observe(At(10), 90, 50));
        Assert.Equal(2, governor.EffectiveConcurrency);

        Assert.Equal(PressureAction.Reduced, governor.Observe(At(15), 90, 50));
        Assert.Equal(1, governor.EffectiveConcurrency);
        Assert.Equal("reduced", governor.Pressure);

        // Still breaching: one more step only after another 15 seconds.
        Assert.Equal(PressureAction.None, governor.Observe(At(20), 90, 50));
        Assert.Equal(PressureAction.Reduced, governor.Observe(At(30), 90, 50));
        Assert.Equal(0, governor.EffectiveConcurrency);
        Assert.Equal(PressureAction.None, governor.Observe(At(60), 90, 50));
        Assert.Equal(0, governor.EffectiveConcurrency);
    }

    [Fact]
    public void Low_free_memory_also_reduces_and_a_short_spike_does_not()
    {
        var governor = new PressureGovernor(PolicyDefaults.DefaultPressure, 2);
        Assert.Equal(PressureAction.None, governor.Observe(At(0), 10, 15));
        Assert.Equal(PressureAction.Reduced, governor.Observe(At(15), 10, 15));

        var spike = new PressureGovernor(PolicyDefaults.DefaultPressure, 2);
        spike.Observe(At(0), 95, 50);
        spike.Observe(At(5), 95, 50);
        spike.Observe(At(10), 30, 50); // back to calm before 15 s
        Assert.Equal(PressureAction.None, spike.Observe(At(15), 95, 50));
        Assert.Equal(2, spike.EffectiveConcurrency);
    }

    [Fact]
    public void Capacity_is_restored_only_after_a_continuous_calm_of_two_minutes_one_step_at_a_time()
    {
        var governor = new PressureGovernor(PolicyDefaults.DefaultPressure, 2);
        governor.Observe(At(0), 95, 50);
        governor.Observe(At(15), 95, 50);
        governor.Observe(At(30), 95, 50);
        Assert.Equal(0, governor.EffectiveConcurrency);

        // Calm begins at t=100. 119 seconds later nothing has been restored.
        Assert.Equal(PressureAction.None, governor.Observe(At(100), 30, 60));
        Assert.Equal(PressureAction.None, governor.Observe(At(219), 30, 60));
        Assert.Equal(0, governor.EffectiveConcurrency);

        Assert.Equal(PressureAction.Restored, governor.Observe(At(220), 30, 60));
        Assert.Equal(1, governor.EffectiveConcurrency);

        // The next step needs another full two minutes.
        Assert.Equal(PressureAction.None, governor.Observe(At(300), 30, 60));
        Assert.Equal(PressureAction.Restored, governor.Observe(At(340), 30, 60));
        Assert.Equal(2, governor.EffectiveConcurrency);
        Assert.Equal("normal", governor.Pressure);
        Assert.Equal(PressureAction.None, governor.Observe(At(1000), 30, 60));
    }

    [Fact]
    public void The_dead_band_between_the_thresholds_neither_reduces_nor_counts_towards_a_restore()
    {
        var governor = new PressureGovernor(PolicyDefaults.DefaultPressure, 2);
        governor.Observe(At(0), 95, 50);
        governor.Observe(At(15), 95, 50);
        Assert.Equal(1, governor.EffectiveConcurrency);

        // 70% CPU is below the reduce threshold (80) but above the restore threshold (60): hysteresis.
        governor.Observe(At(20), 70, 50);
        Assert.Equal(PressureAction.None, governor.Observe(At(200), 70, 50));
        Assert.Equal(1, governor.EffectiveConcurrency);

        // A dip into the dead band resets a calm that was in progress.
        governor.Observe(At(300), 30, 60);
        governor.Observe(At(400), 70, 60);
        Assert.Equal(PressureAction.None, governor.Observe(At(430), 30, 60));
        Assert.Equal(PressureAction.None, governor.Observe(At(540), 30, 60));
        Assert.Equal(PressureAction.Restored, governor.Observe(At(550), 30, 60));
    }

    [Fact]
    public void Free_memory_below_ten_percent_asks_to_shed_the_youngest_job_no_more_than_once_per_fifteen_seconds()
    {
        var governor = new PressureGovernor(PolicyDefaults.DefaultPressure, 2);
        Assert.Equal(PressureAction.Shed, governor.Observe(At(0), 50, 8));
        Assert.Equal(PressureAction.None, governor.Observe(At(5), 50, 8));

        // The breach has now lasted fifteen seconds, so the SAME sample also lowers the effective concurrency: both are reported.
        var combined = governor.Observe(At(15), 50, 8);
        Assert.Equal(PressureAction.Reduced | PressureAction.Shed, combined);
        Assert.True(combined.HasFlag(PressureAction.Shed));
        Assert.True(combined.HasFlag(PressureAction.Reduced));
        Assert.Equal(1, governor.EffectiveConcurrency);
    }
}
