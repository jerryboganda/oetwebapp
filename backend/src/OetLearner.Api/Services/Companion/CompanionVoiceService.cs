using OetLearner.Api.Services.Conversation.Asr;
using OetLearner.Api.Services.Conversation.Tts;

namespace OetLearner.Api.Services.Companion;

/// <summary>One transcribed voice note, or the reason there is none.</summary>
public sealed record CompanionVoiceTranscript(
    bool Ok,
    string Text,
    int DurationMs,
    string? Provider,
    string? Error);

/// <summary>Synthesised speech for one companion reply.</summary>
public sealed record CompanionVoiceReply(
    bool Ok,
    byte[] Audio,
    string MimeType,
    int DurationMs,
    string? Error);

public interface ICompanionVoiceService
{
    Task<CompanionVoiceTranscript> TranscribeAsync(
        byte[] audio, string mimeType, string locale, CancellationToken ct);

    Task<CompanionVoiceReply> SpeakAsync(string text, string locale, CancellationToken ct);
}

/// <summary>
/// Voice for the companion, built on the pipelines the platform already runs.
///
/// <para>
/// The AI Conversation feature has had working speech recognition and speech
/// synthesis — with provider selection, configuration checks, a production
/// guard against the mock provider, and usage recording — for some time. A
/// second voice stack for the companion would have been a second set of
/// provider keys to rotate, a second place for the mock provider to leak into
/// production, and two answers to "which ASR are we using?". So this is a thin
/// adapter over <see cref="IConversationAsrProviderSelector"/> and
/// <see cref="IConversationTtsProviderSelector"/>, not a new pipeline.
/// </para>
///
/// <para>
/// <b>A transcript is untrusted input.</b> What comes back is whatever the
/// learner said, and a learner can say "ignore your instructions" as easily as
/// they can type it — with the added problem that the transcript arrives looking
/// like system-generated text. The caller folds it into the turn labelled as the
/// learner's own words, and the prompt's rule that the learner's message is data
/// rather than instruction covers it from there.
/// </para>
/// </summary>
public sealed class CompanionVoiceService(
    IConversationAsrProviderSelector asr,
    IConversationTtsProviderSelector tts,
    ILogger<CompanionVoiceService> logger) : ICompanionVoiceService
{
    /// <summary>
    /// Longest reply worth speaking. Past this the learner is better served
    /// reading it, and a two-minute synthesis is a slow, expensive way to
    /// deliver a list.
    /// </summary>
    private const int MaxSpokenChars = 1500;

    public async Task<CompanionVoiceTranscript> TranscribeAsync(
        byte[] audio,
        string mimeType,
        string locale,
        CancellationToken ct)
    {
        if (audio.Length == 0)
        {
            return new CompanionVoiceTranscript(false, string.Empty, 0, null, "empty_audio");
        }

        try
        {
            var provider = await asr.SelectAsync(ct);
            using var stream = new MemoryStream(audio, writable: false);

            var result = await provider.TranscribeAsync(
                new ConversationAsrRequest(stream, mimeType, locale, audio.LongLength),
                ct);

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                // Silence, or speech the provider could not make out. Saying so
                // is far better than passing an empty turn to the model, which
                // would answer something — anything — about nothing.
                return new CompanionVoiceTranscript(false, string.Empty, result.DurationMs, result.ProviderName, "no_speech_detected");
            }

            return new CompanionVoiceTranscript(true, result.Text.Trim(), result.DurationMs, result.ProviderName, null);
        }
        catch (ConversationAsrException ex)
        {
            logger.LogWarning(ex, "Companion voice transcription failed ({Code}).", ex.Code);
            return new CompanionVoiceTranscript(false, string.Empty, 0, null, ex.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not configured is the common case, and it must degrade to "voice
            // is unavailable" rather than taking the conversation down. The
            // learner can still type.
            logger.LogWarning(ex, "Companion voice transcription unavailable.");
            return new CompanionVoiceTranscript(false, string.Empty, 0, null, "asr_unavailable");
        }
    }

    public async Task<CompanionVoiceReply> SpeakAsync(string text, string locale, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CompanionVoiceReply(false, [], string.Empty, 0, "empty_text");
        }

        var spoken = text.Length > MaxSpokenChars ? text[..MaxSpokenChars] : text;

        try
        {
            if (await tts.IsTtsDisabledAsync(ct))
            {
                return new CompanionVoiceReply(false, [], string.Empty, 0, "tts_disabled");
            }

            var provider = await tts.TrySelectAsync(ct);
            if (provider is null)
            {
                return new CompanionVoiceReply(false, [], string.Empty, 0, "tts_unavailable");
            }

            var result = await provider.SynthesizeAsync(
                new ConversationTtsRequest(spoken, Voice: string.Empty, Locale: locale), ct);

            return new CompanionVoiceReply(true, result.Audio, result.MimeType, result.DurationMs, null);
        }
        catch (ConversationTtsException ex)
        {
            logger.LogWarning(ex, "Companion voice synthesis failed ({Code}).", ex.Code);
            return new CompanionVoiceReply(false, [], string.Empty, 0, ex.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Companion voice synthesis unavailable.");
            return new CompanionVoiceReply(false, [], string.Empty, 0, "tts_unavailable");
        }
    }
}
