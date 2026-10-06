using System.Collections.Concurrent;
using Fleet.Core.Audit;
using Fleet.Core.Domain;
using Fleet.Core.Ssh;
using Fleet.Core.Validation;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Persistence;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Vault;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Operations;

/// <summary>
/// Drives operations one tick at a time. A tick performs exactly one unit of work (a host-key scan, or
/// one step), persists its result and says whether to continue. Everything that matters is in SQLite after
/// every tick, so killing the manager at any point and starting it again resumes without duplicating a
/// host user, a container, a node or a token (every step checks its predicate first, OET-RWP/1 section 8.2).
/// Runs are serialised: one operation executes at a time, which bounds the load on the primary.
/// </summary>
public sealed class OperationRunner
{
    private const int MaxTicksPerRun = 400;

    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);

    private readonly OperationStore _store;
    private readonly HostStore _hosts;
    private readonly StepExecutor _steps;
    private readonly HostSecurityService _hostSecurity;
    private readonly CredentialStore _credentials;
    private readonly IProvisioner _provisioner;
    private readonly AddressGuard _guard;
    private readonly RolloutTokenHolder _tokens;
    private readonly IAuditService _audit;
    private readonly IEventBus _events;
    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OperationRunner> _logger;

    public OperationRunner(
        OperationStore store,
        HostStore hosts,
        StepExecutor steps,
        HostSecurityService hostSecurity,
        CredentialStore credentials,
        IProvisioner provisioner,
        AddressGuard guard,
        RolloutTokenHolder tokens,
        IAuditService audit,
        IEventBus events,
        IOptions<FleetOptions> options,
        TimeProvider time,
        ILogger<OperationRunner> logger)
    {
        _store = store;
        _hosts = hosts;
        _steps = steps;
        _hostSecurity = hostSecurity;
        _credentials = credentials;
        _provisioner = provisioner;
        _guard = guard;
        _tokens = tokens;
        _audit = audit;
        _events = events;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public bool IsRunning(string operationId) => _running.ContainsKey(operationId);

    /// <summary>Startup recovery: steps a dead process left running go back to pending, expired owner credentials are erased.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var reset = await _store.ResetRunningStepsAsync(cancellationToken);
        var purged = await _credentials.PurgeExpiredOwnerCredentialsAsync(cancellationToken);
        if (reset > 0 || purged.Count > 0)
        {
            _logger.LogInformation("Recovery: {Reset} interrupted step(s) re-queued, {Purged} expired owner credential(s) erased.", reset, purged.Count);
        }
    }

    /// <summary>Runs ticks until the operation is blocked on the owner, finished, failed or cancelled.</summary>
    public async Task<OperationEntity?> RunAsync(string operationId, CancellationToken cancellationToken)
    {
        await _runGate.WaitAsync(cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running[operationId] = linked;
        try
        {
            for (var tick = 0; tick < MaxTicksPerRun; tick++)
            {
                var op = await _store.GetAsync(operationId, CancellationToken.None);
                if (op is null)
                {
                    return null;
                }

                if (op.CancelRequested && CanCancelNow(op))
                {
                    await FinalizeCancelAsync(operationId, "owner", CancellationToken.None);
                    break;
                }

                var advance = await TickAsync(op, linked.Token);
                if (advance != Advance.Continue)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The owner cancelled while a step was running: the step was interrupted, finish the cancellation.
            await FinalizeCancelAsync(operationId, "owner", CancellationToken.None);
        }
        finally
        {
            _running.TryRemove(operationId, out _);
            _runGate.Release();
        }

        return await _store.GetAsync(operationId, CancellationToken.None);
    }

    // ---- cancel ----------------------------------------------------------------------------

    private static bool CanCancelNow(OperationEntity op)
    {
        if (op.Kind == "enroll")
        {
            return EnrollmentStateMachine.TryParse(op.State, out var state) && EnrollmentStateMachine.CanCancel(state);
        }

        // A failed maintenance operation can be abandoned too (otherwise it would block a new one of its kind forever).
        return op.State is nameof(GenericOperationState.Queued)
            or nameof(GenericOperationState.Running)
            or nameof(GenericOperationState.AwaitingOwner)
            or nameof(GenericOperationState.Failed);
    }

    /// <summary>
    /// Owner cancel. Allowed only from the states of the section 8.1 table; a running step is interrupted.
    /// Returns false when the operation is not in a cancellable state.
    /// </summary>
    public async Task<bool> CancelAsync(string operationId, string actor, CancellationToken cancellationToken)
    {
        var op = await _store.GetAsync(operationId, cancellationToken);
        if (op is null || !CanCancelNow(op))
        {
            return false;
        }

        await _store.RequestCancelAsync(operationId, cancellationToken);
        if (_running.TryGetValue(operationId, out var source))
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run just ended; its flag check or the owner's next action finishes the cancellation.
            }

            // The running run interrupts the current step and finalizes the cancellation itself.
            return true;
        }

        await FinalizeCancelAsync(operationId, actor, cancellationToken);
        return true;
    }

    public async Task<bool> FinalizeCancelAsync(string operationId, string actor, CancellationToken cancellationToken)
    {
        var op = await _store.GetAsync(operationId, cancellationToken);
        if (op is null || !CanCancelNow(op))
        {
            return false;
        }

        var now = _time.GetUtcNow();
        var cancelled = op.Kind == "enroll" ? nameof(EnrollmentState.Cancelled) : nameof(GenericOperationState.Cancelled);
        var changed = await _store.UpdateAsync(
            op.Id,
            op.State,
            o =>
            {
                o.State = cancelled;
                o.FinishedAt = now;
                o.CancelRequested = false;
            },
            cancellationToken);
        if (!changed)
        {
            return false;
        }

        foreach (var step in await _store.GetStepsAsync(op.Id, cancellationToken))
        {
            if (step.State == "running")
            {
                await _store.UpdateStepAsync(op.Id, step.Seq, s =>
                {
                    s.State = "failed";
                    s.FinishedAt = now;
                    s.Summary = "cancelled by the owner";
                }, cancellationToken);
            }
        }

        if (op.HostId is not null)
        {
            // The owner credential is destroyed on cancel (RW-137).
            await _credentials.DestroyAsync(op.HostId, CredentialPurposes.OwnerBootstrap, cancellationToken);
            var cancelledHost = await _hosts.GetAsync(op.HostId, cancellationToken);
            if (cancelledHost is not null && op.Kind == "enroll")
            {
                var lifecycle = cancelledHost.HostKeyAlgo is null ? nameof(HostLifecycle.Removed) : nameof(HostLifecycle.Failed);
                await _hosts.UpdateAsync(cancelledHost.Id, h => h.Lifecycle = lifecycle, cancellationToken);
            }
            else if (cancelledHost is { Lifecycle: nameof(HostLifecycle.Removing) } && op.Kind == OperationKinds.ToWire(OperationKind.Remove))
            {
                // An abandoned removal leaves the host out of service (disabled), never half-removed.
                await _hosts.UpdateAsync(cancelledHost.Id, h => h.Lifecycle = nameof(HostLifecycle.Disabled), cancellationToken);
            }
        }

        await _audit.AppendAsync(actor, "operation.cancelled", op.Id, new Dictionary<string, object?> { ["kind"] = op.Kind }, cancellationToken);
        Publish(op.Id, op.Kind, cancelled, null);
        return true;
    }

    // ---- ticks -----------------------------------------------------------------------------

    private async Task<Advance> TickAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        if (op.Kind == "enroll")
        {
            return await EnrollTickAsync(op, cancellationToken);
        }

        return await GenericTickAsync(op, cancellationToken);
    }

    private async Task<Advance> EnrollTickAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        if (!EnrollmentStateMachine.TryParse(op.State, out var state))
        {
            return Advance.Blocked;
        }

        switch (state)
        {
            case EnrollmentState.Created:
                return await ScanAsync(op, cancellationToken);

            case EnrollmentState.Provisioned:
                return await EnterImageAsync(op, cancellationToken);

            case EnrollmentState.ImageAwaitingSync:
                if (!_tokens.HasToken)
                {
                    return Advance.Blocked;
                }

                return await TransitionAsync(op, EnrollmentState.ImagePulling, cancellationToken) ? Advance.Continue : Advance.Blocked;

            case EnrollmentState.Bootstrapping:
                // S8 marks itself done and THEN moves the operation to Provisioned: two writes. A process killed between them leaves
                // Bootstrapping with S1..S8 finished, and no step is left that could require the Bootstrapping state. Finish the move here
                // (the owner credential is already destroyed, so a stuck operation could only be abandoned and re-added).
                if (await BootstrapFinishedAsync(op, cancellationToken))
                {
                    return await TransitionAsync(op, EnrollmentState.Provisioned, cancellationToken) ? Advance.Continue : Advance.Blocked;
                }

                return await RunNextStepAsync(op, cancellationToken);

            case EnrollmentState.ImagePulling:
            case EnrollmentState.AgentStarting:
            case EnrollmentState.Verifying:
            case EnrollmentState.Canary:
                return await RunNextStepAsync(op, cancellationToken);

            case EnrollmentState.Active:
            case EnrollmentState.Cancelled:
                return Advance.Done;

            default:
                // HostKeyPending, HostKeyConfirmed, Failed: waiting for the owner.
                return Advance.Blocked;
        }
    }

    private async Task<Advance> GenericTickAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        switch (op.State)
        {
            case nameof(GenericOperationState.Queued):
                var started = await _store.UpdateAsync(op.Id, op.State, o => o.State = nameof(GenericOperationState.Running), cancellationToken);
                if (started)
                {
                    Publish(op.Id, op.Kind, nameof(GenericOperationState.Running), null);
                }

                return started ? Advance.Continue : Advance.Blocked;

            case nameof(GenericOperationState.Running):
                return await RunNextStepAsync(op, cancellationToken);

            case nameof(GenericOperationState.Succeeded):
            case nameof(GenericOperationState.Cancelled):
                return Advance.Done;

            default:
                return Advance.Blocked;
        }
    }

    /// <summary>True when every owner-credential step (S1..S8) of an enrollment is done or skipped, which is exactly when Bootstrapping is over.</summary>
    private async Task<bool> BootstrapFinishedAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        var bootstrap = (await _store.GetStepsAsync(op.Id, cancellationToken))
            .Where(s => EnrollSteps.TryParse(s.Name, out var step) && step <= EnrollStep.DiscardOwnerKey)
            .ToList();
        return bootstrap.Count > 0 && bootstrap.All(s => s.State is "done" or "skipped");
    }

    /// <summary>Created: fetch the host's key fingerprints for DISPLAY. Nothing is trusted until the owner confirms one out of band.</summary>
    private async Task<Advance> ScanAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        var host = op.HostId is null ? null : await _hosts.GetAsync(op.HostId, cancellationToken);
        if (host is null)
        {
            await FailOperationAsync(op, FailureReasons.InternalError, "the host no longer exists", cancellationToken);
            return Advance.Blocked;
        }

        var check = await _guard.CheckAsync(host.Address, cancellationToken);
        if (!check.Allowed)
        {
            await FailOperationAsync(op, FailureReasons.ForbiddenHost, check.Message, cancellationToken);
            return Advance.Blocked;
        }

        var scan = await _provisioner.ScanHostKeyAsync(host.Address, host.SshPort, cancellationToken);
        if (!scan.Success)
        {
            await FailOperationAsync(op, FailureReasons.HostKeyUnreachable, scan.Detail, cancellationToken);
            return Advance.Blocked;
        }

        var data = OperationData.Parse(op.DataJson);
        data.HostKeyCandidates = scan.Keys.Select(k => new HostKeyCandidate(k.Algorithm, k.PublicKeyBase64, k.Fingerprint)).ToList();
        await _store.SaveDataAsync(op.Id, data, cancellationToken);
        if (await TransitionAsync(op, EnrollmentState.HostKeyPending, cancellationToken))
        {
            await _audit.AppendAsync(
                "system",
                "enroll.host_key_scanned",
                host.NodeRef,
                new Dictionary<string, object?> { ["keys"] = data.HostKeyCandidates.Count },
                cancellationToken);
        }

        return Advance.Blocked;
    }

    /// <summary>
    /// Provisioned: decide between pulling and waiting for a sync. In scoped-token mode with no token in
    /// memory the operation waits in <c>ImageAwaitingSync</c> (unless the image is already on the host).
    /// </summary>
    private async Task<Advance> EnterImageAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        var scoped = string.Equals(_options.Value.Image.Mode, ImageOptions.ScopedTokenMode, StringComparison.Ordinal);
        if (scoped && !_tokens.HasToken)
        {
            var host = op.HostId is null ? null : await _hosts.GetAsync(op.HostId, cancellationToken);
            var present = host is not null && await _steps.ImageAlreadyPresentAsync(host, cancellationToken);
            if (!present)
            {
                if (await TransitionAsync(op, EnrollmentState.ImageAwaitingSync, cancellationToken))
                {
                    await _audit.AppendAsync("system", "enroll.awaiting_image_sync", op.Id, null, cancellationToken);
                }

                return Advance.Blocked;
            }
        }

        return await TransitionAsync(op, EnrollmentState.ImagePulling, cancellationToken) ? Advance.Continue : Advance.Blocked;
    }

    private async Task<Advance> RunNextStepAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        var steps = await _store.GetStepsAsync(op.Id, cancellationToken);
        var next = steps.FirstOrDefault(s => s.State is "pending" or "running" or "failed");
        var host = op.HostId is null ? null : await _hosts.GetAsync(op.HostId, cancellationToken);

        if (next is null)
        {
            return await CompleteAsync(op, cancellationToken);
        }

        EnrollStep enrollStep = default;
        var isEnroll = op.Kind == "enroll";
        if (isEnroll)
        {
            if (!EnrollSteps.TryParse(next.Name, out enrollStep))
            {
                await FailOperationAsync(op, FailureReasons.InternalError, "unknown enrollment step " + next.Name, cancellationToken);
                return Advance.Blocked;
            }

            var required = EnrollSteps.StateFor(enrollStep);
            if (EnrollmentStateMachine.TryParse(op.State, out var current) && current != required)
            {
                if (!await TransitionAsync(op, required, cancellationToken))
                {
                    return Advance.Blocked;
                }

                op = await _store.GetAsync(op.Id, cancellationToken) ?? op;
            }
        }

        var attempt = string.Equals(op.CurrentStep, next.Name, StringComparison.Ordinal) ? op.StepAttempt + 1 : 1;
        var startedAt = _time.GetUtcNow();
        await _store.UpdateStepAsync(
            op.Id,
            next.Seq,
            s =>
            {
                s.State = "running";
                s.StartedAt = startedAt;
                s.FinishedAt = null;
                s.ExitCode = null;
                s.Summary = null;
            },
            cancellationToken);
        await _store.UpdateAsync(op.Id, null, o =>
        {
            o.CurrentStep = next.Name;
            o.StepAttempt = attempt;
        }, cancellationToken);
        Publish(op.Id, op.Kind, op.State, next.Name);

        var fresh = await _store.GetAsync(op.Id, cancellationToken) ?? op;
        var context = new StepContext(fresh, host, OperationData.Parse(fresh.DataJson));
        StepOutcome outcome;
        try
        {
            outcome = isEnroll
                ? await _steps.ExecuteEnrollStepAsync(context, enrollStep, cancellationToken)
                : await _steps.ExecuteMaintenanceStepAsync(context, next.Name, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Step {Step} of operation {Operation} failed unexpectedly: {Type}: {Message}", next.Name, op.Id, ex.GetType().Name, LogScrubber.Scrub(ex.Message, 200));
            outcome = StepOutcome.Fail(FailureReasons.InternalError, ex.GetType().Name, "unexpected error");
        }

        var finishedAt = _time.GetUtcNow();
        var summary = LogScrubber.SanitizeUntrusted(outcome.Summary, 500);
        if (outcome.Verdict == StepVerdict.Failed)
        {
            await _store.UpdateStepAsync(
                op.Id,
                next.Seq,
                s =>
                {
                    s.State = "failed";
                    s.FinishedAt = finishedAt;
                    s.Summary = summary;
                },
                cancellationToken);
            await FailOperationAsync(fresh, outcome.FailureReason, outcome.FailureDetail, cancellationToken);
            if (outcome.FailureReason == FailureReasons.HostKeyChanged && context.Host is { } affected)
            {
                await _hostSecurity.OnHostKeyChangedAsync(affected.Id, "system", cancellationToken);
            }

            return Advance.Blocked;
        }

        await _store.UpdateStepAsync(
            op.Id,
            next.Seq,
            s =>
            {
                s.State = outcome.Verdict == StepVerdict.Skipped ? "skipped" : "done";
                s.FinishedAt = finishedAt;
                s.Summary = summary;
            },
            cancellationToken);
        Publish(op.Id, op.Kind, op.State, next.Name);

        if (isEnroll && enrollStep == EnrollStep.DiscardOwnerKey)
        {
            var latest = await _store.GetAsync(op.Id, cancellationToken) ?? fresh;
            await TransitionAsync(latest, EnrollmentState.Provisioned, cancellationToken);
        }
        else if (isEnroll && enrollStep == EnrollStep.Activate)
        {
            var latest = await _store.GetAsync(op.Id, cancellationToken) ?? fresh;
            if (await TransitionAsync(latest, EnrollmentState.Active, cancellationToken))
            {
                await _audit.AppendAsync("system", "enroll.completed", host?.NodeRef ?? op.Id, null, cancellationToken);
                return Advance.Done;
            }
        }

        return Advance.Continue;
    }

    private async Task<Advance> CompleteAsync(OperationEntity op, CancellationToken cancellationToken)
    {
        if (op.Kind == "enroll")
        {
            // Only reachable when the process died between the last step and the state change.
            if (EnrollmentStateMachine.TryParse(op.State, out var state) && state == EnrollmentState.Canary)
            {
                return await TransitionAsync(op, EnrollmentState.Active, cancellationToken) ? Advance.Done : Advance.Blocked;
            }

            return Advance.Blocked;
        }

        var now = _time.GetUtcNow();
        var done = await _store.UpdateAsync(
            op.Id,
            op.State,
            o =>
            {
                o.State = nameof(GenericOperationState.Succeeded);
                o.FinishedAt = now;
            },
            cancellationToken);
        if (done)
        {
            await _audit.AppendAsync("system", "operation.succeeded", op.Id, new Dictionary<string, object?> { ["kind"] = op.Kind }, cancellationToken);
            Publish(op.Id, op.Kind, nameof(GenericOperationState.Succeeded), null);
        }

        return Advance.Done;
    }

    // ---- state changes ---------------------------------------------------------------------

    /// <summary>
    /// A validated enrollment transition (OET-RWP/1 section 8.1). Returns false when another writer won, and also when the edge is
    /// not in the table (nothing is written then; the runner logs it and stops this tick instead of throwing out of the worker loop).
    /// </summary>
    public async Task<bool> TransitionAsync(OperationEntity op, EnrollmentState to, CancellationToken cancellationToken)
    {
        if (!EnrollmentStateMachine.TryParse(op.State, out var from))
        {
            return false;
        }

        if (!EnrollmentStateMachine.CanTransition(from, to))
        {
            _logger.LogError("Operation {Operation} cannot move from {From} to {To}; the transition was refused.", op.Id, from, to);
            return false;
        }

        var now = _time.GetUtcNow();
        var changed = await _store.UpdateAsync(
            op.Id,
            op.State,
            o =>
            {
                o.State = to.ToString();
                if (EnrollmentStateMachine.IsTerminal(to))
                {
                    o.FinishedAt = now;
                }
            },
            cancellationToken);
        if (changed)
        {
            Publish(op.Id, op.Kind, to.ToString(), null);
        }

        return changed;
    }

    /// <summary>Moves an operation to Failed with a reason from the stable enum. Retry later re-enters <c>resume_state</c>.</summary>
    public async Task FailOperationAsync(OperationEntity op, string? reason, string? detail, CancellationToken cancellationToken)
    {
        var normalized = FailureReasons.Normalize(reason);
        var safeDetail = LogScrubber.SanitizeUntrusted(detail, 500);
        var now = _time.GetUtcNow();
        var latest = await _store.GetAsync(op.Id, cancellationToken) ?? op;
        string failed;
        string resume;
        if (latest.Kind == "enroll")
        {
            if (!EnrollmentStateMachine.TryParse(latest.State, out var from) || !EnrollmentStateMachine.CanTransition(from, EnrollmentState.Failed))
            {
                _logger.LogError("Operation {Operation} cannot fail from state {State}.", latest.Id, latest.State);
                return;
            }

            failed = nameof(EnrollmentState.Failed);
            resume = from.ToString();
        }
        else
        {
            failed = nameof(GenericOperationState.Failed);
            resume = nameof(GenericOperationState.Running);
        }

        var changed = await _store.UpdateAsync(
            latest.Id,
            latest.State,
            o =>
            {
                o.State = failed;
                o.ResumeState = resume;
                o.FailureReason = normalized;
                o.FailureDetail = safeDetail;
                o.FinishedAt = null;
            },
            cancellationToken);
        if (!changed)
        {
            return;
        }

        if (latest.HostId is not null && latest.Kind == "enroll")
        {
            await _hosts.UpdateAsync(latest.HostId, h => h.Lifecycle = nameof(HostLifecycle.Failed), cancellationToken);
        }

        await _audit.AppendAsync(
            "system",
            "operation.failed",
            latest.Id,
            new Dictionary<string, object?> { ["kind"] = latest.Kind, ["reason"] = normalized, ["step"] = latest.CurrentStep, ["at"] = now },
            cancellationToken);
        Publish(latest.Id, latest.Kind, failed, latest.CurrentStep);
    }

    private void Publish(string id, string kind, string state, string? step) =>
        _events.Publish("operation.updated", new { id, kind, state, step });
}
