using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// The operation identity of a gateway call must change when the audio does. It used to fold in only the
/// NUMBER of clips, so two different recordings sent to the same prompt were one operation and the second
/// would have been served the first one's answer.
/// </summary>
public sealed class CoordinatedAiGatewayHashTests
{
    private static byte[] B(params byte[] bytes) => bytes;

    private static AiGatewayRequest Request(params byte[][] clips)
        => new()
        {
            FeatureCode = "speaking.audio_assess",
            Provider = "openai-audio",
            Model = "gpt-audio-1.5",
            UserInput = "listen and judge",
            ResourceId = "session-1",
            ResourceType = "speaking_session",
            AudioAttachments = clips.Length == 0
                ? null
                : clips.Select(data => new AiProviderAudioAttachment { MimeType = "audio/mpeg", Data = data }).ToList(),
        };

    [Fact]
    public void TwoDifferentRecordings_AreTwoDifferentOperations()
        => Assert.NotEqual(
            CoordinatedAiGatewayService.BuildRequestHash(Request(B(1, 2, 3))),
            CoordinatedAiGatewayService.BuildRequestHash(Request(B(1, 2, 4))));

    [Fact]
    public void TheSameRecording_IsTheSameOperation()
        => Assert.Equal(
            CoordinatedAiGatewayService.BuildRequestHash(Request(B(1, 2, 3))),
            CoordinatedAiGatewayService.BuildRequestHash(Request(B(1, 2, 3))));

    [Fact]
    public void ClipOrderAndCount_Matter()
    {
        var ab = CoordinatedAiGatewayService.BuildRequestHash(Request(B(1), B(2)));
        var ba = CoordinatedAiGatewayService.BuildRequestHash(Request(B(2), B(1)));
        var a = CoordinatedAiGatewayService.BuildRequestHash(Request(B(1)));

        Assert.NotEqual(ab, ba);
        Assert.NotEqual(ab, a);
    }

    [Fact]
    public void ACallWithoutAudio_IsDifferentFromOneWithAudio_AndStaysStable()
    {
        var none = CoordinatedAiGatewayService.BuildRequestHash(Request());
        var emptyList = CoordinatedAiGatewayService.BuildRequestHash(Request() with { AudioAttachments = [] });

        Assert.Equal(none, emptyList); // "no audio" is one thing, however it is spelled
        Assert.NotEqual(none, CoordinatedAiGatewayService.BuildRequestHash(Request(B(1))));
    }
}
