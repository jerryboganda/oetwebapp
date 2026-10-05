using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>An input of a leased job, ready to stream. Never exposes a storage key or an asset id to the node.</summary>
public sealed record RemoteInputOpen(
    RemoteProblemResult? Error,
    Stream? Stream,
    long Length,
    string Sha256,
    string ContentType)
{
    public static RemoteInputOpen Fail(RemoteProblemResult error) => new(error, null, 0, string.Empty, string.Empty);
}

/// <summary>A manifest entry as stored in <c>RemoteJobs.InputsJson</c> (the storage key stays server-side).</summary>
public sealed record RemoteInputEntry(string Name, long SizeBytes, string Sha256, string ContentType, string StorageKey);

public sealed record RemoteOutputPut(RemoteProblemResult? Error, string? Name, long SizeBytes, string? Sha256, bool Replaced);

/// <summary>
/// Data-plane access of a leased job: reading its named inputs and writing its binary outputs (OET-RWP/1 sections 4.3
/// and 4.4). Every request re-applies the full lease guard, including each Range request, so an expired-but-unreaped
/// lease can neither read nor write. The service never holds a database connection while bytes move: lookups complete
/// first, then the storage stream is opened and handed to the response.
/// </summary>
public sealed partial class RemoteInputOutputService(
    LearnerDbContext db,
    RemoteJobLifecycleService lifecycle,
    IFileStorage storage,
    ILogger<RemoteInputOutputService> logger)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex OutputNamePattern();

    public static string OutputKey(string jobId, long fence, string name) => $"remote-jobs/{jobId}/{fence}/{name}";

    // ── inputs ───────────────────────────────────────────────────────────────

    public async Task<RemoteInputOpen> OpenInputAsync(
        string nodeId,
        string jobId,
        long fence,
        string name,
        bool headOnly,
        CancellationToken ct)
    {
        var check = await lifecycle.GuardAsync(jobId, nodeId, fence, ct);
        if (check.Error is not null) return RemoteInputOpen.Fail(check.Error);
        var row = check.Row!;

        var entry = ReadManifest(row.InputsJson).FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
        if (entry is null) return RemoteInputOpen.Fail(RemoteProblems.NotFound("input_not_found", "No such input."));

        // From here on no database connection is open: the guard and the manifest lookup are complete.
        try
        {
            if (entry.StorageKey.StartsWith(RemoteCanary.EmbeddedKeyPrefix, StringComparison.Ordinal))
            {
                if (entry.StorageKey != RemoteCanary.EmbeddedKey) return RemoteInputOpen.Fail(RemoteProblems.NotFound("input_not_found", "No such input."));
                return new RemoteInputOpen(null, headOnly ? null : RemoteCanary.OpenRead(), RemoteCanary.PdfSize, RemoteCanary.PdfSha256, entry.ContentType);
            }

            long length;
            Stream? stream = null;
            if (headOnly)
            {
                length = await storage.LengthAsync(entry.StorageKey, ct);
            }
            else
            {
                var opened = await storage.OpenReadWithMetadataAsync(entry.StorageKey, ct);
                stream = opened.Stream;
                length = opened.Length;
            }

            if (length != entry.SizeBytes)
            {
                // The manifest is stale: withdraw the job so the producer re-enqueues with a fresh fingerprint.
                if (stream is not null) await stream.DisposeAsync();
                await lifecycle.CancelLeasedAsync(jobId, nodeId, fence, "stale_input", ct);
                return RemoteInputOpen.Fail(RemoteProblems.Conflict("stale_input", "The input changed since the job was created."));
            }

            return new RemoteInputOpen(null, stream, length, entry.Sha256, entry.ContentType);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            await lifecycle.CancelLeasedAsync(jobId, nodeId, fence, "stale_input", ct);
            return RemoteInputOpen.Fail(RemoteProblems.Conflict("stale_input", "The input no longer exists."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning(ex, "Storage failure while opening an input of job {JobId}.", jobId);
            return RemoteInputOpen.Fail(RemoteProblems.ServiceUnavailable("storage_unavailable", "Storage is temporarily unavailable."));
        }
    }

    internal static IReadOnlyList<RemoteInputEntry> ReadManifest(string inputsJson)
    {
        var entries = new List<RemoteInputEntry>();
        try
        {
            using var document = JsonDocument.Parse(inputsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return entries;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var name = Str(element, "name");
                var sha = Str(element, "sha256");
                var key = Str(element, "storageKey");
                if (name is null || sha is null || key is null) continue;
                var size = element.TryGetProperty("sizeBytes", out var sizeElement) && sizeElement.TryGetInt64(out var parsed) ? parsed : -1;
                entries.Add(new RemoteInputEntry(name, size, sha, Str(element, "contentType") ?? "application/octet-stream", key));
            }
        }
        catch (JsonException)
        {
            // An unreadable manifest has no inputs.
        }

        return entries;
    }

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // ── outputs ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Streams one binary output to its final per-job key while hashing it. A declared-versus-computed hash mismatch deletes
    /// the object (<c>422 output_hash_mismatch</c>); a lease lost during the upload deletes it too and answers 409.
    /// </summary>
    public async Task<RemoteOutputPut> PutOutputAsync(
        string nodeId,
        string jobId,
        long fence,
        string name,
        string? declaredSha256,
        long? contentLength,
        Stream body,
        CancellationToken ct)
    {
        var check = await lifecycle.GuardAsync(jobId, nodeId, fence, ct);
        if (check.Error is not null) return Failed(check.Error);
        var row = check.Row!;

        var spec = RemoteJobKinds.Find(row.Kind);
        var limits = RemoteJobLimits.FromJson(row.LimitsJson, spec is null
            ? new RemoteJobLimits(1, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            : RemoteJobKinds.LimitsFor(spec, row.Purpose));

        if (limits.MaxOutputBytes <= 0 || limits.MaxOutputs <= 0)
        {
            return Failed(RemoteProblems.Forbidden("outputs_not_permitted", "This job kind does not accept outputs."));
        }

        if (!OutputNamePattern().IsMatch(name)) return Failed(RemoteProblems.BadRequest("The output name is invalid."));
        if (!RemoteIds.IsSha256Hex(declaredSha256)) return Failed(RemoteProblems.BadRequest("X-Content-SHA256 is required."));
        if (contentLength is null or < 0) return Failed(RemoteProblems.BadRequest("Content-Length is required (chunked uploads are not accepted)."));

        var existing = await RemoteDb.QueryAsync(
            db,
            """
            SELECT "Name", "SizeBytes", "Sha256" FROM "RemoteJobOutputs" WHERE "JobId" = @id AND "Fence" = @fence;
            """,
            parameters =>
            {
                parameters.AddWithValue("id", jobId);
                parameters.AddWithValue("fence", fence);
            },
            reader => (Name: RemoteDb.Str(reader, "Name"), Size: RemoteDb.Long(reader, "SizeBytes"), Sha: RemoteDb.Str(reader, "Sha256")),
            ct);

        var others = existing.Where(output => output.Name != name).ToList();
        var previous = existing.FirstOrDefault(output => output.Name == name);
        if (others.Count + 1 > limits.MaxOutputs || others.Sum(output => output.Size) + contentLength.Value > limits.MaxOutputBytes)
        {
            return Failed(RemoteProblems.PayloadTooLarge("output_too_large", "The output exceeds the limits of this job."));
        }

        var key = OutputKey(jobId, fence, name);
        long written = 0;
        string computed;
        try
        {
            await using var destination = await storage.OpenWriteAsync(key, ct);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            while (true)
            {
                var read = await body.ReadAsync(buffer, ct);
                if (read == 0) break;
                written += read;
                if (written > contentLength.Value || written > limits.MaxOutputBytes)
                {
                    await destination.DisposeAsync();
                    await storage.DeleteAsync(key, ct);
                    return Failed(RemoteProblems.PayloadTooLarge("output_too_large", "The output exceeds its declared length."));
                }

                hasher.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            computed = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning(ex, "Storage failure while writing an output of job {JobId}.", jobId);
            await TryDeleteAsync(key);
            return Failed(RemoteProblems.ServiceUnavailable("storage_unavailable", "Storage is temporarily unavailable."));
        }

        if (written != contentLength.Value || !string.Equals(computed, declaredSha256, StringComparison.Ordinal))
        {
            await TryDeleteAsync(key);
            return Failed(RemoteProblems.Unprocessable("output_hash_mismatch", "The uploaded bytes do not match X-Content-SHA256."));
        }

        // The upload can outlive the lease: re-check before the output counts.
        var stillLive = await lifecycle.GuardAsync(jobId, nodeId, fence, ct);
        if (stillLive.Error is not null)
        {
            await TryDeleteAsync(key);
            return Failed(stillLive.Error);
        }

        await RemoteDb.ExecuteAsync(
            db,
            """
            INSERT INTO "RemoteJobOutputs" ("JobId", "Fence", "Name", "Sha256", "SizeBytes", "StorageKey", "CreatedAt")
            VALUES (@id, @fence, @name, @sha, @size, @key, clock_timestamp())
            ON CONFLICT ("JobId", "Fence", "Name") DO UPDATE SET
                "Sha256" = EXCLUDED."Sha256", "SizeBytes" = EXCLUDED."SizeBytes", "StorageKey" = EXCLUDED."StorageKey",
                "CreatedAt" = clock_timestamp();
            """,
            parameters =>
            {
                parameters.AddWithValue("id", jobId);
                parameters.AddWithValue("fence", fence);
                parameters.AddWithValue("name", name);
                parameters.AddWithValue("sha", computed);
                parameters.AddWithValue("size", written);
                parameters.AddWithValue("key", key);
            },
            ct);

        var replaced = previous.Name is not null && !string.Equals(previous.Sha, computed, StringComparison.Ordinal);
        return new RemoteOutputPut(null, name, written, computed, replaced);
    }

    private async Task TryDeleteAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not delete a rejected output object.");
        }
    }

    private static RemoteOutputPut Failed(RemoteProblemResult error) => new(error, null, 0, null, false);
}
