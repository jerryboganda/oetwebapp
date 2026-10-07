using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// The combined Full Mock judgement (owner spec 4 Oct 2026). A two-card test is assessed as ONE performance: the
/// grader reads both role-plays together and scores each of the nine criteria once, so the candidate gets one set of
/// criterion scores, one reported score out of 500 and one grade — never an average of two card scores. The card
/// grades still run first (they carry the per-card breakdown, the audio evidence and the credit settlement); the
/// combined grade is an extra, durable operation (<see cref="SpeakingCanonicalAssessmentService"/>) whose failure leaves
/// the test without an overall number rather than with a made-up one.
/// </summary>
public sealed partial class SpeakingAiAssessmentService
{
    /// <summary>The combined test's prompt template: the card template's rubric, band descriptors and reply schema with
    /// both role-plays in one user message. A different template is a different grader version, so the combined
    /// judgement is calibrated on its own and stays "provisional" until it has been.</summary>
    internal const string CombinedPromptTemplateId = PromptTemplateId + "-combined";

    /// <summary>Grades a finished AI exam once and stores the result on the exam (idempotent: a stored result is returned).</summary>
    public async Task<SpeakingAiAssessmentProjection> RunCombinedAssessmentAsync(string examId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(examId))
        {
            throw ApiException.Validation("SPEAKING_EXAM_ID_REQUIRED", "Speaking exam id is required.");
        }

        var exam = await db.SpeakingExamSessions.FirstOrDefaultAsync(e => e.Id == examId, ct)
            ?? throw ApiException.NotFound("speaking_exam_not_found", "That Speaking exam does not exist.");

        if (ProjectCombinedJson(exam.CombinedAssessmentJson) is { } stored) return stored;

        if (exam.Mode != SpeakingExamMode.Ai
            || string.IsNullOrWhiteSpace(exam.SessionAId)
            || string.IsNullOrWhiteSpace(exam.SessionBId))
        {
            throw ApiException.Conflict("speaking_exam_not_gradable", "Only an AI exam with both cards can be graded as one test.");
        }

        var sessionIds = new[] { exam.SessionAId!, exam.SessionBId! };
        var sessions = await db.SpeakingSessions.AsNoTracking().Where(s => sessionIds.Contains(s.Id)).ToListAsync(ct);
        var cards = new List<CombinedCardInput>(2);
        var cardGrades = new List<SpeakingAiAssessment>(2);
        foreach (var sessionId in sessionIds)
        {
            var session = sessions.FirstOrDefault(s => s.Id == sessionId)
                ?? throw ApiException.NotFound("speaking_session_not_found", "That Speaking session does not exist.");
            var card = await db.RolePlayCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == session.RolePlayCardId, ct)
                ?? throw ApiException.NotFound("role_play_card_not_found", "That role-play card does not exist.");
            var script = await db.InterlocutorScripts.AsNoTracking().FirstOrDefaultAsync(s => s.RolePlayCardId == card.Id, ct);
            SpeakingCardType? cardType = null;
            if (!string.IsNullOrWhiteSpace(card.CardTypeId))
            {
                cardType = await db.SpeakingCardTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == card.CardTypeId, ct);
            }

            var transcript = await db.SpeakingTranscripts.AsNoTracking()
                .Where(t => t.SpeakingSessionId == sessionId && t.IsLatest)
                .OrderByDescending(t => t.GeneratedAt)
                .FirstOrDefaultAsync(ct)
                ?? throw ApiException.Conflict("speaking_session_no_transcript",
                    "An AI assessment requires a transcript. Wait for transcription to complete and try again.");

            // Ordered in memory (SQLite cannot ORDER BY a DateTimeOffset; a session has a handful of rows).
            var grade = (await db.SpeakingAiAssessments.AsNoTracking().Where(a => a.SpeakingSessionId == sessionId).ToListAsync(ct))
                .OrderByDescending(a => a.GeneratedAt)
                .FirstOrDefault()
                ?? throw ApiException.Conflict(ExamCardsNotGradedCode, "Both cards must be graded before the test is graded as one.");

            cards.Add(new CombinedCardInput(card, script, cardType, transcript));
            cardGrades.Add(grade);
        }

        // Funded the same way the card grades were: a free sample or a credit-funded card skips the plan gate.
        var grant = false;
        foreach (var session in sessions)
        {
            grant |= await FreeSamples.FreeSampleService.IsFreeSpeakingSessionAsync(db, session, ct)
                || await SpeakingCreditSettlement.IsCreditFundedAsync(db, session, ct);
        }

        var context = sessions.Any(s => !string.IsNullOrWhiteSpace(s.MockSetId) || !string.IsNullOrWhiteSpace(s.MockSessionId))
            ? AiAssessmentContext.Mock
            : AiAssessmentContext.Practice;

        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ParseProfession(cards[0].Card.ProfessionId),
            Task = AiTaskMode.Score,
            CardType = CombinedCardToken(RulebookCardToken(cards[0].Card), RulebookCardToken(cards[1].Card)),
        });

        // The audio evidence the card grades already stored (one audio call per card, never a third).
        var audio = CombineAudioEvidence(ReadStoredAudioEvidence(cardGrades[0]), ReadStoredAudioEvidence(cardGrades[1]));

        var outcome = await GradeCoreAsync(new SpeakingGradeInput(
            LogKey: examId,
            UserId: exam.UserId,
            Prompt: prompt,
            TemplateId: CombinedPromptTemplateId,
            // Neither card's score is in the prompt: the grader judges the performance, it does not average results.
            UserInput: BuildCombinedUserInput(cards, audio),
            TranscriptText: string.Join(
                "\n",
                cards.Select(c => ExtractTranscriptText(SpeakingTranscriptEvidence.StripConnectivityChatter(c.Transcript.SegmentsJson)))),
            FreeSampleGrant: grant,
            Context: context,
            Audio: audio), ct);

        var now = DateTimeOffset.UtcNow;
        var row = new SpeakingAiAssessment
        {
            Id = $"spa_exam_{examId}",
            SpeakingSessionId = exam.SessionAId!,
            TranscriptId = cards[0].Transcript.Id,
            Provider = outcome.Provider,
            ModelId = outcome.ModelId,
            PromptTemplateId = CombinedPromptTemplateId,
            GraderVersion = outcome.GraderVersion,
            Intelligibility = outcome.Scores.Intelligibility,
            Fluency = outcome.Scores.Fluency,
            Appropriateness = outcome.Scores.Appropriateness,
            GrammarExpression = outcome.Scores.GrammarExpression,
            RelationshipBuilding = outcome.Scores.RelationshipBuilding,
            PatientPerspective = outcome.Scores.PatientPerspective,
            Structure = outcome.Scores.Structure,
            InformationGathering = outcome.Scores.InformationGathering,
            InformationGiving = outcome.Scores.InformationGiving,
            EstimatedScaledScore = outcome.ReportedScaled,
            ReadinessBand = outcome.ReadinessBand,
            PerCriterionRationalesJson = JsonSerializer.Serialize(outcome.RationalesPayload),
            OverallSummary = outcome.OverallSummary ?? string.Empty,
            ConfidenceBand = outcome.ConfidenceBand,
            GeneratedAt = now,
            RulebookFindingsJson = "[]",
            IsAdvisory = true,
            RubricVersion = exam.RulebookVersion,
            CardId = exam.CardAId,
        };

        // Another runner may have finished while the grader was working: the first stored result stands.
        var already = await db.SpeakingExamSessions.AsNoTracking()
            .Where(e => e.Id == examId)
            .Select(e => e.CombinedAssessmentJson)
            .FirstOrDefaultAsync(ct);
        if (ProjectCombinedJson(already) is { } winner) return winner;

        // One row shape for card and test: the same projection reads both, so the grade, label, evidence and report
        // can never be derived two different ways.
        exam.CombinedAssessmentJson = JsonSerializer.Serialize(row);
        exam.CombinedScaledSnapshot = outcome.ReportedScaled;
        exam.ReadinessBandSnapshot = outcome.ReadinessBand;
        exam.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return ProjectAssessment(row, RehydrateCriterionScores(row));
    }

    /// <summary>The stored combined judgement of an exam, as the learner reads it; null when none has been stored.</summary>
    internal static SpeakingAiAssessmentProjection? ProjectCombinedJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var row = JsonSerializer.Deserialize<SpeakingAiAssessment>(json);
            return row is null ? null : ProjectAssessment(row, RehydrateCriterionScores(row));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Thrown (as a conflict) while a card of the exam has no grade yet: the combined run retries shortly.</summary>
    internal const string ExamCardsNotGradedCode = "speaking_exam_cards_not_graded";

    /// <summary>The rulebook rule token for a two-card test: the one specialised card type wins over the generic
    /// token; two different specialised types (rare) fall back to the generic one.</summary>
    internal static string CombinedCardToken(string first, string second)
    {
        const string generic = "role_play";
        if (string.Equals(first, second, StringComparison.Ordinal)) return first;
        if (first == generic) return second;
        if (second == generic) return first;
        return generic;
    }

    private sealed record CombinedCardInput(
        RolePlayCard Card, InterlocutorScript? Script, SpeakingCardType? CardType, SpeakingTranscript Transcript);

    private static string BuildCombinedUserInput(IReadOnlyList<CombinedCardInput> cards, SpeakingAudioEvidence? audio)
    {
        var sb = new StringBuilder();
        sb.AppendLine(PROMPT_TEMPLATE_V3);
        sb.AppendLine();
        sb.AppendLine("---- THIS IS ONE COMPLETE OET SPEAKING TEST: TWO ROLE-PLAYS BY THE SAME CANDIDATE ----");
        sb.AppendLine("Score each of the nine criteria ONCE, for the candidate's performance across BOTH role-plays together, against the band descriptors. Do not score the role-plays separately and do not average: weigh the whole test, so a strong second role-play neither erases nor excuses a weak first one. Evidence quotes may come from either role-play. `strengths`, `priorityWeaknesses` and `drills` may refer to either role-play; say which (for example \"in the first role-play\"). Never mention a separate score for a role-play.");
        sb.AppendLine();
        for (var i = 0; i < cards.Count; i++)
        {
            sb.AppendLine($"======== ROLE-PLAY {i + 1} OF {cards.Count} ========");
            AppendCardSections(sb, cards[i].Card, cards[i].Script, cards[i].CardType);
        }

        AppendEvidenceNote(sb, cards.All(c => IsLiveVoiceTranscript(c.Transcript)), plural: true, audio);
        for (var i = 0; i < cards.Count; i++)
        {
            AppendTranscriptSections(sb, cards[i].Transcript, $" OF ROLE-PLAY {i + 1}");
        }

        sb.AppendLine("Now produce the strict JSON object specified above.");
        return sb.ToString();
    }

    // ── The acoustic evidence of a whole test ────────────────────────────────────────────

    /// <summary>The audio evidence a card grade stored (the reverse of the <c>_acoustic</c> payload); null when the audio
    /// stage did not run for that card.</summary>
    internal static SpeakingAudioEvidence? ReadStoredAudioEvidence(SpeakingAiAssessment row)
    {
        if (string.IsNullOrWhiteSpace(row.PerCriterionRationalesJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(row.PerCriterionRationalesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(AcousticKey, out var stored)
                || stored.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (TryReadString(stored, "source") != "audio")
            {
                return SpeakingAudioEvidence.Unavailable(TryReadString(stored, "reason") ?? "no_audio") with
                {
                    ClipCount = TryReadInt(stored, "clips") ?? 0,
                    DurationMs = TryReadInt(stored, "durationMs") ?? 0,
                    AudioMs = TryReadInt(stored, "audioMs") ?? 0,
                    SpeechMs = TryReadInt(stored, "speechMs") ?? 0,
                    Turns = TryReadInt(stored, "turns") ?? 0,
                    TurnsWithClip = TryReadInt(stored, "turnsWithClip") ?? 0,
                };
            }

            var observations = new List<SpeakingAudioObservation>();
            if (stored.TryGetProperty("observations", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var issue = TryReadString(item, "issue");
                    if (string.IsNullOrWhiteSpace(issue)) continue;
                    observations.Add(new SpeakingAudioObservation(
                        TryReadInt(item, "clip") ?? 1, TryReadInt(item, "approxSecond") ?? 0, issue, TryReadString(item, "example")));
                }
            }

            return new SpeakingAudioEvidence
            {
                Status = SpeakingAudioEvidence.StatusAudio,
                // The audio score replaced the grader's own, so the stored criterion score IS the audio judgement.
                IntelligibilityScore = row.Intelligibility,
                IntelligibilityRationale = ReadRationales(row.PerCriterionRationalesJson).TryGetValue("intelligibility", out var packed)
                    ? packed.Rationale
                    : null,
                AudioQuality = TryReadString(stored, "audioQuality") ?? "unknown",
                PatientVoiceBleed = stored.TryGetProperty("patientVoiceBleed", out var bleed) && bleed.ValueKind == JsonValueKind.True,
                Confidence = TryReadString(stored, "confidence") ?? "medium",
                Observations = observations,
                Model = TryReadString(stored, "model"),
                ClipCount = TryReadInt(stored, "clips") ?? 0,
                DurationMs = TryReadInt(stored, "durationMs") ?? 0,
                AudioMs = TryReadInt(stored, "audioMs") ?? 0,
                SpeechMs = TryReadInt(stored, "speechMs") ?? 0,
                Turns = TryReadInt(stored, "turns") ?? 0,
                TurnsWithClip = TryReadInt(stored, "turnsWithClip") ?? 0,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The one acoustic evidence the combined grader is given. null = the audio stage never ran for either card (grading is
    /// exactly as without it). Both cards judged from audio: one Intelligibility
    /// (<see cref="OetScoring.SpeakingCombinedIntelligibility"/>), the observations of both (each marked with its
    /// role-play) and the weaker confidence. Anything less is unavailable for the WHOLE test: Intelligibility is then the
    /// grader's labelled, low-confidence transcript estimate, never half an audio judgement presented as audio.
    /// </summary>
    internal static SpeakingAudioEvidence? CombineAudioEvidence(SpeakingAudioEvidence? first, SpeakingAudioEvidence? second)
    {
        if (first is null && second is null) return null;

        // The test's totals travel with the verdict, usable or not: how many clips and how much audio there was for how much
        // candidate speech is what tells a thin recording from a stage that never ran.
        SpeakingAudioEvidence WithTotals(SpeakingAudioEvidence evidence) => evidence with
        {
            ClipCount = (first?.ClipCount ?? 0) + (second?.ClipCount ?? 0),
            DurationMs = (first?.DurationMs ?? 0) + (second?.DurationMs ?? 0),
            AudioMs = (first?.AudioMs ?? 0) + (second?.AudioMs ?? 0),
            SpeechMs = (first?.SpeechMs ?? 0) + (second?.SpeechMs ?? 0),
            Turns = (first?.Turns ?? 0) + (second?.Turns ?? 0),
            TurnsWithClip = (first?.TurnsWithClip ?? 0) + (second?.TurnsWithClip ?? 0),
        };

        if (first is not { IsAudio: true } || second is not { IsAudio: true })
        {
            if (first is not { IsAudio: true } && second is not { IsAudio: true })
            {
                return WithTotals(SpeakingAudioEvidence.Unavailable(first?.Reason ?? second?.Reason ?? "no_audio"));
            }

            return WithTotals(SpeakingAudioEvidence.Unavailable("partial_audio"));
        }

        static string Join(string? one, string? two)
            => string.Join(" ", new[]
            {
                string.IsNullOrWhiteSpace(one) ? null : $"Role-play 1: {one.Trim()}",
                string.IsNullOrWhiteSpace(two) ? null : $"Role-play 2: {two.Trim()}",
            }.Where(part => part is not null));

        static int QualityRank(string quality) => quality switch { "poor" => 0, "fair" => 1, "good" => 3, _ => 2 };
        static int ConfidenceRank(string confidence) => confidence switch { "low" => 0, "high" => 2, _ => 1 };

        return new SpeakingAudioEvidence
        {
            Status = SpeakingAudioEvidence.StatusAudio,
            IntelligibilityScore = OetScoring.SpeakingCombinedIntelligibility(first.IntelligibilityScore ?? 0, second.IntelligibilityScore ?? 0),
            IntelligibilityRationale = Join(first.IntelligibilityRationale, second.IntelligibilityRationale),
            AudioQuality = QualityRank(first.AudioQuality) <= QualityRank(second.AudioQuality) ? first.AudioQuality : second.AudioQuality,
            PatientVoiceBleed = first.PatientVoiceBleed || second.PatientVoiceBleed,
            Confidence = ConfidenceRank(first.Confidence) <= ConfidenceRank(second.Confidence) ? first.Confidence : second.Confidence,
            Observations =
            [
                .. first.Observations.Select(o => o with { Issue = $"Role-play 1: {o.Issue}" }),
                .. second.Observations.Select(o => o with { Issue = $"Role-play 2: {o.Issue}" }),
            ],
            Model = first.Model ?? second.Model,
            ClipCount = first.ClipCount + second.ClipCount,
            DurationMs = first.DurationMs + second.DurationMs,
            AudioMs = first.AudioMs + second.AudioMs,
            SpeechMs = first.SpeechMs + second.SpeechMs,
            Turns = first.Turns + second.Turns,
            TurnsWithClip = first.TurnsWithClip + second.TurnsWithClip,
        };
    }
}
