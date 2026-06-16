namespace OetLearner.Api.Services.Conversation.Tts;

/// <summary>
/// Shared helper for estimating spoken duration from text length. TTS providers
/// use it to populate <see cref="ConversationTtsResult.DurationMs"/> without
/// decoding the synthesized audio. Roughly 350ms per word, floored at 300ms.
/// </summary>
internal static class ConversationTtsDuration
{
    public static int ApproxDurationMs(string text) =>
        Math.Max(300, (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 350);
}
