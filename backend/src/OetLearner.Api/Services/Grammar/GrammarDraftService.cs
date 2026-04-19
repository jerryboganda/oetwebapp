using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// ============================================================================
/// GrammarDraftService — single entry-point for AI-assisted grammar authoring
/// ============================================================================
///
/// MISSION CRITICAL. Every grammar lesson draft produced with AI assistance
/// goes through this service. Guarantees:
///
///   1. Grounded prompt: always calls <see cref="IAiGatewayService"/> with
///      <see cref="RuleKind.Grammar"/> + <see cref="AiTaskMode.GenerateGrammarLesson"/>.
///   2. Platform-only credential: feature code <see cref="AiFeatureCodes.AdminGrammarDraft"/>.
///   3. Validated output: every <c>appliedRuleIds</c> value must exist in the
///      loaded grammar rulebook. Unknown rule IDs are dropped; if fewer than
///      two remain, the draft is rejected and a deterministic fallback template
///      is used instead (warning surfaced to the admin).
///   4. Persistence: always inserted as <c>status = "draft"</c>. Admin edits
///      and publishes via the existing admin flow.
///   5. Audit: one <see cref="AuditEvent"/> row per draft, regardless of
///      whether AI succeeded or the template was used.
/// ============================================================================
/// </summary>
public interface IGrammarDraftService
{
    Task<GrammarDraftResult> GenerateAsync(
        GrammarDraftRequest request,
        string? adminId,
        string? adminName,
        string? authAccountId,
        CancellationToken ct);
}

public sealed record GrammarDraftRequest(
    string ExamTypeCode,
    string? TopicSlug,
    string Prompt,
    string Level,
    int TargetExerciseCount,
    string? Profession);

public sealed record GrammarDraftResult(
    string LessonId,
    string Title,
    int ContentBlockCount,
    int ExerciseCount,
    string RulebookVersion,
    IReadOnlyList<string> AppliedRuleIds,
    string? Warning);

public sealed class GrammarDraftService(
    LearnerDbContext db,
    IRulebookLoader rulebookLoader,
    IAiGatewayService gateway,
    ILogger<GrammarDraftService> logger) : IGrammarDraftService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<GrammarDraftResult> GenerateAsync(
        GrammarDraftRequest request,
        string? adminId,
        string? adminName,
        string? authAccountId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt is required.", nameof(request));

        var profession = ParseProfession(request.Profession);
        var rulebook = rulebookLoader.Load(RuleKind.Grammar, profession);
        var ruleIds = rulebook.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Always build a grounded prompt — the gateway will physically refuse
        // anything that is not grounded.
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Grammar,
            Profession = profession,
            Task = AiTaskMode.GenerateGrammarLesson,
        });

        var userMessage = BuildUserMessage(request);

        AiGatewayResult? aiResult = null;
        ParsedLessonDraft? parsed = null;
        string? warning = null;

        try
        {
            aiResult = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Model = "mock",
                Temperature = 0.3,
                FeatureCode = AiFeatureCodes.AdminGrammarDraft,
                UserId = adminId,
                AuthAccountId = authAccountId,
            }, ct);

            parsed = TryParseLesson(aiResult.Completion, ruleIds);
            if (parsed is null)
            {
                warning = "AI reply could not be parsed as a grammar lesson. A deterministic starter template was used instead. Edit before publishing.";
            }
        }
        catch (PromptNotGroundedException pex)
        {
            // Structural invariant. Never silently fall through.
            logger.LogError(pex, "Grammar AI draft refused — ungrounded prompt.");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Grammar AI draft — provider error; using deterministic fallback.");
            warning = "AI provider error. A deterministic starter template was used instead. Edit before publishing.";
        }

        parsed ??= BuildFallbackDraft(request, rulebook);

        // Persist
        var lessonId = $"GRM-{Guid.NewGuid():N}"[..12];
        var docJson = JsonSerializer.Serialize(new
        {
            topicId = parsed.TopicSlug,
            category = parsed.TopicSlug,
            sourceProvenance = warning is null
                ? $"Grounded AI draft (grammar rulebook v{rulebook.Version}). appliedRuleIds={string.Join(",", parsed.AppliedRuleIds)}"
                : $"Starter template fallback ({warning}). Rulebook v{rulebook.Version}.",
            prerequisiteLessonIds = Array.Empty<string>(),
            contentBlocks = parsed.ContentBlocks,
            exercises = parsed.Exercises,
            version = 1,
            updatedAt = DateTimeOffset.UtcNow.ToString("O"),
            appliedRuleIds = parsed.AppliedRuleIds,
        }, JsonOpts);

        var entity = new GrammarLesson
        {
            Id = lessonId,
            Title = parsed.Title,
            ExamTypeCode = request.ExamTypeCode,
            Category = parsed.TopicSlug,
            Description = parsed.Description ?? $"Auto-drafted grammar lesson on {parsed.TopicSlug.Replace('_', ' ')}.",
            ContentHtml = docJson,
            ExercisesJson = JsonSerializer.Serialize(parsed.Exercises, JsonOpts),
            Level = parsed.Level,
            EstimatedMinutes = parsed.EstimatedMinutes,
            SortOrder = 0,
            Status = "draft",
        };
        db.GrammarLessons.Add(entity);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId ?? "system",
            ActorName = adminName ?? "system",
            Action = warning is null ? "GrammarAiDraftCreated" : "GrammarAiDraftFallback",
            ResourceType = "GrammarLesson",
            ResourceId = lessonId,
            Details = $"Grammar lesson AI draft created. Title=\"{parsed.Title}\" rulebook=v{rulebook.Version} appliedRuleIds=[{string.Join(",", parsed.AppliedRuleIds)}]" + (warning is null ? "" : $" warning=\"{warning}\""),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        return new GrammarDraftResult(
            LessonId: lessonId,
            Title: parsed.Title,
            ContentBlockCount: parsed.ContentBlocks.Count,
            ExerciseCount: parsed.Exercises.Count,
            RulebookVersion: rulebook.Version,
            AppliedRuleIds: parsed.AppliedRuleIds,
            Warning: warning);
    }

    // ---------------------------------------------------------------------
    // Parsing + validation
    // ---------------------------------------------------------------------

    private static string BuildUserMessage(GrammarDraftRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Admin has requested a grammar lesson draft with the following parameters.");
        sb.AppendLine();
        sb.AppendLine($"- Exam family: {request.ExamTypeCode}");
        if (!string.IsNullOrWhiteSpace(request.TopicSlug))
            sb.AppendLine($"- Target topic slug: {request.TopicSlug}");
        sb.AppendLine($"- Level: {request.Level}");
        sb.AppendLine($"- Target exercise count: {request.TargetExerciseCount} (3–12)");
        sb.AppendLine();
        sb.AppendLine("Admin prompt:");
        sb.AppendLine(request.Prompt.Trim());
        sb.AppendLine();
        sb.AppendLine("Produce the JSON lesson strictly per the reply format above. Cite rule IDs from the grammar rulebook only.");
        return sb.ToString();
    }

    private static ParsedLessonDraft? TryParseLesson(string completion, HashSet<string> validRuleIds)
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
            var topicSlug = Slugify(SafeString(root, "topicSlug") ?? "grammar_topic");
            var level = NormaliseLevel(SafeString(root, "level"));
            var estimated = root.TryGetProperty("estimatedMinutes", out var em) && em.TryGetInt32(out var emv) ? emv : 12;

            var contentBlocks = new List<LessonContentBlock>();
            if (root.TryGetProperty("contentBlocks", out var cbEl) && cbEl.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var cb in cbEl.EnumerateArray())
                {
                    var md = SafeString(cb, "contentMarkdown")?.Trim();
                    if (string.IsNullOrWhiteSpace(md)) continue;
                    var type = SafeString(cb, "type") ?? "prose";
                    contentBlocks.Add(new LessonContentBlock(
                        Id: $"cb-{++i}",
                        SortOrder: i,
                        Type: type,
                        ContentMarkdown: md,
                        Content: null));
                }
            }

            var exercises = new List<LessonExercise>();
            if (root.TryGetProperty("exercises", out var exEl) && exEl.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var ex in exEl.EnumerateArray())
                {
                    var parsed = ParseExercise(ex, validRuleIds, ++i);
                    if (parsed is not null) exercises.Add(parsed);
                }
            }

            var lessonAppliedRuleIds = new List<string>();
            if (root.TryGetProperty("appliedRuleIds", out var arEl) && arEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arEl.EnumerateArray())
                {
                    var id = el.GetString();
                    if (!string.IsNullOrWhiteSpace(id) && validRuleIds.Contains(id)) lessonAppliedRuleIds.Add(id);
                }
            }

            // Union of lesson appliedRuleIds and each exercise's appliedRuleIds
            foreach (var exercise in exercises)
                foreach (var id in exercise.AppliedRuleIds)
                    if (!lessonAppliedRuleIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                        lessonAppliedRuleIds.Add(id);

            if (string.IsNullOrWhiteSpace(title)) return null;
            if (contentBlocks.Count == 0) return null;
            if (exercises.Count < 3) return null;
            if (lessonAppliedRuleIds.Count == 0) return null;

            return new ParsedLessonDraft(
                Title: title!,
                TopicSlug: topicSlug,
                Level: level,
                EstimatedMinutes: estimated,
                Description: null,
                ContentBlocks: contentBlocks,
                Exercises: exercises,
                AppliedRuleIds: lessonAppliedRuleIds);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LessonExercise? ParseExercise(JsonElement ex, HashSet<string> validRuleIds, int sortOrder)
    {
        var type = SafeString(ex, "type") ?? "mcq";
        var prompt = SafeString(ex, "promptMarkdown")?.Trim();
        var explanation = SafeString(ex, "explanationMarkdown")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt)) return null;
        if (string.IsNullOrWhiteSpace(explanation)) return null;

        var difficulty = NormaliseLevel(SafeString(ex, "difficulty"));
        var points = ex.TryGetProperty("points", out var pts) && pts.TryGetInt32(out var pv) ? pv : 1;

        object options = ex.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array
            ? opts.Clone()
            : Array.Empty<object>();

        object? correctAnswer = ex.TryGetProperty("correctAnswer", out var ca) ? (object)ca.Clone() : null;

        var accepted = new List<string>();
        if (ex.TryGetProperty("acceptedAnswers", out var aaEl) && aaEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in aaEl.EnumerateArray())
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) accepted.Add(s);
            }
        }

        var applied = new List<string>();
        if (ex.TryGetProperty("appliedRuleIds", out var arEl) && arEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arEl.EnumerateArray())
            {
                var id = el.GetString();
                if (!string.IsNullOrWhiteSpace(id) && validRuleIds.Contains(id)) applied.Add(id);
            }
        }

        if (applied.Count == 0) return null;

        return new LessonExercise(
            Id: $"ex-{sortOrder}",
            SortOrder: sortOrder,
            Type: type,
            PromptMarkdown: prompt!,
            Options: options,
            CorrectAnswer: correctAnswer,
            AcceptedAnswers: accepted,
            ExplanationMarkdown: explanation,
            Difficulty: difficulty,
            Points: points,
            AppliedRuleIds: applied);
    }

    private static string? ExtractJsonBlock(string raw)
    {
        // Accept plain JSON or fenced ```json ... ``` blocks.
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

    private static string NormaliseLevel(string? raw)
    {
        var v = (raw ?? "intermediate").Trim().ToLowerInvariant();
        return v switch
        {
            "beginner" => "beginner",
            "intermediate" => "intermediate",
            "advanced" => "advanced",
            _ => "intermediate",
        };
    }

    private static string Slugify(string raw)
    {
        var v = (raw ?? "grammar_topic").Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in v)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('_');
        }
        var slug = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(slug) ? "grammar_topic" : slug;
    }

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        return Enum.TryParse<ExamProfession>(raw.Replace("-", ""), ignoreCase: true, out var p)
            ? p
            : ExamProfession.Medicine;
    }

    // ---------------------------------------------------------------------
    // Fallback template — deterministic starter lesson grounded in the rulebook
    // ---------------------------------------------------------------------

    private static ParsedLessonDraft BuildFallbackDraft(GrammarDraftRequest request, OetRulebook rulebook)
    {
        var slug = Slugify(request.TopicSlug ?? DeriveSlug(request.Prompt));
        var topicLabel = TitleCase(slug.Replace('_', ' '));
        var level = NormaliseLevel(request.Level);

        // Pick up to 3 rules from the rulebook to anchor the fallback lesson.
        // Bias towards critical + major severities.
        var anchorRules = rulebook.Rules
            .OrderBy(r => r.Severity switch
            {
                RuleSeverity.Critical => 0,
                RuleSeverity.Major => 1,
                RuleSeverity.Minor => 2,
                _ => 3,
            })
            .Take(3)
            .ToList();

        var appliedIds = anchorRules.Select(r => r.Id).ToList();

        var contentBlocks = new List<LessonContentBlock>
        {
            new("cb-1", 1, "callout", $"Focus on **{topicLabel.ToLowerInvariant()}** patterns in a clinical context.", null),
            new("cb-2", 2, "example", anchorRules.Count > 0
                ? $"Example (cite {anchorRules[0].Id}): {(anchorRules[0].ExemplarPhrases is { Count: > 0 } ? anchorRules[0].ExemplarPhrases![0] : "The patient was reviewed by the team.")}"
                : "Example: The patient was reviewed by the team.",
                null),
            new("cb-3", 3, "note", "Watch tense choice, article use, and formal register.", null),
        };

        var targetCount = Math.Clamp(request.TargetExerciseCount, 3, 12);
        var exercises = new List<LessonExercise>();
        var types = new[] { "mcq", "fill_blank", "error_correction", "sentence_transformation", "matching" };
        for (var i = 0; i < targetCount; i++)
        {
            var t = types[i % types.Length];
            var rule = anchorRules[i % Math.Max(1, anchorRules.Count)];
            exercises.Add(MakeFallbackExercise(t, i + 1, rule));
        }

        return new ParsedLessonDraft(
            Title: $"{topicLabel} practice",
            TopicSlug: slug,
            Level: level,
            EstimatedMinutes: Math.Max(8, targetCount * 2),
            Description: $"Starter lesson drafted from the admin prompt: {Truncate(request.Prompt, 200)}",
            ContentBlocks: contentBlocks,
            Exercises: exercises,
            AppliedRuleIds: appliedIds);
    }

    private static LessonExercise MakeFallbackExercise(string type, int index, OetRule rule)
    {
        var applied = new List<string> { rule.Id };
        var exemplar = rule.ExemplarPhrases is { Count: > 0 } ? rule.ExemplarPhrases![0] : "The patient was admitted for further management.";

        return type switch
        {
            "mcq" => new LessonExercise(
                Id: $"ex-{index}", SortOrder: index, Type: "mcq",
                PromptMarkdown: $"Which sentence best follows {rule.Id} — {rule.Title}?",
                Options: new[]
                {
                    new { id = "a", label = "The patient has presented three weeks ago." },
                    new { id = "b", label = exemplar },
                    new { id = "c", label = "The patient presents three weeks ago." },
                },
                CorrectAnswer: "b",
                AcceptedAnswers: new List<string>(),
                ExplanationMarkdown: $"Option b follows {rule.Id}. See the rulebook body for the full guidance.",
                Difficulty: "intermediate",
                Points: 1,
                AppliedRuleIds: applied),
            "fill_blank" => new LessonExercise(
                Id: $"ex-{index}", SortOrder: index, Type: "fill_blank",
                PromptMarkdown: $"Fill the blank so the sentence follows {rule.Id}: '{exemplar.Replace("was", "___")}'",
                Options: Array.Empty<object>(),
                CorrectAnswer: "was",
                AcceptedAnswers: new List<string> { "was" },
                ExplanationMarkdown: $"'was' keeps the sentence in past simple, as required by {rule.Id}.",
                Difficulty: "beginner",
                Points: 1,
                AppliedRuleIds: applied),
            "error_correction" => new LessonExercise(
                Id: $"ex-{index}", SortOrder: index, Type: "error_correction",
                PromptMarkdown: $"Correct the sentence so it follows {rule.Id}: 'The nurse explain the procedure.'",
                Options: Array.Empty<object>(),
                CorrectAnswer: "The nurse explains the procedure.",
                AcceptedAnswers: new List<string> { "The nurse explains the procedure" },
                ExplanationMarkdown: $"Subject–verb agreement: explain → explains. Cite {rule.Id}.",
                Difficulty: "beginner",
                Points: 1,
                AppliedRuleIds: applied),
            "sentence_transformation" => new LessonExercise(
                Id: $"ex-{index}", SortOrder: index, Type: "sentence_transformation",
                PromptMarkdown: $"Rewrite so the sentence uses passive voice and follows {rule.Id}: 'The doctor prescribed the medication.'",
                Options: Array.Empty<object>(),
                CorrectAnswer: "The medication was prescribed by the doctor.",
                AcceptedAnswers: new List<string> { "The medication was prescribed by the doctor" },
                ExplanationMarkdown: $"Use passive voice to keep focus on the patient or the action per {rule.Id}.",
                Difficulty: "intermediate",
                Points: 2,
                AppliedRuleIds: applied),
            _ => new LessonExercise(
                Id: $"ex-{index}", SortOrder: index, Type: "matching",
                PromptMarkdown: $"Match each fragment to the best completion (cite {rule.Id}).",
                Options: new[]
                {
                    new { left = "The patient was admitted", right = "after the assessment" },
                    new { left = "The results were discussed", right = "with the consultant" },
                },
                CorrectAnswer: new[]
                {
                    new { left = "The patient was admitted", right = "after the assessment" },
                    new { left = "The results were discussed", right = "with the consultant" },
                },
                AcceptedAnswers: new List<string>(),
                ExplanationMarkdown: $"Preserve the time relationship and clinical reference per {rule.Id}.",
                Difficulty: "intermediate",
                Points: 2,
                AppliedRuleIds: applied),
        };
    }

    private static string TitleCase(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Grammar";
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string DeriveSlug(string prompt)
    {
        var words = prompt.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Take(3);
        return Slugify(string.Join('_', words));
    }

    private static string Truncate(string raw, int max)
        => string.IsNullOrEmpty(raw) ? "" : (raw.Length <= max ? raw : raw[..max].TrimEnd() + "…");

    // ---------------------------------------------------------------------
    // Intermediate records
    // ---------------------------------------------------------------------

    private sealed record ParsedLessonDraft(
        string Title,
        string TopicSlug,
        string Level,
        int EstimatedMinutes,
        string? Description,
        List<LessonContentBlock> ContentBlocks,
        List<LessonExercise> Exercises,
        List<string> AppliedRuleIds);

    private sealed record LessonContentBlock(
        string Id,
        int SortOrder,
        string Type,
        string ContentMarkdown,
        string? Content);

    private sealed record LessonExercise(
        string Id,
        int SortOrder,
        string Type,
        string PromptMarkdown,
        object Options,
        object? CorrectAnswer,
        List<string> AcceptedAnswers,
        string ExplanationMarkdown,
        string Difficulty,
        int Points,
        List<string> AppliedRuleIds);
}
