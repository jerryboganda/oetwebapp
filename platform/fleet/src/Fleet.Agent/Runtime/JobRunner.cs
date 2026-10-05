using System.Text;

namespace Fleet.Agent;

/// <summary>
/// Runs one claimed job from start to its report: admission checks, scratch directory, the executor, then exactly one of
/// complete / fail / nothing, decided by the abort reason (protocol 4.2.3, 4.5, 4.6, 5.6). The caller (the supervisor) owns the
/// lease, heartbeat and capacity registrations and releases them afterwards.
/// </summary>
internal sealed class JobRunner
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly IRemoteWorkerApi _api;
    private readonly ExecutorRegistry _executors;
    private readonly ScratchManager _scratch;
    private readonly IJobIo _io;
    private readonly AgentStatus _status;
    private readonly CapacityAccountant _capacity;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public JobRunner(IRemoteWorkerApi api, ExecutorRegistry executors, ScratchManager scratch, IJobIo io, AgentStatus status,
        CapacityAccountant capacity, IMonotonicClock clock, ILogger log, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _api = api;
        _executors = executors;
        _scratch = scratch;
        _io = io;
        _status = status;
        _capacity = capacity;
        _clock = clock;
        _log = log;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    public async Task RunAsync(ClaimedJob job, JobLease lease)
    {
        ExecutionResult? result;
        Timer? timeout = null;
        try
        {
            timeout = new Timer(_ => lease.Abort(AbortReason.Timeout), null, TimeSpan.FromSeconds(Math.Max(1, job.Limits.TimeoutSeconds)), Timeout.InfiniteTimeSpan);
            result = await ExecuteAsync(job, lease).ConfigureAwait(false);
        }
        finally
        {
            timeout?.Dispose();
        }

        await ReportAsync(job, lease, result).ConfigureAwait(false);
    }

    // ---- execution -----------------------------------------------------------------------------------------

    private async Task<ExecutionResult?> ExecuteAsync(ClaimedJob job, JobLease lease)
    {
        string? directory = null;
        try
        {
            if (lease.Reason != AbortReason.None) return MapAbort(lease.Reason);
            var executor = _executors.Find(job.Kind, job.SchemaVersion);
            if (executor is null || !string.Equals(executor.EngineVersion, job.EngineVersion, StringComparison.Ordinal))
            {
                return ExecutionResult.Fail(FailCodes.InternalError, false, "kind or engine version is not offered by this agent");
            }

            var state = _status.Decision.State;
            if (state == AgentState.Stopping) return ExecutionResult.Fail(FailCodes.Shutdown, true, "agent is stopping");
            if (state == AgentState.Draining) return ExecutionResult.Fail(FailCodes.Drain, true, "node is draining");
            if (!_capacity.FitsBudgetsEver(job.Limits))
            {
                return ExecutionResult.Fail(FailCodes.LimitsExceeded, false, "job limits exceed the agent budgets");
            }

            directory = _scratch.CreateJobDirectory(job.Id, job.Fence);
            var context = new JobContext { Job = job, Lease = lease, ScratchDirectory = directory, Io = _io };
            return await executor.ExecuteAsync(context, lease.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MapAbort(lease.Reason);
        }
        catch (JobFailureException failure)
        {
            return ExecutionResult.Fail(failure.Code, failure.Retryable, failure.Detail);
        }
        catch (JobAbandonException abandon)
        {
            lease.Abort(abandon.Reason);
            return null;
        }
        catch (UnrepresentableResultException)
        {
            return ExecutionResult.Fail(FailCodes.ExtractException, false, "result is not representable");
        }
        catch (Exception ex)
        {
            _log.LogError("job {Job} kind={Kind} failed unexpectedly: {Error}", job.Id, job.Kind, Redact.Exception(ex));
            return ExecutionResult.Fail(FailCodes.InternalError, true, "unexpected agent error");
        }
        finally
        {
            // Delete the scratch directory on EVERY exit path (H2).
            _scratch.DeleteJobDirectory(directory);
        }
    }

    /// <summary>What a cancelled execution should report; null means "report nothing" (the lease is gone).</summary>
    internal static ExecutionResult? MapAbort(AbortReason reason) => reason switch
    {
        AbortReason.Timeout => ExecutionResult.Fail(FailCodes.Timeout, true, "job exceeded its timeout"),
        AbortReason.Shutdown => ExecutionResult.Fail(FailCodes.Shutdown, true, "agent is shutting down"),
        AbortReason.Shed => ExecutionResult.Fail(FailCodes.PressureShed, true, "memory pressure shed"),
        AbortReason.None => ExecutionResult.Fail(FailCodes.InternalError, true, "execution was cancelled"),
        _ => null,
    };

    // ---- reporting -----------------------------------------------------------------------------------------

    private async Task ReportAsync(ClaimedJob job, JobLease lease, ExecutionResult? result)
    {
        var reason = lease.Reason;
        if (reason is AbortReason.LeaseLost or AbortReason.Quarantined or AbortReason.AuthFailed or AbortReason.Superseded or AbortReason.StaleInput)
        {
            // Lease lost: abort, delete scratch, NEVER call complete or fail (RW-055).
            _log.LogInformation("job {Job} dropped without a report reason={Reason}", job.Id, reason);
            return;
        }

        if (reason == AbortReason.LocalExpiry || _clock.NowMs >= lease.LocalExpiryMs)
        {
            // Past the conservative local expiry: never complete (RW-054). One best-effort fail may still land.
            _log.LogWarning("job {Job} passed its local lease expiry; no completion", job.Id);
            await FailAsync(job, lease, FailCodes.Shutdown, true, "lease expired locally", maxAttempts: 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return;
        }

        if (result is null) return;
        var attempts = reason == AbortReason.Shutdown ? 2 : int.MaxValue;
        if (result.Success)
        {
            await CompleteAsync(job, lease, result).ConfigureAwait(false);
        }
        else
        {
            await FailAsync(job, lease, result.FailCode, result.Retryable, result.FailMessage, attempts, null).ConfigureAwait(false);
        }
    }

    private async Task CompleteAsync(ClaimedJob job, JobLease lease, ExecutionResult result)
    {
        string resultJson;
        try
        {
            resultJson = StrictUtf8.GetString(result.ResultUtf8);
        }
        catch (DecoderFallbackException)
        {
            await FailAsync(job, lease, FailCodes.InternalError, true, "result is not valid utf-8", int.MaxValue, null).ConfigureAwait(false);
            return;
        }

        var request = new CompleteRequest
        {
            Fence = job.Fence,
            ResultSha256 = Hashing.Sha256Hex(result.ResultUtf8),
            ResultJson = resultJson,
            Outputs = result.Outputs,
            Metrics = new CompleteMetrics { DurationMs = _clock.NowMs - lease.StartedMs, PeakRssMiB = result.PeakRssMiB, InputBytes = result.InputBytes },
        };

        // One body, built once: every retry is byte-identical so a lost success response replays (RW-087).
        var body = ProtocolJson.ToUtf8(request);
        for (var attempt = 0; ; attempt++)
        {
            if (!MayStillReport(lease)) return;
            var response = await _api.CompleteAsync(job.Id, body, CancellationToken.None).ConfigureAwait(false);
            if (response.Kind == ApiKind.Ok)
            {
                _log.LogInformation("job {Job} completed status={Status} outcome={Outcome} replayed={Replayed}",
                    job.Id, response.Value?.Status, response.Value?.Outcome, response.Value?.Replayed);
                return;
            }

            if (response.Kind == ApiKind.ServerError && response.Code == "applies_frozen")
            {
                _log.LogWarning("job {Job} completion refused: applies are frozen", job.Id);
                return;
            }

            if (response.Transient)
            {
                await _delay(WaitFor(response, attempt), CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            switch (response.Kind)
            {
                case ApiKind.Conflict when response.IsLeaseLost:
                    lease.Abort(AbortReason.LeaseLost);
                    return;
                case ApiKind.PayloadTooLarge:
                    await FailAsync(job, lease, FailCodes.LimitsExceeded, false, "result rejected as too large", int.MaxValue, null).ConfigureAwait(false);
                    return;
                case ApiKind.BadRequest or ApiKind.OtherClientError:
                    // A client defect: never retried; release the lease with an internal error (2.7).
                    _log.LogError("job {Job} completion rejected status={Status} code={Code}", job.Id, response.Status, response.Code ?? "none");
                    await FailAsync(job, lease, FailCodes.InternalError, false, "completion rejected", int.MaxValue, null).ConfigureAwait(false);
                    return;
                case ApiKind.Unauthorized:
                    _status.OnAuthFailed();
                    return;
                case ApiKind.RouteAbsent or ApiKind.NotImplemented:
                    _status.OnApiUnsupported();
                    return;
                case ApiKind.ProtocolUnsupported:
                    _status.OnProtocolMismatch();
                    return;
                default:
                    // 409 stale_input / resource_gone / result_conflict, 422, 403, 404: logged, never retried (Appendix A).
                    _log.LogWarning("job {Job} completion not accepted status={Status} code={Code}", job.Id, response.Status, response.Code ?? "none");
                    return;
            }
        }
    }

    private async Task FailAsync(ClaimedJob job, JobLease lease, string code, bool retryable, string message, int maxAttempts, TimeSpan? timeout)
    {
        var request = new FailRequest
        {
            Fence = job.Fence,
            Retryable = retryable,
            Code = code,
            Message = FailMessage.Sanitize(message),
            Metrics = new FailMetrics { ElapsedMs = _clock.NowMs - lease.StartedMs, PeakRssMiB = lease.RssMiB },
        };

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // The best-effort fail after a local expiry passes a timeout and is allowed to try once.
            if (!MayStillReport(lease, allowExpired: timeout is not null)) return;

            var response = await _api.FailAsync(job.Id, request, timeout, CancellationToken.None).ConfigureAwait(false);
            if (response.Kind == ApiKind.Ok)
            {
                _log.LogInformation("job {Job} failed code={Code} status={Status}", job.Id, code, response.Value?.Status);
                return;
            }

            if (response.IsLeaseLost)
            {
                lease.Abort(AbortReason.LeaseLost);
                return;
            }

            if (response.Kind == ApiKind.Unauthorized) _status.OnAuthFailed();
            if (!response.Transient) return;
            await _delay(WaitFor(response, attempt), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Reports continue only while the lease may still be live: not lost, not past the local expiry (2.7).</summary>
    private bool MayStillReport(JobLease lease, bool allowExpired = false)
    {
        var reason = lease.Reason;
        if (reason is AbortReason.LeaseLost or AbortReason.Quarantined or AbortReason.AuthFailed or AbortReason.Superseded or AbortReason.StaleInput) return false;
        if (reason == AbortReason.LocalExpiry && !allowExpired) return false;
        return allowExpired || _clock.NowMs < lease.LocalExpiryMs;
    }

    private static TimeSpan WaitFor(ApiResponse response, int attempt) =>
        response.RetryAfter is { } after ? Backoff.RetryAfterWithJitter(after) : Backoff.Compute(attempt);
}
