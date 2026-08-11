using System.Text.Json;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Hubs;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Conversation.Asr;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Deterministic acceptance pins for the v1.1 specification. Provider calls,
/// browser rendering, and production deployment remain separate operational
/// gates; these tests protect the contracts that must never vary by provider.
/// </summary>
public sealed class SpeakingSimulationV11AcceptanceTests
{
    [Fact]
    public void S01_Server_timing_metadata_never_accepts_client_deadline()
    {
        var (interrupted, duration) = ConversationHub.ParseTurnMeta(
            "{\"interruptedPatient\":true,\"speechDurationMs\":999999999}");
        Assert.True(interrupted);
        Assert.Equal(999999999, duration);

        var timing = new SpeakingSimulationV11CardTimingSnapshot
        {
            PrepSeconds = 180,
            RolePlaySeconds = 300,
            ServerAuthoritative = true,
        };
        Assert.True(timing.ServerAuthoritative);
        Assert.Equal(180, timing.PrepSeconds);
        Assert.Equal(300, timing.RolePlaySeconds);
    }

    [Fact]
    public void S02_released_rubric_is_exactly_ten_criteria_and_excludes_rule55()
    {
        var rubric = SpeakingSimulationV11Contracts.RubricCriteria.Criteria;
        Assert.True(SpeakingSimulationV11Contracts.IsValidRubric(rubric));
        Assert.Equal(100, rubric.Sum(x => x.Weight));
        Assert.DoesNotContain(rubric.SelectMany(x => x.EnabledRuleIds),
            rule => string.Equals(rule, "R55", StringComparison.OrdinalIgnoreCase));

        var reordered = rubric.Reverse().ToArray();
        Assert.False(SpeakingSimulationV11Contracts.IsValidRubric(reordered));
    }

    [Fact]
    public void S03_actor_safety_boundary_removes_coaching_scoring_and_medical_advice()
    {
        Assert.Equal(SpeakingSimulationV11PersonaService.NeutralSilencePrompt,
            SpeakingSimulationV11PersonaService.SanitizeActorReply(
                "Excellent question. Your score is 400 and you should stop this medicine."));
        Assert.Equal(SpeakingSimulationV11PersonaService.NeutralSilencePrompt,
            SpeakingSimulationV11PersonaService.SanitizeActorReply(
                "The rulebook says C01.1 applies to your response."));
        Assert.Equal("I feel worried about the pain.",
            SpeakingSimulationV11PersonaService.SanitizeActorReply(" I feel worried about the pain. "));
    }

    [Fact]
    public void S04_your_patient_is_not_a_second_visit_signal()
    {
        Assert.False(SpeakingSimulationV11PersonaService.ContainsExplicitSecondVisitIndicator(
            "You are my patient and I need advice.", "returning for review"));
        Assert.True(SpeakingSimulationV11PersonaService.ContainsExplicitSecondVisitIndicator(
            "I am returning for a follow-up appointment.", "returning for review"));
    }

    [Fact]
    public void S05_warmup_segments_have_an_explicit_unscored_phase()
    {
        var json = ConversationHub.SerializeSpeakingSegments(new[]
        {
            new ConversationHub.SpeakingTurnSegment(
                "interlocutor", 0, 1000, "Hello", 1, false, "warmup"),
            new ConversationHub.SpeakingTurnSegment(
                "candidate", 1000, 2000, "Good morning", .98, false, "roleplay"),
        });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("warmup", doc.RootElement[0].GetProperty("phase").GetString());
        Assert.Equal("roleplay", doc.RootElement[1].GetProperty("phase").GetString());
    }

    [Fact]
    public void S06_transcript_serialization_keeps_speaker_timestamps_and_interruptions()
    {
        var json = ConversationHub.SerializeSpeakingSegments(new[]
        {
            new ConversationHub.SpeakingTurnSegment(
                "candidate", 120, 980, "Please tell me more", .87, true, "roleplay",
                "recording-1", new[] { new ConversationWordConfidence("Please", 120, 320, .91) }),
        });
        using var document = JsonDocument.Parse(json);
        var segment = document.RootElement[0];
        Assert.Equal("candidate", segment.GetProperty("speaker").GetString());
        Assert.Equal(120, segment.GetProperty("startMs").GetInt64());
        Assert.True(segment.GetProperty("interrupted").GetBoolean());
        Assert.Equal(.91, segment.GetProperty("words")[0].GetProperty("confidence").GetDouble());
    }

    [Fact]
    public void S07_malformed_client_metadata_is_non_blocking()
    {
        var result = ConversationHub.ParseTurnMeta("not-json");
        Assert.False(result.InterruptedPatient);
        Assert.Null(result.SpeechDurationMs);

        var turns = SpeakingSimulationV11EvidenceCaptureService.ParseSourceTurns(
            "[{\"speaker\":\"candidate\",\"text\":\"hello\",\"confidence\":0.2}]");
        Assert.Single(turns);
        Assert.Equal(.2, turns[0].Confidence);
    }

    [Fact]
    public void S08_audio_assessment_contract_is_audio_only_and_not_visual()
    {
        var properties = typeof(SpeakingSimulationV11AudioAssessmentResult)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();
        Assert.Contains(nameof(SpeakingSimulationV11AudioAssessmentResult.Score), properties);
        Assert.DoesNotContain(properties, name => name.Contains("eye", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("gesture", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("facial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Audio_signal_quality_marks_clipping_noise_and_near_silence_for_pcm()
    {
        var clipped = SpeakingSimulationV11AudioSignalAnalyzer.Analyze(
            new MemoryStream(BuildWave(Enumerable.Repeat(short.MaxValue, 8_000).ToArray())),
            "audio/wav");
        Assert.True(clipped.ClippingDetected);
        Assert.Equal("audio_clipping_detected", clipped.IssueCode);

        var noise = Enumerable.Range(0, 8_000)
            .Select(index => (short)(index % 2 == 0 ? 26_000 : -26_000))
            .ToArray();
        var noisy = SpeakingSimulationV11AudioSignalAnalyzer.Analyze(
            new MemoryStream(BuildWave(noise)), "audio/wav");
        Assert.True(noisy.SevereNoiseDetected);
        Assert.Equal("audio_severe_noise_detected", noisy.IssueCode);

        var silent = SpeakingSimulationV11AudioSignalAnalyzer.Analyze(
            new MemoryStream(BuildWave(new short[8_000])), "audio/wav");
        Assert.True(silent.NearSilenceDetected);
        Assert.Equal("audio_near_silent", silent.IssueCode);
    }

    [Fact]
    public void S09_assessment_parser_requires_all_released_criteria_and_valid_scores()
    {
        var body = JsonSerializer.Serialize(new
        {
            criteria = SpeakingSimulationV11Contracts.RubricCriteria.Criteria.Select(c => new
            {
                criterionCode = c.CriterionCode,
                score = 50,
                rationale = "source-backed",
                evidence = new[] { new { evidenceType = "quote", quote = "Hello", turnNumber = 1 } },
            }),
            confidence = new { label = "medium", score = .7, rangeLow = 200, rangeHigh = 300 },
        });
        Assert.NotNull(SpeakingSimulationV11AssessmentService.ParseAssessment(body));

        var invalid = body.Replace("\"score\":50", "\"score\":101", StringComparison.Ordinal);
        Assert.Null(SpeakingSimulationV11AssessmentService.ParseAssessment(invalid));
    }

    [Fact]
    public void S10_practice_score_uses_released_weights_and_500_projection()
    {
        var weighted = SpeakingSimulationV11Contracts.RubricCriteria.Criteria
            .Sum(c => 100m * c.Weight / 100m);
        Assert.Equal(100m, weighted);
        Assert.Equal(500, (int)Math.Round(weighted * 5m));
    }

    [Fact]
    public void S11_graph_disclaimer_is_persistent_and_distinct_from_official_result()
    {
        Assert.Contains("AI Estimated Practice Score", SpeakingSimulationV11Contracts.GraphDisclaimer);
        Assert.Contains("not an official OET result", SpeakingSimulationV11Contracts.GraphDisclaimer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void S12_evidence_contract_distinguishes_primary_score_sources()
    {
        var evidence = new SpeakingSimulationV11EvidenceResult(
            "quote", "supported", "relationship_building_empathy", 3,
            "I understand this is worrying.", 1200, 2200, "empathy", "Keep using empathy.",
            "high", .95m, "transcript-1", "recording-1", true);
        var teachingOnly = evidence with { EvidenceStatus = "teaching_only", IsPrimary = false };
        Assert.True(evidence.IsPrimary);
        Assert.False(teachingOnly.IsPrimary);
        Assert.Equal("teaching_only", teachingOnly.EvidenceStatus);
    }

    [Fact]
    public void S13_technical_review_has_no_estimated_score()
    {
        var response = new SpeakingSimulationV11AssessmentResponse(
            "review-1", SpeakingSimulationV11AssessmentStatus.TechnicalReview.ToString(), "card", "A",
            null, null, null, SpeakingSimulationV11Contracts.GraphDisclaimer, "low", null, null,
            "original_audio_unverified", DateTimeOffset.UtcNow);
        Assert.Equal(SpeakingSimulationV11AssessmentStatus.TechnicalReview.ToString(), response.Status);
        Assert.Null(response.EstimatedPracticeScore);
        Assert.Null(response.Report);
    }

    [Fact]
    public void S14_unknown_profession_cannot_be_silently_replaced_by_medicine()
    {
        Assert.False(RulebookProfessionParser.TryParse("not-a-profession", out _));
        Assert.True(RulebookProfessionParser.TryParse("physiotherapy", out var profession));
        Assert.Equal(ExamProfession.Physiotherapy, profession);
    }

    [Fact]
    public void S15_source_audio_evidence_carries_exact_timestamp_and_recording_identity()
    {
        var evidence = new SpeakingSimulationV11EvidenceResult(
            "audio_acoustic", "supported", "intelligibility_pronunciation", 4,
            "The candidate's source turn", 4200, 7600, "acoustic finding", "practise", "high", .9m,
            "transcript-2", "recording-2", true);
        Assert.Equal(4200, evidence.StartMs);
        Assert.Equal(7600, evidence.EndMs);
        Assert.Equal("recording-2", evidence.SourceRecordingId);
    }

    [Fact]
    public void S16_tutor_override_contract_preserves_original_assessment_identity()
    {
        var original = new SpeakingSimulationV11TutorOverrideResponse(
            "override-1", "assessment-1", "session-1", "tutor-1", 350, 330, 370,
            "calibrated review", "original-report", "override-report",
            SpeakingSimulationV11Contracts.SpecVersion, SpeakingSimulationV11Contracts.RubricVersion,
            SpeakingSimulationV11Contracts.CalibrationVersion, "azure-phoneme", "model-1",
            DateTimeOffset.UtcNow);
        Assert.Equal("assessment-1", original.AssessmentId);
        Assert.Equal("original-report", original.OriginalReportJson);
        Assert.Equal("override-report", original.OverrideReportJson);
    }

    [Fact]
    public void S17_operational_release_contract_includes_silence_threshold_and_audio_provider()
    {
        var gate = new SpeakingSimulationV11GateResult(
            true, Array.Empty<string>(), Array.Empty<string>(),
            SpeakingSimulationV11Contracts.SpecVersion,
            SpeakingSimulationV11Contracts.RubricVersion,
            SpeakingSimulationV11Contracts.RubricCriteria.Criteria,
            SpeakingSimulationV11Contracts.DefaultSilencePromptThresholdMs,
            "azure-phoneme",
            SpeakingSimulationV11Contracts.CalibrationVersion);
        Assert.True(gate.IsReleased);
        Assert.Equal(12_000, gate.SilencePromptThresholdMs);
        Assert.Equal("azure-phoneme", gate.AudioAssessmentProvider);
    }

    [Fact]
    public void S18_turn_telemetry_contains_operational_fields_but_no_candidate_content()
    {
        var names = typeof(SpeakingSimulationV11TurnTelemetry)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();
        Assert.Contains(nameof(SpeakingSimulationV11TurnTelemetry.AsrLatencyMs), names);
        Assert.Contains(nameof(SpeakingSimulationV11TurnTelemetry.ActorUsageRecordId), names);
        Assert.Contains(nameof(SpeakingSimulationV11TurnTelemetry.TtsLatencyMs), names);
        Assert.Contains(nameof(SpeakingSimulationV11TurnTelemetry.BudgetBreachCode), names);
        Assert.Contains(nameof(SpeakingSimulationV11TurnTelemetry.CostComponentsJson), names);
        Assert.DoesNotContain(names, name => name.Equals("RawAudio", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Equals("TranscriptText", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] BuildWave(IReadOnlyList<short> samples, int sampleRateHz = 16_000)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var dataSize = samples.Count * sizeof(short);
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRateHz);
            writer.Write(sampleRateHz * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(dataSize);
            foreach (var sample in samples)
            {
                writer.Write(sample);
            }
        }

        return output.ToArray();
    }
}
