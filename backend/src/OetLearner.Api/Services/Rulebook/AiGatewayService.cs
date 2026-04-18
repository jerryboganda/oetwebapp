using System.Text;
using System.Text.Json;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// ============================================================================
/// AI Gateway — SINGLE ENTRY POINT for every AI call in the .NET backend
/// ============================================================================
///
/// MISSION CRITICAL. Every AI invocation — OpenAI, Anthropic, Google Gemini,
/// any future provider — MUST flow through this gateway. The gateway refuses
/// to hand a request to a model unless the system prompt was assembled by
/// <see cref="RulebookPromptBuilder"/> and therefore embeds:
///
///   1. The active OET rulebook (Writing or Speaking, per profession).
///   2. The canonical OET scoring rules (from OetScoring), including the
///      country-aware Writing pass mark.
///   3. Strict guardrails ("do not invent rules", "advisory output only", etc.).
///   4. A structured reply-format contract for the task at hand.
///
/// Any attempt to send raw, unbounded prompts to a model raises
/// <see cref="PromptNotGroundedException"/>. This is the structural defence
/// that keeps the platform consistent, defensible, and aligned with Dr.
/// Hesham's authoritative content.
///
/// The gateway itself is provider-agnostic: provider implementations
/// (OpenAI, Anthropic, Gemini, …) implement <see cref="IAiModelProvider"/>
/// and are selected based on the configured AIConfigVersion in the admin
/// CMS. Replacing providers never touches this grounding code.
/// ============================================================================
/// </summary>
public interface IAiGatewayService
{
    Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default);

    AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context);
}

public sealed class AiGatewayService(IRulebookLoader loader, IEnumerable<IAiModelProvider> providers)
    : IAiGatewayService
{
    private readonly RulebookPromptBuilder _promptBuilder = new(loader);

    public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        => _promptBuilder.Build(context);

    public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
    {
        if (request.Prompt is null)
            throw new PromptNotGroundedException("AiGatewayRequest.Prompt is null. Always build a prompt via BuildGroundedPrompt first.");

        if (string.IsNullOrWhiteSpace(request.Prompt.SystemPrompt))
            throw new PromptNotGroundedException("SystemPrompt is empty. The gateway refuses to call a model without rulebook grounding.");

        if (!request.Prompt.SystemPrompt.Contains("OET AI — Rulebook-Grounded System Prompt", StringComparison.Ordinal))
            throw new PromptNotGroundedException(
                "SystemPrompt does not carry the rulebook grounding header. Build it via AiGatewayService.BuildGroundedPrompt.");

        IAiModelProvider? provider = null;
        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            provider = providers.FirstOrDefault(p => string.Equals(p.Name, request.Provider, StringComparison.OrdinalIgnoreCase));
        }

        // If the caller did not pin a provider, prefer the first real provider
        // over the mock fallback so configured production deployments use the
        // actual model path by default.
        provider ??= providers.FirstOrDefault(p => !string.Equals(p.Name, "mock", StringComparison.OrdinalIgnoreCase));
        provider ??= providers.FirstOrDefault();
        if (provider is null)
            throw new InvalidOperationException("No AI model provider registered.");

        var completion = await provider.CompleteAsync(new AiProviderRequest
        {
            Model = request.Model,
            SystemPrompt = request.Prompt.SystemPrompt,
            UserPrompt = BuildUserMessage(request),
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
        }, ct);

        return new AiGatewayResult
        {
            Completion = completion.Text,
            Usage = completion.Usage,
            Metadata = request.Prompt.Metadata,
            RulebookVersion = request.Prompt.Metadata.RulebookVersion,
            AppliedRuleIds = request.Prompt.Metadata.AppliedRuleIds,
        };
    }

    private static string BuildUserMessage(AiGatewayRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(request.Prompt!.TaskInstruction);
        if (!string.IsNullOrWhiteSpace(request.UserInput))
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine(request.UserInput);
        }
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------
// Grounded prompt builder (mirror of lib/rulebook/ai-prompt.ts)
// ---------------------------------------------------------------------------

public sealed class RulebookPromptBuilder(IRulebookLoader loader)
{
    public AiGroundedPrompt Build(AiGroundingContext ctx)
    {
        var book = loader.Load(ctx.Kind, ctx.Profession);

        var (passMark, passGrade) = ResolvePassMark(ctx);
        var applicable = SelectApplicableRules(book, ctx);
        var systemPrompt = RenderSystemPrompt(book, applicable, ctx, passMark, passGrade);
        var taskInstruction = RenderTaskInstruction(ctx, passMark, passGrade);

        return new AiGroundedPrompt
        {
            SystemPrompt = systemPrompt,
            TaskInstruction = taskInstruction,
            Metadata = new AiGroundedPromptMetadata
            {
                RulebookVersion = book.Version,
                RulebookKind = book.Kind,
                Profession = book.Profession,
                ScoringPassMark = passMark,
                ScoringGrade = passGrade,
                AppliedRulesCount = applicable.Count,
                AppliedRuleIds = applicable.Select(r => r.Id).ToArray(),
            },
        };
    }

    private static (int passMark, string passGrade) ResolvePassMark(AiGroundingContext ctx)
    {
        if (ctx.Kind == RuleKind.Speaking)
            return (OetScoring.ScaledPassGradeB, "B");

        var t = OetScoring.GetWritingPassThreshold(ctx.CandidateCountry);
        if (t is null) return (OetScoring.ScaledPassGradeB, "B");
        return (t.Threshold, t.Grade);
    }

    private static List<OetRule> SelectApplicableRules(OetRulebook book, AiGroundingContext ctx)
    {
        var context = ctx.LetterType ?? ctx.CardType;
        return book.Rules.Where(rule =>
        {
            if (rule.AppliesTo is null) return true;
            var el = rule.AppliesTo.Value;
            if (el.ValueKind == JsonValueKind.String && string.Equals(el.GetString(), "all", StringComparison.OrdinalIgnoreCase))
                return true;
            if (context is null) return true;
            if (el.ValueKind != JsonValueKind.Array) return true;
            foreach (var v in el.EnumerateArray())
                if (string.Equals(v.GetString(), context, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }).ToList();
    }

    private static string RenderSystemPrompt(OetRulebook book, List<OetRule> applicable, AiGroundingContext ctx, int passMark, string passGrade)
    {
        var critical = applicable.Where(r => r.Severity == RuleSeverity.Critical).ToList();
        var major = applicable.Where(r => r.Severity == RuleSeverity.Major).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("# OET AI — Rulebook-Grounded System Prompt");
        sb.AppendLine();
        sb.AppendLine("You are the AI assistant for the OET Preparation platform by Dr. Ahmed Hesham. Your knowledge about OET exam rules, grading, and feedback comes EXCLUSIVELY from the authoritative rulebook and scoring system reproduced below. Do not invent, extrapolate, or rely on outside opinions about OET.");
        sb.AppendLine();
        sb.AppendLine($"Rulebook: {book.Kind.ToString().ToUpperInvariant()} / {book.Profession.ToString().ToUpperInvariant()} / v{book.Version}");
        if (!string.IsNullOrWhiteSpace(book.AuthoritySource)) sb.AppendLine($"Authority: {book.AuthoritySource}");
        sb.AppendLine($"Task mode: {ctx.Task}");
        if (!string.IsNullOrWhiteSpace(ctx.CandidateCountry)) sb.AppendLine($"Candidate target country: {ctx.CandidateCountry}");
        sb.AppendLine($"Applied pass mark: {passMark}/500 (Grade {passGrade})");
        sb.AppendLine();
        AppendScoringSection(sb, ctx);
        AppendRulesBlock(sb, critical, major, applicable.Count);
        AppendGuardrails(sb, ctx);
        AppendReplyFormat(sb, ctx);
        return sb.ToString();
    }

    private static void AppendScoringSection(StringBuilder sb, AiGroundingContext ctx)
    {
        sb.AppendLine("## Canonical OET Scoring (non-negotiable)");
        sb.AppendLine();
        sb.AppendLine("- LISTENING: Grade B at 350/500; raw 30/42 ≡ 350/500 EXACTLY.");
        sb.AppendLine("- READING: Grade B at 350/500; raw 30/42 ≡ 350/500 EXACTLY.");
        sb.AppendLine($"- WRITING (country-aware): Grade B at {OetScoring.ScaledPassGradeB}/500 for UK/IE/AU/NZ/CA; Grade C+ at {OetScoring.ScaledPassGradeCPlus}/500 for US/QA.");
        sb.AppendLine("- SPEAKING: Grade B at 350/500, universal (no country variation).");
        sb.AppendLine();
        sb.AppendLine(ctx.Kind == RuleKind.Writing
            ? "**This call concerns WRITING** — apply the country-aware pass mark above. Never use the universal 350 threshold for Writing without verifying the country."
            : "**This call concerns SPEAKING** — apply the universal 350/500 pass mark regardless of country.");
        sb.AppendLine();
        sb.AppendLine("Always reference pass/fail using the exact OET grade letters: A, B, C+, C, D, E.");
        sb.AppendLine();
    }

    private static void AppendRulesBlock(StringBuilder sb, List<OetRule> critical, List<OetRule> major, int appliedTotal)
    {
        sb.AppendLine("## Active Rulebook");
        sb.AppendLine();
        sb.AppendLine($"Applied rules for this task: {appliedTotal} (critical: {critical.Count}, major: {major.Count}).");
        sb.AppendLine();
        sb.AppendLine("### CRITICAL rules (violations are auto-mark-deductions; flag them first)");
        sb.AppendLine();
        foreach (var rule in critical) sb.AppendLine(FormatRule(rule));
        sb.AppendLine();
        sb.AppendLine("### MAJOR rules (significant feedback items)");
        sb.AppendLine();
        foreach (var rule in major.Take(60)) sb.AppendLine(FormatRule(rule));
        if (major.Count > 60) sb.AppendLine($"… and {major.Count - 60} more major rules.");
        sb.AppendLine();
    }

    private static string FormatRule(OetRule rule)
    {
        var exemplar = rule.ExemplarPhrases is { Count: > 0 } ? $" · ex: \"{rule.ExemplarPhrases[0]}\"" : "";
        return $"- **{rule.Id}** ({rule.Severity.ToString().ToLowerInvariant()}) — {rule.Title}: {rule.Body}{exemplar}";
    }

    private static void AppendGuardrails(StringBuilder sb, AiGroundingContext ctx)
    {
        sb.AppendLine("## Guardrails (STRICT)");
        sb.AppendLine();
        sb.AppendLine("1. Cite rule IDs explicitly in every feedback finding (e.g. \"R03.4\", \"RULE_27\").");
        sb.AppendLine("2. Do NOT invent, rename, or extend rules. If a concern falls outside the rulebook, say so plainly.");
        sb.AppendLine("3. Do NOT produce a numeric grade that contradicts the country-aware scoring table above.");
        sb.AppendLine("4. Do NOT replace expert grading — your output is advisory. Mark it clearly as AI-generated.");
        sb.AppendLine("5. Never request the candidate's OET score from them; derive grades from the rulebook + inputs.");
        sb.AppendLine("6. Be concise, clinical, and direct. No filler praise. No motivational platitudes.");
        sb.AppendLine("7. Use the same tone Dr. Hesham uses: professional, specific, example-driven.");
        sb.AppendLine(ctx.Kind == RuleKind.Speaking
            ? "8. For speaking: respect the 13-stage consultation state machine and the Breaking Bad News 7-step protocol when analysing transcripts."
            : "8. For writing: respect the letter structure order (Address → Date → Salutation → Re: line → Body → Yours sincerely/faithfully → Doctor) and flag layout violations.");
        sb.AppendLine();
    }

    private static void AppendReplyFormat(StringBuilder sb, AiGroundingContext ctx)
    {
        sb.AppendLine("## Reply format");
        sb.AppendLine();
        switch (ctx.Task)
        {
            case AiTaskMode.Score:
                sb.AppendLine("Return a SINGLE JSON object:");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"findings\": [ { \"ruleId\": \"R03.4\", \"severity\": \"critical\", \"quote\": \"...\", \"message\": \"...\", \"fixSuggestion\": \"...\" } ],");
                sb.AppendLine("  \"criteriaScores\": { \"purpose\": 0, \"content\": 0, \"conciseness_clarity\": 0, \"genre_style\": 0, \"organisation_layout\": 0, \"language\": 0 },");
                sb.AppendLine("  \"estimatedScaledScore\": 0,");
                sb.AppendLine("  \"estimatedGrade\": \"B\",");
                sb.AppendLine("  \"passed\": true,");
                sb.AppendLine("  \"passRequires\": { \"scaled\": 0, \"grade\": \"B\" },");
                sb.AppendLine("  \"advisory\": \"AI-generated — pending expert review\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;
            case AiTaskMode.Coach:
                sb.AppendLine("```json");
                sb.AppendLine("{ \"findings\": [...], \"nextBestAction\": \"...\", \"encouragement\": \"...\" }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.Correct:
                sb.AppendLine("```json");
                sb.AppendLine("{ \"findings\": [...], \"revisedText\": \"...\", \"changesSummary\": \"...\" }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GenerateFeedback:
                sb.AppendLine("```json");
                sb.AppendLine("{ \"sections\": [ { \"title\": \"...\", \"bullets\": [\"...\"] } ], \"ruleCitations\": [\"R03.4\"] }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GenerateContent:
                sb.AppendLine("```json");
                sb.AppendLine("{ \"content\": \"...\", \"appliedRuleIds\": [\"R03.4\"], \"selfCheckNotes\": \"...\" }");
                sb.AppendLine("```");
                break;
            default:
                sb.AppendLine("Plain text, concise, ≤ 200 words. Cite rule IDs in parentheses when invoking rules.");
                break;
        }
    }

    private static string RenderTaskInstruction(AiGroundingContext ctx, int passMark, string passGrade)
    {
        var baseText = ctx.Kind == RuleKind.Writing
            ? $"Task: analyse the candidate's OET Writing letter ({ctx.LetterType ?? "letter type TBD"}) against the active rulebook, and produce rule-cited feedback."
            : $"Task: analyse the candidate's OET Speaking transcript ({ctx.CardType ?? "card type TBD"}) against the active rulebook, and produce rule-cited feedback.";
        return $"{baseText} Apply the {passMark}/500 (Grade {passGrade}) pass mark for this {ctx.Kind.ToString().ToLowerInvariant()} call. Respond strictly in the reply format above.";
    }
}

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

public enum AiTaskMode { Score, Coach, Correct, Summarise, GenerateFeedback, GenerateContent }

public sealed class AiGroundingContext
{
    public RuleKind Kind { get; init; }
    public ExamProfession Profession { get; init; } = ExamProfession.Medicine;
    public string? LetterType { get; init; }
    public string? CardType { get; init; }
    public AiTaskMode Task { get; init; } = AiTaskMode.Score;
    public string? CandidateCountry { get; init; }
}

public sealed class AiGroundedPrompt
{
    public string SystemPrompt { get; init; } = "";
    public string TaskInstruction { get; init; } = "";
    public AiGroundedPromptMetadata Metadata { get; init; } = new();
}

public sealed class AiGroundedPromptMetadata
{
    public string RulebookVersion { get; init; } = "";
    public RuleKind RulebookKind { get; init; }
    public ExamProfession Profession { get; init; }
    public int ScoringPassMark { get; init; }
    public string ScoringGrade { get; init; } = "B";
    public int AppliedRulesCount { get; init; }
    public IReadOnlyList<string> AppliedRuleIds { get; init; } = Array.Empty<string>();
}

public sealed class AiGatewayRequest
{
    public AiGroundedPrompt? Prompt { get; init; }
    public string? UserInput { get; init; }
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "mock";
    public double Temperature { get; init; } = 0.2;
    public int? MaxTokens { get; init; }
}

public sealed class AiGatewayResult
{
    public string Completion { get; init; } = "";
    public AiUsage? Usage { get; init; }
    public AiGroundedPromptMetadata Metadata { get; init; } = new();
    public string RulebookVersion { get; init; } = "";
    public IReadOnlyList<string> AppliedRuleIds { get; init; } = Array.Empty<string>();
}

public sealed class AiUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
}

public sealed class PromptNotGroundedException(string message) : InvalidOperationException(message);

// ---------------------------------------------------------------------------
// Provider contract — implement per model vendor
// ---------------------------------------------------------------------------

public interface IAiModelProvider
{
    string Name { get; }
    Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct);
}

public sealed class AiProviderRequest
{
    public string Model { get; init; } = "";
    public string SystemPrompt { get; init; } = "";
    public string UserPrompt { get; init; } = "";
    public double Temperature { get; init; } = 0.2;
    public int? MaxTokens { get; init; }
}

public sealed class AiProviderCompletion
{
    public string Text { get; init; } = "";
    public AiUsage? Usage { get; init; }
}

/// <summary>
/// Default provider — echoes the grounding metadata back without calling any
/// external model. Installed as the DI fallback so every environment has a
/// working gateway even without AI API keys configured. Production pods
/// swap in <c>OpenAiProvider</c> / <c>AnthropicProvider</c> / <c>GeminiProvider</c>
/// at DI registration time.
/// </summary>
public sealed class MockAiProvider : IAiModelProvider
{
    public string Name => "mock";

    public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        var text = "{\"findings\":[],\"advisory\":\"mock AI provider — no external model call was made\"}";
        return Task.FromResult(new AiProviderCompletion { Text = text, Usage = new AiUsage() });
    }
}
