using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
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
//   • Always requires explicit human approval before applying AI output.
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
    IReadingPolicyService policyService) : IReadingExtractionService
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
            // Persist a failed draft without exposing provider/internal details.
            var safeFailureMessage = SanitiseExtractionFailureMessage(ex);
            var failed = new ReadingExtractionDraft
            {
                Id = Guid.NewGuid().ToString("N"),
                PaperId = paperId,
                MediaAssetId = mediaAssetId,
                Status = ReadingExtractionStatus.Failed,
                Notes = safeFailureMessage,
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
                Details = $"draftId={failed.Id} errorCode=READING_EXTRACTION_FAILED",
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
            RawAiResponseJson = BuildRetainedAiResponseMetadata(aiResult.RawResponseJson),
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
        if (draft.IsStub)
            throw new InvalidOperationException("Stub Reading extraction drafts cannot be approved. Re-run extraction after source text and AI provider configuration are ready.");
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
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
            throw new InvalidOperationException("A rejection reason is required.");
        if (trimmedReason.Length > 500)
            throw new InvalidOperationException("Rejection reason must be 500 characters or fewer.");

        draft.Status = ReadingExtractionStatus.Rejected;
        draft.ResolvedByAdminId = adminId;
        draft.ResolvedAt = DateTimeOffset.UtcNow;
        draft.Notes = trimmedReason;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = draft.ResolvedAt.Value,
            ActorId = adminId,
            ActorName = adminId,
            Action = "ReadingExtractionDraftRejected",
            ResourceType = "ContentPaper",
            ResourceId = draft.PaperId,
            Details = $"draftId={draft.Id} reasonLength={trimmedReason.Length}",
        });
        await db.SaveChangesAsync(ct);
        return draft;
    }

    private static string SanitiseExtractionFailureMessage(Exception exception)
        => exception switch
        {
            OperationCanceledException => "Reading extraction was cancelled.",
            _ => "Reading extraction failed. Check secure server logs for provider details.",
        };

    private static string? BuildRetainedAiResponseMetadata(string? rawResponseJson)
    {
        if (string.IsNullOrWhiteSpace(rawResponseJson))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(rawResponseJson);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            responseSha256 = sha256,
            responseLength = bytes.Length,
            rawBodyStored = false,
        });
    }
}

/// <summary>
/// Production Reading extraction implementation. It builds a rulebook-grounded
/// prompt (RuleKind.Reading + GenerateReadingStructure), feeds only extracted
/// source text and answer-key text into the gateway, and validates the returned
/// manifest before creating a pending draft. Failures become non-approvable
/// stub drafts so the admin sees the blocker without applying unsafe content.
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
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ReadingExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == paperId, ct);

        if (paper is null)
            return await StubAsync("ContentPaper not found.", paperId, mediaAssetId, ct);

        var source = BuildSourceMessage(paper, mediaAssetId);
        if (!source.HasExtractedText)
            return await StubAsync("No extracted Reading source text was found. Run text extraction on the PDF assets first.", paperId, mediaAssetId, ct);

        AiGroundedPrompt prompt;
        try
        {
            prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Reading,
                Profession = ResolveProfession(paper),
                Task = AiTaskMode.GenerateReadingStructure,
            });
        }
        catch (Exception ex) when (ex is RulebookNotFoundException or PromptNotGroundedException)
        {
            logger.LogError(ex, "Grounded Reading prompt build failed for paper {PaperId}", paperId);
            return await StubAsync("Grounded Reading prompt could not be built: " + ex.Message, paperId, mediaAssetId, ct);
        }

        AiGatewayResult result;
        try
        {
            result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = source.Message,
                FeatureCode = AiFeatureCodes.AdminReadingDraft,
                Temperature = 0.1,
                MaxTokens = 8000,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI gateway Reading extraction failed for paper {PaperId}", paperId);
            return await StubAsync("AI gateway call failed; check AI provider configuration and usage policy.", paperId, mediaAssetId, ct);
        }

        var (manifest, error) = ParseAndValidate(result.Completion);
        if (manifest is null)
        {
            logger.LogWarning("Grounded Reading extraction parse failed for paper {PaperId}: {Error}", paperId, error);
            return await StubAsync("AI reply could not be parsed into a valid Reading manifest: " + error, paperId, mediaAssetId, ct);
        }

        return new ReadingExtractionAiResult(
            Manifest: manifest,
            RawResponseJson: result.Completion,
            IsStub: false,
            StubReason: null);
    }

    private static ExamProfession ResolveProfession(ContentPaper paper)
    {
        if (!string.IsNullOrWhiteSpace(paper.ProfessionId)
            && Enum.TryParse<ExamProfession>(paper.ProfessionId, ignoreCase: true, out var profession))
        {
            return profession;
        }
        return ExamProfession.Medicine;
    }

    private (string Message, bool HasExtractedText) BuildSourceMessage(ContentPaper paper, string? mediaAssetId)
    {
        var extracted = ParseExtractedText(paper.ExtractedTextJson);
        var sb = new StringBuilder();
        sb.AppendLine($"# Reading paper source bundle - paperId {paper.Id}");
        sb.AppendLine($"Title: {paper.Title}");
        sb.AppendLine($"Slug: {paper.Slug}");
        sb.AppendLine($"Difficulty: {paper.Difficulty}");
        sb.AppendLine();
        sb.AppendLine("Only use facts present in the supplied source bundle and answer-key text.");
        sb.AppendLine();

        var hasExtractedText = false;
        foreach (var asset in paper.Assets.OrderBy(a => a.Role).ThenBy(a => a.DisplayOrder))
        {
            if (mediaAssetId is not null
                && !string.Equals(asset.MediaAssetId, mediaAssetId, StringComparison.Ordinal)
                && !string.Equals(asset.Id, mediaAssetId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!extracted.TryGetValue(asset.Id, out var text)
                && asset.MediaAssetId is not null)
            {
                extracted.TryGetValue(asset.MediaAssetId, out text);
            }
            if (string.IsNullOrWhiteSpace(text)) continue;

            hasExtractedText = true;
            sb.AppendLine($"## Asset role: {asset.Role}");
            sb.AppendLine($"Asset id: {asset.Id}");
            sb.AppendLine("```");
            sb.AppendLine(text.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        return (sb.ToString(), hasExtractedText);
    }

    private static Dictionary<string, string> ParseExtractedText(string? extractedTextJson)
    {
        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(extractedTextJson)) return extracted;
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(extractedTextJson, JsonOpts);
            if (root is null) return extracted;
            foreach (var (key, value) in root)
            {
                if (value.ValueKind == JsonValueKind.String)
                    extracted[key] = value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Treat malformed extracted text as absent; the caller returns a stub.
        }
        return extracted;
    }

    private static (ReadingStructureManifest? Manifest, string? Error) ParseAndValidate(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion))
            return (null, "AI returned an empty completion.");
        var json = StripFences(completion);
        ReadingStructureManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReadingStructureManifest>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            return (null, "Reply was not valid JSON: " + ex.Message);
        }

        if (manifest?.Parts is null || manifest.Parts.Count != 3)
            return (null, "Reply must contain exactly 3 parts.");

        if (manifest.Parts.GroupBy(p => p.PartCode).Any(g => g.Count() > 1))
            return (null, "Reply contains duplicate Reading parts.");
        var parts = manifest.Parts.ToDictionary(p => p.PartCode);
        if (!parts.ContainsKey(ReadingPartCode.A) || !parts.ContainsKey(ReadingPartCode.B) || !parts.ContainsKey(ReadingPartCode.C))
            return (null, "Reply must contain Part A, Part B, and Part C.");
        if (parts[ReadingPartCode.A].Texts.Count != 4 || parts[ReadingPartCode.A].Questions.Count != 20)
            return (null, "Part A must contain 4 texts and 20 questions.");
        if (parts[ReadingPartCode.B].Texts.Count != 6 || parts[ReadingPartCode.B].Questions.Count != 6)
            return (null, "Part B must contain 6 texts and 6 questions.");
        if (parts[ReadingPartCode.C].Texts.Count != 2 || parts[ReadingPartCode.C].Questions.Count != 16)
            return (null, "Part C must contain 2 texts and 16 questions.");

        foreach (var part in manifest.Parts)
        {
            foreach (var q in part.Questions)
            {
                if (q.Points != 1) return (null, "Every Reading question must be worth 1 point.");
                if (!IsQuestionTypeAllowed(part.PartCode, q.QuestionType))
                    return (null, $"Question type {q.QuestionType} is not allowed in Part {part.PartCode}.");
                if (!IsValidJson(q.OptionsJson)) return (null, $"Question {q.DisplayOrder} has invalid optionsJson.");
                if (!IsValidJson(q.CorrectAnswerJson)) return (null, $"Question {q.DisplayOrder} has invalid correctAnswerJson.");
            }
        }

        return (manifest, null);
    }

    private static bool IsQuestionTypeAllowed(ReadingPartCode partCode, ReadingQuestionType questionType) => partCode switch
    {
        ReadingPartCode.A => questionType is ReadingQuestionType.MatchingTextReference
            or ReadingQuestionType.ShortAnswer
            or ReadingQuestionType.SentenceCompletion,
        ReadingPartCode.B => questionType == ReadingQuestionType.MultipleChoice3,
        ReadingPartCode.C => questionType == ReadingQuestionType.MultipleChoice4,
        _ => false,
    };

    private static bool IsValidJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StripFences(string input)
    {
        var s = input.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var firstNewline = s.IndexOf('\n');
        var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline >= 0 && lastFence > firstNewline)
            return s[(firstNewline + 1)..lastFence].Trim();
        return s;
    }

    private static async Task<ReadingExtractionAiResult> StubAsync(
        string reason,
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        var stub = await new StubReadingExtractionAi().ExtractAsync(paperId, mediaAssetId, ct);
        return stub with { StubReason = reason };
    }
}

/// <summary>
/// Default AI implementation. Today this is a deterministic stub: it
/// returns a canonical 20+6+16 placeholder manifest so the admin UI works
/// end-to-end without needing AI configured. Swap with a real
/// <c>IAiGatewayService</c>-backed implementation when the PDF parsing
/// pipeline lands (Reading kind/task in the gateway).
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
