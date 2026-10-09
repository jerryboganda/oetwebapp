using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleet.Core.Audit;
using Fleet.Core.Domain;
using Fleet.Core.Ssh;
using Fleet.Core.Validation;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Provisioning;

/// <summary>
/// The production <see cref="IProvisioner"/>: Ansible (<c>ansible-playbook</c>) over OpenSSH for the
/// root-level steps and plain <c>ssh</c> to the restricted <c>oet-fleet-ctl</c> for everything after the
/// owner credential is gone. Rules it enforces (OET-RWP/1 sections 7.4, 8.2, 8.9):
/// <list type="bullet">
/// <item>every host-supplied or owner-supplied value reaches Ansible as JSON DATA via <c>-e @file</c>
/// and reaches ssh as a separate argv element, never through a shell;</item>
/// <item>SSH always uses <c>StrictHostKeyChecking=yes</c> against a per-run known_hosts file rebuilt
/// from the owner-verified pin;</item>
/// <item>key material lives only in a 0600 file in a 0700 tmpfs directory that is zeroed and removed;</item>
/// <item>ansible runs are serialised and bounded (forks, timeout); helper output is untrusted and is
/// sanitised, capped and HTML-encoded before it is stored.</item>
/// </list>
/// </summary>
public sealed class AnsibleProvisioner : IProvisioner
{
    private static readonly SemaphoreSlim PlaybookGate = new(1, 1);

    private static readonly IReadOnlyDictionary<EnrollStep, string> Playbooks = new Dictionary<EnrollStep, string>
    {
        [EnrollStep.Preflight] = "preflight.yml",
        [EnrollStep.FleetUser] = "fleet-user.yml",
        [EnrollStep.InstallKey] = "install-key.yml",
        [EnrollStep.Docker] = "docker.yml",
        [EnrollStep.Firewall] = "firewall.yml",
        [EnrollStep.HostBaseline] = "host-baseline.yml",
        [EnrollStep.HardenSsh] = "harden-ssh.yml",
    };

    private static readonly Regex ChangedPattern = new(@"changed=(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex FailedPattern = new(@"failed=(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex UnreachablePattern = new(@"unreachable=(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex DataKeyPattern = new(@"\A[a-z][a-z0-9_]{0,63}\z", RegexOptions.CultureInvariant);

    private readonly IProcessRunner _runner;
    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AnsibleProvisioner> _logger;

    public AnsibleProvisioner(
        IProcessRunner runner,
        IOptions<FleetOptions> options,
        TimeProvider time,
        ILogger<AnsibleProvisioner> logger)
    {
        _runner = runner;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task<HostKeyScanResult> ScanHostKeyAsync(string address, int port, CancellationToken cancellationToken)
    {
        if (!InputValidator.TryParseAddress(address, out _, out var normalized, out _, out _) || InputValidator.ValidatePort(port) is not null)
        {
            return new HostKeyScanResult(false, Array.Empty<ScannedHostKey>(), "invalid target");
        }

        var provisioning = _options.Value.Provisioning;
        var result = await _runner.RunAsync(
            new ProcessSpec(
                provisioning.SshKeyscanExecutable,
                new[] { "-T", "10", "-p", port.ToString(CultureInfo.InvariantCulture), "-t", "ed25519,ecdsa,rsa", normalized },
                null,
                null,
                TimeSpan.FromSeconds(provisioning.KeyscanTimeoutSeconds)),
            cancellationToken);
        var keys = HostKeys.ParseKeyScan(result.Stdout);
        return keys.Count == 0
            ? new HostKeyScanResult(false, keys, result.TimedOut ? "timed out" : "no host keys offered")
            : new HostKeyScanResult(true, keys, null);
    }

    public async Task<ProvisionResult> CheckAsync(ProvisionRequest request, CancellationToken cancellationToken)
    {
        switch (request.Step)
        {
            case EnrollStep.Preflight:
                // Read-only already: the apply run IS the predicate.
                return ProvisionResult.NotSatisfied("preflight always runs");

            case EnrollStep.InstallKey:
            case EnrollStep.HostBaseline:
                if (request.ManagerKey is null)
                {
                    return ProvisionResult.NotSatisfied("manager key not installed yet");
                }

                var verb = request.Step == EnrollStep.InstallKey ? "status" : "harden-check";
                var ctl = await RunCtlAsync(new CtlRequest(request.Target, request.ManagerKey, verb, Array.Empty<string>(), null), cancellationToken);
                return ctl.Success && (verb == "status" || StdoutSaysOk(ctl.Stdout))
                    ? ProvisionResult.AlreadySatisfied("restricted login works")
                    : ProvisionResult.NotSatisfied("predicate not satisfied");

            case EnrollStep.Firewall:
                // The check-mode run records ok without applying anything (record-result runs with
                // check_mode:false), so a satisfied verdict here would skip S5 forever — and a
                // desired-table change (the UBAG dial port, 7 Oct) would never reach the helper.
                // The apply is the honest predicate: nft loads atomically and the lockout dance
                // re-verifies SSH, so re-asserting the firewall on every repair is safe.
                return ProvisionResult.NotSatisfied("the firewall re-asserts on every repair");

            case EnrollStep.FleetUser:
                // Same lying-check shape as the firewall: the fleet-user playbook copies the current
                // oet-fleet-gate/oet-fleet-ctl/sudoers onto the helper, and a check-mode "satisfied"
                // would freeze a helper on an old control surface forever (seen live 9 Oct: a helper
                // missed the 443 publish update). The copies are idempotent re-asserts.
                return ProvisionResult.NotSatisfied("the fleet user and control surface re-assert on every repair");

            default:
                return await RunPlaybookAsync(request, check: true, cancellationToken);
        }
    }

    public Task<ProvisionResult> ApplyAsync(ProvisionRequest request, CancellationToken cancellationToken) =>
        RunPlaybookAsync(request, check: false, cancellationToken);

    public async Task<CtlResult> RunCtlAsync(CtlRequest request, CancellationToken cancellationToken)
    {
        if (!FleetCtlVerbs.TryBuild(request.Verb, request.Args, out var command, out var error))
        {
            return new CtlResult(false, FailureReasons.InternalError, error, string.Empty, -1);
        }

        var spec = FleetCtlVerbs.Find(request.Verb)!;
        if (spec.UsesStdin != (request.StdinText is not null)
            || (request.StdinText is not null && request.StdinText.Length > spec.MaxStdinBytes))
        {
            return new CtlResult(false, FailureReasons.InternalError, "stdin does not match the verb", string.Empty, -1);
        }

        if (request.Target.KnownHostsLine is null)
        {
            return new CtlResult(false, FailureReasons.HostKeyMismatch, "the host key is not pinned", string.Empty, -1);
        }

        var provisioning = _options.Value.Provisioning;
        using var scratch = RunScratch.Create(provisioning.ScratchDirectory);
        var knownHosts = scratch.File("known_hosts");
        await File.WriteAllTextAsync(knownHosts, request.Target.KnownHostsLine + "\n", cancellationToken);
        using var keyFile = TempSecretFile.Create(scratch.Path, "key", request.ManagerKey);

        var args = SshOptions.BuildArguments(knownHosts, keyFile.Path, request.Target.Address, request.Target.Port, SshOptions.FleetUser, command);
        var timeout = TimeSpan.FromSeconds(request.Verb == "pull" ? provisioning.PullTimeoutSeconds : provisioning.CtlTimeoutSeconds);
        var result = await _runner.RunAsync(
            new ProcessSpec(provisioning.SshExecutable, args, null, request.StdinText, timeout),
            cancellationToken);

        if (result.TimedOut)
        {
            return new CtlResult(false, FailureReasons.SshUnreachable, "timed out", string.Empty, result.ExitCode);
        }

        if (result.ExitCode == 0)
        {
            return new CtlResult(true, null, null, result.Stdout, 0);
        }

        if (result.ExitCode == 255 || result.ExitCode == 127)
        {
            return new CtlResult(false, ClassifySshFailure(result.Stderr), Sanitize(result.Stderr), string.Empty, result.ExitCode);
        }

        // The restricted ctl ran and refused or failed: its stdout is JSON {"ok":false,"error":"..."}.
        return new CtlResult(false, null, CtlErrorDetail(result), result.Stdout, result.ExitCode);
    }

    /// <summary>The helper's own error sentence when stdout is the ctl's JSON error object, else the sanitised stdout or stderr. Always untrusted text.</summary>
    private static string? CtlErrorDetail(ProcessResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.Stdout);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return Sanitize(error.GetString());
            }
        }
        catch (JsonException)
        {
            // Not the ctl's JSON: fall through to the raw text.
        }

        return Sanitize(string.IsNullOrWhiteSpace(result.Stdout) ? result.Stderr : result.Stdout);
    }

    private async Task<ProvisionResult> RunPlaybookAsync(ProvisionRequest request, bool check, CancellationToken cancellationToken)
    {
        if (!Playbooks.TryGetValue(request.Step, out var playbook))
        {
            return ProvisionResult.Fail(FailureReasons.InternalError, "no playbook for step " + request.Step, "no playbook");
        }

        if (request.Target.KnownHostsLine is null)
        {
            return ProvisionResult.Fail(FailureReasons.HostKeyMismatch, "the host key is not pinned", "host key not pinned");
        }

        var identity = request.OwnerKey ?? request.ManagerKey;
        var user = request.OwnerKey is not null ? request.OwnerUser : SshOptions.FleetUser;
        if (identity is null || user is null || !InputValidator.IsValidSshUser(user))
        {
            return ProvisionResult.Fail(FailureReasons.OwnerCredentialExpired, "no usable credential for this step", "no credential");
        }

        var provisioning = _options.Value.Provisioning;
        var directory = provisioning.AnsibleDirectory;
        await PlaybookGate.WaitAsync(cancellationToken);
        try
        {
            using var scratch = RunScratch.Create(provisioning.ScratchDirectory);
            var knownHosts = scratch.File("known_hosts");
            await File.WriteAllTextAsync(knownHosts, request.Target.KnownHostsLine + "\n", cancellationToken);
            using var keyFile = TempSecretFile.Create(scratch.Path, "key", identity);
            TempSecretFile? managerKeyFile = null;
            try
            {
                if (request.ManagerKey is not null)
                {
                    managerKeyFile = TempSecretFile.Create(scratch.Path, "manager-key", request.ManagerKey);
                }

                var resultPath = scratch.File("result.json");
                var varsPath = scratch.File("vars.json");
                var vars = BuildVars(request, user, keyFile.Path, managerKeyFile?.Path, knownHosts, resultPath, check);
                await File.WriteAllTextAsync(varsPath, JsonSerializer.Serialize(vars), cancellationToken);

                foreach (var sub in new[] { "home", "ansible-home", "ansible-local", "cp" })
                {
                    Directory.CreateDirectory(Path.Combine(scratch.Path, sub));
                }

                var args = new List<string>
                {
                    "-i", Path.Combine(directory, "inventory", "target.yml"),
                    Path.Combine(directory, "playbooks", playbook),
                    "-e", "@" + varsPath,
                    "--forks", provisioning.ClampedForks.ToString(CultureInfo.InvariantCulture),
                    "--timeout", "30",
                };
                if (check)
                {
                    args.Add("--check");
                }

                var environment = new Dictionary<string, string>
                {
                    ["ANSIBLE_CONFIG"] = Path.Combine(directory, "ansible.cfg"),
                    ["ANSIBLE_HOST_KEY_CHECKING"] = "True",
                    ["ANSIBLE_NOCOLOR"] = "1",
                    ["ANSIBLE_FORCE_COLOR"] = "0",
                    ["ANSIBLE_DEPRECATION_WARNINGS"] = "False",
                    ["ANSIBLE_RETRY_FILES_ENABLED"] = "False",
                    ["ANSIBLE_HOME"] = Path.Combine(scratch.Path, "ansible-home"),
                    ["ANSIBLE_LOCAL_TEMP"] = Path.Combine(scratch.Path, "ansible-local"),
                    ["ANSIBLE_SSH_CONTROL_PATH_DIR"] = Path.Combine(scratch.Path, "cp"),
                    ["HOME"] = Path.Combine(scratch.Path, "home"),
                    ["TMPDIR"] = scratch.Path,
                    ["LC_ALL"] = "C.UTF-8",
                    ["PYTHONDONTWRITEBYTECODE"] = "1",
                };

                var run = await _runner.RunAsync(
                    new ProcessSpec(
                        provisioning.AnsiblePlaybookExecutable,
                        args,
                        environment,
                        null,
                        TimeSpan.FromSeconds(provisioning.StepTimeoutSeconds),
                        WorkingDirectory: directory),
                    cancellationToken);

                _logger.LogInformation(
                    "Ansible step {Step} ({Mode}) finished with exit code {ExitCode}.",
                    EnrollSteps.Name(request.Step),
                    check ? "check" : "apply",
                    run.ExitCode);
                return Interpret(request.Step, check, run, resultPath);
            }
            finally
            {
                managerKeyFile?.Dispose();
            }
        }
        finally
        {
            PlaybookGate.Release();
        }
    }

    private Dictionary<string, object?> BuildVars(
        ProvisionRequest request,
        string user,
        string keyPath,
        string? managerKeyPath,
        string knownHostsPath,
        string resultPath,
        bool check)
    {
        var provisioning = _options.Value.Provisioning;
        var vars = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in request.Data)
        {
            if (DataKeyPattern.IsMatch(key) && !key.StartsWith("fleet_", StringComparison.Ordinal))
            {
                vars[key] = value;
            }
        }

        // Reserved names are set last so request data can never override them.
        vars["fleet_target_address"] = request.Target.Address;
        vars["fleet_target_port"] = request.Target.Port;
        vars["fleet_ssh_user"] = user;
        vars["fleet_become"] = !string.Equals(user, "root", StringComparison.Ordinal);
        vars["fleet_key_file"] = keyPath;
        vars["fleet_manager_key_file"] = managerKeyPath;
        vars["fleet_known_hosts_file"] = knownHostsPath;
        vars["fleet_ssh_common_args"] = SshOptions.AnsibleCommonArgs(knownHostsPath);
        vars["fleet_result_file"] = resultPath;
        vars["fleet_step"] = EnrollSteps.Name(request.Step);
        vars["fleet_check_mode"] = check;
        vars["fleet_manager_pubkey"] = request.ManagerPublicKey;
        vars["fleet_fleet_user"] = SshOptions.FleetUser;
        vars["fleet_controller_epoch"] = _time.GetUtcNow().ToUnixTimeSeconds();
        vars["fleet_primary_address"] = AddressPolicy.PrimaryAddress;
        vars["fleet_docker_source"] = provisioning.DockerSource;
        vars["fleet_docker_version_pin"] = provisioning.DockerVersionPin;
        vars["fleet_agent_repository"] = FleetCtlVerbs.AgentRepository;
        return vars;
    }

    private static ProvisionResult Interpret(EnrollStep step, bool check, ProcessResult run, string resultPath)
    {
        var combined = run.Stdout + "\n" + run.Stderr;
        var stepName = EnrollSteps.Name(step);

        if (run.TimedOut)
        {
            return ProvisionResult.Fail(FailureReasons.BootstrapStepFailed, stepName + ": timed out", "timed out");
        }

        PlaybookOutcome? outcome = TryReadResult(resultPath);
        if (run.ExitCode == 0 && (outcome is null || outcome.Ok))
        {
            if (check)
            {
                return IsUnchanged(run.Stdout)
                    ? ProvisionResult.AlreadySatisfied("already satisfied")
                    : ProvisionResult.NotSatisfied("changes pending");
            }

            return ProvisionResult.Done(outcome?.Summary ?? "applied", outcome?.Facts);
        }

        if (check && run.ExitCode == 0)
        {
            return ProvisionResult.NotSatisfied("check reported a problem");
        }

        if (outcome is { Ok: false })
        {
            var reason = FailureReasons.IsKnown(outcome.Reason) ? outcome.Reason! : FailureReasons.BootstrapStepFailed;
            var detail = outcome.Detail ?? (reason == FailureReasons.BootstrapStepFailed ? stepName : null);
            return ProvisionResult.Fail(reason, Sanitize(detail), outcome.Summary ?? "step failed");
        }

        if (SshOptions.IndicatesChangedHostKey(combined))
        {
            return ProvisionResult.Fail(FailureReasons.HostKeyChanged, "the host presented a different key than the pinned one", "host key changed");
        }

        if (combined.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return ProvisionResult.Fail(FailureReasons.AuthFailed, "SSH authentication was refused", "authentication failed");
        }

        if (run.ExitCode == 4)
        {
            return ProvisionResult.Fail(FailureReasons.SshUnreachable, "the host could not be reached over SSH", "unreachable");
        }

        // In check mode a failing run (for example a task that cannot run without its prerequisite) just means "not satisfied".
        if (check)
        {
            return ProvisionResult.NotSatisfied("check could not confirm the step");
        }

        return ProvisionResult.Fail(FailureReasons.BootstrapStepFailed, stepName + ": exit code " + run.ExitCode.ToString(CultureInfo.InvariantCulture), "step failed");
    }

    private static bool IsUnchanged(string stdout)
    {
        var changed = ChangedPattern.Match(stdout);
        var failed = FailedPattern.Match(stdout);
        var unreachable = UnreachablePattern.Match(stdout);
        return changed.Success && failed.Success && unreachable.Success
            && changed.Groups[1].Value == "0" && failed.Groups[1].Value == "0" && unreachable.Groups[1].Value == "0";
    }

    private static PlaybookOutcome? TryReadResult(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            var facts = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (root.TryGetProperty("facts", out var factsElement) && factsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in factsElement.EnumerateObject())
                {
                    object? factValue = null;
                    switch (property.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                            factValue = property.Value.GetString();
                            break;
                        case JsonValueKind.Number:
                            if (property.Value.TryGetInt64(out var number))
                            {
                                factValue = number;
                            }
                            else
                            {
                                factValue = property.Value.GetDouble();
                            }

                            break;
                        case JsonValueKind.True:
                            factValue = true;
                            break;
                        case JsonValueKind.False:
                            factValue = false;
                            break;
                    }

                    facts[property.Name] = factValue;
                }
            }

            return new PlaybookOutcome(ok, ReadString(root, "reason"), ReadString(root, "detail"), ReadString(root, "summary"), facts);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool StdoutSaysOk(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ClassifySshFailure(string stderr)
    {
        if (SshOptions.IndicatesChangedHostKey(stderr))
        {
            return FailureReasons.HostKeyChanged;
        }

        return stderr.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            ? FailureReasons.AuthFailed
            : FailureReasons.SshUnreachable;
    }

    private static string Sanitize(string? text) => LogScrubber.SanitizeUntrusted(text, 500);

    private sealed record PlaybookOutcome(
        bool Ok,
        string? Reason,
        string? Detail,
        string? Summary,
        IReadOnlyDictionary<string, object?> Facts);
}
