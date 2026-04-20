using System.Text;
using OetLearner.Api.Services.Pronunciation;

namespace OetLearner.Api.Tests;

/// <summary>
/// Sanity tests for the mock pronunciation ASR provider — the deterministic
/// fallback used when neither Azure nor Whisper credentials are configured.
/// The provider must never lie about having run real ASR, and must produce
/// reproducible scores for the same inputs.
/// </summary>
public class MockPronunciationAsrProviderTests
{
    private static AsrRequest BuildRequest(string reference, byte[] audio) =>
        new(
            Audio: new MemoryStream(audio),
            AudioMimeType: "audio/webm",
            ReferenceText: reference,
            TargetPhoneme: "θ",
            Locale: "en-GB",
            TargetRuleId: "P01.1",
            RulebookProfession: "medicine",
            AudioBytes: audio.Length);

    [Fact]
    public async Task Produces_Scores_In_Plausible_Range()
    {
        var provider = new MockPronunciationAsrProvider();
        var audio = new byte[4096];
        Array.Fill<byte>(audio, 42);
        var result = await provider.AnalyzeAsync(
            BuildRequest("The therapist recommended three therapy sessions.", audio),
            CancellationToken.None);

        Assert.InRange(result.AccuracyScore, 40, 95);
        Assert.InRange(result.FluencyScore, 40, 95);
        Assert.InRange(result.CompletenessScore, 50, 98);
        Assert.InRange(result.ProsodyScore, 40, 90);
        Assert.InRange(result.OverallScore, 40, 95);
    }

    [Fact]
    public async Task Is_Deterministic_For_Identical_Input()
    {
        var provider = new MockPronunciationAsrProvider();
        var audio = Encoding.UTF8.GetBytes("deterministic-audio-bytes");
        var first = await provider.AnalyzeAsync(BuildRequest("think thoroughly", audio), CancellationToken.None);
        var second = await provider.AnalyzeAsync(BuildRequest("think thoroughly", audio), CancellationToken.None);
        Assert.Equal(first.AccuracyScore, second.AccuracyScore);
        Assert.Equal(first.FluencyScore, second.FluencyScore);
        Assert.Equal(first.OverallScore, second.OverallScore);
        Assert.Equal(first.WordScores.Count, second.WordScores.Count);
    }

    [Fact]
    public async Task Produces_One_Word_Score_Per_Reference_Word_Up_To_Cap()
    {
        var provider = new MockPronunciationAsrProvider();
        var audio = new byte[1024];
        var result = await provider.AnalyzeAsync(
            BuildRequest("one two three four five six seven", audio),
            CancellationToken.None);
        Assert.True(result.WordScores.Count >= 5);
        Assert.All(result.WordScores, ws => Assert.False(string.IsNullOrWhiteSpace(ws.Word)));
    }

    [Fact]
    public async Task Always_Reports_Target_Phoneme_In_Problematic_List()
    {
        var provider = new MockPronunciationAsrProvider();
        var audio = new byte[512];
        var result = await provider.AnalyzeAsync(BuildRequest("short reference", audio), CancellationToken.None);
        Assert.NotEmpty(result.ProblematicPhonemes);
        Assert.Equal("θ", result.ProblematicPhonemes[0].Phoneme);
        Assert.Equal("P01.1", result.ProblematicPhonemes[0].RuleId);
    }

    [Fact]
    public async Task ProviderName_Is_Mock()
    {
        var provider = new MockPronunciationAsrProvider();
        var audio = new byte[256];
        var result = await provider.AnalyzeAsync(BuildRequest("hello", audio), CancellationToken.None);
        Assert.Equal("mock", result.ProviderName);
    }
}
