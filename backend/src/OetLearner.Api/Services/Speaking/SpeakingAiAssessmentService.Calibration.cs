using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

public sealed partial class SpeakingAiAssessmentService
{
    /// <summary>
    /// Grades one expert-marked performance with the CURRENT grader for the calibration harness and returns what it
    /// concluded. The same grader core and prompt a learner's grade uses, on the transcript pinned at promotion; nothing is
    /// persisted (no assessment row, no credit, no Jev call) and no learner is charged: <c>UserId</c> is null, which skips
    /// the plan gate. The audio stage runs whenever <paramref name="useAudio"/> is true, whatever the learner-facing admin
    /// flag says, so the audio judge can be calibrated before it is ever switched on.
    /// </summary>
    internal async Task<(SpeakingGradeOutcome Outcome, SpeakingAudioEvidence? Audio)> GradeForCalibrationAsync(
        string sessionId, string transcriptId, bool useAudio, CancellationToken ct)
    {
        var session = await db.SpeakingSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct)
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
            .FirstOrDefaultAsync(t => t.Id == transcriptId && t.SpeakingSessionId == sessionId, ct)
            ?? throw ApiException.Conflict("speaking_session_no_transcript", "The pinned transcript no longer exists.");

        var cardToken = RulebookCardToken(card);
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ParseProfession(card.ProfessionId),
            Task = AiTaskMode.Score,
            CardType = cardToken,
        });

        var gradedSegments = SpeakingTranscriptEvidence.StripConnectivityChatter(transcript.SegmentsJson);
        SpeakingAudioEvidence? audio = null;
        if (useAudio && audioEvidence is not null)
        {
            audio = await audioEvidence.AssessAsync(new SpeakingAudioAssessRequest(
                sessionId, UserId: null, card.ProfessionId, cardToken, gradedSegments, FreeSampleGrant: false, AiAssessmentContext.Practice), ct);
        }

        var outcome = await GradeCoreAsync(new SpeakingGradeInput(
            LogKey: sessionId,
            UserId: null,
            Prompt: prompt,
            TemplateId: PromptTemplateId,
            UserInput: BuildUserInput(card, script, transcript, cardType, audio),
            TranscriptText: ExtractTranscriptText(gradedSegments),
            FreeSampleGrant: false,
            Context: AiAssessmentContext.Practice,
            Audio: audio), ct);

        return (outcome, audio);
    }

    /// <summary>
    /// Grades one expert-marked WHOLE two-card test with the CURRENT combined grader for the calibration harness.
    /// The same combined core a learner's Full Mock uses (the <c>speaking.score.v3-combined</c> prompt, both cards and
    /// both transcripts in one user message, one set of nine criterion scores), but on the transcripts pinned at
    /// promotion and with nothing persisted (no assessment row, no credit, no Jev call) and no learner charged:
    /// <c>UserId</c> is null, which skips the plan gate. The audio stage runs per card whenever
    /// <paramref name="useAudio"/> is true and the two are combined by the same rule the learner path uses — the
    /// combined Intelligibility is judged from audio only when BOTH cards were.
    /// </summary>
    internal async Task<(SpeakingGradeOutcome Outcome, SpeakingAudioEvidence? Audio)> GradeCombinedForCalibrationAsync(
        string examId, string sessionAId, string sessionBId, string transcriptAId, string transcriptBId,
        bool useAudio, CancellationToken ct)
    {
        var sessionIds = new[] { sessionAId, sessionBId };
        var sessions = await db.SpeakingSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .ToListAsync(ct);
        if (sessions.Count != 2)
        {
            throw ApiException.NotFound("speaking_session_not_found", "A card session of that Full Mock no longer exists.");
        }

        var cards = new List<(Domain.RolePlayCard Card, InterlocutorScript? Script, SpeakingCardType? CardType, Domain.SpeakingTranscript Transcript)>(2);
        foreach (var (sessionId, transcriptId) in new[] { (sessionAId, transcriptAId), (sessionBId, transcriptBId) })
        {
            var session = sessions.First(s => s.Id == sessionId);
            var card = await db.RolePlayCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == session.RolePlayCardId, ct)
                ?? throw ApiException.NotFound("role_play_card_not_found", "A role-play card of that Full Mock no longer exists.");
            var script = await db.InterlocutorScripts.AsNoTracking().FirstOrDefaultAsync(s => s.RolePlayCardId == card.Id, ct);
            SpeakingCardType? cardType = null;
            if (!string.IsNullOrWhiteSpace(card.CardTypeId))
            {
                cardType = await db.SpeakingCardTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == card.CardTypeId, ct);
            }

            var transcript = await db.SpeakingTranscripts.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == transcriptId && t.SpeakingSessionId == sessionId, ct)
                ?? throw ApiException.Conflict("speaking_session_no_transcript", "A pinned transcript no longer exists.");
            cards.Add((card, script, cardType, transcript));
        }

        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ParseProfession(cards[0].Card.ProfessionId),
            Task = AiTaskMode.Score,
            CardType = CombinedCardToken(RulebookCardToken(cards[0].Card), RulebookCardToken(cards[1].Card)),
        });

        SpeakingAudioEvidence? audio = null;
        if (useAudio && audioEvidence is not null)
        {
            var perCard = new List<SpeakingAudioEvidence>(2);
            foreach (var (card, _, _, transcript) in cards)
            {
                var gradedSegments = SpeakingTranscriptEvidence.StripConnectivityChatter(transcript.SegmentsJson);
                perCard.Add(await audioEvidence.AssessAsync(new SpeakingAudioAssessRequest(
                    transcript.SpeakingSessionId, UserId: null, card.ProfessionId, RulebookCardToken(card),
                    gradedSegments, FreeSampleGrant: false, AiAssessmentContext.Practice), ct));
            }

            audio = CombineAudioEvidence(perCard[0], perCard[1]);
        }

        var outcome = await GradeCoreAsync(new SpeakingGradeInput(
            LogKey: examId,
            UserId: null,
            Prompt: prompt,
            TemplateId: CombinedPromptTemplateId,
            // Neither card's score is in the prompt: the grader judges the performance, it does not average results.
            UserInput: BuildCombinedUserInput(
                cards.Select(c => new CombinedCardInput(c.Card, c.Script, c.CardType, c.Transcript)).ToList(),
                audio),
            TranscriptText: string.Join("\n", cards.Select(c =>
                ExtractTranscriptText(SpeakingTranscriptEvidence.StripConnectivityChatter(c.Transcript.SegmentsJson)))),
            FreeSampleGrant: false,
            Context: AiAssessmentContext.Practice,
            Audio: audio), ct);

        return (outcome, audio);
    }
}
