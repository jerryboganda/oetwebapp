using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part B / Part C — AI-assisted answer-key entry.
//
// Part B/C questions are source-backed: the learner must receive the printed
// question stem and all three options as normalized authored data, alongside
// the correct option letter (A/B/C) + an optional "why correct" rationale.
// This service automates that:
//
//   1. Mistral OCR the QuestionPaper PDF(s) → Markdown (Part C has two extracts:
//      C1 + C2, uploaded as two documents).
//   2. Mistral OCR the AnswerKey PDF        → Markdown.
//   3. Claude (forced emit_part_bc_answers tool) reads the correct option per
//      question from the answer key, cross-checks the question paper, and drafts
//      a one-sentence rationale.
//   4. Deterministic server-side validation (numbers in the part's range, letter
//      ∈ {A,B,C}).
//
// Unlike Part A, this returns a PROJECTION only — the admin reviews the answers
// in the answer-sheet UI and persists them through the normal
// replaceListeningStructure path (which carries its own publish/attempt guards).
// ═════════════════════════════════════════════════════════════════════════════

public sealed record ListeningPartBCAnswer(
    int Number,
    string CorrectAnswer,
    string? Rationale,
    // Real inline question text extracted from the question paper. Empty values
    // are retained in the projection only so the admin can correct an OCR miss;
    // the authoring API refuses to persist a Part B/C item until these fields
    // are present and source-backed.
    string? Stem = null,
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null);

public sealed record ListeningPartBCImportResult(
    string Part,
    bool IsStub,
    string? StubReason,
    string Summary,
    IReadOnlyList<ListeningPartBCAnswer> Answers);

public interface IListeningPartBCExtractionService
{
    /// <summary>Run the OCR + Claude pipeline against AD-HOC uploaded question
    /// paper(s) + answer-key file for Listening Part B or Part C, and return the
    /// projected source-backed stem/options + correct option (A/B/C) + rationale
    /// for the admin to review then Save. Fast-fails (409) if learner attempts
    /// already exist.</summary>
    Task<ListeningPartBCImportResult> ExtractFromUploadAsync(
        string paperId, string part,
        IReadOnlyList<(byte[] Bytes, string Mime)> questionDocs,
        byte[] answerBytes, string answerMime,
        string adminId, CancellationToken ct);
}

public sealed class ListeningPartBCExtractionService(
    LearnerDbContext db,
    IOcrService ocr,
    IAiProviderRegistry registry,
    IAiFeatureRouteResolver routeResolver,
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.AiProviderOptions> providerOptions,
    IDirectAiCallRecorder usageRecorder,
    TimeProvider clock,
    ILogger<ListeningPartBCExtractionService> logger,
    IListeningPolicyService? listeningPolicyService = null) : IListeningPartBCExtractionService
{
    // Owner policy is checked before any OCR or model call. This path returns
    // a projection only; the admin must still review and save it explicitly.
    private const string AnthropicProviderCode = "anthropic";
    // UBAG route: mirrors the Part A route-aware pattern — an admin ubag
    // route sends the same OCR markdown to the facade with response_format
    // json_object + forced-tool emulation.
    private const string UbagProviderCode = "ubag";
    private const string DefaultModel = CoreAiProviderSeeder.AnthropicDefaultModel;
    private const string ToolName = "emit_part_bc_answers";

    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ListeningPartBCImportResult> ExtractFromUploadAsync(
        string paperId, string part,
        IReadOnlyList<(byte[] Bytes, string Mime)> questionDocs,
        byte[] answerBytes, string answerMime,
        string adminId, CancellationToken ct)
    {
        part = (part ?? string.Empty).Trim().ToUpperInvariant();
        if (part != "B" && part != "C")
            throw ApiException.Validation("listening_partbc_invalid_part", "Part must be 'B' or 'C'.");

        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        // Never silently replace a structure that learners have already attempted —
        // fast-fail before spending OCR / LLM budget (the Save would be rejected too).
        if (await db.ListeningAttempts.AnyAsync(a => a.PaperId == paperId, ct))
        {
            throw ApiException.Conflict(
                "listening_manifest_attempts_exist",
                "Learner attempts already exist for this paper, so its structure can't be replaced. Create a new revision instead.");
        }

        if (questionDocs is null || questionDocs.Count == 0 || questionDocs.All(d => d.Bytes.Length == 0))
            throw ApiException.Validation("listening_partbc_missing_question_paper",
                $"Upload the Part {part} question paper to extract.");
        if (answerBytes is null || answerBytes.Length == 0)
            throw ApiException.Validation("listening_partbc_missing_answer_key",
                "Upload the answer-key PDF — Part B/C correct options are read from it.");

        // Projection-only Part B/C imports do not create a review draft, so
        // record each permitted attempt in the existing audit ledger before
        // spending any OCR or model budget. The guard returns the ordinal it
        // authorized for THIS run.
        var extractionAttempt = await EnsureExtractionAllowedAsync(paperId, part, adminId, ct);

        // ── W2: durable operation BEFORE OCR and before the provider ────────────
        // OCR is itself a paid direct AI call, so a duplicate/conflicting/refused
        // run must be stopped before ANY spend.
        var lease = await BeginExtractionOperationAsync(
            paperId, part, adminId, extractionAttempt, questionDocs, answerBytes, ct);

        // Every exit path below owns a lease, so every exit path must reconcile
        // it — otherwise a transport/parse/save failure leaves the row Leased
        // and blocks every future authorized attempt. Errors are re-thrown.
        return await DirectAiOperationReconciler.RunAsync(
            usageRecorder, lease, AnthropicProviderCode,
            () => RunPartBCExtractionAsync(paperId, part, questionDocs, answerBytes, answerMime, adminId, lease, ct),
            ct);
    }

    private async Task<ListeningPartBCImportResult> RunPartBCExtractionAsync(
        string paperId, string part,
        IReadOnlyList<(byte[] Bytes, string Mime)> questionDocs,
        byte[] answerBytes, string answerMime,
        string adminId, DirectAiOperationLease lease, CancellationToken ct)
    {
        // OCR each question document (Part C ships two extracts: C1 + C2) and the key.
        var questionMarkdownParts = new List<string>();
        for (var i = 0; i < questionDocs.Count; i++)
        {
            var (bytes, mime) = questionDocs[i];
            if (bytes.Length == 0) continue;
            var md = await ocr.OcrToMarkdownAsync(bytes, mime, AiFeatureCodes.OcrListeningPartBC, adminId, ct);
            questionMarkdownParts.Add(questionDocs.Count > 1 ? $"--- QUESTION DOCUMENT {i + 1} ---\n{md}" : md);
        }
        var questionMarkdown = string.Join("\n\n", questionMarkdownParts);
        var answerMarkdown = await ocr.OcrToMarkdownAsync(answerBytes, answerMime, AiFeatureCodes.OcrListeningPartBC, adminId, ct);

        var rawJson = await CallClaudeAnswersAsync(part, questionMarkdown, answerMarkdown, adminId, lease, ct);

        BcToolOutput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BcToolOutput>(rawJson, CamelJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Claude returned non-deserialisable Part B/C answers for paper {PaperId}", paperId);
            throw ApiException.Validation("listening_partbc_bad_output",
                "The AI returned answers that could not be parsed. Try again.");
        }

        var (answers, warnings) = ValidateAndProject(part, parsed?.Answers ?? new List<BcToolAnswer>());
        var isStub = warnings.Count > 0;
        var summary = isStub
            ? $"AI extraction for Part {part} with {warnings.Count} issue(s) to review — {answers.Count} answer(s)."
            : $"AI extraction OK for Part {part} — {answers.Count} answer(s).";

        // Projection-only: there is no draft row to point at, so the operation
        // closes with no ResultRef rather than a pointer to nothing.
        await usageRecorder.CompleteOperationAsync(
            lease.OperationId!, AiOperationState.Completed, null,
            AnthropicProviderCode, null, CancellationToken.None, lease.BudgetReservation);

        return new ListeningPartBCImportResult(
            part, isStub, isStub ? Truncate(string.Join("; ", warnings), 512) : null, summary, answers);
    }

    /// <summary>
    /// W2 — opens the durable control-plane operation for one Part B/C
    /// extraction run, BEFORE any OCR or Anthropic spend. The request hash is a
    /// digest of the uploaded bytes; raw file content is never persisted.
    /// <para>
    /// <paramref name="extractionAttempt"/> is the ordinal the owner-policy
    /// guard authorized for THIS run, so each policy-authorized retry gets its
    /// own idempotency key and resource slot while a concurrent duplicate of the
    /// SAME attempt still collides. Hard-coding 1 was a one-run-ever lockout.
    /// </para>
    /// </summary>
    private async Task<DirectAiOperationLease> BeginExtractionOperationAsync(
        string paperId, string part, string adminId, int extractionAttempt,
        IReadOnlyList<(byte[] Bytes, string Mime)> questionDocs, byte[] answerBytes, CancellationToken ct)
    {
        var lease = await usageRecorder.BeginOperationAsync(new DirectAiOperationRequest
        {
            FeatureCode = AiFeatureCodes.ListeningPartBCExtract,
            Module = "listening",
            UserId = adminId,
            ResourceId = $"{paperId}:{part}",
            ResourceType = "content_paper_partbc",
            ResourceVersion = extractionAttempt,
            RequestHash = HashInputs(questionDocs, answerBytes),
            PromptVersion = ToolName,
            ModelRoute = AnthropicProviderCode,
            OperationClass = AiOperationClass.AdminBatch,
            // The audit-ledger attempt ordinal above IS this caller's durable
            // attempt counter and AiExtractionMaxRetriesPerPaper is the single
            // authority on how many runs are allowed, so the recorder must not
            // invent extra rounds of its own.
            AllowRetryAfterFailure = false,
        }, ct);

        if (lease.CanProceed) return lease;

        logger.LogWarning(
            "Part B/C extraction refused for paper {PaperId} part {Part}: {Disposition} ({Reason}); zero provider calls.",
            paperId, part, lease.Disposition, lease.Reason);

        throw lease.Disposition switch
        {
            DirectAiOperationDisposition.PolicyRefused => ApiException.Conflict(
                "listening_partbc_policy_refused",
                "AI extraction is disabled for this feature. Re-enable the feature policy and try again."),
            DirectAiOperationDisposition.Unavailable => ApiException.Conflict(
                "listening_partbc_unavailable",
                "AI extraction is temporarily unavailable. Please try again shortly."),
            _ => ApiException.Conflict(
                "listening_partbc_already_running",
                "An AI extraction for this paper part and these files is already running or has already completed. Refresh and try again, or upload a new revision."),
        };
    }

    /// <summary>SHA-256 over the exact bytes that will be sent for OCR. Never
    /// persisted in raw form.</summary>
    private static string HashInputs(IReadOnlyList<(byte[] Bytes, string Mime)> questionDocs, byte[] answerBytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        foreach (var (bytes, _) in questionDocs)
        {
            if (bytes.Length > 0) sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(answerBytes, 0, answerBytes.Length);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    // ── Validation (deterministic; never trust the model) ───────────────────────

    /// <summary>
    /// Owner-policy gate for one Part B/C extraction run. Returns the 1-based
    /// ordinal of THIS authorized run (existing durable starts + 1), which
    /// becomes the operation's resource version.
    /// </summary>
    private async Task<int> EnsureExtractionAllowedAsync(
        string paperId,
        string part,
        string adminId,
        CancellationToken ct)
    {
        var policy = listeningPolicyService is not null
            ? await listeningPolicyService.GetGlobalAsync(ct)
            : await db.ListeningPolicies.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == "global", ct)
                ?? new ListeningPolicy { Id = "global" };

        if (!policy.AiExtractionEnabled)
        {
            throw ApiException.Conflict(
                "listening_ai_extraction_disabled",
                "Listening AI extraction is disabled by the owner policy.");
        }

        var attemptedSoFar = await db.AuditEvents.AsNoTracking()
            .CountAsync(entry => entry.ResourceType == "ContentPaper"
                && entry.ResourceId == paperId
                && entry.Action == "ListeningPartBCExtractionStarted", ct);
        if (policy.AiExtractionMaxRetriesPerPaper > 0
            && attemptedSoFar >= policy.AiExtractionMaxRetriesPerPaper)
        {
            throw ApiException.Conflict(
                "listening_ai_extraction_retry_limit_reached",
                $"The maximum number of Listening AI extractions ({policy.AiExtractionMaxRetriesPerPaper}) has been reached for this paper.");
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = clock.GetUtcNow(),
            ActorId = adminId,
            ActorName = adminId,
            Action = "ListeningPartBCExtractionStarted",
            ResourceType = "ContentPaper",
            ResourceId = paperId,
            Details = JsonSerializer.Serialize(new { part, projectionOnly = true }),
        });
        await db.SaveChangesAsync(ct);

        return attemptedSoFar + 1;
    }

    private static (IReadOnlyList<ListeningPartBCAnswer> Answers, IReadOnlyList<string> Warnings)
        ValidateAndProject(string part, IReadOnlyList<BcToolAnswer> raw)
    {
        var (lo, hi) = part == "B" ? (25, 30) : (31, 42);
        var warnings = new List<string>();
        var byNumber = new Dictionary<int, ListeningPartBCAnswer>();

        foreach (var a in raw)
        {
            if (a.Number < lo || a.Number > hi)
            {
                warnings.Add($"Ignored out-of-range question {a.Number} (Part {part} is {lo}–{hi}).");
                continue;
            }
            var letter = (a.CorrectAnswer ?? string.Empty).Trim().ToUpperInvariant();
            if (letter is not ("A" or "B" or "C"))
            {
                warnings.Add($"Q{a.Number} has an invalid correct option '{a.CorrectAnswer}'.");
                continue;
            }
            var rationale = string.IsNullOrWhiteSpace(a.Rationale) ? null : Truncate(a.Rationale.Trim(), 1024);
            var stem = CleanSourceStem(a.Stem);
            var optionA = CleanSourceOption(a.OptionA);
            var optionB = CleanSourceOption(a.OptionB);
            var optionC = CleanSourceOption(a.OptionC);
            if (stem is null)
                warnings.Add($"Q{a.Number} is missing a source question stem; transcribe the exact printed question before saving.");
            if (optionA is null || optionB is null || optionC is null)
                warnings.Add($"Q{a.Number} is missing one or more source answer choices; transcribe A, B and C before saving.");

            byNumber[a.Number] = new ListeningPartBCAnswer(
                a.Number, letter, rationale,
                Stem: stem,
                OptionA: optionA,
                OptionB: optionB,
                OptionC: optionC);
        }

        for (var n = lo; n <= hi; n++)
        {
            if (!byNumber.ContainsKey(n))
                warnings.Add($"No answer extracted for Q{n}.");
        }

        var answers = byNumber.Values.OrderBy(a => a.Number).ToList();
        return (answers, warnings);
    }

    private sealed record BcToolOutput(List<BcToolAnswer>? Answers);
    private sealed record BcToolAnswer(
        int Number, string? CorrectAnswer, string? Rationale,
        string? Stem, string? OptionA, string? OptionB, string? OptionC);

    private static string? CleanText(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), max);

    private static string? CleanSourceStem(string? value)
    {
        var cleaned = ListeningLearnerService.SanitizeQuestionPrompt(value);
        return ListeningLearnerService.IsUsablePartBCStem(cleaned)
            ? Truncate(cleaned, 2048)
            : null;
    }

    private static string? CleanSourceOption(string? value)
    {
        var cleaned = ListeningLearnerService.SanitizeOptionText(value);
        return string.IsNullOrWhiteSpace(cleaned) ? null : Truncate(cleaned, 1024);
    }

    // ── Claude call (forced tool, no temperature for Opus 4.7/4.8) ──────────────
    // Route-aware: honours the admin board toggle for
    // listening.partbc.extract (ubag route → facade + emulation, otherwise
    // the Anthropic forced-tool path byte-identical to before).

    private async Task<string> CallClaudeAnswersAsync(
        string part, string questionMarkdown, string answerMarkdown, string adminId,
        DirectAiOperationLease lease, CancellationToken ct)
    {
        var ubagRoute = await routeResolver.ResolveAsync(AiFeatureCodes.ListeningPartBCExtract, ct);
        if (ubagRoute is not null
            && string.Equals(ubagRoute.ProviderCode, UbagProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            return await CallUbagAnswersAsync(
                part, questionMarkdown, answerMarkdown, adminId, lease, ubagRoute.Model, ct);
        }

        var row = await registry.FindByCodeAsync(AnthropicProviderCode, ct)
            ?? throw new InvalidOperationException(
                $"Anthropic provider '{AnthropicProviderCode}' is not registered. Add a row in /admin/ai-providers with Code={AnthropicProviderCode}.");

        var model = string.IsNullOrWhiteSpace(row.DefaultModel) ? DefaultModel : row.DefaultModel;
        var apiKey = await registry.GetPlatformKeyAsync(AnthropicProviderCode, ct)
            ?? throw new InvalidOperationException($"Platform API key missing for provider {AnthropicProviderCode}.");
        var baseUrl = string.IsNullOrWhiteSpace(row.BaseUrl) ? null : AnthropicProvider.NormalizeBaseUrl(row.BaseUrl);
        if (baseUrl is not null)
        {
            var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
            if (unsafeReason is not null) throw new InvalidOperationException(unsafeReason);
        }

        var range = part == "B" ? "25-30 (six 3-option MCQs)" : "31-42 (twelve 3-option MCQs)";
        var userText =
            $"PART: {part} — questions {range}.\n\n" +
            "QUESTION PAPER (OCR Markdown):\n\n" + questionMarkdown +
            "\n\n=====\n\nANSWER KEY (OCR Markdown):\n\n" + answerMarkdown;

        var startedAt = clock.GetUtcNow();
        var usageContext = new AiUsageContext(
            UserId: adminId,
            AuthAccountId: null,
            TenantId: null,
            FeatureCode: AiFeatureCodes.ListeningPartBCExtract,
            RulebookVersion: null,
            PromptTemplateId: ToolName,
            SystemPrompt: SystemPrompt,
            UserPrompt: userText,
            StartedAt: startedAt);
        int LatencyMs() => (int)(clock.GetUtcNow() - startedAt).TotalMilliseconds;

        try
        {
            var completion = await new AnthropicProvider(httpClientFactory, registry).CompleteAsync(
                new AiProviderRequest
                {
                    ProviderCode = AnthropicProviderCode,
                    Model = model,
                    SystemPrompt = SystemPrompt,
                    UserPrompt = userText,
                    MaxTokens = 8000,
                    ApiKeyOverride = apiKey,
                    BaseUrlOverride = baseUrl,
                    Tools =
                    [
                        new AiToolDefinition(
                            ToolName,
                            ToolName,
                            "Emit the OET Listening Part B/C source question, all three options, correct option and rationale for every requested item.",
                            AiToolCategory.Read,
                            ToolSchemaJson),
                    ],
                    ToolChoice = ToolName,
                },
                ct);

            var toolCall = completion.ToolCalls?.FirstOrDefault(call =>
                string.Equals(call.ToolCode, ToolName, StringComparison.Ordinal));
            if (toolCall is null || string.IsNullOrWhiteSpace(toolCall.ArgsJson))
            {
                await usageRecorder.RecordFailureAsync(
                    usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
                    "no_tool_use", "Claude did not return a tool_use answers block.",
                    LatencyMs(), "listening.partbc.extract", CancellationToken.None,
                    operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
                throw new InvalidOperationException("Claude did not return a tool_use answers block.");
            }

            var usage = completion.Usage;
            var cost = usage is null
                ? 0m
                : row.PricePer1kPromptTokens * usage.PromptTokens / 1000m
                  + row.PricePer1kCompletionTokens * usage.CompletionTokens / 1000m;
            await usageRecorder.RecordSuccessAsync(
                usageContext, AnthropicProviderCode, model, usage,
                LatencyMs(), "listening.partbc.extract", cost, CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            return toolCall.ArgsJson;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiProviderHttpException ex)
        {
            await usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
                $"http_{ex.StatusCode}",
                $"Anthropic returned HTTP {ex.StatusCode} for {AiFeatureCodes.ListeningPartBCExtract}.",
                LatencyMs(), "listening.partbc.extract", CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            throw new InvalidOperationException($"Claude extraction failed: HTTP {ex.StatusCode}.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            await usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
                "anthropic_network", $"Anthropic transport failure ({ex.GetType().Name}).",
                LatencyMs(), "listening.partbc.extract", CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            throw;
        }
    }

    /// <summary>UBAG route for the Part B/C answers call. Same OCR markdown,
    /// forced emit_part_bc_answers tool definition plus response_format
    /// json_object through the registry (forced-tool emulation surfaces the
    /// JSON as ArgsJson). Usage is recorded against ubag.</summary>
    private async Task<string> CallUbagAnswersAsync(
        string part, string questionMarkdown, string answerMarkdown, string adminId,
        DirectAiOperationLease lease, string? routeModel, CancellationToken ct)
    {
        var row = await registry.FindByCodeAsync(UbagProviderCode, ct)
            ?? throw new InvalidOperationException(
                $"UBAG provider '{UbagProviderCode}' is not registered. Add a row in /admin/ai-providers with Code={UbagProviderCode}.");
        var model = !string.IsNullOrWhiteSpace(routeModel)
            ? routeModel.Trim()
            : string.IsNullOrWhiteSpace(row.DefaultModel) ? "chatgpt_web" : row.DefaultModel;
        var apiKey = await registry.GetPlatformKeyAsync(UbagProviderCode, ct)
            ?? throw new InvalidOperationException($"Platform API key missing for provider {UbagProviderCode}.");
        var baseUrl = string.IsNullOrWhiteSpace(row.BaseUrl) ? null : row.BaseUrl.Trim().TrimEnd('/');
        if (baseUrl is not null)
        {
            var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
            if (unsafeReason is not null) throw new InvalidOperationException(unsafeReason);
        }

        var range = part == "B" ? "25-30 (six 3-option MCQs)" : "31-42 (twelve 3-option MCQs)";
        var userText =
            $"PART: {part} — questions {range}.\n\n" +
            "QUESTION PAPER (OCR Markdown):\n\n" + questionMarkdown +
            "\n\n=====\n\nANSWER KEY (OCR Markdown):\n\n" + answerMarkdown;

        var startedAt = clock.GetUtcNow();
        var usageContext = new AiUsageContext(
            UserId: adminId,
            AuthAccountId: null,
            TenantId: null,
            FeatureCode: AiFeatureCodes.ListeningPartBCExtract,
            RulebookVersion: null,
            PromptTemplateId: ToolName,
            SystemPrompt: SystemPrompt,
            UserPrompt: userText,
            StartedAt: startedAt);
        int LatencyMs() => (int)(clock.GetUtcNow() - startedAt).TotalMilliseconds;

        try
        {
            var completion = await new RegistryBackedProvider(httpClientFactory, registry, providerOptions).CompleteAsync(
                new AiProviderRequest
                {
                    ProviderCode = UbagProviderCode,
                    Model = model,
                    SystemPrompt = SystemPrompt,
                    UserPrompt = userText,
                    MaxTokens = 8000,
                    ApiKeyOverride = apiKey,
                    BaseUrlOverride = baseUrl,
                    ResponseFormatJson = "json_object",
                    Tools =
                    [
                        new AiToolDefinition(
                            ToolName,
                            ToolName,
                            "Emit the OET Listening Part B/C source question, all three options, correct option and rationale for every requested item.",
                            AiToolCategory.Read,
                            ToolSchemaJson),
                    ],
                    ToolChoice = ToolName,
                },
                ct);

            var toolCall = completion.ToolCalls?.FirstOrDefault(call =>
                string.Equals(call.ToolCode, ToolName, StringComparison.Ordinal));
            if (toolCall is null || string.IsNullOrWhiteSpace(toolCall.ArgsJson))
            {
                await usageRecorder.RecordFailureAsync(
                    usageContext, UbagProviderCode, model, AiCallOutcome.ProviderError,
                    "no_tool_use", "UBAG did not return an emit_part_bc_answers JSON block.",
                    LatencyMs(), "listening.partbc.extract", CancellationToken.None,
                    operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
                throw new InvalidOperationException("UBAG did not return an emit_part_bc_answers JSON block.");
            }

            var usage = completion.Usage;
            var cost = usage is null
                ? 0m
                : row.PricePer1kPromptTokens * usage.PromptTokens / 1000m
                  + row.PricePer1kCompletionTokens * usage.CompletionTokens / 1000m;
            await usageRecorder.RecordSuccessAsync(
                usageContext, UbagProviderCode, model, usage,
                LatencyMs(), "listening.partbc.extract", cost, CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            return toolCall.ArgsJson;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            await usageRecorder.RecordFailureAsync(
                usageContext, UbagProviderCode, model, AiCallOutcome.ProviderError,
                "ubag_network", $"UBAG transport failure ({ex.GetType().Name}).",
                LatencyMs(), "listening.partbc.extract", CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            throw;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ── Prompt + tool schema ─────────────────────────────────────────────────

    private const string SystemPrompt = """
You are an expert OET (Occupational English Test) content engineer. Read OCR'd OET
Listening Part B or Part C material and emit the answer key by calling the
emit_part_bc_answers tool.

OET Listening Part B = questions 25-30: six short 3-option multiple-choice questions
(options A, B, C), each about a brief workplace extract.
OET Listening Part C = questions 31-42: twelve 3-option multiple-choice questions
(options A, B, C) across two extracts (31-36, then 37-42).

You receive a QUESTION PAPER (the printed MCQs with their A/B/C options) and an
ANSWER KEY (the official correct option per question). You are told which PART to emit.

For EVERY question in the requested part's range, emit one entry with:
  - number: the printed question number.
  - stem: the full question text ("stem") EXACTLY as printed on the QUESTION PAPER — the
    prompt the candidate answers. Do NOT include the A/B/C options inside the stem.
  - optionA / optionB / optionC: the full text of options A, B and C EXACTLY as printed,
    WITHOUT the leading "A."/"B."/"C." label.
  - correctAnswer: the correct option LETTER — exactly one of "A", "B", or "C" — taken
    from the ANSWER KEY (cross-check it against the question paper).
  - rationale: ONE concise sentence (< 240 chars) explaining why that option is correct,
    grounded in the printed options. This is shown to the learner after they submit.

HARD REQUIREMENTS:
  - Emit ONLY questions in the requested part's range (Part B → 25-30; Part C → 31-42).
    Never invent numbers outside that range.
  - correctAnswer MUST be a single uppercase letter: A, B, or C.
  - Provide EVERY question in the range. If the source text is unclear, emit an empty
    stem or option field for that item and let the deterministic validator flag it for
    human correction; never substitute a heading, "See PDF", "Option A/B/C", or guessed prose.
  - Transcribe stem + optionA/B/C VERBATIM from the QUESTION PAPER. If the OCR is unclear,
    preserve the source wording when legible and otherwise leave the field empty for a
    reviewer. Do not invent, summarize, or reconstruct content from answer choices.
  - Do not fabricate. Base each rationale on the actual printed options.
""";

    private const string ToolSchemaJson = """
{
  "type": "object",
  "properties": {
    "answers": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "number": { "type": "integer" },
          "stem": { "type": "string" },
          "optionA": { "type": "string" },
          "optionB": { "type": "string" },
          "optionC": { "type": "string" },
          "correctAnswer": { "type": "string", "enum": ["A", "B", "C"] },
          "rationale": { "type": "string" }
        },
        "required": ["number", "stem", "optionA", "optionB", "optionC", "correctAnswer"]
      }
    }
  },
  "required": ["answers"]
}
""";
}
