using System.Text.Json;
using Fleet.Core.Crypto;
using Fleet.Core.Domain;
using Fleet.Core.Ssh;
using Fleet.Core.Validation;
using Fleet.Manager.Configuration;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Tests.Provisioning;

/// <summary>
/// The production provisioner with the child processes recorded instead of started (OET-RWP/1 sections 7.4, 8.2, 8.9): no shell,
/// values as JSON data, strict host keys, secrets only in 0600 files that are removed, runs serialised and bounded.
/// </summary>
public sealed class AnsibleProvisionerTests : IDisposable
{
    private const string Address = "203.0.113.10";
    private const string OwnerKeyBody = "OWNER-PRIVATE-KEY-BODY-MARKER";
    private const string ManagerKeyBody = "MANAGER-PRIVATE-KEY-BODY-MARKER";
    private const string AnsibleDirectory = "/opt/fleet/ansible";

    private readonly string _scratch = Directory.CreateTempSubdirectory("fleet-ansible-tests-").FullName;
    private readonly RecordingProcessRunner _runner = new();
    private readonly ManualTimeProvider _time = new();
    private readonly string _knownHosts = HostKeys.KnownHostsLine(Address, 22, "ssh-ed25519", FakeKeys.HostKeyBlob("target"));

    private string? _capturedVars;
    private string? _capturedKnownHosts;
    private string? _capturedKey;
    private string? _capturedManagerKey;
    private bool _keyExistedDuringRun;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private AnsibleProvisioner Create(Action<ProvisioningOptions>? configure = null)
    {
        var options = new FleetOptions();
        options.Provisioning.ScratchDirectory = _scratch;
        options.Provisioning.AnsibleDirectory = AnsibleDirectory;
        configure?.Invoke(options.Provisioning);
        return new AnsibleProvisioner(_runner, Options.Create(options), _time, NullLogger<AnsibleProvisioner>.Instance);
    }

    private ProvisionRequest Request(
        EnrollStep step,
        IReadOnlyDictionary<string, object?>? data = null,
        bool owner = true,
        bool manager = true,
        string ownerUser = "root",
        bool pinned = true) =>
        new(
            step,
            new HostTarget(Address, 22, pinned ? _knownHosts : null),
            owner ? ownerUser : null,
            owner ? SecretBuffer.FromUtf8(OwnerKeyBody) : null,
            manager ? SecretBuffer.FromUtf8(ManagerKeyBody) : null,
            "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIManagerPublicKeyForTests oet-fleet-manager",
            data ?? new Dictionary<string, object?>());

    private static string ArgAfter(ProcessSpec spec, string flag)
    {
        var index = spec.Arguments.ToList().IndexOf(flag);
        Assert.True(index >= 0, "argument " + flag + " is missing");
        return spec.Arguments[index + 1];
    }

    /// <summary>Plays ansible-playbook: reads what the manager handed it, writes the result file the playbook would write.</summary>
    private ProcessResult Playbook(ProcessSpec spec, string? resultJson, int exitCode = 0, string stdout = "", string stderr = "")
    {
        var varsPath = ArgAfter(spec, "-e")[1..];
        _capturedVars = File.ReadAllText(varsPath);
        using var vars = JsonDocument.Parse(_capturedVars);
        var root = vars.RootElement;
        var keyPath = root.GetProperty("fleet_key_file").GetString()!;
        _keyExistedDuringRun = File.Exists(keyPath);
        _capturedKey = File.ReadAllText(keyPath);
        _capturedKnownHosts = File.ReadAllText(root.GetProperty("fleet_known_hosts_file").GetString()!);
        if (root.GetProperty("fleet_manager_key_file").ValueKind == JsonValueKind.String)
        {
            _capturedManagerKey = File.ReadAllText(root.GetProperty("fleet_manager_key_file").GetString()!);
        }

        if (resultJson is not null)
        {
            File.WriteAllText(root.GetProperty("fleet_result_file").GetString()!, resultJson);
        }

        return new ProcessResult(exitCode, stdout, stderr, false);
    }

    private static string Recap(int changed, int failed = 0, int unreachable = 0) =>
        "PLAY RECAP\n203.0.113.10 : ok=3 changed=" + changed + " unreachable=" + unreachable + " failed=" + failed + " skipped=0\n";

    // ---- apply -----------------------------------------------------------------------------

    [Fact]
    public async Task An_apply_run_hands_everything_over_as_data_and_nothing_through_argv_env_or_a_shell()
    {
        _runner.Handler = spec => Playbook(
            spec,
            "{\"ok\":true,\"summary\":\"firewall applied\",\"facts\":{\"ports\":3,\"label\":\"x\",\"flag\":true,\"ratio\":1.5,\"nothing\":null}}",
            stdout: Recap(1));
        var data = new Dictionary<string, object?>
        {
            ["extra_value"] = "ok",
            ["fleet_target_address"] = "evil.example",
            ["fleet_evil"] = "x",
            ["BadKey"] = "x",
            ["list_value"] = new[] { 1, 2 },
        };

        var result = await Create().ApplyAsync(Request(EnrollStep.Firewall, data), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Satisfied);
        Assert.Equal("firewall applied", result.Summary);
        Assert.Equal(3L, result.Facts["ports"]);
        Assert.Equal("x", result.Facts["label"]);
        Assert.Equal(true, result.Facts["flag"]);
        Assert.Equal(1.5, result.Facts["ratio"]);
        Assert.Null(result.Facts["nothing"]);

        var spec = Assert.Single(_runner.Specs);
        Assert.Equal("ansible-playbook", spec.FileName);
        Assert.Equal(AnsibleDirectory, spec.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(900), spec.Timeout);
        Assert.Null(spec.StdinText);
        Assert.Equal(new[] { "-i", Path.Combine(AnsibleDirectory, "inventory", "target.yml") }, spec.Arguments.Take(2).ToArray());
        Assert.Equal(Path.Combine(AnsibleDirectory, "playbooks", "firewall.yml"), spec.Arguments[2]);
        Assert.StartsWith("@", ArgAfter(spec, "-e"));
        Assert.EndsWith("vars.json", ArgAfter(spec, "-e"));
        Assert.Equal("2", ArgAfter(spec, "--forks"));
        Assert.Equal("30", ArgAfter(spec, "--timeout"));
        Assert.DoesNotContain("--check", spec.Arguments);

        // Nothing sensitive or host-supplied is on the command line or in the environment.
        var commandLine = string.Join(' ', spec.Arguments);
        Assert.DoesNotContain(Address, commandLine);
        Assert.DoesNotContain(OwnerKeyBody, commandLine);
        Assert.DoesNotContain(ManagerKeyBody, commandLine);
        Assert.DoesNotContain("evil.example", commandLine);
        Assert.All(spec.Environment!.Values, value => Assert.DoesNotContain("KEY-BODY-MARKER", value));
        Assert.Equal("True", spec.Environment!["ANSIBLE_HOST_KEY_CHECKING"]);
        Assert.EndsWith("ansible.cfg", spec.Environment["ANSIBLE_CONFIG"]);

        // The vars file: reserved names win, hostile data names and values are only data.
        using var vars = JsonDocument.Parse(_capturedVars!);
        var root = vars.RootElement;
        Assert.Equal(Address, root.GetProperty("fleet_target_address").GetString());
        Assert.Equal(22, root.GetProperty("fleet_target_port").GetInt32());
        Assert.Equal("root", root.GetProperty("fleet_ssh_user").GetString());
        Assert.False(root.GetProperty("fleet_become").GetBoolean());
        Assert.Equal("firewall", root.GetProperty("fleet_step").GetString());
        Assert.False(root.GetProperty("fleet_check_mode").GetBoolean());
        Assert.Equal(AddressPolicy.PrimaryAddress, root.GetProperty("fleet_primary_address").GetString());
        Assert.Equal(FleetCtlVerbs.AgentRepository, root.GetProperty("fleet_agent_repository").GetString());
        Assert.Equal("oetfleet", root.GetProperty("fleet_fleet_user").GetString());
        Assert.Contains("StrictHostKeyChecking=yes", root.GetProperty("fleet_ssh_common_args").GetString());
        Assert.Equal("ok", root.GetProperty("extra_value").GetString());
        Assert.Equal(2, root.GetProperty("list_value").GetArrayLength());
        Assert.False(root.TryGetProperty("BadKey", out _));
        Assert.False(root.TryGetProperty("fleet_evil", out _));

        // Pin and keys: the known_hosts file holds exactly the owner-verified line; the key files existed only during the run.
        Assert.Equal(_knownHosts + "\n", _capturedKnownHosts);
        Assert.True(_keyExistedDuringRun);
        Assert.Equal(OwnerKeyBody, _capturedKey);
        Assert.Equal(ManagerKeyBody, _capturedManagerKey);
        Assert.Empty(Directory.GetFileSystemEntries(_scratch));
    }

    [Fact]
    public async Task A_run_without_an_owner_key_uses_the_manager_key_as_the_fleet_user_with_become()
    {
        _runner.Handler = spec => Playbook(spec, "{\"ok\":true,\"summary\":\"done\"}");

        var result = await Create().ApplyAsync(Request(EnrollStep.HardenSsh, owner: false), CancellationToken.None);

        Assert.True(result.Success);
        using var vars = JsonDocument.Parse(_capturedVars!);
        Assert.Equal("oetfleet", vars.RootElement.GetProperty("fleet_ssh_user").GetString());
        Assert.True(vars.RootElement.GetProperty("fleet_become").GetBoolean());
        Assert.Equal(ManagerKeyBody, _capturedKey);
    }

    [Fact]
    public async Task Forks_are_clamped_between_one_and_five()
    {
        _runner.Handler = spec => Playbook(spec, "{\"ok\":true}");

        await Create(options => options.Forks = 99).ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None);
        await Create(options => options.Forks = 0).ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None);

        Assert.Equal("5", ArgAfter(_runner.Specs[0], "--forks"));
        Assert.Equal("1", ArgAfter(_runner.Specs[1], "--forks"));
    }

    [Fact]
    public async Task Playbook_runs_are_serialised_so_the_load_on_the_primary_is_bounded()
    {
        _runner.AsyncHandler = async spec =>
        {
            await Task.Delay(40);
            return new ProcessResult(0, string.Empty, string.Empty, false);
        };
        var provisioner = Create();

        await Task.WhenAll(
            provisioner.ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None),
            provisioner.ApplyAsync(Request(EnrollStep.Firewall), CancellationToken.None),
            provisioner.ApplyAsync(Request(EnrollStep.HostBaseline), CancellationToken.None));

        Assert.Equal(3, _runner.Specs.Count);
        Assert.Equal(1, _runner.MaxConcurrent);
    }

    [Fact]
    public async Task A_cancelled_call_never_starts_a_process_and_a_crashing_runner_leaves_no_key_behind()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().ApplyAsync(Request(EnrollStep.Docker), cancelled.Token));
        Assert.Empty(_runner.Specs);

        _runner.Handler = _ => throw new InvalidOperationException("the runner blew up");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None));
        Assert.Empty(Directory.GetFileSystemEntries(_scratch));
    }

    // ---- check -----------------------------------------------------------------------------

    [Fact]
    public async Task A_check_run_is_satisfied_only_when_nothing_would_change()
    {
        _runner.Handler = spec => Playbook(spec, null, stdout: Recap(0));
        var satisfied = await Create().CheckAsync(Request(EnrollStep.Firewall), CancellationToken.None);
        Assert.True(satisfied.Success);
        Assert.True(satisfied.Satisfied);
        Assert.Contains("--check", _runner.Specs[0].Arguments);
        using (var vars = JsonDocument.Parse(_capturedVars!))
        {
            Assert.True(vars.RootElement.GetProperty("fleet_check_mode").GetBoolean());
        }

        _runner.Handler = spec => Playbook(spec, null, stdout: Recap(2));
        var pending = await Create().CheckAsync(Request(EnrollStep.Firewall), CancellationToken.None);
        Assert.True(pending.Success);
        Assert.False(pending.Satisfied);

        // A check that cannot even run (a prerequisite is missing) is "not satisfied", never a failure of the enrollment.
        _runner.Handler = spec => Playbook(spec, null, exitCode: 2, stderr: "task failed");
        var broken = await Create().CheckAsync(Request(EnrollStep.Firewall), CancellationToken.None);
        Assert.True(broken.Success);
        Assert.False(broken.Satisfied);
    }

    [Fact]
    public async Task Preflight_has_no_separate_check_and_runs_nothing()
    {
        var result = await Create().CheckAsync(Request(EnrollStep.Preflight), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Satisfied);
        Assert.Empty(_runner.Specs);
    }

    [Fact]
    public async Task The_manager_key_install_and_the_baseline_are_checked_over_the_restricted_login_not_ansible()
    {
        _runner.Handler = _ => new ProcessResult(0, "{\"ok\":true}", string.Empty, false);
        var login = await Create().CheckAsync(Request(EnrollStep.InstallKey, owner: false), CancellationToken.None);
        Assert.True(login.Satisfied);
        Assert.Equal("ssh", _runner.Specs[0].FileName);
        Assert.Equal(new[] { "oet-fleet-ctl", "status" }, _runner.Specs[0].Arguments.Skip(_runner.Specs[0].Arguments.Count - 2).ToArray());

        _runner.Specs.Clear();
        _runner.Handler = _ => new ProcessResult(0, "{\"ok\":true,\"findings\":[]}", string.Empty, false);
        var baseline = await Create().CheckAsync(Request(EnrollStep.HostBaseline, owner: false), CancellationToken.None);
        Assert.True(baseline.Satisfied);
        Assert.Equal("harden-check", _runner.Specs[0].Arguments[^1]);

        _runner.Handler = _ => new ProcessResult(0, "{\"ok\":false,\"findings\":[\"swap is on\"]}", string.Empty, false);
        Assert.False((await Create().CheckAsync(Request(EnrollStep.HostBaseline, owner: false), CancellationToken.None)).Satisfied);

        _runner.Handler = _ => new ProcessResult(1, "{\"ok\":false}", string.Empty, false);
        Assert.False((await Create().CheckAsync(Request(EnrollStep.InstallKey, owner: false), CancellationToken.None)).Satisfied);

        _runner.Specs.Clear();
        var noKey = await Create().CheckAsync(Request(EnrollStep.InstallKey, owner: false, manager: false), CancellationToken.None);
        Assert.False(noKey.Satisfied);
        Assert.Empty(_runner.Specs);
    }

    // ---- interpreting a run ----------------------------------------------------------------

    [Fact]
    public async Task A_result_file_that_says_not_ok_gives_a_stable_reason_and_unknown_reasons_become_a_generic_failure()
    {
        _runner.Handler = spec => Playbook(
            spec,
            "{\"ok\":false,\"reason\":\"preflight_rejected\",\"detail\":\"os_unsupported\",\"summary\":\"host rejected\"}",
            exitCode: 2);
        var rejected = await Create().ApplyAsync(Request(EnrollStep.Preflight), CancellationToken.None);
        Assert.False(rejected.Success);
        Assert.Equal(FailureReasons.PreflightRejected, rejected.FailureReason);
        Assert.Equal("os_unsupported", rejected.Detail);
        Assert.Equal("host rejected", rejected.Summary);

        _runner.Handler = spec => Playbook(spec, "{\"ok\":false,\"reason\":\"made_up_reason\",\"summary\":\"nope\"}", exitCode: 2);
        var unknown = await Create().ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None);
        Assert.Equal(FailureReasons.BootstrapStepFailed, unknown.FailureReason);
        Assert.Equal("docker", unknown.Detail);
    }

    [Fact]
    public async Task A_failed_run_without_a_result_file_is_classified_from_the_ssh_output()
    {
        _runner.Handler = spec => Playbook(spec, null, exitCode: 4, stderr: "@ WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED! @");
        Assert.Equal(FailureReasons.HostKeyChanged, (await Create().ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None)).FailureReason);

        _runner.Handler = spec => Playbook(spec, null, exitCode: 4, stdout: "root@203.0.113.10: Permission denied (publickey).");
        Assert.Equal(FailureReasons.AuthFailed, (await Create().ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None)).FailureReason);

        _runner.Handler = spec => Playbook(spec, null, exitCode: 4, stderr: "Connection timed out");
        Assert.Equal(FailureReasons.SshUnreachable, (await Create().ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None)).FailureReason);

        _runner.Handler = spec => Playbook(spec, null, exitCode: 2, stderr: "some task failed");
        var generic = await Create().ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None);
        Assert.Equal(FailureReasons.BootstrapStepFailed, generic.FailureReason);
        Assert.Contains("exit code 2", generic.Detail);

        _runner.Handler = _ => new ProcessResult(-1, string.Empty, string.Empty, true);
        var timedOut = await Create().ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None);
        Assert.Equal(FailureReasons.BootstrapStepFailed, timedOut.FailureReason);
        Assert.Contains("timed out", timedOut.Detail);
    }

    [Fact]
    public async Task A_run_that_exits_zero_with_an_unreadable_result_file_still_counts_as_done()
    {
        _runner.Handler = spec => Playbook(spec, "this is not json", stdout: Recap(1));

        var result = await Create().ApplyAsync(Request(EnrollStep.Docker), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("applied", result.Summary);
    }

    // ---- refusals before any process starts ------------------------------------------------

    [Fact]
    public async Task Nothing_runs_without_a_pinned_host_key_a_usable_credential_or_a_valid_user()
    {
        var provisioner = Create();

        var unpinned = await provisioner.ApplyAsync(Request(EnrollStep.Docker, pinned: false), CancellationToken.None);
        Assert.Equal(FailureReasons.HostKeyMismatch, unpinned.FailureReason);

        var noCredential = await provisioner.ApplyAsync(Request(EnrollStep.Docker, owner: false, manager: false), CancellationToken.None);
        Assert.Equal(FailureReasons.OwnerCredentialExpired, noCredential.FailureReason);

        var hostileUser = await provisioner.ApplyAsync(Request(EnrollStep.Docker, ownerUser: "root;id"), CancellationToken.None);
        Assert.Equal(FailureReasons.OwnerCredentialExpired, hostileUser.FailureReason);

        var noPlaybook = await provisioner.ApplyAsync(Request(EnrollStep.Image), CancellationToken.None);
        Assert.Equal(FailureReasons.InternalError, noPlaybook.FailureReason);

        Assert.Empty(_runner.Specs);
    }

    // ---- restricted ctl over ssh -----------------------------------------------------------

    [Fact]
    public async Task A_ctl_call_is_one_strict_ssh_command_with_a_pinned_known_hosts_and_a_temporary_key()
    {
        _runner.Handler = spec =>
        {
            var knownHostsOption = spec.Arguments.First(a => a.StartsWith("UserKnownHostsFile=", StringComparison.Ordinal));
            _capturedKnownHosts = File.ReadAllText(knownHostsOption["UserKnownHostsFile=".Length..]);
            var keyPath = ArgAfter(spec, "-i");
            _keyExistedDuringRun = File.Exists(keyPath);
            _capturedKey = File.ReadAllText(keyPath);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keyPath));
            }

            return new ProcessResult(0, "{\"ok\":true}", string.Empty, false);
        };
        using var key = SecretBuffer.FromUtf8(ManagerKeyBody);

        var result = await Create().RunCtlAsync(
            new CtlRequest(new HostTarget(Address, 22, _knownHosts), key, "verify", new[] { "sha256:" + new string('a', 64) }, null),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("{\"ok\":true}", result.Stdout);
        var spec = Assert.Single(_runner.Specs);
        Assert.Equal("ssh", spec.FileName);
        Assert.Equal(TimeSpan.FromSeconds(90), spec.Timeout);
        var knownHostsPath = spec.Arguments.First(a => a.StartsWith("UserKnownHostsFile=", StringComparison.Ordinal))["UserKnownHostsFile=".Length..];
        Assert.Equal(
            SshOptions.BuildArguments(knownHostsPath, ArgAfter(spec, "-i"), Address, 22, SshOptions.FleetUser, FleetCtlVerbs.Build("verify", "sha256:" + new string('a', 64))).ToArray(),
            spec.Arguments.ToArray());
        Assert.Contains("StrictHostKeyChecking=yes", spec.Arguments);
        Assert.Equal(_knownHosts + "\n", _capturedKnownHosts);
        Assert.True(_keyExistedDuringRun);
        Assert.Equal(ManagerKeyBody, _capturedKey);
        Assert.Empty(Directory.GetFileSystemEntries(_scratch));
    }

    [Fact]
    public async Task Secrets_travel_on_stdin_and_never_in_argv()
    {
        const string Registry = "ci-user\nghs_REGISTRYTOKEN0123456789abcdef\n";
        _runner.Handler = _ => new ProcessResult(0, "{\"ok\":true}", string.Empty, false);
        using var key = SecretBuffer.FromUtf8(ManagerKeyBody);

        var result = await Create().RunCtlAsync(
            new CtlRequest(new HostTarget(Address, 22, _knownHosts), key, "login", new[] { "--registry", "ghcr.io" }, Registry),
            CancellationToken.None);

        Assert.True(result.Success);
        var spec = Assert.Single(_runner.Specs);
        Assert.Equal(Registry, spec.StdinText);
        Assert.DoesNotContain("REGISTRYTOKEN", string.Join(' ', spec.Arguments));
        Assert.DoesNotContain("ci-user", string.Join(' ', spec.Arguments));
    }

    [Fact]
    public async Task A_pull_gets_the_long_timeout_and_everything_else_the_short_one()
    {
        _runner.Handler = _ => new ProcessResult(0, "{\"ok\":true}", string.Empty, false);
        using var key = SecretBuffer.FromUtf8(ManagerKeyBody);
        var target = new HostTarget(Address, 22, _knownHosts);
        var digest = "sha256:" + new string('a', 64);

        await Create().RunCtlAsync(new CtlRequest(target, key, "pull", new[] { FleetCtlVerbs.PullReference(digest) }, null), CancellationToken.None);
        await Create().RunCtlAsync(new CtlRequest(target, key, "status", Array.Empty<string>(), null), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(900), _runner.Specs[0].Timeout);
        Assert.Equal(TimeSpan.FromSeconds(90), _runner.Specs[1].Timeout);
    }

    [Fact]
    public async Task A_ctl_call_that_does_not_match_the_vocabulary_or_the_stdin_contract_never_reaches_ssh()
    {
        using var key = SecretBuffer.FromUtf8(ManagerKeyBody);
        var target = new HostTarget(Address, 22, _knownHosts);
        var provisioner = Create();

        var unknown = await provisioner.RunCtlAsync(new CtlRequest(target, key, "rm", new[] { "-rf", "/" }, null), CancellationToken.None);
        var missingStdin = await provisioner.RunCtlAsync(new CtlRequest(target, key, "put-env", Array.Empty<string>(), null), CancellationToken.None);
        var unwantedStdin = await provisioner.RunCtlAsync(new CtlRequest(target, key, "status", Array.Empty<string>(), "x"), CancellationToken.None);
        var tooLarge = await provisioner.RunCtlAsync(new CtlRequest(target, key, "login", new[] { "--registry", "ghcr.io" }, new string('x', 513)), CancellationToken.None);
        var unpinned = await provisioner.RunCtlAsync(new CtlRequest(new HostTarget(Address, 22, null), key, "status", Array.Empty<string>(), null), CancellationToken.None);

        Assert.All(new[] { unknown, missingStdin, unwantedStdin, tooLarge }, result =>
        {
            Assert.False(result.Success);
            Assert.Equal(FailureReasons.InternalError, result.FailureReason);
        });
        Assert.Equal(FailureReasons.HostKeyMismatch, unpinned.FailureReason);
        Assert.Empty(_runner.Specs);
    }

    [Theory]
    [InlineData(255, "Permission denied (publickey).", false, FailureReasons.AuthFailed)]
    [InlineData(255, "@ WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED! @", false, FailureReasons.HostKeyChanged)]
    [InlineData(255, "Host key verification failed.", false, FailureReasons.HostKeyChanged)]
    [InlineData(255, "ssh: connect to host 203.0.113.10 port 22: Connection timed out", false, FailureReasons.SshUnreachable)]
    [InlineData(127, "executable could not be started", false, FailureReasons.SshUnreachable)]
    [InlineData(-1, "", true, FailureReasons.SshUnreachable)]
    public async Task Transport_failures_map_to_stable_reasons(int exitCode, string stderr, bool timedOut, string reason)
    {
        _runner.Handler = _ => new ProcessResult(exitCode, string.Empty, stderr, timedOut);
        using var key = SecretBuffer.FromUtf8(ManagerKeyBody);

        var result = await Create().RunCtlAsync(
            new CtlRequest(new HostTarget(Address, 22, _knownHosts), key, "status", Array.Empty<string>(), null),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(reason, result.FailureReason);
        Assert.Equal(string.Empty, result.Stdout);
    }

    [Fact]
    public async Task A_ctl_level_refusal_keeps_the_helpers_json_and_exposes_only_its_sanitised_error_sentence()
    {
        using var key = SecretBuffer.FromUtf8(ManagerKeyBody);
        var target = new HostTarget(Address, 22, _knownHosts);

        _runner.Handler = _ => new ProcessResult(1, "{\"ok\":false,\"error\":\"image is not present\"}", string.Empty, false);
        var plain = await Create().RunCtlAsync(new CtlRequest(target, key, "status", Array.Empty<string>(), null), CancellationToken.None);
        Assert.False(plain.Success);
        Assert.Null(plain.FailureReason);
        Assert.Equal("image is not present", plain.Detail);
        Assert.Equal("{\"ok\":false,\"error\":\"image is not present\"}", plain.Stdout);
        Assert.Equal(1, plain.ExitCode);

        // A helper is untrusted: markup and terminal escapes in its error text never reach the UI.
        _runner.Handler = _ => new ProcessResult(1, "{\"ok\":false,\"error\":\"<script>alert(1)</script>\\u001b[31m red\"}", string.Empty, false);
        var hostile = await Create().RunCtlAsync(new CtlRequest(target, key, "status", Array.Empty<string>(), null), CancellationToken.None);
        Assert.DoesNotContain("<script>", hostile.Detail);
        Assert.DoesNotContain('\u001b', hostile.Detail!);
        Assert.Contains("&lt;script&gt;", hostile.Detail);

        // Not JSON at all: the raw (sanitised) text, from stderr when stdout is empty.
        _runner.Handler = _ => new ProcessResult(2, string.Empty, "oet-fleet-gate: command rejected", false);
        var refused = await Create().RunCtlAsync(new CtlRequest(target, key, "status", Array.Empty<string>(), null), CancellationToken.None);
        Assert.Contains("command rejected", refused.Detail);
        Assert.Null(refused.FailureReason);
    }

    // ---- host key scan ---------------------------------------------------------------------

    [Fact]
    public async Task The_host_key_scan_is_ssh_keyscan_with_a_fixed_argument_list()
    {
        var blob = FakeKeys.HostKeyBlob("target");
        _runner.Handler = _ => new ProcessResult(0, Address + " ssh-ed25519 " + blob + "\n", string.Empty, false);

        var scan = await Create().ScanHostKeyAsync(Address, 22, CancellationToken.None);

        Assert.True(scan.Success);
        var key = Assert.Single(scan.Keys);
        Assert.Equal("ssh-ed25519", key.Algorithm);
        Assert.Equal(HostKeys.FingerprintSha256(blob), key.Fingerprint);
        var spec = Assert.Single(_runner.Specs);
        Assert.Equal("ssh-keyscan", spec.FileName);
        Assert.Equal(new[] { "-T", "10", "-p", "22", "-t", "ed25519,ecdsa,rsa", Address }, spec.Arguments.ToArray());
        Assert.Equal(TimeSpan.FromSeconds(20), spec.Timeout);
    }

    [Fact]
    public async Task A_scan_that_finds_nothing_says_why_and_a_hostile_target_never_reaches_the_scanner()
    {
        _runner.Handler = _ => new ProcessResult(0, string.Empty, string.Empty, false);
        var empty = await Create().ScanHostKeyAsync(Address, 22, CancellationToken.None);
        Assert.False(empty.Success);
        Assert.Equal("no host keys offered", empty.Detail);

        _runner.Handler = _ => new ProcessResult(-1, string.Empty, string.Empty, true);
        var slow = await Create().ScanHostKeyAsync(Address, 22, CancellationToken.None);
        Assert.False(slow.Success);
        Assert.Equal("timed out", slow.Detail);

        _runner.Specs.Clear();
        Assert.False((await Create().ScanHostKeyAsync(Address + "; id", 22, CancellationToken.None)).Success);
        Assert.False((await Create().ScanHostKeyAsync("-oProxyCommand=id", 22, CancellationToken.None)).Success);
        Assert.False((await Create().ScanHostKeyAsync(Address, 0, CancellationToken.None)).Success);
        Assert.Empty(_runner.Specs);
    }
}
