using System.Buffers;
using System.Security.Cryptography;

namespace Fleet.Agent;

/// <summary>
/// Streams manifest inputs to tmpfs while hashing and verifies size and SHA-256 (protocol 4.3, RW-068); uploads binary outputs
/// with retries bounded by the local lease expiry (4.4, 2.7). Resumes an interrupted download with Range inside the same lease.
/// </summary>
internal sealed class ApiJobIo : IJobIo
{
    private const int MaxFetchAttempts = 5;

    private readonly IRemoteWorkerApi _api;
    private readonly InputCache _cache;
    private readonly AgentStatus _status;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action? _cacheChanged;

    public ApiJobIo(IRemoteWorkerApi api, InputCache cache, AgentStatus status, IMonotonicClock clock, ILogger log,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Action? cacheChanged = null)
    {
        _api = api;
        _cache = cache;
        _status = status;
        _clock = clock;
        _log = log;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _cacheChanged = cacheChanged;
    }

    public void OfferToCache(string sha256, string verifiedFile, long size)
    {
        if (_cache.TryAdopt(sha256, verifiedFile, size)) _cacheChanged?.Invoke();
    }

    public async Task<InputFile> FetchInputAsync(ClaimedJob job, JobLease lease, InputRef input, string destinationPath, bool cacheable, CancellationToken ct)
    {
        if (!Wire.InputNamePattern.IsMatch(input.Name) || !Wire.Sha256HexPattern.IsMatch(input.Sha256) || input.SizeBytes < 0)
        {
            throw new JobFailureException(FailCodes.InternalError, false, "malformed input manifest");
        }

        // Refuse oversize inputs WITHOUT downloading (RW-068).
        if (input.SizeBytes > job.Limits.MaxInputBytes)
        {
            throw new JobFailureException(FailCodes.InputTooLarge, false, "input exceeds limits.maxInputBytes");
        }

        if (cacheable)
        {
            var pinned = _cache.TryAcquire(input.Sha256, input.SizeBytes);
            if (pinned is not null)
            {
                _log.LogInformation("input served from the local cache kind={Kind} bytes={Bytes}", job.Kind, input.SizeBytes);
                return new InputFile(pinned.Path, input.SizeBytes, input.Sha256, true, pinned);
            }
        }

        var partial = destinationPath + ".part";
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long have = 0;
        string? etag = null;
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            await using var file = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 81920, FileOptions.Asynchronous);
            for (var attempt = 0; attempt < MaxFetchAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                using var response = await _api.OpenInputAsync(job.Id, input.Name, job.Fence, have, etag ?? input.Sha256, ct).ConfigureAwait(false);
                var info = response.Info;

                if (info.Kind == ApiKind.RangeNotSatisfiable)
                {
                    have = 0;
                    file.SetLength(0);
                    file.Position = 0;
                    hasher.GetHashAndReset();
                    continue;
                }

                if (info.Kind != ApiKind.Ok || response.Body is null)
                {
                    await HandleOpenFailureAsync(info, attempt, ct).ConfigureAwait(false);
                    continue;
                }

                etag = response.ETag ?? etag;
                if (response.ContentSha256 is { } declared && !Hashing.HexEquals(declared, input.Sha256))
                {
                    throw new JobFailureException(FailCodes.InputHashMismatch, true, "declared digest differs from the manifest");
                }

                if (have > 0 && !(response.Partial && response.RangeStart == have))
                {
                    // The server ignored the Range request: start over from byte 0 of this answer.
                    have = 0;
                    file.SetLength(0);
                    file.Position = 0;
                    hasher.GetHashAndReset();
                }
                else if (have == 0 && response.ContentLength is { } announced && announced != input.SizeBytes)
                {
                    throw new JobFailureException(FailCodes.InputHashMismatch, true, "content length differs from the manifest");
                }

                var interrupted = false;
                while (true)
                {
                    int read;
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(Wire.InputIdleTimeout);
                    try
                    {
                        read = await response.Body.ReadAsync(buffer.AsMemory(0, 81920), idle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        interrupted = true;
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException)
                    {
                        interrupted = true;
                        break;
                    }

                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hasher.AppendData(buffer, 0, read);
                    have += read;
                    if (have > input.SizeBytes) throw new JobFailureException(FailCodes.InputHashMismatch, true, "input longer than the manifest");
                }

                if (!interrupted && have == input.SizeBytes) break;
                if (attempt == MaxFetchAttempts - 1) throw new JobFailureException(FailCodes.InputUnavailable, true, "download did not complete");
                await _delay(Backoff.Compute(attempt), ct).ConfigureAwait(false);
            }

            await file.FlushAsync(ct).ConfigureAwait(false);
            if (have != input.SizeBytes)
            {
                throw new JobFailureException(FailCodes.InputHashMismatch, true, "input size differs from the manifest");
            }

            var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (!Hashing.HexEquals(actual, input.Sha256))
            {
                throw new JobFailureException(FailCodes.InputHashMismatch, true, "input digest differs from the manifest");
            }
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        File.Move(partial, destinationPath, overwrite: true);
        return new InputFile(destinationPath, input.SizeBytes, input.Sha256, false, null);
    }

    public async Task<OutputRef> UploadOutputAsync(ClaimedJob job, JobLease lease, string name, string filePath, CancellationToken ct)
    {
        var size = new FileInfo(filePath).Length;
        var sha = await Hashing.Sha256FileAsync(filePath, ct).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (_clock.NowMs >= lease.LocalExpiryMs) throw new JobAbandonException(AbortReason.LocalExpiry);

            var response = await _api.PutOutputAsync(job.Id, name, job.Fence, sha, filePath, ct).ConfigureAwait(false);
            if (response.Kind == ApiKind.Ok)
            {
                if (response.Value is { } ack && (ack.Sha256 != sha || ack.SizeBytes != size))
                {
                    throw new JobFailureException(FailCodes.InternalError, true, "output acknowledgement mismatch");
                }

                return new OutputRef { Name = name, SizeBytes = size, Sha256 = sha };
            }

            switch (response.Kind)
            {
                case ApiKind.Conflict when response.IsLeaseLost:
                    throw new JobAbandonException(AbortReason.LeaseLost);
                case ApiKind.Conflict:
                    throw new JobAbandonException(AbortReason.StaleInput);
                case ApiKind.Unauthorized:
                    _status.OnAuthFailed();
                    throw new JobAbandonException(AbortReason.AuthFailed);
                case ApiKind.Forbidden when response.Code == "node_quarantined":
                    throw new JobAbandonException(AbortReason.Quarantined);
                case ApiKind.Forbidden:
                    throw new JobFailureException(FailCodes.InternalError, false, "outputs not permitted");
                case ApiKind.PayloadTooLarge:
                    throw new JobFailureException(FailCodes.LimitsExceeded, false, "output too large");
                case ApiKind.Unprocessable:
                    throw new JobFailureException(FailCodes.InternalError, true, "output hash rejected");
                case ApiKind.RouteAbsent or ApiKind.NotImplemented or ApiKind.ProtocolUnsupported:
                    NoteSkew(response);
                    throw new JobAbandonException(AbortReason.LeaseLost);
            }

            if (!response.Transient && response.Kind != ApiKind.Canceled)
            {
                throw new JobFailureException(FailCodes.InternalError, false, "output upload rejected");
            }

            if (response.Kind == ApiKind.Canceled) throw new OperationCanceledException(ct);
            var wait = response.RetryAfter is { } after ? Backoff.RetryAfterWithJitter(after) : Backoff.Compute(attempt);
            await _delay(wait, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleOpenFailureAsync(ApiResponse info, int attempt, CancellationToken ct)
    {
        switch (info.Kind)
        {
            case ApiKind.Conflict when info.IsLeaseLost:
                throw new JobAbandonException(AbortReason.LeaseLost);
            case ApiKind.Conflict:
                // stale_input / resource_gone: the manifest no longer matches the object; discard, no retry (Appendix A).
                throw new JobAbandonException(AbortReason.StaleInput);
            case ApiKind.NotFound:
                throw new JobAbandonException(info.Code == "job_not_found" ? AbortReason.LeaseLost : AbortReason.StaleInput);
            case ApiKind.Unauthorized:
                _status.OnAuthFailed();
                throw new JobAbandonException(AbortReason.AuthFailed);
            case ApiKind.Forbidden when info.Code == "node_quarantined":
                throw new JobAbandonException(AbortReason.Quarantined);
            case ApiKind.Forbidden:
                throw new JobFailureException(FailCodes.InputUnavailable, true, "input access forbidden");
            case ApiKind.RouteAbsent or ApiKind.NotImplemented or ApiKind.ProtocolUnsupported:
                NoteSkew(info);
                throw new JobAbandonException(AbortReason.LeaseLost);
            case ApiKind.Canceled:
                throw new OperationCanceledException(ct);
        }

        if (!info.Transient)
        {
            throw new JobFailureException(FailCodes.InternalError, false, "input request rejected");
        }

        if (attempt >= MaxFetchAttempts - 1) throw new JobFailureException(FailCodes.InputUnavailable, true, "input unavailable");
        var wait = info.RetryAfter is { } after ? Backoff.RetryAfterWithJitter(after) : Backoff.Compute(attempt);
        await _delay(wait, ct).ConfigureAwait(false);
    }

    private void NoteSkew(ApiResponse info)
    {
        if (info.Kind == ApiKind.ProtocolUnsupported) _status.OnProtocolMismatch();
        else _status.OnApiUnsupported();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The job directory is removed wholesale right after.
        }
    }
}
