using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

// ============================================================================
// Rewrite
// ============================================================================

public sealed record WritingRewriteResult(string OriginalText, string RewrittenText, IReadOnlyList<WritingDiffSegment> Diff);

public sealed record WritingDiffSegment(string Kind, string Text);

public interface IWritingRewriteService
{
    Task<WritingRewriteResult> RewriteAsync(string userId, string originalText, string? letterType, string? profession, CancellationToken ct);
    Task<WritingRewriteResultResponse> RewriteAsync(string userId, WritingRewriteRequest request, CancellationToken ct);
}

public sealed class WritingRewriteService(IAiGatewayService aiGateway) : IWritingRewriteService
{
    public async Task<WritingRewriteResultResponse> RewriteAsync(string userId, WritingRewriteRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await RewriteAsync(userId, request.Text, request.LetterType, request.Profession, ct);
        return new WritingRewriteResultResponse(
            OriginalText: result.OriginalText,
            RewrittenText: result.RewrittenText,
            Diff: result.Diff.Select(s => new WritingDiffSegmentResponse(s.Kind, s.Text)).ToList());
    }

    public async Task<WritingRewriteResult> RewriteAsync(string userId, string originalText, string? letterType, string? profession, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(originalText))
            throw ApiException.Validation("writing_rewrite_empty", "Original text is required.");
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            LetterType = letterType ?? "routine_referral",
            Task = AiTaskMode.Correct,
        });
        var result = await aiGateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = $"Rewrite preserving every clinical fact and the original recipient. Keep paragraph breaks.\n---\n{originalText}\n---",
            Temperature = 0.7,
            FeatureCode = AiFeatureCodes.WritingRewriteV1,
            PromptTemplateId = "writing.rewrite.v1",
            UserId = userId,
        }, ct);
        var rewritten = (result.Completion ?? string.Empty).Trim();
        var diff = WritingTextDiff.Compute(originalText, rewritten);
        return new WritingRewriteResult(originalText, rewritten, diff);
    }
}

// ============================================================================
// Paraphrase
// ============================================================================

public sealed record WritingParaphraseAlternative(string Formality, string Text);

public sealed record WritingParaphraseResult(string OriginalText, IReadOnlyList<WritingParaphraseAlternative> Alternatives);

public interface IWritingParaphraseService
{
    Task<WritingParaphraseResult> ParaphraseAsync(string userId, string originalText, CancellationToken ct);
    Task<WritingParaphraseResultResponse> ParaphraseAsync(string userId, WritingParaphraseRequest request, CancellationToken ct);
}

public sealed class WritingParaphraseService(IAiGatewayService aiGateway) : IWritingParaphraseService
{
    public async Task<WritingParaphraseResultResponse> ParaphraseAsync(string userId, WritingParaphraseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await ParaphraseAsync(userId, request.Text, ct);
        return new WritingParaphraseResultResponse(
            OriginalText: result.OriginalText,
            Alternatives: result.Alternatives.Select(a => new WritingParaphraseAlternativeResponse(a.Formality, a.Text)).ToList());
    }

    public async Task<WritingParaphraseResult> ParaphraseAsync(string userId, string originalText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(originalText))
            throw ApiException.Validation("writing_paraphrase_empty", "Original text is required.");
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Task = AiTaskMode.GenerateContent,
        });
        var result = await aiGateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = $"Provide 3 alternatives for this sentence at 'clinical', 'professional', and 'formal' formality levels. Return JSON {{ \"alternatives\": [{{ \"level\": string, \"text\": string }}] }}.\n---\n{originalText}\n---",
            Temperature = 0.7,
            FeatureCode = AiFeatureCodes.WritingParaphraseV1,
            PromptTemplateId = "writing.paraphrase.v1",
            UserId = userId,
        }, ct);
        var alts = Parse(result.Completion);
        return new WritingParaphraseResult(originalText, alts);
    }

    private static IReadOnlyList<WritingParaphraseAlternative> Parse(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return Array.Empty<WritingParaphraseAlternative>();
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return Array.Empty<WritingParaphraseAlternative>();
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("alternatives", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<WritingParaphraseAlternative>();
            var alts = new List<WritingParaphraseAlternative>();
            foreach (var a in arr.EnumerateArray())
            {
                var level = a.TryGetProperty("level", out var lEl) && lEl.ValueKind == JsonValueKind.String ? lEl.GetString() ?? "professional" : "professional";
                var text = a.TryGetProperty("text", out var tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(text)) continue;
                alts.Add(new WritingParaphraseAlternative(level, text));
            }
            return alts.Take(3).ToList();
        }
        catch (JsonException) { return Array.Empty<WritingParaphraseAlternative>(); }
    }
}

// ============================================================================
// Ask (multi-turn — state stays client-side)
// ============================================================================

public sealed record WritingAskMessage(string Role, string Content);

public sealed record WritingAskInternalRequest(string ThreadId, IReadOnlyList<WritingAskMessage> History, string LearnerMessage, string? LetterContext);

public sealed record WritingAskResult(string ThreadId, WritingAskMessage Reply);

public interface IWritingAskService
{
    Task<WritingAskResult> AskAsync(string userId, WritingAskInternalRequest request, CancellationToken ct);
    Task<WritingAskTurnResponse> AskAsync(string userId, WritingAskRequest request, CancellationToken ct);
}

public sealed class WritingAskService(IAiGatewayService aiGateway) : IWritingAskService
{
    public async Task<WritingAskTurnResponse> AskAsync(string userId, WritingAskRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var internalReq = new WritingAskInternalRequest(
            ThreadId: request.ThreadId ?? string.Empty,
            History: Array.Empty<WritingAskMessage>(),
            LearnerMessage: request.Question,
            LetterContext: request.LetterContent);
        var result = await AskAsync(userId, internalReq, ct);
        return new WritingAskTurnResponse(
            ThreadId: result.ThreadId,
            Reply: new WritingAskMessageResponse(result.Reply.Role, result.Reply.Content, DateTimeOffset.UtcNow));
    }

    public async Task<WritingAskResult> AskAsync(string userId, WritingAskInternalRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LearnerMessage))
            throw ApiException.Validation("writing_ask_empty", "Question is required.");
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Task = AiTaskMode.Coach,
        });
        var historyText = string.Join('\n', (request.History ?? Array.Empty<WritingAskMessage>())
            .TakeLast(10)
            .Select(m => $"{m.Role}: {m.Content}"));
        var userInput = string.Join('\n',
            string.IsNullOrWhiteSpace(request.LetterContext) ? null : $"Letter context:\n{request.LetterContext}",
            string.IsNullOrWhiteSpace(historyText) ? null : $"Conversation so far:\n{historyText}",
            $"learner: {request.LearnerMessage}",
            "Respond concisely (≤80 words) as a Writing tutor.");
        var result = await aiGateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = userInput,
            Temperature = 0.2,
            FeatureCode = AiFeatureCodes.WritingAskV1,
            PromptTemplateId = "writing.ask.v1",
            UserId = userId,
        }, ct);
        var threadId = string.IsNullOrWhiteSpace(request.ThreadId) ? Guid.NewGuid().ToString("N") : request.ThreadId;
        return new WritingAskResult(threadId, new WritingAskMessage("coach", (result.Completion ?? string.Empty).Trim()));
    }
}

// ============================================================================
// Outline
// ============================================================================

public sealed record WritingOutlineParagraph(int Paragraph, string Purpose, IReadOnlyList<string> ContentPoints);

public sealed record WritingOutlineResult(Guid ScenarioId, string Opening, IReadOnlyList<WritingOutlineParagraph> BodyParagraphs, string Closing, int SuggestedLengthWords);

public interface IWritingOutlineService
{
    Task<WritingOutlineResult> OutlineAsync(string userId, Guid scenarioId, string caseNotes, string? letterType, CancellationToken ct);
    Task<WritingOutlineResultResponse> GenerateOutlineAsync(string userId, WritingOutlineRequest request, CancellationToken ct);
}

public sealed class WritingOutlineService(IAiGatewayService aiGateway, OetLearner.Api.Data.LearnerDbContext db) : IWritingOutlineService
{
    public async Task<WritingOutlineResultResponse> GenerateOutlineAsync(string userId, WritingOutlineRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scenario = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.WritingScenarios.AsNoTracking(), s => s.Id == request.ScenarioId, ct);
        if (scenario is null)
        {
            throw ApiException.NotFound("writing_scenario_not_found", "Scenario was not found.");
        }
        var result = await OutlineAsync(userId, request.ScenarioId, scenario.CaseNotesMarkdown, request.LetterType, ct);
        var paragraphs = new List<WritingOutlineParagraphResponse>();
        var openingPara = string.IsNullOrWhiteSpace(result.Opening) ? null : new WritingOutlineParagraphResponse(1, "Opening", new[] { result.Opening });
        if (openingPara is not null) paragraphs.Add(openingPara);
        foreach (var bp in result.BodyParagraphs)
        {
            paragraphs.Add(new WritingOutlineParagraphResponse(bp.Paragraph + (openingPara is null ? 0 : 1), bp.Purpose, bp.ContentPoints));
        }
        if (!string.IsNullOrWhiteSpace(result.Closing))
        {
            paragraphs.Add(new WritingOutlineParagraphResponse(paragraphs.Count + 1, "Closing", new[] { result.Closing }));
        }
        return new WritingOutlineResultResponse(request.ScenarioId, paragraphs);
    }

    public async Task<WritingOutlineResult> OutlineAsync(string userId, Guid scenarioId, string caseNotes, string? letterType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(caseNotes))
            throw ApiException.Validation("writing_outline_no_notes", "Case notes are required to generate an outline.");
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            LetterType = letterType ?? "routine_referral",
            Task = AiTaskMode.GenerateContent,
        });
        var result = await aiGateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = $"Build a structured outline for an OET letter from these case notes. Return JSON {{ \"opening\": string, \"body_paragraphs\": [{{ \"topic\": string, \"content_points\": [string] }}], \"closing\": string, \"suggested_length_words\": int }}.\nCase notes:\n---\n{caseNotes}\n---",
            Temperature = 0.2,
            FeatureCode = AiFeatureCodes.WritingOutlineV1,
            PromptTemplateId = "writing.outline.v1",
            UserId = userId,
        }, ct);
        return Parse(scenarioId, result.Completion);
    }

    private static WritingOutlineResult Parse(Guid scenarioId, string completion)
    {
        var fallback = new WritingOutlineResult(scenarioId, string.Empty, Array.Empty<WritingOutlineParagraph>(), string.Empty, 200);
        if (string.IsNullOrWhiteSpace(completion)) return fallback;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            var opening = doc.RootElement.TryGetProperty("opening", out var oEl) && oEl.ValueKind == JsonValueKind.String ? oEl.GetString() ?? string.Empty : string.Empty;
            var closing = doc.RootElement.TryGetProperty("closing", out var cEl) && cEl.ValueKind == JsonValueKind.String ? cEl.GetString() ?? string.Empty : string.Empty;
            var length = doc.RootElement.TryGetProperty("suggested_length_words", out var lEl) && lEl.TryGetInt32(out var lv) ? lv : 200;
            var paragraphs = new List<WritingOutlineParagraph>();
            if (doc.RootElement.TryGetProperty("body_paragraphs", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var i = 1;
                foreach (var p in arr.EnumerateArray())
                {
                    var topic = p.TryGetProperty("topic", out var tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString() ?? string.Empty : string.Empty;
                    var points = new List<string>();
                    if (p.TryGetProperty("content_points", out var cpArr) && cpArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cp in cpArr.EnumerateArray())
                        {
                            if (cp.ValueKind == JsonValueKind.String)
                            {
                                var s = cp.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) points.Add(s!);
                            }
                        }
                    }
                    paragraphs.Add(new WritingOutlineParagraph(i++, topic, points));
                }
            }
            return new WritingOutlineResult(scenarioId, opening, paragraphs, closing, length);
        }
        catch (JsonException) { return fallback; }
    }
}

// ============================================================================
// Scenario generator (admin)
// ============================================================================

public sealed record WritingScenarioGenerateInternalRequest(string Profession, string LetterType, string? SubDiscipline, string Brief, int Difficulty);

public sealed record WritingScenarioGenerateResult(string DraftJson);

public interface IWritingScenarioGeneratorService
{
    Task<WritingScenarioGenerateResult> GenerateAsync(string adminId, WritingScenarioGenerateInternalRequest request, CancellationToken ct);
    Task<WritingScenarioResponse> GenerateScenarioAsync(string adminId, WritingScenarioGenerateRequest request, CancellationToken ct);
}

public sealed class WritingScenarioGeneratorService(IAiGatewayService aiGateway, IWritingScenarioService scenarioService) : IWritingScenarioGeneratorService
{
    public async Task<WritingScenarioResponse> GenerateScenarioAsync(string adminId, WritingScenarioGenerateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var brief = string.IsNullOrWhiteSpace(request.Instructions)
            ? (request.Topic ?? "Generate a realistic OET scenario.")
            : request.Instructions!;
        var internalReq = new WritingScenarioGenerateInternalRequest(
            Profession: request.Profession,
            LetterType: request.LetterType,
            SubDiscipline: null,
            Brief: brief,
            Difficulty: request.Difficulty);
        var result = await GenerateAsync(adminId, internalReq, ct);
        // Persist as a draft scenario so admins can review/publish through the
        // existing scenario CRUD flow. If parsing the AI JSON fails the caller
        // gets a stub draft they can fill in.
        var draft = ParseDraftToUpsert(result.DraftJson, request);
        var created = await scenarioService.AdminCreateScenarioAsync(adminId, draft, ct);
        return created;
    }

    private static WritingScenarioUpsertRequest ParseDraftToUpsert(string draftJson, WritingScenarioGenerateRequest request)
    {
        try
        {
            using var doc = JsonDocument.Parse(draftJson);
            var title = doc.RootElement.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString() ?? "AI scenario draft" : "AI scenario draft";
            var caseNotes = doc.RootElement.TryGetProperty("case_notes_markdown", out var cEl) && cEl.ValueKind == JsonValueKind.String ? cEl.GetString() ?? string.Empty : string.Empty;
            var topics = new List<string>();
            if (doc.RootElement.TryGetProperty("topics", out var tpEl) && tpEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tpEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var v = item.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) topics.Add(v!);
                    }
                }
            }
            var sentences = new List<WritingScenarioStructuredSentenceResponse>();
            if (doc.RootElement.TryGetProperty("case_notes_structured", out var ssEl) && ssEl.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var s in ssEl.EnumerateArray())
                {
                    var sentence = s.TryGetProperty("sentence", out var stEl) && stEl.ValueKind == JsonValueKind.String ? stEl.GetString() ?? string.Empty : string.Empty;
                    var relevance = s.TryGetProperty("relevance", out var rvEl) && rvEl.ValueKind == JsonValueKind.String ? rvEl.GetString() ?? "relevant" : "relevant";
                    sentences.Add(new WritingScenarioStructuredSentenceResponse(i++, sentence, relevance));
                }
            }
            return new WritingScenarioUpsertRequest(
                Title: title,
                LetterType: request.LetterType,
                Profession: request.Profession,
                SubDiscipline: null,
                Topics: topics,
                Difficulty: request.Difficulty,
                CaseNotesMarkdown: caseNotes,
                CaseNotesStructured: sentences,
                IsDiagnostic: false,
                Status: "draft");
        }
        catch (JsonException)
        {
            return new WritingScenarioUpsertRequest(
                Title: $"AI scenario draft ({request.Profession}/{request.LetterType})",
                LetterType: request.LetterType,
                Profession: request.Profession,
                SubDiscipline: null,
                Topics: null,
                Difficulty: request.Difficulty,
                CaseNotesMarkdown: draftJson,
                CaseNotesStructured: null,
                IsDiagnostic: false,
                Status: "draft");
        }
    }

    public async Task<WritingScenarioGenerateResult> GenerateAsync(string adminId, WritingScenarioGenerateInternalRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Brief))
            throw ApiException.Validation("writing_scenario_brief_required", "Scenario brief is required.");
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            LetterType = request.LetterType,
            Task = AiTaskMode.GenerateContent,
        });
        var result = await aiGateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = string.Join('\n',
                $"Generate a Writing scenario for {request.Profession} ({request.SubDiscipline ?? "general"}), letter type {request.LetterType}, difficulty {request.Difficulty}.",
                "Admin brief:",
                request.Brief,
                "Return JSON { title, letter_type, profession, sub_discipline?, topics, difficulty, case_notes_markdown, case_notes_structured: [{sentence, relevance}], suggested_recipient, suggested_purpose }."),
            Temperature = 0.7,
            FeatureCode = AiFeatureCodes.WritingScenarioGenerateV1,
            PromptTemplateId = "writing.scenario.generate.v1",
            UserId = adminId,
        }, ct);
        return new WritingScenarioGenerateResult((result.Completion ?? string.Empty).Trim());
    }
}

// ============================================================================
// Helpers
// ============================================================================

internal static class WritingTextDiff
{
    public static IReadOnlyList<WritingDiffSegment> Compute(string original, string rewritten)
    {
        // Minimal word-level diff: shared longest-common-subsequence on word
        // arrays. Enough to colour-code in the UI without pulling
        // diff-match-patch in the backend.
        var a = original?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var b = rewritten?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i < a.Length; i++)
        {
            for (var j = 0; j < b.Length; j++)
            {
                dp[i + 1, j + 1] = string.Equals(a[i], b[j], StringComparison.OrdinalIgnoreCase)
                    ? dp[i, j] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }
        var segments = new List<WritingDiffSegment>();
        int ai = a.Length, bi = b.Length;
        var stack = new Stack<WritingDiffSegment>();
        while (ai > 0 && bi > 0)
        {
            if (string.Equals(a[ai - 1], b[bi - 1], StringComparison.OrdinalIgnoreCase))
            {
                stack.Push(new WritingDiffSegment("equal", a[ai - 1]));
                ai--; bi--;
            }
            else if (dp[ai - 1, bi] >= dp[ai, bi - 1])
            {
                stack.Push(new WritingDiffSegment("delete", a[ai - 1]));
                ai--;
            }
            else
            {
                stack.Push(new WritingDiffSegment("insert", b[bi - 1]));
                bi--;
            }
        }
        while (ai > 0) { stack.Push(new WritingDiffSegment("delete", a[--ai])); }
        while (bi > 0) { stack.Push(new WritingDiffSegment("insert", b[--bi])); }
        segments.AddRange(stack);
        return segments;
    }
}
