using Fleet.Core.Domain;
using Fleet.Core.Ssh;
using Fleet.Manager.Api;
using Fleet.Manager.Configuration;
using Fleet.Manager.Persistence;
using Fleet.Manager.Vault;

namespace Fleet.Manager.Operations;

// S9..S13: image, agent start, verification, canary, activation. These use only the manager's
// restricted key (oet-fleet-ctl verbs) and the API; no owner secret exists any more.

public sealed partial class StepExecutor
{
    private static string[] VerifyArgs(string digest, string? imageId) =>
        imageId is null ? new[] { digest } : new[] { digest, imageId };

    /// <summary>True when the current approved image is already on the host (a resumed enrollment need not wait for a registry token).</summary>
    public async Task<bool> ImageAlreadyPresentAsync(HostEntity host, CancellationToken cancellationToken)
    {
        var release = await _releases.GetCurrentApprovedAsync(cancellationToken);
        if (release is null)
        {
            return false;
        }

        var probe = await _access.CtlAsync(host, "verify", VerifyArgs(release.AgentDigest, release.AgentImageId), null, cancellationToken);
        return probe.Success;
    }

    /// <summary>
    /// S9: bring the approved agent image onto the host BY DIGEST. Scoped-token mode logs in with the
    /// per-rollout token on stdin, pulls, verifies digest and image id, and ALWAYS logs out again.
    /// </summary>
    private async Task<StepOutcome> ImageAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        var release = ctx.Data.ImageDigest is { } chosen
            ? await _releases.FindApprovedByDigestAsync(chosen, cancellationToken)
            : await _releases.GetCurrentApprovedAsync(cancellationToken);
        if (release is null)
        {
            return StepOutcome.Fail(FailureReasons.ImageDigestUnapproved, "no approved agent image is available; approve a release first", "no approved image");
        }

        var digest = release.AgentDigest;
        ctx.Data.ImageDigest = digest;
        ctx.Data.ImageId ??= release.AgentImageId;
        await _operations.SaveDataAsync(ctx.Op.Id, ctx.Data, cancellationToken);

        var scoped = string.Equals(_options.Value.Image.Mode, ImageOptions.ScopedTokenMode, StringComparison.Ordinal);
        var present = await _access.CtlAsync(host, "verify", VerifyArgs(digest, ctx.Data.ImageId), null, cancellationToken);
        if (present.Success)
        {
            if (scoped)
            {
                // A previous run may have been killed between login and logout: make sure no registry credential lingers on the helper.
                await _access.CtlAsync(host, "logout", Array.Empty<string>(), null, cancellationToken);
            }

            return StepOutcome.Skipped("agent image already present and verified");
        }

        if (present.FailureReason is not null)
        {
            return FromCtl(present, FailureReasons.SshUnreachable, "host could not be reached");
        }

        var loggedIn = false;
        try
        {
            if (scoped)
            {
                if (!_rolloutToken.TryGetCredential(out var user, out var token))
                {
                    return StepOutcome.Fail(FailureReasons.ImagePullFailed, "no registry token is available; run the sync job and retry", "no registry token");
                }

                var login = await _access.CtlAsync(host, "login", new[] { "--registry", FleetCtlVerbs.Registry }, user + "\n" + token + "\n", cancellationToken);
                if (!login.Success)
                {
                    return FromCtl(login, FailureReasons.ImagePullFailed, "registry login failed");
                }

                loggedIn = true;
            }

            var pull = await _access.CtlAsync(host, "pull", new[] { FleetCtlVerbs.PullReference(digest) }, null, cancellationToken);
            if (!pull.Success)
            {
                return FromCtl(pull, FailureReasons.ImagePullFailed, "image pull failed");
            }

            var reportedId = JsonString(ParseJson(pull.Stdout), "imageId");
            if (ctx.Data.ImageId is { } expectedId && reportedId is not null && !string.Equals(expectedId, reportedId, StringComparison.Ordinal))
            {
                return StepOutcome.Fail(FailureReasons.ImageIdMismatch, "the pulled image id differs from the released one", "image id mismatch");
            }

            var imageId = ctx.Data.ImageId ?? reportedId;
            if (imageId is not null && !string.Equals(imageId, ctx.Data.ImageId, StringComparison.Ordinal))
            {
                ctx.Data.ImageId = imageId;
                await _operations.SaveDataAsync(ctx.Op.Id, ctx.Data, cancellationToken);
            }

            var verify = await _access.CtlAsync(host, "verify", VerifyArgs(digest, imageId), null, cancellationToken);
            return verify.Success
                ? StepOutcome.Done("agent image pulled and verified by digest")
                : FromCtl(verify, FailureReasons.ImageIdMismatch, "image verification failed");
        }
        finally
        {
            if (loggedIn)
            {
                // The trap: the registry credential must not survive on the helper, whatever happened above.
                await _access.CtlAsync(host, "logout", Array.Empty<string>(), null, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// S10: register the node (idempotent on nodeRef), obtain a token (the registration response, or
    /// <c>tokens/rotate</c> when a lost response left the node registered without a delivered token),
    /// push the policy with the approved-image window, render the env file over stdin, and start the
    /// agent container by digest. The token exists only in memory between the API response and put-env.
    /// </summary>
    private async Task<StepOutcome> AgentStartAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        var digest = ctx.Data.ImageDigest ?? host.AgentDigest;
        if (digest is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "no agent image digest was chosen", "no image chosen");
        }

        if (host.ApiNodeId is not null && await _credentials.ExistsAnyAsync(host.Id, CredentialPurposes.NodeTokenRender, cancellationToken))
        {
            var status = await _access.CtlAsync(host, "status", Array.Empty<string>(), null, cancellationToken);
            if (status.Success && AgentRunning(status.Stdout, digest))
            {
                return StepOutcome.Skipped("agent already running the approved image");
            }
        }

        var policy = await _policies.EffectiveAsync(host, cancellationToken);
        RegisterNodeResult registration;
        try
        {
            registration = await _api.RegisterNodeAsync(
                new RegisterNodeRequest(
                    host.NodeRef,
                    host.DisplayName,
                    host.Region,
                    host.Provider,
                    new RegisterPolicyDto(policy.AllowedKinds, policy.MaxConcurrency, policy.PerKind, policy.Budgets),
                    Timing.TokenTtlDays),
                cancellationToken);
        }
        catch (FleetApiException ex)
        {
            return FromApi(ex, FailureReasons.ApiRegisterFailed, "node registration failed");
        }

        var nodeId = registration.Node.Id;
        await _hosts.UpdateAsync(host.Id, h => h.ApiNodeId = nodeId, cancellationToken);
        host = await FreshHostAsync(ctx, cancellationToken);

        ApiToken token;
        try
        {
            token = registration.Token
                ?? await _api.RotateTokenAsync(nodeId, Timing.RotationGraceSeconds, Timing.TokenTtlDays, cancellationToken);
        }
        catch (FleetApiException ex)
        {
            return FromApi(ex, FailureReasons.TokenRenderFailed, "node token could not be obtained");
        }

        try
        {
            await _policies.PushAsync(host, cancellationToken);
        }
        catch (FleetApiException ex)
        {
            return FromApi(ex, FailureReasons.ApiRegisterFailed, "policy push failed");
        }

        string envText;
        try
        {
            envText = AgentEnv.Render(BuildAgentEnv(nodeId, token.Value, digest, policy));
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

        var unit = await _access.CtlAsync(host, "unit-sync", Array.Empty<string>(), null, cancellationToken);
        if (!unit.Success)
        {
            return FromCtl(unit, FailureReasons.AgentStartFailed, "boot unit could not be written");
        }

        var run = await _access.CtlAsync(host, "run", new[] { digest }, null, cancellationToken);
        if (!run.Success)
        {
            return FromCtl(run, FailureReasons.AgentStartFailed, "agent container did not start");
        }

        await _credentials.RecordFingerprintOnlyAsync(host.Id, CredentialPurposes.NodeTokenRender, TokenFingerprint(token.Value), cancellationToken);
        await _hosts.UpdateAsync(host.Id, h => h.AgentDigest = digest, cancellationToken);
        return StepOutcome.Done("agent started from the approved digest");
    }

    private bool ProtocolSupported(int? protocol, ApiStatus? status)
    {
        if (protocol is not { } number)
        {
            return false;
        }

        var minimum = status?.ProtocolMinimum ?? 1;
        var maximum = status?.ProtocolCurrent ?? _options.Value.Api.ProtocolVersion;
        return number >= minimum && number <= maximum;
    }

    /// <summary>S11: wait until the API sees the node heartbeating with the expected image digest and a supported protocol.</summary>
    private async Task<StepOutcome> VerifyAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        var digest = ctx.Data.ImageDigest ?? host.AgentDigest;
        if (host.ApiNodeId is null || digest is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the node or image is not known yet", "nothing to verify");
        }

        ApiStatus? status = null;
        try
        {
            status = await _api.GetStatusAsync(cancellationToken);
        }
        catch (FleetApiException)
        {
            // The protocol range is advisory; without it the manager's own protocol number is the ceiling.
        }

        var (satisfied, last) = await PollNodeAsync(
            host.ApiNodeId,
            TimeSpan.FromSeconds(Timing.VerifyTimeoutSeconds),
            node => (node.Status is "Probation" or "Active")
                && string.Equals(node.Agent?.ImageDigest, digest, StringComparison.Ordinal)
                && ProtocolSupported(node.Agent?.Protocol, status),
            cancellationToken);
        if (satisfied && last is not null)
        {
            await _hosts.UpdateAsync(host.Id, h => h.AppliedRevision = last.AppliedRevision, cancellationToken);
            return StepOutcome.Done("the agent is heartbeating with the expected image");
        }

        if (last?.LastHeartbeatAt is null)
        {
            return StepOutcome.Fail(FailureReasons.AgentNotHeartbeating, "the API has not seen a heartbeat from the agent", "agent is not heartbeating");
        }

        if (last.Agent?.Protocol is { } protocol && !ProtocolSupported(protocol, status))
        {
            return StepOutcome.Fail(FailureReasons.ProtocolUnsupported, "the agent speaks protocol " + protocol + ", which the API does not accept", "protocol unsupported");
        }

        return StepOutcome.Fail(FailureReasons.DigestNotApprovedByApi, "the node heartbeats but does not report the approved digest as accepted", "digest not accepted by the API");
    }

    /// <summary>S12: the known-answer canary. Success needs a canary result newer than the request; a wrong answer is a mismatch, silence is a timeout.</summary>
    private Task<StepOutcome> CanaryAsync(StepContext ctx, CancellationToken cancellationToken) =>
        CanaryCoreAsync(ctx, skipWhenActive: true, cancellationToken);

    /// <param name="skipWhenActive">Enrollment and enable need no canary for a node that is already Active; a rollout proves the NEW agent even though the node is Active.</param>
    private async Task<StepOutcome> CanaryCoreAsync(StepContext ctx, bool skipWhenActive, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        var nodeId = host.ApiNodeId;
        if (nodeId is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the host has no API node", "no API node");
        }

        if (skipWhenActive)
        {
            try
            {
                var current = await _api.GetNodeAsync(nodeId, cancellationToken);
                if (current is { Status: "Active" })
                {
                    return StepOutcome.Skipped("the node is already active");
                }
            }
            catch (FleetApiException ex) when (ex.Retryable || ex.Code == FleetApiException.Unreachable)
            {
                // Fall through: the poll below tolerates transient API errors.
            }
        }

        var requestedAt = ctx.Data.CanaryRequestedAt;
        if (requestedAt is null)
        {
            requestedAt = _time.GetUtcNow();
            ctx.Data.CanaryRequestedAt = requestedAt;
            await _operations.SaveDataAsync(ctx.Op.Id, ctx.Data, cancellationToken);
            try
            {
                await _api.EnqueueCanaryAsync(nodeId, cancellationToken);
            }
            catch (FleetApiException ex) when (ex.Code == "canary_in_progress")
            {
                // One is already open for this node; wait for its result.
            }
            catch (FleetApiException ex)
            {
                return FromApi(ex, FailureReasons.InternalError, "the canary could not be requested");
            }
        }

        var since = requestedAt.Value - TimeSpan.FromSeconds(5);
        var (_, last) = await PollNodeAsync(
            nodeId,
            TimeSpan.FromSeconds(Timing.CanaryTimeoutSeconds),
            node => (skipWhenActive && node.Status == "Active") || (node.LastCanary is { } canary && canary.At >= since),
            cancellationToken);

        if (skipWhenActive && last is { Status: "Active" } && !(last.LastCanary is { } fresh && fresh.At >= since))
        {
            return StepOutcome.Skipped("the node became active");
        }

        if (last?.LastCanary is { } result && result.At >= since)
        {
            return result.Ok
                ? StepOutcome.Done("the known-answer canary passed")
                : StepOutcome.Fail(FailureReasons.CanaryMismatch, "the node returned a wrong canary result", "canary mismatch");
        }

        return StepOutcome.Fail(FailureReasons.CanaryTimeout, "no canary result arrived in time", "canary timed out");
    }

    /// <summary>S13: enable the node; from here it starts claiming work ("auto-assign").</summary>
    private async Task<StepOutcome> ActivateAsync(StepContext ctx, CancellationToken cancellationToken)
    {
        var host = await FreshHostAsync(ctx, cancellationToken);
        var nodeId = host.ApiNodeId;
        if (nodeId is null)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the host has no API node", "no API node");
        }

        ApiNode? node;
        try
        {
            node = await _api.GetNodeAsync(nodeId, cancellationToken);
            if (node is not { Status: "Active" })
            {
                await _api.EnableAsync(nodeId, cancellationToken);
            }
        }
        catch (FleetApiException ex) when (ex.Code == "canary_required")
        {
            return StepOutcome.Fail(FailureReasons.CanaryMismatch, "the API requires a passing canary before activation", "canary required");
        }
        catch (FleetApiException ex)
        {
            return FromApi(ex, FailureReasons.InternalError, "the node could not be enabled");
        }

        var (active, last) = await PollNodeAsync(nodeId, TimeSpan.FromSeconds(30), n => n.Status == "Active", cancellationToken);
        if (!active)
        {
            return StepOutcome.Fail(FailureReasons.InternalError, "the node did not become active", "activation not confirmed");
        }

        await _hosts.UpdateAsync(
            host.Id,
            h =>
            {
                h.Lifecycle = nameof(HostLifecycle.Active);
                h.AppliedRevision = last?.AppliedRevision ?? h.AppliedRevision;
            },
            cancellationToken);
        return StepOutcome.Done("the node is active and claiming work");
    }
}
