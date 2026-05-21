using System.Reflection;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Phase 12 (P12) of the OET Speaking module plan — pins the
/// evidence-quote verification contract used by
/// <see cref="SpeakingAiAssessmentService"/>.
///
/// The AI prompt instructs the model to return evidence quotes that are
/// verbatim substrings of the learner's transcript turns. The service
/// uses a normalised whitespace match (collapsed spaces, case-insensitive
/// substring) to verify each quote. Fabricated quotes — quotes that do
/// not appear in the transcript — MUST be detected so the assessment row
/// can be flagged for review and the AI cache invalidated if the
/// hallucination rate spikes.
///
/// The verification helpers are private statics on
/// <see cref="SpeakingAiAssessmentService"/>; we reach them through
/// reflection here to keep the production surface API-stable while still
/// pinning the security-relevant contract. If the helpers are ever
/// promoted to public or moved to a sibling class, update the
/// <see cref="ResolveContainsNormalised"/> + <see cref="ResolveExtractTranscriptText"/>
/// helpers accordingly.
/// </summary>
public sealed class AssessmentEvidenceVerificationTests
{
    private static MethodInfo ResolveContainsNormalised()
    {
        // Static, non-public, signature (string, string) → bool.
        var method = typeof(SpeakingAiAssessmentService)
            .GetMethod("ContainsNormalised",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(string) },
                modifiers: null);
        Assert.NotNull(method);
        return method!;
    }

    private static MethodInfo ResolveExtractTranscriptText()
    {
        var method = typeof(SpeakingAiAssessmentService)
            .GetMethod("ExtractTranscriptText",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);
        Assert.NotNull(method);
        return method!;
    }

    private static bool InvokeContains(string haystack, string needle)
    {
        var method = ResolveContainsNormalised();
        var result = method.Invoke(null, new object[] { haystack, needle });
        return (bool)result!;
    }

    private static string InvokeExtract(string segmentsJson)
    {
        var method = ResolveExtractTranscriptText();
        var result = method.Invoke(null, new object[] { segmentsJson });
        return (string)result!;
    }

    // ─────────────────────────────────────────────────────────────────
    // Real transcript substrings MUST verify positively.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifiesQuotesPresentInTranscript()
    {
        const string segmentsJson = """
            [
              {"speaker":"candidate","startMs":0,"endMs":4000,"text":"Hello Mrs Carter, I'm Sarah, the nurse looking after you today."},
              {"speaker":"candidate","startMs":4500,"endMs":9000,"text":"I'd like to talk about your discharge plan and the paracetamol you'll be taking at home."},
              {"speaker":"candidate","startMs":9500,"endMs":12000,"text":"Is it alright if we go through how often to take it?"}
            ]
            """;
        var haystack = InvokeExtract(segmentsJson);

        // Exact substrings — must verify.
        Assert.True(InvokeContains(haystack, "I'm Sarah"));
        Assert.True(InvokeContains(haystack, "the paracetamol you'll be taking"));
        Assert.True(InvokeContains(haystack, "how often to take it"));

        // Case-insensitive match — must verify.
        Assert.True(InvokeContains(haystack, "I'M SARAH"));

        // Whitespace-normalised match — extra inner spaces should still verify.
        Assert.True(InvokeContains(haystack, "I'm   Sarah"));
    }

    // ─────────────────────────────────────────────────────────────────
    // Fabricated quotes (not in transcript) MUST NOT verify.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RejectsQuoteNotInTranscript()
    {
        const string segmentsJson = """
            [
              {"speaker":"candidate","startMs":0,"endMs":4000,"text":"Hello Mrs Carter, I'm Sarah, the nurse looking after you today."},
              {"speaker":"candidate","startMs":4500,"endMs":9000,"text":"I'd like to talk about your discharge plan."}
            ]
            """;
        var haystack = InvokeExtract(segmentsJson);

        // Pure fabrication — no overlap with transcript.
        Assert.False(InvokeContains(haystack, "I prescribed a follow-up CT scan"));

        // Subtle hallucination — name swap. A real OET interlocutor would
        // be Mrs Carter; the AI must not be allowed to cite "Mrs Smith"
        // as a verbatim quote.
        Assert.False(InvokeContains(haystack, "Hello Mrs Smith"));

        // Plausible-sounding but absent.
        Assert.False(InvokeContains(haystack, "explained the dosage carefully"));
    }

    [Fact]
    public void RejectsEmptyQuote()
    {
        // Defensive: an empty quote must never silently pass.
        const string segmentsJson = """
            [{"speaker":"candidate","startMs":0,"endMs":2000,"text":"Some real text."}]
            """;
        var haystack = InvokeExtract(segmentsJson);

        Assert.False(InvokeContains(haystack, string.Empty));
        Assert.False(InvokeContains(haystack, "   "));
    }

    [Fact]
    public void RejectsQuoteWhenTranscriptIsEmpty()
    {
        // Tolerate corrupt JSON segments — ExtractTranscriptText returns
        // string.Empty rather than throwing. Verification then refuses
        // every quote so the assessment can be flagged downstream.
        Assert.Equal(string.Empty, InvokeExtract("not-valid-json"));
        Assert.False(InvokeContains(string.Empty, "anything"));
    }

    // ─────────────────────────────────────────────────────────────────
    // ExtractTranscriptText collapses the segment array into a single
    // searchable haystack. Pinning this so future changes don't silently
    // skip non-candidate speakers (and thus let AI quote-fabricate AI
    // patient lines as if the learner had said them).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractTranscriptText_ConcatenatesAllSegments()
    {
        // NOTE: today the helper joins every segment regardless of
        // speaker. That's deliberate so the AI can cite either party in
        // an evidence quote, but it means the assertion focuses on
        // completeness rather than speaker filtering.
        const string segmentsJson = """
            [
              {"speaker":"candidate","text":"first turn"},
              {"speaker":"patient","text":"reply"},
              {"speaker":"candidate","text":"second turn"}
            ]
            """;
        var haystack = InvokeExtract(segmentsJson);
        Assert.Contains("first turn", haystack);
        Assert.Contains("reply", haystack);
        Assert.Contains("second turn", haystack);
    }

    [Fact]
    public void ExtractTranscriptText_TolerantOfMissingText()
    {
        // Segments without a `text` field should be silently skipped, not
        // crash the verifier.
        const string segmentsJson = """
            [
              {"speaker":"candidate","startMs":0,"endMs":1000},
              {"speaker":"candidate","text":"the only quotable segment"}
            ]
            """;
        var haystack = InvokeExtract(segmentsJson);
        Assert.Contains("the only quotable segment", haystack);
    }
}
