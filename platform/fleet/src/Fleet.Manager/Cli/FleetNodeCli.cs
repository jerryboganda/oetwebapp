using System.Text;
using System.Text.Json;
using Fleet.Core.Policy;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Auth;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;

namespace Fleet.Manager.Cli;

/// <summary>
/// The operator's node and job commands, run inside the container
/// (<c>docker exec -i oet-fleet-manager dotnet Fleet.Manager.dll ...</c>). They are the same
/// application services the dashboard calls, with the same contracts: read-only verbs need no
/// step-up, every privileged verb (add, drain, enable, disable, remove, rotate-token, upgrade,
/// policy set, requeue, force-local, cancel) needs a fresh single-use authenticator code, either
/// <c>--totp CODE</c> or the first line of stdin. <c>add</c> reads the one-time owner SSH
/// credential from the rest of stdin (or <c>--ssh-key PATH</c>); it is never echoed or logged.
/// Output is a human table; <c>--json</c> switches to one JSON document for scripts.
/// </summary>
public static class FleetNodeCli
{
    private const string Actor = "cli";

    /// <summary>Verbs that change the fleet. Each requires a TOTP step-up, like its dashboard route.</summary>
    private static readonly HashSet<string> PrivilegedVerbs = new(StringComparer.Ordinal)
    {
        "add", "drain", "enable", "disable", "remove", "rotate-token", "upgrade",
        "policy", "requeue", "force-local", "cancel",
    };

    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        return args[0] switch
        {
            "status" => await FleetCli.RunWithServicesAsync((s, ct) => StatusAsync(s, args[1..], ct)),
            "nodes" => await FleetCli.RunWithServicesAsync((s, ct) => NodesAsync(s, args[1..], ct)),
            "inspect" => await FleetCli.RunWithServicesAsync((s, ct) => InspectAsync(s, args[1..], ct)),
            "operations" => await FleetCli.RunWithServicesAsync((s, ct) => OperationsAsync(s, args[1..], ct)),
            "op" => await FleetCli.RunWithServicesAsync((s, ct) => OperationAsync(s, args[1..], ct)),
            "add" => await FleetCli.RunWithServicesAsync((s, ct) => AddAsync(s, args[1..], ct)),
            "drain" => await FleetCli.RunWithServicesAsync((s, ct) => LifecycleAsync(s, "drain", args[1..], ct)),
            "enable" => await FleetCli.RunWithServicesAsync((s, ct) => LifecycleAsync(s, "enable", args[1..], ct)),
            "disable" => await FleetCli.RunWithServicesAsync((s, ct) => LifecycleAsync(s, "disable", args[1..], ct)),
            "remove" => await FleetCli.RunWithServicesAsync((s, ct) => RemoveAsync(s, args[1..], ct)),
            "rotate-token" => await FleetCli.RunWithServicesAsync((s, ct) => RotateTokenAsync(s, args[1..], ct)),
            "test" => await FleetCli.RunWithServicesAsync((s, ct) => TestAsync(s, args[1..], ct)),
            "upgrade" => await FleetCli.RunWithServicesAsync((s, ct) => UpgradeAsync(s, args[1..], ct)),
            "policy" => await FleetCli.RunWithServicesAsync((s, ct) => PolicyAsync(s, args[1..], ct)),
            "jobs" => await FleetCli.RunWithServicesAsync((s, ct) => JobsAsync(s, args[1..], ct)),
            "job" => await FleetCli.RunWithServicesAsync((s, ct) => JobAsync(s, args[1..], ct)),
            "requeue" => await FleetCli.RunWithServicesAsync((s, ct) => JobActionAsync(s, "requeue", args[1..], ct)),
            "force-local" => await FleetCli.RunWithServicesAsync((s, ct) => JobActionAsync(s, "force-local", args[1..], ct)),
            "cancel" => await FleetCli.RunWithServicesAsync((s, ct) => JobActionAsync(s, "cancel", args[1..], ct)),
            "rebalance" => await FleetCli.RunWithServicesAsync((s, ct) => RebalanceAsync(s, args[1..], ct)),
            _ => null,
        };
    }

    // ---- status, nodes, inspect ---------------------------------------------------------------

    private static async Task<int> StatusAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var api = services.GetRequiredService<IFleetApi>();
        var json = args.Contains("--json", StringComparer.Ordinal);

        var nodes = await api.ListNodesAsync(ct);
        var stats = await api.GetStatsAsync(ct);
        var status = await api.GetStatusAsync(ct);

        if (json)
        {
            Console.WriteLine(FleetJson.Serialize(new { kinds = status.Kinds, protocol = status.ProtocolCurrent, protocolMin = status.ProtocolMinimum, nodes, stats }));
            return 0;
        }

        Console.WriteLine($"FLEET  api=reachable  kinds={string.Join(",", status.Kinds)}  protocol={status.ProtocolCurrent?.ToString() ?? "?"} (min {status.ProtocolMinimum?.ToString() ?? "?"})");
        Console.WriteLine();
        Console.WriteLine(
            "NODE".PadRight(22) + "STATUS".PadRight(12) + "HEALTH".PadRight(10) + "CPU%".PadRight(7)
            + "MEMFREE%".PadRight(10) + "SLOTS".PadRight(8) + "PRESSURE".PadRight(10) + "LAST HEARTBEAT");
        foreach (var node in nodes.OrderByDescending(n => n.Status == "Active").ThenBy(n => n.NodeRef, StringComparer.Ordinal))
        {
            Console.WriteLine(
                Trunc(node.NodeRef, 21).PadRight(22)
                + Trunc(node.Status, 11).PadRight(12)
                + Trunc(node.Health, 9).PadRight(10)
                + (node.Load is null ? "--".PadRight(7) : node.Load.CpuPct.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).PadRight(7))
                + (node.Load is null ? "--".PadRight(10) : node.Load.MemFreePct.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).PadRight(10))
                + (node.Capacity is null ? "--".PadRight(8) : (node.Capacity.HeavySlotsFree + "/" + node.Leases?.Max).PadRight(8))
                + Trunc(node.Load?.Pressure ?? "--", 9).PadRight(10)
                + Age(node.LastHeartbeatAt));
        }

        var leased = stats.ValueKind == JsonValueKind.Object ? Int(stats, "leasedCount") : null;
        var primaryLeases = 0;
        if (stats.ValueKind == JsonValueKind.Object
            && stats.TryGetProperty("leasedWeightByNode", out var weights) && weights.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in weights.EnumerateObject())
            {
                if (nodeById(nodes, entry.Name) is null)
                {
                    primaryLeases += entry.Value.GetInt32();
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("PRIMARY".PadRight(22) + Trunc("local-fallback executor (never a helper; takes queued work when no node claims it, hard after the fallback window)", 100));
        Console.WriteLine("PRIMARY LEASES".PadRight(22) + primaryLeases + " local job(s) held; " + (leased?.ToString() ?? "?") + " leased fleet-wide");
        Console.WriteLine();

        if (stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty("queue", out var queue) && queue.ValueKind == JsonValueKind.Object)
        {
            Console.WriteLine("QUEUE (by kind)");
            foreach (var kind in queue.EnumerateObject())
            {
                var parts = kind.Value.EnumerateObject().Select(s => s.Name + " " + s.Value.GetInt32());
                Console.WriteLine("  " + kind.Name.PadRight(26) + string.Join("  ", parts));
            }

            var oldest = stats.ValueKind == JsonValueKind.Object ? Int(stats, "oldestQueuedAgeSeconds") : null;
            if (oldest is > 0)
            {
                Console.WriteLine("  oldest queued: " + HumanSeconds(oldest.Value));
            }
        }

        if (stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty("nodes", out var statsNodes) && statsNodes.ValueKind == JsonValueKind.Object)
        {
            var byStatus = statsNodes.TryGetProperty("byStatus", out var byStatusValue) && byStatusValue.ValueKind == JsonValueKind.Object
                ? string.Join(" ", byStatusValue.EnumerateObject().Select(s => s.Name + " " + s.Value.GetInt32()))
                : "";
            var byHealth = statsNodes.TryGetProperty("byHealth", out var byHealthValue) && byHealthValue.ValueKind == JsonValueKind.Object
                ? string.Join(" ", byHealthValue.EnumerateObject().Select(s => s.Name + " " + s.Value.GetInt32()))
                : "";
            Console.WriteLine();
            Console.WriteLine("API NODE REGISTRY  status: " + byStatus + "   health: " + byHealth);
        }

        return 0;
    }

    private static async Task<int> NodesAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var api = services.GetRequiredService<IFleetApi>();
        var nodes = await api.ListNodesAsync(ct);
        if (args.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(FleetJson.Serialize(nodes));
            return 0;
        }

        Console.WriteLine(
            "NODE".PadRight(22) + "STATUS".PadRight(12) + "VERSION".PadRight(10) + "KINDS".PadRight(44)
            + "CORES?".PadRight(8) + "LAST HEARTBEAT");
        foreach (var node in nodes)
        {
            var kinds = string.Join(",", node.Agent?.Kinds?.Select(k => k.Kind) ?? Array.Empty<string>());
            Console.WriteLine(
                Trunc(node.NodeRef, 21).PadRight(22)
                + Trunc(node.Status, 11).PadRight(12)
                + Trunc(node.Agent?.Version ?? "?", 9).PadRight(10)
                + Trunc(kinds, 43).PadRight(44)
                + "--".PadRight(8)
                + Age(node.LastHeartbeatAt));
        }

        Console.WriteLine();
        Console.WriteLine("(hardware facts: inspect <node-ref> — cores/RAM/disk come from the host inventory)");
        return 0;
    }

    private static async Task<int> InspectAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        if (refs.Count == 0)
        {
            Console.Error.WriteLine("usage: inspect <node-ref> [--json]");
            return 2;
        }

        var hosts = services.GetRequiredService<HostService>();
        var detail = await ResolveHostAsync(hosts, refs[0], ct)
            ?? throw new FleetNotFoundException("The host '" + refs[0] + "'");
        var view = detail.Host;
        var node = detail.Node;

        if (options.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(FleetJson.Serialize(detail));
            return 0;
        }

        Console.WriteLine("HOST " + view.NodeRef + "  (" + view.Id + ")");
        Console.WriteLine("  address       " + view.Address + ":" + view.SshPort + (view.Region is null ? "" : "  region=" + view.Region) + (view.Provider is null ? "" : "  provider=" + view.Provider));
        Console.WriteLine("  lifecycle     " + view.Lifecycle + (view.Alert is null ? "" : "  ALERT=" + view.Alert));
        Console.WriteLine("  facts         " + (view.Os ?? "?") + " " + (view.Arch ?? "?") + ", " + (view.CpuCores?.ToString() ?? "?") + " cores, " + (view.MemMib?.ToString() ?? "?") + " MiB RAM, " + (view.DiskGib?.ToString() ?? "?") + " GiB disk");
        Console.WriteLine("  host key      " + (view.HostKeyFingerprint is null ? "not pinned" : view.HostKeyAlgorithm + " " + view.HostKeyFingerprint + " pinned " + Age(view.HostKeyPinnedAt)));
        Console.WriteLine("  agent image   " + (view.AgentDigest ?? "none") + "  desired rev " + view.DesiredRevision + " / applied " + view.AppliedRevision);
        Console.WriteLine("  api node      " + (view.ApiNodeId ?? "none"));
        if (node is not null)
        {
            Console.WriteLine();
            Console.WriteLine("NODE " + node.Id);
            Console.WriteLine("  status/health " + node.Status + " / " + node.Health + "  last heartbeat " + Age(node.LastHeartbeatAt) + ", last claim " + Age(node.LastClaimAt));
            Console.WriteLine("  agent         v" + (node.Agent?.Version ?? "?") + "  protocol " + (node.Agent?.Protocol?.ToString() ?? "?") + "  instance " + (node.Agent?.InstanceId ?? "?"));
            Console.WriteLine("  image         " + (node.Agent?.ImageDigest ?? "?"));
            Console.WriteLine("  kinds offered " + string.Join(",", node.Agent?.Kinds?.Select(k => k.Kind) ?? Array.Empty<string>()));
            if (node.Capacity is not null)
            {
                Console.WriteLine("  capacity      cpuFree " + node.Capacity.CpuBudgetFreeMilli + " mCPU, memFree " + node.Capacity.MemBudgetFreeMiB + " MiB, tmpFree " + node.Capacity.TmpFreeMiB + " MiB, slots free " + node.Capacity.HeavySlotsFree + "/" + (node.Leases?.Max.ToString() ?? "?") + ", concurrency " + node.Capacity.EffectiveConcurrency);
            }

            if (node.Load is not null)
            {
                Console.WriteLine("  load          cpu " + node.Load.CpuPct.ToString("0.#") + "%, memFree " + node.Load.MemFreePct.ToString("0.#") + "%, pressure " + node.Load.Pressure);
            }

            Console.WriteLine("  leases        " + (node.Leases is null ? "?" : node.Leases.Count + " (weight " + node.Leases.Weight + "/" + node.Leases.Max + ")"));
            Console.WriteLine("  policy rev    " + node.Policy?.Revision + "  applied " + node.AppliedRevision + "  allowed kinds " + string.Join(",", node.Policy?.AllowedKinds ?? Array.Empty<string>()));
            Console.WriteLine("  canary        " + (node.LastCanary is null ? "never" : (node.LastCanary.Ok ? "PASS" : "FAIL") + " " + Age(node.LastCanary.At)));
            Console.WriteLine("  integrity     " + node.IntegrityStrikes + " strike(s)  tokens active " + node.Tokens?.ActiveCount + " next expiry " + (node.Tokens?.NextExpiryAt?.ToString("u") ?? "?"));
        }

        var operations = await hosts.ListOperationsAsync(5, view.Id, ct);
        if (operations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("RECENT OPERATIONS");
            foreach (var operation in operations)
            {
                Console.WriteLine("  " + operation.Id + "  " + operation.Kind.PadRight(10) + operation.State.PadRight(14) + Age(operation.UpdatedAt) + (operation.FailureReason is null ? "" : "  " + operation.FailureReason));
                Console.WriteLine("    view with: op " + operation.Id);
            }
        }

        return 0;
    }

    private static async Task<int> OperationsAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var hosts = services.GetRequiredService<HostService>();
        var (refs, options) = SplitArgs(args);
        var take = GetIntOption(options, "--take") ?? 20;
        var list = await hosts.ListOperationsAsync(take, refs.Count > 0 ? (await ResolveHostAsync(hosts, refs[0], ct))?.Host.Id : null, ct);
        if (options.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(FleetJson.Serialize(list));
            return 0;
        }

        Console.WriteLine("OPERATION".PadRight(30) + "KIND".PadRight(10) + "STATE".PadRight(14) + "STEP".PadRight(20) + "UPDATED");
        foreach (var operation in list)
        {
            Console.WriteLine(
                operation.Id.PadRight(30) + operation.Kind.PadRight(10) + operation.State.PadRight(14)
                + Trunc(operation.CurrentStep ?? "-", 19).PadRight(20) + Age(operation.UpdatedAt)
                + (operation.FailureReason is null ? "" : "  " + operation.FailureReason));
        }

        return 0;
    }

    private static async Task<int> OperationAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        if (refs.Count == 0)
        {
            Console.Error.WriteLine("usage: op <operation-id> [--json]");
            return 2;
        }

        var hosts = services.GetRequiredService<HostService>();
        var operation = await hosts.GetOperationAsync(refs[0], ct) ?? throw new FleetNotFoundException("The operation");
        if (options.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(FleetJson.Serialize(operation));
            return 0;
        }

        PrintOperation(operation);
        return 0;
    }

    // ---- enrollment -----------------------------------------------------------------------------

    private static async Task<int> AddAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        string? Get(params string[] names) => GetOption(options, names);
        var host = Get("--host") ?? throw new FleetCliUsage("add needs --host <address>.");
        var nodeRef = Get("--node-ref") ?? refs.FirstOrDefault() ?? throw new FleetCliUsage("add needs --node-ref <name> (or the name as the first argument).");
        var port = GetIntOption(options, "--port") ?? 22;
        var sshUser = Get("--user") ?? "root";
        var displayName = Get("--display-name") ?? nodeRef;
        var region = Get("--region");
        var provider = Get("--provider");
        var fingerprint = Get("--confirm-fingerprint");
        var keyPath = Get("--ssh-key");
        var timeout = TimeSpan.FromSeconds(GetIntOption(options, "--timeout") ?? 3600);

        await StepUpAsync(services, options, ct);

        var enrollment = services.GetRequiredService<EnrollmentService>();
        var hosts = services.GetRequiredService<HostService>();
        Console.Error.WriteLine("Validating inventory and opening the enrollment for " + nodeRef + " …");
        var added = await enrollment.AddHostAsync(
            new AddHostRequest(nodeRef, displayName, host, port, region, provider),
            Actor,
            Guid.NewGuid().ToString("D"),
            ct);

        var operation = added.Operation;
        if (added.AlreadyExisted)
        {
            Console.Error.WriteLine("An enrollment for this host already existed (" + operation.Id + ", " + operation.State + "); continuing it idempotently.");
        }

        // Host-key pin: the operator took the fingerprint out-of-band from the provider console.
        if (operation.State == "HostKeyPending" || operation.State == "Created")
        {
            if (operation.HostKeyCandidates.Count > 0 && fingerprint is null)
            {
                Console.Error.WriteLine("Host key candidates offered by " + host + ":");
                foreach (var candidate in operation.HostKeyCandidates)
                {
                    Console.Error.WriteLine("  " + candidate.Algorithm + "  " + candidate.Fingerprint);
                }

                throw new FleetCliUsage(
                    "Compare the fingerprint above with the VPS provider console OUT OF BAND, then re-run with "
                    + "--confirm-fingerprint <first 12 hex characters>. First contact is not authenticated; never trust a "
                    + "fingerprint shown over the same network path.");
            }

            if (fingerprint is not null)
            {
                Console.Error.WriteLine("Pinning the host key with the operator-confirmed prefix …");
                operation = await enrollment.ConfirmHostKeyAsync(operation.Id, fingerprint, Actor, ct);
            }
        }

        // Owner credential: read once, submit; the manager vault holds it encrypted for at most 60 minutes.
        if (!operation.AwaitingOwnerCredential && operation.State != "HostKeyConfirmed" && operation.State != "Bootstrapping" && operation.State != "Active")
        {
            PrintOperation(operation);
            Console.Error.WriteLine("The operation is in state " + operation.State + "; attach later with: op " + operation.Id);
            return 0;
        }

        var credential = keyPath is not null
            ? await File.ReadAllTextAsync(keyPath, ct)
            : await ReadRemainingStdinAsync(ct);
        if (string.IsNullOrWhiteSpace(credential))
        {
            Console.Error.WriteLine("No owner credential arrived on stdin (private key PEM expected; use --ssh-key PATH instead). The operation is waiting:");
            Console.Error.WriteLine("  re-run the same add command with --ssh-key PATH, or pipe: fleet add … --totp CODE < key.pem");
            Console.Error.WriteLine("  attach: op " + operation.Id);
            return 2;
        }

        Console.Error.WriteLine("Submitting the one-time owner credential (never echoed, never logged) …");
        operation = await enrollment.SetOwnerCredentialAsync(operation.Id, sshUser, credential, null, Actor, ct);

        operation = await PollOperationAsync(hosts, operation.Id, timeout, ct);
        PrintOperation(operation);
        if (operation.State == "Active")
        {
            Console.WriteLine();
            Console.WriteLine("NODE " + nodeRef);
            Console.WriteLine("STATUS: ACTIVE");
            Console.WriteLine("ENROLLMENT: PASS (preflight, bootstrap, image, agent, heartbeat, canary)");
            return 0;
        }

        Console.Error.WriteLine("ENROLLMENT: " + operation.State + (operation.FailureReason is null ? "" : " — " + operation.FailureReason + ": " + operation.FailureDetail));
        Console.Error.WriteLine("Resume safely by re-running the same add command (idempotent), or: op " + operation.Id);
        return 1;
    }

    private static async Task<int> LifecycleAsync(IServiceProvider services, string verb, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        if (refs.Count == 0)
        {
            Console.Error.WriteLine("usage: " + verb + " <node-ref> [--timeout S]");
            return 2;
        }

        await StepUpAsync(services, options, ct);
        var hosts = services.GetRequiredService<HostService>();
        var host = ((await ResolveHostAsync(hosts, refs[0], ct)) ?? throw new FleetNotFoundException("The host '" + refs[0] + "'")).Host;
        var operation = verb switch
        {
            "drain" => await hosts.StartDrainAsync(host.Id, Actor, ct),
            "enable" => await hosts.StartEnableAsync(host.Id, Actor, ct),
            "disable" => await hosts.StartDisableAsync(host.Id, Actor, ct),
            _ => throw new InvalidOperationException(verb),
        };

        Console.Error.WriteLine(verb + " " + host.NodeRef + ": operation " + operation.Id + " (" + operation.State + ")");
        if (verb == "drain")
        {
            Console.Error.WriteLine("New claims stop; in-flight leases finish (or expire and requeue). Watch with: op " + operation.Id);
        }

        operation = await PollOperationAsync(hosts, operation.Id, TimeSpan.FromSeconds(GetIntOption(options, "--timeout") ?? 900), ct);
        PrintOperation(operation);
        return operation.State is "Succeeded" or "Completed" or "Active" ? 0 : 1;
    }

    private static async Task<int> RemoveAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        if (refs.Count == 0)
        {
            Console.Error.WriteLine("usage: remove <node-ref> [--force] [--timeout S]");
            return 2;
        }

        await StepUpAsync(services, options, ct);
        var hosts = services.GetRequiredService<HostService>();
        var host = ((await ResolveHostAsync(hosts, refs[0], ct)) ?? throw new FleetNotFoundException("The host '" + refs[0] + "'")).Host;
        var force = options.Contains("--force", StringComparer.Ordinal);
        if (force)
        {
            Console.Error.WriteLine("FORCED removal: leases expire/requeue, identity revoked, host record deleted. Provider-side destruction stays your action.");
        }

        var operation = await hosts.StartRemoveAsync(host.Id, force, Actor, ct);
        Console.Error.WriteLine("remove " + host.NodeRef + ": operation " + operation.Id + " (" + operation.State + ")");
        operation = await PollOperationAsync(hosts, operation.Id, TimeSpan.FromSeconds(GetIntOption(options, "--timeout") ?? 1800), ct);
        PrintOperation(operation);
        return operation.State is "Succeeded" or "Completed" ? 0 : 1;
    }

    private static async Task<int> RotateTokenAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        if (refs.Count == 0)
        {
            Console.Error.WriteLine("usage: rotate-token <node-ref> [--timeout S]");
            return 2;
        }

        await StepUpAsync(services, options, ct);
        var hosts = services.GetRequiredService<HostService>();
        var host = ((await ResolveHostAsync(hosts, refs[0], ct)) ?? throw new FleetNotFoundException("The host '" + refs[0] + "'")).Host;
        var operation = await hosts.StartRotateTokenAsync(host.Id, Actor, ct);
        Console.Error.WriteLine("token rotation " + host.NodeRef + ": operation " + operation.Id + " (" + operation.State + ")");
        operation = await PollOperationAsync(hosts, operation.Id, TimeSpan.FromSeconds(GetIntOption(options, "--timeout") ?? 600), ct);
        PrintOperation(operation);
        return operation.State is "Succeeded" or "Completed" ? 0 : 1;
    }

    private static async Task<int> UpgradeAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (_, options) = SplitArgs(args);
        var digest = GetOption(options, "--digest") ?? throw new FleetCliUsage("upgrade needs --digest sha256:<64 hex> (an approved release; rolling, halts on the first failed host).");
        await StepUpAsync(services, options, ct);
        var hosts = services.GetRequiredService<HostService>();
        var operation = await hosts.StartRolloutAsync(digest, Actor, ct);
        Console.Error.WriteLine("rolling upgrade to " + digest + ": operation " + operation.Id + " (" + operation.State + ")");
        operation = await PollOperationAsync(hosts, operation.Id, TimeSpan.FromSeconds(GetIntOption(options, "--timeout") ?? 3600), ct);
        PrintOperation(operation);
        return operation.State is "Succeeded" or "Completed" ? 0 : 1;
    }

    private static async Task<int> TestAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        if (refs.Count == 0)
        {
            Console.Error.WriteLine("usage: test <node-ref> [--timeout S]");
            return 2;
        }

        var hosts = services.GetRequiredService<HostService>();
        var api = services.GetRequiredService<IFleetApi>();
        var host = ((await ResolveHostAsync(hosts, refs[0], ct)) ?? throw new FleetNotFoundException("The host '" + refs[0] + "'")).Host;
        if (host.ApiNodeId is null)
        {
            Console.Error.WriteLine(host.NodeRef + " has no API node yet (enrollment incomplete); nothing to test.");
            return 2;
        }

        var before = await api.GetNodeAsync(host.ApiNodeId, ct);
        var startedAt = before?.LastCanary?.At;
        var jobId = await api.EnqueueCanaryAsync(host.ApiNodeId, ct);
        Console.Error.WriteLine("Known-answer canary queued as job " + jobId + "; waiting for the node to claim, execute and report …");

        var deadline = TimeSpan.FromSeconds(GetIntOption(options, "--timeout") ?? 240);
        var start = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - start < deadline)
        {
            await FleetCli.DelayAsync(TimeSpan.FromSeconds(3), ct);
            var job = await api.GetJobAsync(jobId, ct);
            var state = job is { ValueKind: JsonValueKind.Object } jobElement ? (Str(jobElement, "state", "State") ?? "?") : null;
            var node = await api.GetNodeAsync(host.ApiNodeId, ct);
            var canary = node?.LastCanary;
            if (canary is not null && (startedAt is null || canary.At > startedAt))
            {
                Console.WriteLine("NODE " + host.NodeRef);
                Console.WriteLine("SMOKE TEST: " + (canary.Ok ? "PASS" : "FAIL") + "  (job " + jobId + ", reported " + Age(canary.At) + ")");
                return canary.Ok ? 0 : 1;
            }

            if (state is "Failed" or "Quarantined" or "Cancelled")
            {
                Console.WriteLine("NODE " + host.NodeRef);
                Console.WriteLine("SMOKE TEST: FAIL  (job " + jobId + " reached " + state + ")");
                return 1;
            }
        }

        Console.WriteLine("NODE " + host.NodeRef);
        Console.WriteLine("SMOKE TEST: TIMEOUT after " + (int)deadline.TotalSeconds + "s (job " + jobId + " still open; inspect: job " + jobId + ")");
        return 3;
    }

    // ---- policy ---------------------------------------------------------------------------------

    private static async Task<int> PolicyAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args[1..]);
        var policies = services.GetRequiredService<PolicyService>();
        var hosts = services.GetRequiredService<HostStore>();

        if (args[0] == "show")
        {
            var globalPolicy = await policies.GetGlobalAsync(ct);
            if (refs.Count == 0)
            {
                Console.WriteLine(options.Contains("--json", StringComparer.Ordinal)
                    ? FleetJson.Serialize(globalPolicy)
                    : RenderPolicy("GLOBAL (default for hosts without an override)", globalPolicy));
                return 0;
            }

            var host = await hosts.GetAsync((await ResolveHostAsync(services.GetRequiredService<HostService>(), refs[0], ct))?.Host.Id ?? refs[0], ct)
                ?? throw new FleetNotFoundException("The host");
            var overridePolicy = await policies.GetHostOverrideAsync(host.Id, ct);
            var effective = await policies.EffectiveAsync(host, ct);
            if (options.Contains("--json", StringComparer.Ordinal))
            {
                Console.WriteLine(FleetJson.Serialize(new { global = globalPolicy, hostOverride = overridePolicy, effective }));
                return 0;
            }

            Console.WriteLine(RenderPolicy("GLOBAL", globalPolicy));
            Console.WriteLine(RenderPolicy("HOST OVERRIDE for " + host.NodeRef, overridePolicy));
            Console.WriteLine(RenderPolicy("EFFECTIVE for " + host.NodeRef, effective));
            return 0;
        }

        if (args[0] == "set")
        {
            if (refs.Count == 0)
            {
                throw new FleetCliUsage("policy set needs a node-ref: policy set <node-ref> --kinds a,b,c [--max-concurrency N]");
            }

            await StepUpAsync(services, options, ct);
            var hostService = services.GetRequiredService<HostService>();
            var api = services.GetRequiredService<IFleetApi>();
            var detail = (await ResolveHostAsync(hostService, refs[0], ct)) ?? throw new FleetNotFoundException("The host '" + refs[0] + "'");
            var entity = await hosts.GetAsync(detail.Host.Id, ct) ?? throw new FleetNotFoundException("The host");
            var effective = await policies.EffectiveAsync(entity, ct);

            var kinds = GetOption(options, "--kinds")?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var registry = await api.GetStatusAsync(ct);
            var maxConcurrency = GetIntOption(options, "--max-concurrency") ?? effective.MaxConcurrency;

            var perKind = new Dictionary<string, int>(effective.PerKind, StringComparer.Ordinal);
            if (kinds is not null)
            {
                foreach (var removed in perKind.Keys.Where(k => !kinds.Contains(k, StringComparer.Ordinal)).ToList())
                {
                    perKind.Remove(removed);
                }

                foreach (var addedKind in kinds.Where(k => !perKind.ContainsKey(k)))
                {
                    perKind[addedKind] = maxConcurrency;
                }
            }

            var updated = effective with
            {
                AllowedKinds = kinds ?? effective.AllowedKinds,
                MaxConcurrency = maxConcurrency,
                PerKind = perKind,
            };
            PolicyValidator.EnsureValid(updated, registry.Kinds.Count > 0 ? registry.Kinds : null);
            await policies.SetHostOverrideAsync(entity.Id, updated, Actor, ct);
            var revision = await policies.PushAsync(entity, ct);
            Console.WriteLine("Policy for " + entity.NodeRef + " updated and pushed (revision " + (revision > 0 ? revision.ToString() : "unchanged") + "):");
            Console.WriteLine(RenderPolicy("EFFECTIVE", updated));
            Console.WriteLine("The agent applies it within one poll cycle (≤30 s); verify with: inspect " + entity.NodeRef);
            return 0;
        }

        Console.Error.WriteLine("usage: policy show [node-ref] | policy set <node-ref> --kinds a,b,c [--max-concurrency N]");
        return 2;
    }

    // ---- jobs -----------------------------------------------------------------------------------

    private static async Task<int> JobsAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var api = services.GetRequiredService<IFleetApi>();
        var (_, options) = SplitArgs(args);
        var state = GetOption(options, "--state");
        var kind = GetOption(options, "--kind");
        var nodeRef = GetOption(options, "--node");
        string? nodeId = null;
        if (nodeRef is not null)
        {
            var hosts = services.GetRequiredService<HostService>();
            nodeId = (await ResolveHostAsync(hosts, nodeRef, ct))?.Host.ApiNodeId ?? nodeRef;
        }

        var take = GetIntOption(options, "--take") ?? 50;
        var jobs = await api.GetJobsAsync(state, kind, nodeId, take, ct);
        if (options.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(FleetJson.Serialize(jobs));
            return 0;
        }

        foreach (var job in JsonArray(jobs))
        {
            Console.WriteLine(
                (Str(job, "id", "Id") ?? "?").PadRight(31)
                + (Str(job, "kind", "Kind") ?? "?").PadRight(26)
                + (Str(job, "state", "State") ?? "?").PadRight(14)
                + ("a" + (Int(job, "attempt", "Attempt")?.ToString() ?? "?")).PadRight(5)
                + Trunc(Str(job, "leaseOwner", "LeaseOwner") ?? "—", 26).PadRight(27)
                + (Str(job, "failureCode", "FailureCode") ?? ""));
        }

        Console.WriteLine();
        Console.WriteLine("usage reminder: jobs --state Failed (dead-letter), --kind media.audio-extract, --node <ref>, --take N, --json");
        return 0;
    }

    private static async Task<int> JobAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        if (refs.Count == 0)
        {
            Console.Error.WriteLine("usage: job <job-id> [--json]");
            return 2;
        }

        var job = await services.GetRequiredService<IFleetApi>().GetJobAsync(refs[0], ct)
            ?? throw new FleetNotFoundException("The job");
        if (options.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(FleetJson.Serialize(job));
            return 0;
        }

        foreach (var property in job.EnumerateObject())
        {
            Console.WriteLine("  " + property.Name.PadRight(22) + (property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString()));
        }

        Console.WriteLine();
        Console.WriteLine("actions: requeue " + refs[0] + " | force-local " + refs[0] + " | cancel " + refs[0] + "  (each needs a fresh TOTP)");
        return 0;
    }

    private static async Task<int> JobActionAsync(IServiceProvider services, string action, string[] args, CancellationToken ct)
    {
        var (refs, options) = SplitArgs(args);
        if (refs.Count == 0)
        {
            Console.Error.WriteLine("usage: " + action + " <job-id>");
            return 2;
        }

        await StepUpAsync(services, options, ct);
        await services.GetRequiredService<IFleetApi>().PostJobActionAsync(action, refs[0], ct);
        Console.WriteLine(action + " accepted for job " + refs[0] + "; watch with: job " + refs[0]);
        return 0;
    }

    // ---- rebalance ------------------------------------------------------------------------------

    private static async Task<int> RebalanceAsync(IServiceProvider services, string[] args, CancellationToken ct)
    {
        // The fleet is worker-pull: there is nothing to move. This verb reports the balance the
        // scheduler will produce right now: per kind, queue depth vs eligible capacity.
        var api = services.GetRequiredService<IFleetApi>();
        var stats = await api.GetStatsAsync(ct);
        var nodes = await api.ListNodesAsync(ct);
        var status = await api.GetStatusAsync(ct);

        Console.WriteLine("WORKER-PULL BALANCE REPORT (claims are atomic; nothing to rebalance by hand)");
        Console.WriteLine();
        foreach (var kind in status.Kinds)
        {
            var queued = 0;
            var leased = 0;
            if (stats.TryGetProperty("queue", out var queue) && queue.TryGetProperty(kind, out var states) && states.ValueKind == JsonValueKind.Object)
            {
                foreach (var stateEntry in states.EnumerateObject())
                {
                    if (stateEntry.Name == "Queued")
                    {
                        queued = stateEntry.Value.GetInt32();
                    }

                    if (stateEntry.Name == "Leased")
                    {
                        leased = stateEntry.Value.GetInt32();
                    }
                }
            }

            var eligible = nodes
                .Where(n => n.Status == "Active"
                            && n.Policy?.AllowedKinds?.Contains(kind, StringComparer.Ordinal) == true
                            && n.Agent?.Kinds?.Any(k => k.Kind == kind) == true)
                .ToList();
            var freeSlots = eligible.Sum(n => Math.Max(0, n.Capacity?.HeavySlotsFree ?? 0));
            var verdict = queued == 0
                ? "queue empty"
                : eligible.Count == 0
                    ? "NO ELIGIBLE NODE — jobs fall back to the primary (10 min soft, 60 min hard)"
                    : "capacity available: " + freeSlots + " free slot(s) on " + eligible.Count + " node(s)";
            Console.WriteLine(
                kind.PadRight(28) + ("queued " + queued).PadRight(12) + ("leased " + leased).PadRight(12)
                + Trunc(string.Join(",", eligible.Select(n => n.NodeRef)), 40).PadRight(41) + verdict);
        }

        Console.WriteLine();
        Console.WriteLine("capacity per node (free/total slots, weight-included):");
        foreach (var node in nodes)
        {
            Console.WriteLine(
                "  " + Trunc(node.NodeRef, 24).PadRight(25) + node.Status.PadRight(10)
                + (node.Capacity is null ? "?" : node.Capacity.HeavySlotsFree + " free, concurrency " + node.Capacity.EffectiveConcurrency + ", leases w" + (node.Leases?.Weight ?? 0) + "/w" + (node.Leases?.Max ?? 0)));
        }

        return 0;
    }

    // ---- shared helpers -------------------------------------------------------------------------

    /// <summary>The TOTP step-up the dashboard's StepUpFilter enforces for privileged routes.</summary>
    private static async Task StepUpAsync(IServiceProvider services, HashSet<string> options, CancellationToken ct)
    {
        var code = GetOption(options, "--totp");
        if (code is null && !Console.IsInputRedirected)
        {
            Console.Error.Write("Authenticator code (a code can be used once): ");
            code = Console.ReadLine()?.Trim();
        }
        else if (code is null)
        {
            var line = await ReadLineAsync(Console.OpenStandardInput(), ct);
            code = line?.Trim();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new FleetCliUsage("This verb is privileged: pass --totp CODE or a fresh code on the first stdin line.");
        }

        var owner = services.GetRequiredService<OwnerAccountService>();
        if (!await owner.VerifyStepUpAsync(code, ct))
        {
            throw new FleetOperationException("step_up_failed", "The authenticator code was rejected (used, expired or wrong). Codes are single-use; wait for the next one.");
        }
    }

    private static async Task<OperationView> PollOperationAsync(
        HostService hosts, string operationId, TimeSpan timeout, CancellationToken ct)
    {
        var start = DateTimeOffset.UtcNow;
        var printedStep = "";
        OperationView operation = await hosts.GetOperationAsync(operationId, ct);
        while (operation.State is not ("Active" or "Succeeded" or "Completed" or "Failed" or "Cancelled"))
        {
            if (DateTimeOffset.UtcNow - start > timeout)
            {
                Console.Error.WriteLine("Timed out watching the operation (it keeps running durably). Attach again with: op " + operationId);
                return operation;
            }

            await FleetCli.DelayAsync(TimeSpan.FromSeconds(3), ct);
            operation = await hosts.GetOperationAsync(operationId, ct);
            var marker = operation.CurrentStep + "#" + operation.StepAttempt;
            if (marker != printedStep)
            {
                printedStep = marker;
                Console.Error.WriteLine("  … " + operation.State + " / " + (operation.CurrentStep ?? "…"));
            }
        }

        return operation;
    }

    private static void PrintOperation(OperationView operation)
    {
        Console.WriteLine("OPERATION " + operation.Id + "  kind=" + operation.Kind + "  state=" + operation.State
            + (operation.FailureReason is null ? "" : "  failure=" + operation.FailureReason + ": " + operation.FailureDetail));
        foreach (var step in operation.Steps)
        {
            Console.WriteLine(
                "  [S" + step.Seq + "] " + step.Name.PadRight(22) + step.State.PadRight(12)
                + (step.ExitCode is null ? "" : "exit " + step.ExitCode + " ")
                + (step.Summary is null ? "" : " " + Trunc(step.Summary, 90)));
        }
    }

    private static async Task<HostDetail?> ResolveHostAsync(HostService hosts, string reference, CancellationToken ct)
    {
        var all = await hosts.ListHostsAsync(ct);
        var host = all.FirstOrDefault(h => string.Equals(h.NodeRef, reference, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(h => string.Equals(h.Id, reference, StringComparison.Ordinal))
            ?? all.FirstOrDefault(h => h.ApiNodeId == reference)
            ?? (reference.Length >= 4
                ? all.FirstOrDefault(h => h.NodeRef.StartsWith(reference, StringComparison.OrdinalIgnoreCase))
                : null);
        if (host is null)
        {
            return null;
        }

        return await hosts.GetHostAsync(host.Id, includeNode: true, ct);
    }

    private static ApiNode? nodeById(IReadOnlyList<ApiNode> nodes, string? id) =>
        id is null ? null : nodes.FirstOrDefault(n => n.Id == id);

    private static string RenderPolicy(string title, NodePolicy? policy)
    {
        if (policy is null)
        {
            return title + ": (none)";
        }

        return title + "\n"
            + "  kinds          " + string.Join(",", policy.AllowedKinds) + "\n"
            + "  concurrency    max " + policy.MaxConcurrency + "  per-kind {" + string.Join(",", policy.PerKind.Select(kv => kv.Key + "=" + kv.Value)) + "}\n"
            + "  budgets        cpu " + policy.Budgets.CpuMilli + " mCPU, mem " + policy.Budgets.MemMiB + " MiB, tmp " + policy.Budgets.TmpMiB + " MiB\n"
            + "  pressure       reduce >" + policy.Pressure.ReduceCpuPct + "% cpu / <" + policy.Pressure.ReduceMemFreePct + "% memFree, restore <" + policy.Pressure.RestoreCpuPct + "% / >" + policy.Pressure.RestoreMemFreePct + "% after " + policy.Pressure.RestoreAfterSeconds + "s\n"
            + "  poll           idle " + policy.PollSeconds.Idle + "s (min " + policy.PollSeconds.Min + ", max " + policy.PollSeconds.Max + ")\n"
            + "  agent image    min v" + policy.AgentImage.MinVersion + ", target " + (policy.AgentImage.Target ?? "none") + ", approved " + policy.AgentImage.ApprovedDigests.Count + " digest(s)";
    }

    private static (List<string> Refs, HashSet<string> Options) SplitArgs(string[] args)
    {
        var refs = new List<string>();
        var options = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                options.Add(args[i]);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) && !IsValueless(args[i]))
                {
                    options.Add(args[i] + "=" + args[i + 1]);
                    i++;
                }
            }
            else
            {
                refs.Add(args[i]);
            }
        }

        return (refs, options);
    }

    private static bool IsValueless(string flag) =>
        flag is "--json" or "--force" or "--reset";

    private static string? GetOption(HashSet<string> options, params string[] names)
    {
        foreach (var name in names)
        {
            var match = options.FirstOrDefault(o => o.StartsWith(name + "=", StringComparison.Ordinal));
            if (match is not null)
            {
                return match[(name.Length + 1)..];
            }
        }

        return null;
    }

    private static int? GetIntOption(HashSet<string> options, params string[] names) =>
        GetOption(options, names) is { } raw && int.TryParse(raw, out var value) ? value : null;

    private static IReadOnlyList<JsonElement> JsonArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().ToList();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "items", "nodes", "jobs" })
            {
                if (element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    return array.EnumerateArray().ToList();
                }
            }
        }

        return Array.Empty<JsonElement>();
    }

    private static string? Str(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int? Int(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static string Age(DateTimeOffset? at) => at is null ? "—" : HumanSeconds((int)(DateTimeOffset.UtcNow - at.Value).TotalSeconds) + " ago";

    private static string HumanSeconds(int seconds) => seconds switch
    {
        < 60 => seconds + "s",
        < 3600 => (seconds / 60) + "m " + (seconds % 60) + "s",
        < 86400 => (seconds / 3600) + "h " + ((seconds % 3600) / 60) + "m",
        _ => (seconds / 86400) + "d " + ((seconds % 86400) / 3600) + "h",
    };

    private static string Trunc(string? value, int width)
    {
        value ??= "";
        return value.Length <= width ? value : value[..(width - 1)] + "…";
    }

    /// <summary>Reads the rest of stdin as the owner credential; buffered once so prompts never mix with it.</summary>
    private static async Task<string> ReadRemainingStdinAsync(CancellationToken ct)
    {
        if (!Console.IsInputRedirected)
        {
            Console.Error.Write("Paste the owner SSH private key, then Ctrl-D/Ctrl-Z+Enter: ");
        }

        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        var buffer = new StringBuilder();
        var line = await reader.ReadLineAsync(ct);
        if (line is not null)
        {
            buffer.AppendLine(line);
        }

        var rest = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(rest, ct)) > 0)
        {
            buffer.Append(rest, 0, read);
        }

        return buffer.ToString();
    }

    private static async Task<string?> ReadLineAsync(System.IO.Stream stream, CancellationToken ct)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one, ct) > 0)
        {
            if (one[0] == (byte)'\n')
            {
                break;
            }

            buffer.Add(one[0]);
            if (buffer.Count > 32)
            {
                throw new FleetCliUsage("The first stdin line must be just the authenticator code.");
            }
        }

        var text = Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r');
        if (text.Length == 0 && !Console.IsInputRedirected)
        {
            return null;
        }

        return text.Length == 0 ? null : text;
    }

    /// <summary>Usage or precondition error: message on stderr, exit code 2.</summary>
    public sealed class FleetCliUsage : Exception
    {
        public FleetCliUsage(string message) : base(message) { }
    }
}
