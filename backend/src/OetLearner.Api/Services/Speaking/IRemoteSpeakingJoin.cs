namespace OetLearner.Api.Services.Speaking;

/// <summary>What the precompute decided for one session.</summary>
public enum SpeakingJoinEnqueue
{
    /// <summary>A flag is off or the kind has no pinned engine: nothing was done.</summary>
    Disabled,

    /// <summary>No healthy node can take the work: nothing was enqueued (the grade joins locally, as always).</summary>
    NoEligibleNode,

    /// <summary>The session's clips cannot be joined remotely (none, too many or too large, an unsupported type, no readable bytes).</summary>
    NotEligible,

    /// <summary>A join job already exists for this session.</summary>
    AlreadyQueued,

    /// <summary>A job was enqueued.</summary>
    Enqueued,
}

/// <summary>
/// Optional remote precompute of the Speaking audio join (<c>media.speaking-join</c>, OET-RWP/1 section 6.4): a helper decodes the
/// consented candidate clips and joins them into the one mp3 the audio judge receives. The judge call itself, grading and credits stay
/// on the API, and the local <see cref="FfmpegSpeakingAudioTranscoder"/> is ALWAYS the fallback: the grade path never waits for a remote
/// job. Learner audio hygiene: every derivative lives at a per-job key (never content-addressed, never shared between learners), is
/// deleted the moment it is used, expires after a short TTL, and is deleted with the clips it derives from (retention and erasure).
/// Not registered at all unless the configured provider is PostgreSQL.
/// </summary>
public interface IRemoteSpeakingJoin
{
    /// <summary>
    /// Enqueues the join for the session's candidate clips when every condition holds (flags, pinned engine, a healthy node, eligible
    /// clips). Never throws for a remote problem.
    /// </summary>
    Task<SpeakingJoinEnqueue> EnqueueForSessionAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Serves the join a helper prepared for exactly this ordered clip list (by SHA-256), consuming it: the stored mp3 is read, verified
    /// and deleted. Null when there is none (flag off, no job, a different clip list, an unverifiable object): the caller joins locally.
    /// </summary>
    Task<SpeakingAudioJoin?> TryServeAsync(string sessionId, IReadOnlyList<string> clipSha256s, CancellationToken ct);

    /// <summary>
    /// Deletes the derivatives (and withdraws jobs not yet claimed) of these sessions. Called when their clips are deleted: by the audio
    /// retention sweep and by a learner's erasure of a recording.
    /// </summary>
    Task DeleteForSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct);
}
