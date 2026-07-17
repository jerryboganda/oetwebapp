using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Hubs;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Tests.Speaking;

/// <summary>
/// Candidate-first role-play invariants (2026-07-03 owner rule):
///   * The patient persona never instructs the AI to open the consultation —
///     the admin-authored OpeningResponse becomes the patient's scripted
///     FIRST REPLY, delivered only after the candidate speaks.
///   * Client turn metadata ({"interruptedPatient":true,...}) is parsed
///     defensively and the `interrupted` flag round-trips through the
///     transcript segment serialisation the AI grader reads back.
///   * The grader's server-computed INTERACTION SIGNALS count candidate
///     barge-ins so sparse flags cannot be overlooked in a long transcript.
/// </summary>
public sealed class SpeakingCandidateFirstRoleplayTests
{
    private static RolePlayCard MakeCard() => new()
    {
        Id = "rpc-1",
        ContentItemId = "ci-1",
        ProfessionId = "medicine",
        ScenarioTitle = "Post-operative pain review",
        Setting = "Hospital ward",
        CandidateRole = "Doctor",
        InterlocutorRole = "Patient",
        Background = "Day two after knee surgery.",
        PatientName = "Alex Morgan",
        PatientAge = "54",
        PatientEmotion = "anxious",
        CommunicationGoal = "Reassure and plan analgesia",
        ClinicalTopic = "pain management",
        Difficulty = "core",
        CriteriaFocusJson = "[]",
        Disclaimer = "Practice estimate only.",
        Status = ContentStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static InterlocutorScript MakeScript() => new()
    {
        Id = "is-1",
        RolePlayCardId = "rpc-1",
        OpeningResponse = "Doctor, this pain is much worse than yesterday.",
        Prompt1 = "Push back on taking more opioids",
        HiddenInformation = "Previous bad reaction to morphine",
        ResistanceLevel = ResistanceLevel.Medium,
        ClosingCue = "Accept the plan once reassured",
        EmotionalState = "Worried",
        LayLanguageTriggersJson = "[]",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    // ── Persona: candidate opens ──────────────────────────────────────────

    [Fact]
    public void PatientPersona_DoesNotInstructAiToOpen()
    {
        var persona = ConversationHub.BuildPatientPersonaPrompt(MakeCard(), MakeScript());

        Assert.DoesNotContain("Open with this line", persona, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OPENS the consultation", persona, StringComparison.Ordinal);
        Assert.Contains("Do not speak until they do", persona, StringComparison.Ordinal);
    }

    [Fact]
    public void PatientPersona_RepurposesOpeningResponseAsFirstReply()
    {
        var script = MakeScript();
        var persona = ConversationHub.BuildPatientPersonaPrompt(MakeCard(), script);

        Assert.Contains("deliver this scripted response naturally", persona, StringComparison.Ordinal);
        Assert.Contains(script.OpeningResponse, persona, StringComparison.Ordinal);
    }

    [Fact]
    public void PatientPersona_ContainsPatientRealismAndInterruptionRules()
    {
        var persona = ConversationHub.BuildPatientPersonaPrompt(MakeCard(), MakeScript());

        Assert.Contains("Answer ONLY the question the candidate asked", persona, StringComparison.Ordinal);
        Assert.Contains("one to three short sentences", persona, StringComparison.Ordinal);
        Assert.Contains("interrupted:true", persona, StringComparison.Ordinal);
        // Existing behaviours must survive the rework.
        Assert.Contains("Hidden information (only reveal on direct questioning):", persona, StringComparison.Ordinal);
        Assert.Contains("Push back on taking more opioids", persona, StringComparison.Ordinal);
        Assert.Contains("Never coach the candidate.", persona, StringComparison.Ordinal);
    }

    // ── Turn metadata parsing ─────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"interruptedPatient\":\"yes\"}")]
    public void ParseTurnMeta_MalformedOrMissing_YieldsNoOpDefaults(string? metaJson)
    {
        var (interrupted, durationMs) = ConversationHub.ParseTurnMeta(metaJson);

        Assert.False(interrupted);
        Assert.Null(durationMs);
    }

    [Fact]
    public void ParseTurnMeta_ValidMeta_ParsesFlagAndDuration()
    {
        var (interrupted, durationMs) =
            ConversationHub.ParseTurnMeta("{\"interruptedPatient\":true,\"speechDurationMs\":12345}");

        Assert.True(interrupted);
        Assert.Equal(12345L, durationMs);
    }

    [Fact]
    public void ParseTurnMeta_NonPositiveDuration_IsIgnored()
    {
        var (interrupted, durationMs) =
            ConversationHub.ParseTurnMeta("{\"interruptedPatient\":true,\"speechDurationMs\":-40}");

        Assert.True(interrupted);
        Assert.Null(durationMs);
    }

    // ── Segment round-trip ────────────────────────────────────────────────

    [Fact]
    public void SpeakingSegments_InterruptedFlag_SurvivesSerializeParseRoundTrip()
    {
        var segments = new List<ConversationHub.SpeakingTurnSegment>
        {
            new("candidate", 0, 4_000, "Hello, I'm Dr Ahmed.", 0.95),
            new("patient", 4_800, 4_800, "Hello doctor.", 1.0),
            new("candidate", 6_000, 9_500, "Sorry to jump in —", 0.9, Interrupted: true),
        };

        var parsed = ConversationHub.ParseSpeakingSegments(
            ConversationHub.SerializeSpeakingSegments(segments));

        Assert.Equal(3, parsed.Count);
        Assert.False(parsed[0].Interrupted);
        Assert.False(parsed[1].Interrupted);
        Assert.True(parsed[2].Interrupted);
        Assert.Equal(6_000, parsed[2].StartMs);
        Assert.Equal(9_500, parsed[2].EndMs);
    }

    [Fact]
    public void SpeakingSegments_LegacyJsonWithoutInterrupted_ParsesAsNotInterrupted()
    {
        const string legacy =
            "[{\"speaker\":\"candidate\",\"startMs\":0,\"endMs\":0,\"text\":\"Hi\",\"confidence\":1.0,\"words\":[]}]";

        var parsed = ConversationHub.ParseSpeakingSegments(legacy);

        var segment = Assert.Single(parsed);
        Assert.False(segment.Interrupted);
    }

    // ── Grader interaction signals ────────────────────────────────────────

    [Fact]
    public void ComputeInteractionSignals_CountsOnlyInterruptedCandidateSegments()
    {
        var segmentsJson = ConversationHub.SerializeSpeakingSegments(
            new List<ConversationHub.SpeakingTurnSegment>
            {
                new("candidate", 0, 3_000, "Hello, I'm Dr Ahmed.", 0.95),
                new("patient", 3_800, 3_800, "Hello doctor.", 1.0),
                new("candidate", 5_000, 8_000, "Sorry, but —", 0.9, Interrupted: true),
                new("patient", 9_000, 9_000, "As I was saying…", 1.0),
                new("candidate", 12_000, 15_000, "Go on, please.", 0.9, Interrupted: true),
            });

        var (count, atMs) = SpeakingAiAssessmentService.ComputeInteractionSignals(segmentsJson);

        Assert.Equal(2, count);
        Assert.Equal(new[] { 5_000L, 12_000L }, atMs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"speaker\":\"candidate\"}")]
    public void ComputeInteractionSignals_MalformedTranscript_YieldsZeroSignals(string? segmentsJson)
    {
        var (count, atMs) = SpeakingAiAssessmentService.ComputeInteractionSignals(segmentsJson);

        Assert.Equal(0, count);
        Assert.Empty(atMs);
    }
}
