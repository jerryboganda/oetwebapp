using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Extraction Service — Phase 6
//
// AI-assisted PDF → ReadingStructureManifest pipeline:
//   1. Admin uploads a PDF (via the existing MediaAsset slice).
//   2. CreateDraftAsync(paperId, mediaAssetId) calls IReadingExtractionAi
//      which is the swappable AI seam. Provider failures are persisted as
//      failed drafts without generated manifests.
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

        // Auto-approve only when policy explicitly opts out of human review.
        if (!policy.AiExtractionRequireHumanApproval && !draft.IsStub)
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
        if (draft.IsStub)
            throw new InvalidOperationException("Stub Reading extraction drafts cannot be approved. Re-run extraction after a real provider is configured.");
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
}

/// <summary>
/// Development placeholder implementation. It fails closed so no generated
/// placeholder Reading manifest can be approved into authored content.
/// </summary>
public sealed class StubReadingExtractionAi : IReadingExtractionAi
{
    public Task<ReadingExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        throw new InvalidOperationException("Reading AI extraction provider is not configured.");
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
