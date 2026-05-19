using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// ============================================================================
/// WritingDraftService — single entry-point for AI-assisted writing authoring
/// ============================================================================
///
/// Mirrors <see cref="OetLearner.Api.Services.Grammar.IGrammarDraftService"/>:
///
///   1. Grounded prompt: always calls <see cref="IAiGatewayService"/> with
///      <see cref="RuleKind.Writing"/> + <see cref="AiTaskMode.GenerateContent"/>.
///   2. Platform-only credential: feature code <see cref="AiFeatureCodes.AdminWritingDraft"/>.
///   3. Validated output: every <c>appliedRuleIds</c> value must exist in the
///      loaded writing rulebook for the requested profession. Unusable replies
///      fail closed rather than creating template content.
///   4. Persistence: only usable grounded AI output is inserted as
///      <see cref="ContentStatus.Draft"/>. Admin edits and publishes via the
///      existing content CMS flow.
///   5. Audit: one <see cref="AuditEvent"/> row per persisted draft.
/// ============================================================================
/// </summary>
public interface IWritingDraftService
{
    Task<WritingDraftResult> GenerateAsync(
        WritingDraftRequest request,
        string? adminId,
        string? adminName,
        string? authAccountId,
        CancellationToken ct);
}

public sealed record WritingDraftRequest(
    string Profession,
    string LetterType,
    string? RecipientSpecialty,
    string Prompt,
    string Difficulty,
    int TargetCaseNoteCount);

public sealed record WritingDraftResult(
    string ContentId,
    string Title,
    int CaseNoteCount,
    int ModelLetterWordCount,
    string RulebookVersion,
    IReadOnlyList<string> AppliedRuleIds,
    string? Warning);

public sealed class WritingDraftService(
    LearnerDbContext db,
    IRulebookLoader rulebookLoader,
    IAiGatewayService gateway,
    ILogger<WritingDraftService> logger) : IWritingDraftService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly HashSet<string> ValidLetterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "routine_referral",
        "urgent_referral",
        "discharge",
        "transfer",
        "non_medical_referral",
        "referral_to_gp",
    };

    public async Task<WritingDraftResult> GenerateAsync(
        WritingDraftRequest request,
        string? adminId,
        string? adminName,
        string? authAccountId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt is required.", nameof(request));

        var profession = ParseProfession(request.Profession);
        var letterType = NormaliseLetterType(request.LetterType);
        var difficulty = NormaliseDifficulty(request.Difficulty);

        var rulebook = rulebookLoader.Load(RuleKind.Writing, profession);
        var ruleIds = rulebook.Rules
            .Select(r => r.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Always build a grounded prompt — the gateway will physically refuse
        // anything that is not grounded.
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = profession,
            LetterType = letterType,
            Task = AiTaskMode.GenerateContent,
        });

        var userMessage = BuildUserMessage(request, letterType, difficulty);

        ParsedWritingDraft? parsed = null;

        try
        {
            var aiResult = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Temperature = 0.4,
                FeatureCode = AiFeatureCodes.AdminWritingDraft,
                UserId = adminId,
                AuthAccountId = authAccountId,
            }, ct);

            parsed = TryParseDraft(aiResult.Completion, ruleIds);
            if (parsed is null)
            {
                throw ApiException.ServiceUnavailable(
                    "WRITING_AI_DRAFT_UNUSABLE",
                    "Writing AI draft response was not usable. Please try again.");
            }
        }
        catch (PromptNotGroundedException pex)
        {
            // Structural invariant. Never silently fall through.
            logger.LogError(pex, "Writing AI draft refused — ungrounded prompt.");
            throw;
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing AI draft provider error.");
            throw ApiException.ServiceUnavailable(
                "WRITING_AI_DRAFT_UNAVAILABLE",
                "Writing AI draft generation is unavailable right now. Please try again.");
        }

        // Persist as ContentItem with Status = Draft.
        var contentId = $"ci-{Guid.NewGuid():N}";
        var detailJson = JsonSerializer.Serialize(new
        {
            generatedBy = "AI",
            sourceProvenance = $"Grounded AI draft (writing rulebook v{rulebook.Version}). appliedRuleIds={string.Join(",", parsed.AppliedRuleIds)}",
            profession = request.Profession,
            letterType = parsed.LetterType,
            recipientSpecialty = request.RecipientSpecialty,
            difficulty = parsed.Difficulty,
            caseNotes = parsed.CaseNotes,
            modelLetterMarkdown = parsed.ModelLetterMarkdown,
            estimatedWordCount = parsed.EstimatedWordCount,
            appliedRuleIds = parsed.AppliedRuleIds,
            rulebookVersion = rulebook.Version,
            adminPrompt = request.Prompt,
        }, JsonOpts);

        var modelAnswerJson = JsonSerializer.Serialize(new
        {
            modelLetterMarkdown = parsed.ModelLetterMarkdown,
            appliedRuleIds = parsed.AppliedRuleIds,
            estimatedWordCount = parsed.EstimatedWordCount,
        }, JsonOpts);

        var entity = new ContentItem
        {
            Id = contentId,
            ContentType = "practice_task",
            SubtestCode = "writing",
            ProfessionId = request.Profession,
            Title = parsed.Title,
            Difficulty = parsed.Difficulty,
            EstimatedDurationMinutes = 45,
            CriteriaFocusJson = "[]",
            ScenarioType = parsed.LetterType,
            ModeSupportJson = "[]",
            PublishedRevisionId = string.Empty,
            Status = ContentStatus.Draft,
            CaseNotes = parsed.CaseNotes,
            DetailJson = detailJson,
            ModelAnswerJson = modelAnswerJson,
            CreatedBy = adminId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ExamFamilyCode = "oet",
            ExamTypeCode = OetLearner.Api.Services.Common.ExamCodes.DefaultCode,
            SourceType = "ai_generated",
            QaStatus = "pending",
        };
        db.ContentItems.Add(entity);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId ?? "system",
            ActorName = adminName ?? "system",
            Action = "writing.ai_draft_generated",
            ResourceType = "ContentItem",
            ResourceId = contentId,
            Details = $"Writing AI draft created. Title=\"{parsed.Title}\" letterType={parsed.LetterType} profession={request.Profession} rulebook=v{rulebook.Version} appliedRuleIds=[{string.Join(",", parsed.AppliedRuleIds)}]",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        return new WritingDraftResult(
            ContentId: contentId,
            Title: parsed.Title,
            CaseNoteCount: CountCaseNoteLines(parsed.CaseNotes),
            ModelLetterWordCount: parsed.EstimatedWordCount,
            RulebookVersion: rulebook.Version,
            AppliedRuleIds: parsed.AppliedRuleIds,
            Warning: null);
    }

    // ---------------------------------------------------------------------
    // Parsing + validation
    // ---------------------------------------------------------------------

    private static string BuildUserMessage(WritingDraftRequest request, string letterType, string difficulty)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Admin has requested a Writing task draft (case notes + model letter) with the following parameters.");
        sb.AppendLine();
        sb.AppendLine($"- Profession: {request.Profession}");
        sb.AppendLine($"- Letter type: {letterType}");
        if (!string.IsNullOrWhiteSpace(request.RecipientSpecialty))
            sb.AppendLine($"- Recipient specialty hint: {request.RecipientSpecialty}");
        sb.AppendLine($"- Difficulty: {difficulty}");
        sb.AppendLine($"- Target case-note line count: {Math.Clamp(request.TargetCaseNoteCount, 8, 20)}");
        sb.AppendLine();
        sb.AppendLine("Admin scenario brief:");
        sb.AppendLine(request.Prompt.Trim());
        sb.AppendLine();
        sb.AppendLine("Reply with a SINGLE JSON object:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"title\": \"...\",");
        sb.AppendLine($"  \"letterType\": \"{letterType}\",");
        sb.AppendLine("  \"caseNotes\": \"## Today's visit\\n- ...\\n## History\\n- ...\",");
        sb.AppendLine("  \"modelLetterMarkdown\": \"Dr ...\\nCardiology Clinic\\n...\",");
        sb.AppendLine("  \"appliedRuleIds\": [\"R03.4\", \"R10.6\"],");
        sb.AppendLine($"  \"difficulty\": \"{difficulty}\",");
        sb.AppendLine("  \"estimatedWordCount\": 195");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine("Hard requirements: caseNotes and modelLetterMarkdown non-empty; every appliedRuleIds value MUST exist in the writing rulebook above. Never invent a rule ID.");
        return sb.ToString();
    }

    private static ParsedWritingDraft? TryParseDraft(string completion, HashSet<string> validRuleIds)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;

        var jsonText = ExtractJsonBlock(completion);
        if (jsonText is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var title = SafeString(root, "title")?.Trim();
            var letterType = NormaliseLetterType(SafeString(root, "letterType"));
            var caseNotes = SafeString(root, "caseNotes")?.Trim();
            var modelLetter = SafeString(root, "modelLetterMarkdown")?.Trim();
            var difficulty = NormaliseDifficulty(SafeString(root, "difficulty"));
            var estimatedWordCount = root.TryGetProperty("estimatedWordCount", out var ewc) && ewc.TryGetInt32(out var ewcv)
                ? ewcv
                : EstimateWordCount(modelLetter);

            if (string.IsNullOrWhiteSpace(title)) return null;
            if (string.IsNullOrWhiteSpace(caseNotes)) return null;
            if (string.IsNullOrWhiteSpace(modelLetter)) return null;

            var applied = new List<string>();
            if (root.TryGetProperty("appliedRuleIds", out var arEl) && arEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arEl.EnumerateArray())
                {
                    var id = el.GetString();
                    if (!string.IsNullOrWhiteSpace(id) && validRuleIds.Contains(id))
                        applied.Add(id!);
                }
            }
            if (applied.Count == 0) return null;

            return new ParsedWritingDraft(
                Title: title!,
                LetterType: letterType,
                CaseNotes: caseNotes!,
                ModelLetterMarkdown: modelLetter!,
                Difficulty: difficulty,
                EstimatedWordCount: estimatedWordCount,
                AppliedRuleIds: applied);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonBlock(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}")) return trimmed;
        var fenceStart = trimmed.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart < 0) fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart < 0) return null;
        var afterFence = trimmed.IndexOf('\n', fenceStart);
        if (afterFence < 0) return null;
        var closeFence = trimmed.IndexOf("```", afterFence + 1, StringComparison.Ordinal);
        if (closeFence < 0) return null;
        var inner = trimmed[(afterFence + 1)..closeFence].Trim();
        return inner.StartsWith("{") && inner.EndsWith("}") ? inner : null;
    }

    private static string? SafeString(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null,
        };
    }

    private static string NormaliseDifficulty(string? raw)
    {
        var v = (raw ?? "medium").Trim().ToLowerInvariant();
        return v switch
        {
            "easy" => "easy",
            "medium" => "medium",
            "hard" => "hard",
            _ => "medium",
        };
    }

    private static string NormaliseLetterType(string? raw)
    {
        var v = (raw ?? "routine_referral").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return ValidLetterTypes.Contains(v) ? v : "routine_referral";
    }

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        return Enum.TryParse<ExamProfession>(raw.Replace("-", ""), ignoreCase: true, out var p)
            ? p
            : ExamProfession.Medicine;
    }

    private static int EstimateWordCount(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : text!.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    private static int CountCaseNoteLines(string? caseNotes)
    {
        if (string.IsNullOrWhiteSpace(caseNotes)) return 0;
        var count = 0;
        foreach (var line in caseNotes.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) count++;
        }
        return count;
    }

    // ---------------------------------------------------------------------
    // Intermediate records
    // ---------------------------------------------------------------------

    private sealed record ParsedWritingDraft(
        string Title,
        string LetterType,
        string CaseNotes,
        string ModelLetterMarkdown,
        string Difficulty,
        int EstimatedWordCount,
        List<string> AppliedRuleIds);
}
