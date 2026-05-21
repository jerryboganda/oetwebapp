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
// Plan P2.1 — profession filter:
//   Both the list and detail endpoints enforce the rule
//     (RolePlayCard.ProfessionId = caller.ActiveProfessionId
//      OR ContentItem.ProfessionId IS NULL)
//     AND Status IN (Published, Archived)
//   A mismatch on profession returns 404 (NotFound), never 403, to avoid
//   leaking the existence of cards meant for other professions.
//
// Plan P2.2 — interlocutor script leakage guard:
//   The projection below uses only fields from the `RolePlayCard` table.
//   No join, no .Include, no anonymous-object reference to any
//   InterlocutorScript field. Pinned by InterlocutorScriptLeakageTests.
//
// The returned anonymous object is deliberately the same shape the
// frontend `RolePlayCardLearnerDetail` interface expects so we keep one
// source of truth for the candidate-card schema between admin and
// learner surfaces.
public partial class LearnerService
{
    /// <summary>
    /// Returns the published / archived role-play cards visible to a
    /// learner, filtered by their <see cref="LearnerUser.ActiveProfessionId"/>
    /// and the universal-profession flag on <see cref="ContentItem"/>.
    /// </summary>
    public async Task<object> ListSpeakingRolePlayCardsForLearnerAsync(
        string userId,
        string? difficulty,
        CancellationToken ct)
    {
        var user = await EnsureLearnerProfileAsync(userId, ct);
        var activeProfession = (user.ActiveProfessionId ?? string.Empty).Trim().ToLowerInvariant();

        // Load cards that match the learner's profession or are flagged
        // universal at the ContentItem level (ProfessionId == null). We
        // intentionally do NOT call `.Include(c => c.ContentItem)` — the
        // ContentItem join below is a left-join via key equality so we
        // only pull `ContentItem.ProfessionId` for the universal check
        // (no InterlocutorScript reference anywhere in this method).
        var rows = await (
            from card in db.RolePlayCards.AsNoTracking()
            join item in db.ContentItems.AsNoTracking()
                on card.ContentItemId equals item.Id into joined
            from item in joined.DefaultIfEmpty()
            where card.Status == ContentStatus.Published
                && (card.ProfessionId == activeProfession
                    || item == null
                    || item.ProfessionId == null)
            select new
            {
                card.Id,
                card.ProfessionId,
                card.ScenarioTitle,
                card.Setting,
                card.CandidateRole,
                card.InterlocutorRole,
                card.PatientName,
                card.PatientAge,
                card.Background,
                card.Task1,
                card.Task2,
                card.Task3,
                card.Task4,
                card.Task5,
                card.AllowedNotes,
                card.PrepTimeSeconds,
                card.RolePlayTimeSeconds,
                card.PatientEmotion,
                card.CommunicationGoal,
                card.ClinicalTopic,
                card.Difficulty,
                card.CriteriaFocusJson,
                card.Disclaimer,
                card.UpdatedAt,
                ContentItemProfessionId = item != null ? item.ProfessionId : null,
            })
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);

        var filtered = rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            var normalised = difficulty.Trim().ToLowerInvariant();
            filtered = filtered.Where(r => string.Equals(r.Difficulty, normalised, StringComparison.OrdinalIgnoreCase));
        }

        var summaries = filtered.Select(r =>
        {
            var tasks = new[] { r.Task1, r.Task2, r.Task3, r.Task4, r.Task5 }
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim())
                .ToArray();
            var criteriaFocus = AdminService.DeserializeCriteriaFocus(r.CriteriaFocusJson);
            return new
            {
                cardId = r.Id,
                professionId = r.ProfessionId,
                appliesToAllProfessions = r.ContentItemProfessionId == null,
                scenarioTitle = r.ScenarioTitle,
                setting = r.Setting,
                candidateRole = r.CandidateRole,
                interlocutorRole = r.InterlocutorRole,
                patientName = r.PatientName,
                patientAge = r.PatientAge,
                background = r.Background,
                tasks,
                allowedNotes = r.AllowedNotes,
                prepTimeSeconds = r.PrepTimeSeconds,
                rolePlayTimeSeconds = r.RolePlayTimeSeconds,
                patientEmotion = r.PatientEmotion,
                communicationGoal = r.CommunicationGoal,
                clinicalTopic = r.ClinicalTopic,
                difficulty = r.Difficulty,
                criteriaFocus,
                disclaimer = r.Disclaimer,
            };
        }).ToArray();

        return new
        {
            rolePlayCards = summaries,
            activeProfessionId = string.IsNullOrWhiteSpace(activeProfession) ? null : activeProfession,
        };
    }

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
        //
        // Plan P2.1 — load the user record so we can enforce the
        // profession filter alongside the existing status guard.
        LearnerUser? learner = null;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            learner = await EnsureLearnerProfileAsync(userId, ct);
        }

        // Pull the card (and only the card — no .Include for InterlocutorScript)
        // and the parent ContentItem.ProfessionId so we can detect the
        // universal-profession case. The ContentItem join is a left-join via
        // key equality; we never expose any of its other fields to the learner.
        var record = await (
            from card in db.RolePlayCards.AsNoTracking()
            join item in db.ContentItems.AsNoTracking()
                on card.ContentItemId equals item.Id into joined
            from item in joined.DefaultIfEmpty()
            where card.Id == cardId
            select new
            {
                Card = card,
                ContentItemProfessionId = item != null ? item.ProfessionId : null,
            })
            .FirstOrDefaultAsync(ct);

        if (record is null
            || (record.Card.Status != ContentStatus.Published
                && record.Card.Status != ContentStatus.Archived))
        {
            // Archived cards are still resolvable so a learner who has
            // started a session against a card that was later archived
            // can finish it. Drafts and in-review cards are NotFound
            // from the learner perspective.
            throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");
        }

        // Plan P2.1 — profession filter. Cards that don't match the
        // learner's active profession (and aren't universal) are
        // surfaced as 404 to avoid leaking the existence of content
        // intended for a different profession.
        var activeProfession = (learner?.ActiveProfessionId ?? string.Empty).Trim().ToLowerInvariant();
        var cardProfession = (record.Card.ProfessionId ?? string.Empty).Trim().ToLowerInvariant();
        var appliesToAllProfessions = record.ContentItemProfessionId is null;

        if (!appliesToAllProfessions
            && !string.IsNullOrEmpty(activeProfession)
            && !string.Equals(cardProfession, activeProfession, StringComparison.Ordinal))
        {
            throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");
        }

        var card = record.Card;
        var tasks = new[] { card.Task1, card.Task2, card.Task3, card.Task4, card.Task5 }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .ToArray();

        var criteriaFocus = AdminService.DeserializeCriteriaFocus(card.CriteriaFocusJson);

        return new
        {
            cardId = card.Id,
            professionId = card.ProfessionId,
            appliesToAllProfessions,
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
