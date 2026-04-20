using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services;

/// <summary>
/// ============================================================================
/// VocabularyDraftService — admin AI-assisted vocabulary authoring
/// ============================================================================
///
/// MISSION CRITICAL. Every AI-drafted vocabulary term goes through this service.
/// Guarantees:
///   1. Grounded prompt via <see cref="IAiGatewayService"/> with
///      <see cref="RuleKind.Vocabulary"/> + <see cref="AiTaskMode.GenerateVocabularyTerm"/>.
///   2. Platform-only credential: <see cref="AiFeatureCodes.AdminVocabularyDraft"/>.
///   3. Validated output: every <c>appliedRuleIds</c> value must exist in the
///      loaded vocabulary rulebook. Invalid IDs are dropped; a draft with zero
///      valid rule IDs is rejected and replaced with a deterministic template.
///   4. Persistence: admins review drafts in the admin UI before acceptance.
///      Accepted drafts are inserted as <c>Status="draft"</c>; admins promote to
///      active via the normal update flow.
///   5. Audit: one <see cref="AuditEvent"/> per draft generation invocation.
/// ============================================================================
/// </summary>
public sealed class VocabularyDraftService(
    LearnerDbContext db,
    IRulebookLoader rulebookLoader,
    IAiGatewayService gateway,
    ILogger<VocabularyDraftService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public async Task<AdminVocabularyAiDraftResponse> GenerateAsync(
        AdminVocabularyAiDraftRequest request,
        string? adminId,
        string? adminName,
        string? authAccountId,
        CancellationToken ct)
    {
        if (request.Count <= 0 || request.Count > 25)
            throw ApiException.Validation("VOCAB_DRAFT_COUNT", "Count must be between 1 and 25.");
        if (string.IsNullOrWhiteSpace(request.Category))
            throw ApiException.Validation("VOCAB_DRAFT_CATEGORY", "Category is required.");

        var profession = ParseProfession(request.ProfessionId);
        OetRulebook rulebook;
        try
        {
            rulebook = rulebookLoader.Load(RuleKind.Vocabulary, profession);
        }
        catch (RulebookNotFoundException)
        {
            // Fall back to medicine rulebook for professions without a dedicated bank.
            rulebook = rulebookLoader.Load(RuleKind.Vocabulary, ExamProfession.Medicine);
        }
        var ruleIds = rulebook.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Vocabulary,
            Profession = profession,
            Task = AiTaskMode.GenerateVocabularyTerm,
        });

        var userMessage = BuildUserMessage(request, profession);

        string? warning = null;
        List<AdminVocabularyDraftTerm> drafts = new();

        try
        {
            var aiResult = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Model = "mock",
                Temperature = 0.4,
                FeatureCode = AiFeatureCodes.AdminVocabularyDraft,
                UserId = adminId,
                AuthAccountId = authAccountId,
            }, ct);

            drafts = ParseDrafts(aiResult.Completion, ruleIds, request.Category, request.Difficulty);
            if (drafts.Count == 0)
            {
                warning = "AI reply could not be parsed. Deterministic starter templates were used instead. Edit before publishing.";
            }
        }
        catch (PromptNotGroundedException pex)
        {
            logger.LogError(pex, "Vocabulary AI draft refused — ungrounded prompt.");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Vocabulary AI draft — provider error; using deterministic fallback.");
            warning = "AI provider error. Deterministic starter templates were used instead. Edit before publishing.";
        }

        if (drafts.Count == 0)
            drafts = BuildFallbackDrafts(request, rulebook);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId ?? "system",
            ActorName = adminName ?? "system",
            Action = warning is null ? "VocabularyAiDraftGenerated" : "VocabularyAiDraftFallback",
            ResourceType = "VocabularyTerm",
            ResourceId = $"vocab-draft-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Details = Truncate($"Vocabulary AI draft batch: count={request.Count} category={request.Category} profession={request.ProfessionId} rulebook=v{rulebook.Version}"
                + (warning is null ? "" : $" warning=\"{warning}\""), 1000),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return new AdminVocabularyAiDraftResponse(
            RulebookVersion: rulebook.Version,
            Drafts: drafts,
            Warning: warning);
    }

    public async Task<IReadOnlyList<string>> AcceptAsync(
        AdminVocabularyAiDraftAcceptRequest request,
        string adminId,
        string adminName,
        CancellationToken ct)
    {
        if (request.Drafts is null || request.Drafts.Count == 0)
            throw ApiException.Validation("VOCAB_DRAFT_EMPTY", "No drafts to accept.");

        var createdIds = new List<string>();
        foreach (var draft in request.Drafts)
        {
            if (string.IsNullOrWhiteSpace(draft.Term) || string.IsNullOrWhiteSpace(draft.Definition))
                continue;

            var dup = await db.VocabularyTerms.FirstOrDefaultAsync(
                t => t.Term == draft.Term
                  && t.ExamTypeCode == request.ExamTypeCode
                  && t.ProfessionId == request.ProfessionId,
                ct);
            if (dup is not null) continue;

            var id = $"VOC-{Guid.NewGuid():N}"[..12];
            db.VocabularyTerms.Add(new VocabularyTerm
            {
                Id = id,
                Term = draft.Term.Trim(),
                Definition = draft.Definition.Trim(),
                ExampleSentence = draft.ExampleSentence.Trim(),
                ContextNotes = string.IsNullOrWhiteSpace(draft.ContextNotes) ? null : draft.ContextNotes.Trim(),
                ExamTypeCode = request.ExamTypeCode,
                ProfessionId = request.ProfessionId,
                Category = draft.Category,
                Difficulty = draft.Difficulty,
                IpaPronunciation = draft.IpaPronunciation,
                SynonymsJson = JsonSerializer.Serialize(draft.Synonyms ?? Array.Empty<string>()),
                CollocationsJson = JsonSerializer.Serialize(draft.Collocations ?? Array.Empty<string>()),
                RelatedTermsJson = JsonSerializer.Serialize(draft.RelatedTerms ?? Array.Empty<string>()),
                SourceProvenance = request.SourceProvenance,
                Status = "draft",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            createdIds.Add(id);
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId,
            ActorName = adminName,
            Action = "VocabularyAiDraftAccepted",
            ResourceType = "VocabularyTerm",
            ResourceId = $"vocab-accept-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Details = Truncate($"Accepted {createdIds.Count} AI-drafted vocabulary term(s); provenance=\"{request.SourceProvenance}\"", 1000),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return createdIds;
    }

    // ── Parsing ───────────────────────────────────────────────────────────

    private static List<AdminVocabularyDraftTerm> ParseDrafts(
        string completion,
        HashSet<string> validRuleIds,
        string fallbackCategory,
        string? fallbackDifficulty)
    {
        if (string.IsNullOrWhiteSpace(completion)) return new();

        var jsonText = ExtractJsonBlock(completion);
        if (jsonText is null) return new();

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new();

            if (!root.TryGetProperty("terms", out var termsEl) || termsEl.ValueKind != JsonValueKind.Array)
                return new();

            var results = new List<AdminVocabularyDraftTerm>();
            foreach (var t in termsEl.EnumerateArray())
            {
                var term = SafeString(t, "term")?.Trim();
                var definition = SafeString(t, "definition")?.Trim();
                var example = SafeString(t, "exampleSentence")?.Trim();
                if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(example))
                    continue;

                var category = SafeString(t, "category")?.Trim() ?? fallbackCategory;
                var difficulty = NormaliseDifficulty(SafeString(t, "difficulty") ?? fallbackDifficulty);
                var ipa = SafeString(t, "ipaPronunciation");
                var context = SafeString(t, "contextNotes");
                var synonyms = ParseArrayOfStrings(t, "synonyms");
                var collocations = ParseArrayOfStrings(t, "collocations");
                var related = ParseArrayOfStrings(t, "relatedTerms");
                var applied = ParseArrayOfStrings(t, "appliedRuleIds")
                    .Where(id => validRuleIds.Contains(id))
                    .ToList();
                if (applied.Count == 0) continue;

                results.Add(new AdminVocabularyDraftTerm(
                    Term: term!,
                    Definition: definition!,
                    ExampleSentence: example!,
                    ContextNotes: context,
                    Category: category,
                    Difficulty: difficulty,
                    IpaPronunciation: ipa,
                    Synonyms: synonyms,
                    Collocations: collocations,
                    RelatedTerms: related,
                    AppliedRuleIds: applied));
            }
            return results;
        }
        catch (JsonException)
        {
            return new();
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

    private static List<string> ParseArrayOfStrings(JsonElement el, string property)
    {
        var result = new List<string>();
        if (!el.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in v.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s)) result.Add(s!.Trim());
        }
        return result;
    }

    private static string NormaliseDifficulty(string? raw)
    {
        var v = (raw ?? "medium").Trim().ToLowerInvariant();
        return v switch
        {
            "a2" => "easy",
            "b1" => "medium",
            "b2" => "medium",
            "c1" => "hard",
            "easy" or "medium" or "hard" => v,
            _ => "medium",
        };
    }

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        return Enum.TryParse<ExamProfession>(raw.Replace("-", ""), ignoreCase: true, out var p)
            ? p
            : ExamProfession.Medicine;
    }

    private static string BuildUserMessage(AdminVocabularyAiDraftRequest request, ExamProfession profession)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Admin has requested a batch of OET vocabulary terms with the following parameters.");
        sb.AppendLine();
        sb.AppendLine($"- Exam family: {request.ExamTypeCode}");
        sb.AppendLine($"- Profession: {profession}");
        sb.AppendLine($"- Category: {request.Category}");
        sb.AppendLine($"- Difficulty: {request.Difficulty ?? "medium"}");
        sb.AppendLine($"- Count: {request.Count}");
        if (!string.IsNullOrWhiteSpace(request.SeedPrompt))
        {
            sb.AppendLine();
            sb.AppendLine("Seed hint from admin:");
            sb.AppendLine(request.SeedPrompt.Trim());
        }
        sb.AppendLine();
        sb.AppendLine("Produce the JSON term batch strictly per the reply format above. Cite rule IDs from the vocabulary rulebook only.");
        return sb.ToString();
    }

    // ── Deterministic fallback ────────────────────────────────────────────

    private static List<AdminVocabularyDraftTerm> BuildFallbackDrafts(
        AdminVocabularyAiDraftRequest request,
        OetRulebook rulebook)
    {
        var anchorRuleIds = rulebook.Rules
            .OrderBy(r => r.Severity)
            .Select(r => r.Id)
            .Take(2)
            .ToList();
        if (anchorRuleIds.Count == 0) anchorRuleIds.Add("V01.1");

        var pool = SeedLexicon(request.Category).Take(Math.Max(1, request.Count)).ToList();
        return pool.Select(entry => new AdminVocabularyDraftTerm(
            Term: entry.Term,
            Definition: entry.Definition,
            ExampleSentence: entry.Example,
            ContextNotes: null,
            Category: request.Category,
            Difficulty: NormaliseDifficulty(request.Difficulty),
            IpaPronunciation: null,
            Synonyms: entry.Synonyms,
            Collocations: Array.Empty<string>(),
            RelatedTerms: Array.Empty<string>(),
            AppliedRuleIds: anchorRuleIds)).ToList();
    }

    private sealed record LexEntry(string Term, string Definition, string Example, IReadOnlyList<string> Synonyms);

    private static string Truncate(string raw, int max)
        => string.IsNullOrEmpty(raw) ? "" : (raw.Length <= max ? raw : raw[..max].TrimEnd() + "…");

    private static IEnumerable<LexEntry> SeedLexicon(string category)
    {
        // Small deterministic lexicon used only when the AI provider errors out.
        // Content is intentionally generic and flagged as fallback via
        // SourceProvenance when admins accept the batch.
        var medical = new[]
        {
            new LexEntry("dyspnoea", "Difficulty in breathing reported by the patient.", "She presented with dyspnoea on exertion for two weeks.", new[] { "shortness of breath" }),
            new LexEntry("anorexia", "Loss of appetite in a clinical setting.", "He reported anorexia and weight loss over the past month.", new[] { "loss of appetite" }),
            new LexEntry("palpitations", "An awareness of the heart beating irregularly, rapidly, or forcefully.", "She complained of palpitations after climbing stairs.", new[] { "rapid heartbeat" }),
            new LexEntry("lethargy", "A state of sluggishness or reduced energy.", "The child was noted to be lethargic on examination.", new[] { "tiredness" }),
            new LexEntry("oedema", "Accumulation of fluid in body tissues, causing swelling.", "Bilateral ankle oedema was evident on admission.", new[] { "swelling" }),
            new LexEntry("dysuria", "Painful or difficult urination.", "She reported dysuria and urinary frequency.", new[] { "painful urination" }),
            new LexEntry("hypertension", "Persistently elevated arterial blood pressure.", "He has a long-standing history of hypertension.", new[] { "high blood pressure" }),
            new LexEntry("tachycardia", "An elevated heart rate above the normal resting range.", "The patient developed tachycardia during the episode.", new[] { "fast heart rate" }),
        };
        var clinical = new[]
        {
            new LexEntry("counsel", "To provide professional guidance to a patient on their condition or treatment.", "She was counselled regarding the side effects of metformin.", new[] { "advise" }),
            new LexEntry("adhere", "To follow a treatment or management plan consistently.", "He has struggled to adhere to the antihypertensive regimen.", new[] { "comply" }),
            new LexEntry("reassure", "To offer comfort and confidence to a patient.", "She was reassured that the symptoms were self-limiting.", new[] { "comfort" }),
        };
        return category switch
        {
            "clinical_communication" => clinical,
            _ => medical,
        };
    }
}
