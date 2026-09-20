using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

public sealed record InterlocutorTurnPlanRequest(
    SpeakingSimulationV11PersonaRuntimeSnapshot PersonaSnapshot,
    ExamProfession Profession,
    string LearnerText,
    string TranscriptJson,
    int TurnIndex,
    int ElapsedSeconds,
    int RemainingSeconds,
    string SessionId,
    string UserId,
    string? AuthAccountId = null,
    string? TenantId = null,
    string? CandidateCountry = null);

public sealed class InterlocutorTurnPlanner(
    IAiGatewayService gateway,
    IConversationOptionsProvider optionsProvider,
    ILogger<InterlocutorTurnPlanner> logger)
{
    private const string PolicyVersion = "live-interlocutor-v11";

    private static readonly JsonSerializerOptions ModelJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions OutboundJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Regex SentenceBoundary = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);
    private static readonly Regex WordPattern = new(@"[a-z0-9']+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "about", "after", "again", "because", "before", "could", "from", "have", "into", "more", "only",
        "other", "really", "should", "that", "their", "there", "these", "they", "this", "those", "very",
        "what", "when", "where", "which", "while", "with", "would", "your", "patient", "candidate", "doctor", "nurse",
    };

    private static readonly string[] BroadElicitationSignals =
    [
        "anything else", "any other", "any concerns", "what concerns", "what worries", "worried about",
        "how do you feel", "how are you feeling", "tell me more", "what do you think", "what's bothering",
    ];

    private static readonly string[] ClosingSignals =
    [
        "anything else", "any other questions", "any questions", "does that sound", "are you happy with",
        "are you comfortable with", "is that okay", "is that ok", "shall we do that", "before we finish", "before we end",
    ];

    public async Task<ConversationAiReply> PlanAsync(InterlocutorTurnPlanRequest request, CancellationToken ct)
    {
        var disclosureSnapshot = BuildDisclosureSnapshot(request.PersonaSnapshot);
        var satisfied = EvaluateConditions(disclosureSnapshot, request.PersonaSnapshot, request.LearnerText, request.TurnIndex);
        var safeContext = InterlocutorDisclosurePolicy.ProjectForModel(disclosureSnapshot, satisfied);

        var scenarioJson = JsonSerializer.Serialize(new
        {
            mode = "roleplay",
            roleClass = InterlocutorRoleClassifier.ToCode(safeContext.RoleClass),
            disclosure = safeContext,
        }, OutboundJsonOptions);

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Conversation,
            Profession = request.Profession,
            Task = AiTaskMode.PlanConversationReply,
            CandidateCountry = request.CandidateCountry,
            ConversationScenarioJson = scenarioJson,
            ConversationTranscriptJson = request.TranscriptJson,
            ConversationTaskTypeCode = "speaking_roleplay_v11",
            ConversationTurnIndex = request.TurnIndex,
            ConversationElapsedSeconds = request.ElapsedSeconds,
            ConversationRemainingSeconds = request.RemainingSeconds,
        });

        var options = await optionsProvider.GetAsync(ct);
        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = "Select only IDs and statement variants present in the safe disclosure projection. Return FactSelections and NonFactualResponseKind only. Select at most one fact. Never write spoken text or infer an unavailable fact.",
            UserId = request.UserId,
            AuthAccountId = request.AuthAccountId,
            TenantId = request.TenantId,
            FeatureCode = AiFeatureCodes.ConversationReply,
            Model = string.IsNullOrWhiteSpace(options.ReplyModel) ? "claude-sonnet-5" : options.ReplyModel,
            Temperature = Math.Min(options.ReplyTemperature, 0.3),
            MaxTokens = 220,
            ResponseFormatJson = "json_object",
            PromptTemplateId = "SpeakingInterlocutorV11Plan",
        }, ct);

        var response = ParseOrFallback(result.Completion, safeContext, request.SessionId);
        var rendered = InterlocutorDisclosurePolicy.Render(safeContext, response);
        rendered = SpeakingSimulationV11PersonaService.SanitizeActorReply(rendered);

        return new ConversationAiReply(
            rendered, null,
            response.NonFactualResponseKind == InterlocutorNonFactualResponseKind.Closing,
            result.AppliedRuleIds, result.RulebookVersion,
            result.ResolvedProvider, result.ResolvedModel, result.UsageRecordId,
            result.LatencyMs, result.EstimatedCostUsd, result.RetryCount,
            result.Usage?.PromptTokens ?? 0, result.Usage?.CompletionTokens ?? 0);
    }

    internal static InterlocutorDisclosureSnapshot BuildDisclosureSnapshot(SpeakingSimulationV11PersonaRuntimeSnapshot snapshot)
    {
        var facts = new List<InterlocutorFactDefinition>();
        var persona = ParseObject(snapshot.PersonaJson);
        AddStringFact(facts, persona, "openingResponse", "opening.response", "opening.first_turn", NaturalizeDialogue);
        AddStringFact(facts, persona, "patientBackground", "patient.background", "patient.background.relevant", NaturalizeNarrative);
        AddArrayFacts(facts, persona, "patientTasks", "patient_task", "relevant", NaturalizeNarrative);
        AddArrayFacts(facts, persona, "prompts", "prompt", "relevant", NaturalizeDialogue);
        AddSentenceFacts(facts, persona, "hiddenInformation", "hidden", "direct_relevant", NaturalizeNarrative);
        AddStringFact(facts, persona, "closingCue", "closing.cue", "closing.ready", NaturalizeClosingCue);
        AddCarriedFacts(facts, snapshot);

        return new InterlocutorDisclosureSnapshot(
            PolicyVersion,
            $"case:{StableIdFragment(snapshot.RolePlayCardId)}",
            InterlocutorRoleClassifier.Resolve(snapshot.InterlocutorRole),
            facts);
    }

    internal static InterlocutorSatisfiedConditions EvaluateConditions(
        InterlocutorDisclosureSnapshot disclosureSnapshot,
        SpeakingSimulationV11PersonaRuntimeSnapshot personaSnapshot,
        string learnerText,
        int turnIndex)
    {
        var satisfied = new List<string>();
        foreach (var fact in disclosureSnapshot.Facts)
        {
            if (fact.Disclosure != InterlocutorFactDisclosure.Conditional || fact.ConditionId is null)
                continue;

            var statement = fact.ApprovedStatements.FirstOrDefault()?.Text ?? string.Empty;
            var condition = fact.ConditionId;
            var isSatisfied = condition switch
            {
                "opening.first_turn" => turnIndex <= 1,
                "closing.ready" => turnIndex >= 3 && ContainsAny(learnerText, ClosingSignals),
                "followup.active" => personaSnapshot.FollowUpActivated,
                _ when condition.EndsWith(".direct_relevant", StringComparison.Ordinal) =>
                    LooksLikeQuestion(learnerText) && HasDirectContentOverlap(learnerText, statement),
                _ when condition.EndsWith(".relevant", StringComparison.Ordinal) =>
                    ContainsAny(learnerText, BroadElicitationSignals) || HasContentOverlap(learnerText, statement),
                _ => false,
            };

            if (isSatisfied)
                satisfied.Add(condition);
        }

        return InterlocutorSatisfiedConditions.FromServerState(satisfied.Distinct(StringComparer.Ordinal).ToArray());
    }

    private InterlocutorModelResponse ParseOrFallback(string completion, InterlocutorModelSafeContext safeContext, string sessionId)
    {
        try
        {
            var response = JsonSerializer.Deserialize<InterlocutorModelResponse>(ExtractJsonObject(completion), ModelJsonOptions)
                ?? throw new JsonException("Empty interlocutor plan.");
            if (response.FactSelections is null)
                throw new InvalidOperationException("Interlocutor plan omitted FactSelections.");
            if (response.FactSelections.Count > 1)
                throw new InvalidOperationException("Interlocutor plan selected more than one fact.");
            _ = InterlocutorDisclosurePolicy.Render(safeContext, response);
            return response;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            logger.LogWarning(ex,
                "Rejected invalid v1.1 interlocutor plan for session {SessionId}; using server-authored clarification.",
                sessionId);
            return new InterlocutorModelResponse(
                Array.Empty<InterlocutorFactSelection>(),
                InterlocutorNonFactualResponseKind.ClarificationRequest);
        }
    }

    private static void AddStringFact(
        ICollection<InterlocutorFactDefinition> facts,
        IReadOnlyDictionary<string, JsonElement> persona,
        string sourceKey,
        string factId,
        string? conditionId,
        Func<string, string> transform)
    {
        if (!persona.TryGetValue(sourceKey, out var value) || value.ValueKind != JsonValueKind.String)
            return;
        var source = value.GetString();
        if (!string.IsNullOrWhiteSpace(source))
            AddFact(facts, factId, conditionId, transform(source.Trim()));
    }

    private static void AddArrayFacts(
        ICollection<InterlocutorFactDefinition> facts,
        IReadOnlyDictionary<string, JsonElement> persona,
        string sourceKey,
        string factPrefix,
        string conditionSuffix,
        Func<string, string> transform)
    {
        if (!persona.TryGetValue(sourceKey, out var value) || value.ValueKind != JsonValueKind.Array)
            return;
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                continue;
            index++;
            var factId = $"{factPrefix}.{index}";
            AddFact(facts, factId, $"{factId}.{conditionSuffix}", transform(item.GetString()!.Trim()));
        }
    }

    private static void AddSentenceFacts(
        ICollection<InterlocutorFactDefinition> facts,
        IReadOnlyDictionary<string, JsonElement> persona,
        string sourceKey,
        string factPrefix,
        string conditionSuffix,
        Func<string, string> transform)
    {
        if (!persona.TryGetValue(sourceKey, out var value) || value.ValueKind != JsonValueKind.String)
            return;
        var source = value.GetString();
        if (string.IsNullOrWhiteSpace(source))
            return;
        var index = 0;
        foreach (var sentence in SentenceBoundary.Split(source.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            index++;
            var factId = $"{factPrefix}.{index}";
            AddFact(facts, factId, $"{factId}.{conditionSuffix}", transform(sentence.Trim()));
        }
    }

    private static void AddCarriedFacts(ICollection<InterlocutorFactDefinition> facts, SpeakingSimulationV11PersonaRuntimeSnapshot snapshot)
    {
        if (!snapshot.FollowUpActivated)
            return;
        var approved = ParseStringArray(snapshot.ApprovedCarryFactKeysJson);
        foreach (var (key, value) in ParseObject(snapshot.CarriedFactsJson))
        {
            if (!approved.Contains(key, StringComparer.Ordinal)
                || key is "communicationGoal" or "clinicalTopic" or "resistanceLevel" or "layLanguageTriggersJson")
                continue;
            var prefix = $"carry.{StableIdFragment(key)}";
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                AddFact(facts, prefix, "followup.active", NaturalizeNarrative(value.GetString()!.Trim()));
                continue;
            }
            if (value.ValueKind != JsonValueKind.Array)
                continue;
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                    continue;
                index++;
                AddFact(facts, $"{prefix}.{index}", "followup.active", NaturalizeNarrative(item.GetString()!.Trim()));
            }
        }
    }

    private static void AddFact(ICollection<InterlocutorFactDefinition> facts, string factId, string? conditionId, string statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
            return;
        facts.Add(new InterlocutorFactDefinition(
            factId,
            conditionId is null ? InterlocutorFactDisclosure.AlwaysEligible : InterlocutorFactDisclosure.Conditional,
            [new InterlocutorApprovedStatement("default", statement.Trim())],
            conditionId));
    }

    private static string NaturalizeDialogue(string text) => text.Trim();

    private static string NaturalizeNarrative(string text)
    {
        var value = text.Trim();
        value = Regex.Replace(value, @"^(?:the\s+)?(?:patient|client|owner)\s+", "I ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"^(?:she|he)\s+", "I ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\b(?:her|his)\b", "my", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bI\s+has\b", "I have", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bI\s+is\b", "I am", RegexOptions.IgnoreCase);
        return value;
    }

    private static string NaturalizeClosingCue(string text)
    {
        var value = text.Trim();
        value = Regex.Replace(value, @"^Agrees\s+to\s+", "I agree to ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"^Accepts\s+", "I accept ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"^Understands\s+", "I understand ", RegexOptions.IgnoreCase);
        return NaturalizeNarrative(value);
    }

    private static bool LooksLikeQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Contains('?'))
            return true;
        string[] starters =
        [
            "what ", "when ", "where ", "why ", "how ", "have you", "did you", "do you", "can you",
            "could you", "would you", "are you", "is there", "tell me", "describe ", "any ", "anything ",
        ];
        return starters.Any(normalized.StartsWith);
    }

    private static bool HasContentOverlap(string left, string right)
    {
        var leftTokens = ContentTokens(left);
        return leftTokens.Count > 0 && leftTokens.Overlaps(ContentTokens(right));
    }

    private static bool HasDirectContentOverlap(string left, string right)
    {
        var leftTokens = ContentTokens(left);
        var rightTokens = ContentTokens(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return false;

        var overlap = leftTokens.Where(rightTokens.Contains).ToArray();
        return overlap.Length >= 2 || overlap.Any(token => token.Length >= 8);
    }

    private static HashSet<string> ContentTokens(string value)
        => WordPattern.Matches(value.ToLowerInvariant())
            .Select(x => x.Value.Trim('\''))
            .Where(x => x.Length >= 4 && !StopWords.Contains(x))
            .ToHashSet(StringComparer.Ordinal);

    private static bool ContainsAny(string text, IEnumerable<string> signals)
    {
        var normalized = text?.ToLowerInvariant() ?? string.Empty;
        return signals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            return document.RootElement.EnumerateObject()
                .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    private static string[] ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                .Select(x => x.GetString()!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ExtractJsonObject(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion))
            throw new JsonException("Empty interlocutor plan response.");
        var trimmed = completion.Trim();
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
            throw new JsonException("Interlocutor plan did not contain a JSON object.");
        return trimmed[firstBrace..(lastBrace + 1)];
    }

    private static string StableIdFragment(string value)
    {
        var chars = value.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_').ToArray();
        var normalized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }
}
