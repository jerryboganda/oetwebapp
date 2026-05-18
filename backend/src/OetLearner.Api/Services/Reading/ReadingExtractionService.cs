using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Extraction Service — Phase 6
//
// AI-assisted PDF → ReadingStructureManifest pipeline:
//   1. Admin uploads a PDF (via the existing MediaAsset slice).
//   2. CreateDraftAsync(paperId, mediaAssetId) calls IReadingExtractionAi
//      which is the swappable AI seam. When the gateway is unavailable
//      (no AI:BaseUrl configured, network failure, refusal), we fall back
//      to a deterministic stub so the admin UI still has something to act
//      on — the draft is flagged IsStub=true.
//   3. ApproveDraftAsync(draftId) re-uses ImportManifestAsync to apply the
//      manifest to the paper (replaces existing structure).
//   4. RejectDraftAsync(draftId, reason) records the rejection with audit.
//
// Mission-critical guardrails:
//   • Honours ReadingPolicy.AiExtractionEnabled (kill-switch).
//   • Honours ReadingPolicy.AiExtractionMaxRetriesPerPaper (per paper).
//   • Honours ReadingPolicy.AiExtractionRequireHumanApproval — when false
//     (rare, opt-in), CreateDraftAsync auto-approves the result.
//   • Every state change writes an AuditEvent.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// AI seam for the actual PDF→manifest extraction. The default
/// implementation calls the grounded AI gateway; tests substitute a stub
/// that returns a known manifest.
/// </summary>
public interface IReadingExtractionAi
{
    Task<ReadingExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct);
}

public sealed record ReadingExtractionAiResult(
    ReadingStructureManifest Manifest,
    string? RawResponseJson,
    bool IsStub,
    string? StubReason);

public interface IReadingExtractionService
{
    Task<ReadingExtractionDraft> CreateDraftAsync(
        string paperId,
        string? mediaAssetId,
        string adminId,
        CancellationToken ct);

    Task<ReadingExtractionDraft?> GetDraftAsync(string draftId, CancellationToken ct);

    Task<IReadOnlyList<ReadingExtractionDraft>> ListDraftsAsync(
        string paperId,
        CancellationToken ct);

    Task<ReadingExtractionDraft> ApproveDraftAsync(
        string draftId,
        string adminId,
        CancellationToken ct);

    Task<ReadingExtractionDraft> RejectDraftAsync(
        string draftId,
        string adminId,
        string? reason,
        CancellationToken ct);
}

public sealed class ReadingExtractionService(
    LearnerDbContext db,
    IReadingExtractionAi ai,
    IReadingStructureService structure,
    IReadingPolicyService policyService,
    IWebHostEnvironment environment) : IReadingExtractionService
{
    public async Task<ReadingExtractionDraft> CreateDraftAsync(
        string paperId,
        string? mediaAssetId,
        string adminId,
        CancellationToken ct)
    {
        var policy = await policyService.GetGlobalAsync(ct);
        if (!policy.AiExtractionEnabled)
            throw new InvalidOperationException("AI extraction is disabled by policy.");

        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");
        if (!string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Paper is not a Reading paper.");

        var attemptedSoFar = await db.ReadingExtractionDrafts.AsNoTracking()
            .CountAsync(d => d.PaperId == paperId, ct);
        if (policy.AiExtractionMaxRetriesPerPaper > 0
            && attemptedSoFar >= policy.AiExtractionMaxRetriesPerPaper)
        {
            throw new InvalidOperationException(
                $"Max AI extractions ({policy.AiExtractionMaxRetriesPerPaper}) reached for this paper.");
        }

        ReadingExtractionAiResult aiResult;
        try
        {
            aiResult = await ai.ExtractAsync(paperId, mediaAssetId, ct);
        }
        catch (Exception ex)
        {
            // Persist the failure as a Failed draft so the admin sees it.
            var failed = new ReadingExtractionDraft
            {
                Id = Guid.NewGuid().ToString("N"),
                PaperId = paperId,
                MediaAssetId = mediaAssetId,
                Status = ReadingExtractionStatus.Failed,
                Notes = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message,
                IsStub = false,
                CreatedByAdminId = adminId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ReadingExtractionDrafts.Add(failed);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = failed.CreatedAt,
                ActorId = adminId,
                ActorName = adminId,
                Action = "ReadingExtractionFailed",
                ResourceType = "ContentPaper",
                ResourceId = paperId,
                Details = $"draftId={failed.Id} error={failed.Notes}",
            });
            await db.SaveChangesAsync(ct);
            return failed;
        }

        var manifestJson = JsonSerializer.Serialize(aiResult.Manifest);
        var draft = new ReadingExtractionDraft
        {
            Id = Guid.NewGuid().ToString("N"),
            PaperId = paperId,
            MediaAssetId = mediaAssetId,
            Status = ReadingExtractionStatus.Pending,
            ExtractedManifestJson = manifestJson,
            RawAiResponseJson = aiResult.RawResponseJson is { Length: > 65536 }
                ? aiResult.RawResponseJson[..65536]
                : aiResult.RawResponseJson,
            IsStub = aiResult.IsStub,
            Notes = aiResult.IsStub ? aiResult.StubReason : null,
            CreatedByAdminId = adminId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ReadingExtractionDrafts.Add(draft);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = draft.CreatedAt,
            ActorId = adminId,
            ActorName = adminId,
            Action = "ReadingExtractionDraftCreated",
            ResourceType = "ContentPaper",
            ResourceId = paperId,
            Details = $"draftId={draft.Id} stub={draft.IsStub}",
        });
        await db.SaveChangesAsync(ct);

        // Production invariant: AI-generated Reading structure is never
        // auto-approved. The policy toggle is only honoured in Development /
        // automated tests so engineers can exercise import paths quickly.
        if (!policy.AiExtractionRequireHumanApproval
            && !draft.IsStub
            && IsNonProductionAutoApprovalAllowed(environment))
        {
            return await ApproveDraftAsync(draft.Id, adminId, ct);
        }

        return draft;
    }

    public async Task<ReadingExtractionDraft?> GetDraftAsync(string draftId, CancellationToken ct)
        => await db.ReadingExtractionDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == draftId, ct);

    public async Task<IReadOnlyList<ReadingExtractionDraft>> ListDraftsAsync(
        string paperId,
        CancellationToken ct)
        => await db.ReadingExtractionDrafts.AsNoTracking()
            .Where(d => d.PaperId == paperId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    public async Task<ReadingExtractionDraft> ApproveDraftAsync(
        string draftId,
        string adminId,
        CancellationToken ct)
    {
        var draft = await db.ReadingExtractionDrafts.FirstOrDefaultAsync(d => d.Id == draftId, ct)
            ?? throw new InvalidOperationException("Extraction draft not found.");
        if (draft.Status != ReadingExtractionStatus.Pending)
            throw new InvalidOperationException($"Draft is already {draft.Status}.");
        if (string.IsNullOrWhiteSpace(draft.ExtractedManifestJson))
            throw new InvalidOperationException("Draft has no manifest to apply.");

        ReadingStructureManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReadingStructureManifest>(draft.ExtractedManifestJson)
                ?? throw new InvalidOperationException("Manifest deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Draft manifest is not valid JSON: {ex.Message}");
        }

        // Re-use the existing manifest importer — same validation, audit,
        // and structural guarantees as a manual import.
        await structure.ImportManifestAsync(draft.PaperId, manifest, replaceExisting: true, adminId, ct);

        draft.Status = ReadingExtractionStatus.Approved;
        draft.ResolvedByAdminId = adminId;
        draft.ResolvedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = draft.ResolvedAt.Value,
            ActorId = adminId,
            ActorName = adminId,
            Action = "ReadingExtractionDraftApproved",
            ResourceType = "ContentPaper",
            ResourceId = draft.PaperId,
            Details = $"draftId={draft.Id}",
        });
        await db.SaveChangesAsync(ct);
        return draft;
    }

    public async Task<ReadingExtractionDraft> RejectDraftAsync(
        string draftId,
        string adminId,
        string? reason,
        CancellationToken ct)
    {
        var draft = await db.ReadingExtractionDrafts.FirstOrDefaultAsync(d => d.Id == draftId, ct)
            ?? throw new InvalidOperationException("Extraction draft not found.");
        if (draft.Status != ReadingExtractionStatus.Pending)
            throw new InvalidOperationException($"Draft is already {draft.Status}.");

        draft.Status = ReadingExtractionStatus.Rejected;
        draft.ResolvedByAdminId = adminId;
        draft.ResolvedAt = DateTimeOffset.UtcNow;
        draft.Notes = string.IsNullOrWhiteSpace(reason) ? draft.Notes : reason;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = draft.ResolvedAt.Value,
            ActorId = adminId,
            ActorName = adminId,
            Action = "ReadingExtractionDraftRejected",
            ResourceType = "ContentPaper",
            ResourceId = draft.PaperId,
            Details = $"draftId={draft.Id} reason={reason}",
        });
        await db.SaveChangesAsync(ct);
        return draft;
    }

    private static bool IsNonProductionAutoApprovalAllowed(IHostEnvironment env)
        => env.IsDevelopment()
           || env.IsEnvironment("Test")
           || env.IsEnvironment("Testing");
}

/// <summary>
/// Production Reading extraction implementation. It only calls AI through
/// <see cref="IAiGatewayService"/> using a Reading rulebook-grounded prompt and
/// <see cref="AiFeatureCodes.AdminReadingDraft"/> (platform-only). Invalid JSON
/// or non-canonical 20/6/16 output is rejected before any draft can be staged.
/// </summary>
public sealed class GroundedReadingExtractionAi(
    LearnerDbContext db,
    IAiGatewayService gateway,
    ILogger<GroundedReadingExtractionAi> logger)
    : IReadingExtractionAi
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public async Task<ReadingExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("ContentPaper not found.");

        var sourceBundle = BuildSourceMessage(paper, mediaAssetId);
        if (!sourceBundle.HasExtractedText)
            throw new InvalidOperationException("No extracted Reading PDF text was found on the paper. Run PDF text extraction before AI extraction.");

        AiGroundedPrompt prompt;
        try
        {
            prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Reading,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.GenerateReadingStructure,
            });
        }
        catch (Exception ex) when (ex is RulebookNotFoundException or PromptNotGroundedException)
        {
            logger.LogError(ex, "Grounded Reading prompt build failed for paper {PaperId}", paperId);
            throw new InvalidOperationException("Grounded Reading prompt build failed: " + ex.Message, ex);
        }

        AiGatewayResult result;
        try
        {
            result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = sourceBundle.Message,
                FeatureCode = AiFeatureCodes.AdminReadingDraft,
                Temperature = 0.0,
                MaxTokens = 8000,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI gateway Reading extraction failed for paper {PaperId}", paperId);
            throw new InvalidOperationException("AI gateway Reading extraction failed: " + ex.Message, ex);
        }

        var manifest = ParseAndValidate(result.Completion);
        return new ReadingExtractionAiResult(
            Manifest: manifest,
            RawResponseJson: result.Completion,
            IsStub: false,
            StubReason: null);
    }

    private static (string Message, bool HasExtractedText) BuildSourceMessage(ContentPaper paper, string? mediaAssetId)
    {
        Dictionary<string, string>? extractedByAsset = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(paper.ExtractedTextJson))
            {
                var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson, JsonOpts);
                if (root is not null)
                {
                    extractedByAsset = root
                        .Where(kvp => kvp.Value.ValueKind == JsonValueKind.String)
                        .ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value.GetString() ?? string.Empty,
                            StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (JsonException)
        {
            extractedByAsset = null;
        }

        extractedByAsset ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine($"# Reading paper source bundle — paperId {paper.Id}");
        sb.AppendLine();
        sb.AppendLine($"Title: {paper.Title}");
        sb.AppendLine($"Slug: {paper.Slug}");
        sb.AppendLine("Instruction: Extract only from the supplied source text. Do not guess missing answer keys.");
        sb.AppendLine();

        var hasExtractedText = false;
        var assets = paper.Assets
            .Where(a => a.Role is PaperAssetRole.QuestionPaper or PaperAssetRole.AnswerKey)
            .Where(a => string.IsNullOrWhiteSpace(mediaAssetId)
                || string.Equals(a.MediaAssetId, mediaAssetId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Id, mediaAssetId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Role)
            .ThenBy(a => a.DisplayOrder)
            .ToList();

        foreach (var asset in assets)
        {
            if (!extractedByAsset.TryGetValue(asset.Id, out var text)
                && !string.IsNullOrWhiteSpace(asset.MediaAssetId))
            {
                extractedByAsset.TryGetValue(asset.MediaAssetId, out text);
            }

            if (string.IsNullOrWhiteSpace(text)) continue;
            hasExtractedText = true;
            sb.AppendLine($"## {asset.Role} {asset.Part}".Trim());
            sb.AppendLine($"AssetId: {asset.Id}");
            if (!string.IsNullOrWhiteSpace(asset.MediaAsset?.OriginalFilename))
                sb.AppendLine($"Filename: {asset.MediaAsset.OriginalFilename}");
            sb.AppendLine("```");
            sb.AppendLine(text.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        return (sb.ToString(), hasExtractedText);
    }

    private static ReadingStructureManifest ParseAndValidate(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion))
            throw new InvalidOperationException("AI returned an empty completion.");

        var json = StripFences(completion);
        ReadingExtractionReply? reply;
        try
        {
            reply = JsonSerializer.Deserialize<ReadingExtractionReply>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("AI reply was not valid JSON: " + ex.Message, ex);
        }

        if (reply?.Parts is null || reply.Parts.Count != 3)
            throw new InvalidOperationException("AI reply must contain exactly three parts.");

        var parts = new List<ReadingPartManifest>(3);
        var seen = new HashSet<ReadingPartCode>();
        foreach (var part in reply.Parts)
        {
            var partCode = ParsePartCode(part.PartCode);
            if (!seen.Add(partCode))
                throw new InvalidOperationException($"Duplicate Reading part {partCode}.");

            var texts = part.Texts ?? new List<ReadingExtractionTextReply>();
            var questions = part.Questions ?? new List<ReadingExtractionQuestionReply>();
            var expectedTexts = partCode switch { ReadingPartCode.A => 4, ReadingPartCode.B => 6, ReadingPartCode.C => 2, _ => 0 };
            var expectedQuestions = partCode switch { ReadingPartCode.A => 20, ReadingPartCode.B => 6, ReadingPartCode.C => 16, _ => 0 };
            if (texts.Count != expectedTexts)
                throw new InvalidOperationException($"Part {partCode} must contain exactly {expectedTexts} texts; got {texts.Count}.");
            if (questions.Count != expectedQuestions)
                throw new InvalidOperationException($"Part {partCode} must contain exactly {expectedQuestions} questions; got {questions.Count}.");

            var textOrders = new HashSet<int>();
            var mappedTexts = texts
                .OrderBy(t => t.DisplayOrder)
                .Select(t =>
                {
                    if (t.DisplayOrder <= 0 || !textOrders.Add(t.DisplayOrder))
                        throw new InvalidOperationException($"Part {partCode} contains an invalid or duplicate text displayOrder.");
                    RequireText(t.Title, $"Part {partCode} text {t.DisplayOrder} title");
                    RequireText(t.BodyHtml, $"Part {partCode} text {t.DisplayOrder} bodyHtml");
                    return new ReadingTextManifest(
                        t.DisplayOrder,
                        t.Title!.Trim(),
                        string.IsNullOrWhiteSpace(t.Source) ? null : t.Source.Trim(),
                        t.BodyHtml!.Trim(),
                        t.WordCount > 0 ? t.WordCount : EstimateWordCount(t.BodyHtml!),
                        string.IsNullOrWhiteSpace(t.TopicTag) ? null : t.TopicTag.Trim());
                })
                .ToList();

            var questionOrders = new HashSet<int>();
            var mappedQuestions = questions
                .OrderBy(q => q.DisplayOrder)
                .Select(q =>
                {
                    if (q.DisplayOrder <= 0 || !questionOrders.Add(q.DisplayOrder))
                        throw new InvalidOperationException($"Part {partCode} contains an invalid or duplicate question displayOrder.");
                    RequireText(q.Stem, $"Part {partCode} question {q.DisplayOrder} stem");
                    var type = ParseQuestionType(q.QuestionType);
                    ValidateTypeForPart(partCode, type, q);
                    if (q.ReadingTextDisplayOrder is not null && !textOrders.Contains(q.ReadingTextDisplayOrder.Value))
                        throw new InvalidOperationException($"Part {partCode} question {q.DisplayOrder} references unknown text displayOrder {q.ReadingTextDisplayOrder}.");

                    var optionsJson = RawOrSerialized(q.Options, type is ReadingQuestionType.ShortAnswer ? "[]" : null, $"Part {partCode} question {q.DisplayOrder} options");
                    var correctAnswerJson = RawOrSerialized(q.CorrectAnswer, null, $"Part {partCode} question {q.DisplayOrder} correctAnswer");
                    var acceptedSynonymsJson = q.AcceptedSynonyms.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                        ? null
                        : q.AcceptedSynonyms.GetRawText();

                    return new ReadingQuestionManifest(
                        q.DisplayOrder,
                        q.Points <= 0 ? 1 : q.Points,
                        type,
                        q.Stem!.Trim(),
                        optionsJson,
                        correctAnswerJson,
                        string.IsNullOrWhiteSpace(acceptedSynonymsJson) ? null : acceptedSynonymsJson,
                        q.CaseSensitive,
                        string.IsNullOrWhiteSpace(q.ExplanationMarkdown) ? null : q.ExplanationMarkdown.Trim(),
                        string.IsNullOrWhiteSpace(q.SkillTag) ? null : q.SkillTag.Trim(),
                        q.ReadingTextDisplayOrder,
                        OptionDistractorsJson: q.OptionDistractors.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                            ? null
                            : q.OptionDistractors.GetRawText(),
                        ReviewState: ReadingReviewState.Draft);
                })
                .ToList();

            parts.Add(new ReadingPartManifest(
                partCode,
                part.TimeLimitMinutes,
                string.IsNullOrWhiteSpace(part.Instructions) ? null : part.Instructions.Trim(),
                mappedTexts,
                mappedQuestions));
        }

        if (!seen.SetEquals(new[] { ReadingPartCode.A, ReadingPartCode.B, ReadingPartCode.C }))
            throw new InvalidOperationException("AI reply must include parts A, B, and C exactly once.");

        return new ReadingStructureManifest(parts.OrderBy(p => p.PartCode).ToList());
    }

    private static string StripFences(string s)
    {
        var t = s.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = t.IndexOf('\n');
            if (nl >= 0) t = t[(nl + 1)..];
            if (t.EndsWith("```", StringComparison.Ordinal)) t = t[..^3];
        }
        return t.Trim();
    }

    private static ReadingPartCode ParsePartCode(string? value)
        => Enum.TryParse<ReadingPartCode>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid Reading partCode '{value}'.");

    private static ReadingQuestionType ParseQuestionType(string? value)
        => Enum.TryParse<ReadingQuestionType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid Reading questionType '{value}'.");

    private static void ValidateTypeForPart(ReadingPartCode partCode, ReadingQuestionType type, ReadingExtractionQuestionReply q)
    {
        if (partCode == ReadingPartCode.B && type != ReadingQuestionType.MultipleChoice3)
            throw new InvalidOperationException($"Part B question {q.DisplayOrder} must be MultipleChoice3.");
        if (partCode == ReadingPartCode.C && type != ReadingQuestionType.MultipleChoice4)
            throw new InvalidOperationException($"Part C question {q.DisplayOrder} must be MultipleChoice4.");
        if (partCode == ReadingPartCode.A
            && type is not (ReadingQuestionType.MatchingTextReference or ReadingQuestionType.ShortAnswer or ReadingQuestionType.SentenceCompletion))
            throw new InvalidOperationException($"Part A question {q.DisplayOrder} has invalid type {type}.");

        if (partCode == ReadingPartCode.B || partCode == ReadingPartCode.C)
        {
            var expectedOptions = partCode == ReadingPartCode.B ? 3 : 4;
            if (q.Options.ValueKind != JsonValueKind.Array || q.Options.GetArrayLength() != expectedOptions)
                throw new InvalidOperationException($"Part {partCode} question {q.DisplayOrder} must have exactly {expectedOptions} options.");
            var answer = q.CorrectAnswer.ValueKind == JsonValueKind.String ? q.CorrectAnswer.GetString() : null;
            var allowed = partCode == ReadingPartCode.B ? "ABC" : "ABCD";
            if (string.IsNullOrWhiteSpace(answer) || answer.Length != 1 || !allowed.Contains(char.ToUpperInvariant(answer[0])))
                throw new InvalidOperationException($"Part {partCode} question {q.DisplayOrder} must have a correctAnswer in {{{string.Join(",", allowed.ToCharArray())}}}.");
        }
    }

    private static string RawOrSerialized(JsonElement value, string? fallback, string label)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (fallback is not null) return fallback;
            throw new InvalidOperationException($"{label} is required.");
        }
        return value.GetRawText();
    }

    private static void RequireText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");
    }

    private static int EstimateWordCount(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private sealed class ReadingExtractionReply
    {
        public List<ReadingExtractionPartReply>? Parts { get; set; }
    }

    private sealed class ReadingExtractionPartReply
    {
        public string? PartCode { get; set; }
        public int? TimeLimitMinutes { get; set; }
        public string? Instructions { get; set; }
        public List<ReadingExtractionTextReply>? Texts { get; set; }
        public List<ReadingExtractionQuestionReply>? Questions { get; set; }
    }

    private sealed class ReadingExtractionTextReply
    {
        public int DisplayOrder { get; set; }
        public string? Title { get; set; }
        public string? Source { get; set; }
        public string? BodyHtml { get; set; }
        public int WordCount { get; set; }
        public string? TopicTag { get; set; }
    }

    private sealed class ReadingExtractionQuestionReply
    {
        public int DisplayOrder { get; set; }
        public int Points { get; set; } = 1;
        public string? QuestionType { get; set; }
        public string? Stem { get; set; }
        public JsonElement Options { get; set; }
        public JsonElement CorrectAnswer { get; set; }
        public JsonElement AcceptedSynonyms { get; set; }
        public bool CaseSensitive { get; set; }
        public string? ExplanationMarkdown { get; set; }
        public string? SkillTag { get; set; }
        public int? ReadingTextDisplayOrder { get; set; }
        public JsonElement OptionDistractors { get; set; }
    }
}

/// <summary>
/// Development/test AI implementation. It returns a canonical 20+6+16
/// placeholder manifest so local admin UI can exercise review/import flows
/// without configured AI credentials. Production DI uses
/// <see cref="GroundedReadingExtractionAi"/> instead.
/// </summary>
public sealed class StubReadingExtractionAi : IReadingExtractionAi
{
    public Task<ReadingExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        var partA = new ReadingPartManifest(
            ReadingPartCode.A, 15, "Match each item to the correct text.",
            new[]
            {
                new ReadingTextManifest(1, "Text A1 (extracted placeholder)", "Placeholder source",
                    "<p>Extraction placeholder. Replace with real content.</p>", 80, "general"),
            },
            Enumerable.Range(1, 20).Select(i =>
                new ReadingQuestionManifest(
                    DisplayOrder: i,
                    Points: 1,
                    QuestionType: ReadingQuestionType.ShortAnswer,
                    Stem: $"Short-answer item {i}",
                    OptionsJson: "[]",
                    CorrectAnswerJson: $"\"placeholder-{i}\"",
                    AcceptedSynonymsJson: null,
                    CaseSensitive: false,
                    ExplanationMarkdown: null,
                    SkillTag: "scan",
                    ReadingTextDisplayOrder: 1)).ToList());

        var partB = new ReadingPartManifest(
            ReadingPartCode.B, null, "Choose the option that best matches the text.",
            new[]
            {
                new ReadingTextManifest(1, "Text B1 (extracted placeholder)", "Placeholder source",
                    "<p>Extraction placeholder. Replace with real content.</p>", 100, "workplace"),
            },
            Enumerable.Range(1, 6).Select(i =>
                new ReadingQuestionManifest(
                    DisplayOrder: i,
                    Points: 1,
                    QuestionType: ReadingQuestionType.MultipleChoice3,
                    Stem: $"MCQ3 item {i}",
                    OptionsJson: "[\"A option\",\"B option\",\"C option\"]",
                    CorrectAnswerJson: "\"A\"",
                    AcceptedSynonymsJson: null,
                    CaseSensitive: false,
                    ExplanationMarkdown: null,
                    SkillTag: "purpose",
                    ReadingTextDisplayOrder: 1)).ToList());

        var partC = new ReadingPartManifest(
            ReadingPartCode.C, null, "Choose the option that best answers the question.",
            new[]
            {
                new ReadingTextManifest(1, "Text C1 (extracted placeholder)", "Placeholder source",
                    "<p>Extraction placeholder. Replace with real content.</p>", 600, "research"),
            },
            Enumerable.Range(1, 16).Select(i =>
                new ReadingQuestionManifest(
                    DisplayOrder: i,
                    Points: 1,
                    QuestionType: ReadingQuestionType.MultipleChoice4,
                    Stem: $"MCQ4 item {i}",
                    OptionsJson: "[\"A option\",\"B option\",\"C option\",\"D option\"]",
                    CorrectAnswerJson: "\"A\"",
                    AcceptedSynonymsJson: null,
                    CaseSensitive: false,
                    ExplanationMarkdown: null,
                    SkillTag: "inference",
                    ReadingTextDisplayOrder: 1)).ToList());

        var manifest = new ReadingStructureManifest(new[] { partA, partB, partC });
        return Task.FromResult(new ReadingExtractionAiResult(
            Manifest: manifest,
            RawResponseJson: null,
            IsStub: true,
            StubReason: "AI gateway not configured for Reading extraction; returning deterministic placeholder structure."));
    }
}

public sealed class DisabledReadingExtractionAi : IReadingExtractionAi
{
    public Task<ReadingExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        throw new InvalidOperationException(
            "Reading AI extraction is disabled outside Development until a production provider is configured.");
    }
}
