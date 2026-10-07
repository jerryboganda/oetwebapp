using System.Text.Json;
using Fleet.Core.Domain;
using Fleet.Core.Policy;
using Fleet.Core.Ssh;
using Fleet.Manager.Api;
using Fleet.Manager.Persistence;
using Fleet.Manager.Projects;
using Fleet.Manager.Vault;

namespace Fleet.Manager.Operations;

/// <summary>The ordered step names of every operation kind. Steps are created up front as <c>pending</c>.</summary>
public static class StepPlans
{
    public static IReadOnlyList<string> Enroll() => EnrollSteps.All.Select(EnrollSteps.Name).ToList();

    /// <summary>Repair re-runs the root-level steps S1..S8 with a fresh, 60-minute owner credential.</summary>
    public static IReadOnlyList<string> Repair() =>
        EnrollSteps.All.Where(s => (int)s <= (int)EnrollStep.DiscardOwnerKey).Select(EnrollSteps.Name).ToList();

    public static IReadOnlyList<string> Drain() => new[] { "api-drain", "wait-leases" };

    public static IReadOnlyList<string> Disable() => new[] { "api-disable" };

    public static IReadOnlyList<string> Enable() => new[] { "ensure-canary", "api-enable" };

    public static IReadOnlyList<string> Remove() =>
        new[] { "api-drain", "wait-leases-strict", "uninstall", "api-revoke", "destroy-credentials", "finalize-remove" };

    public static IReadOnlyList<string> RotateToken() =>
        new[] { "rotate-and-render", "restart-agent", "verify-heartbeat", "finalize-rotate" };

    /// <summary>One block per host, strictly in order: a failure halts the operation and leaves later hosts untouched.</summary>
    public static IReadOnlyList<string> Rollout(IEnumerable<string> nodeRefs)
    {
        var steps = new List<string>();
        foreach (var nodeRef in nodeRefs)
        {
            foreach (var name in new[] { "roll-drain", "roll-wait", "roll-image", "roll-run", "roll-verify", "roll-enable", "roll-canary" })
            {
                steps.Add(name + ":" + nodeRef);
            }
        }

        return steps;
    }
}

// Drain, disable, enable, remove, token rotation, rolling update. Every one of these is a sequence of
// idempotent steps over the API and the restricted oet-fleet-ctl; "remove" and "uninstall" touch ONLY
// components this fleet installed (the agent container, agent-repository images, /etc/oet-fleet, the
// systemd unit, the oetfleet account). Docker itself, the firewall, other containers, images and volumes are never touched.

public sealed partial class StepExecutor
{
    public async Task<StepOutcome> ExecuteMaintenanceStepAsync(StepContext ctx, string stepName, CancellationToken cancellationToken)
    {
        var colon = stepName.IndexOf(':');
        var baseName = colon < 0 ? stepName : stepName[..colon];
        var nodeRef = colon < 0 ? null : stepName[(colon + 1)..];

        if (EnrollSteps.TryParse(stepName, out var enrollStep) && (int)enrollStep <= (int)EnrollStep.DiscardOwnerKey)
        {
            return await ExecuteEnrollStepAsync(ctx, enrollStep, cancellationToken);
        }

        if (nodeRef is not null)
        {
            var target = await _hosts.FindByNodeRefAsync(nodeRef, cancellationToken);
            if (target is null)
            {
                return StepOutcome.Fail(FailureReasons.InternalError, "rollout target host no longer exists", "host missing");
            }

            ctx.Host = target;
        }
        else if (ctx.Op.HostId is not null)
        {
            await FreshHostAsync(ctx, cancellationToken);
        }

        switch (baseName)
        {
            case "api-drain":
            case "roll-drain":
                return await ApiDrainAsync(ctx, cancellationToken);
            case "wait-leases":
            case "roll-wait":
                return await WaitLeasesAsync(ctx, strict: false, cancellationToken);
            case "wait-leases-strict":
                return await WaitLeasesAsync(ctx, strict: true, cancellationToken);
            case "api-disable":
                return await ApiDisableAsync(ctx, cancellationToken);
            case "ensure-canary":
                return await EnsureCanaryAsync(ctx, cancellationToken);
            case "api-enable":
            case "roll-enable":
                return await ApiEnableAsync(ctx, cancellationToken);
            case "uninstall":
                return await UninstallAsync(ctx, cancellationToken);
            case "api-revoke":
                return await ApiRevokeAsync(ctx, cancellationToken);
            case "destroy-credentials":
                return await DestroyCredentialsAsync(ctx, cancellationToken);
            case "finalize-remove":
                return await FinalizeRemoveAsync(ctx, cancellationToken);
            case "rotate-and-render":
                return await RotateAndRenderAsync(ctx, cancellationToken);
            case "restart-agent":
                return await RestartAgentAsync(ctx, cancellationToken);
            case "verify-heartbeat":
                return await VerifyHeartbeatAsync(ctx, cancellationToken);
            case "finalize-rotate":
                return await FinalizeRotateAsync(ctx, cancellationToken);
            case "roll-image":
                return await RollImageAsync(ctx, cancellationToken);
            case "roll-run":
                return await RollRunAsync(ctx, cancellationToken);
            case "roll-verify":
                return await RollVerifyAsync(ctx, cancellationToken);
            case "roll-canary":
                return await RollCanaryAsync(ctx, cancellationToken);
            default:
                return StepOutcome.Fail(FailureReasons.InternalError, "unknown step " + baseName, "unknown step");
        }
    }

    private static bool ParamBool(OperationEntity op, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(op.ParamsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ParamString(OperationEntity op, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(op.ParamsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<(ApiNode? Node, StepOutcome? Failure)> LoadNodeAsync(HostEntity host, CancellationToken cancellationToken)
    {
        if (host.ApiNodeId is null)
        {
            return (null, null);
        }

        try
        {
            return (await _api.GetNodeAsync(host.ApiNodeId, cancellationToken), null);
        }
        catch (FleetApiException ex)
        {
            return (null, FromApi(ex, FailureReasons.InternalError, "the API could not be read"));
        }
    }

    private async Task<StepOutcome> ApiDrainAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        var (node, failure) = await LoadNodeAsync(host, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (node is null)
        {
            return StepOutcome.Skipped("the host has no API node to drain");
        }

        // The API drains only an Active node (OET-RWP/1 section 3.9). Every other status either is already out of the claim path
        // (Draining, Disabled, Quarantined, Revoked) or never entered it (Pending, Probation: a failed or half-finished enrollment),
        // so there is nothing to drain and the removal must go on to uninstall and revoke instead of failing on a 409.
        if (node.Status is "Draining" or "Disabled" or "Quarantined" or "Revoked" or "Pending" or "Probation")
        {
            await _hosts.UpdateAsync(host.Id, h => h.Lifecycle = node.Status == "Draining" ? nameof(HostLifecycle.Draining) : h.Lifecycle, cancellationToken);
            var neverActive = node.Status is "Pending" or "Probation";
            return StepOutcome.Skipped(neverActive
                ? "the node never became active; there is nothing to drain"
                : "the node is already " + node.Status);
        }

        try
        {
            await _api.DrainAsync(node.Id, cancellationToken);
        }
        catch (FleetApiException ex)
        {
            return FromApi(ex, FailureReasons.InternalError, "drain was refused");
        }

        await _hosts.UpdateAsync(host.Id, h => h.Lifecycle = nameof(HostLifecycle.Draining), cancellationToken);
        return StepOutcome.Done("the node is draining");
    }

    /// <summary>
    /// Waits until the node holds no leases. A normal drain or rollout proceeds after the wait window
    /// (the leases then expire or are reclaimed by the API); removal is strict and fails with
    /// <c>drain_timeout</c> instead of pulling a host that is still working.
    /// </summary>
    private async Task<StepOutcome> WaitLeasesAsync(StepContext ctx, bool strict, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        if (host.ApiNodeId is null)
        {
            return StepOutcome.Skipped("no API node");
        }

        var (satisfied, last) = await PollNodeAsync(
            host.ApiNodeId,
            TimeSpan.FromSeconds(Timing.DrainWaitSeconds),
            node => (node.Leases?.Count ?? 0) == 0,
            cancellationToken);
        if (satisfied)
        {
            return StepOutcome.Done("no leases remain");
        }

        var remaining = last?.Leases?.Count ?? 0;
        return strict
            ? StepOutcome.Fail(FailureReasons.DrainTimeout, remaining + " lease(s) were still held after the wait window", "drain timed out")
            : StepOutcome.Done(remaining + " lease(s) still held after the wait window; continuing");
    }

    private async Task<StepOutcome> ApiDisableAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        var (node, failure) = await LoadNodeAsync(host, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (node is null)
        {
            await _hosts.UpdateAsync(host.Id, h => h.Lifecycle = nameof(HostLifecycle.Disabled), cancellationToken);
            return StepOutcome.Skipped("the host has no API node");
        }

        if (node.Status is not ("Disabled" or "Quarantined" or "Revoked"))
        {
            try
            {
                await _api.DisableAsync(node.Id, cancellationToken);
            }
            catch (FleetApiException ex)
            {
                return FromApi(ex, FailureReasons.InternalError, "disable was refused");
            }
        }

        await _hosts.UpdateAsync(host.Id, h => h.Lifecycle = nameof(HostLifecycle.Disabled), cancellationToken);
        return StepOutcome.Done("the node is disabled");
    }

    private async Task<StepOutcome> EnsureCanaryAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        var (node, failure) = await LoadNodeAsync(host, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (node is null || node.Status is "Active" or "Draining" or "Disabled")
        {
            return StepOutcome.Skipped("no canary is required from this status");
        }

        // Probation or Quarantined: release needs a fresh passing canary (OET-RWP/1 section 7.1).
        return await CanaryAsync(ctx, cancellationToken);
    }

    private async Task<StepOutcome> ApiEnableAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        if (host.ApiNodeId is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the host has no API node", "no API node");
        }

        try
        {
            var node = await _api.GetNodeAsync(host.ApiNodeId, cancellationToken);
            if (node is not { Status: "Active" })
            {
                await _api.EnableAsync(host.ApiNodeId, cancellationToken);
            }
        }
        catch (FleetApiException ex) when (ex.Code == "canary_required")
        {
            return StepOutcome.Fail(FailureReasons.CanaryMismatch, "the API requires a passing canary before enabling", "canary required");
        }
        catch (FleetApiException ex)
        {
            return FromApi(ex, FailureReasons.InternalError, "enable was refused");
        }

        await _hosts.UpdateAsync(host.Id, h => h.Lifecycle = nameof(HostLifecycle.Active), cancellationToken);
        return StepOutcome.Done("the node is active");
    }

    /// <summary>
    /// Removes ONLY fleet-owned components from the helper through the restricted <c>uninstall</c> verb. A
    /// host that cannot be reached is an error unless the owner chose <c>force</c> (the VPS is gone).
    /// </summary>
    private async Task<StepOutcome> UninstallAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        if (host.HostKeyAlgo is null)
        {
            return StepOutcome.Skipped("the host never completed enrollment; nothing was installed");
        }

        // The verb removes everything fleet-owned and closes the manager's own login LAST. Record the attempt before sending it, so that
        // after a lost response, a timeout or a crash the retry can tell "never ran" (host still reachable) from "already ran" (login refused).
        var alreadyAttempted = ctx.Data.UninstallRequested;
        if (!alreadyAttempted)
        {
            ctx.Data.UninstallRequested = true;
            await _operations.SaveDataAsync(ctx.Op.Id, ctx.Data, cancellationToken);
        }

        var result = await _access.CtlAsync(host, "uninstall", Array.Empty<string>(), null, cancellationToken);
        if (result.Success)
        {
            return StepOutcome.Done("fleet components removed from the helper");
        }

        // ExitCode -1 means the manager never reached the helper (no key in the vault); only a REFUSED login after an earlier attempt counts.
        if (alreadyAttempted && result.FailureReason == FailureReasons.AuthFailed && result.ExitCode != -1)
        {
            return StepOutcome.Skipped("the helper no longer accepts the manager key: an earlier uninstall already removed it");
        }

        if (ParamBool(ctx.Op, "force"))
        {
            return StepOutcome.Skipped("the helper could not be cleaned (forced removal)");
        }

        return FromCtl(result, FailureReasons.BootstrapStepFailed, "the helper could not be cleaned");
    }

    private async Task<StepOutcome> ApiRevokeAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        if (host.ApiNodeId is null)
        {
            return StepOutcome.Skipped("the host has no API node");
        }

        try
        {
            await _api.RevokeAsync(host.ApiNodeId, cancellationToken);
        }
        catch (FleetApiException ex) when (ex.Code == "node_not_found")
        {
            return StepOutcome.Skipped("the API node no longer exists");
        }
        catch (FleetApiException ex) when (ex.Code == "invalid_transition" && ex.Reason == "Revoked")
        {
            return StepOutcome.Skipped("the API node is already revoked");
        }
        catch (FleetApiException ex)
        {
            return FromApi(ex, FailureReasons.InternalError, "revoke was refused");
        }

        return StepOutcome.Done("the API node is revoked and its credentials are dead");
    }

    private async Task<StepOutcome> DestroyCredentialsAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        var removed = await _credentials.DestroyAllForHostAsync(host.Id, cancellationToken);
        return StepOutcome.Done(removed + " vault record(s) destroyed");
    }

    private async Task<StepOutcome> FinalizeRemoveAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        await _hosts.UpdateAsync(
            host.Id,
            h =>
            {
                h.Lifecycle = nameof(HostLifecycle.Removed);
                h.Alert = null;
            },
            cancellationToken);
        await _audit.AppendAsync("system", "host.removed", host.NodeRef, null, cancellationToken);
        return StepOutcome.Done("the host is removed");
    }

    // ---- token rotation --------------------------------------------------------------------

    /// <summary>
    /// Mint a new node token (the old one keeps working for the grace period), render it into the helper's
    /// env file over ssh stdin. One step on purpose: the token lives only in this method's memory.
    /// </summary>
    private async Task<StepOutcome> RotateAndRenderAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        var digest = host.AgentDigest;
        if (host.ApiNodeId is null || digest is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the host has no running agent to rotate", "nothing to rotate");
        }

        ApiToken token;
        try
        {
            token = await _api.RotateTokenAsync(host.ApiNodeId, Timing.RotationGraceSeconds, Timing.TokenTtlDays, cancellationToken);
        }
        catch (FleetApiException ex)
        {
            return FromApi(ex, FailureReasons.TokenRenderFailed, "token rotation was refused");
        }

        var policy = await _policies.EffectiveAsync(host, cancellationToken);
        string envText;
        try
        {
            envText = AgentEnv.Render(BuildAgentEnv(host.ApiNodeId, token.Value, digest, policy));
        }
        catch (ArgumentException)
        {
            return StepOutcome.Fail(FailureReasons.TokenRenderFailed, "the agent environment could not be rendered", "env render failed");
        }

        var putEnv = await _access.CtlAsync(host, "put-env", Array.Empty<string>(), envText, cancellationToken);
        if (!putEnv.Success)
        {
            return FromCtl(putEnv, FailureReasons.TokenRenderFailed, "env file could not be written");
        }

        ctx.Data.NewTokenFingerprint = TokenFingerprint(token.Value);
        await _operations.SaveDataAsync(ctx.Op.Id, ctx.Data, cancellationToken);
        return StepOutcome.Done("a new token was rendered; the old one expires after the grace period");
    }

    /// <summary>
    /// The ctl <c>restart</c> verb RECREATES the container from the env file (a plain <c>docker restart</c> would keep the environment it was
    /// created with, i.e. the OLD token). The id of the agent instance running now is remembered first, so the next step can tell a new
    /// process from the old one that keeps heartbeating on the old token during its grace period.
    /// </summary>
    private async Task<StepOutcome> RestartAgentAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        if (ctx.Data.PreviousAgentInstanceId is null && host.ApiNodeId is not null)
        {
            try
            {
                ctx.Data.PreviousAgentInstanceId = (await _api.GetNodeAsync(host.ApiNodeId, cancellationToken))?.Agent?.InstanceId;
            }
            catch (FleetApiException)
            {
                // Best effort: without it the next step falls back to the heartbeat time alone.
            }
        }

        ctx.Data.RestartRequestedAt = _time.GetUtcNow();
        await _operations.SaveDataAsync(ctx.Op.Id, ctx.Data, cancellationToken);
        var result = await _access.CtlAsync(host, "restart", Array.Empty<string>(), null, cancellationToken);
        return result.Success
            ? StepOutcome.Done("the agent was recreated from the new environment file")
            : FromCtl(result, FailureReasons.AgentStartFailed, "the agent could not be restarted");
    }

    private async Task<StepOutcome> VerifyHeartbeatAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        if (host.ApiNodeId is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the host has no API node", "no API node");
        }

        var since = (ctx.Data.RestartRequestedAt ?? _time.GetUtcNow()) - TimeSpan.FromSeconds(1);
        var previousInstance = ctx.Data.PreviousAgentInstanceId;
        var (satisfied, _) = await PollNodeAsync(
            host.ApiNodeId,
            TimeSpan.FromSeconds(Timing.HeartbeatAfterRestartSeconds),
            node => node.LastHeartbeatAt is { } heartbeat
                && heartbeat >= since
                && (previousInstance is null
                    || (node.Agent?.InstanceId is { } current && !string.Equals(current, previousInstance, StringComparison.Ordinal))),
            cancellationToken);
        return satisfied
            ? StepOutcome.Done("a new agent process heartbeats with the new token")
            : StepOutcome.Fail(FailureReasons.AgentNotHeartbeating, "no heartbeat from a restarted agent process arrived", "agent is not heartbeating");
    }

    private async Task<StepOutcome> FinalizeRotateAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        if (ctx.Data.NewTokenFingerprint is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "no rotation was recorded", "nothing to finalize");
        }

        await _credentials.RecordFingerprintOnlyAsync(host.Id, CredentialPurposes.NodeTokenRender, ctx.Data.NewTokenFingerprint, cancellationToken);
        await _audit.AppendAsync(
            "system",
            "host.token_rotated",
            host.NodeRef,
            new Dictionary<string, object?> { ["fingerprint"] = ctx.Data.NewTokenFingerprint },
            cancellationToken);
        return StepOutcome.Done("the new token is recorded");
    }

    // ---- rolling update --------------------------------------------------------------------

    private async Task<StepOutcome> RollImageAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var digest = ParamString(ctx.Op, "digest");
        if (digest is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the rollout has no target digest", "no digest");
        }

        // Each host re-resolves the release for the rollout digest (it must be approved).
        ctx.Data.ImageDigest = digest;
        ctx.Data.ImageId = null;
        return await ImageAsync(ctx, cancellationToken);
    }

    /// <summary>Replaces the container with the new digest. The ctl <c>run</c> verb also rewrites the digest in the env file, so the token is untouched.
    /// Trust plane (decision D3): the bundle is (re)rendered BEFORE <c>run</c> — start_agent refuses a container whose env enables
    /// the listener without the certificate files, so a rollout re-provisions identity exactly like S10 does (and rotates leaves).</summary>
    private async Task<StepOutcome> RollRunAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        var digest = ParamString(ctx.Op, "digest");
        if (digest is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the rollout has no target digest", "no digest");
        }

        var status = await _access.CtlAsync(host, "status", Array.Empty<string>(), null, cancellationToken);
        if (status.Success && AgentRunning(status.Stdout, digest))
        {
            return StepOutcome.Skipped("the agent already runs the target digest");
        }

        if (_options.Value.Ubag.TrustEnabled)
        {
            var render = await RenderTrustBundleAsync(host, cancellationToken);
            if (render is not null)
            {
                return render;
            }
        }

        var run = await _access.CtlAsync(host, "run", new[] { digest }, null, cancellationToken);
        if (!run.Success)
        {
            return FromCtl(run, FailureReasons.AgentStartFailed, "the agent container did not start");
        }

        await _hosts.UpdateAsync(host.Id, h => h.AgentDigest = digest, cancellationToken);
        return StepOutcome.Done("the agent was replaced");
    }

    /// <summary>Issues (or reuses) the host's UBAG node certificate and renders it through <c>put-certs</c>.
    /// Returns null on success, or the failure outcome. Shared by the enrollment agent step and rollouts.</summary>
    private async Task<StepOutcome?> RenderTrustBundleAsync(HostEntity host, CancellationToken cancellationToken)
    {
        UbagNodeBundle bundle;
        try
        {
            bundle = await _trust.EnsureBundleAsync(host.Id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return StepOutcome.Fail(FailureReasons.TokenRenderFailed, ex.Message, "cert render failed");
        }

        var payload = FleetJson.Serialize(new
        {
            ca = bundle.CaPem,
            cert = bundle.CertPem,
            key = bundle.KeyPem,
        });
        var putCerts = await _access.CtlAsync(host, "put-certs", Array.Empty<string>(), payload, cancellationToken);
        return putCerts.Success ? null : FromCtl(putCerts, FailureReasons.TokenRenderFailed, "node certificate could not be written");
    }

    private async Task<StepOutcome> RollVerifyAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = ctx.RequireHost();
        var digest = ParamString(ctx.Op, "digest");
        if (host.ApiNodeId is null || digest is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the node or digest is not known", "nothing to verify");
        }

        var (satisfied, last) = await PollNodeAsync(
            host.ApiNodeId,
            TimeSpan.FromSeconds(Timing.VerifyTimeoutSeconds),
            node => string.Equals(node.Agent?.ImageDigest, digest, StringComparison.Ordinal) && node.Health == "Online",
            cancellationToken);
        if (satisfied)
        {
            return StepOutcome.Done("the agent heartbeats with the new digest");
        }

        return last?.LastHeartbeatAt is null
            ? StepOutcome.Fail(FailureReasons.AgentNotHeartbeating, "the API has not seen a heartbeat from the new agent", "agent is not heartbeating")
            : StepOutcome.Fail(FailureReasons.DigestNotApprovedByApi, "the node does not report the target digest as accepted", "digest not accepted by the API");
    }

    /// <summary>
    /// Post-enable canary. The API only lets Active or Probation nodes claim a canary (OET-RWP/1 section 3.8),
    /// so the rollout enables the node first, proves it, and drains it again if the answer is wrong.
    /// </summary>
    private async Task<StepOutcome> RollCanaryAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        ctx.Data.CanaryRequestedAt = null;
        var outcome = await CanaryCoreAsync(ctx, skipWhenActive: false, cancellationToken);
        if (outcome.Verdict == StepVerdict.Done)
        {
            // The new image is proven: old agent images beyond the two newest rollback candidates would otherwise pile up on every rollout.
            await PruneAgentImagesAsync(ctx.RequireHost(), cancellationToken);
        }

        if (outcome.Verdict == StepVerdict.Failed)
        {
            var host = ctx.RequireHost();
            if (host.ApiNodeId is not null)
            {
                try
                {
                    await _api.DrainAsync(host.ApiNodeId, cancellationToken);
                }
                catch (FleetApiException)
                {
                    // Best effort: the failed rollout already halts here and the owner sees the failure.
                }
            }
        }

        return outcome;
    }
}
