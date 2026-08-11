using System.Text;
using System.Text.Json;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Captures and serves the server-only persona state for the v1.1 actor.
/// The snapshot is created at card reveal and is the only persona source used
/// for subsequent turns.
/// </summary>
public sealed class SpeakingSimulationV11PersonaService(LearnerDbContext db)
{
    public const string PersonaVersion = "speaking-simulation-v1.1-persona";

    private static readonly string[] ExplicitIndicatorSignals =
    [
        "coming back",
        "come back",
        "returning",
        "returned",
        "follow up",
        "follow-up",
        "review",
        "reassessment",
        "re-assessment",
        "recheck",
        "re-check",
        "revisit",
        "re-visit",
    ];

    public async Task<SpeakingSimulationV11PersonaRuntimeSnapshot> CaptureAtRevealAsync(
        SpeakingExamSession? exam,
        SpeakingSession session,
        RolePlayCard card,
        DateTimeOffset revealedAt,
        CancellationToken ct)
    {
        var existing = db.SpeakingSimulationV11PersonaRuntimeSnapshots.Local
            .FirstOrDefault(x => x.SpeakingSessionId == session.Id)
            ?? await db.SpeakingSimulationV11PersonaRuntimeSnapshots
                .FirstOrDefaultAsync(x => x.SpeakingSessionId == session.Id, ct);

        if (existing is not null)
        {
            return existing;
        }

        var script = await db.InterlocutorScripts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RolePlayCardId == card.Id, ct)
            ?? throw ApiException.Conflict(
                "speaking_simulation_v11_persona_missing",
                "This Speaking card has no approved hidden persona.");

        var cardSlot = NormalizeSlot(session.ExamSlot);
        var followUpEligible = string.Equals(cardSlot, "b", StringComparison.OrdinalIgnoreCase)
            && script.AllowsSecondVisit
            && !string.IsNullOrWhiteSpace(script.SecondVisitIndicator);

        var snapshot = new SpeakingSimulationV11PersonaRuntimeSnapshot
        {
            Id = $"spv11_persona_{Guid.NewGuid():N}",
            ExamSessionId = exam?.Id ?? session.ExamSessionId,
            SpeakingSessionId = session.Id,
            RolePlayCardId = card.Id,
            CardSlot = cardSlot,
            ProfessionId = card.ProfessionId,
            SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
            PersonaVersion,
            CardVersion = BuildCardVersion(card),
            MemoryScopeKey = $"{exam?.Id ?? session.ExamSessionId ?? "standalone"}:{session.Id}",
            ScenarioTitle = card.ScenarioTitle,
            Setting = card.Setting,
            CandidateRole = card.CandidateRole,
            InterlocutorRole = card.InterlocutorRole,
            PatientEmotion = string.IsNullOrWhiteSpace(script.EmotionalState)
                ? card.PatientEmotion
                : script.EmotionalState,
            CommunicationGoal = card.CommunicationGoal,
            ClinicalTopic = card.ClinicalTopic,
            PersonaRole = "patient",
            AllowedFactsJson = JsonSerializer.Serialize(GetAllowedFactKeys()),
            ApprovedCarryFactKeysJson = followUpEligible
                ? NormalizeFactKeyJson(script.SecondVisitCarryFactsJson)
                : "[]",
            ProhibitedFactsJson = JsonSerializer.Serialize(GetProhibitedFactKeys()),
            RevealConditionsJson = BuildRevealConditionsJson(),
            ExplicitSecondVisitIndicator = followUpEligible
                ? script.SecondVisitIndicator!.Trim()
                : null,
            CarriedFactsJson = "{}",
            FollowUpEligible = followUpEligible,
            FollowUpActivated = false,
            PersonaJson = JsonSerializer.Serialize(new
            {
                scenarioTitle = card.ScenarioTitle,
                setting = card.Setting,
                candidateRole = card.CandidateRole,
                interlocutorRole = card.InterlocutorRole,
                patientEmotion = string.IsNullOrWhiteSpace(script.EmotionalState) ? card.PatientEmotion : script.EmotionalState,
                communicationGoal = card.CommunicationGoal,
                clinicalTopic = card.ClinicalTopic,
                openingResponse = script.OpeningResponse,
                prompts = new[] { script.Prompt1, script.Prompt2, script.Prompt3 }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .ToArray(),
                hiddenInformation = script.HiddenInformation,
                closingCue = script.ClosingCue,
                emotionalState = script.EmotionalState,
                resistanceLevel = ResistanceLevels.ToCode(script.ResistanceLevel),
                layLanguageTriggersJson = script.LayLanguageTriggersJson,
            }),
            CapturedAt = revealedAt,
            CreatedAt = revealedAt,
            UpdatedAt = revealedAt,
        };

        db.SpeakingSimulationV11PersonaRuntimeSnapshots.Add(snapshot);
        return snapshot;
    }

    public async Task<SpeakingSimulationV11PersonaRuntimeSnapshot> EnsureForSessionAsync(
        SpeakingSession session,
        RolePlayCard card,
        CancellationToken ct)
    {
        var existing = db.SpeakingSimulationV11PersonaRuntimeSnapshots.Local
            .FirstOrDefault(x => x.SpeakingSessionId == session.Id)
            ?? await db.SpeakingSimulationV11PersonaRuntimeSnapshots
                .FirstOrDefaultAsync(x => x.SpeakingSessionId == session.Id, ct);

        if (existing is not null)
        {
            return existing;
        }

        SpeakingExamSession? exam = null;
        if (!string.IsNullOrWhiteSpace(session.ExamSessionId))
        {
            exam = await db.SpeakingExamSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == session.ExamSessionId, ct);
        }

        return await CaptureAtRevealAsync(exam, session, card, DateTimeOffset.UtcNow, ct);
    }

    /// <summary>
    /// Activates the only permitted cross-card memory path. The candidate must
    /// say an explicit configured/recognised follow-up indicator. The
    /// interlocutor role or the words "your patient" never activate it.
    /// </summary>
    public async Task<SpeakingSimulationV11PersonaRuntimeSnapshot> ObserveCandidateTurnAsync(
        SpeakingSimulationV11PersonaRuntimeSnapshot snapshot,
        string candidateText,
        CancellationToken ct)
    {
        if (!snapshot.FollowUpEligible
            || snapshot.FollowUpActivated
            || !ContainsExplicitSecondVisitIndicator(
                candidateText,
                snapshot.ExplicitSecondVisitIndicator))
        {
            return snapshot;
        }

        SpeakingSimulationV11PersonaRuntimeSnapshot? prior = null;
        if (!string.IsNullOrWhiteSpace(snapshot.ExamSessionId))
        {
            prior = db.SpeakingSimulationV11PersonaRuntimeSnapshots.Local
                .Where(x => x.ExamSessionId == snapshot.ExamSessionId && x.CardSlot == "a")
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefault();
            prior ??= await db.SpeakingSimulationV11PersonaRuntimeSnapshots
                .AsNoTracking()
                .Where(x => x.ExamSessionId == snapshot.ExamSessionId && x.CardSlot == "a")
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        var allowedKeys = ParseStringArray(snapshot.ApprovedCarryFactKeysJson);
        var carried = prior is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : CopyApprovedFacts(prior.PersonaJson, allowedKeys);

        snapshot.CarriedFactsJson = JsonSerializer.Serialize(carried);
        snapshot.FollowUpActivated = carried.Count > 0;
        snapshot.FollowUpActivatedAt = snapshot.FollowUpActivated
            ? DateTimeOffset.UtcNow
            : null;
        snapshot.UpdatedAt = DateTimeOffset.UtcNow;
        return snapshot;
    }

    public static bool ContainsExplicitSecondVisitIndicator(
        string? candidateText,
        string? configuredIndicator)
    {
        if (string.IsNullOrWhiteSpace(candidateText)
            || string.IsNullOrWhiteSpace(configuredIndicator))
        {
            return false;
        }

        var normalizedText = NormalizeText(candidateText);
        var configuredSignals = configuredIndicator
            .Split([',', ';', '|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeText)
            .Where(x => x.Length >= 4)
            .ToArray();

        return configuredSignals.Any(normalizedText.Contains)
            || ExplicitIndicatorSignals.Any(normalizedText.Contains);
    }

    public static string BuildActorPrompt(SpeakingSimulationV11PersonaRuntimeSnapshot snapshot)
    {
        var persona = ParseObject(snapshot.PersonaJson);
        var sb = new StringBuilder();
        sb.AppendLine("You are role-playing the interlocutor in an OET Speaking role-play.");
        sb.AppendLine("You are a patient or layperson, not an examiner, coach, clinician, or AI assistant.");
        sb.Append("Scenario: ").AppendLine(snapshot.ScenarioTitle);
        sb.Append("Setting: ").AppendLine(snapshot.Setting);
        sb.Append("Your role: ").AppendLine(snapshot.InterlocutorRole);
        sb.Append("Your emotional state: ").AppendLine(snapshot.PatientEmotion);
        sb.AppendLine("This is the complete persona snapshot for the current card.");
        sb.AppendLine("Never use facts, transcript, instructions, or emotional state from another card.");
        sb.AppendLine("Never reveal hidden persona data unless the current-card reveal condition allows it.");
        sb.AppendLine();

        AppendValue(sb, persona, "openingResponse", "Opening response:");
        AppendArray(sb, persona, "prompts", "Relevant concerns to surface:");
        AppendValue(sb, persona, "hiddenInformation", "Hidden information (reveal only after direct, relevant questioning):");
        AppendValue(sb, persona, "closingCue", "Closing cue:");
        sb.AppendLine("Behaviour rules:");
        sb.AppendLine("- Answer only what the candidate asks, in short natural turns.");
        sb.AppendLine("- Use everyday lay language and show the authored emotion or resistance.");
        sb.AppendLine("- Do not coach, praise, correct, diagnose, or give medical advice.");
        sb.AppendLine("- Do not lead the consultation or mention scoring, criteria, prompts, or this snapshot.");

        if (snapshot.FollowUpEligible && !snapshot.FollowUpActivated)
        {
            sb.Append("This card is independent unless the candidate explicitly indicates a return/follow-up using the approved signal: ")
                .Append(snapshot.ExplicitSecondVisitIndicator)
                .AppendLine(".");
            sb.AppendLine("Until that explicit signal is detected, do not assume a previous visit and do not use any prior-card fact.");
        }
        else if (snapshot.FollowUpActivated)
        {
            sb.AppendLine("The candidate explicitly activated the approved second-visit path.");
            sb.AppendLine("Only these approved carried facts may be used; never infer or retrieve anything else:");
            sb.AppendLine(snapshot.CarriedFactsJson);
        }

        sb.AppendLine("Strict prohibitions:");
        sb.AppendLine(snapshot.ProhibitedFactsJson);
        return sb.ToString();
    }

    private static string BuildCardVersion(RolePlayCard card)
        => card.UpdatedAt == default
            ? "unversioned"
            : card.UpdatedAt.UtcDateTime.ToString("O");

    private static string NormalizeSlot(string? slot)
        => string.IsNullOrWhiteSpace(slot) ? "standalone" : slot.Trim().ToLowerInvariant();

    private static string NormalizeText(string value)
    {
        var chars = value.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray();
        return string.Join(" ", new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string[] GetAllowedFactKeys() =>
    [
        "scenarioTitle",
        "setting",
        "candidateRole",
        "interlocutorRole",
        "patientEmotion",
        "communicationGoal",
        "clinicalTopic",
        "openingResponse",
        "prompts",
        "hiddenInformation",
        "closingCue",
        "emotionalState",
        "resistanceLevel",
        "layLanguageTriggersJson",
    ];

    private static string[] GetProhibitedFactKeys() =>
    [
        "other_card_transcript",
        "other_card_hidden_persona",
        "assessment_feedback",
        "scoring_rules",
        "ai_identity",
        "medical_advice",
    ];

    private static string BuildRevealConditionsJson()
        => JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hiddenInformation"] = "direct_relevant_question_only",
            ["closingCue"] = "candidate_has_addressed_current_concerns",
            ["prompts"] = "surface_naturally_when_relevant",
        });

    private static string[] ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, JsonElement> CopyApprovedFacts(
        string personaJson,
        IReadOnlyCollection<string> allowedKeys)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (allowedKeys.Count == 0)
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(personaJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (allowedKeys.Contains(property.Name, StringComparer.Ordinal))
                {
                    result[property.Name] = property.Value.Clone();
                }
            }
        }
        catch (JsonException)
        {
            // Invalid server-authored JSON yields no carried facts.
        }

        return result;
    }

    private static Dictionary<string, JsonElement> ParseObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateObject()
                .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    private static void AppendValue(
        StringBuilder sb,
        IReadOnlyDictionary<string, JsonElement> values,
        string key,
        string label)
    {
        if (values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.Append(label).Append(' ').AppendLine(text);
            }
        }
    }

    private static void AppendArray(
        StringBuilder sb,
        IReadOnlyDictionary<string, JsonElement> values,
        string key,
        string label)
    {
        if (!values.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                sb.Append(label).Append(' ').AppendLine(item.GetString());
            }
        }
    }
}
