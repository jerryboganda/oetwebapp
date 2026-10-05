using System.Text;
using System.Text.Json;
using Fleet.Core.Domain;
using Fleet.Core.Ssh;
using Fleet.Manager.Provisioning;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>A helper VPS as the manager's tools see it. Everything the real playbooks and oet-fleet-ctl change is a field here.</summary>
public sealed class FakeHost
{
    public string Address { get; init; } = string.Empty;

    public int Port { get; init; } = 22;

    public string Algorithm { get; set; } = "ssh-ed25519";

    /// <summary>The key the host presents NOW. Changing it simulates a rebuilt VPS or a man in the middle.</summary>
    public string KeyBlob { get; set; } = string.Empty;

    public bool Reachable { get; set; } = true;

    public string OwnerKeyText { get; init; } = string.Empty;

    public string Os { get; set; } = "Ubuntu 24.04 LTS";

    public bool OsSupported { get; set; } = true;

    public int CpuCores { get; set; } = 4;

    public int MemMiB { get; set; } = 7900;

    public int DiskGiB { get; set; } = 60;

    // ---- things that are NOT ours and must survive removal ----
    public List<string> ForeignContainers { get; } = new();

    public List<string> ForeignImages { get; } = new() { "nginx@sha256:" + new string('f', 64) };

    // ---- state the enrollment creates ----
    public bool FleetUser { get; set; }

    public bool DockerInstalled { get; set; }

    public bool FirewallApplied { get; set; }

    public bool BaselineApplied { get; set; }

    public bool SshHardened { get; set; }

    public string? ManagerPublicLine { get; set; }

    public int KeyInstalls { get; set; }

    public HashSet<string> Images { get; } = new(StringComparer.Ordinal);

    public bool LoggedIn { get; set; }

    public int Logins { get; set; }

    public int Logouts { get; set; }

    public int Pulls { get; set; }

    /// <summary>The env FILE on the helper (what <c>put-env</c> wrote).</summary>
    public string? EnvText { get; set; }

    /// <summary>
    /// The environment the running container was CREATED with. Docker bakes <c>--env-file</c> in at creation, so a later <c>put-env</c> changes
    /// <see cref="EnvText"/> but not this; only <c>run</c> and <c>restart</c> (which recreates the container) copy the file into it.
    /// </summary>
    public string? ContainerEnvText { get; set; }

    public int EnvWrites { get; set; }

    public string? RunningDigest { get; set; }

    public int Runs { get; set; }

    public int Restarts { get; set; }

    public int Prunes { get; set; }

    /// <summary>How often <c>status</c> was answered while Docker was not installed yet (S3 proves the restricted login before S4 installs it).</summary>
    public int StatusCallsWithoutDocker { get; set; }

    public bool UnitWritten { get; set; }

    public bool Uninstalled { get; set; }

    public bool ManagerAccountLocked { get; set; }

    public Dictionary<EnrollStep, int> ApplyCounts { get; } = new();

    /// <summary>Every ctl call (verb and argv) in order, for argv-hygiene assertions.</summary>
    public List<(string Verb, IReadOnlyList<string> Args)> CtlLog { get; } = new();

    /// <summary>What reached each verb on stdin (the only place a secret may travel).</summary>
    public Dictionary<string, string> Stdin { get; } = new(StringComparer.Ordinal);

    public string Fingerprint => HostKeys.FingerprintSha256(KeyBlob);

    public string Key => Address + ":" + Port;
}

/// <summary>
/// <see cref="IProvisioner"/> over fake helpers. It enforces what the real thing enforces: the pinned host key (a changed key is
/// <c>host_key_changed</c>), the owner credential for root-level steps, the manager key for ctl, the real
/// <see cref="FleetCtlVerbs"/> argument patterns, and idempotent effects. A crash plan kills the "process" at an exact call.
/// </summary>
public sealed class FakeProvisioner : IProvisioner
{
    private readonly Dictionary<string, FakeHost> _hosts = new(StringComparer.Ordinal);
    private int _calls;

    /// <summary>Digest to image id of everything published to the fake registry.</summary>
    public Dictionary<string, string> Registry { get; } = new(StringComparer.Ordinal);

    public bool RequireLoginToPull { get; set; } = true;

    public Action<FakeHost, string, string>? OnAgentStart { get; set; }

    public int Calls => _calls;

    /// <summary>Kill the manager at this call number (1-based over every provisioner call). 0 = never.</summary>
    public int CrashAtCall { get; set; }

    public bool CrashAfterEffect { get; set; } = true;

    public CancellationTokenSource? CrashSource { get; set; }

    /// <summary>Kill the manager right after this step's apply changed the helper (a more readable plan than a call number).</summary>
    public EnrollStep? CrashAfterApplyOfStep { get; set; }

    public Dictionary<EnrollStep, ProvisionResult> FailStepOnce { get; } = new();

    public Dictionary<string, CtlResult> FailCtlOnce { get; } = new(StringComparer.Ordinal);

    /// <summary>Verbs whose next call RUNS on the helper but whose answer never reaches the manager (the connection drops after the effect).</summary>
    public HashSet<string> LoseResponseOnce { get; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<FakeHost> Hosts => _hosts.Values;

    public FakeHost AddHost(string address, int port = 22, string? ownerKeyMarker = null)
    {
        var host = new FakeHost
        {
            Address = address,
            Port = port,
            KeyBlob = FakeKeys.HostKeyBlob(address + ":" + port),
            OwnerKeyText = FakeKeys.OwnerKeyText(ownerKeyMarker ?? ("OWNER-KEY-MARKER-" + address)),
        };
        _hosts[host.Key] = host;
        return host;
    }

    public FakeHost? Find(string address, int port = 22) => _hosts.TryGetValue(address + ":" + port, out var host) ? host : null;

    // ---- IProvisioner ----

    public Task<HostKeyScanResult> ScanHostKeyAsync(string address, int port, CancellationToken cancellationToken)
    {
        Enter(false);
        var host = Find(address, port);
        if (host is null || !host.Reachable)
        {
            return Task.FromResult(new HostKeyScanResult(false, Array.Empty<ScannedHostKey>(), "unreachable"));
        }

        var key = new ScannedHostKey(host.Algorithm, host.KeyBlob, host.Fingerprint);
        Leave();
        return Task.FromResult(new HostKeyScanResult(true, new[] { key }, null));
    }

    public Task<ProvisionResult> CheckAsync(ProvisionRequest request, CancellationToken cancellationToken)
    {
        Enter(false);
        // The real predicate of S3 is a restricted-login probe with the manager key: no owner credential is involved.
        var gate = Gate(request, requireOwner: IsOwnerStep(request.Step) && request.Step != EnrollStep.InstallKey, out var host);
        if (gate is not null)
        {
            return Task.FromResult(gate);
        }

        var satisfied = request.Step switch
        {
            EnrollStep.Preflight => false,
            EnrollStep.FleetUser => host!.FleetUser,
            EnrollStep.InstallKey => host!.ManagerPublicLine is not null
                && request.ManagerKey is not null
                && host.ManagerPublicLine == FakeKeys.PublicLineFor(request.ManagerKey),
            EnrollStep.Docker => host!.DockerInstalled,
            EnrollStep.Firewall => host!.FirewallApplied,
            EnrollStep.HostBaseline => host!.BaselineApplied,
            EnrollStep.HardenSsh => host!.SshHardened,
            _ => false,
        };
        Leave();
        return Task.FromResult(satisfied ? ProvisionResult.AlreadySatisfied("already satisfied") : ProvisionResult.NotSatisfied());
    }

    public Task<ProvisionResult> ApplyAsync(ProvisionRequest request, CancellationToken cancellationToken)
    {
        Enter(false);
        var gate = Gate(request, requireOwner: IsOwnerStep(request.Step), out var host);
        if (gate is not null)
        {
            return Task.FromResult(gate);
        }

        if (FailStepOnce.Remove(request.Step, out var injected))
        {
            return Task.FromResult(injected);
        }

        var helper = host!;
        helper.ApplyCounts[request.Step] = helper.ApplyCounts.GetValueOrDefault(request.Step) + 1;
        ProvisionResult result;
        switch (request.Step)
        {
            case EnrollStep.Preflight:
                if (!helper.OsSupported)
                {
                    result = ProvisionResult.Fail(FailureReasons.PreflightRejected, "os_unsupported", "host rejected");
                }
                else if (helper.ForeignContainers.Any(c => c.StartsWith("oet-", StringComparison.Ordinal)))
                {
                    result = ProvisionResult.Fail(FailureReasons.PreflightRejected, "existing_oet_workload", "host rejected");
                }
                else
                {
                    result = ProvisionResult.Done(
                        "host meets the baseline",
                        new Dictionary<string, object?>
                        {
                            ["os"] = helper.Os,
                            ["arch"] = "x86_64",
                            ["cpuCores"] = (long)helper.CpuCores,
                            ["memMiB"] = (long)helper.MemMiB,
                            ["diskGiB"] = (long)helper.DiskGiB,
                        });
                }

                break;
            case EnrollStep.FleetUser:
                helper.FleetUser = true;
                result = ProvisionResult.Done("fleet user installed");
                break;
            case EnrollStep.InstallKey:
                if (request.ManagerPublicKey is null)
                {
                    result = ProvisionResult.Fail(FailureReasons.BootstrapStepFailed, "no public key", "install-key failed");
                }
                else
                {
                    helper.ManagerPublicLine = request.ManagerPublicKey;
                    helper.KeyInstalls++;
                    result = ProvisionResult.Done("manager key installed");
                }

                break;
            case EnrollStep.Docker:
                helper.DockerInstalled = true;
                result = ProvisionResult.Done("docker installed");
                break;
            case EnrollStep.Firewall:
                helper.FirewallApplied = true;
                result = ProvisionResult.Done("firewall applied");
                break;
            case EnrollStep.HostBaseline:
                helper.BaselineApplied = true;
                result = ProvisionResult.Done("baseline applied");
                break;
            case EnrollStep.HardenSsh:
                // The real playbook refuses to harden without a working manager login: so does the fake.
                if (request.ManagerKey is null || helper.ManagerPublicLine != FakeKeys.PublicLineFor(request.ManagerKey))
                {
                    result = ProvisionResult.Fail(FailureReasons.LockoutRisk, "manager login does not work", "refused to harden");
                }
                else
                {
                    helper.SshHardened = true;
                    result = ProvisionResult.Done("sshd hardened");
                }

                break;
            default:
                result = ProvisionResult.Fail(FailureReasons.InternalError, "unexpected step", "unexpected step");
                break;
        }

        if (CrashAfterApplyOfStep == request.Step)
        {
            CrashAfterApplyOfStep = null;
            Kill();
        }

        Leave();
        return Task.FromResult(result);
    }

    public Task<CtlResult> RunCtlAsync(CtlRequest request, CancellationToken cancellationToken)
    {
        Enter(false);
        if (!FleetCtlVerbs.TryBuild(request.Verb, request.Args, out _, out var error))
        {
            return Task.FromResult(new CtlResult(false, FailureReasons.InternalError, error, "{\"ok\":false}", 2));
        }

        var host = Find(request.Target.Address, request.Target.Port);
        if (host is null || !host.Reachable)
        {
            return Task.FromResult(new CtlResult(false, FailureReasons.SshUnreachable, "unreachable", string.Empty, 255));
        }

        if (!PinMatches(request.Target, host))
        {
            return Task.FromResult(new CtlResult(false, FailureReasons.HostKeyChanged, "host key changed", string.Empty, 255));
        }

        if (host.ManagerAccountLocked
            || host.ManagerPublicLine is null
            || host.ManagerPublicLine != FakeKeys.PublicLineFor(request.ManagerKey))
        {
            return Task.FromResult(new CtlResult(false, FailureReasons.AuthFailed, "permission denied", string.Empty, 255));
        }

        host.CtlLog.Add((request.Verb, request.Args.ToList()));
        if (request.StdinText is not null)
        {
            host.Stdin[request.Verb] = request.StdinText;
        }

        if (FailCtlOnce.Remove(request.Verb, out var injected))
        {
            return Task.FromResult(injected);
        }

        var result = Execute(host, request);
        if (LoseResponseOnce.Remove(request.Verb))
        {
            // The verb ran (its effect is on the helper) but the answer was lost: the manager only sees a dropped connection.
            Leave();
            return Task.FromResult(new CtlResult(false, FailureReasons.SshUnreachable, "connection lost", string.Empty, 255));
        }

        Leave();
        return Task.FromResult(result);
    }

    // ---- ctl verbs ----

    private CtlResult Execute(FakeHost host, CtlRequest request)
    {
        // Like the real ctl: only `status` works on a helper without Docker (a missing binary is "docker.running = false"); every verb that
        // drives the daemon fails there, and S3 must therefore prove the restricted login with `status` alone.
        if (!host.DockerInstalled && request.Verb is "login" or "pull" or "verify" or "run" or "stop" or "restart" or "prune" or "wipe-scratch" or "logs")
        {
            return Failed("docker: not found");
        }

        switch (request.Verb)
        {
            case "status":
                if (!host.DockerInstalled)
                {
                    host.StatusCallsWithoutDocker++;
                }

                return Ok(StatusJson(host));

            case "harden-check":
                return host.BaselineApplied && host.FirewallApplied
                    ? Ok("{\"ok\":true,\"findings\":[]}")
                    : new CtlResult(false, null, "findings", "{\"ok\":false,\"findings\":[\"swap is on\"]}", 1);

            case "login":
                if (request.StdinText is null || request.StdinText.Split('\n').Length < 2)
                {
                    return Refused("credential shape rejected");
                }

                host.LoggedIn = true;
                host.Logins++;
                return Ok("{\"ok\":true}");

            case "pull":
            {
                var digest = request.Args[0][(request.Args[0].IndexOf('@') + 1)..];
                if (!Registry.TryGetValue(digest, out var imageId))
                {
                    return Failed("manifest unknown");
                }

                if (RequireLoginToPull && !host.LoggedIn)
                {
                    return Failed("unauthorized");
                }

                host.Images.Add(digest);
                host.Pulls++;
                return Ok("{\"ok\":true,\"imageId\":\"" + imageId + "\",\"repoDigests\":[\"" + FleetCtlVerbs.AgentRepository + "@" + digest + "\"]}");
            }

            case "verify":
            {
                var digest = request.Args[0];
                if (!host.Images.Contains(digest))
                {
                    return Failed("image is not present");
                }

                if (request.Args.Count > 1 && Registry.TryGetValue(digest, out var id) && id != request.Args[1])
                {
                    return Failed("image id mismatch");
                }

                return Ok("{\"ok\":true}");
            }

            case "logout":
                host.LoggedIn = false;
                host.Logouts++;
                return Ok("{\"ok\":true}");

            case "put-env":
                if (!TryValidateEnv(request.StdinText, out var env))
                {
                    return Refused("env rejected");
                }

                host.EnvText = request.StdinText;
                host.EnvWrites++;
                return Ok("{\"ok\":true}");

            case "run":
            {
                var digest = request.Args[0];
                if (!host.Images.Contains(digest) || host.EnvText is null)
                {
                    return Failed("image or env missing");
                }

                // Like the real ctl: the digest line of the env file follows the container being started.
                var lines = host.EnvText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.StartsWith("OET_AGENT_IMAGE_DIGEST=", StringComparison.Ordinal) ? "OET_AGENT_IMAGE_DIGEST=" + digest : l)
                    .ToList();
                if (!lines.Any(l => l.StartsWith("OET_AGENT_IMAGE_DIGEST=", StringComparison.Ordinal)))
                {
                    lines.Add("OET_AGENT_IMAGE_DIGEST=" + digest);
                }

                host.EnvText = string.Join('\n', lines) + "\n";
                host.ContainerEnvText = host.EnvText;
                host.RunningDigest = digest;
                host.Runs++;
                OnAgentStart?.Invoke(host, host.ContainerEnvText, digest);
                return Ok("{\"ok\":true,\"containerId\":\"c0ffee\"}");
            }

            case "restart":
            {
                // Like the real ctl: the container is RECREATED from the CURRENT env file, because `docker restart` would keep the environment
                // it was created with (the old node token). The digest comes from that file, exactly as in oet-fleet-ctl.
                var digest = host.EnvText?
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.StartsWith("OET_AGENT_IMAGE_DIGEST=", StringComparison.Ordinal))
                    .Select(line => line["OET_AGENT_IMAGE_DIGEST=".Length..])
                    .FirstOrDefault();
                if (host.EnvText is null)
                {
                    return Failed("no env file; put-env first");
                }

                if (digest is null)
                {
                    return Failed("the env file names no agent image digest");
                }

                if (!host.Images.Contains(digest))
                {
                    return Failed("image is not present");
                }

                host.ContainerEnvText = host.EnvText;
                host.RunningDigest = digest;
                host.Restarts++;
                OnAgentStart?.Invoke(host, host.ContainerEnvText, digest);
                return Ok("{\"ok\":true,\"containerId\":\"c0ffee\"}");
            }

            case "stop":
                host.RunningDigest = null;
                return Ok("{\"ok\":true}");

            case "prune":
                host.Prunes++;
                return Ok("{\"ok\":true,\"removed\":0}");

            case "unit-sync":
                host.UnitWritten = true;
                return Ok("{\"ok\":true}");

            case "uninstall":
                // ONLY fleet-owned components. Docker, the firewall, the baseline, foreign containers and foreign images stay.
                host.RunningDigest = null;
                host.Images.Clear();
                host.EnvText = null;
                host.ContainerEnvText = null;
                host.UnitWritten = false;
                host.Uninstalled = true;
                host.ManagerAccountLocked = true;
                return Ok("{\"ok\":true,\"warnings\":[]}");

            default:
                return Ok("{\"ok\":true}");
        }
    }

    private static bool TryValidateEnv(string? text, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0)
            {
                return false;
            }

            var key = line[..index];
            if (!AgentEnv.AllowedKeys.Contains(key, StringComparer.Ordinal) || values.ContainsKey(key))
            {
                return false;
            }

            values[key] = line[(index + 1)..];
        }

        return true;
    }

    private static string StatusJson(FakeHost host)
    {
        var agent = host.RunningDigest is null
            ? new Dictionary<string, object?> { ["present"] = false }
            : new Dictionary<string, object?> { ["present"] = true, ["state"] = "running", ["imageDigest"] = host.RunningDigest, ["restartCount"] = 0, ["oomKilled"] = false };
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schema"] = "oet-fleet-ctl.status/1",
            ["host"] = new Dictionary<string, object?> { ["os"] = host.Os, ["arch"] = "x86_64", ["cpuCores"] = host.CpuCores, ["memTotalMiB"] = host.MemMiB, ["swapMiB"] = 0 },
            ["docker"] = new Dictionary<string, object?> { ["version"] = host.DockerInstalled ? "27.3.1" : null, ["running"] = host.DockerInstalled },
            ["agent"] = agent,
            ["ctl"] = new Dictionary<string, object?> { ["version"] = 1 },
        });
    }

    private static CtlResult Ok(string stdout) => new(true, null, null, stdout, 0);

    private static CtlResult Failed(string error) => new(false, null, error, "{\"ok\":false,\"error\":\"" + error + "\"}", 1);

    private static CtlResult Refused(string error) => new(false, null, error, "{\"ok\":false,\"error\":\"" + error + "\"}", 2);

    // ---- gate shared by the ansible-style steps ----

    private static bool IsOwnerStep(EnrollStep step) => EnrollSteps.UsesOwnerCredential(step);

    private ProvisionResult? Gate(ProvisionRequest request, bool requireOwner, out FakeHost? host)
    {
        host = Find(request.Target.Address, request.Target.Port);
        if (host is null || !host.Reachable)
        {
            return ProvisionResult.Fail(FailureReasons.SshUnreachable, "unreachable", "unreachable");
        }

        if (!PinMatches(request.Target, host))
        {
            return ProvisionResult.Fail(FailureReasons.HostKeyChanged, "the host presented a different key", "host key changed");
        }

        if (requireOwner)
        {
            var presented = request.OwnerKey is null ? null : Encoding.UTF8.GetString(request.OwnerKey.AsSpan());
            if (presented != host.OwnerKeyText)
            {
                return ProvisionResult.Fail(FailureReasons.AuthFailed, "permission denied", "authentication failed");
            }
        }

        return null;
    }

    private static bool PinMatches(HostTarget target, FakeHost host) =>
        target.KnownHostsLine == HostKeys.KnownHostsLine(host.Address, host.Port, host.Algorithm, host.KeyBlob);

    // ---- crash plan ----

    private void Enter(bool afterEffect)
    {
        _calls++;
        if (CrashAtCall == _calls && !CrashAfterEffect)
        {
            Kill();
        }
    }

    private void Leave()
    {
        if (CrashAtCall == _calls && CrashAfterEffect)
        {
            Kill();
        }
    }

    private void Kill()
    {
        CrashAtCall = 0;
        var source = CrashSource ?? throw new InvalidOperationException("A crash plan needs a CrashSource.");
        source.Cancel();
        throw new OperationCanceledException(source.Token);
    }
}
