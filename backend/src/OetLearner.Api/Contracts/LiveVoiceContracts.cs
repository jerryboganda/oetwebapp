namespace OetLearner.Api.Contracts;

/// <summary>
/// Provider disclosure returned before the browser requests microphone access.
/// <c>Provider</c> is always <c>Candidates[0]</c>. <c>Candidates</c> is the
/// server-ordered list of providers the browser may try, in order, when a
/// session creation is refused. <c>Pinned</c> means the server honoured a QA pin
/// (a provider was requested AND the learner holds an enabled
/// <c>speaking_live_voice_pin:{userId}</c> feature flag): a single candidate and no
/// failover. A request from any other account is ignored and <c>Pinned</c> is false.
/// </summary>
public sealed record LiveVoicePreflightResponse(
    string Provider,
    string ProviderDisplayName,
    string Model,
    string Disclosure,
    int RetentionDays,
    string SessionId,
    string RolePlayCardId,
    IReadOnlyList<string>? Candidates = null,
    bool Pinned = false);

/// <summary>Browser WebRTC offer forwarded to the OpenAI Realtime session broker.</summary>
public sealed record LiveVoiceOpenAiOfferRequest(
    string Sdp,
    string? ClientSessionId = null);

/// <summary><c>HardStopAt</c> is when the server force-ends this role-play (deadline plus grace).</summary>
public sealed record LiveVoiceOpenAiOfferResponse(
    string Provider,
    string Model,
    string ProviderSessionId,
    string AnswerSdp,
    DateTimeOffset HardStopAt);

/// <summary>Short-lived Gemini Live credential. The API key never leaves the server.</summary>
public sealed record LiveVoiceGeminiTokenResponse(
    string Provider,
    string Model,
    string ProviderSessionId,
    string WebSocketUrl,
    DateTimeOffset ExpiresAt,
    DateTimeOffset HardStopAt);

/// <summary>
/// A completed pair of provider transcripts. Partial provider events stay in
/// the browser and are submitted only when the provider marks a turn complete.
/// </summary>
public sealed record LiveVoiceTurnRequest(
    string Provider,
    string ProviderSessionId,
    string? CandidateText,
    string? PatientText,
    string? ClientTurnId = null,
    int? TurnIndex = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null);

public sealed record LiveVoiceTurnResponse(
    string SessionId,
    int SequenceNumber,
    bool Duplicate,
    string? AdvisoryStatus);

/// <summary>
/// Provider-reported token usage of one live voice conversation, sent by the browser (the realtime media and
/// its events never pass through this API). It is used for cost REPORTING only: every number is clamped, and a
/// wrong or missing report can never affect grading, credits or the candidate. <c>Basis</c> is <c>final</c>
/// (the provider's end-of-session total), <c>sum</c> (the sum of per-response usage events) or <c>mixed</c>.
/// </summary>
public sealed record LiveVoiceUsageRequest(
    string ProviderSessionId,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? InputAudioTokens,
    long? OutputAudioTokens,
    string? Basis = null,
    int? Responses = null);

public sealed record LiveVoiceUsageResponse(bool Accepted);

public sealed record LiveVoiceTranscriptRequest(
    string Provider,
    string ProviderSessionId,
    IReadOnlyList<LiveVoiceTranscriptSegment> Segments);

public sealed record LiveVoiceTranscriptSegment(
    string Speaker,
    int StartMs,
    int EndMs,
    string Text,
    double? Confidence = null,
    string? SourceRecordingId = null);

public sealed record LiveVoiceAudioCaptureResponse(
    string RecordingId,
    string MimeType,
    int DurationSeconds);

public sealed record LiveVoiceTranscriptResponse(
    string TranscriptId,
    string Provider,
    int WordCount,
    double MeanConfidence,
    DateTimeOffset GeneratedAt);
