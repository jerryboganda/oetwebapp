using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Pronunciation coaching copy generator. ALWAYS routes through the grounded
/// AI gateway with <c>RuleKind.Pronunciation</c> + <c>AiTaskMode.GeneratePronunciationFeedback</c>
/// and <c>FeatureCode = AiFeatureCodes.PronunciationFeedback</c>. Platform-only.
///
/// The gateway physically refuses ungrounded prompts, so this service cannot
/// drift into free-form generation even by accident.
///
/// Output is cached on the assessment row (<see cref="PronunciationAssessment.FeedbackJson"/>)
/// so repeated views don't re-bill the AI provider.
/// </summary>
public interface IPronunciationFeedbackService
{
    Task<PronunciationFeedback> GenerateAsync(
        PronunciationAssessment assessment,
        PronunciationDrill drill,
        string? userId,
        string profession,
        CancellationToken ct);
}

public sealed record PronunciationFeedback(
    string Summary,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<PronunciationImprovement> Improvements,
    IReadOnlyList<string> AppliedRuleIds,
    string? NextDrillTargetPhoneme);

public sealed record PronunciationImprovement(
    string RuleId,
    string Message,
    string? DrillSuggestion);

public sealed class PronunciationFeedbackService(
    IAiGatewayService gateway,
    ILogger<PronunciationFeedbackService> logger) : IPronunciationFeedbackService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PronunciationFeedback> GenerateAsync(
        PronunciationAssessment assessment,
        PronunciationDrill drill,
        string? userId,
        string profession,
        CancellationToken ct)
    {
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = ParseProfession(profession, drill.Profession),
            Task = AiTaskMode.GeneratePronunciationFeedback,
        });

        var userPrompt = BuildUserPrompt(assessment, drill);

        try
        {
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userPrompt,
                Model = "auto",
                Temperature = 0.3,
                MaxTokens = 900,
                UserId = userId,
                FeatureCode = AiFeatureCodes.PronunciationFeedback,
                PromptTemplateId = "pronunciation.feedback.v1",
            }, ct);

            return ParseFeedback(result.Completion, drill)
                   ?? FallbackFeedback(assessment, drill);
        }
        catch (PromptNotGroundedException)
        {
            throw; // a bug — surface loudly
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pronunciation feedback AI call failed — using deterministic fallback.");
            return FallbackFeedback(assessment, drill);
        }
    }

    private static string BuildUserPrompt(PronunciationAssessment a, PronunciationDrill d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Drill: {d.Label} (target phoneme: {d.TargetPhoneme}, focus: {d.Focus}, difficulty: {d.Difficulty}).");
        if (!string.IsNullOrWhiteSpace(d.PrimaryRuleId))
            sb.AppendLine($"Primary rule: {d.PrimaryRuleId}");
        sb.AppendLine();
        sb.AppendLine("Scores (0-100):");
        sb.AppendLine($"  accuracy={a.AccuracyScore} fluency={a.FluencyScore} completeness={a.CompletenessScore} prosody={a.ProsodyScore} overall={a.OverallScore}");
        sb.AppendLine($"Projected Speaking band: {a.ProjectedSpeakingScaled}/500 ({a.ProjectedSpeakingGrade}).");
        sb.AppendLine();
        sb.AppendLine("Word scores (JSON):");
        sb.AppendLine(a.WordScoresJson);
        sb.AppendLine();
        sb.AppendLine("Problematic phonemes (JSON):");
        sb.AppendLine(a.ProblematicPhonemesJson);
        sb.AppendLine();
        sb.AppendLine("Respond with the Reply format above. Every ruleId must be a P-rule from the pronunciation rulebook.");
        return sb.ToString();
    }

    private static PronunciationFeedback? ParseFeedback(string completion, PronunciationDrill drill)
    {
        var jsonText = ExtractJsonObject(completion);
        if (jsonText is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";

            var strengths = new List<string>();
            if (root.TryGetProperty("strengths", out var st) && st.ValueKind == JsonValueKind.Array)
                foreach (var e in st.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) strengths.Add(e.GetString() ?? "");

            var improvements = new List<PronunciationImprovement>();
            if (root.TryGetProperty("improvements", out var im) && im.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in im.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var ruleId = e.TryGetProperty("ruleId", out var r) ? r.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(ruleId)) continue;
                    var msg = e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    var sug = e.TryGetProperty("drillSuggestion", out var ds) ? ds.GetString() : null;
                    improvements.Add(new PronunciationImprovement(ruleId, msg, sug));
                }
            }

            var appliedRuleIds = new List<string>();
            if (root.TryGetProperty("appliedRuleIds", out var ar) && ar.ValueKind == JsonValueKind.Array)
                foreach (var e in ar.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) appliedRuleIds.Add(e.GetString() ?? "");
            if (appliedRuleIds.Count == 0 && !string.IsNullOrWhiteSpace(drill.PrimaryRuleId))
                appliedRuleIds.Add(drill.PrimaryRuleId!);

            string? next = root.TryGetProperty("nextDrillTargetPhoneme", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(next)) next = null;

            return new PronunciationFeedback(summary, strengths, improvements, appliedRuleIds, next);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PronunciationFeedback FallbackFeedback(PronunciationAssessment a, PronunciationDrill d)
    {
        var summary = a.OverallScore >= 80
            ? "Strong attempt — clear articulation across most of the reference."
            : a.OverallScore >= 65
                ? "Good progress — most of the target phoneme lands clearly but a few words need more shaping."
                : "Keep practising — the target phoneme is not yet consistent. Slow down and exaggerate the articulation.";
        var strengths = new List<string>();
        if (a.CompletenessScore >= 85) strengths.Add("You read every word of the reference text.");
        if (a.FluencyScore >= 80) strengths.Add("Pacing is natural.");
        if (a.AccuracyScore >= 80) strengths.Add("Most phonemes are well articulated.");
        if (strengths.Count == 0) strengths.Add("You attempted the drill and produced a scoreable recording.");

        var improvements = new List<PronunciationImprovement>();
        var primary = string.IsNullOrWhiteSpace(d.PrimaryRuleId) ? "P01.1" : d.PrimaryRuleId!;
        improvements.Add(new PronunciationImprovement(
            primary,
            $"Focus on the {d.TargetPhoneme} phoneme — re-record slowly and compare with the model audio.",
            DrillSuggestion: "Try the same drill again after 10 minutes of targeted practice."));

        return new PronunciationFeedback(summary, strengths, improvements,
            AppliedRuleIds: new[] { primary },
            NextDrillTargetPhoneme: d.TargetPhoneme);
    }

    private static string? ExtractJsonObject(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        int start = completion.IndexOf('{');
        int end = completion.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start) return null;
        return completion.Substring(start, end - start + 1);
    }

    private static ExamProfession ParseProfession(string? req, string drillProfession)
    {
        var candidate = !string.IsNullOrWhiteSpace(req) ? req! : drillProfession ?? "medicine";
        if (string.Equals(candidate, "all", StringComparison.OrdinalIgnoreCase))
            candidate = "medicine";
        var normalized = candidate.Replace("-", "_").ToLowerInvariant();
        foreach (var value in Enum.GetValues<ExamProfession>())
        {
            if (string.Equals(value.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        // occupational_therapy handling
        if (normalized.Replace("_", "") == "occupationaltherapy") return ExamProfession.OccupationalTherapy;
        if (normalized.Replace("_", "") == "speechpathology") return ExamProfession.SpeechPathology;
        return ExamProfession.Medicine;
    }
}
