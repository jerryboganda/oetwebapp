using OetLearner.Api.Domain;
using OetLearner.Api.Prompts.Writing;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// In-process registry of Writing Module V2 prompt templates. Every Writing V2
/// AI call resolves its template through this registry so model id, cache
/// strategy, temperature, max tokens and structured-output JSON schema live in
/// ONE place per feature code rather than scattered across services.
///
/// Per AGENTS.md, ALL production AI calls flow through
/// <see cref="IAiGatewayService"/> with grounded prompts — this registry holds
/// the per-template metadata services need to populate
/// <see cref="AiGatewayRequest"/> consistently. Callers are expected to:
///
/// <list type="number">
///   <item>Resolve the template via <c>GetTemplate(featureCode)</c>.</item>
///   <item>Build the grounded prompt with <c>IAiGatewayService.BuildGroundedPrompt</c>
///         (the gateway then appends the template's <c>SystemPrompt</c>
///         segment via the prompt builder or the service composes it inline,
///         depending on the call site — see WS5 services).</item>
///   <item>Call <c>IAiGatewayService.CompleteAsync</c> with
///         <see cref="AiGatewayRequest.PromptTemplateId"/> set to the
///         registry id and <see cref="AiGatewayRequest.Temperature"/> /
///         <see cref="AiGatewayRequest.MaxTokens"/> populated from the
///         template metadata.</item>
/// </list>
///
/// The registry throws at startup if any template fails to register
/// (<see cref="WritingPromptTemplateRegistrar.RegisterWritingV2Templates"/>),
/// so prompt-template misconfig fails fast at boot rather than at first
/// request — see the fail-fast call in <c>Program.cs</c>.
/// </summary>
public interface IWritingPromptTemplateRegistry
{
    /// <summary>Resolve a registered template by its canonical feature code.
    /// Throws <see cref="InvalidOperationException"/> if no template exists.</summary>
    WritingPromptTemplate GetTemplate(string featureCode);

    /// <summary>Try-pattern variant; returns <c>false</c> when the feature code
    /// has no registered template instead of throwing.</summary>
    bool TryGetTemplate(string featureCode, out WritingPromptTemplate template);

    /// <summary>All registered template feature codes — used by the boot-time
    /// fail-fast guard in <see cref="WritingPromptTemplateRegistrar"/> and by
    /// admin /v1/ai/templates listings.</summary>
    IReadOnlyList<string> RegisteredFeatureCodes { get; }

    /// <summary>Internal — used by <see cref="WritingPromptTemplateRegistrar"/>
    /// at startup to populate the registry. Throws if a template is added
    /// twice with the same feature code (catches duplicate registrar bugs).</summary>
    void Register(WritingPromptTemplate template);
}

/// <summary>
/// Single template definition. Immutable record so registrations are safe to
/// share across scoped DI consumers (the registry itself is a singleton).
/// </summary>
public sealed record WritingPromptTemplate
{
    /// <summary>Canonical feature code (matches a constant in
    /// <see cref="AiFeatureCodes"/>). Used as the registry key.</summary>
    public required string FeatureCode { get; init; }

    /// <summary>Display id (e.g. <c>writing.coach.v1</c>) — stamped onto
    /// <c>AiGatewayRequest.PromptTemplateId</c> by the calling service so the
    /// AI usage explorer can correlate cost/quality with prompt version.
    /// Always identical to <see cref="FeatureCode"/> in v1 of the registry —
    /// kept separate so future templates can ship multiple versions per
    /// feature (A/B rollouts).</summary>
    public required string TemplateId { get; init; }

    /// <summary>Model identifier handed to the provider. Per spec §12.2 we
    /// pin Sonnet 4.6 for grading-adjacent + rewrite + scenario generation;
    /// Haiku 4.5 for coach + canon detect + drill grade + outline + paraphrase
    /// + ask; GPT-5.5 medium for appeal; text-embedding-3-small for exemplar
    /// embed. The actual model id string is resolved by the AI provider
    /// registry — leaving the string empty here lets the gateway fall back to
    /// the active feature route's default model.</summary>
    public required string Model { get; init; }

    /// <summary>Verbatim system-prompt body. Kept as a constant in
    /// <see cref="WritingPromptTemplates"/>.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>Sampling temperature — 0.2 for deterministic grading-style
    /// templates (coach, grading, canon detect, drill grade, ask, outline);
    /// 0.7 for creative templates (rewrite, scenario generation, paraphrase).</summary>
    public required double Temperature { get; init; }

    /// <summary>Hard cap on output tokens per call.</summary>
    public required int MaxOutputTokens { get; init; }

    /// <summary>Hint at the maximum input tokens this template expects. Used
    /// only for capacity planning — providers ignore this; the request is
    /// truncated by the gateway upstream.</summary>
    public required int MaxInputTokens { get; init; }

    /// <summary>Cache strategy for the system prompt segment.
    /// <c>ephemeral_60min</c> = Anthropic ephemeral cache with a 60-min TTL
    /// (used for templates that ship a large cached header — coach + canon
    /// detect + appeal); <c>none</c> = no cache directive (one-shot calls).</summary>
    public required string CacheStrategy { get; init; }

    /// <summary>Structured-output JSON schema hint for the calling service.
    /// The string here is a human-readable shape descriptor (NOT a JSON Schema
    /// document — that lives in the service that parses the response). Acts
    /// as a single source of truth so any drift between prompt + parser shows
    /// up in code review.</summary>
    public required string OutputSchema { get; init; }

    /// <summary>Whether the call is multi-turn (chat-style — only
    /// <c>writing.ask.v1</c> in v1 of the registry). Multi-turn templates
    /// require the caller to thread the conversation history into
    /// <c>AiGatewayRequest.UserInput</c> or via the Phase 5 messages array.</summary>
    public bool MultiTurn { get; init; }
}

/// <summary>
/// Singleton implementation backed by a case-insensitive dictionary.
/// Registration is one-shot from <see cref="WritingPromptTemplateRegistrar"/>
/// at boot; runtime mutation is not supported (templates are an immutable
/// part of the deployed code).
/// </summary>
public sealed class WritingPromptTemplateRegistry : IWritingPromptTemplateRegistry
{
    private readonly Dictionary<string, WritingPromptTemplate> _byFeatureCode =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> RegisteredFeatureCodes => _byFeatureCode.Keys.ToList();

    public WritingPromptTemplate GetTemplate(string featureCode)
    {
        if (_byFeatureCode.TryGetValue(featureCode, out var template))
        {
            return template;
        }

        throw new InvalidOperationException(
            $"No Writing prompt template registered for feature code '{featureCode}'. "
            + $"Call WritingPromptTemplateRegistrar.RegisterWritingV2Templates at startup. "
            + $"Registered codes: {string.Join(", ", RegisteredFeatureCodes)}.");
    }

    public bool TryGetTemplate(string featureCode, out WritingPromptTemplate template)
    {
        if (_byFeatureCode.TryGetValue(featureCode, out var found))
        {
            template = found;
            return true;
        }

        template = null!;
        return false;
    }

    public void Register(WritingPromptTemplate template)
    {
        if (_byFeatureCode.ContainsKey(template.FeatureCode))
        {
            throw new InvalidOperationException(
                $"Duplicate Writing prompt template registration for feature code "
                + $"'{template.FeatureCode}'. Each feature code may only register once "
                + $"per boot. Check WritingPromptTemplateRegistrar for double-registration bugs.");
        }

        _byFeatureCode[template.FeatureCode] = template;
    }
}

/// <summary>
/// Boot-time registrar — called from <c>Program.cs</c> next to the existing AI
/// gateway bootstrap. Registers all 10 Writing Module V2 templates and then
/// asserts every expected feature code resolves through the registry. A
/// missing template throws at startup so misconfig surfaces at boot, not at
/// the first learner-visible request.
/// </summary>
public static class WritingPromptTemplateRegistrar
{
    /// <summary>Feature codes the registrar guarantees to register at boot.
    /// Kept as a static list so the fail-fast guard below can assert each
    /// code resolves through the registry post-registration.</summary>
    public static readonly IReadOnlyList<string> ExpectedFeatureCodes = new[]
    {
        AiFeatureCodes.WritingCoachV1,
        AiFeatureCodes.WritingRewriteV1,
        AiFeatureCodes.WritingScenarioGenerateV1,
        AiFeatureCodes.WritingExemplarEmbedV1,
        AiFeatureCodes.WritingAppealV1,
        AiFeatureCodes.WritingCanonDetectV1,
        AiFeatureCodes.WritingDrillGradeV1,
        AiFeatureCodes.WritingOutlineV1,
        AiFeatureCodes.WritingParaphraseV1,
        AiFeatureCodes.WritingAskV1,
    };

    /// <summary>Register all 10 Writing Module V2 prompt templates with the
    /// supplied registry. Throws <see cref="InvalidOperationException"/> if
    /// any template fails to register or if a post-registration probe fails
    /// to resolve an expected feature code.</summary>
    public static void RegisterWritingV2Templates(IWritingPromptTemplateRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingCoachV1,
            TemplateId = "writing.coach.v1",
            Model = "claude-haiku-4-5",
            SystemPrompt = WritingPromptTemplates.CoachV1,
            Temperature = 0.2,
            MaxInputTokens = 8_000,
            MaxOutputTokens = 250,
            CacheStrategy = "ephemeral_60min",
            OutputSchema = """
                { "hints": [
                    { "category": "style|structure|length|encouragement",
                      "text": "≤12 words",
                      "ruleId": "SC-xxx (optional)",
                      "charStart": "int (optional)",
                      "charEnd": "int (optional)" }
                  ] }
                Hard cap: ≤4 hints per response.
                """,
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingRewriteV1,
            TemplateId = "writing.rewrite.v1",
            Model = "claude-sonnet-4-6",
            SystemPrompt = WritingPromptTemplates.RewriteV1,
            Temperature = 0.7,
            MaxInputTokens = 8_000,
            MaxOutputTokens = 1_500,
            CacheStrategy = "none",
            OutputSchema = "plain text — the rewritten letter (no JSON wrapper, paragraph breaks preserved).",
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingScenarioGenerateV1,
            TemplateId = "writing.scenario.generate.v1",
            Model = "claude-sonnet-4-6",
            SystemPrompt = WritingPromptTemplates.ScenarioGenerateV1,
            Temperature = 0.7,
            MaxInputTokens = 4_000,
            MaxOutputTokens = 2_000,
            CacheStrategy = "none",
            OutputSchema = """
                { "title", "letter_type", "profession", "sub_discipline?",
                  "topics": [], "difficulty": int,
                  "case_notes_markdown": string,
                  "case_notes_structured": [
                    { "sentence": string, "relevance": "relevant|maybe|irrelevant" }
                  ],
                  "suggested_recipient": string,
                  "suggested_purpose": string }
                """,
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingExemplarEmbedV1,
            TemplateId = "writing.exemplar.embed.v1",
            Model = "text-embedding-3-small",
            SystemPrompt = WritingPromptTemplates.ExemplarEmbedV1,
            Temperature = 0.0,
            MaxInputTokens = 8_000,
            MaxOutputTokens = 0,
            CacheStrategy = "none",
            OutputSchema = "float[1536] vector returned by the embedding provider (no completion text).",
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingAppealV1,
            TemplateId = "writing.appeal.v1",
            Model = "gpt-5.5-medium",
            SystemPrompt = WritingPromptTemplates.AppealV1,
            Temperature = 0.2,
            MaxInputTokens = 12_000,
            MaxOutputTokens = 800,
            CacheStrategy = "ephemeral_60min",
            OutputSchema = """
                { "c1": 0-3, "c2": 0-7, "c3": 0-7, "c4": 0-7, "c5": 0-7, "c6": 0-7,
                  "rawTotal": int,
                  "estimatedBand": "A|B|C+|C|D|E",
                  "rationale": "why your scores differ (if they do)" }
                """,
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingCanonDetectV1,
            TemplateId = "writing.canon.detect.v1",
            Model = "claude-haiku-4-5",
            SystemPrompt = WritingPromptTemplates.CanonDetectV1,
            Temperature = 0.2,
            MaxInputTokens = 8_000,
            MaxOutputTokens = 500,
            CacheStrategy = "ephemeral_60min",
            OutputSchema = """
                { "violations": [
                    { "char_start": int, "char_end": int,
                      "snippet": string, "suggested_fix": string }
                  ] }
                Empty array means no violations.
                """,
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingDrillGradeV1,
            TemplateId = "writing.drill.grade.v1",
            Model = "claude-haiku-4-5",
            SystemPrompt = WritingPromptTemplates.DrillGradeV1,
            Temperature = 0.2,
            MaxInputTokens = 2_000,
            MaxOutputTokens = 100,
            CacheStrategy = "none",
            OutputSchema = """
                { "correct": bool, "feedback": "≤10 words" }
                No partial credit; correct=false ⇒ feedback explains the single biggest gap.
                """,
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingOutlineV1,
            TemplateId = "writing.outline.v1",
            Model = "claude-haiku-4-5",
            SystemPrompt = WritingPromptTemplates.OutlineV1,
            Temperature = 0.2,
            MaxInputTokens = 4_000,
            MaxOutputTokens = 600,
            CacheStrategy = "none",
            OutputSchema = """
                { "opening": string,
                  "body_paragraphs": [ { "topic": string, "content_points": [string] } ],
                  "closing": string,
                  "suggested_length_words": int }
                """,
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingParaphraseV1,
            TemplateId = "writing.paraphrase.v1",
            Model = "claude-haiku-4-5",
            SystemPrompt = WritingPromptTemplates.ParaphraseV1,
            Temperature = 0.7,
            MaxInputTokens = 1_000,
            MaxOutputTokens = 300,
            CacheStrategy = "none",
            OutputSchema = """
                { "alternatives": [
                    { "level": "clinical|professional|formal", "text": string }
                  ] }
                Exactly 3 alternatives, one per formality level.
                """,
        });

        registry.Register(new WritingPromptTemplate
        {
            FeatureCode = AiFeatureCodes.WritingAskV1,
            TemplateId = "writing.ask.v1",
            Model = "claude-haiku-4-5",
            SystemPrompt = WritingPromptTemplates.AskV1,
            Temperature = 0.2,
            MaxInputTokens = 8_000,
            MaxOutputTokens = 250,
            CacheStrategy = "none",
            MultiTurn = true,
            OutputSchema = "plain text — concise tutor answer ≤80 words, no JSON wrapper.",
        });

        AssertAllExpectedTemplatesRegistered(registry);
    }

    /// <summary>Fail-fast guard — verifies every expected feature code resolves
    /// through the registry after <see cref="RegisterWritingV2Templates"/>
    /// runs. Throws <see cref="InvalidOperationException"/> with the full
    /// missing-code list so prompt-template misconfig is caught at boot, not
    /// at the first learner-visible request.</summary>
    private static void AssertAllExpectedTemplatesRegistered(IWritingPromptTemplateRegistry registry)
    {
        var missing = new List<string>();
        foreach (var code in ExpectedFeatureCodes)
        {
            if (!registry.TryGetTemplate(code, out _))
            {
                missing.Add(code);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "WritingPromptTemplateRegistrar failed to register the following expected "
                + $"feature codes: {string.Join(", ", missing)}. This is a configuration bug "
                + "— add the missing Register() call to RegisterWritingV2Templates.");
        }
    }
}
