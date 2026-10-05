using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>A parsed <c>media.speaking-join.result/1</c> (OET-RWP/1 section 6.4). Never trusted until validated.</summary>
public sealed record SpeakingJoinResult(
    string Schema,
    string EngineVersion,
    string InputSha256,
    int ClipCount,
    int DurationMs,
    bool Truncated,
    int Mp3Bytes,
    string Mp3Sha256);

/// <summary>Parameters, fingerprint and input rules of <c>media.speaking-join</c> v1 (OET-RWP/1 section 6.4).</summary>
public static class SpeakingJoinSettings
{
    public const string ResourceType = "SpeakingSession";

    /// <summary>The single output: the joined mp3.</summary>
    public const string OutputName = "join.mp3";

    /// <summary>The procedure id: <c>FfmpegSpeakingAudioTranscoder.JoinToMp3Async</c> + <see cref="PcmJoiner"/>, replicated.</summary>
    public const string Engine = "ffmpeg-pcm-join/1";

    public const int BitrateKbps = 48;
    public const int DecodeTimeoutSeconds = 60;
    public const int EncodeTimeoutSeconds = 90;

    public const int MaxClips = 64;
    public const long MaxClipBytes = 16L * 1024 * 1024;
    public const long MaxTotalBytes = 64L * 1024 * 1024;

    /// <summary>The audio the agent is offered (anything else is joined locally).</summary>
    private static readonly HashSet<string> ContentTypes = new(StringComparer.Ordinal)
    {
        "audio/webm", "video/webm", "audio/mp4", "audio/aac", "audio/wav", "audio/mpeg", "audio/ogg",
    };

    /// <summary>The canonical content type of a recorded mime type (parameters stripped), or null when the agent is not offered it.</summary>
    public static string? NormalizeContentType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return null;
        var type = mimeType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (type is "audio/x-wav" or "audio/wave" or "audio/vnd.wave") type = "audio/wav";
        return ContentTypes.Contains(type) ? type : null;
    }

    public static string InputName(int index) => "clip-" + index.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>The job's input fingerprint: the ORDERED clip hashes joined by <c>,</c> (OET-RWP/1 section 6.0 rule 1).</summary>
    public static string InputSha256(IEnumerable<string> orderedClipSha256s) => RemoteIds.Sha256Hex(string.Join(",", orderedClipSha256s));

    public static string Hash(int gapMilliseconds, int maxAudioSeconds)
        => RemoteJobKeys.SettingsHash(
        [
            new KeyValuePair<string, string>("bitrateKbps", BitrateKbps.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("engine", Engine),
            new KeyValuePair<string, string>("gapMilliseconds", gapMilliseconds.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("maxAudioSeconds", maxAudioSeconds.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("sampleRateHz", PcmJoiner.SampleRate.ToString(CultureInfo.InvariantCulture)),
        ]);

    /// <summary>The job parameters offered to the agent (nothing about the learner or the session: <c>params</c> is sent as is).</summary>
    public static string ParamsJson(int gapMilliseconds, int maxAudioSeconds)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["gapMilliseconds"] = gapMilliseconds,
            ["maxAudioSeconds"] = maxAudioSeconds,
            ["sampleRateHz"] = PcmJoiner.SampleRate,
            ["bitrateKbps"] = BitrateKbps,
            ["decodeTimeoutSeconds"] = DecodeTimeoutSeconds,
            ["encodeTimeoutSeconds"] = EncodeTimeoutSeconds,
            ["engine"] = Engine,
        });
}

/// <summary>Strict parsing and verification of a speaking-join result. Pure: no database, no clock.</summary>
public static class SpeakingJoinValidator
{
    public static bool TryParse(string resultJson, out SpeakingJoinResult? result, out string? error)
    {
        result = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(resultJson, new JsonDocumentOptions { MaxDepth = 8 });
            var json = new StrictJson(document.RootElement);
            if (!json.IsObject) return Fail("The result is not a JSON object.", out error);

            if (!json.TryString("schema", out var schema) || schema is null) return Fail("schema is required.", out error);
            if (!json.TryString("engineVersion", out var engine) || engine is null) return Fail("engineVersion is required.", out error);
            if (!json.TryString("inputSha256", out var inputSha) || inputSha is null) return Fail("inputSha256 is required.", out error);
            if (!json.TryInt("clipCount", out var clipCount)) return Fail("clipCount is required.", out error);
            if (!json.TryInt("durationMs", out var durationMs)) return Fail("durationMs is required.", out error);
            if (!json.TryBool("truncated", out var truncated)) return Fail("truncated is required.", out error);
            if (!json.TryInt("mp3Bytes", out var mp3Bytes)) return Fail("mp3Bytes is required.", out error);
            if (!json.TryString("mp3Sha256", out var mp3Sha) || mp3Sha is null) return Fail("mp3Sha256 is required.", out error);

            result = new SpeakingJoinResult(schema, engine, inputSha, clipCount, durationMs, truncated, mp3Bytes, mp3Sha);
            return true;
        }
        catch (JsonException)
        {
            return Fail("The result is not valid JSON.", out error);
        }

        static bool Fail(string message, out string? failure)
        {
            failure = message;
            return false;
        }
    }

    public static RemoteResultValidation Validate(RemoteJobRow job, SpeakingJoinResult r, IReadOnlyList<RemoteOutputRow> outputs)
    {
        if (!string.Equals(r.Schema, $"media.speaking-join.result/{job.SchemaVersion}", StringComparison.Ordinal))
        {
            return RemoteResultValidation.Invalid("schema differs from the job.");
        }

        if (!string.Equals(r.EngineVersion, job.EngineVersion, StringComparison.Ordinal))
        {
            return RemoteResultValidation.EngineMismatch("engineVersion differs from the job.");
        }

        if (!string.Equals(r.InputSha256, job.InputSha256, StringComparison.Ordinal))
        {
            return RemoteResultValidation.Invalid("inputSha256 differs from the job.");
        }

        var spec = RemoteJobKinds.Find(job.Kind);
        if (spec is null) return RemoteResultValidation.Invalid("The job kind is unknown.");
        var limits = RemoteJobLimits.FromJson(job.LimitsJson, spec.Limits);

        var clips = RemoteInputOutputService.ReadManifest(job.InputsJson).Count;
        if (r.ClipCount != clips) return RemoteResultValidation.Invalid("clipCount does not match the job inputs.");

        var maxAudioSeconds = RemoteJobParams.ReadInt(job.ParamsJson, "maxAudioSeconds") ?? 360;
        if (maxAudioSeconds < 1 || r.DurationMs < 1 || r.DurationMs > (long)maxAudioSeconds * 1000)
        {
            return RemoteResultValidation.Invalid("durationMs is out of range.");
        }

        if (r.Mp3Bytes < 1 || r.Mp3Bytes > limits.MaxOutputBytes) return RemoteResultValidation.Invalid("mp3Bytes is out of range.");
        if (!RemoteIds.IsSha256Hex(r.Mp3Sha256)) return RemoteResultValidation.Invalid("mp3Sha256 must be 64 lowercase hex characters.");

        // The uploaded object is the result's own claim made real: exactly one output, with the same size and hash.
        var uploaded = outputs.Count == 1 ? outputs[0] : null;
        if (uploaded is null
            || !string.Equals(uploaded.Name, SpeakingJoinSettings.OutputName, StringComparison.Ordinal)
            || uploaded.SizeBytes != r.Mp3Bytes
            || !string.Equals(uploaded.Sha256, r.Mp3Sha256, StringComparison.Ordinal))
        {
            return RemoteResultValidation.OutputMismatch("The uploaded join differs from the result.");
        }

        var summary = JsonSerializer.Serialize(new
        {
            clipCount = r.ClipCount,
            durationMs = r.DurationMs,
            truncated = r.Truncated,
            mp3Bytes = r.Mp3Bytes,
            mp3Sha256 = r.Mp3Sha256,
        });
        return RemoteResultValidation.Ok(r, summary);
    }
}

/// <summary>
/// <c>media.speaking-join</c> v1 (OET-RWP/1 section 6.4): a helper joins the consented candidate clips into the one mp3 the audio judge
/// receives. The result is the mp3 as a per-job output; nothing is written to a domain table. The derivative is recorded ONLY by its
/// <c>RemoteJobOutputs</c> row and its job (resource <c>SpeakingSession</c>), which is exactly what the cleanup paths walk: it is deleted
/// the moment the audio stage uses it, after a short TTL by the reaper, and together with the session's clips (retention sweep and
/// learner erasure). It is deliberately NOT a <c>SpeakingRecording</c>: a recording row would make the audio stage join the derivative
/// as if it were another clip. The applier refuses (<c>Discarded</c>) a join whose clips were archived or erased while it ran.
/// </summary>
public sealed class SpeakingJoinKindHandler(ILogger<SpeakingJoinKindHandler> logger) : IRemoteKindHandler
{
    public string Kind => RemoteJobKinds.MediaSpeakingJoin;

    public RemoteResultValidation Validate(
        RemoteJobRow job,
        string resultJson,
        IReadOnlyList<RemoteOutputRow> outputs,
        RemoteJobsOptions options)
    {
        if (!SpeakingJoinValidator.TryParse(resultJson, out var result, out var error))
        {
            return RemoteResultValidation.Invalid(error ?? "The result could not be parsed.");
        }

        return SpeakingJoinValidator.Validate(job, result!, outputs);
    }

    public async Task<RemoteApplyOutcome> ApplyAsync(RemoteApplyContext context, CancellationToken ct)
    {
        var result = (SpeakingJoinResult)context.Parsed;
        var job = context.Job;

        // Only the apply purpose exists for this kind.
        if (!string.Equals(job.Purpose, RemoteJobPurpose.Apply, StringComparison.Ordinal))
        {
            return RemoteApplyOutcome.NoOp("unsupported_purpose");
        }

        // Every clip the join was made from must still exist un-archived: a learner's erasure or the retention sweep that landed while
        // the helper worked means the derivative must not be kept.
        var recordingIds = ReadRecordingIds(job.InputsJson);
        if (recordingIds.Count == 0) return RemoteApplyOutcome.Discarded("stale_input");

        var db = context.Db;
        var live = await db.SpeakingRecordings.AsNoTracking()
            .Where(r => recordingIds.Contains(r.Id) && !r.IsArchived)
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (live.Count != recordingIds.Count) return RemoteApplyOutcome.Discarded("resource_gone");

        logger.LogInformation(
            "Remote Speaking join of session {SessionId} applied: {ClipCount} clip(s), {DurationMs} ms.",
            job.ResourceId, result.ClipCount, result.DurationMs);
        return RemoteApplyOutcome.Applied(new
        {
            clipCount = result.ClipCount,
            durationMs = result.DurationMs,
            truncated = result.Truncated,
            mp3Bytes = result.Mp3Bytes,
        });
    }

    /// <summary>The recording ids the producer put in the job manifest (server-side only: the node is never shown them).</summary>
    private static List<string> ReadRecordingIds(string inputsJson)
    {
        var ids = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(inputsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return ids;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("recordingId", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { Length: > 0 } value)
                {
                    ids.Add(value);
                }
            }
        }
        catch (JsonException)
        {
            // An unreadable manifest names no clips.
        }

        return ids.Distinct(StringComparer.Ordinal).ToList();
    }
}
