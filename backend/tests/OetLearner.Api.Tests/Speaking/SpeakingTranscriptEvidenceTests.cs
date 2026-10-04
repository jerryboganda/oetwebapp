using System.Text.Json;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Owner spec 4 Oct 2026 section 7.2: the connection check at the start of a live conversation ("Hi, can you
/// hear me" / "Yeah, I hear you. Go ahead.") is not part of the assessed performance. Only LEADING chatter
/// is removed, so a real greeting and anything said later in the consultation always stays.
/// </summary>
public sealed class SpeakingTranscriptEvidenceTests
{
    [Theory]
    [InlineData("Hi, can you hear me?")]
    [InlineData("Can you hear me")]
    [InlineData("Hello, can you hear me okay?")]
    [InlineData("Yeah, I hear you.")]
    [InlineData("Yes, I can hear you clearly.")]
    [InlineData("Go ahead.")]
    [InlineData("Yeah, go ahead.")]
    [InlineData("Testing, testing.")]
    [InlineData("Is this working?")]
    [InlineData("Am I audible?")]
    [InlineData("Loud and clear.")]
    public void IsConnectivitySentence_RecognisesAConnectionCheck(string sentence)
        => Assert.True(SpeakingTranscriptEvidence.IsConnectivitySentence(sentence));

    [Theory]
    [InlineData("Hello, I'm Dr Faisal and today we are going to talk about your heart attack.")]
    [InlineData("Hello.")] // a greeting is assessed (Relationship building), never chatter
    [InlineData("Good morning, how are you today?")]
    [InlineData("Can you hear me describing the pain?")]
    [InlineData("How can I help you today?")]
    [InlineData("I can hear that you are worried.")]
    [InlineData("Please go ahead and sit down on the couch so that I can examine you.")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsConnectivitySentence_NeverTreatsRealConsultationSpeechAsChatter(string sentence)
        => Assert.False(SpeakingTranscriptEvidence.IsConnectivitySentence(sentence));

    [Fact]
    public void Strip_DropsTheOpeningConnectionCheck_FromBothSides()
    {
        var segments = Json(
            Segment("candidate", "Hi, can you hear me?", 1000),
            Segment("patient", "Yeah, I hear you. Go ahead.", 5000),
            Segment("candidate", "Hello, I'm Dr Faisal and today we are going to talk about your heart attack.", 7000),
            Segment("patient", "Hello doctor.", 12000));

        var stripped = SpeakingTranscriptEvidence.StripConnectivityChatter(segments);

        Assert.Equal(
            new[]
            {
                "Hello, I'm Dr Faisal and today we are going to talk about your heart attack.",
                "Hello doctor.",
            },
            TextsOf(stripped));
    }

    [Fact]
    public void Strip_KeepsTheRealOpeningThatSharesASegmentWithTheCheck()
    {
        var segments = Json(
            Segment("candidate", "Hi, can you hear me? Hello, I'm Dr Faisal and I'll be looking after you today.", 1000));

        var stripped = SpeakingTranscriptEvidence.StripConnectivityChatter(segments);

        Assert.Equal(new[] { "Hello, I'm Dr Faisal and I'll be looking after you today." }, TextsOf(stripped));
        using var doc = JsonDocument.Parse(stripped);
        var kept = doc.RootElement[0];
        Assert.Equal("candidate", kept.GetProperty("speaker").GetString());
        Assert.Equal(1000, kept.GetProperty("startMs").GetInt32());
    }

    [Fact]
    public void Strip_NeverTouchesAnythingAfterTheRealRolePlayStarts()
    {
        var segments = Json(
            Segment("candidate", "Hello, I'm Dr Faisal.", 1000),
            Segment("patient", "Go ahead.", 3000),
            Segment("candidate", "Can you hear me? Sorry, the line dropped for a moment.", 5000));

        Assert.Same(segments, SpeakingTranscriptEvidence.StripConnectivityChatter(segments));
    }

    [Fact]
    public void Strip_KeepsAGreetingThatIsAssessed()
    {
        var segments = Json(
            Segment("candidate", "Hello.", 1000),
            Segment("patient", "Go ahead.", 3000));

        Assert.Same(segments, SpeakingTranscriptEvidence.StripConnectivityChatter(segments));
    }

    [Fact]
    public void Strip_OnlyChatter_LeavesAnEmptyTranscript()
    {
        var segments = Json(
            Segment("candidate", "Can you hear me?", 1000),
            Segment("patient", "Yes, I can hear you. Go ahead.", 3000));

        Assert.Equal("[]", SpeakingTranscriptEvidence.StripConnectivityChatter(segments));
    }

    [Fact]
    public void Strip_PreservesTheOtherFieldsOfTheKeptSegments()
    {
        var segments = "[" +
            """{"speaker":"candidate","startMs":0,"endMs":900,"text":"Can you hear me?"},""" +
            """{"speaker":"candidate","startMs":2000,"endMs":4000,"text":"Hello, I'm Dr Faisal.","interrupted":true,"sourceRecordingId":"rec-1","words":[{"w":"Hello"}]}""" +
            "]";

        var stripped = SpeakingTranscriptEvidence.StripConnectivityChatter(segments);

        using var doc = JsonDocument.Parse(stripped);
        var kept = Assert.Single(doc.RootElement.EnumerateArray().ToArray());
        Assert.True(kept.GetProperty("interrupted").GetBoolean());
        Assert.Equal("rec-1", kept.GetProperty("sourceRecordingId").GetString());
        Assert.Equal("Hello", kept.GetProperty("words")[0].GetProperty("w").GetString());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("not json", "not json")]
    [InlineData("{}", "{}")]
    [InlineData("[]", "[]")]
    public void Strip_NeverFailsOnAnUnreadableTranscript(string? input, string expected)
        => Assert.Equal(expected, SpeakingTranscriptEvidence.StripConnectivityChatter(input));

    private static string Segment(string speaker, string text, int startMs)
        => JsonSerializer.Serialize(new { speaker, startMs, endMs = startMs + 1000, text });

    private static string Json(params string[] segments) => "[" + string.Join(",", segments) + "]";

    private static string[] TextsOf(string segmentsJson)
    {
        using var doc = JsonDocument.Parse(segmentsJson);
        return doc.RootElement.EnumerateArray().Select(s => s.GetProperty("text").GetString()!).ToArray();
    }
}
