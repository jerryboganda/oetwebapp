using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

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
}
