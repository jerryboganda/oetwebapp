using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Grammar AI draft generator. ALWAYS routes through
/// <see cref="IAiGatewayService"/> + <see cref="IAiGatewayService.BuildGroundedPrompt"/>
/// so the rulebook, scoring, and guardrails are inlined. The gateway
/// physically refuses ungrounded prompts. The result is persisted via
/// <see cref="IGrammarAuthoringService"/> as a Draft lesson for admin
/// review. Drafts never auto-publish.
/// </summary>
public interface IGrammarDraftGenerator
{
    Task<AdminGrammarAiDraftResponse> GenerateAsync(
        string adminId, string adminName, AdminGrammarAiDraftRequest req, CancellationToken ct);
}

public sealed class GrammarDraftGenerator(
    IAiGatewayService gateway,
    IGrammarAuthoringService authoring,
    IGrammarPolicyService policyService,
    ILogger<GrammarDraftGenerator> logger) : IGrammarDraftGenerator
{
    public async Task<AdminGrammarAiDraftResponse> GenerateAsync(
        string adminId, string adminName, AdminGrammarAiDraftRequest req, CancellationToken ct)
    {
        var policy = await policyService.GetEffectiveAsync(req.ExamTypeCode ?? "oet", ct);
        if (!policy.AiDraftEnabled)
            throw ApiException.Forbidden("GRAMMAR_AI_DISABLED", "Grammar AI draft generation is disabled by policy.");

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing, // Grammar rules live with Writing
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateContent,
            CandidateCountry = "UK",
        });

        var targetCount = Math.Clamp(req.TargetExerciseCount ?? 6, 3, 12);
        var instruction = $@"Generate an OET Grammar lesson draft in strict JSON.
Topic slug: {req.TopicSlug ?? "unspecified"}
Exam: {req.ExamTypeCode}
Target level: {req.Level ?? "intermediate"}
Target exercise count: {targetCount}

Admin prompt:
{req.Prompt}

Output a single JSON object with the following shape:
{{
  ""title"": string (concise),
  ""description"": string (<= 200 chars),
  ""level"": ""beginner""|""intermediate""|""advanced"",
  ""estimatedMinutes"": int (5..30),
  ""category"": string (slug),
  ""contentBlocks"": [
     {{ ""type"": ""prose""|""callout""|""example""|""note"", ""contentMarkdown"": string }}
  ],
  ""exercises"": [
     {{
        ""type"": ""mcq""|""fill_blank""|""error_correction""|""sentence_transformation""|""matching"",
        ""promptMarkdown"": string,
        ""options"": [{{ ""id"":""a"", ""label"":""...""}}] | [],
        ""correctAnswer"": string | string[] | [{{""left"":""..."", ""right"":""...""}}],
        ""acceptedAnswers"": string[] (optional synonyms),
        ""explanationMarkdown"": string,
        ""difficulty"": ""beginner""|""intermediate""|""advanced"",
        ""points"": int (1..3)
     }}
  ],
  ""sourceProvenance"": string (cite the pedagogical basis, e.g. ""OET Medicine Rulebook R12.* grammar series""),
  ""selfCheckNotes"": string
}}

Rules:
- Use OET-medicine-relevant example sentences (patient handovers, referral letters, clinical notes).
- All exercises must have non-empty correctAnswer and explanationMarkdown.
- Do NOT include TODO or TBD placeholders.
- Keep prose blocks ≤ 400 words each.
- Prefer short, direct phrasing (Dr Hesham tone).";

        AiGatewayResult result;
        try
        {
            result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = instruction,
                Model = string.IsNullOrWhiteSpace(policy.AiDraftModel) ? (req.Model ?? "mock") : policy.AiDraftModel,
                Temperature = 0.4,
                MaxTokens = 3500,
                UserId = adminId,
                FeatureCode = AiFeatureCodes.GrammarDraft,
            }, ct);
        }
        catch (PromptNotGroundedException ex)
        {
            logger.LogError(ex, "Grammar AI draft refused: ungrounded prompt");
            throw;
        }

        var parsed = ParseDraft(result.Completion);
        if (parsed is null)
        {
            // Fall back to a stub draft admins can edit.
            logger.LogWarning("Grammar AI draft returned non-JSON; creating stub draft for admin");
            parsed = BuildStubDraft(req);
        }

        var create = new AdminGrammarLessonFullCreateRequest(
            ExamTypeCode: req.ExamTypeCode ?? "oet",
            TopicId: null,
            Title: parsed.Title,
            Description: parsed.Description,
            Level: parsed.Level ?? "intermediate",
            Category: parsed.Category ?? (req.TopicSlug ?? "general"),
            EstimatedMinutes: parsed.EstimatedMinutes,
            SortOrder: 0,
            PrerequisiteLessonId: null,
            PrerequisiteLessonIds: null,
            SourceProvenance: parsed.SourceProvenance ?? "AI-drafted via grounded gateway — requires admin review",
            ContentBlocks: parsed.ContentBlocks,
            Exercises: parsed.Exercises);

        var lessonId = await authoring.CreateLessonAsync(adminId, adminName, create, ct);

        return new AdminGrammarAiDraftResponse(
            LessonId: lessonId,
            Title: parsed.Title,
            Status: "draft",
            ContentBlockCount: parsed.ContentBlocks?.Count ?? 0,
            ExerciseCount: parsed.Exercises?.Count ?? 0,
            Warning: parsed.Warning);
    }

    // ── Parser ──────────────────────────────────────────────────────────

    private sealed class ParsedDraft
    {
        public string Title { get; set; } = "Untitled Grammar Draft";
        public string? Description { get; set; }
        public string? Level { get; set; }
        public int? EstimatedMinutes { get; set; }
        public string? Category { get; set; }
        public string? SourceProvenance { get; set; }
        public List<AdminGrammarContentBlockDto>? ContentBlocks { get; set; }
        public List<AdminGrammarExerciseDto>? Exercises { get; set; }
        public string? Warning { get; set; }
    }

    private static ParsedDraft? ParseDraft(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;

        // Strip fenced ```json ... ``` markers if present.
        var stripped = completion.Trim();
        if (stripped.StartsWith("```"))
        {
            var firstNewline = stripped.IndexOf('\n');
            if (firstNewline > 0) stripped = stripped[(firstNewline + 1)..];
            var closingFence = stripped.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence > 0) stripped = stripped[..closingFence];
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(stripped); }
        catch { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var draft = new ParsedDraft();
            if (root.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                draft.Title = titleEl.GetString() ?? draft.Title;
            if (root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
                draft.Description = descEl.GetString();
            if (root.TryGetProperty("level", out var lvlEl) && lvlEl.ValueKind == JsonValueKind.String)
                draft.Level = lvlEl.GetString();
            if (root.TryGetProperty("estimatedMinutes", out var estEl) && estEl.ValueKind == JsonValueKind.Number)
                draft.EstimatedMinutes = estEl.GetInt32();
            if (root.TryGetProperty("category", out var catEl) && catEl.ValueKind == JsonValueKind.String)
                draft.Category = catEl.GetString();
            if (root.TryGetProperty("sourceProvenance", out var provEl) && provEl.ValueKind == JsonValueKind.String)
                draft.SourceProvenance = provEl.GetString();

            if (root.TryGetProperty("contentBlocks", out var blocksEl) && blocksEl.ValueKind == JsonValueKind.Array)
            {
                var list = new List<AdminGrammarContentBlockDto>();
                int i = 0;
                foreach (var b in blocksEl.EnumerateArray())
                {
                    if (b.ValueKind != JsonValueKind.Object) continue;
                    var type = b.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "prose" : "prose";
                    var md = b.TryGetProperty("contentMarkdown", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                    list.Add(new AdminGrammarContentBlockDto(null, ++i, type, md, null));
                }
                draft.ContentBlocks = list;
            }

            if (root.TryGetProperty("exercises", out var exEl) && exEl.ValueKind == JsonValueKind.Array)
            {
                var list = new List<AdminGrammarExerciseDto>();
                int i = 0;
                foreach (var e in exEl.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var type = e.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "mcq" : "mcq";
                    var prompt = e.TryGetProperty("promptMarkdown", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
                    JsonElement? options = e.TryGetProperty("options", out var o) ? o.Clone() : null;
                    JsonElement correct = e.TryGetProperty("correctAnswer", out var c) ? c.Clone() : default;
                    JsonElement? accepted = e.TryGetProperty("acceptedAnswers", out var a) ? a.Clone() : null;
                    var explain = e.TryGetProperty("explanationMarkdown", out var ex2) && ex2.ValueKind == JsonValueKind.String ? ex2.GetString() ?? "" : "";
                    var diff = e.TryGetProperty("difficulty", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "intermediate" : "intermediate";
                    var pts = e.TryGetProperty("points", out var pp) && pp.ValueKind == JsonValueKind.Number ? pp.GetInt32() : 1;
                    list.Add(new AdminGrammarExerciseDto(null, ++i, type, prompt, options, correct, accepted, explain, diff, pts));
                }
                draft.Exercises = list;
            }

            return draft;
        }
    }

    private static ParsedDraft BuildStubDraft(AdminGrammarAiDraftRequest req)
    {
        return new ParsedDraft
        {
            Title = $"Grammar draft: {req.TopicSlug ?? "general"}",
            Description = "AI draft could not be parsed. Please edit the content blocks and exercises.",
            Level = req.Level ?? "intermediate",
            EstimatedMinutes = 10,
            Category = req.TopicSlug ?? "general",
            SourceProvenance = "AI-drafted (stub fallback) — requires full admin authoring",
            ContentBlocks = new List<AdminGrammarContentBlockDto>
            {
                new(null, 1, "prose", $"Lesson seed: {req.Prompt}", null),
            },
            Exercises = new List<AdminGrammarExerciseDto>(),
            Warning = "Draft was not parsable as JSON — please replace stub content and exercises before publishing.",
        };
    }
}
