using System.Text.Json;
using Fleet.Core.Domain;
using Fleet.Core.Policy;
using Fleet.Core.Validation;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Operations;

namespace Fleet.Manager.Tests.ConsoleViews;

/// <summary>The pure helpers of the owner console: display formatting, parsing of what helpers report, node references and the policy form.</summary>
public sealed class FmtAndParsersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    // ---- Fmt ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min")]
    [InlineData(3599, "59 min")]
    [InlineData(3600, "1 h")]
    [InlineData(172799, "47 h")]
    [InlineData(172800, "2 d")]
    public void An_age_uses_the_largest_whole_unit(int seconds, string expected) =>
        Assert.Equal(expected, Fmt.Age(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Times_in_the_past_and_future_read_naturally_and_a_clock_skew_never_shows_a_negative_age()
    {
        Assert.Equal("never", Fmt.Ago(null, Now));
        Assert.Equal("5 min ago", Fmt.Ago(Now.AddMinutes(-5), Now));
        Assert.Equal("0 s ago", Fmt.Ago(Now.AddSeconds(30), Now));
        Assert.Equal("—", Fmt.Until(null, Now));
        Assert.Equal("expired", Fmt.Until(Now.AddSeconds(-1), Now));
        Assert.Equal("in 41 min", Fmt.Until(Now.AddMinutes(41), Now));
        Assert.Equal("2026-10-05 12:00:00Z", Fmt.Timestamp(Now));
        Assert.Equal("—", Fmt.Timestamp(null));
    }

    [Fact]
    public void Numbers_are_shown_in_the_unit_a_person_reads()
    {
        Assert.Equal("73%", Fmt.Percent(0.734));
        Assert.Equal("100%", Fmt.Percent(1.0));
        Assert.Equal("—", Fmt.Percent(null));
        Assert.Equal("41%", Fmt.PercentPoints(41.2));
        Assert.Equal("512 MiB", Fmt.MiB(512));
        Assert.Equal("7.7 GiB", Fmt.MiB(7900));
        Assert.Equal("—", Fmt.MiB(null));
        Assert.Equal("40 GiB", Fmt.GiB(40));
        Assert.Equal("3 vCPU", Fmt.Cores(3000));
        Assert.Equal("1.5 vCPU", Fmt.Cores(1500));
        Assert.Equal("sha256:aaaaaaaaaaaa…", Fmt.ShortDigest("sha256:" + new string('a', 64)));
        Assert.Equal("—", Fmt.ShortDigest(null));
    }

    [Fact]
    public void Text_from_a_helper_is_decoded_once_stripped_redacted_and_capped_and_stays_plain_text_for_the_encoder()
    {
        // The manager stores helper text HTML-encoded; Razor encodes again on output, so it is decoded here (never "&amp;lt;" on screen).
        Assert.Equal("<script>alert(1)</script>", Fmt.Untrusted("&lt;script&gt;alert(1)&lt;/script&gt;"));
        Assert.Equal("red", Fmt.Untrusted("\u001b[31mred\u001b[0m"));
        Assert.Equal("a b", Fmt.Untrusted("a\u0007b"));
        Assert.Equal("token [redacted]", Fmt.Untrusted("token ghp_" + new string('A', 24)));
        Assert.Equal(new string('x', 50) + "…", Fmt.Untrusted(new string('x', 300), 50));
        Assert.Equal(string.Empty, Fmt.Untrusted(null));
        Assert.Equal("—", Fmt.OrDash("  "));
    }

    [Theory]
    [InlineData("Active", "ok")]
    [InlineData("Online", "ok")]
    [InlineData("Draining", "warn")]
    [InlineData("HostKeyPending", "warn")]
    [InlineData("Failed", "bad")]
    [InlineData("Offline", "bad")]
    [InlineData("host_key_changed", "bad")]
    [InlineData("Disabled", "muted")]
    [InlineData("Cancelled", "muted")]
    [InlineData("something new", "muted")]
    [InlineData("info", "info")]
    public void A_badge_class_follows_the_state(string state, string expected) => Assert.Equal(expected, Fmt.BadgeClass(state));

    [Fact]
    public void Every_failure_reason_has_its_own_plain_words_and_only_an_unknown_one_gets_the_generic_text()
    {
        var generic = Fmt.FailureGuidance("a reason nobody defined");
        Assert.NotEmpty(generic);
        foreach (var reason in FailureReasons.All.Where(r => r != FailureReasons.InternalError))
        {
            var text = Fmt.FailureGuidance(reason);
            Assert.NotEmpty(text);
            Assert.NotEqual(generic, text);
        }

        Assert.Equal(string.Empty, Fmt.FailureGuidance(null));
    }

    [Fact]
    public void Every_step_of_every_plan_has_a_label_and_a_rollout_step_names_its_helper()
    {
        var names = StepPlans.Enroll()
            .Concat(StepPlans.Repair())
            .Concat(StepPlans.Drain())
            .Concat(StepPlans.Disable())
            .Concat(StepPlans.Enable())
            .Concat(StepPlans.Remove())
            .Concat(StepPlans.RotateToken())
            .Concat(StepPlans.Rollout(new[] { "helper-eu-01" }))
            .Distinct()
            .ToList();
        Assert.True(names.Count > 25);

        foreach (var name in names)
        {
            var label = Fmt.StepLabel(name);
            Assert.NotEmpty(label);
            Assert.NotEqual(name, label);
        }

        Assert.Equal("Pull the new image (helper-eu-01)", Fmt.StepLabel("roll-image:helper-eu-01"));
        Assert.Equal("—", Fmt.StepLabel(null));
    }

    [Fact]
    public void States_and_kinds_have_words_and_an_unknown_one_is_shown_safely_instead_of_failing()
    {
        Assert.Equal("Waiting for your host-key check", Fmt.StateLabel("HostKeyPending"));
        Assert.Equal("Done", Fmt.StateLabel("Succeeded"));
        Assert.Equal("Rotating", Fmt.StateLabel("Rotating"));
        Assert.Equal("Image rollout", Fmt.KindLabel("rollout"));
        Assert.Equal("Token rotation", Fmt.KindLabel("rotate-token"));
        Assert.Equal("a b", Fmt.KindLabel("a\u0007b"));
        foreach (var kind in Enum.GetValues<OperationKind>())
        {
            Assert.NotEqual(OperationKinds.ToWire(kind), Fmt.KindLabel(OperationKinds.ToWire(kind)));
        }
    }

    // ---- what a helper reports ------------------------------------------------------------------

    private const string StatusJson =
        "{\"status\":\"Active\",\"health\":\"Online\",\"lastHeartbeatAt\":\"2026-10-05T12:00:00.0000000+00:00\",\"leases\":1,\"effectiveConcurrency\":2,"
        + "\"agentVersion\":\"1.0.0\",\"agentDigest\":\"sha256:aa\",\"integrityStrikes\":2,"
        + "\"host\":{\"polledAt\":\"2026-10-05T11:59:00.0000000+00:00\",\"status\":{\"schema\":\"oet-fleet-ctl.status/1\","
        + "\"host\":{\"os\":\"Ubuntu 24.04\",\"arch\":\"x86_64\",\"cpuCores\":4,\"memTotalMiB\":7900,\"memAvailableMiB\":6100,\"diskFreeGiB\":40,\"uptimeSeconds\":86400,\"swapMiB\":0,\"timeSyncOk\":true},"
        + "\"docker\":{\"version\":\"27.3.1\",\"running\":true},"
        + "\"agent\":{\"present\":true,\"state\":\"running\",\"imageDigest\":\"sha256:bb\",\"startedAt\":\"2026-10-05T10:00:00.0000000+00:00\",\"restartCount\":3,\"oomKilled\":false},"
        + "\"ctl\":{\"version\":1}},\"latencyMs\":87}}";

    [Fact]
    public void A_status_report_is_read_field_by_field_and_a_missing_one_is_simply_null()
    {
        var report = HostStatusParser.Parse(StatusJson)!;

        Assert.Equal("Active", report.NodeStatus);
        Assert.Equal("Online", report.NodeHealth);
        Assert.Equal("1.0.0", report.AgentVersion);
        Assert.Equal(2, report.IntegrityStrikes);
        Assert.Equal(87, report.CtlLatencyMs);
        Assert.True(report.CtlReported);
        Assert.Null(report.CtlFailure);
        Assert.Equal("Ubuntu 24.04", report.Os);
        Assert.Equal(4, report.CpuCores);
        Assert.Equal(7900, report.MemTotalMiB);
        Assert.Equal(6100, report.MemAvailableMiB);
        Assert.Equal(40, report.DiskFreeGiB);
        Assert.Equal(86400L, report.UptimeSeconds);
        Assert.True(report.TimeSyncOk);
        Assert.Equal("27.3.1", report.DockerVersion);
        Assert.True(report.DockerRunning);
        Assert.True(report.AgentPresent);
        Assert.Equal("running", report.AgentState);
        Assert.Equal(3, report.AgentRestartCount);
        Assert.False(report.AgentOomKilled);
        Assert.Equal(1, report.CtlVersion);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 11, 59, 0, TimeSpan.Zero), report.CtlPolledAt);

        var nodeOnly = HostStatusParser.Parse("{\"status\":\"Draining\",\"health\":\"Stale\"}")!;
        Assert.Equal("Draining", nodeOnly.NodeStatus);
        Assert.Null(nodeOnly.Os);
        Assert.False(nodeOnly.CtlReported);
    }

    [Fact]
    public void A_failed_status_call_is_reported_as_such_and_unreadable_json_is_not_a_crash()
    {
        var failed = HostStatusParser.Parse("{\"status\":\"Active\",\"host\":{\"status\":{\"ok\":false,\"reason\":\"auth_failed\"}}}")!;
        Assert.False(failed.CtlReported);
        Assert.Equal("auth_failed", failed.CtlFailure);

        Assert.Null(HostStatusParser.Parse(null));
        Assert.Null(HostStatusParser.Parse("   "));
        Assert.Null(HostStatusParser.Parse("{\"status\":\"Active\",\"host\":{\"status\":{\"schema\":\"oet-fl"));
        Assert.Null(HostStatusParser.Parse("[1,2,3]"));
    }

    [Fact]
    public void A_hostile_string_in_a_report_is_capped_and_left_as_plain_text_for_the_encoder()
    {
        var json = "{\"host\":{\"status\":{\"schema\":\"x\",\"host\":{\"os\":\"<script>alert(1)</script>" + new string('A', 400) + "\"}}}}";

        var report = HostStatusParser.Parse(json)!;

        Assert.Equal(120, report.Os!.Length);
        Assert.StartsWith("<script>", report.Os);
    }

    // ---- node references -----------------------------------------------------------------------

    [Theory]
    [InlineData("Helper EU 01", "helper-eu-01")]
    [InlineData("  --Ünï--code  ", "n-code")]
    [InlineData("203.0.113.10", "203-0-113-10")]
    [InlineData("2001:db8::1", "2001-db8-1")]
    [InlineData("!!!", "")]
    public void A_slug_is_lower_case_letters_digits_and_single_hyphens(string text, string expected) =>
        Assert.Equal(expected, NodeRefs.Slug(text));

    [Theory]
    [InlineData(null, "203.0.113.10")]
    [InlineData("Helper EU 01", "203.0.113.10")]
    [InlineData("Berlin box", "203.0.113.10")]
    [InlineData("", "2001:db8::1")]
    [InlineData("!!!", "!!!")]
    [InlineData("x", "host.example.org")]
    public void Every_derived_node_reference_is_valid_and_never_doubles_the_helper_prefix(string? name, string address)
    {
        var candidate = NodeRefs.Candidate(name, address);

        Assert.Null(InputValidator.ValidateNodeRef(candidate));
        Assert.DoesNotContain("helper-helper", candidate);
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            Assert.Null(InputValidator.ValidateNodeRef(NodeRefs.WithSuffix(candidate, attempt)));
        }
    }

    [Fact]
    public void A_derived_node_reference_for_an_ordinary_name_and_a_long_one()
    {
        Assert.Equal("helper-eu-01", NodeRefs.Candidate("Helper EU 01", "203.0.113.10"));
        Assert.Equal("helper-203-0-113-10", NodeRefs.Candidate(null, "203.0.113.10"));
        Assert.Equal("helper-berlin-box", NodeRefs.Candidate("Berlin box", "203.0.113.10"));
        Assert.Equal("helper", NodeRefs.Candidate("!!!", "!!!"));
        Assert.True(NodeRefs.Candidate(new string('a', 200), "203.0.113.10").Length <= 7 + NodeRefs.MaxSlug);
        Assert.Equal("helper-eu-01", NodeRefs.WithSuffix("helper-eu-01", 1));
        Assert.Equal("helper-eu-01-2", NodeRefs.WithSuffix("helper-eu-01", 2));
    }

    // ---- the policy form -----------------------------------------------------------------------

    [Fact]
    public void A_policy_survives_a_round_trip_through_the_form_unchanged()
    {
        var policy = PolicyDefaults.Global() with
        {
            AllowedKinds = new[] { "media.audio-extract", "pdf.extract" },
            PerKind = new Dictionary<string, int> { ["pdf.extract"] = 2, ["media.audio-extract"] = 1 },
            AgentImage = new AgentImagePolicy(new[] { "sha256:" + new string('a', 64) }, "sha256:" + new string('a', 64), "1.2.3"),
        };

        var form = PolicyInput.From(policy, new[] { "companion.index-prep" });
        var rebuilt = form.TryBuild(policy, out var issues);

        Assert.Empty(issues);
        Assert.NotNull(rebuilt);
        Assert.Equal(policy.AllowedKinds, rebuilt!.AllowedKinds);
        Assert.Equal(policy.PerKind.OrderBy(p => p.Key).ToList(), rebuilt.PerKind.OrderBy(p => p.Key).ToList());
        Assert.Equal(policy.MaxConcurrency, rebuilt.MaxConcurrency);
        Assert.Equal(policy.Budgets, rebuilt.Budgets);
        Assert.Equal(policy.Pressure, rebuilt.Pressure);
        Assert.Equal(policy.PollSeconds, rebuilt.PollSeconds);
        Assert.Equal(policy.AgentImage, rebuilt.AgentImage);
        Assert.Contains(form.Kinds, row => row.Kind == "companion.index-prep" && !row.Allowed);
        Assert.Empty(PolicyValidator.Validate(rebuilt));
    }

    [Fact]
    public void A_missing_number_is_reported_and_a_kind_that_is_not_ticked_is_left_out()
    {
        var form = PolicyInput.From(PolicyDefaults.Global(), new[] { "companion.index-prep" });
        form.CpuMilli = null;
        form.PollMax = null;

        Assert.Null(form.TryBuild(PolicyDefaults.Global(), out var issues));
        Assert.Contains(issues, issue => issue.Field == "budgets.cpuMilli");
        Assert.Contains(issues, issue => issue.Field == "pollSeconds.max");

        form.CpuMilli = 2000;
        form.PollMax = 30;
        var built = form.TryBuild(PolicyDefaults.Global(), out var none)!;
        Assert.Empty(none);
        Assert.Equal(new[] { "pdf.extract" }, built.AllowedKinds);
        Assert.False(built.PerKind.ContainsKey("companion.index-prep"));
    }

    [Fact]
    public void A_blank_cap_means_the_maximum_a_repeated_or_blank_kind_row_is_ignored_and_the_list_is_bounded()
    {
        var form = new PolicyInput
        {
            MaxConcurrency = 3,
            CpuMilli = 3000,
            MemMiB = 4096,
            TmpMiB = 1024,
            ReduceCpuPct = 80,
            ReduceMemFreePct = 20,
            RestoreCpuPct = 60,
            RestoreMemFreePct = 25,
            RestoreAfterSeconds = 120,
            PollIdle = 10,
            PollMin = 5,
            PollMax = 30,
            Kinds = new List<KindInput>
            {
                new() { Kind = "pdf.extract", Allowed = true, Max = null },
                new() { Kind = "pdf.extract", Allowed = true, Max = 1 },
                new() { Kind = " ", Allowed = true, Max = 1 },
                new() { Kind = null, Allowed = true, Max = 1 },
            },
        };
        for (var i = 0; i < 100; i++)
        {
            form.Kinds.Add(new KindInput { Kind = "pdf.k" + i, Allowed = true, Max = 1 });
        }

        var built = form.TryBuild(PolicyDefaults.Global(), out _)!;

        Assert.Equal(3, built.PerKind["pdf.extract"]);
        Assert.True(built.AllowedKinds.Count <= PolicyInput.MaxKinds);
        Assert.Equal(built.AllowedKinds.Count, built.AllowedKinds.Distinct().Count());
    }

    // ---- the API's job statistics -----------------------------------------------------------

    [Fact]
    public void Job_statistics_are_parsed_leniently_and_a_completion_rate_counts_only_jobs_that_ended()
    {
        using var document = JsonDocument.Parse(
            "{\"queue\":{\"pdf.extract\":{\"Queued\":4,\"Leased\":1,\"Succeeded\":9,\"Failed\":1,\"Quarantined\":0,\"FallbackLocal\":2,\"Cancelled\":3},\"media.audio-extract\":{\"Queued\":0}},"
            + "\"oldestQueuedAgeSeconds\":125,\"leasedCount\":1,\"leasedWeightByNode\":{\"rw_a\":2,\"rw_b\":\"x\"}}");

        var stats = DashboardService.ParseStats(document.RootElement);

        Assert.Equal(125L, stats.OldestQueuedAgeSeconds);
        Assert.Equal(1, stats.LeasedCount);
        Assert.Equal(2, stats.LeasedWeightByNode["rw_a"]);
        Assert.False(stats.LeasedWeightByNode.ContainsKey("rw_b"));
        var pdf = stats.Kinds.Single(k => k.Kind == "pdf.extract");
        Assert.Equal(4, pdf.Queued);
        Assert.Equal(1, pdf.Leased);
        Assert.Equal(0.9, pdf.CompletionRate!.Value, 3);
        Assert.Null(stats.Kinds.Single(k => k.Kind == "media.audio-extract").CompletionRate);

        Assert.Empty(DashboardService.ParseStats(JsonDocument.Parse("{\"queued\":0}").RootElement).Kinds);
        Assert.Empty(DashboardService.ParseStats(JsonDocument.Parse("[]").RootElement).Kinds);
        Assert.Null(DashboardService.ParseStats(JsonDocument.Parse("{\"oldestQueuedAgeSeconds\":null}").RootElement).OldestQueuedAgeSeconds);
    }

    [Theory]
    [InlineData("pdf.extract", "OET · Content papers")]
    [InlineData("companion.index-prep", "OET · AI companion")]
    [InlineData("media.speaking-join", "OET · Media (live classes, speaking)")]
    [InlineData("something.else", "OET")]
    public void A_job_kind_belongs_to_an_area_of_the_platform(string kind, string expected) =>
        Assert.Equal(expected, DashboardService.ProjectOf(kind));
}
