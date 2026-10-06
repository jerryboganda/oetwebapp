using System.Globalization;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>A JSON 200 (or other status) whose body is an object tree, written camelCase with the protocol's response options.</summary>
public sealed class RemoteJsonResult(object body, int statusCode = StatusCodes.Status200OK, IReadOnlyDictionary<string, string>? headers = null) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        if (response.HasStarted) return;

        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        if (headers is not null)
        {
            foreach (var (name, value) in headers) response.Headers[name] = value;
        }

        await JsonSerializer.SerializeAsync(response.Body, body, RemoteJson.Response, httpContext.RequestAborted);
    }
}

/// <summary>A <c>204</c> from claim: no body, a machine reason, a retry hint and the desired-state revision (OET-RWP/1 section 4.1.3).</summary>
public sealed class RemoteNoContentResult(string reason, long desiredRevision) : IResult
{
    /// <summary><c>Retry-After</c> seconds per reason (the table of section 4.1.3).</summary>
    public static int RetryAfterSeconds(string reason) => reason switch
    {
        "no_work" => 10,
        "no_capacity" => 5,
        "heartbeat_stale" => 5,
        "fair_share" => 5,
        _ => 30,
    };

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        if (response.HasStarted) return Task.CompletedTask;

        response.StatusCode = StatusCodes.Status204NoContent;
        response.Headers[RemoteHeaders.Reason] = reason;
        response.Headers.RetryAfter = RetryAfterSeconds(reason).ToString(CultureInfo.InvariantCulture);
        response.Headers[RemoteHeaders.DesiredRevision] = desiredRevision.ToString(CultureInfo.InvariantCulture);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Streams a leased job's input (OET-RWP/1 section 4.3): <c>200</c> or single-range <c>206</c>, <c>HEAD</c> without a body,
/// <c>ETag</c>/<c>X-Content-SHA256</c> from the manifest, <c>Cache-Control: no-store</c>. The stream and the per-node stream
/// permit are released when the response finishes, however it finishes; no database connection is involved here.
/// </summary>
public sealed class RemoteStreamResult(
    Stream? stream,
    long length,
    string sha256,
    string contentType,
    bool headOnly,
    IDisposable? permit) : IResult
{
    private const int BufferSize = 81920;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        try
        {
            var request = httpContext.Request;
            var response = httpContext.Response;
            var etag = "\"" + sha256 + "\"";

            response.Headers[RemoteHeaders.ContentSha256] = sha256;
            response.Headers.ETag = etag;
            response.Headers.AcceptRanges = "bytes";
            response.Headers.CacheControl = "no-store";
            response.ContentType = "application/octet-stream";
            _ = contentType; // the manifest's type is informational; bytes are always served as octet-stream.

            var rangeHeader = request.Headers.Range.ToString();
            var ifRange = request.Headers.IfRange.ToString();
            // If-Range with a validator that does not match means "send the whole thing".
            if (!string.IsNullOrEmpty(ifRange) && !string.Equals(ifRange.Trim(), etag, StringComparison.Ordinal))
            {
                rangeHeader = string.Empty;
            }

            var range = RemoteRangeParser.Parse(rangeHeader, length);
            if (range.Kind == RemoteRangeKind.Unsatisfiable)
            {
                response.Headers.ContentRange = $"bytes */{length}";
                await RemoteProblems.RangeNotSatisfiable(length).ExecuteAsync(httpContext);
                return;
            }

            long start = 0;
            var count = length;
            if (range.Kind == RemoteRangeKind.Partial)
            {
                start = range.Start;
                count = range.Length;
                response.StatusCode = StatusCodes.Status206PartialContent;
                response.Headers.ContentRange = $"bytes {range.Start}-{range.End}/{length}";
            }
            else
            {
                response.StatusCode = StatusCodes.Status200OK;
            }

            response.ContentLength = count;
            if (headOnly || stream is null) return;

            await SkipAsync(stream, start, httpContext.RequestAborted);
            var buffer = new byte[BufferSize];
            var remaining = count;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), httpContext.RequestAborted);
                if (read == 0) break;
                await response.Body.WriteAsync(buffer.AsMemory(0, read), httpContext.RequestAborted);
                remaining -= read;
            }
        }
        finally
        {
            if (stream is not null) await stream.DisposeAsync();
            permit?.Dispose();
        }
    }

    private static async Task SkipAsync(Stream stream, long bytes, CancellationToken ct)
    {
        if (bytes <= 0) return;
        if (stream.CanSeek)
        {
            stream.Seek(bytes, SeekOrigin.Begin);
            return;
        }

        // Providers that cannot seek (remote object stores) are read and discarded up to the start of the range.
        var scratch = new byte[BufferSize];
        var remaining = bytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(scratch.AsMemory(0, (int)Math.Min(scratch.Length, remaining)), ct);
            if (read == 0) break;
            remaining -= read;
        }
    }
}

/// <summary>Bounded reading of small JSON request bodies for the job and service planes.</summary>
public static class RemoteRequestBody
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads at most <paramref name="maxBytes"/> bytes; a longer body (declared or actual) is a <c>413</c>.</summary>
    public static async Task<(byte[]? Body, RemoteProblemResult? Error)> ReadAsync(HttpContext http, long maxBytes, CancellationToken ct)
    {
        var request = http.Request;
        if (request.ContentLength is long declared && declared > maxBytes)
        {
            return (null, RemoteProblems.PayloadTooLarge("result_too_large", "The request body is too large."));
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        long total = 0;
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, ct);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
            {
                return (null, RemoteProblems.PayloadTooLarge("result_too_large", "The request body is too large."));
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), null);
    }

    /// <summary>Reads and deserialises a JSON body with the strict request options; any defect is a <c>400 bad_request</c>.</summary>
    public static async Task<(T? Value, RemoteProblemResult? Error)> ReadJsonAsync<T>(HttpContext http, long maxBytes, CancellationToken ct)
        where T : class
    {
        var (body, error) = await ReadAsync(http, maxBytes, ct);
        if (error is not null || body is null) return (null, error ?? RemoteProblems.BadRequest("A JSON body is required."));

        try
        {
            var value = JsonSerializer.Deserialize<T>(StrictUtf8.GetString(body), RemoteJson.Request);
            if (value is null) return (null, RemoteProblems.BadRequest("A JSON body is required."));
            return (value, null);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or NotSupportedException)
        {
            return (null, RemoteProblems.BadRequest("The body is not valid JSON."));
        }
    }
}

/// <summary>Builds the wire form of a leased job for the claim response (OET-RWP/1 section 4.1.3). No storage key, no asset id.</summary>
public static class RemoteJobWire
{
    public static Dictionary<string, object?> ToClaimJob(RemoteJobRow row, long leaseRemainingMs, RemoteJobsOptions options)
    {
        var spec = RemoteJobKinds.Find(row.Kind);
        var limits = RemoteJobLimits.FromJson(
            row.LimitsJson, spec is null ? new RemoteJobLimits(1, 0, 0, 0, 0, 0, 0, 0, 0, 0) : RemoteJobKinds.LimitsFor(spec, row.Purpose));

        var inputs = RemoteInputOutputService.ReadManifest(row.InputsJson)
            .Select(entry => new Dictionary<string, object?>
            {
                ["name"] = entry.Name,
                ["sizeBytes"] = entry.SizeBytes,
                ["sha256"] = entry.Sha256,
                ["contentType"] = entry.ContentType,
            })
            .ToList();

        return new Dictionary<string, object?>
        {
            ["id"] = row.Id,
            ["kind"] = row.Kind,
            ["schemaVersion"] = row.SchemaVersion,
            ["purpose"] = row.Purpose,
            ["engineVersion"] = row.EngineVersion,
            ["attempt"] = row.Attempt,
            ["maxAttempts"] = row.MaxAttempts,
            ["fence"] = row.FenceToken,
            ["leaseExpiresAt"] = RemoteIds.FormatTime(row.LeaseExpiresAt),
            ["leaseRemainingMs"] = leaseRemainingMs,
            ["heartbeatEverySeconds"] = options.HeartbeatEverySeconds,
            ["deadlineAt"] = RemoteIds.FormatTime(row.DeadlineAt),
            ["deadlineSeconds"] = limits.DeadlineSeconds,
            ["inputs"] = inputs,
            ["params"] = ParseElement(row.ParamsJson),
            ["limits"] = new Dictionary<string, object?>
            {
                ["weight"] = limits.Weight,
                ["cpuMilli"] = limits.CpuMilli,
                ["memMiB"] = limits.MemMiB,
                ["tmpMiB"] = limits.TmpMiB,
                ["timeoutSeconds"] = limits.TimeoutSeconds,
                ["maxInputBytes"] = limits.MaxInputBytes,
                ["maxResultBytes"] = limits.MaxResultBytes,
                ["maxOutputBytes"] = limits.MaxOutputBytes,
                ["maxOutputs"] = limits.MaxOutputs,
            },
        };
    }

    private static object? ParseElement(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
