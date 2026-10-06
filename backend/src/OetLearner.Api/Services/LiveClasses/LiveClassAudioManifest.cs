using System.Text.Json;
using System.Text.Json.Serialization;

namespace OetLearner.Api.Services.LiveClasses;

/// <summary>One transcription-sized mp3 chunk of a Live Class recording (<c>media.audio-extract</c>, OET-RWP/1 section 6.3).</summary>
public sealed class LiveClassAudioChunk
{
    public int Index { get; set; }

    /// <summary><c>chunk-NNNN.mp3</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Server-side storage key (<c>remote-jobs/{jobId}/{fence}/{name}</c>). Never sent to a client or a node.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public long StartMs { get; set; }
    public long DurationMs { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Transcript of this chunk once it has been transcribed (null = not yet). Persisted per chunk so a retry resumes instead of paying again.</summary>
    public string? Transcript { get; set; }
}

/// <summary>
/// The chunk manifest stored in <c>LiveClassRecording.AudioChunksJson</c>. Written once by the completion applier of
/// <c>media.audio-extract</c>; afterwards only the per-chunk transcripts and <see cref="AudioDeleted"/> change, and only the
/// transcription stage writes them. Parsing is strict: anything that is not a well-formed manifest is "no manifest", so a damaged
/// value can never make the stage read an arbitrary storage key.
/// </summary>
public sealed class LiveClassAudioManifest
{
    public const string SchemaName = "live-class-audio-chunks/1";
    public const string MimeType = "audio/mpeg";

    /// <summary>Every chunk lives under this prefix; a key outside it is never read or deleted by the transcription stage.</summary>
    public const string StorageKeyPrefix = "remote-jobs/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Schema { get; set; } = SchemaName;

    /// <summary>The remote job that produced the chunks (diagnostics only).</summary>
    public string JobId { get; set; } = string.Empty;

    public long Fence { get; set; }
    public string InputSha256 { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int SegmentSeconds { get; set; }

    /// <summary>True once every chunk has been transcribed and its audio deleted (delete-on-complete); the transcripts remain.</summary>
    public bool AudioDeleted { get; set; }

    public List<LiveClassAudioChunk> Chunks { get; set; } = [];

    /// <summary>Every chunk has its transcript (derived, never stored).</summary>
    [JsonIgnore]
    public bool IsTranscribed => Chunks.Count > 0 && Chunks.All(chunk => chunk.Transcript is not null);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>The manifest, or null when <paramref name="json"/> is empty or not a well-formed manifest.</summary>
    public static LiveClassAudioManifest? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        LiveClassAudioManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<LiveClassAudioManifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is null || !string.Equals(manifest.Schema, SchemaName, StringComparison.Ordinal)) return null;
        if (manifest.Chunks is null || manifest.Chunks.Count == 0 || manifest.Chunks.Count > 64) return null;

        for (var i = 0; i < manifest.Chunks.Count; i++)
        {
            var chunk = manifest.Chunks[i];
            if (chunk is null || chunk.Index != i) return null;
            if (string.IsNullOrWhiteSpace(chunk.StorageKey)
                || !chunk.StorageKey.StartsWith(StorageKeyPrefix, StringComparison.Ordinal)
                || chunk.StorageKey.Contains("..", StringComparison.Ordinal)) return null;
            if (chunk.SizeBytes <= 0 || chunk.DurationMs <= 0) return null;
        }

        return manifest;
    }

    /// <summary>The transcripts of all chunks in order, empty chunks skipped, one chunk per line.</summary>
    public string JoinTranscripts()
        => string.Join(
            "\n",
            Chunks.OrderBy(chunk => chunk.Index)
                .Select(chunk => chunk.Transcript?.Trim())
                .Where(text => !string.IsNullOrEmpty(text)));
}
