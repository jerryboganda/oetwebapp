namespace OetLearner.Api.Contracts;

public record AdminVoiceDesignConfigRequest(
    string? ModelVariant,
    string? VoiceId,
    string? Instructions,
    double? Speed,
    double? Pitch,
    string? Emotion);

public record AdminVoiceDesignConfigResponse(
    string ModelVariant,
    string VoiceId,
    string VoiceInstructions,
    double Speed,
    double Pitch,
    string Emotion,
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
    string AudioType,     // "all" | "listening" | "vocabulary" | "conversation"
    string Scope,         // "all" | "missing" | "different-voice"
    string? ModelVariant,
    string? VoiceId,
    string? Instructions,
    double? Speed,
    double? Pitch,
    string? Emotion,
    bool? DryRun);

public record AdminAudioRegenerateBatchResult(
    string BatchId,
    string AudioType,
    string Scope,
    int TotalItems,
    bool DryRun,
    string ModelVariant,
    string? VoiceId);

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
    double Speed,
    double Pitch,
    string Emotion,
    string StartedAt,
    string? CompletedAt,
    string RequestedBy);
