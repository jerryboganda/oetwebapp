using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services;

// Phase 1 (B.1) of the OET Speaking module roadmap.
//
// Admin CRUD for the typed `RolePlayCard` entity. Each card is paired 1:1
// with both an underlying `ContentItem` (so the existing publish/audit
// pipeline still applies) and an `InterlocutorScript` (the hidden patient
// persona that learners must never see).
//
// Publish gate (mirrors the discipline of
// `AdminService.SpeakingMockSets.PublishSpeakingMockSetAsync` and the
// content publish gate at `IContentPaperService.RequiredRolesFor`):
//   - The card must have a linked InterlocutorScript.
//   - At least 3 of Task1..Task5 must be non-empty (an OET role-play is
//     typically four-five tasks; three is the minimum that constitutes a
//     usable card).
//   - Background must be non-empty.
//   - The card must not already be archived.
//
// Permissions reuse the existing AdminContent grants: read for list/get,
// write for create/update/archive/duplicate, publish for the publish
// transition. Wired in `AdminSpeakingContentEndpoints.cs`.
public partial class AdminService
{
    public async Task<object> ListSpeakingRolePlayCardsAsync(
        string? professionId,
        string? difficulty,
        string? status,
        CancellationToken ct)
    {
        var q = db.RolePlayCards.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(professionId))
        {
            var normalised = professionId.Trim().ToLowerInvariant();
            q = q.Where(x => x.ProfessionId == normalised);
        }
        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            var normalised = difficulty.Trim().ToLowerInvariant();
            q = q.Where(x => x.Difficulty == normalised);
        }
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<ContentStatus>(status, ignoreCase: true, out var parsed))
        {
            q = q.Where(x => x.Status == parsed);
        }

        var rows = await q
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

        var cardIds = rows.Select(r => r.Id).ToArray();
        var scriptCardIds = await db.InterlocutorScripts.AsNoTracking()
            .Where(s => cardIds.Contains(s.RolePlayCardId))
            .Select(s => s.RolePlayCardId)
            .ToListAsync(ct);
        var hasScript = new HashSet<string>(scriptCardIds, StringComparer.Ordinal);

        return new
        {
            rolePlayCards = rows
                .Select(r => new AdminRolePlayCardSummary(
                    CardId: r.Id,
                    ContentItemId: r.ContentItemId,
                    ProfessionId: r.ProfessionId,
                    ScenarioTitle: r.ScenarioTitle,
                    Setting: r.Setting,
                    CandidateRole: r.CandidateRole,
                    InterlocutorRole: r.InterlocutorRole,
                    Difficulty: r.Difficulty,
                    Status: r.Status.ToString().ToLowerInvariant(),
                    HasInterlocutorScript: hasScript.Contains(r.Id),
                    IsLiveTutorEligible: r.IsLiveTutorEligible,
                    CreatedAt: r.CreatedAt,
                    UpdatedAt: r.UpdatedAt,
                    PublishedAt: r.PublishedAt,
                    ArchivedAt: r.ArchivedAt))
                .ToArray(),
        };
    }

    public async Task<AdminRolePlayCardDetail> GetSpeakingRolePlayCardAsync(
        string cardId,
        CancellationToken ct)
    {
        var card = await db.RolePlayCards.AsNoTracking()
            .Include(x => x.ContentItem)
            .FirstOrDefaultAsync(x => x.Id == cardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        var script = await db.InterlocutorScripts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RolePlayCardId == cardId, ct);

        return ProjectCardDetail(card, script);
    }

    public async Task<AdminRolePlayCardDetail> CreateSpeakingRolePlayCardAsync(
        string adminId,
        string adminName,
        AdminRolePlayCardCreateRequest req,
        CancellationToken ct)
    {
        ValidateCreateRequest(req);

        var now = DateTimeOffset.UtcNow;
        var profession = NormaliseProfession(req.ProfessionId);
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        var cardId = $"rpc-{Guid.NewGuid():N}";

        var content = new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = profession,
            Title = req.ScenarioTitle.Trim(),
            Difficulty = NormaliseDifficulty(req.Difficulty),
            EstimatedDurationMinutes = ComputeEstimatedMinutes(req.PrepTimeSeconds, req.RolePlayTimeSeconds),
            CriteriaFocusJson = SerializeCriteriaFocus(req.CriteriaFocus),
            PublishedRevisionId = $"{contentItemId}-r1",
            Status = ContentStatus.Draft,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now,
            SourceType = "manual",
            QaStatus = "approved",
        };

        var card = new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ProfessionId = profession,
            ScenarioTitle = req.ScenarioTitle.Trim(),
            Setting = req.Setting.Trim(),
            CandidateRole = req.CandidateRole.Trim(),
            InterlocutorRole = string.IsNullOrWhiteSpace(req.InterlocutorRole)
                ? "Patient"
                : req.InterlocutorRole.Trim(),
            PatientName = req.PatientName?.Trim(),
            PatientAge = req.PatientAge?.Trim(),
            Background = req.Background?.Trim() ?? string.Empty,
            Task1 = req.Task1?.Trim(),
            Task2 = req.Task2?.Trim(),
            Task3 = req.Task3?.Trim(),
            Task4 = req.Task4?.Trim(),
            Task5 = req.Task5?.Trim(),
            AllowedNotes = req.AllowedNotes ?? true,
            PrepTimeSeconds = req.PrepTimeSeconds ?? 180,
            RolePlayTimeSeconds = req.RolePlayTimeSeconds ?? 300,
            PatientEmotion = string.IsNullOrWhiteSpace(req.PatientEmotion)
                ? "neutral"
                : req.PatientEmotion.Trim(),
            CommunicationGoal = string.IsNullOrWhiteSpace(req.CommunicationGoal)
                ? "Inform"
                : req.CommunicationGoal.Trim(),
            ClinicalTopic = string.IsNullOrWhiteSpace(req.ClinicalTopic)
                ? "general"
                : req.ClinicalTopic.Trim(),
            Difficulty = NormaliseDifficulty(req.Difficulty),
            CriteriaFocusJson = SerializeCriteriaFocus(req.CriteriaFocus),
            Disclaimer = string.IsNullOrWhiteSpace(req.Disclaimer)
                ? "Practice estimate only. This is not an official OET score or result."
                : req.Disclaimer.Trim(),
            Status = ContentStatus.Draft,
            IsLiveTutorEligible = req.IsLiveTutorEligible ?? false,
            CreatedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.ContentItems.Add(content);
        db.RolePlayCards.Add(card);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "RolePlayCard", cardId,
            $"Created role-play card: {card.ScenarioTitle}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectCardDetail(card, interlocutorScript: null);
    }

    public async Task<AdminRolePlayCardDetail> UpdateSpeakingRolePlayCardAsync(
        string adminId,
        string adminName,
        string cardId,
        AdminRolePlayCardUpdateRequest req,
        CancellationToken ct)
    {
        var card = await db.RolePlayCards.FirstOrDefaultAsync(x => x.Id == cardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        if (card.Status == ContentStatus.Archived)
        {
            throw ApiException.Conflict("role_play_card_archived",
                "Archived role-play cards are read-only.");
        }

        if (req.ProfessionId is not null)
        {
            if (string.IsNullOrWhiteSpace(req.ProfessionId))
            {
                throw ApiException.Validation("ROLE_PLAY_CARD_PROFESSION_REQUIRED",
                    "Profession id cannot be blank.");
            }
            card.ProfessionId = NormaliseProfession(req.ProfessionId);
        }
        if (req.ScenarioTitle is not null)
        {
            if (string.IsNullOrWhiteSpace(req.ScenarioTitle))
            {
                throw ApiException.Validation("ROLE_PLAY_CARD_TITLE_REQUIRED",
                    "Scenario title cannot be blank.");
            }
            card.ScenarioTitle = req.ScenarioTitle.Trim();
        }
        if (req.Setting is not null) card.Setting = req.Setting.Trim();
        if (req.CandidateRole is not null) card.CandidateRole = req.CandidateRole.Trim();
        if (req.InterlocutorRole is not null)
        {
            card.InterlocutorRole = string.IsNullOrWhiteSpace(req.InterlocutorRole)
                ? "Patient"
                : req.InterlocutorRole.Trim();
        }
        if (req.PatientName is not null) card.PatientName = string.IsNullOrWhiteSpace(req.PatientName) ? null : req.PatientName.Trim();
        if (req.PatientAge is not null) card.PatientAge = string.IsNullOrWhiteSpace(req.PatientAge) ? null : req.PatientAge.Trim();
        if (req.Background is not null) card.Background = req.Background.Trim();
        if (req.Task1 is not null) card.Task1 = string.IsNullOrWhiteSpace(req.Task1) ? null : req.Task1.Trim();
        if (req.Task2 is not null) card.Task2 = string.IsNullOrWhiteSpace(req.Task2) ? null : req.Task2.Trim();
        if (req.Task3 is not null) card.Task3 = string.IsNullOrWhiteSpace(req.Task3) ? null : req.Task3.Trim();
        if (req.Task4 is not null) card.Task4 = string.IsNullOrWhiteSpace(req.Task4) ? null : req.Task4.Trim();
        if (req.Task5 is not null) card.Task5 = string.IsNullOrWhiteSpace(req.Task5) ? null : req.Task5.Trim();
        if (req.AllowedNotes.HasValue) card.AllowedNotes = req.AllowedNotes.Value;
        if (req.PrepTimeSeconds.HasValue) card.PrepTimeSeconds = Math.Max(0, req.PrepTimeSeconds.Value);
        if (req.RolePlayTimeSeconds.HasValue) card.RolePlayTimeSeconds = Math.Max(0, req.RolePlayTimeSeconds.Value);
        if (req.PatientEmotion is not null && !string.IsNullOrWhiteSpace(req.PatientEmotion))
            card.PatientEmotion = req.PatientEmotion.Trim();
        if (req.CommunicationGoal is not null && !string.IsNullOrWhiteSpace(req.CommunicationGoal))
            card.CommunicationGoal = req.CommunicationGoal.Trim();
        if (req.ClinicalTopic is not null && !string.IsNullOrWhiteSpace(req.ClinicalTopic))
            card.ClinicalTopic = req.ClinicalTopic.Trim();
        if (req.Difficulty is not null) card.Difficulty = NormaliseDifficulty(req.Difficulty);
        if (req.CriteriaFocus is not null) card.CriteriaFocusJson = SerializeCriteriaFocus(req.CriteriaFocus);
        if (req.Disclaimer is not null && !string.IsNullOrWhiteSpace(req.Disclaimer))
            card.Disclaimer = req.Disclaimer.Trim();
        if (req.IsLiveTutorEligible.HasValue) card.IsLiveTutorEligible = req.IsLiveTutorEligible.Value;

        card.UpdatedAt = DateTimeOffset.UtcNow;

        // Keep the underlying ContentItem title/profession in sync so the
        // existing admin/content surfaces continue to render a useful row
        // for this card.
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == card.ContentItemId, ct);
        if (content is not null)
        {
            content.Title = card.ScenarioTitle;
            content.ProfessionId = card.ProfessionId;
            content.Difficulty = card.Difficulty;
            content.CriteriaFocusJson = card.CriteriaFocusJson;
            content.UpdatedAt = card.UpdatedAt;
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "RolePlayCard", cardId,
            $"Updated role-play card: {card.ScenarioTitle}", ct);
        await CommitIfOwnedAsync(tx, ct);

        var script = await db.InterlocutorScripts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RolePlayCardId == cardId, ct);
        return ProjectCardDetail(card, script);
    }

    public async Task<AdminRolePlayCardDetail> PublishSpeakingRolePlayCardAsync(
        string adminId,
        string adminName,
        string cardId,
        CancellationToken ct)
    {
        var card = await db.RolePlayCards.FirstOrDefaultAsync(x => x.Id == cardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        if (card.Status == ContentStatus.Archived)
        {
            throw ApiException.Conflict("role_play_card_archived",
                "Archived role-play cards cannot be published.");
        }

        var script = await db.InterlocutorScripts
            .FirstOrDefaultAsync(x => x.RolePlayCardId == cardId, ct);
        if (script is null)
        {
            throw ApiException.Conflict("role_play_card_missing_interlocutor",
                "This card cannot be published until an interlocutor script has been authored for it.");
        }

        var taskCount = CountNonEmptyTasks(card);
        if (taskCount < 3)
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_INSUFFICIENT_TASKS",
                $"A publishable role-play card must include at least three task bullets ({taskCount} provided).");
        }
        if (string.IsNullOrWhiteSpace(card.Background))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_BACKGROUND_REQUIRED",
                "Background must be filled in before a role-play card can be published.");
        }

        var now = DateTimeOffset.UtcNow;
        card.Status = ContentStatus.Published;
        card.PublishedAt = now;
        card.UpdatedAt = now;

        // Publish the underlying ContentItem so legacy speaking surfaces
        // that read from `ContentItem.Status == Published` keep working.
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == card.ContentItemId, ct);
        if (content is not null)
        {
            content.Status = ContentStatus.Published;
            content.PublishedAt = now;
            content.UpdatedAt = now;
            if (string.IsNullOrWhiteSpace(content.PublishedRevisionId))
            {
                content.PublishedRevisionId = $"{content.Id}-r1";
            }
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Published", "RolePlayCard", cardId,
            $"Published role-play card: {card.ScenarioTitle}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectCardDetail(card, script);
    }

    public async Task<AdminRolePlayCardDetail> ArchiveSpeakingRolePlayCardAsync(
        string adminId,
        string adminName,
        string cardId,
        CancellationToken ct)
    {
        var card = await db.RolePlayCards.FirstOrDefaultAsync(x => x.Id == cardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        if (card.Status != ContentStatus.Archived)
        {
            var now = DateTimeOffset.UtcNow;
            card.Status = ContentStatus.Archived;
            card.ArchivedAt = now;
            card.UpdatedAt = now;

            var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == card.ContentItemId, ct);
            if (content is not null)
            {
                content.Status = ContentStatus.Archived;
                content.ArchivedAt = now;
                content.UpdatedAt = now;
            }

            await using var tx = await BeginTransactionIfNeededAsync(ct);
            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, "Archived", "RolePlayCard", cardId,
                $"Archived role-play card: {card.ScenarioTitle}", ct);
            await CommitIfOwnedAsync(tx, ct);
        }

        var script = await db.InterlocutorScripts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RolePlayCardId == cardId, ct);
        return ProjectCardDetail(card, script);
    }

    public async Task<AdminRolePlayCardDetail> DuplicateSpeakingRolePlayCardAsync(
        string adminId,
        string adminName,
        string cardId,
        CancellationToken ct)
    {
        var source = await db.RolePlayCards.AsNoTracking()
            .Include(x => x.ContentItem)
            .FirstOrDefaultAsync(x => x.Id == cardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        var sourceScript = await db.InterlocutorScripts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RolePlayCardId == cardId, ct);

        var now = DateTimeOffset.UtcNow;
        var newContentId = $"ci-{Guid.NewGuid():N}";
        var newCardId = $"rpc-{Guid.NewGuid():N}";

        var clonedContent = new ContentItem
        {
            Id = newContentId,
            ContentType = source.ContentItem?.ContentType ?? "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = source.ContentItem?.ProfessionId ?? source.ProfessionId,
            Title = $"{source.ScenarioTitle} (Copy)",
            Difficulty = source.Difficulty,
            EstimatedDurationMinutes = source.ContentItem?.EstimatedDurationMinutes
                ?? ComputeEstimatedMinutes(source.PrepTimeSeconds, source.RolePlayTimeSeconds),
            CriteriaFocusJson = source.CriteriaFocusJson,
            PublishedRevisionId = $"{newContentId}-r1",
            Status = ContentStatus.Draft,
            DetailJson = source.ContentItem?.DetailJson ?? "{}",
            ModelAnswerJson = source.ContentItem?.ModelAnswerJson ?? "{}",
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now,
            SourceType = source.ContentItem?.SourceType ?? "manual",
            QaStatus = "approved",
        };

        var clonedCard = new RolePlayCard
        {
            Id = newCardId,
            ContentItemId = newContentId,
            ProfessionId = source.ProfessionId,
            ScenarioTitle = $"{source.ScenarioTitle} (Copy)",
            Setting = source.Setting,
            CandidateRole = source.CandidateRole,
            InterlocutorRole = source.InterlocutorRole,
            PatientName = source.PatientName,
            PatientAge = source.PatientAge,
            Background = source.Background,
            Task1 = source.Task1,
            Task2 = source.Task2,
            Task3 = source.Task3,
            Task4 = source.Task4,
            Task5 = source.Task5,
            AllowedNotes = source.AllowedNotes,
            PrepTimeSeconds = source.PrepTimeSeconds,
            RolePlayTimeSeconds = source.RolePlayTimeSeconds,
            PatientEmotion = source.PatientEmotion,
            CommunicationGoal = source.CommunicationGoal,
            ClinicalTopic = source.ClinicalTopic,
            Difficulty = source.Difficulty,
            CriteriaFocusJson = source.CriteriaFocusJson,
            Disclaimer = source.Disclaimer,
            Status = ContentStatus.Draft,
            IsLiveTutorEligible = source.IsLiveTutorEligible,
            CreatedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        InterlocutorScript? clonedScript = null;
        if (sourceScript is not null)
        {
            clonedScript = new InterlocutorScript
            {
                Id = $"is-{Guid.NewGuid():N}",
                RolePlayCardId = newCardId,
                OpeningResponse = sourceScript.OpeningResponse,
                Prompt1 = sourceScript.Prompt1,
                Prompt2 = sourceScript.Prompt2,
                Prompt3 = sourceScript.Prompt3,
                HiddenInformation = sourceScript.HiddenInformation,
                ResistanceLevel = sourceScript.ResistanceLevel,
                ClosingCue = sourceScript.ClosingCue,
                EmotionalState = sourceScript.EmotionalState,
                ProfessionRoleNotes = sourceScript.ProfessionRoleNotes,
                LayLanguageTriggersJson = sourceScript.LayLanguageTriggersJson,
                CreatedByUserId = adminId,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.ContentItems.Add(clonedContent);
        db.RolePlayCards.Add(clonedCard);
        if (clonedScript is not null) db.InterlocutorScripts.Add(clonedScript);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Duplicated", "RolePlayCard", newCardId,
            $"Duplicated role-play card from {cardId}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectCardDetail(clonedCard, clonedScript);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void ValidateCreateRequest(AdminRolePlayCardCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ProfessionId))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_PROFESSION_REQUIRED",
                "Profession id is required.");
        }
        if (string.IsNullOrWhiteSpace(req.ScenarioTitle))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_TITLE_REQUIRED",
                "Scenario title is required.");
        }
        if (string.IsNullOrWhiteSpace(req.Setting))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_SETTING_REQUIRED",
                "Setting is required.");
        }
        if (string.IsNullOrWhiteSpace(req.CandidateRole))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_CANDIDATE_ROLE_REQUIRED",
                "Candidate role is required.");
        }
        if (string.IsNullOrWhiteSpace(req.PatientEmotion))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_EMOTION_REQUIRED",
                "Patient emotion is required.");
        }
        if (string.IsNullOrWhiteSpace(req.CommunicationGoal))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_GOAL_REQUIRED",
                "Communication goal is required.");
        }
        if (string.IsNullOrWhiteSpace(req.ClinicalTopic))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_TOPIC_REQUIRED",
                "Clinical topic is required.");
        }
    }

    private static int CountNonEmptyTasks(RolePlayCard card)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(card.Task1)) count++;
        if (!string.IsNullOrWhiteSpace(card.Task2)) count++;
        if (!string.IsNullOrWhiteSpace(card.Task3)) count++;
        if (!string.IsNullOrWhiteSpace(card.Task4)) count++;
        if (!string.IsNullOrWhiteSpace(card.Task5)) count++;
        return count;
    }

    private static string NormaliseProfession(string raw)
        => raw.Trim().ToLowerInvariant();

    private static string NormaliseDifficulty(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "core";
        var v = raw.Trim().ToLowerInvariant();
        return v switch
        {
            "core" or "extension" or "exam" => v,
            _ => "core",
        };
    }

    private static int ComputeEstimatedMinutes(int? prep, int? roleplay)
    {
        var total = (prep ?? 180) + (roleplay ?? 300);
        return Math.Max(1, (int)Math.Ceiling(total / 60.0));
    }

    private static string SerializeCriteriaFocus(string[]? codes)
    {
        if (codes is null || codes.Length == 0) return "[]";
        var cleaned = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return JsonSerializer.Serialize(cleaned);
    }

    internal static string[] DeserializeCriteriaFocus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    internal static AdminRolePlayCardDetail ProjectCardDetail(
        RolePlayCard card,
        InterlocutorScript? interlocutorScript)
    {
        var tasks = new[] { card.Task1, card.Task2, card.Task3, card.Task4, card.Task5 };
        return new AdminRolePlayCardDetail(
            CardId: card.Id,
            ContentItemId: card.ContentItemId,
            ProfessionId: card.ProfessionId,
            ScenarioTitle: card.ScenarioTitle,
            Setting: card.Setting,
            CandidateRole: card.CandidateRole,
            InterlocutorRole: card.InterlocutorRole,
            PatientName: card.PatientName,
            PatientAge: card.PatientAge,
            Background: card.Background,
            Tasks: tasks,
            AllowedNotes: card.AllowedNotes,
            PrepTimeSeconds: card.PrepTimeSeconds,
            RolePlayTimeSeconds: card.RolePlayTimeSeconds,
            PatientEmotion: card.PatientEmotion,
            CommunicationGoal: card.CommunicationGoal,
            ClinicalTopic: card.ClinicalTopic,
            Difficulty: card.Difficulty,
            CriteriaFocus: DeserializeCriteriaFocus(card.CriteriaFocusJson),
            Disclaimer: card.Disclaimer,
            Status: card.Status.ToString().ToLowerInvariant(),
            IsLiveTutorEligible: card.IsLiveTutorEligible,
            CreatedByUserId: card.CreatedByUserId,
            CreatedAt: card.CreatedAt,
            UpdatedAt: card.UpdatedAt,
            PublishedAt: card.PublishedAt,
            ArchivedAt: card.ArchivedAt,
            InterlocutorScript: interlocutorScript is null
                ? null
                : ProjectInterlocutorScript(interlocutorScript));
    }

    internal static AdminInterlocutorScriptDetail ProjectInterlocutorScript(InterlocutorScript script)
        => new(
            ScriptId: script.Id,
            RolePlayCardId: script.RolePlayCardId,
            OpeningResponse: script.OpeningResponse,
            Prompt1: script.Prompt1,
            Prompt2: script.Prompt2,
            Prompt3: script.Prompt3,
            HiddenInformation: script.HiddenInformation,
            ResistanceLevel: ResistanceLevels.ToCode(script.ResistanceLevel),
            ClosingCue: script.ClosingCue,
            EmotionalState: script.EmotionalState,
            ProfessionRoleNotes: script.ProfessionRoleNotes,
            LayLanguageTriggers: DeserializeStringArray(script.LayLanguageTriggersJson),
            CreatedByUserId: script.CreatedByUserId,
            CreatedAt: script.CreatedAt,
            UpdatedAt: script.UpdatedAt);

    internal static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Phase 11 (G.11) — AI-assisted authoring
    // ════════════════════════════════════════════════════════════════════════
    //
    // `AiDraftRolePlayCardAsync` is the single-shot draft path the admin
    // "AI draft" page calls (and the batch worker calls one card at a
    // time). The flow:
    //
    //   1. Validate the request (profession + emotion + difficulty).
    //   2. Build a grounded prompt via the canonical gateway with
    //      `RuleKind.Speaking + AiTaskMode.GenerateContent`.
    //   3. Compose a user message describing the desired card shape and
    //      requesting a strict JSON payload that carries both the
    //      candidate card and the paired interlocutor script.
    //   4. Call the gateway with `AiFeatureCodes.AdminContentGeneration`.
    //   5. Parse the JSON, sanity-check it, and run the originality guard
    //      against all currently published cards. Reject duplicates.
    //   6. Persist the draft (card + interlocutor script) inside a single
    //      transaction; audit-log the action.
    //
    // The gateway is passed in by the endpoint to avoid expanding the
    // `AdminService` primary constructor (which would ripple through every
    // partial). If the gateway returns an unparseable payload, the method
    // falls back to a deterministic starter template and surfaces a
    // `Warning` so the admin knows to edit before publishing.

    public async Task<AdminRolePlayCardAiDraftResponse> AiDraftRolePlayCardAsync(
        IAiGatewayService gateway,
        string adminId,
        string adminName,
        AdminRolePlayCardAiDraftRequest request,
        CancellationToken ct)
    {
        if (gateway is null) throw new ArgumentNullException(nameof(gateway));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ProfessionId))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_PROFESSION_REQUIRED",
                "Profession id is required.");
        }

        var profession = NormaliseProfession(request.ProfessionId);
        var topic = string.IsNullOrWhiteSpace(request.Topic) ? "general clinical communication" : request.Topic.Trim();
        var emotion = string.IsNullOrWhiteSpace(request.Emotion) ? "worried" : request.Emotion.Trim().ToLowerInvariant();
        var difficulty = NormaliseDifficulty(request.Difficulty);
        var setting = string.IsNullOrWhiteSpace(request.Setting) ? null : request.Setting.Trim();
        var candidateRole = string.IsNullOrWhiteSpace(request.CandidateRole) ? GuessCandidateRole(profession) : request.CandidateRole.Trim();
        var interlocutorRole = string.IsNullOrWhiteSpace(request.InterlocutorRole) ? "Patient" : request.InterlocutorRole.Trim();
        var communicationGoal = string.IsNullOrWhiteSpace(request.CommunicationGoal) ? "Inform" : request.CommunicationGoal.Trim();

        var examProfession = ParseProfessionToExam(profession);
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = examProfession,
            Task = AiTaskMode.GenerateContent,
            CardType = "role_play",
        });

        var userInput = BuildCardDraftUserMessage(profession, topic, emotion, difficulty, candidateRole, interlocutorRole, communicationGoal, setting);

        AiGatewayResult? aiResult = null;
        ParsedCardDraft? parsed = null;
        string? warning = null;
        try
        {
            aiResult = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userInput,
                Temperature = 0.4,
                MaxTokens = 4096,
                FeatureCode = AiFeatureCodes.AdminContentGeneration,
                UserId = adminId,
                PromptTemplateId = "card.draft.v1",
            }, ct);
            parsed = TryParseCardDraft(aiResult.Completion);
            if (parsed is null)
            {
                warning = "AI reply could not be parsed. A deterministic starter template was used instead. Edit before publishing.";
            }
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (Exception)
        {
            warning = "AI provider error. A deterministic starter template was used instead. Edit before publishing.";
        }

        parsed ??= BuildFallbackCardDraft(profession, topic, emotion, difficulty, candidateRole, interlocutorRole, communicationGoal, setting);

        // Originality guard — refuse if title+background closely resemble any
        // already-published card. Uses normalised Levenshtein on the
        // concatenation; threshold 0.85.
        var combined = $"{parsed.ScenarioTitle} {parsed.Background}";
        var collision = await FindNearestPublishedCardAsync(combined, threshold: 0.85, ct);
        if (collision is not null)
        {
            throw ApiException.Conflict("role_play_card_too_similar",
                $"AI draft is >85% similar to an existing published card ('{collision.ScenarioTitle}', {collision.Id}). Try a different topic or emotion combination.");
        }

        // Persist the draft.
        var now = DateTimeOffset.UtcNow;
        var contentItemId = $"ci-{Guid.NewGuid():N}";
        var cardId = $"rpc-{Guid.NewGuid():N}";

        var content = new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = profession,
            Title = parsed.ScenarioTitle,
            Difficulty = difficulty,
            EstimatedDurationMinutes = ComputeEstimatedMinutes(parsed.PrepTimeSeconds, parsed.RolePlayTimeSeconds),
            CriteriaFocusJson = SerializeCriteriaFocus(parsed.CriteriaFocus),
            PublishedRevisionId = $"{contentItemId}-r1",
            Status = ContentStatus.Draft,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now,
            SourceType = "ai_draft",
            SourceProvenance = warning is null ? "AI draft via card.draft.v1" : $"AI draft with template fallback ({warning})",
            RightsStatus = "owned",
            QaStatus = "pending",
            FreshnessConfidence = "current",
            InstructionLanguage = "en",
            ContentLanguage = "en",
        };

        var card = new RolePlayCard
        {
            Id = cardId,
            ContentItemId = contentItemId,
            ProfessionId = profession,
            ScenarioTitle = parsed.ScenarioTitle,
            Setting = parsed.Setting,
            CandidateRole = parsed.CandidateRole,
            InterlocutorRole = parsed.InterlocutorRole,
            PatientName = parsed.PatientName,
            PatientAge = parsed.PatientAge,
            Background = parsed.Background,
            Task1 = parsed.Tasks.ElementAtOrDefault(0),
            Task2 = parsed.Tasks.ElementAtOrDefault(1),
            Task3 = parsed.Tasks.ElementAtOrDefault(2),
            Task4 = parsed.Tasks.ElementAtOrDefault(3),
            Task5 = parsed.Tasks.ElementAtOrDefault(4),
            AllowedNotes = true,
            PrepTimeSeconds = parsed.PrepTimeSeconds,
            RolePlayTimeSeconds = parsed.RolePlayTimeSeconds,
            PatientEmotion = parsed.PatientEmotion,
            CommunicationGoal = parsed.CommunicationGoal,
            ClinicalTopic = parsed.ClinicalTopic,
            Difficulty = difficulty,
            CriteriaFocusJson = SerializeCriteriaFocus(parsed.CriteriaFocus),
            Disclaimer = "Practice estimate only. This is not an official OET score or result.",
            Status = ContentStatus.Draft,
            IsLiveTutorEligible = false,
            CreatedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var script = new InterlocutorScript
        {
            Id = $"is-{Guid.NewGuid():N}",
            RolePlayCardId = cardId,
            OpeningResponse = parsed.OpeningResponse,
            Prompt1 = parsed.Prompts.ElementAtOrDefault(0),
            Prompt2 = parsed.Prompts.ElementAtOrDefault(1),
            Prompt3 = parsed.Prompts.ElementAtOrDefault(2),
            HiddenInformation = parsed.HiddenInformation,
            ResistanceLevel = ResistanceLevels.Parse(parsed.ResistanceLevel),
            ClosingCue = parsed.ClosingCue,
            EmotionalState = parsed.EmotionalState,
            ProfessionRoleNotes = parsed.ProfessionRoleNotes,
            LayLanguageTriggersJson = JsonSerializer.Serialize(parsed.LayLanguageTriggers),
            CreatedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.ContentItems.Add(content);
        db.RolePlayCards.Add(card);
        db.InterlocutorScripts.Add(script);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName,
            warning is null ? "AiDrafted" : "AiDraftedWithFallback",
            "RolePlayCard", cardId,
            $"AI-drafted role-play card: {card.ScenarioTitle} ({profession})"
            + (warning is null ? "" : $" — warning: {warning}"),
            ct);
        await CommitIfOwnedAsync(tx, ct);

        return new AdminRolePlayCardAiDraftResponse(
            CardId: cardId,
            Card: ProjectCardDetail(card, script),
            Warning: warning);
    }

    // ── Batch generation request entity CRUD ────────────────────────────────

    public async Task<AdminRolePlayCardBatchSummary> EnqueueRolePlayCardBatchAsync(
        string adminId,
        string adminName,
        AdminRolePlayCardBatchRequest request,
        CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ProfessionId))
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_BATCH_PROFESSION_REQUIRED",
                "Profession id is required.");
        }
        if (request.Count < 1 || request.Count > 50)
        {
            throw ApiException.Validation("ROLE_PLAY_CARD_BATCH_COUNT_INVALID",
                "Count must be between 1 and 50.");
        }

        // Idempotency: if the same key already exists, return the existing
        // row instead of creating a duplicate.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await db.SpeakingCardBatchRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, ct);
            if (existing is not null)
            {
                return ProjectBatchSummary(existing);
            }
        }

        var topicJson = JsonSerializer.Serialize(request.TopicList ?? Array.Empty<string>());
        var distributionDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (request.DifficultyDistribution is not null)
        {
            foreach (var bucket in request.DifficultyDistribution)
            {
                if (bucket is null || string.IsNullOrWhiteSpace(bucket.Difficulty)) continue;
                if (bucket.Count <= 0) continue;
                distributionDict[NormaliseDifficulty(bucket.Difficulty)] = bucket.Count;
            }
        }
        var distributionJson = JsonSerializer.Serialize(distributionDict);

        var row = new SpeakingCardBatchRequest
        {
            Id = $"sbr-{Guid.NewGuid():N}",
            ProfessionId = NormaliseProfession(request.ProfessionId),
            Count = request.Count,
            GeneratedCount = 0,
            TopicListJson = topicJson,
            DifficultyDistributionJson = distributionJson,
            Status = SpeakingCardBatchRequestStatus.Pending,
            RequestedByAdminId = adminId,
            RequestedByAdminName = adminName,
            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.SpeakingCardBatchRequests.Add(row);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "EnqueuedRolePlayCardBatch",
            "SpeakingCardBatchRequest", row.Id,
            $"Enqueued batch: {row.Count} cards for profession {row.ProfessionId}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectBatchSummary(row);
    }

    internal static AdminRolePlayCardBatchSummary ProjectBatchSummary(SpeakingCardBatchRequest row)
        => new(
            BatchId: row.Id,
            ProfessionId: row.ProfessionId,
            Count: row.Count,
            GeneratedCount: row.GeneratedCount,
            Status: row.Status.ToString().ToLowerInvariant(),
            RequestedByAdminId: row.RequestedByAdminId,
            RequestedByAdminName: row.RequestedByAdminName,
            Error: row.Error,
            CreatedAt: row.CreatedAt,
            StartedAt: row.StartedAt,
            CompletedAt: row.CompletedAt);

    // ── Originality guard ──────────────────────────────────────────────────

    internal async Task<RolePlayCard?> FindNearestPublishedCardAsync(
        string candidateCombined,
        double threshold,
        CancellationToken ct)
    {
        var normalisedCandidate = NormaliseForSimilarity(candidateCombined);
        if (string.IsNullOrWhiteSpace(normalisedCandidate)) return null;

        // Pull published cards. Cap to 500 (latest first) — beyond that the
        // Levenshtein cost dominates and a duplicate would have been caught
        // by exact-match search anyway.
        var published = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Status == ContentStatus.Published)
            .OrderByDescending(c => c.PublishedAt ?? c.UpdatedAt)
            .Take(500)
            .Select(c => new { c.Id, c.ScenarioTitle, c.Background })
            .ToListAsync(ct);

        foreach (var p in published)
        {
            var other = NormaliseForSimilarity($"{p.ScenarioTitle} {p.Background}");
            if (string.IsNullOrWhiteSpace(other)) continue;
            var similarity = LevenshteinSimilarity(normalisedCandidate, other);
            if (similarity > threshold)
            {
                return new RolePlayCard
                {
                    Id = p.Id,
                    ScenarioTitle = p.ScenarioTitle,
                    Background = p.Background,
                };
            }
        }
        return null;
    }

    private static string NormaliseForSimilarity(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    /// <summary>Length-normalised Levenshtein similarity in the range
    /// [0, 1]. Strings are compared up to their first 1024 characters to
    /// keep the matrix cost bounded.</summary>
    internal static double LevenshteinSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;
        const int Max = 1024;
        if (a.Length > Max) a = a.Substring(0, Max);
        if (b.Length > Max) b = b.Substring(0, Max);
        var distance = LevenshteinDistance(a, b);
        var longer = Math.Max(a.Length, b.Length);
        return longer == 0 ? 1.0 : 1.0 - (double)distance / longer;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;
        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[m];
    }

    // ── Card draft prompt + parser ─────────────────────────────────────────

    private static string BuildCardDraftUserMessage(
        string profession,
        string topic,
        string emotion,
        string difficulty,
        string candidateRole,
        string interlocutorRole,
        string communicationGoal,
        string? setting)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are drafting an original OET Speaking role-play card. Produce a strict JSON object — no markdown, no commentary. The shape MUST be:");
        sb.AppendLine("{");
        sb.AppendLine("  \"scenarioTitle\": string,");
        sb.AppendLine("  \"setting\": string,");
        sb.AppendLine("  \"candidateRole\": string,");
        sb.AppendLine("  \"interlocutorRole\": string,");
        sb.AppendLine("  \"patientName\": string,");
        sb.AppendLine("  \"patientAge\": string,");
        sb.AppendLine("  \"background\": string,        // 2-4 sentence case background");
        sb.AppendLine("  \"tasks\": [string, string, string, string, string],   // 4-5 task bullets");
        sb.AppendLine("  \"prepTimeSeconds\": 180,");
        sb.AppendLine("  \"rolePlayTimeSeconds\": 300,");
        sb.AppendLine("  \"patientEmotion\": string,");
        sb.AppendLine("  \"communicationGoal\": string,");
        sb.AppendLine("  \"clinicalTopic\": string,");
        sb.AppendLine("  \"criteriaFocus\": [string, string, string],            // 2-3 of the 9 OET criteria codes");
        sb.AppendLine("  \"interlocutorScript\": {");
        sb.AppendLine("    \"openingResponse\": string,                          // exact first line the patient says");
        sb.AppendLine("    \"prompts\": [string, string, string],                // 2-3 cue prompts during the role-play");
        sb.AppendLine("    \"hiddenInformation\": string,                        // 2-3 sentences of facts NOT printed on the candidate card");
        sb.AppendLine("    \"resistanceLevel\": \"low\"|\"medium\"|\"high\",");
        sb.AppendLine("    \"closingCue\": string,                               // how the role-play ends if the candidate handles the case well");
        sb.AppendLine("    \"emotionalState\": string,                           // 1-sentence description of the affect");
        sb.AppendLine("    \"layLanguageTriggers\": [string, string, string]    // 3-4 jargon terms the candidate must rephrase");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Constraints:");
        sb.AppendLine($"- Profession: {profession}");
        sb.AppendLine($"- Clinical topic / scenario seed: {topic}");
        sb.AppendLine($"- Patient emotion: {emotion}");
        sb.AppendLine($"- Difficulty: {difficulty} (core | extension | exam)");
        sb.AppendLine($"- Candidate role: {candidateRole}");
        sb.AppendLine($"- Interlocutor role: {interlocutorRole}");
        sb.AppendLine($"- Communication goal: {communicationGoal}");
        if (!string.IsNullOrWhiteSpace(setting))
        {
            sb.AppendLine($"- Setting: {setting}");
        }
        sb.AppendLine("- The card must be ORIGINAL. Do NOT copy any real OET sample card. Vary surnames, exact ages, and clinical specifics.");
        sb.AppendLine("- Use the same OET Speaking style: 4-5 short task bullets that name the candidate's responsibilities.");
        sb.AppendLine("- Criteria focus codes MUST be drawn from: intelligibility, fluency, appropriateness, grammarExpression, relationshipBuilding, patientPerspective, structure, informationGathering, informationGiving.");
        sb.AppendLine("- The interlocutor script MUST stay hidden from the candidate at runtime.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY the JSON object.");
        return sb.ToString();
    }

    private sealed record ParsedCardDraft(
        string ScenarioTitle,
        string Setting,
        string CandidateRole,
        string InterlocutorRole,
        string? PatientName,
        string? PatientAge,
        string Background,
        string[] Tasks,
        int PrepTimeSeconds,
        int RolePlayTimeSeconds,
        string PatientEmotion,
        string CommunicationGoal,
        string ClinicalTopic,
        string[] CriteriaFocus,
        string OpeningResponse,
        string[] Prompts,
        string HiddenInformation,
        string ResistanceLevel,
        string ClosingCue,
        string EmotionalState,
        string? ProfessionRoleNotes,
        string[] LayLanguageTriggers);

    private static readonly HashSet<string> AllowedCriteriaCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "intelligibility","fluency","appropriateness","grammarExpression",
        "relationshipBuilding","patientPerspective","structure",
        "informationGathering","informationGiving",
    };

    private static ParsedCardDraft? TryParseCardDraft(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var jsonText = ExtractJsonBlock(completion);
        if (jsonText is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var title = SafeString(root, "scenarioTitle");
            var setting = SafeString(root, "setting");
            var candidateRole = SafeString(root, "candidateRole");
            var interlocutorRole = SafeString(root, "interlocutorRole");
            var patientName = SafeString(root, "patientName");
            var patientAge = SafeString(root, "patientAge");
            var background = SafeString(root, "background");
            var tasks = ReadStringArray(root, "tasks").Take(5).ToArray();
            var prepTime = ReadInt(root, "prepTimeSeconds", 180);
            var rolePlayTime = ReadInt(root, "rolePlayTimeSeconds", 300);
            var emotion = SafeString(root, "patientEmotion") ?? "worried";
            var goal = SafeString(root, "communicationGoal") ?? "Inform";
            var clinical = SafeString(root, "clinicalTopic") ?? "general";
            var criteria = ReadStringArray(root, "criteriaFocus")
                .Where(c => AllowedCriteriaCodes.Contains(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4).ToArray();

            if (string.IsNullOrWhiteSpace(title)) return null;
            if (string.IsNullOrWhiteSpace(setting)) return null;
            if (string.IsNullOrWhiteSpace(candidateRole)) return null;
            if (string.IsNullOrWhiteSpace(background)) return null;
            if (tasks.Length < 3) return null;
            if (criteria.Length < 1) return null;

            string opening = "";
            string[] prompts = Array.Empty<string>();
            string hidden = "";
            string resistance = "low";
            string closing = "";
            string emotionalState = "";
            string? professionRoleNotes = null;
            string[] layTriggers = Array.Empty<string>();
            if (root.TryGetProperty("interlocutorScript", out var iEl) && iEl.ValueKind == JsonValueKind.Object)
            {
                opening = SafeString(iEl, "openingResponse") ?? "";
                prompts = ReadStringArray(iEl, "prompts").Take(3).ToArray();
                hidden = SafeString(iEl, "hiddenInformation") ?? "";
                resistance = (SafeString(iEl, "resistanceLevel") ?? "low").Trim().ToLowerInvariant();
                closing = SafeString(iEl, "closingCue") ?? "";
                emotionalState = SafeString(iEl, "emotionalState") ?? emotion;
                professionRoleNotes = SafeString(iEl, "professionRoleNotes");
                layTriggers = ReadStringArray(iEl, "layLanguageTriggers").Take(6).ToArray();
            }
            if (string.IsNullOrWhiteSpace(opening)) return null;

            return new ParsedCardDraft(
                ScenarioTitle: title!.Trim(),
                Setting: setting!.Trim(),
                CandidateRole: candidateRole!.Trim(),
                InterlocutorRole: string.IsNullOrWhiteSpace(interlocutorRole) ? "Patient" : interlocutorRole!.Trim(),
                PatientName: patientName,
                PatientAge: patientAge,
                Background: background!.Trim(),
                Tasks: tasks,
                PrepTimeSeconds: prepTime,
                RolePlayTimeSeconds: rolePlayTime,
                PatientEmotion: emotion.Trim().ToLowerInvariant(),
                CommunicationGoal: goal.Trim(),
                ClinicalTopic: clinical.Trim(),
                CriteriaFocus: criteria,
                OpeningResponse: opening.Trim(),
                Prompts: prompts,
                HiddenInformation: hidden.Trim(),
                ResistanceLevel: resistance is "low" or "medium" or "high" ? resistance : "low",
                ClosingCue: closing.Trim(),
                EmotionalState: emotionalState.Trim(),
                ProfessionRoleNotes: professionRoleNotes,
                LayLanguageTriggers: layTriggers);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ParsedCardDraft BuildFallbackCardDraft(
        string profession,
        string topic,
        string emotion,
        string difficulty,
        string candidateRole,
        string interlocutorRole,
        string communicationGoal,
        string? setting)
    {
        var name = profession switch
        {
            "nursing" => "Mr Jordan Reyes",
            "medicine" => "Mrs Anya Patel",
            "pharmacy" => "Mr Leon Park",
            "physiotherapy" => "Ms Imani Cole",
            _ => "Mr Sam Carter",
        };
        var settingResolved = setting ?? profession switch
        {
            "nursing" => "Surgical ward, mid-morning",
            "medicine" => "GP clinic, scheduled appointment",
            "pharmacy" => "Community pharmacy consultation room",
            "physiotherapy" => "Outpatient physiotherapy clinic",
            _ => "Outpatient clinic",
        };
        var tasks = new[]
        {
            $"Find out how the {interlocutorRole.ToLowerInvariant()} is feeling and check their main concerns.",
            $"Explain the relevant aspects of {topic} in plain, jargon-free language.",
            $"Acknowledge the {emotion} feelings and work through any worries.",
            "Negotiate a practical plan that the patient is willing to follow.",
            "Confirm understanding and arrange follow-up.",
        };
        var prompts = new[]
        {
            "I'm not really sure I understand — could you explain that another way?",
            "What if it doesn't work? I have heard of people having problems with this.",
            "I'd rather not bother my family with this. Can we keep it between us?",
        };
        return new ParsedCardDraft(
            ScenarioTitle: $"Drafting topic: {topic}",
            Setting: settingResolved,
            CandidateRole: candidateRole,
            InterlocutorRole: interlocutorRole,
            PatientName: name,
            PatientAge: "52",
            Background: $"You are the {candidateRole.ToLowerInvariant()} on shift. The {interlocutorRole.ToLowerInvariant()} attends today regarding {topic}. Recent notes suggest some additional context that you will need to clarify in the consultation.",
            Tasks: tasks,
            PrepTimeSeconds: 180,
            RolePlayTimeSeconds: 300,
            PatientEmotion: emotion,
            CommunicationGoal: communicationGoal,
            ClinicalTopic: topic,
            CriteriaFocus: new[] { "informationGiving", "patientPerspective", "relationshipBuilding" },
            OpeningResponse: "I hope this won't take too long — I have a lot on my mind today.",
            Prompts: prompts,
            HiddenInformation: $"The patient has a personal reason to be {emotion} that they will only reveal if directly asked with empathy. Accept advice once that concern is acknowledged.",
            ResistanceLevel: difficulty == "exam" ? "high" : "medium",
            ClosingCue: "Accepts the plan once their concern is heard and a clear next step is agreed.",
            EmotionalState: $"{emotion} but trying to stay polite",
            ProfessionRoleNotes: null,
            LayLanguageTriggers: new[] { "diagnosis", "treatment", "adherence", "prognosis" });
    }

    // ── JSON helpers (private to this file) ────────────────────────────────

    private static string? ExtractJsonBlock(string completion)
    {
        var firstBrace = completion.IndexOf('{');
        var lastBrace = completion.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace) return null;
        return completion.Substring(firstBrace, lastBrace - firstBrace + 1);
    }

    private static string? SafeString(JsonElement root, params string[] propNames)
    {
        foreach (var prop in propNames)
        {
            if (!root.TryGetProperty(prop, out var el)) continue;
            if (el.ValueKind == JsonValueKind.String) return el.GetString();
            if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
        }
        return null;
    }

    private static string[] ReadStringArray(JsonElement root, string propName)
    {
        if (!root.TryGetProperty(propName, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
            }
        }
        return list.ToArray();
    }

    private static int ReadInt(JsonElement root, string propName, int fallback)
    {
        if (root.TryGetProperty(propName, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v))
        {
            return v;
        }
        return fallback;
    }

    private static string GuessCandidateRole(string profession) => profession switch
    {
        "nursing" => "Nurse",
        "medicine" => "Doctor",
        "pharmacy" => "Pharmacist",
        "physiotherapy" => "Physiotherapist",
        "dentistry" => "Dentist",
        "occupational_therapy" => "Occupational therapist",
        "radiography" => "Radiographer",
        "optometry" => "Optometrist",
        "podiatry" => "Podiatrist",
        "speech_pathology" => "Speech pathologist",
        "dietetics" => "Dietitian",
        "veterinary_science" => "Veterinarian",
        _ => "Health professional",
    };

    private static ExamProfession ParseProfessionToExam(string profession) => profession switch
    {
        "nursing" => ExamProfession.Nursing,
        "medicine" or "doctor" => ExamProfession.Medicine,
        "pharmacy" => ExamProfession.Pharmacy,
        "physiotherapy" => ExamProfession.Physiotherapy,
        "dentistry" => ExamProfession.Dentistry,
        "veterinary_science" => ExamProfession.Veterinary,
        _ => ExamProfession.Medicine,
    };
}
