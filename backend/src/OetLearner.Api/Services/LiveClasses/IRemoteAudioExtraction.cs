namespace OetLearner.Api.Services.LiveClasses;

/// <summary>What the transcription stage should do with a recording that is too large to transcribe in one call.</summary>
public enum AudioExtractAction
{
    /// <summary>No remote path applies (flag off, no node, anything doubtful): behave exactly as before.</summary>
    Local,

    /// <summary>A remote job owns the extraction and has not finished: try this recording again shortly.</summary>
    Pending,

    /// <summary>The remote path failed in a way an admin must look at (the note says why); the recording cannot be transcribed yet.</summary>
    Failed,
}

/// <summary>The remote path's answer for one oversize recording.</summary>
public sealed record AudioExtractPlan(AudioExtractAction Action, string? Note = null, string? JobId = null)
{
    public static readonly AudioExtractPlan UseLocal = new(AudioExtractAction.Local);
}

/// <summary>
/// Optional remote audio extraction for Live Class recordings (<c>media.audio-extract</c>, OET-RWP/1 section 6.3). A helper decodes the
/// recording once with ffmpeg and returns transcription-sized mp3 chunks plus a manifest; transcription itself (every AI call, one per
/// chunk, with its <c>AiUsageRecord</c>) stays on the API through <c>IAiGatewayService</c>. Not registered at all unless the configured
/// provider is PostgreSQL, in which case an oversize recording behaves exactly as it always did.
/// </summary>
public interface IRemoteAudioExtraction
{
    /// <summary>
    /// Called by the transcription stage for a recording whose stored file exceeds the single-call cap and that has no usable chunk
    /// manifest. Enqueues an idempotent extraction job when a healthy node can take it, and reports where that job stands.
    /// </summary>
    Task<AudioExtractPlan> PlanAsync(string recordingId, string storageKey, long sizeBytes, CancellationToken ct);
}
