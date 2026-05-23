namespace OetLearner.Api.Contracts;

public record AdminVoiceDesignConfigRequest(
    string? ModelVariant,
    string? VoiceId,
    string? Instructions,
    double? Speed,
    double? Pitch,
    string? Emotion,
    string? ElevenLabsApiKey,
    string? ElevenLabsTtsBaseUrl,
    string? ElevenLabsDefaultVoiceId,
    string? ElevenLabsModel,
    string? ElevenLabsOutputFormat,
    string? ElevenLabsPronunciationDictionaryId,
    string? ElevenLabsPronunciationDictionaryVersionId,
    double? ElevenLabsStability,
    double? ElevenLabsSimilarityBoost,
    double? ElevenLabsStyle,
    bool? ElevenLabsUseSpeakerBoost);

public record AdminVoiceDesignConfigResponse(
    string ModelVariant,
    string VoiceId,
    string VoiceInstructions,
    double Speed,
    double Pitch,
    string Emotion,
    string ElevenLabsTtsBaseUrl,
    string ElevenLabsDefaultVoiceId,
    string ElevenLabsModel,
    string ElevenLabsOutputFormat,
    string? ElevenLabsPronunciationDictionaryId,
    string? ElevenLabsPronunciationDictionaryVersionId,
    double ElevenLabsStability,
    double ElevenLabsSimilarityBoost,
    double ElevenLabsStyle,
    bool ElevenLabsUseSpeakerBoost,
    bool ElevenLabsApiKeyPresent,
    string? LastUpdatedAt,
    string? LastUpdatedBy);

public record AdminVoiceDesignPreviewRequest(
    string? Text,
    string? VoiceId,
    string? Locale,
    string? ModelVariant,
    string? Instructions,
    double? Speed,
    double? Pitch,
    string? Emotion);

public record AdminAudioRegenerateRequest(
    string AudioType,     // "all" | "listening" | "vocabulary" | "conversation" | "recalls"
    string Scope,         // "all" | "missing" | "different-voice"
    string? ModelVariant,
    string? VoiceId,
    string? Instructions,
    double? Speed,
    double? Pitch,
    string? Emotion,
    string? ProviderName,
    bool? ForceRegenerate,
    bool? DryRun);

public record AdminAudioRegenerateBatchResult(
    string BatchId,
    string AudioType,
    string Scope,
    int TotalItems,
    bool DryRun,
    string ModelVariant,
    string? VoiceId,
    string? ProviderName = null);

public record AdminAudioBatchDto(
    string BatchId,
    string AudioType,
    string Scope,
    string Status,
    int TotalItems,
    int CompletedItems,
    int FailedItems,
    string VoiceId,
    string ModelVariant,
    string ProviderName,
    double Speed,
    double Pitch,
    string Emotion,
    string StartedAt,
    string? CompletedAt,
    string RequestedBy);
