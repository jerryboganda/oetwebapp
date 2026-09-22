namespace OetLearner.Api.Contracts;

/// <summary>Provider disclosure returned before the browser requests microphone access.</summary>
public sealed record LiveVoicePreflightResponse(
    string Provider,
    string ProviderDisplayName,
    string Model,
    string Disclosure,
    int RetentionDays,
    string SessionId,
    string RolePlayCardId);

/// <summary>Browser WebRTC offer forwarded to the OpenAI Realtime session broker.</summary>
public sealed record LiveVoiceOpenAiOfferRequest(
    string Sdp,
    string? ClientSessionId = null);

public sealed record LiveVoiceOpenAiOfferResponse(
    string Provider,
    string Model,
    string ProviderSessionId,
    string AnswerSdp);

/// <summary>Short-lived Gemini Live credential. The API key never leaves the server.</summary>
public sealed record LiveVoiceGeminiTokenResponse(
    string Provider,
    string Model,
    string ProviderSessionId,
    string WebSocketUrl,
    DateTimeOffset ExpiresAt);

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

public sealed record LiveVoiceTranscriptRequest(
    string Provider,
    string ProviderSessionId,
    IReadOnlyList<LiveVoiceTranscriptSegment> Segments);

public sealed record LiveVoiceTranscriptSegment(
    string Speaker,
    int StartMs,
    int EndMs,
    string Text,
    double? Confidence = null);

public sealed record LiveVoiceTranscriptResponse(
    string TranscriptId,
    string Provider,
    int WordCount,
    double MeanConfidence,
    DateTimeOffset GeneratedAt);
