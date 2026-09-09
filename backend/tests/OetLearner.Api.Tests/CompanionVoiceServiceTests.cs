using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Conversation.Asr;
using OetLearner.Api.Services.Conversation.Tts;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Voice in and voice out for the companion.
///
/// <para>
/// Every case here is about a failure path, because the happy path is a thin
/// call into a pipeline that already has its own tests. What is new is what
/// happens when speech recognition is unconfigured, returns nothing, or throws
/// — and the answer has to be "voice is unavailable, type instead", never a
/// dropped turn and never an empty message handed to the model, which would
/// produce an answer about nothing.
/// </para>
/// </summary>
public sealed class CompanionVoiceServiceTests
{
    [Fact]
    public async Task A_recording_becomes_the_learners_words()
    {
        var service = Build(asr: new StubAsr("How do I open a discharge letter?"));

        var result = await service.TranscribeAsync([1, 2, 3], "audio/webm", "en", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("How do I open a discharge letter?", result.Text);
    }

    [Fact]
    public async Task Silence_is_reported_rather_than_passed_on_as_an_empty_turn()
    {
        // An empty message reaches the model as a question about nothing, and it
        // will answer something. Better to say the recording was not audible.
        var service = Build(asr: new StubAsr("   "));

        var result = await service.TranscribeAsync([1, 2, 3], "audio/webm", "en", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("no_speech_detected", result.Error);
    }

    [Fact]
    public async Task An_empty_recording_never_reaches_a_paid_provider()
    {
        var asr = new StubAsr("unused");
        var service = Build(asr);

        var result = await service.TranscribeAsync([], "audio/webm", "en", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("empty_audio", result.Error);
        Assert.Equal(0, asr.Calls);
    }

    [Fact]
    public async Task An_unconfigured_provider_degrades_instead_of_taking_the_turn_down()
    {
        // Voice being unconfigured is a permanent state on some deployments. The
        // learner can still type, so this must never surface as a crash.
        var service = Build(asr: new ThrowingAsr());

        var result = await service.TranscribeAsync([1], "audio/webm", "en", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("asr_unavailable", result.Error);
    }

    [Fact]
    public async Task A_provider_error_code_survives_to_the_caller()
    {
        // The hub distinguishes "I could not hear you" from "voice is off" when
        // it chooses what to tell the learner, so the code has to reach it.
        var service = Build(asr: new ThrowingAsr(new ConversationAsrException("rate_limited", "slow down")));

        var result = await service.TranscribeAsync([1], "audio/webm", "en", CancellationToken.None);

        Assert.Equal("rate_limited", result.Error);
    }

    [Fact]
    public async Task Speech_synthesis_is_skipped_when_it_is_switched_off()
    {
        var tts = new StubTts();
        var service = Build(tts: tts, ttsDisabled: true);

        var result = await service.SpeakAsync("Here is how to open the letter.", "en", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("tts_disabled", result.Error);
        Assert.Equal(0, tts.Calls);
    }

    [Fact]
    public async Task A_long_answer_is_trimmed_rather_than_spoken_in_full()
    {
        // Past a point the learner is better off reading it, and a two-minute
        // synthesis is a slow, expensive way to deliver a list.
        var tts = new StubTts();
        var service = Build(tts: tts);

        await service.SpeakAsync(new string('a', 5000), "en", CancellationToken.None);

        Assert.True(tts.LastText!.Length < 5000);
    }

    [Fact]
    public async Task Nothing_is_synthesised_for_an_empty_answer()
    {
        var tts = new StubTts();
        var service = Build(tts: tts);

        var result = await service.SpeakAsync("   ", "en", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(0, tts.Calls);
    }

    private static CompanionVoiceService Build(
        IConversationAsrProvider? asr = null,
        StubTts? tts = null,
        bool ttsDisabled = false) =>
        new(new StubAsrSelector(asr ?? new StubAsr("hello")),
            new StubTtsSelector(tts ?? new StubTts(), ttsDisabled),
            NullLogger<CompanionVoiceService>.Instance);

    // ── doubles ──────────────────────────────────────────────────────────────

    private sealed class StubAsr(string text) : IConversationAsrProvider
    {
        public int Calls { get; private set; }
        public string Name => "stub";
        public bool IsConfigured => true;

        public Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new ConversationAsrResult(text, 0.95, 1200, "en", "stub", null));
        }
    }

    private sealed class ThrowingAsr(Exception? failure = null) : IConversationAsrProvider
    {
        private readonly Exception _failure = failure ?? new InvalidOperationException("not configured");

        public string Name => "throwing";
        public bool IsConfigured => false;

        public Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct) =>
            throw _failure;
    }

    private sealed class StubTts : IConversationTtsProvider
    {
        public int Calls { get; private set; }
        public string? LastText { get; private set; }
        public string Name => "stub";
        public bool IsConfigured => true;

        public Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
        {
            Calls++;
            LastText = request.Text;
            return Task.FromResult(new ConversationTtsResult([1, 2, 3], "audio/mpeg", 900, "stub", null));
        }
    }

    private sealed class StubAsrSelector(IConversationAsrProvider provider) : IConversationAsrProviderSelector
    {
        public Task<IConversationAsrProvider> SelectAsync(CancellationToken ct = default) =>
            Task.FromResult(provider);

        public Task<IConversationRealtimeAsrProvider?> TrySelectRealtimeAsync(CancellationToken ct = default) =>
            Task.FromResult<IConversationRealtimeAsrProvider?>(null);
    }

    private sealed class StubTtsSelector(IConversationTtsProvider provider, bool disabled)
        : IConversationTtsProviderSelector
    {
        public Task<IConversationTtsProvider?> TrySelectAsync(CancellationToken ct = default) =>
            Task.FromResult<IConversationTtsProvider?>(provider);

        public Task<bool> IsTtsDisabledAsync(CancellationToken ct = default) => Task.FromResult(disabled);

        public Task<IConversationTtsProvider?> TrySelectAsync(string providerName, CancellationToken ct = default) =>
            Task.FromResult<IConversationTtsProvider?>(provider);
    }
}
