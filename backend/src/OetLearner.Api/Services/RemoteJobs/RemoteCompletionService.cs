using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Answer of <c>POST complete</c>: an error, a rejected (content-class) result, or the accepted outcome.</summary>
public sealed record RemoteCompleteOutcome(
    RemoteProblemResult? Error,
    string? Status,
    string? Outcome,
    bool Replayed,
    string? Code)
{
    public static RemoteCompleteOutcome Fail(RemoteProblemResult error) => new(error, null, null, false, null);

    public static RemoteCompleteOutcome Succeeded(string outcome, bool replayed) => new(null, "succeeded", outcome, replayed, null);

    public static RemoteCompleteOutcome Rejected(bool replayed) => new(null, "rejected", null, replayed, RemoteFailCodes.ContentRejected);
}

/// <summary>
/// <c>POST complete</c> (OET-RWP/1 section 4.5). The whole body is read and validated BEFORE any transaction opens; the
/// commit is one transaction in which a fenced compare-and-set moves the job to <c>Succeeded</c> and the kind's applier
/// writes the domain rows, so a result is never applied without the job being settled, or the reverse. A zombie (stale
/// fence, expired or reclaimed lease) finds zero rows and changes nothing. The same <c>(job, fence, resultSha256)</c>
/// replays the stored outcome without running the applier again.
/// </summary>
public sealed class RemoteCompletionService(
    LearnerDbContext db,
    IEnumerable<IRemoteKindHandler> handlers,
    IRemoteJobFlags flags,
    RemoteJobLifecycleService lifecycle,
    RemoteJobsSettings settings,
    TimeProvider timeProvider,
    ILogger<RemoteCompletionService> logger)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const string CasSql = """
        UPDATE "RemoteJobs" SET
            "State"             = 'Succeeded',
            "ResultSha256"      = @resultSha,
            "ResultSummaryJson" = @summary,
            "MetricsJson"       = COALESCE(@metrics, "MetricsJson"),
            "SettledFence"      = @fence,
            "SettledBy"         = @node,
            "SettledCode"       = NULL,
            "FailureCode"       = NULL,
            "CompletedAt"       = clock_timestamp(),
            "LeaseOwner"        = NULL,
            "LeaseExpiresAt"    = NULL,
            "DeadlineAt"        = NULL,
            "ClaimNonce"        = NULL,
            "UpdatedAt"         = clock_timestamp()
        WHERE "Id" = @id AND "State" = 'Leased' AND "LeaseOwner" = @node
          AND "FenceToken" = @fence AND "LeaseExpiresAt" > clock_timestamp()
        RETURNING
        """;

    private static readonly string CasReturningSql = CasSql + " " + RemoteJobRow.Columns("\"RemoteJobs\"") + ";";

    public async Task<RemoteCompleteOutcome> CompleteAsync(
        RemoteWorker node,
        string jobId,
        byte[] body,
        CancellationToken ct)
    {
        PgScope.RequireNpgsql(db);
        var options = settings.Current;

        // 1. Strict UTF-8 on the wire. A body that is not valid UTF-8 cannot even be parsed, so there is no fence
        //    to requeue by: it is a strike, and the lease simply expires.
        string text;
        try
        {
            text = StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            await StrikeAsync(node.Id, "result_invalid", options, ct);
            return RemoteCompleteOutcome.Fail(RemoteProblems.Unprocessable("result_invalid", "The body is not valid UTF-8."));
        }

        // 2. Envelope.
        RemoteCompleteRequestDto? request;
        try
        {
            request = JsonSerializer.Deserialize<RemoteCompleteRequestDto>(text, RemoteJson.Request);
        }
        catch (JsonException)
        {
            return RemoteCompleteOutcome.Fail(RemoteProblems.BadRequest("The body is not a valid result envelope."));
        }

        var envelopeError = ValidateEnvelope(request);
        if (envelopeError is not null) return RemoteCompleteOutcome.Fail(RemoteProblems.BadRequest(envelopeError));
        request = request!;

        var fence = request.Fence!.Value;
        var resultJson = request.ResultJson!;
        var resultSha = request.ResultSha256!;

        // 3. The hash covers the transmitted UTF-8 bytes of resultJson EXACTLY as sent (no canonicalisation).
        var resultBytes = Encoding.UTF8.GetBytes(resultJson);
        var computedSha = RemoteIds.Sha256Hex(resultBytes);
        var hashMatches = string.Equals(computedSha, resultSha, StringComparison.Ordinal);

        var snapshot = await lifecycle.GetSnapshotAsync(jobId, ct);
        if (snapshot is null) return RemoteCompleteOutcome.Fail(RemoteProblems.JobNotFound());
        var row = snapshot.Row;

        // Replay and lost-lease classification come first: they depend only on the row, not on the payload.
        var live = row.State == RemoteJobState.Leased && row.LeaseOwner == node.Id && row.FenceToken == fence && !snapshot.PastExpiry;
        if (!live)
        {
            return Classify(snapshot, node.Id, fence, resultSha, hashMatches);
        }

        if (!hashMatches)
        {
            await RejectAsync(node.Id, jobId, fence, "result_hash_mismatch", options, ct);
            return RemoteCompleteOutcome.Fail(RemoteProblems.Unprocessable("result_hash_mismatch", "resultSha256 does not match the transmitted resultJson."));
        }

        var handler = handlers.FirstOrDefault(candidate => string.Equals(candidate.Kind, row.Kind, StringComparison.Ordinal));
        var spec = RemoteJobKinds.Find(row.Kind);
        if (handler is null || spec is null)
        {
            return RemoteCompleteOutcome.Fail(RemoteProblems.NotImplemented("No handler is registered for this job kind."));
        }

        var limits = RemoteJobLimits.FromJson(row.LimitsJson, RemoteJobKinds.LimitsFor(spec, row.Purpose));

        // Generic size and output checks, then the kind's pure validation.
        if (resultBytes.LongLength > limits.MaxResultBytes)
        {
            await RejectAsync(node.Id, jobId, fence, RemoteFailCodes.ResultInvalid, options, ct);
            return RemoteCompleteOutcome.Fail(RemoteProblems.Unprocessable("result_invalid", "resultJson exceeds the limit for this kind."));
        }

        var outputs = await LoadOutputsAsync(jobId, fence, ct);
        var declared = request.Outputs ?? new List<RemoteOutputRefDto>();
        if (limits.MaxOutputs == 0 && declared.Count > 0)
        {
            await RejectAsync(node.Id, jobId, fence, RemoteFailCodes.ResultInvalid, options, ct);
            return RemoteCompleteOutcome.Fail(RemoteProblems.Unprocessable("result_invalid", "This kind does not accept outputs."));
        }

        var outputMismatch = CompareOutputs(declared, outputs);
        if (outputMismatch is not null)
        {
            await RejectAsync(node.Id, jobId, fence, "output_hash_mismatch", options, ct);
            return RemoteCompleteOutcome.Fail(RemoteProblems.Unprocessable("output_hash_mismatch", outputMismatch));
        }

        var validation = handler.Validate(row, resultJson, outputs, options);
        switch (validation.Status)
        {
            case RemoteValidationStatus.Invalid:
                await RejectAsync(node.Id, jobId, fence, RemoteFailCodes.ResultInvalid, options, ct);
                return RemoteCompleteOutcome.Fail(RemoteProblems.Unprocessable("result_invalid", validation.Message ?? "The result is invalid."));
            case RemoteValidationStatus.EngineMismatch:
                await RejectAsync(node.Id, jobId, fence, "engine_version_mismatch", options, ct);
                return RemoteCompleteOutcome.Fail(RemoteProblems.Unprocessable("engine_version_mismatch", validation.Message ?? "The engine version differs from the job."));
            case RemoteValidationStatus.OutputMismatch:
                await RejectAsync(node.Id, jobId, fence, "output_hash_mismatch", options, ct);
                return RemoteCompleteOutcome.Fail(RemoteProblems.Unprocessable("output_hash_mismatch", validation.Message ?? "The outputs differ from the result."));
            case RemoteValidationStatus.ContentRejected:
                // Content-class: the job fails non-retryably so the local path handles the asset. No strike.
                var rejected = await lifecycle.ApplyFailureAsync(
                    jobId, node.Id, fence, RemoteFailCodes.ContentRejected, "content rejected", retryable: false, refund: false,
                    metricsJson: null, options, ct);
                return rejected is null
                    ? RemoteCompleteOutcome.Fail(await lifecycle.ClassifyLostAsync(jobId, node.Id, fence, ct))
                    : RemoteCompleteOutcome.Rejected(replayed: false);
        }

        // Emergency freeze: refuse to apply, and withdraw the job (lease-guarded).
        if ((await flags.GetAsync(ct)).FreezeApplies)
        {
            var cancelled = await lifecycle.CancelLeasedAsync(jobId, node.Id, fence, "applies_frozen", ct);
            return cancelled
                ? RemoteCompleteOutcome.Fail(RemoteProblems.ServiceUnavailable("applies_frozen", "Result application is frozen.", retryable: false, retryAfterSeconds: null))
                : RemoteCompleteOutcome.Fail(await lifecycle.ClassifyLostAsync(jobId, node.Id, fence, ct));
        }

        return await CommitAsync(node, row, fence, resultSha, resultJson, request, handler, validation, outputs, options, ct);
    }

    private async Task<RemoteCompleteOutcome> CommitAsync(
        RemoteWorker node,
        RemoteJobRow row,
        long fence,
        string resultSha,
        string resultJson,
        RemoteCompleteRequestDto request,
        IRemoteKindHandler handler,
        RemoteResultValidation validation,
        IReadOnlyList<RemoteOutputRow> outputs,
        RemoteJobsOptions options,
        CancellationToken ct)
    {
        var jobId = row.Id;
        var now = timeProvider.GetUtcNow();
        var metricsJson = RemoteJobLifecycleService.BuildMetricsJson(null, request.Metrics);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 5. The fenced compare-and-set. Zero rows: roll back and classify; the applier never runs.
        var settled = await RemoteDb.QueryFirstAsync(
            db,
            CasReturningSql,
            parameters =>
            {
                parameters.AddWithValue("resultSha", resultSha);
                parameters.Add(new NpgsqlParameter("summary", NpgsqlDbType.Jsonb) { Value = (object?)validation.SummaryJson ?? DBNull.Value });
                parameters.Add(new NpgsqlParameter("metrics", NpgsqlDbType.Jsonb) { Value = (object?)metricsJson ?? DBNull.Value });
                parameters.AddWithValue("fence", fence);
                parameters.AddWithValue("node", node.Id);
                parameters.AddWithValue("id", jobId);
            },
            RemoteJobRow.Read,
            ct);

        if (settled is null)
        {
            await transaction.RollbackAsync(ct);
            var after = await lifecycle.GetSnapshotAsync(jobId, ct);
            return after is null
                ? RemoteCompleteOutcome.Fail(RemoteProblems.JobNotFound())
                : Classify(after, node.Id, fence, resultSha, hashMatches: true);
        }

        // 6. The kind's applier, in THIS transaction and on THIS connection.
        RemoteApplyOutcome outcome;
        try
        {
            outcome = await handler.ApplyAsync(
                new RemoteApplyContext(db, settled, validation.Parsed!, resultJson, node.Id, fence, outputs, options, timeProvider),
                ct);
        }
        catch (RemoteApplyConflictException ex)
        {
            await transaction.RollbackAsync(ct);
            logger.LogWarning(ex, "Applier for {Kind} hit repeated concurrency conflicts on job {JobId}.", row.Kind, jobId);
            return RemoteCompleteOutcome.Fail(RemoteProblems.ServiceUnavailable("apply_conflict", "The result could not be applied; retry."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(ex, "Applier for {Kind} failed on job {JobId}; the job stays leased.", row.Kind, jobId);
            return RemoteCompleteOutcome.Fail(RemoteProblems.ServiceUnavailable("apply_failed", "The result could not be applied; retry."));
        }

        if (outcome.Kind == RemoteApplyKind.Discarded)
        {
            // 7. The domain is untouched; the settled row is withdrawn.
            await RemoteDb.ExecuteAsync(
                db,
                """
                UPDATE "RemoteJobs" SET "State" = 'Cancelled', "ApplyOutcome" = 'Discarded', "FailureCode" = @reason,
                    "UpdatedAt" = clock_timestamp()
                WHERE "Id" = @id AND "State" = 'Succeeded' AND "SettledFence" = @fence;
                """,
                parameters =>
                {
                    parameters.AddWithValue("reason", outcome.Reason ?? "discarded");
                    parameters.AddWithValue("id", jobId);
                    parameters.AddWithValue("fence", fence);
                },
                ct);
            RemoteAudit.Add(db, RemoteAudit.NodeActor(node.Id), "remote-worker", "RemoteJob.Discarded", RemoteAudit.ResourceJob, jobId,
                new { kind = row.Kind, reason = outcome.Reason, fence }, now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return RemoteCompleteOutcome.Fail(DiscardProblem(outcome.Reason));
        }

        // 8. Record the outcome; Deferred parks the validated result for an API-side consumer.
        await RemoteDb.ExecuteAsync(
            db,
            """
            UPDATE "RemoteJobs" SET "ApplyOutcome" = @outcome, "ResultJson" = @resultJson, "UpdatedAt" = clock_timestamp()
            WHERE "Id" = @id AND "State" = 'Succeeded' AND "SettledFence" = @fence;
            """,
            parameters =>
            {
                parameters.AddWithValue("outcome", outcome.WireName);
                parameters.Add(new NpgsqlParameter("resultJson", NpgsqlDbType.Text)
                {
                    Value = outcome.Kind == RemoteApplyKind.Deferred ? (object)resultJson : DBNull.Value,
                });
                parameters.AddWithValue("id", jobId);
                parameters.AddWithValue("fence", fence);
            },
            ct);

        if (outcome.Kind is RemoteApplyKind.Applied or RemoteApplyKind.Deferred)
        {
            RemoteAudit.Add(db, RemoteAudit.NodeActor(node.Id), "remote-worker", "RemoteJob.Applied", RemoteAudit.ResourceJob, jobId,
                new { kind = row.Kind, purpose = row.Purpose, outcome = outcome.WireName, fence, inputSha256 = row.InputSha256, detail = outcome.Detail }, now);
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return RemoteCompleteOutcome.Succeeded(outcome.WireName, replayed: false);
    }

    /// <summary>
    /// Zero-row classification (OET-RWP/1 section 4.5.3): replay, result conflict, or a lost lease with its reason.
    /// </summary>
    internal static RemoteCompleteOutcome Classify(
        RemoteJobSnapshot snapshot,
        string nodeId,
        long fence,
        string resultSha,
        bool hashMatches)
    {
        var row = snapshot.Row;

        // A retried complete after a lost success response: same (fence, node, hash) is a replay.
        if (row.State == RemoteJobState.Succeeded && row.SettledFence == fence && row.SettledBy == nodeId)
        {
            return hashMatches && string.Equals(row.ResultSha256, resultSha, StringComparison.Ordinal)
                ? RemoteCompleteOutcome.Succeeded(row.ApplyOutcome ?? "Applied", replayed: true)
                : RemoteCompleteOutcome.Fail(RemoteProblems.Conflict("result_conflict", "A different result was already accepted for this fence."));
        }

        // A discarded result replays as the same discard.
        if (row.State == RemoteJobState.Cancelled && row.SettledFence == fence && row.SettledBy == nodeId
            && row.ApplyOutcome == "Discarded" && string.Equals(row.ResultSha256, resultSha, StringComparison.Ordinal))
        {
            return RemoteCompleteOutcome.Fail(DiscardProblem(row.FailureCode));
        }

        // A content-rejected result replays as the same rejection.
        if (row.State == RemoteJobState.Failed && row.SettledFence == fence && row.SettledBy == nodeId
            && row.SettledCode == RemoteFailCodes.ContentRejected)
        {
            return RemoteCompleteOutcome.Rejected(replayed: true);
        }

        return RemoteCompleteOutcome.Fail(RemoteProblems.LeaseLost(RemoteLeaseClassifier.LostReason(snapshot, nodeId, fence)));
    }

    private static RemoteProblemResult DiscardProblem(string? reason)
        => reason == "resource_gone"
            ? RemoteProblems.Conflict("resource_gone", "The resource no longer exists.")
            : RemoteProblems.Conflict("stale_input", "The input changed since the job was created.");

    private static string? ValidateEnvelope(RemoteCompleteRequestDto? request)
    {
        if (request is null) return "A JSON body is required.";
        if (request.Fence is null or < 0) return "fence is required.";
        if (!RemoteIds.IsSha256Hex(request.ResultSha256)) return "resultSha256 must be 64 lowercase hex characters.";
        if (request.ResultJson is null) return "resultJson is required.";
        if (request.Outputs is { Count: > 64 }) return "outputs has too many entries.";
        if (request.Metrics is { Count: > 32 }) return "metrics has too many entries.";
        if (request.Outputs is not null)
        {
            foreach (var output in request.Outputs)
            {
                if (output is null || string.IsNullOrEmpty(output.Name) || output.SizeBytes is null or < 0
                    || !RemoteIds.IsSha256Hex(output.Sha256))
                {
                    return "outputs[] entries need name, sizeBytes and sha256.";
                }
            }
        }

        return null;
    }

    /// <summary>Declared outputs must match the uploaded rows exactly (name, size, hash); null means they do.</summary>
    internal static string? CompareOutputs(IReadOnlyList<RemoteOutputRefDto> declared, IReadOnlyList<RemoteOutputRow> uploaded)
    {
        foreach (var output in declared)
        {
            var match = uploaded.FirstOrDefault(row => string.Equals(row.Name, output.Name, StringComparison.Ordinal));
            if (match is null || match.SizeBytes != output.SizeBytes || !string.Equals(match.Sha256, output.Sha256, StringComparison.Ordinal))
            {
                return "A declared output does not match what was uploaded.";
            }
        }

        return null;
    }

    private Task<List<RemoteOutputRow>> LoadOutputsAsync(string jobId, long fence, CancellationToken ct)
        => RemoteDb.QueryAsync(
            db,
            """
            SELECT "Name", "SizeBytes", "Sha256", "StorageKey" FROM "RemoteJobOutputs"
            WHERE "JobId" = @id AND "Fence" = @fence ORDER BY "Name";
            """,
            parameters =>
            {
                parameters.AddWithValue("id", jobId);
                parameters.AddWithValue("fence", fence);
            },
            reader => new RemoteOutputRow(
                RemoteDb.Str(reader, "Name"),
                RemoteDb.Long(reader, "SizeBytes"),
                RemoteDb.Str(reader, "Sha256"),
                RemoteDb.Str(reader, "StorageKey")),
            ct);

    /// <summary>A rejected result: strike the node and requeue the job (a retryable failure that consumes the attempt).</summary>
    private async Task RejectAsync(string nodeId, string jobId, long fence, string code, RemoteJobsOptions options, CancellationToken ct)
    {
        await lifecycle.ApplyFailureAsync(jobId, nodeId, fence, code, null, retryable: true, refund: false, metricsJson: null, options, ct);
        await StrikeAsync(nodeId, code, options, ct);
    }

    private Task StrikeAsync(string nodeId, string reason, RemoteJobsOptions options, CancellationToken ct)
        => RemoteNodeOps.AddStrikeAsync(db, nodeId, reason, options, timeProvider.GetUtcNow(), ct);
}
