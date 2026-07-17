using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services;

// Phase 1 (B.1) of the OET Speaking module roadmap.
//
// Admin upsert + read for the hidden `InterlocutorScript` row that is
// paired 1:1 with a `RolePlayCard`. The interlocutor card MUST NEVER be
// emitted by any learner-facing endpoint — only this admin partial and
// the tutor live-room cue panel (Phase 3) read it.
//
// Mirrors the conventions used by `AdminService.SpeakingRolePlayCards.cs`
// (audit-logged + transactional via the shared `AdminService` helpers).
// Wired into the route surface from `AdminSpeakingContentEndpoints.cs`.
public partial class AdminService
{
    public async Task<AdminInterlocutorScriptDetail?> GetInterlocutorScriptAsync(
        string cardId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_ID_REQUIRED",
                "Role-play card id is required.");
        }

        var script = await db.InterlocutorScripts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RolePlayCardId == cardId, ct);
        return script is null ? null : ProjectInterlocutorScript(script);
    }

    public async Task<AdminInterlocutorScriptDetail> UpsertInterlocutorScriptAsync(
        string adminId,
        string adminName,
        string cardId,
        AdminInterlocutorScriptUpsertRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_ID_REQUIRED",
                "Role-play card id is required.");
        }
        if (req is null)
        {
            throw ApiException.Validation("INTERLOCUTOR_SCRIPT_BODY_REQUIRED",
                "Interlocutor script body is required.");
        }
        if (string.IsNullOrWhiteSpace(req.OpeningResponse))
        {
            throw ApiException.Validation("INTERLOCUTOR_SCRIPT_OPENING_REQUIRED",
                "Opening response is required so the AI patient knows how to start.");
        }

        // Ensure the parent card exists so we never create an orphan
        // script. We deliberately do NOT include the script eagerly here
        // because the card detail projection guards leakage by reloading
        // the script via an explicit query — but we still need a server
        // side check that the FK points at something real.
        var card = await db.RolePlayCards.FirstOrDefaultAsync(x => x.Id == cardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        if (card.Status == ContentStatus.Archived)
        {
            throw ApiException.Conflict("role_play_card_archived",
                "Archived role-play cards cannot have their interlocutor script edited.");
        }

        var now = DateTimeOffset.UtcNow;
        var resistance = ResistanceLevels.Parse(req.ResistanceLevel);
        var layLanguageJson = JsonSerializer.Serialize(req.LayLanguageTriggers ?? Array.Empty<string>());

        var script = await db.InterlocutorScripts
            .FirstOrDefaultAsync(x => x.RolePlayCardId == cardId, ct);

        var isCreate = script is null;
        if (script is null)
        {
            script = new InterlocutorScript
            {
                Id = $"is-{Guid.NewGuid():N}",
                RolePlayCardId = cardId,
                CreatedByUserId = adminId,
                CreatedAt = now,
            };
            db.InterlocutorScripts.Add(script);
        }

        script.OpeningResponse = req.OpeningResponse.Trim();
        script.Prompt1 = string.IsNullOrWhiteSpace(req.Prompt1) ? null : req.Prompt1.Trim();
        script.Prompt2 = string.IsNullOrWhiteSpace(req.Prompt2) ? null : req.Prompt2.Trim();
        script.Prompt3 = string.IsNullOrWhiteSpace(req.Prompt3) ? null : req.Prompt3.Trim();
        script.HiddenInformation = req.HiddenInformation?.Trim() ?? string.Empty;
        script.ResistanceLevel = resistance;
        script.ClosingCue = req.ClosingCue?.Trim() ?? string.Empty;
        script.EmotionalState = req.EmotionalState?.Trim() ?? string.Empty;
        script.ProfessionRoleNotes = string.IsNullOrWhiteSpace(req.ProfessionRoleNotes)
            ? null
            : req.ProfessionRoleNotes.Trim();
        script.LayLanguageTriggersJson = layLanguageJson;
        // Speaking module rebuild (2026-06-11) — printed roleplayer card face.
        script.PatientBackground = req.PatientBackground?.Trim() ?? string.Empty;
        script.PatientTask1 = string.IsNullOrWhiteSpace(req.PatientTask1) ? null : req.PatientTask1.Trim();
        script.PatientTask2 = string.IsNullOrWhiteSpace(req.PatientTask2) ? null : req.PatientTask2.Trim();
        script.PatientTask3 = string.IsNullOrWhiteSpace(req.PatientTask3) ? null : req.PatientTask3.Trim();
        script.PatientTask4 = string.IsNullOrWhiteSpace(req.PatientTask4) ? null : req.PatientTask4.Trim();
        script.PatientTask5 = string.IsNullOrWhiteSpace(req.PatientTask5) ? null : req.PatientTask5.Trim();
        script.UpdatedAt = now;

        // Touch the parent card so list views resort by latest activity.
        card.UpdatedAt = now;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(
            adminId,
            adminName,
            isCreate ? "Created" : "Updated",
            "InterlocutorScript",
            script.Id,
            $"{(isCreate ? "Created" : "Updated")} interlocutor script for card {cardId}",
            ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectInterlocutorScript(script);
    }
}
