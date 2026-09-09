using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// 2026-09-09 regression — AiOperations.ResourceId/RequestHash are both
/// varchar(64). OpenAiWhisperSpeakingProvider used to pass the raw media
/// asset storage path (e.g. "audio/{attemptId}/upload-{uploadId}", 80+
/// chars) straight through, which overflowed the column and threw a
/// DbUpdateException on every real Speaking submission — masked in earlier
/// testing by an unrelated credential-cache bug that meant this code path
/// was never actually reached in production before. ShortHash must always
/// fit, deterministically, regardless of input length.
/// </summary>
public sealed class OpenAiWhisperSpeakingProviderTests
{
    [Fact]
    public void ShortHash_RealisticLongMediaAssetPath_FitsVarchar64()
    {
        // Representative production shape: "audio/{attemptId}/upload-{uploadId}"
        // with both ids as 32-hex-char GUIDs — the exact case that overflowed.
        var longPath = "audio/sa-a8b8510b5ade4e1ab6db170ee20c1ea5/upload-b9a378815a384266889037c23589017b";
        Assert.True(longPath.Length > 64, "test fixture should reproduce the real overflow condition");

        var hash = OpenAiWhisperSpeakingProvider.ShortHash(longPath);

        Assert.True(hash.Length <= 64, $"ShortHash produced {hash.Length} chars, exceeds varchar(64)");
    }

    [Fact]
    public void ShortHash_RequestHashShape_FitsVarchar64()
    {
        // The RequestHash call site concatenates model:language:mediaAssetReference.
        var longInput = "whisper-1:en:audio/sa-a8b8510b5ade4e1ab6db170ee20c1ea5/upload-b9a378815a384266889037c23589017b";

        var hash = OpenAiWhisperSpeakingProvider.ShortHash(longInput);

        Assert.True(hash.Length <= 64, $"ShortHash produced {hash.Length} chars, exceeds varchar(64)");
    }

    [Fact]
    public void ShortHash_IsDeterministic_SameInputSameOutput()
    {
        // Idempotency/dedup depends on this — a random or time-based key
        // would defeat the whole point of ResourceId/RequestHash.
        const string input = "audio/sa-abc/upload-def";

        Assert.Equal(OpenAiWhisperSpeakingProvider.ShortHash(input), OpenAiWhisperSpeakingProvider.ShortHash(input));
    }

    [Fact]
    public void ShortHash_DifferentInputs_ProduceDifferentOutputs()
    {
        var a = OpenAiWhisperSpeakingProvider.ShortHash("audio/sa-one/upload-x");
        var b = OpenAiWhisperSpeakingProvider.ShortHash("audio/sa-two/upload-y");

        Assert.NotEqual(a, b);
    }
}
