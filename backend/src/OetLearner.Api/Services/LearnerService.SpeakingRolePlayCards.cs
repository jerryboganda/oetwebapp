using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

// Phase 1 (B.1) of the OET Speaking module roadmap.
//
// Learner-facing read for `RolePlayCard`. This is the **only** code path
// that returns a role-play card to a learner — and by construction the
// projection NEVER includes any field from `InterlocutorScript` (no FK
// join, no Include, no manual property copy). The pinned xUnit test
// `RolePlayCardSerializationTests` enforces the leak-prevention guard.
//
// The returned anonymous object is deliberately the same shape the
// frontend `RolePlayCardLearnerDetail` interface expects so we keep one
// source of truth for the candidate-card schema between admin and
// learner surfaces.
public partial class LearnerService
{
    public async Task<object> GetSpeakingRolePlayCardForLearnerAsync(
        string userId,
        string cardId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_ID_REQUIRED",
                "Role-play card id is required.");
        }

        // We still tie this to a real learner profile so the endpoint
        // can't be hit unauthenticated through some test seam — the
        // /v1 group already enforces LearnerOnly auth but this keeps
        // service-level callers honest.
        if (!string.IsNullOrWhiteSpace(userId))
        {
            await EnsureLearnerProfileAsync(userId, ct);
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cardId, ct);

        if (card is null
            || (card.Status != ContentStatus.Published
                && card.Status != ContentStatus.Archived))
        {
            // Archived cards are still resolvable so a learner who has
            // started a session against a card that was later archived
            // can finish it. Drafts and in-review cards are NotFound
            // from the learner perspective.
            throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");
        }

        var tasks = new[] { card.Task1, card.Task2, card.Task3, card.Task4, card.Task5 }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .ToArray();

        var criteriaFocus = AdminService.DeserializeCriteriaFocus(card.CriteriaFocusJson);

        return new
        {
            cardId = card.Id,
            professionId = card.ProfessionId,
            scenarioTitle = card.ScenarioTitle,
            setting = card.Setting,
            candidateRole = card.CandidateRole,
            interlocutorRole = card.InterlocutorRole,
            patientName = card.PatientName,
            patientAge = card.PatientAge,
            background = card.Background,
            tasks,
            allowedNotes = card.AllowedNotes,
            prepTimeSeconds = card.PrepTimeSeconds,
            rolePlayTimeSeconds = card.RolePlayTimeSeconds,
            patientEmotion = card.PatientEmotion,
            communicationGoal = card.CommunicationGoal,
            clinicalTopic = card.ClinicalTopic,
            difficulty = card.Difficulty,
            criteriaFocus,
            disclaimer = card.Disclaimer,
        };
    }
}
