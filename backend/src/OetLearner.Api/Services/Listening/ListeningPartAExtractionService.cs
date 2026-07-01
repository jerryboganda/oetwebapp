using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part A — AI-assisted data entry.
//
// Pipeline (admin-triggered, synchronous):
//   1. Mistral OCR the QuestionPaper PDF  → high-fidelity Markdown.
//   2. Mistral OCR the AnswerKey PDF       → high-fidelity Markdown.
//   3. Claude (forced emit_part_a_manifest tool) structures both into a
//      partA-only ListeningStructureManifest: notesBody (canonical ____ grammar)
//      + per-gap correctAnswer / acceptedAnswers read from the answer key.
//   4. Deterministic server-side validation (gap count == 12 per extract, etc.).
//   5. Persist a Pending ListeningExtractionDraft for human review.
//
// Approval re-uses ListeningAuthoringService.ImportManifestAsync, so the same
// validation + publish-gate + audit trail applies as for a manual edit. Drafts
// are NEVER auto-published.
// ═════════════════════════════════════════════════════════════════════════════

public sealed record ListeningExtractionRunResult(
    string DraftId,
    string Status,
    int GapCountA1,
    int GapCountA2,
    int AnswerCountA1,
    int AnswerCountA2,
    IReadOnlyList<string> Warnings,
    string Summary);

public sealed record ListeningExtractionDraftSummary(
    string Id,
    string Status,
    DateTimeOffset ProposedAt,
    string? ProposedByUserId,
    string Summary,
    bool IsStub,
    string? StubReason);

public sealed record ListeningExtractionApprovalResult(
    string DraftId,
    ListeningStructureImportResult Import);

public sealed record ListeningExtractionAnswerPreview(
    int Number,
    string? CorrectAnswer,
    IReadOnlyList<string> AcceptedAnswers);

public sealed record ListeningExtractionExtractPreview(
    string PartCode,
    int ExtractNumber,
    int GapCount,
    string? NotesBody,
    IReadOnlyList<ListeningExtractionAnswerPreview> Answers);

public sealed record ListeningExtractionDraftDetail(
    string Id,
    string Status,
    string Summary,
    bool IsStub,
    string? StubReason,
    IReadOnlyList<ListeningExtractionExtractPreview> Extracts);

public interface IListeningPartAExtractionService
{
    /// <summary>Run the OCR + Claude pipeline for a paper's QuestionPaper +
    /// AnswerKey PDFs and stage a Pending draft. Fast-fails (409) if learner
    /// attempts already exist, and (400) if either PDF is missing.</summary>
    Task<ListeningExtractionRunResult> ExtractAsync(string paperId, string adminId, CancellationToken ct);

    /// <summary>Run the OCR + Claude pipeline against an AD-HOC uploaded
    /// question-paper (and optional answer-key) file — for the one-click
    /// "AI import" button on the Part A authoring page. Stages a Pending draft
    /// (auditable / re-approvable) and returns its projected detail so the
    /// editor can be pre-filled for human review. Fast-fails (409) if learner
    /// attempts already exist.</summary>
    Task<ListeningExtractionDraftDetail> ExtractFromUploadAsync(
        string paperId, byte[] questionBytes, string questionMime,
        byte[]? answerBytes, string? answerMime, string adminId, CancellationToken ct);

    Task<IReadOnlyList<ListeningExtractionDraftSummary>> ListPendingAsync(string paperId, CancellationToken ct);

    /// <summary>Project a draft's stored manifest into a review shape (per-extract
    /// notesBody + answers) so the admin can preview it before approving.</summary>
    Task<ListeningExtractionDraftDetail> GetDraftAsync(string paperId, string draftId, CancellationToken ct);

    /// <summary>Approve a Pending draft → import its manifest via the same
    /// validated path as a manual edit. Sets SourceProvenance if blank.</summary>
    Task<ListeningExtractionApprovalResult> ApproveAsync(string paperId, string draftId, string adminId, CancellationToken ct);

    Task RejectAsync(string paperId, string draftId, string adminId, string? reason, CancellationToken ct);
}

public sealed class ListeningPartAExtractionService(
    LearnerDbContext db,
    IFileStorage storage,
    IOcrService ocr,
    IListeningAuthoringService authoring,
    IAiProviderRegistry registry,
    IHttpClientFactory httpClientFactory,
    IDirectAiCallRecorder usageRecorder,
    TimeProvider clock,
    ILogger<ListeningPartAExtractionService> logger) : IListeningPartAExtractionService
{
    public const string AnthropicProviderCode = "anthropic";
    private const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";
    // Claude Sonnet 4.6 is the app-wide contextual-understanding model; the
    // registered `anthropic` row's DefaultModel overrides this when set.
    private const string DefaultModel = "claude-sonnet-5";
    private const string ToolName = "emit_part_a_manifest";

    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // ── Run ───────────────────────────────────────────────────────────────────

    public async Task<ListeningExtractionRunResult> ExtractAsync(string paperId, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets).ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        // Fast-fail before spending OCR / LLM budget: an authored structure
        // cannot be replaced once learner attempts exist.
        if (await db.ListeningAttempts.AnyAsync(a => a.PaperId == paperId, ct))
        {
            throw ApiException.Conflict(
                "listening_manifest_attempts_exist",
                "Learner attempts already exist for this paper, so its structure can't be replaced. Create a new revision instead.");
        }

        var questionPaper = ResolveAsset(paper, PaperAssetRole.QuestionPaper)
            ?? throw ApiException.Validation(
                "listening_extract_missing_question_paper",
                "Upload the Part A question-paper PDF (PDFs tab) before running AI extraction.");
        var answerKey = ResolveAsset(paper, PaperAssetRole.AnswerKey)
            ?? throw ApiException.Validation(
                "listening_extract_missing_answer_key",
                "Upload the answer-key PDF (PDFs tab) before running AI extraction.");

        var questionBytes = await ReadAssetBytesAsync(questionPaper, ct);
        var answerBytes = await ReadAssetBytesAsync(answerKey, ct);

        var questionMarkdown = await ocr.OcrToMarkdownAsync(
            questionBytes, questionPaper.MediaAsset!.MimeType, AiFeatureCodes.OcrListeningPartA, adminId, ct);
        var answerMarkdown = await ocr.OcrToMarkdownAsync(
            answerBytes, answerKey.MediaAsset!.MimeType, AiFeatureCodes.OcrListeningPartA, adminId, ct);

        var manifestJson = await CallClaudeManifestAsync(questionMarkdown, answerMarkdown, adminId, ct);

        ListeningStructureManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ListeningStructureManifest>(manifestJson, CamelJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Claude returned non-deserialisable manifest for paper {PaperId}", paperId);
            throw ApiException.Validation("listening_extract_bad_manifest",
                "The AI returned a manifest that could not be parsed. Try again.");
        }
        if (manifest is null)
            throw ApiException.Validation("listening_extract_bad_manifest", "The AI returned an empty manifest.");

        var (warnings, gapsA1, gapsA2, ansA1, ansA2) = ValidateManifest(manifest);
        var isStub = warnings.Count > 0;
        var summary = isStub
            ? $"AI extraction with {warnings.Count} issue(s) to review — A1 {gapsA1} gaps / {ansA1} answers, A2 {gapsA2} gaps / {ansA2} answers."
            : $"AI extraction OK — A1 {gapsA1} gaps / {ansA1} answers, A2 {gapsA2} gaps / {ansA2} answers.";

        var draft = new ListeningExtractionDraft
        {
            Id = $"lxd_{Guid.NewGuid():N}",
            PaperId = paperId,
            Status = ListeningExtractionDraftStatus.Pending,
            ProposedAt = DateTimeOffset.UtcNow,
            ProposedByUserId = adminId,
            IsStub = isStub,
            StubReason = isStub ? Truncate(string.Join("; ", warnings), 512) : null,
            Summary = Truncate(summary, 2048),
            ProposedQuestionsJson = JsonSerializer.Serialize(manifest, CamelJson),
            RawAiResponseJson = Truncate(manifestJson, 65536),
        };
        db.ListeningExtractionDrafts.Add(draft);
        await db.SaveChangesAsync(ct);

        return new ListeningExtractionRunResult(
            draft.Id, "pending", gapsA1, gapsA2, ansA1, ansA2, warnings, summary);
    }

    // ── List ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ListeningExtractionDraftSummary>> ListPendingAsync(string paperId, CancellationToken ct)
    {
        var rows = await db.ListeningExtractionDrafts.AsNoTracking()
            .Where(d => d.PaperId == paperId && d.Status == ListeningExtractionDraftStatus.Pending)
            .OrderByDescending(d => d.ProposedAt)
            .ToListAsync(ct);

        return rows.Select(d => new ListeningExtractionDraftSummary(
            d.Id, d.Status.ToString().ToLowerInvariant(), d.ProposedAt, d.ProposedByUserId,
            d.Summary, d.IsStub, d.StubReason)).ToList();
    }

    public async Task<ListeningExtractionDraftDetail> GetDraftAsync(string paperId, string draftId, CancellationToken ct)
    {
        var draft = await db.ListeningExtractionDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == draftId && d.PaperId == paperId, ct)
            ?? throw ApiException.NotFound("listening_extract_draft_not_found", "Extraction draft not found.");

        return BuildDraftDetail(draft);
    }

    /// <summary>Project a stored draft's manifest into the review shape (per-extract
    /// notesBody + answers) used by both the draft-detail GET and the one-click
    /// upload import. Shared so the two paths stay byte-identical.</summary>
    private static ListeningExtractionDraftDetail BuildDraftDetail(ListeningExtractionDraft draft)
    {
        ListeningStructureManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ListeningStructureManifest>(draft.ProposedQuestionsJson, CamelJson);
        }
        catch (JsonException)
        {
            manifest = null;
        }

        var extracts = (manifest?.PartA?.Extracts ?? Array.Empty<ListeningExtractManifest>())
            .Select((e, i) => new ListeningExtractionExtractPreview(
                PartCode: i == 0 ? "A1" : "A2",
                ExtractNumber: e.ExtractNumber,
                GapCount: CountGaps(e.NotesBody),
                NotesBody: e.NotesBody,
                Answers: (e.Questions ?? Array.Empty<ListeningQuestionManifest>())
                    .Select(q => new ListeningExtractionAnswerPreview(
                        q.Number, q.CorrectAnswer, q.AcceptedAnswers ?? Array.Empty<string>()))
                    .ToList()))
            .ToList();

        return new ListeningExtractionDraftDetail(
            draft.Id, draft.Status.ToString().ToLowerInvariant(), draft.Summary, draft.IsStub, draft.StubReason, extracts);
    }

    // ── Import from ad-hoc upload (one-click "AI import" button) ────────────────

    public async Task<ListeningExtractionDraftDetail> ExtractFromUploadAsync(
        string paperId, byte[] questionBytes, string questionMime,
        byte[]? answerBytes, string? answerMime, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        // Same guard as ExtractAsync: never silently replace a structure that
        // learners have already attempted.
        if (await db.ListeningAttempts.AnyAsync(a => a.PaperId == paperId, ct))
        {
            throw ApiException.Conflict(
                "listening_manifest_attempts_exist",
                "Learner attempts already exist for this paper, so its structure can't be replaced. Create a new revision instead.");
        }

        if (questionBytes is null || questionBytes.Length == 0)
            throw ApiException.Validation("listening_extract_missing_question_paper",
                "Upload the Part A question-paper PDF or image to import.");

        var questionMarkdown = await ocr.OcrToMarkdownAsync(
            questionBytes, questionMime, AiFeatureCodes.OcrListeningPartA, adminId, ct);
        // Answer key is optional for ad-hoc import: without it the operator fills
        // the answer key by hand (the validator flags the missing answers).
        var answerMarkdown = answerBytes is { Length: > 0 }
            ? await ocr.OcrToMarkdownAsync(answerBytes, answerMime ?? questionMime, AiFeatureCodes.OcrListeningPartA, adminId, ct)
            : "(No separate answer key supplied. Leave correctAnswer empty where the answer is unknown.)";

        var manifestJson = await CallClaudeManifestAsync(questionMarkdown, answerMarkdown, adminId, ct);

        ListeningStructureManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ListeningStructureManifest>(manifestJson, CamelJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Claude returned non-deserialisable manifest for uploaded import on paper {PaperId}", paperId);
            throw ApiException.Validation("listening_extract_bad_manifest",
                "The AI returned a manifest that could not be parsed. Try again.");
        }
        if (manifest is null)
            throw ApiException.Validation("listening_extract_bad_manifest", "The AI returned an empty manifest.");

        var (warnings, gapsA1, gapsA2, ansA1, ansA2) = ValidateManifest(manifest);
        var isStub = warnings.Count > 0;
        var summary = isStub
            ? $"AI import with {warnings.Count} issue(s) to review — A1 {gapsA1} gaps / {ansA1} answers, A2 {gapsA2} gaps / {ansA2} answers."
            : $"AI import OK — A1 {gapsA1} gaps / {ansA1} answers, A2 {gapsA2} gaps / {ansA2} answers.";

        // Stage a Pending draft so the import is auditable and can still be
        // approved later through the normal flow, even though the operator's
        // primary path is "review in the editor, then Save".
        var draft = new ListeningExtractionDraft
        {
            Id = $"lxd_{Guid.NewGuid():N}",
            PaperId = paperId,
            Status = ListeningExtractionDraftStatus.Pending,
            ProposedAt = DateTimeOffset.UtcNow,
            ProposedByUserId = adminId,
            IsStub = isStub,
            StubReason = isStub ? Truncate(string.Join("; ", warnings), 512) : null,
            Summary = Truncate(summary, 2048),
            ProposedQuestionsJson = JsonSerializer.Serialize(manifest, CamelJson),
            RawAiResponseJson = Truncate(manifestJson, 65536),
        };
        db.ListeningExtractionDrafts.Add(draft);
        await db.SaveChangesAsync(ct);

        return BuildDraftDetail(draft);
    }

    // ── Approve ──────────────────────────────────────────────────────────────

    public async Task<ListeningExtractionApprovalResult> ApproveAsync(
        string paperId, string draftId, string adminId, CancellationToken ct)
    {
        var draft = await db.ListeningExtractionDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.PaperId == paperId, ct)
            ?? throw ApiException.NotFound("listening_extract_draft_not_found", "Extraction draft not found.");
        if (draft.Status != ListeningExtractionDraftStatus.Pending)
            throw ApiException.Conflict("listening_extract_draft_decided", "This draft has already been decided.");

        ListeningStructureManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ListeningStructureManifest>(draft.ProposedQuestionsJson, CamelJson);
        }
        catch (JsonException)
        {
            manifest = null;
        }
        if (manifest is null)
            throw ApiException.Validation("listening_extract_bad_manifest", "The stored draft manifest could not be parsed.");

        // ImportManifestAsync → ReplaceStructureAsync requires SourceProvenance.
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");
        if (string.IsNullOrWhiteSpace(paper.SourceProvenance))
        {
            paper.SourceProvenance = "AI extraction (Mistral OCR + Claude)";
            paper.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        var import = await authoring.ImportManifestAsync(paperId, manifest, replaceExisting: true, adminId, ct);

        draft.Status = ListeningExtractionDraftStatus.Approved;
        draft.DecidedAt = DateTimeOffset.UtcNow;
        draft.DecidedByUserId = adminId;
        await db.SaveChangesAsync(ct);

        return new ListeningExtractionApprovalResult(draft.Id, import);
    }

    // ── Reject ───────────────────────────────────────────────────────────────

    public async Task RejectAsync(string paperId, string draftId, string adminId, string? reason, CancellationToken ct)
    {
        var draft = await db.ListeningExtractionDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.PaperId == paperId, ct)
            ?? throw ApiException.NotFound("listening_extract_draft_not_found", "Extraction draft not found.");
        if (draft.Status != ListeningExtractionDraftStatus.Pending)
            throw ApiException.Conflict("listening_extract_draft_decided", "This draft has already been decided.");

        draft.Status = ListeningExtractionDraftStatus.Rejected;
        draft.DecidedAt = DateTimeOffset.UtcNow;
        draft.DecidedByUserId = adminId;
        draft.DecisionReason = Truncate(reason ?? string.Empty, 512);
        await db.SaveChangesAsync(ct);
    }

    // ── Validation (deterministic; never trust the model) ───────────────────────

    private static (IReadOnlyList<string> warnings, int gapsA1, int gapsA2, int ansA1, int ansA2)
        ValidateManifest(ListeningStructureManifest manifest)
    {
        var warnings = new List<string>();
        if (manifest.PartB is not null || manifest.PartC is not null)
            warnings.Add("Manifest unexpectedly included Part B/C — only Part A is used.");

        var extracts = manifest.PartA?.Extracts ?? Array.Empty<ListeningExtractManifest>();
        if (extracts.Count != 2)
            warnings.Add($"Expected exactly 2 Part A extracts, got {extracts.Count}.");

        var e1 = extracts.ElementAtOrDefault(0);
        var e2 = extracts.ElementAtOrDefault(1);
        var gapsA1 = CountGaps(e1?.NotesBody);
        var gapsA2 = CountGaps(e2?.NotesBody);
        var ansA1 = e1?.Questions?.Count ?? 0;
        var ansA2 = e2?.Questions?.Count ?? 0;

        if (gapsA1 != 12) warnings.Add($"Extract 1 has {gapsA1} gaps (expected 12).");
        if (gapsA2 != 12) warnings.Add($"Extract 2 has {gapsA2} gaps (expected 12).");
        if (ansA1 != gapsA1) warnings.Add($"Extract 1 has {ansA1} answers but {gapsA1} gaps.");
        if (ansA2 != gapsA2) warnings.Add($"Extract 2 has {ansA2} answers but {gapsA2} gaps.");

        foreach (var (extract, idx) in new[] { (e1, 1), (e2, 2) })
        {
            foreach (var q in extract?.Questions ?? Array.Empty<ListeningQuestionManifest>())
            {
                if (string.IsNullOrWhiteSpace(q.CorrectAnswer))
                    warnings.Add($"Extract {idx} Q{q.Number} has no correct answer.");
            }
        }

        return (warnings, gapsA1, gapsA2, ansA1, ansA2);
    }

    /// <summary>Count gap markers — a run of 4+ underscores is ONE gap. Byte-identical
    /// to <c>countGaps</c> in <c>lib/listening-part-a-notes.ts</c>.</summary>
    private static int CountGaps(string? body)
        => string.IsNullOrEmpty(body) ? 0 : Regex.Matches(body, "_{4,}").Count;

    // ── Claude call (forced tool, no temperature for Opus 4.7/4.8) ──────────────

    private async Task<string> CallClaudeManifestAsync(string questionMarkdown, string answerMarkdown, string adminId, CancellationToken ct)
    {
        var row = await registry.FindByCodeAsync(AnthropicProviderCode, ct)
            ?? throw new InvalidOperationException(
                $"Anthropic provider '{AnthropicProviderCode}' is not registered. Add a row in /admin/ai-providers with Code={AnthropicProviderCode}.");

        var baseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(row.BaseUrl) ? DefaultAnthropicBaseUrl : row.BaseUrl);
        var model = string.IsNullOrWhiteSpace(row.DefaultModel) ? DefaultModel : row.DefaultModel;
        var apiKey = await registry.GetPlatformKeyAsync(AnthropicProviderCode, ct)
            ?? throw new InvalidOperationException($"Platform API key missing for provider {AnthropicProviderCode}.");

        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
        if (unsafeReason is not null) throw new InvalidOperationException(unsafeReason);

        var userText =
            "QUESTION PAPER (OCR Markdown):\n\n" + questionMarkdown +
            "\n\n=====\n\nANSWER KEY (OCR Markdown):\n\n" + answerMarkdown;

        var startedAt = clock.GetUtcNow();
        var usageContext = new AiUsageContext(
            UserId: adminId,
            AuthAccountId: null,
            TenantId: null,
            FeatureCode: AiFeatureCodes.ListeningPartAExtract,
            RulebookVersion: null,
            PromptTemplateId: ToolName,
            SystemPrompt: SystemPrompt,
            UserPrompt: userText,
            StartedAt: startedAt);
        int LatencyMs() => (int)(clock.GetUtcNow() - startedAt).TotalMilliseconds;

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            // No temperature/top_p/top_k — removed on Opus 4.7/4.8 (400 if sent).
            ["max_tokens"] = 8000,
            ["system"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = SystemPrompt,
                    ["cache_control"] = new Dictionary<string, object?> { ["type"] = "ephemeral" },
                },
            },
            ["messages"] = new object[]
            {
                new Dictionary<string, object?> { ["role"] = "user", ["content"] = userText },
            },
            ["tools"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = ToolName,
                    ["description"] = "Emit the structured OET Listening Part A manifest (partA only).",
                    ["input_schema"] = JsonSerializer.Deserialize<JsonElement>(ToolSchemaJson),
                },
            },
            ["tool_choice"] = new Dictionary<string, object?> { ["type"] = "tool", ["name"] = ToolName },
        };

        var client = httpClientFactory.CreateClient("ListeningExtractionAnthropic");
        client.BaseAddress = new Uri(baseUrl + "/");
        client.DefaultRequestHeaders.Remove("x-api-key");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Remove("anthropic-version");
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage response;
        string body;
        try
        {
            response = await client.PostAsync(
                "v1/messages",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
                "anthropic_network", ex.Message, LatencyMs(), "listening.parta.extract", ct);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            await usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
                $"http_{(int)response.StatusCode}", Truncate(body, 500), LatencyMs(), "listening.parta.extract", ct);
            throw new InvalidOperationException(
                $"Claude extraction failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 500)}");
        }
        response.Dispose();

        using var doc = JsonDocument.Parse(body);
        var usage = ParseAnthropicUsage(doc.RootElement);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t)
                    && string.Equals(t.GetString(), "tool_use", StringComparison.Ordinal)
                    && block.TryGetProperty("input", out var input))
                {
                    var cost = usage is null
                        ? 0m
                        : row.PricePer1kPromptTokens * usage.PromptTokens / 1000m
                          + row.PricePer1kCompletionTokens * usage.CompletionTokens / 1000m;
                    await usageRecorder.RecordSuccessAsync(
                        usageContext, AnthropicProviderCode, model, usage,
                        LatencyMs(), "listening.parta.extract", cost, ct);
                    return input.GetRawText();
                }
            }
        }

        await usageRecorder.RecordFailureAsync(
            usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
            "no_tool_use", "Claude did not return a tool_use manifest block.", LatencyMs(), "listening.parta.extract", ct);
        throw new InvalidOperationException("Claude did not return a tool_use manifest block.");
    }

    /// <summary>Parse the Anthropic <c>usage</c> block (input_tokens /
    /// output_tokens) into the gateway's <see cref="AiUsage"/> shape. Returns
    /// null when absent so the recorder persists zero tokens.</summary>
    private static AiUsage? ParseAnthropicUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return null;
        var input = u.TryGetProperty("input_tokens", out var it) && it.ValueKind == JsonValueKind.Number ? it.GetInt32() : 0;
        var output = u.TryGetProperty("output_tokens", out var ot) && ot.ValueKind == JsonValueKind.Number ? ot.GetInt32() : 0;
        return new AiUsage { PromptTokens = input, CompletionTokens = output };
    }

    // ── Asset helpers ──────────────────────────────────────────────────────────

    /// <summary>Pick the best asset for a role — prefer Part "A" (or whole-paper /
    /// blank) so a per-part PDF and a whole-paper key both resolve.</summary>
    private static ContentPaperAsset? ResolveAsset(ContentPaper paper, PaperAssetRole role)
        => paper.Assets
            .Where(a => a.Role == role && a.MediaAsset is not null)
            .OrderByDescending(a => RoleAssetRank(a.Part))
            .ThenByDescending(a => a.IsPrimary)
            .ThenBy(a => a.DisplayOrder)
            .FirstOrDefault();

    private static int RoleAssetRank(string? part)
    {
        if (string.IsNullOrWhiteSpace(part)) return 2;                 // whole-paper
        return string.Equals(part.Trim(), "A", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
    }

    private async Task<byte[]> ReadAssetBytesAsync(ContentPaperAsset asset, CancellationToken ct)
    {
        await using var s = await storage.OpenReadAsync(asset.MediaAsset!.StoragePath, ct);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].TrimEnd('/');
        return trimmed;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ── Prompt + tool schema ─────────────────────────────────────────────────

    private const string SystemPrompt = """
You are an expert OET (Occupational English Test) content engineer. Convert OCR'd OET
Listening Part A material into a strict JSON manifest by calling the emit_part_a_manifest tool.

OET Listening Part A has EXACTLY TWO note-completion extracts:
  - Extract 1 (A1): questions 1-12
  - Extract 2 (A2): questions 13-24
You receive two OCR'd documents: a QUESTION PAPER (printed notes with numbered blanks like
"(1)____") and an ANSWER KEY (the official answers for questions 1-24).

Return a manifest with partA ONLY — set partB and partC to null. partA.extracts must contain
EXACTLY 2 entries (extractNumber 1 and 2).

For each extract, write notesBody using THIS EXACT plain-text grammar (output literally — do NOT
use real markdown bold/tables):
  - A line starting with "## " is a SECTION HEADING (e.g. "## Background to condition").
  - A line starting with "- " is a BULLET.
  - A line starting with "  - " (two spaces then dash) is a SUB-BULLET.
  - A line that is exactly "---" is a divider.
  - Any other non-empty line is a plain paragraph — use this for the intro line
    ("You hear a ... talking to a patient called ..."), the "You now have thirty seconds to look
    at the notes." line, and the "Patient: <name>" label line.
  - A GAP (candidate blank) is written as EXACTLY four underscores: ____  . Gaps sit INLINE,
    mid-sentence, exactly where the printed "(n)____" appears — keep the surrounding words and
    DROP the "(n)" number. Example: printed "discomfort from episodes of bloating, (1)____ and
    fatigue" becomes the bullet "- discomfort from episodes of bloating, ____ and fatigue".
  - Each "____" is ONE gap. Never merge two blanks into one run. The Nth gap in a document
    (top-to-bottom) is that extract's Nth question.

HARD REQUIREMENTS:
  - Extract 1 notesBody must contain EXACTLY 12 gaps; extract 2 must contain EXACTLY 12 gaps.
  - For every question set: type = "gap_fill"; number (1-12 for extract 1, 13-24 for extract 2);
    correctAnswer (from the ANSWER KEY); acceptedAnswers (sensible variants — UK/US spelling,
    common abbreviations; empty array if none). Leave noteTextBeforeGap empty — notesBody carries
    the full document.
  - Set patientName / professionalRole from the question-paper intro when present.
  - Preserve the original wording faithfully. Do not invent content. If the answer key is
    ambiguous for a gap, use your best reading and still provide a correctAnswer.
""";

    private const string ToolSchemaJson = """
{
  "type": "object",
  "properties": {
    "testTitle": { "type": "string" },
    "partA": {
      "type": "object",
      "properties": {
        "extracts": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "extractNumber": { "type": "integer" },
              "patientName": { "type": "string" },
              "professionalRole": { "type": "string" },
              "readingTimeSeconds": { "type": "integer" },
              "notesBody": { "type": "string" },
              "questions": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "number": { "type": "integer" },
                    "type": { "type": "string" },
                    "noteTextBeforeGap": { "type": "string" },
                    "correctAnswer": { "type": "string" },
                    "acceptedAnswers": { "type": "array", "items": { "type": "string" } }
                  },
                  "required": ["number", "correctAnswer"]
                }
              }
            },
            "required": ["extractNumber", "notesBody", "questions"]
          }
        }
      },
      "required": ["extracts"]
    }
  },
  "required": ["partA"]
}
""";
}
