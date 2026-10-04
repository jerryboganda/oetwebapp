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
}
